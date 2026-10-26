using System.Security.Claims;
using Dapper;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace AF.Blood.Api.Health;

public sealed class DatabaseHealthCheck(NpgsqlDataSource dataSource) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var conn = await dataSource.OpenConnectionAsync(cancellationToken);
            var version = await conn.ExecuteScalarAsync<string>(
                new CommandDefinition("select postgis_version()", cancellationToken: cancellationToken));
            return HealthCheckResult.Healthy($"PostGIS {version}");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Database or PostGIS extension unavailable.", ex);
        }
    }
}

public static class HealthCheckExtensions
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = registration => registration.Name == "database",
        });
        return app;
    }
}
