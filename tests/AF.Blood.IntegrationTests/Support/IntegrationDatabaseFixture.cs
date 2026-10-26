using Npgsql;
using Testcontainers.PostgreSql;

namespace AF.Blood.IntegrationTests.Support;

/// <summary>
/// One PostgreSQL 17 + PostGIS container for the whole test run. Migrations from
/// database/sql are applied once to a template database; every test then gets a
/// private database cloned from that template (ADR-0008).
/// </summary>
public sealed class IntegrationDatabaseFixture
{
    private const string TemplateDb = "aff_migrated_template";
    private const string Password = "aff_it_password";
    private readonly PostgreSqlContainer _container;
    private int _counter;

    public IntegrationDatabaseFixture()
    {
        _container = new PostgreSqlBuilder()
            .WithImage("postgis/postgis:17-3.5")
            .WithDatabase("aff_root")
            .WithUsername("aff")
            .WithPassword(Password)
            .Build();
    }

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await ExecOn("postgres", $"DROP DATABASE IF EXISTS {TemplateDb} WITH (FORCE)");
        await ExecOn("postgres", $"CREATE DATABASE {TemplateDb}");
        // The postgis image installs the extension only into its init database;
        // test-created databases must enable it explicitly (needs superuser,
        // which the container's POSTGRES_USER is).
        await ExecOn(TemplateDb, "CREATE EXTENSION IF NOT EXISTS postgis");
        await SqlMigrator.MigrateAsync(ConnectionStringFor(TemplateDb));
        await ExecOn("postgres", $"ALTER DATABASE {TemplateDb} WITH IS_TEMPLATE TRUE");
    }

    public async Task<string> CreateDatabaseAsync()
    {
        var name = $"aff_t{Interlocked.Increment(ref _counter)}_{DateTime.UtcNow:HHmmss}";
        // Cloning fails while any backend is attached to the source; clear
        // stragglers (e.g. pooled connections from earlier steps).
        await ExecOn("postgres",
            $"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '{TemplateDb}' AND pid <> pg_backend_pid()");
        await ExecOn("postgres", $"CREATE DATABASE {name} TEMPLATE {TemplateDb}");
        return ConnectionStringFor(name);
    }

    public async Task DropDatabaseAsync(string connectionString)
    {
        var parsed = new NpgsqlConnectionStringBuilder(connectionString);
        await ExecOn("postgres", $"DROP DATABASE IF EXISTS \"{parsed.Database}\" WITH (FORCE)");
    }

    public string ConnectionStringFor(string database)
    {
        var parsed = new NpgsqlConnectionStringBuilder(_container.GetConnectionString());
        parsed.Database = database;
        return parsed.ConnectionString;
    }

    private async Task ExecOn(string database, string sql)
    {
        await using var conn = new NpgsqlConnection(ConnectionStringFor(database));
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }
}

/// <summary>Lazily starts the shared container for the whole test process.</summary>
public static class SharedPostgres
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static IntegrationDatabaseFixture? _fixture;

    public static async Task<IntegrationDatabaseFixture> GetAsync()
    {
        if (_fixture is not null)
        {
            return _fixture;
        }

        await Gate.WaitAsync();
        try
        {
            if (_fixture is null)
            {
                var fixture = new IntegrationDatabaseFixture();
                await fixture.InitializeAsync();
                _fixture = fixture;
            }
        }
        finally
        {
            Gate.Release();
        }

        return _fixture;
    }
}

/// <summary>Applies the exact Flyway SQL files, in version order, through Npgsql
/// (whole file per command so dollar-quoted functions survive). CI additionally
/// runs the real Flyway binary against a scratch database.</summary>
public static class SqlMigrator
{
    public static async Task MigrateAsync(string connectionString)
    {
        var directory = FindDatabaseSqlDirectory();
        var files = Directory.GetFiles(directory, "V*.sql")
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

        // Pooling disabled so physical connections to the template database are
        // released immediately after migration (required for template cloning).
        var builder = new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false };

        await using var conn = new NpgsqlConnection(builder.ConnectionString);
        await conn.OpenAsync();

        foreach (var file in files)
        {
            var sql = await File.ReadAllTextAsync(file);
            await using var tx = await conn.BeginTransactionAsync();
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = (NpgsqlTransaction)tx;
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync();
            await tx.CommitAsync();
        }
    }

    private static string FindDatabaseSqlDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "database", "sql")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            throw new InvalidOperationException("Could not locate database/sql relative to the test output.");
        }

        return Path.Combine(dir.FullName, "database", "sql");
    }
}
