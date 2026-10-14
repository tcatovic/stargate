using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using StargateAPI.Business.Data;

namespace StargateAPI.Tests;

public sealed class StarbaseDatabaseTests : IDisposable
{
    private readonly string _dir = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), "stargate-tests", $"dir-{Guid.NewGuid():N}")).FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static string DataSource(string connectionString) =>
        new SqliteConnectionStringBuilder(connectionString).DataSource;

    [Fact]
    public void Relative_data_source_is_anchored_to_the_base_directory()
    {
        var result = StarbaseDatabase.ResolveConnectionString("Data Source=starbase.db", _dir);

        Assert.Equal(Path.Combine(_dir, "starbase.db"), DataSource(result));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_connection_string_falls_back_to_the_default(string? configured)
    {
        var result = StarbaseDatabase.ResolveConnectionString(configured, _dir);

        Assert.Equal(Path.Combine(_dir, "starbase.db"), DataSource(result));
    }

    [Fact]
    public void Absolute_data_source_is_unchanged()
    {
        var absolute = Path.Combine(Path.GetTempPath(), "somewhere", "x.db");

        var result = StarbaseDatabase.ResolveConnectionString($"Data Source={absolute}", _dir);

        Assert.Equal(absolute, DataSource(result));
    }

    [Theory]
    [InlineData("Data Source=:memory:")]
    [InlineData("Data Source=file.db;Mode=Memory")]
    public void In_memory_sources_are_unchanged(string configured)
    {
        var result = StarbaseDatabase.ResolveConnectionString(configured, _dir);

        Assert.Equal(new SqliteConnectionStringBuilder(configured).ConnectionString, result);
    }

    [Fact]
    public void Other_keywords_are_kept()
    {
        var result = StarbaseDatabase.ResolveConnectionString("Data Source=starbase.db;Cache=Shared", _dir);

        Assert.Equal(SqliteCacheMode.Shared, new SqliteConnectionStringBuilder(result).Cache);
    }

    [Fact]
    public void Missing_directory_throws_and_creates_no_file()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            StarbaseDatabase.ResolveConnectionString("Data Source=../../nope/x.db", _dir));

        var expectedDirectory = Path.GetDirectoryName(Path.GetFullPath(Path.Combine(_dir, "../../nope/x.db")))!;
        Assert.Contains(expectedDirectory, error.Message);
        Assert.False(File.Exists(Path.Combine(expectedDirectory, "x.db")));
    }

    [Fact]
    public void Relative_path_without_an_embedded_directory_fails_loudly()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            StarbaseDatabase.ResolveConnectionString("Data Source=starbase.db", null));

        Assert.Contains("absolute path", error.Message);
    }

    private static IConfiguration Config(string? value) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(value is null
                ? new Dictionary<string, string?>()
                : new Dictionary<string, string?> { [$"ConnectionStrings:{StarbaseDatabase.ConnectionStringName}"] = value })
            .Build();

    [Fact]
    public void FromConfiguration_absolute_value_passes_through()
    {
        var absolute = Path.Combine(_dir, "abs.db");

        Assert.Equal(absolute, DataSource(StarbaseDatabase.FromConfiguration(Config($"Data Source={absolute}"))));
    }

#if DEBUG
    // Debug builds embed the API project folder; Release builds do not (ProjectDirectory is null).
    [Fact]
    public void FromConfiguration_anchors_relative_and_missing_values_to_the_project_folder_whatever_the_cwd()
    {
        var expected = Path.Combine(Assert.IsType<string>(StarbaseDatabase.ProjectDirectory), "starbase.db");
        var original = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(_dir);
        try
        {
            Assert.Equal(expected, DataSource(StarbaseDatabase.FromConfiguration(Config("Data Source=starbase.db"))));
            Assert.Equal(expected, DataSource(StarbaseDatabase.FromConfiguration(Config(null))));
        }
        finally
        {
            Directory.SetCurrentDirectory(original);
        }
    }

    [Fact]
    public void Project_directory_is_the_real_api_folder()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "api", "StargateAPI.csproj")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        Assert.Equal(
            Path.GetFullPath(Path.Combine(directory.FullName, "api")),
            Path.GetFullPath(Assert.IsType<string>(StarbaseDatabase.ProjectDirectory)));
    }
#else
    [Fact]
    public void Release_builds_do_not_embed_the_project_folder_and_reject_relative_paths()
    {
        Assert.Null(StarbaseDatabase.ProjectDirectory);
        Assert.Throws<InvalidOperationException>(() =>
            StarbaseDatabase.FromConfiguration(Config("Data Source=starbase.db")));
    }
#endif

    [Theory]
    [InlineData("C:starbase.db")]
    [InlineData("\\starbase.db")]
    public void Drive_relative_paths_are_not_treated_as_absolute_on_windows(string dataSource)
    {
        // Path.IsPathFullyQualified is platform specific: these are only drive-relative on Windows.
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var result = StarbaseDatabase.ResolveConnectionString($"Data Source={dataSource}", _dir);

        Assert.StartsWith(_dir, DataSource(result));
    }

    [Fact]
    public void Test_databases_live_under_the_temp_folder_and_never_touch_the_api_starbase_db()
    {
        var apiDb = new FileInfo(Path.Combine(FindApiFolder(), "starbase.db"));
        var before = (apiDb.Exists, apiDb.Exists ? apiDb.Length : 0, apiDb.Exists ? apiDb.LastWriteTimeUtc : default);

        using (var db = new TestDatabase())
        {
            Assert.StartsWith(Path.GetTempPath(), db.Path);
            db.Migrate();
        }

        apiDb.Refresh();
        Assert.Equal(before, (apiDb.Exists, apiDb.Exists ? apiDb.Length : 0, apiDb.Exists ? apiDb.LastWriteTimeUtc : default));
    }

    private static string FindApiFolder()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "api", "StargateAPI.csproj")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(Assert.IsType<DirectoryInfo>(directory).FullName, "api");
    }
}
