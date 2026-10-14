using Microsoft.Data.Sqlite;
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

        Assert.Equal(
            StarbaseDatabase.ResolveConnectionString(StarbaseDatabase.DefaultConnectionString, _dir),
            result);
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

    [Fact]
    public void Project_directory_is_the_real_api_folder()
    {
        // Debug builds embed the folder; the tests run in Debug.
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

    [Fact]
    public void Test_databases_live_under_the_temp_folder_not_in_the_api_project()
    {
        using var db = new TestDatabase();

        Assert.StartsWith(Path.GetTempPath(), db.Path);
    }
}
