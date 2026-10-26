using AF.Blood.Application.Abstractions;
using AF.Blood.Application.Features.Audit;
using AF.Blood.Application.Features.Devices;
using AF.Blood.Application.Features.Donations;
using AF.Blood.Application.Features.Donors;
using AF.Blood.Application.Features.Matching;
using AF.Blood.Application.Features.Moderation;
using AF.Blood.Application.Features.Notifications;
using AF.Blood.Application.Features.Requests;
using AF.Blood.Infrastructure.Auth;
using AF.Blood.Infrastructure.Identity;
using AF.Blood.Infrastructure.Push;
using AF.Blood.Infrastructure.Verification;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using AppOptions = AF.Blood.Application.Options;

namespace AF.Blood.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Registers the shared Npgsql data source used by all Dapper services.</summary>
    public static IServiceCollection AddDatabase(this IServiceCollection services, string connectionString)
    {
        services.AddSingleton<NpgsqlDataSource>(_ =>
        {
            var builder = new NpgsqlDataSourceBuilder(connectionString);
            return builder.Build();
        });
        return services;
    }

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AppDbContext>((sp, options) =>
        {
            var dataSource = sp.GetRequiredService<NpgsqlDataSource>();
            options.UseNpgsql(dataSource);
        });

        services.AddIdentityCore<AppUser>(identity =>
        {
            identity.User.RequireUniqueEmail = true;
            identity.Password.RequiredLength = 8;
            identity.Password.RequireDigit = true;
            identity.Password.RequireLowercase = true;
            identity.Password.RequireUppercase = true;
            identity.Password.RequireNonAlphanumeric = false;
            identity.Lockout.MaxFailedAccessAttempts = 5;
            identity.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            identity.Lockout.AllowedForNewUsers = true;
        })
        .AddRoles<AppRole>()
        .AddEntityFrameworkStores<AppDbContext>();

        services.AddScoped<JwtTokenService>();
        services.AddScoped<RefreshTokenService>();
        services.AddScoped<AuthService>();

        // Push delivery: real OneSignal when configured, no-op otherwise.
        var oneSignal = configuration.GetSection(OneSignalOptions.SectionName).Get<OneSignalOptions>() ?? new();
        if (oneSignal.IsConfigured)
        {
            services.AddHttpClient<IPushDispatcher, OneSignalPushDispatcher>(client =>
            {
                client.BaseAddress = new Uri("https://api.onesignal.com/");
                client.DefaultRequestHeaders.Add("Authorization", $"Basic {oneSignal.ApiKey}");
                client.Timeout = TimeSpan.FromSeconds(15);
            });
        }
        else
        {
            services.AddSingleton<IPushDispatcher, NoOpPushDispatcher>();
        }

        services.AddSingleton<IVerificationCodeSender, LoggingVerificationCodeSender>();

        return services;
    }

    /// <summary>Registers application feature services and cross-cutting pieces.</summary>
    public static IServiceCollection AddApplicationServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AppOptions.MatchingOptions>(configuration.GetSection(AppOptions.MatchingOptions.SectionName));
        services.Configure<AppOptions.RequestOptions>(configuration.GetSection(AppOptions.RequestOptions.SectionName));
        services.Configure<AppOptions.DonationOptions>(configuration.GetSection(AppOptions.DonationOptions.SectionName));
        services.Configure<AppOptions.NotificationOptions>(configuration.GetSection(AppOptions.NotificationOptions.SectionName));
        services.Configure<AppOptions.WorkerOptions>(configuration.GetSection(AppOptions.WorkerOptions.SectionName));

        services.AddSingleton<Application.Abstractions.IClock, Application.Abstractions.SystemClock>();
        services.AddSingleton<AuditWriter>();
        services.AddSingleton<OutboxEnqueuer>();

        services.AddScoped<DonorProfileService>();
        services.AddScoped<RequestService>();
        services.AddScoped<MatchingService>();
        services.AddScoped<DonationService>();
        services.AddScoped<PolicyService>();
        services.AddScoped<NotificationService>();
        services.AddScoped<DeviceService>();
        services.AddScoped<ModerationService>();
        services.AddScoped<ExpiryService>();

        return services;
    }
}
