using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace StargateAPI.Business.Data
{
    /// <param name="Ran">False when the environment is not Development or the build is not a Debug build.</param>
    /// <param name="AppliedMigrations">Migration ids applied by this run; empty when up to date or not run.</param>
    /// <param name="Seeded">True only when this run inserted the seed data.</param>
    public sealed record DatabaseInitializationResult(
        bool Ran,
        IReadOnlyList<string> AppliedMigrations,
        bool Seeded);

    /// <summary>
    /// In Development of a Debug build only: applies pending migrations, then seeds sample data when no people exist.
    /// Any failure is logged as Critical and rethrown so the app never starts on a half-initialised database.
    /// </summary>
    public sealed class DevelopmentDatabaseInitializer(
        StargateContext context,
        IHostEnvironment environment,
        ILogger<DevelopmentDatabaseInitializer> logger,
        bool isDebugBuild)
    {
        /// <summary>
        /// True only when the API assembly was compiled in Debug. A second, build-level gate so a Release/deployed
        /// build started with ASPNETCORE_ENVIRONMENT=Development by mistake never migrates or seeds a real database.
        /// A property (not a const) so the value is read from the API assembly, not inlined into callers.
        /// </summary>
        public static bool IsDebugBuild { get; } =
#if DEBUG
            true;
#else
            false;
#endif

        /// <summary>Resolves the initializer in its own scope and runs it. Called once from Program.cs before the app serves requests.</summary>
        public static async Task RunAsync(IServiceProvider services, CancellationToken cancellationToken = default)
        {
            using var scope = services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<DevelopmentDatabaseInitializer>()
                .InitializeAsync(cancellationToken);
        }

        public async Task<DatabaseInitializationResult> InitializeAsync(CancellationToken cancellationToken = default)
        {
            if (!isDebugBuild)
            {
                logger.LogInformation("This is not a Debug build; automatic migration and seeding skipped");
                return new DatabaseInitializationResult(false, [], false);
            }

            if (!environment.IsDevelopment())
            {
                logger.LogInformation(
                    "Environment {Environment} is not Development; automatic migration and seeding skipped",
                    environment.EnvironmentName);
                return new DatabaseInitializationResult(false, [], false);
            }

            // Only the file path is logged, never the full connection string.
            var dataSource = new SqliteConnectionStringBuilder(context.Database.GetConnectionString()).DataSource;

            var applied = await MigrateAsync(dataSource, cancellationToken);
            var seeded = await SeedAsync(dataSource, cancellationToken);
            return new DatabaseInitializationResult(true, applied, seeded);
        }

        private async Task<IReadOnlyList<string>> MigrateAsync(string dataSource, CancellationToken cancellationToken)
        {
            try
            {
                var pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
                await context.Database.MigrateAsync(cancellationToken);
                if (pending.Count == 0)
                {
                    logger.LogInformation("Database {DataSource} is up to date; no migrations applied", dataSource);
                }
                else
                {
                    logger.LogInformation(
                        "Applied {Count} migration(s) to {DataSource}: {Migrations}",
                        pending.Count, dataSource, string.Join(", ", pending));
                }
                return pending;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw Fail(ex, "migration", dataSource);
            }
        }

        private async Task<bool> SeedAsync(string dataSource, CancellationToken cancellationToken)
        {
            try
            {
                // The emptiness check and the inserts share one transaction. A second instance seeding the same
                // file waits for the SQLite busy timeout, then sees the committed people and skips (or fails
                // with a clear error if the lock is still held), so it cannot insert duplicates.
                await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

                var existing = await context.People.CountAsync(cancellationToken);
                if (existing > 0)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    logger.LogInformation("Seeding skipped: database already holds {PeopleCount} people", existing);
                    return false;
                }

                var people = DevelopmentSeedData.People();
                context.People.AddRange(people);
                await context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                logger.LogInformation(
                    "Seeded {PeopleCount} people, {DetailCount} astronaut details and {DutyCount} duties",
                    people.Count,
                    people.Count(p => p.AstronautDetail is not null),
                    people.Sum(p => p.AstronautDuties.Count));
                return true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw Fail(ex, "seeding", dataSource);
            }
        }

        private InvalidOperationException Fail(Exception inner, string step, string dataSource)
        {
            logger.LogCritical(
                inner,
                "Development database initialization failed during {Step} for {DataSource}",
                step, dataSource);
            return new InvalidOperationException(
                $"Development database initialization failed during {step} for '{dataSource}'. " +
                "No seed data was written. See exercise1/api/README.md (Troubleshooting).",
                inner);
        }
    }

    public static class DevelopmentDatabaseInitializerExtensions
    {
        public static IServiceCollection AddDevelopmentDatabaseInitializer(
            this IServiceCollection services, bool? isDebugBuild = null) =>
            services.AddScoped(sp => new DevelopmentDatabaseInitializer(
                sp.GetRequiredService<StargateContext>(),
                sp.GetRequiredService<IHostEnvironment>(),
                sp.GetRequiredService<ILogger<DevelopmentDatabaseInitializer>>(),
                isDebugBuild ?? DevelopmentDatabaseInitializer.IsDebugBuild));
    }
}
