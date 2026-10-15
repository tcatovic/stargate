using System.Net;
using System.Text.Json;

namespace StargateAPI.Tests;

/// <summary>
/// The second-order SQL injection found in task 006 QA, replayed over the real HTTP stack against a seeded temp database.
/// A name that passes the name rule is just a name: it may only ever affect the person who has it.
/// </summary>
public sealed class AstronautDutySecurityHttpTests : IDisposable
{
    private readonly DutyApiHost _host = new();

    public void Dispose() => _host.Dispose();

    public static TheoryData<string> RuleValidHostileNames() => new()
    {
        "a' OR Id IS 3 OR 'a",
        "a' OR 1 OR 'a",
        "a' OR 'a' IS 'a",
        "Tomas Lindqvist'--",
        "Aoife O'Brien Jr.",
        "O'Neil-Smith Jr.",
    };

    [Theory]
    [MemberData(nameof(RuleValidHostileNames))]
    public async Task S1_AHostileButRuleValidName_GetsItsDutyOnItsOwnPersonOnly(string name)
    {
        Assert.Equal("Tomas Lindqvist", _host.Db.Scalar<string>("SELECT \"Name\" FROM \"Person\" WHERE \"Id\" = 3"));
        var created = await _host.PostPerson(name);
        Assert.Equal(HttpStatusCode.Created, created.Status);
        var target = created.Json.GetProperty("id").GetInt64();
        Assert.Equal(7, target);
        var before = _host.Db.DumpExcept(target);

        var reply = await _host.PostDuty(name, "CPT", "Injected Role", "2026-03-01");

        Assert.Equal(HttpStatusCode.Created, reply.Status);
        Assert.Equal("/AstronautDuty?name=" + Uri.EscapeDataString(name), reply.Location);
        Assert.Equal(
            ["CPT|Injected Role|2026-03-01 00:00:00|<null>"],
            _host.Db.Strings($"SELECT \"Rank\" || '|' || \"DutyTitle\" || '|' || \"DutyStartDate\" || '|' || COALESCE(\"DutyEndDate\", '<null>') FROM \"AstronautDuty\" WHERE \"PersonId\" = {target}"));
        Assert.Equal(
            ["CPT|Injected Role|2026-03-01 00:00:00|<null>"],
            _host.Db.Strings($"SELECT \"CurrentRank\" || '|' || \"CurrentDutyTitle\" || '|' || \"CareerStartDate\" || '|' || COALESCE(\"CareerEndDate\", '<null>') FROM \"AstronautDetail\" WHERE \"PersonId\" = {target}"));
        // Nobody else changed, byte for byte: in particular Tomas's Pilot duty is still open and his detail untouched.
        Assert.Equal(before, _host.Db.DumpExcept(target));
        Assert.Equal(
            ["CPT|Pilot|2021-06-14 00:00:00|<null>"],
            _host.Db.Strings("SELECT \"Rank\" || '|' || \"DutyTitle\" || '|' || \"DutyStartDate\" || '|' || COALESCE(\"DutyEndDate\", '<null>') FROM \"AstronautDuty\" WHERE \"PersonId\" = 3"));
    }

    [Fact]
    public async Task S7_APaddedAndDifferentlyCasedHostileName_ResolvesToTheSamePerson()
    {
        const string name = "a' OR Id IS 3 OR 'a";
        var target = (await _host.PostPerson(name)).Json.GetProperty("id").GetInt64();
        Assert.Equal(HttpStatusCode.Created, (await _host.PostDuty(name, "CPT", "Injected Role", "2026-03-01")).Status);
        var before = _host.Db.DumpExcept(target);

        var reply = await _host.PostDuty("  A' or id is 3 or 'A  ", "MAJ", "Second Role", "2026-04-01");

        Assert.Equal(HttpStatusCode.Created, reply.Status);
        Assert.Equal("/AstronautDuty?name=a%27%20OR%20Id%20IS%203%20OR%20%27a", reply.Location);
        Assert.Equal(
            ["CPT|Injected Role|2026-03-01 00:00:00|2026-03-31 00:00:00", "MAJ|Second Role|2026-04-01 00:00:00|<null>"],
            _host.Db.Strings($"SELECT \"Rank\" || '|' || \"DutyTitle\" || '|' || \"DutyStartDate\" || '|' || COALESCE(\"DutyEndDate\", '<null>') FROM \"AstronautDuty\" WHERE \"PersonId\" = {target} ORDER BY \"Id\""));
        Assert.Equal(before, _host.Db.DumpExcept(target));
    }

    [Fact]
    public async Task S5_ALegitimateApostropheName_WorksEndToEnd_WithoutA500()
    {
        const string name = "Aoife O'Brien Jr.";
        Assert.Equal(HttpStatusCode.Created, (await _host.PostPerson(name)).Status);

        var reply = await _host.PostDuty(name, "CPT", "Pilot", "2026-03-01");

        Assert.Equal(HttpStatusCode.Created, reply.Status);
        Assert.Equal("/AstronautDuty?name=Aoife%20O%27Brien%20Jr.", reply.Location);
        Assert.Equal(JsonValueKind.Number, reply.Json.GetProperty("id").ValueKind);
        Assert.Equal(1, _host.Db.Scalar<long>("SELECT COUNT(*) FROM \"AstronautDuty\" WHERE \"PersonId\" = 7 AND \"DutyTitle\" = 'Pilot'"));

        // The Location (and the escaped form of the same name) returns the new duty.
        foreach (var url in new[] { reply.Location!, "/AstronautDuty?name=Aoife%20O%27Brien%20Jr." })
        {
            var read = await _host.Get(url);
            Assert.Equal(HttpStatusCode.OK, read.Status);
            Assert.Equal("Aoife O'Brien Jr.", read.Json.GetProperty("person").GetProperty("name").GetString());
            Assert.Equal(reply.Json.GetProperty("id").GetInt32(), Assert.Single(read.Json.GetProperty("astronautDuties").EnumerateArray()).GetProperty("id").GetInt32());
        }
    }

    [Fact]
    public async Task S6_RankAndTitleAreData_QuotesAreStoredVerbatim_AndOthersAreUnchanged()
    {
        var others = _host.Db.DumpExcept(2);

        var reply = await _host.PostDuty("Kenji Watanabe", "O'Neil", "Pilot' OR '1", "2026-03-01");

        Assert.Equal(HttpStatusCode.Created, reply.Status);
        Assert.Equal("O'Neil", _host.Db.Scalar<string>("SELECT \"Rank\" FROM \"AstronautDuty\" WHERE \"PersonId\" = 2"));
        Assert.Equal("Pilot' OR '1", _host.Db.Scalar<string>("SELECT \"DutyTitle\" FROM \"AstronautDuty\" WHERE \"PersonId\" = 2"));
        Assert.Equal("O'Neil", _host.Db.Scalar<string>("SELECT \"CurrentRank\" FROM \"AstronautDetail\" WHERE \"PersonId\" = 2"));
        Assert.Equal("Pilot' OR '1", _host.Db.Scalar<string>("SELECT \"CurrentDutyTitle\" FROM \"AstronautDetail\" WHERE \"PersonId\" = 2"));
        Assert.Equal(others, _host.Db.DumpExcept(2));
    }

    [Theory]
    [InlineData("rank", "x' OR '1'='1")]
    [InlineData("rank", "a;b")]
    [InlineData("dutyTitle", "x' OR '1'='1")]
    [InlineData("dutyTitle", "a;b")]
    public async Task S6b_ARankOrTitleThatFailsTheRule_Is400_WithoutEcho_AndNothingIsWritten(string field, string value)
    {
        var before = _host.Db.Dump();
        var rank = field == "rank" ? value : "CPT";
        var title = field == "dutyTitle" ? value : "Pilot";

        var reply = await _host.PostDuty("Kenji Watanabe", rank, title, "2026-03-01");

        Assert.Equal(HttpStatusCode.BadRequest, reply.Status);
        ProblemAssert.HasNameErrors(reply.Json, field, ProblemAssert.NameRuleMessage);
        ProblemAssert.NothingDecodedContains(reply.Json, value);
        Assert.DoesNotContain(value, reply.Body);
        Assert.Equal(before, _host.Db.Dump());
    }
}
