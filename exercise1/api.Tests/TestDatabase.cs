using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using StargateAPI.Business.Data;

namespace StargateAPI.Tests;

/// <summary>
/// A per-test SQLite file under the temp folder. A file (not in-memory) matches real behaviour:
/// the SQLite table rebuild toggles PRAGMA foreign_keys and EF opens and closes connections.
/// Never touches exercise1/api/starbase.db.
/// </summary>
public sealed class TestDatabase : IDisposable
{
    public const string InitialCreate = "20240122154939_InitialCreate";

    public string Path { get; } = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), "stargate-tests", $"{Guid.NewGuid():N}.db");

    public TestDatabase()
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
    }

    public string ConnectionString => $"Data Source={Path}";

    public StargateContext CreateContext() =>
        new(new DbContextOptionsBuilder<StargateContext>().UseSqlite(ConnectionString).Options);

    public static string LatestMigration
    {
        get
        {
            using var context = new StargateContext(
                new DbContextOptionsBuilder<StargateContext>().UseSqlite("Data Source=:memory:").Options);
            return context.Database.GetMigrations().Last();
        }
    }

    /// <summary>Migrates to <paramref name="targetMigration"/>, or to the latest when null.</summary>
    public void Migrate(string? targetMigration = null)
    {
        using var context = CreateContext();
        context.GetService<IMigrator>().Migrate(targetMigration);
    }

    public T Scalar<T>(string sql)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = command.ExecuteScalar();
        return value is null or DBNull ? default! : (T)Convert.ChangeType(value, typeof(T));
    }

    public void Execute(string sql)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public List<string> Strings(string sql)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var result = new List<string>();
        while (reader.Read())
        {
            result.Add(reader.GetString(0));
        }
        return result;
    }

    public List<string> AppliedMigrations() =>
        Strings("SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" ORDER BY \"MigrationId\"");

    public bool CareerStartDateIsNotNull() =>
        Scalar<long>("SELECT \"notnull\" FROM pragma_table_info('AstronautDetail') WHERE name = 'CareerStartDate'") == 1;

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        return connection;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-shm", "-wal", "-journal" })
        {
            File.Delete(Path + suffix);
        }
    }
}
