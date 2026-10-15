using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
    public const string MakeCareerStartDateRequired = "20261014212120_MakeCareerStartDateRequired";

    public string Path { get; } = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), "stargate-tests", $"{Guid.NewGuid():N}.db");

    public TestDatabase()
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
    }

    public string ConnectionString => $"Data Source={Path}";

    public StargateContext CreateContext() =>
        new(new DbContextOptionsBuilder<StargateContext>().UseSqlite(ConnectionString).Options);

    public StargateContext CreateContext(params IInterceptor[] interceptors) =>
        new(new DbContextOptionsBuilder<StargateContext>().UseSqlite(ConnectionString).AddInterceptors(interceptors).Options);

    /// <summary>Every migration in the assembly, in order. Generalises "exactly two migrations" assumptions.</summary>
    public static IReadOnlyList<string> AllMigrations
    {
        get
        {
            using var context = new StargateContext(
                new DbContextOptionsBuilder<StargateContext>().UseSqlite("Data Source=:memory:").Options);
            return context.Database.GetMigrations().ToList();
        }
    }

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

    public DevelopmentDatabaseInitializer CreateInitializer(string environmentName, ILogger<DevelopmentDatabaseInitializer>? logger = null, bool isDebugBuild = true) =>
        new(CreateContext(), new FakeHostEnvironment(environmentName), logger ?? NullLogger<DevelopmentDatabaseInitializer>.Instance, isDebugBuild);

    /// <summary>Every row of the three data tables, ordered by Id, as one string. Equal dumps mean identical data.</summary>
    public string Dump() => Dump("Person", "AstronautDetail", "AstronautDuty");

    /// <summary>Like <see cref="Dump()"/> for the named tables only.</summary>
    public string Dump(params string[] tables)
    {
        using var connection = Open();
        var text = new System.Text.StringBuilder();
        foreach (var table in tables)
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT * FROM \"{table}\" ORDER BY \"Id\"";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                text.Append(table);
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    text.Append('|').Append(reader.IsDBNull(i) ? "<null>" : Convert.ToString(reader.GetValue(i), System.Globalization.CultureInfo.InvariantCulture));
                }
                text.Append('\n');
            }
        }
        return text.ToString();
    }

    /// <summary>
    /// Like <see cref="Dump()"/>, but without the Person row with Id = <paramref name="personId"/> and without that
    /// person's AstronautDetail and AstronautDuty rows. Equal before and after means "no other person was touched".
    /// </summary>
    public string DumpExcept(long personId)
    {
        using var connection = Open();
        var text = new System.Text.StringBuilder();
        foreach (var (table, column) in new[] { ("Person", "Id"), ("AstronautDetail", "PersonId"), ("AstronautDuty", "PersonId") })
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT * FROM \"{table}\" WHERE \"{column}\" <> $personId ORDER BY \"Id\"";
            command.Parameters.AddWithValue("$personId", personId);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                text.Append(table);
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    text.Append('|').Append(reader.IsDBNull(i) ? "<null>" : Convert.ToString(reader.GetValue(i), System.Globalization.CultureInfo.InvariantCulture));
                }
                text.Append('\n');
            }
        }
        return text.ToString();
    }

    /// <summary>A migrated database holding DevelopmentSeedData (Mira 1, Kenji 2, Tomas Lindqvist 3, Priya 4, Elena 5, Desmond 6).</summary>
    public static TestDatabase Seeded()
    {
        var db = new TestDatabase();
        db.Migrate();
        using var context = db.CreateContext();
        context.People.AddRange(DevelopmentSeedData.People());
        context.SaveChanges();
        return db;
    }

    /// <summary>The collation the unique index uses for Person.Name, or null when there is no IX_Person_Name.</summary>
    public string? PersonNameCollation() =>
        Scalar<string?>("SELECT coll FROM pragma_index_xinfo('IX_Person_Name') WHERE name = 'Name'");

    public bool HasUniquePersonNameIndex() =>
        Scalar<long>("SELECT COUNT(*) FROM pragma_index_list('Person') WHERE name = 'IX_Person_Name' AND \"unique\" = 1") == 1;

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
