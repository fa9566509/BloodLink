using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AF.Blood.Application.Abstractions;
using AF.Blood.Contracts;
using AF.Blood.Infrastructure.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace AF.Blood.IntegrationTests.Support;

/// <summary>Base class for integration tests: a fresh database per test plus a
/// WebApplicationFactory wired to it with test fakes for push and codes.</summary>
public abstract class IntegrationTestBase : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private IntegrationDatabaseFixture Fixture { get; set; } = null!;
    private WebApplicationFactory<Program>? _factory;

    protected string ConnectionString { get; private set; } = null!;
    protected FakePushDispatcher Push { get; } = new();
    protected FakeVerificationCodeSender Codes { get; } = new();
    protected WebApplicationFactory<Program> Factory =>
        _factory ?? throw new InvalidOperationException("Test not initialised.");

    public async Task InitializeAsync()
    {
        Fixture = await SharedPostgres.GetAsync();
        ConnectionString = await Fixture.CreateDatabaseAsync();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Default", ConnectionString);
            // Test-only signing/bootstrap configuration (environment-specific
            // appsettings files are not relied on in the test host).
            builder.UseSetting("Jwt:Key", "test-signing-key-0123456789abcdef0123456789abcdef");
            builder.UseSetting("Jwt:BootstrapKey", "dev-bootstrap-key");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IPushDispatcher>();
                services.AddSingleton<IPushDispatcher>(Push);
                services.RemoveAll<IVerificationCodeSender>();
                services.AddSingleton<IVerificationCodeSender>(Codes);
            });
        });
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
            _factory = null;
            await Fixture.DropDatabaseAsync(ConnectionString);
        }
    }

    // ---------------------------------------------------------------- helpers

    protected HttpClient Client() => Factory.CreateClient();

    protected async Task<HttpResponseMessage> PostAsync(HttpClient client, string uri, object body)
    {
        var response = await client.PostAsJsonAsync(uri, body, Json);
        return response;
    }

    protected async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    protected async Task<(HttpClient Client, AuthResponse Auth)> RegisterUserAsync(
        string? email = null, string password = "Sup3rSecret!x", string? displayName = null)
    {
        email ??= $"user-{Guid.NewGuid():N}@example.test";
        var client = Client();
        var response = await PostAsync(client, "/api/v1/auth/register",
            new RegisterRequest(email, password, displayName ?? "Test User", null));
        var auth = await ReadAsync<AuthResponse>(response);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return (client, auth);
    }

    /// <summary>Registers a user, creates a donor profile and enables discovery
    /// with an approximate location and availability.</summary>
    protected Task<HttpResponseMessage> PutAsync(HttpClient client, string uri, object body) =>
        client.PutAsJsonAsync(uri, body, Json);

    protected async Task<(HttpClient Client, AuthResponse Auth)> RegisterDonorAsync(
        double lat, double lon, string bloodGroup = "O-", bool available = true, int radiusKm = 25)
    {
        var (client, auth) = await RegisterUserAsync();

        var upsert = await PutAsync(client, "/api/v1/donors/me", new UpsertDonorProfileRequest(bloodGroup, radiusKm));
        upsert.EnsureSuccessStatusCode();

        var consent = await PutAsync(client, "/api/v1/donors/me/consent",
            new DonorConsentRequest(true, "2026-10-v1", lat, lon));
        consent.EnsureSuccessStatusCode();

        var availability = await PutAsync(client, "/api/v1/donors/me/availability",
            new DonorAvailabilityRequest(available));
        availability.EnsureSuccessStatusCode();

        return (client, auth);
    }

    /// <summary>Registers the first administrator via the bootstrap key
    /// (dev configuration) and re-authenticates so the role claim is in the
    /// access token (tokens minted before the bootstrap carry no roles).</summary>
    protected async Task<(HttpClient Client, AuthResponse Auth)> RegisterAdminAsync()
    {
        var (client, auth) = await RegisterUserAsync(displayName: "Admin");
        var bootstrap = await PostAsync(client, "/api/v1/admin/bootstrap", new AdminBootstrapRequest("dev-bootstrap-key"));
        bootstrap.EnsureSuccessStatusCode();

        var anon = Client();
        var login = await PostAsync(anon, "/api/v1/auth/login", new LoginRequest(auth.User.Email, "Sup3rSecret!x"));
        login.EnsureSuccessStatusCode();
        var refreshedAuth = await ReadAsync<AuthResponse>(login);

        var adminClient = Client();
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", refreshedAuth.AccessToken);
        return (adminClient, refreshedAuth);
    }

    protected Guid UserIdOf(AuthResponse auth) => auth.User.Id;

    /// <summary>Creates a whole-blood request owned by the given client.</summary>
    protected async Task<RequestDto> CreateRequestAsync(
        HttpClient requester, double lat = 31.52, double lon = 74.35,
        string bloodGroup = "O+", string urgency = "urgent", string label = "General Hospital Lahore")
    {
        var response = await PostAsync(requester, "/api/v1/requests",
            new CreateRequestRequest(bloodGroup, lat, lon, label, "General Hospital", null, urgency, null));
        return await ReadAsync<RequestDto>(response);
    }

    protected async Task AcceptAsync(HttpClient donor, Guid requestId)
    {
        var response = await PostAsync(donor, $"/api/v1/requests/{requestId}/responses",
            new RespondToRequestRequest(ResponseTypes.Accepted));
        response.EnsureSuccessStatusCode();
    }

    protected async Task RunMatchingAsync(int maxRuns = 10)
    {
        using var scope = Factory.Services.CreateScope();
        var matching = scope.ServiceProvider.GetRequiredService<Application.Features.Matching.MatchingService>();
        await matching.ProcessDueRunsAsync("test-worker", maxRuns, CancellationToken.None);
    }

    protected async Task DeliverOutboxAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var delivery = scope.ServiceProvider.GetRequiredService<Application.Features.Notifications.OutboxDeliveryService>();
        await delivery.DeliverBatchAsync(CancellationToken.None);
    }

    protected async Task<NpgsqlConnection> OpenDbAsync()
    {
        var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        return conn;
    }

    protected UserManager<AppUser> UserManager()
    {
        var scope = Factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
    }

    protected T Service<T>() where T : notnull
    {
        using var scope = Factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<T>();
    }
}
