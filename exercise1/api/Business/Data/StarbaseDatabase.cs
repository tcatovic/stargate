using System.Reflection;
using Microsoft.Data.Sqlite;

namespace StargateAPI.Business.Data
{
    /// <summary>
    /// Resolves the SQLite connection string so that every entry point (dotnet ef, dotnet run,
    /// the built dll started from any folder) uses the same database file.
    /// SQLite-specific and transitional: delete this class when the project moves to SQL Server.
    /// </summary>
    public static class StarbaseDatabase
    {
        public const string ConnectionStringName = "StarbaseApiDatabase";
        public const string DefaultConnectionString = "Data Source=starbase.db";

        private const string ProjectDirectoryMetadataKey = "StargateApiProjectDirectory";

        /// <summary>
        /// The API project folder, embedded at build time. Only Debug builds embed it;
        /// in other configurations this is null and a relative Data Source is rejected.
        /// </summary>
        public static string? ProjectDirectory { get; } = typeof(StarbaseDatabase).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == ProjectDirectoryMetadataKey)?.Value;

        /// <summary>
        /// Anchors a relative SQLite Data Source to <paramref name="baseDirectory"/>.
        /// Absolute and in-memory sources are returned unchanged.
        /// </summary>
        public static string ResolveConnectionString(string? configured, string? baseDirectory)
        {
            var builder = new SqliteConnectionStringBuilder(
                string.IsNullOrWhiteSpace(configured) ? DefaultConnectionString : configured);

            var dataSource = builder.DataSource;
            if (string.IsNullOrWhiteSpace(dataSource)
                || dataSource == ":memory:"
                || builder.Mode == SqliteOpenMode.Memory
                || Path.IsPathRooted(dataSource))
            {
                return builder.ToString();
            }

            if (string.IsNullOrWhiteSpace(baseDirectory))
            {
                throw new InvalidOperationException(
                    $"The database path '{dataSource}' is relative and this build does not embed the API project folder. "
                    + $"Set ConnectionStrings__{ConnectionStringName} to an absolute path.");
            }

            var fullPath = Path.GetFullPath(Path.Combine(baseDirectory, dataSource));
            var directory = Path.GetDirectoryName(fullPath);
            if (directory is null || !Directory.Exists(directory))
            {
                throw new InvalidOperationException(
                    $"Database directory '{directory}' does not exist. "
                    + $"Set ConnectionStrings__{ConnectionStringName} to an absolute path.");
            }

            builder.DataSource = fullPath;
            return builder.ToString();
        }
    }
}
