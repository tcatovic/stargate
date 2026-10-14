using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StargateAPI.Business.Data;

namespace StargateAPI.Tests;

public class DevelopmentDatabaseInitializerTests
{
    private const string Development = "Development";

    private static long Count(TestDatabase db, string table) =>
        db.Scalar<long>($"SELECT COUNT(*) FROM \"{table}\"");

    private static IEnumerable<Exception> Chain(Exception ex)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            yield return e;
        }
    }

    [Fact]
    public async Task Missing_file_is_created_migrated_and_seeded()
    {
        using var db = new TestDatabase();
        Assert.False(File.Exists(db.Path));

        var result = await db.CreateInitializer(Development).InitializeAsync();

        Assert.True(result.Ran);
        Assert.True(result.Seeded);
        Assert.Equal(new[] { TestDatabase.InitialCreate, TestDatabase.LatestMigration }, result.AppliedMigrations);
        Assert.Equal(new[] { TestDatabase.InitialCreate, TestDatabase.LatestMigration }, db.AppliedMigrations());
        Assert.Equal(6, Count(db, "Person"));
        Assert.Equal(4, Count(db, "AstronautDetail"));
        Assert.Equal(8, Count(db, "AstronautDuty"));
    }

    [Fact]
    public async Task Zero_byte_file_is_migrated_and_seeded()
    {
        using var db = new TestDatabase();
        File.WriteAllBytes(db.Path, []);

        var result = await db.CreateInitializer(Development).InitializeAsync();

        Assert.True(result.Seeded);
        Assert.Equal(2, result.AppliedMigrations.Count);
        Assert.Equal(6, Count(db, "Person"));
    }

    [Fact]
    public async Task Out_of_date_empty_database_gets_only_the_missing_migration_and_is_seeded()
    {
        using var db = new TestDatabase();
        db.Migrate(TestDatabase.InitialCreate);
        Assert.False(db.CareerStartDateIsNotNull());

        var result = await db.CreateInitializer(Development).InitializeAsync();

        Assert.Equal(new[] { TestDatabase.LatestMigration }, result.AppliedMigrations);
        Assert.True(db.CareerStartDateIsNotNull());
        Assert.True(result.Seeded);
        Assert.Equal(6, Count(db, "Person"));
    }

    [Fact]
    public async Task Up_to_date_empty_database_applies_nothing_and_is_seeded()
    {
        using var db = new TestDatabase();
        db.Migrate();

        var result = await db.CreateInitializer(Development).InitializeAsync();

        Assert.Empty(result.AppliedMigrations);
        Assert.True(result.Seeded);
        Assert.Equal(6, Count(db, "Person"));
    }

    [Fact]
    public async Task Running_twice_changes_nothing_the_second_time()
    {
        using var db = new TestDatabase();
        await db.CreateInitializer(Development).InitializeAsync();
        var before = db.Dump();

        var second = await db.CreateInitializer(Development).InitializeAsync();

        Assert.True(second.Ran);
        Assert.Empty(second.AppliedMigrations);
        Assert.False(second.Seeded);
        Assert.Equal(before, db.Dump());
        Assert.Equal(6, Count(db, "Person"));
        Assert.Equal(4, Count(db, "AstronautDetail"));
        Assert.Equal(8, Count(db, "AstronautDuty"));
    }

    [Fact]
    public async Task Existing_people_are_never_touched_or_seeded_around()
    {
        using var db = new TestDatabase();
        db.Migrate();
        db.Execute("INSERT INTO \"Person\" (\"Id\", \"Name\") VALUES (1, 'Ada');");
        var before = db.Dump();

        var result = await db.CreateInitializer(Development).InitializeAsync();

        Assert.False(result.Seeded);
        Assert.Equal(before, db.Dump());
        Assert.Equal(1, Count(db, "Person"));
        Assert.Equal(0, Count(db, "AstronautDetail"));
        Assert.Equal(0, Count(db, "AstronautDuty"));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Test")]
    public async Task Other_environments_do_not_touch_a_missing_database(string environment)
    {
        using var db = new TestDatabase();

        var result = await db.CreateInitializer(environment).InitializeAsync();

        Assert.False(result.Ran);
        Assert.Empty(result.AppliedMigrations);
        Assert.False(result.Seeded);
        Assert.False(File.Exists(db.Path));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task Other_environments_do_not_migrate_or_seed_an_existing_database(string environment)
    {
        using var db = new TestDatabase();
        db.Migrate(TestDatabase.InitialCreate);

        await db.CreateInitializer(environment).InitializeAsync();

        Assert.Equal(new[] { TestDatabase.InitialCreate }, db.AppliedMigrations());
        Assert.Equal(0, Count(db, "Person"));
    }

    [Theory]
    [InlineData("development")]
    [InlineData("DEVELOPMENT")]
    public async Task Development_is_detected_case_insensitively(string environment)
    {
        using var db = new TestDatabase();

        var result = await db.CreateInitializer(environment).InitializeAsync();

        Assert.True(result.Ran);
        Assert.Equal(6, Count(db, "Person"));
    }

    [Fact]
    public async Task Seeding_failure_rolls_back_everything_and_a_retry_succeeds()
    {
        using var db = new TestDatabase();
        db.Migrate();
        db.Execute("CREATE TRIGGER fail_seed BEFORE INSERT ON AstronautDuty WHEN NEW.DutyTitle = 'RETIRED' " +
                   "BEGIN SELECT RAISE(ABORT, 'seed boom'); END;");
        var logger = new CapturingLogger<DevelopmentDatabaseInitializer>();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => db.CreateInitializer(Development, logger).InitializeAsync());

        Assert.Contains("seeding", ex.Message);
        Assert.Contains(db.Path, ex.Message);
        Assert.Contains(Chain(ex), e => e.Message.Contains("seed boom"));
        Assert.Equal(0, Count(db, "Person"));
        Assert.Equal(0, Count(db, "AstronautDetail"));
        Assert.Equal(0, Count(db, "AstronautDuty"));
        var critical = Assert.Single(logger.Entries, e => e.Level == LogLevel.Critical);
        Assert.Contains("seeding", critical.Message);

        db.Execute("DROP TRIGGER fail_seed;");
        var retry = await db.CreateInitializer(Development).InitializeAsync();

        Assert.True(retry.Seeded);
        Assert.Equal(6, Count(db, "Person"));
    }

    [Fact]
    public async Task Migration_failure_stops_startup_and_seeds_nothing()
    {
        using var db = new TestDatabase();
        db.Migrate(TestDatabase.InitialCreate);
        db.Execute("INSERT INTO \"Person\" (\"Id\", \"Name\") VALUES (1, 'Ada');");
        db.Execute("INSERT INTO \"AstronautDetail\" (\"PersonId\", \"CurrentRank\", \"CurrentDutyTitle\", \"CareerStartDate\") " +
                   "VALUES (1, 'CPT', 'Pilot', NULL);");
        var logger = new CapturingLogger<DevelopmentDatabaseInitializer>();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => db.CreateInitializer(Development, logger).InitializeAsync());

        Assert.Contains("migration", ex.Message);
        Assert.Contains(Chain(ex), e => e.Message.Contains("NULL CareerStartDate"));
        Assert.Equal(new[] { TestDatabase.InitialCreate }, db.AppliedMigrations());
        Assert.Equal(1, Count(db, "Person"));
        Assert.Equal(1, Count(db, "AstronautDetail"));
        Assert.Equal(0, Count(db, "AstronautDuty"));
        Assert.Single(logger.Entries, e => e.Level == LogLevel.Critical);
    }

    [Fact]
    public async Task Logs_state_clearly_what_happened_without_leaking_the_connection_string()
    {
        using var db = new TestDatabase();
        var first = new CapturingLogger<DevelopmentDatabaseInitializer>();
        var second = new CapturingLogger<DevelopmentDatabaseInitializer>();
        var other = new CapturingLogger<DevelopmentDatabaseInitializer>();

        await db.CreateInitializer(Development, first).InitializeAsync();
        await db.CreateInitializer(Development, second).InitializeAsync();
        await db.CreateInitializer("Production", other).InitializeAsync();

        Assert.Contains(first.Entries, e => e.Level == LogLevel.Information && e.Message.Contains("Applied 2 migration"));
        Assert.Contains(first.Entries, e => e.Level == LogLevel.Information && e.Message.Contains("Seeded 6 people, 4 astronaut details and 8 duties"));
        Assert.Contains(second.Entries, e => e.Message.Contains("no migrations applied"));
        Assert.Contains(second.Entries, e => e.Message.Contains("Seeding skipped"));
        Assert.Contains(other.Entries, e => e.Message.Contains("not Development") && e.Message.Contains("Production"));
        foreach (var entry in first.Entries.Concat(second.Entries).Concat(other.Entries))
        {
            Assert.DoesNotContain("Data Source=", entry.Message);
            Assert.NotEqual(LogLevel.Critical, entry.Level);
        }
    }

    [Fact]
    public async Task A_cancelled_token_throws_and_leaves_no_seed_rows()
    {
        using var db = new TestDatabase();
        db.Migrate();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => db.CreateInitializer(Development).InitializeAsync(cts.Token));

        Assert.Equal(0, Count(db, "Person"));
    }

    // Program.cs is a thin call into RunAsync plus AddDevelopmentDatabaseInitializer; these cover that wiring
    // without Mvc.Testing: the registration resolves from a real container and RunAsync does the work.
    [Fact]
    public async Task The_registered_initializer_runs_through_RunAsync_in_a_service_provider()
    {
        using var db = new TestDatabase();
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<Microsoft.Extensions.Hosting.IHostEnvironment>(new FakeHostEnvironment(Development))
            .AddDbContext<StargateContext>(o => o.UseSqlite(db.ConnectionString))
            .AddDevelopmentDatabaseInitializer();
        await using var provider = services.BuildServiceProvider();

        await DevelopmentDatabaseInitializer.RunAsync(provider);

        Assert.Equal(6, Count(db, "Person"));
    }

    [Fact]
    public async Task RunAsync_does_nothing_outside_Development()
    {
        using var db = new TestDatabase();
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<Microsoft.Extensions.Hosting.IHostEnvironment>(new FakeHostEnvironment("Production"))
            .AddDbContext<StargateContext>(o => o.UseSqlite(db.ConnectionString))
            .AddDevelopmentDatabaseInitializer();
        await using var provider = services.BuildServiceProvider();

        await DevelopmentDatabaseInitializer.RunAsync(provider);

        Assert.False(File.Exists(db.Path));
    }

    [Fact]
    public async Task RunAsync_propagates_failures_so_startup_aborts()
    {
        using var db = new TestDatabase();
        db.Migrate(TestDatabase.InitialCreate);
        db.Execute("INSERT INTO \"Person\" (\"Id\", \"Name\") VALUES (1, 'Ada');");
        db.Execute("INSERT INTO \"AstronautDetail\" (\"PersonId\", \"CurrentRank\", \"CurrentDutyTitle\", \"CareerStartDate\") VALUES (1, 'CPT', 'Pilot', NULL);");
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<Microsoft.Extensions.Hosting.IHostEnvironment>(new FakeHostEnvironment(Development))
            .AddDbContext<StargateContext>(o => o.UseSqlite(db.ConnectionString))
            .AddDevelopmentDatabaseInitializer();
        await using var provider = services.BuildServiceProvider();

        await Assert.ThrowsAsync<InvalidOperationException>(() => DevelopmentDatabaseInitializer.RunAsync(provider));
    }
}
