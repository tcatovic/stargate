using StargateAPI.Business.Data;

namespace StargateAPI.Tests;

/// <summary>
/// Asserts the domain rules on the seeded database with independent SQL, never by reading DevelopmentSeedData.
/// </summary>
public class SeedDataRulesTests : IDisposable
{
    private readonly TestDatabase _db = new();

    public SeedDataRulesTests()
    {
        _db.CreateInitializer("Development").InitializeAsync().GetAwaiter().GetResult();
    }

    public void Dispose() => _db.Dispose();

    private long Count(string sql) => _db.Scalar<long>(sql);

    [Fact]
    public void Rule1_names_are_unique_ignoring_case()
    {
        Assert.Empty(_db.Strings("SELECT lower(Name) FROM Person GROUP BY lower(Name) HAVING COUNT(*) > 1"));
        Assert.True(Count("SELECT COUNT(*) FROM Person") >= 2);
    }

    [Fact]
    public void Rule2_people_without_an_assignment_have_no_records_and_records_come_together()
    {
        Assert.True(Count("SELECT COUNT(*) FROM Person p WHERE NOT EXISTS (SELECT 1 FROM AstronautDetail d WHERE d.PersonId = p.Id) " +
                          "AND NOT EXISTS (SELECT 1 FROM AstronautDuty u WHERE u.PersonId = p.Id)") >= 1);
        Assert.Equal(0, Count("SELECT COUNT(*) FROM Person p WHERE " +
                              "EXISTS (SELECT 1 FROM AstronautDetail d WHERE d.PersonId = p.Id) <> EXISTS (SELECT 1 FROM AstronautDuty u WHERE u.PersonId = p.Id)"));
    }

    [Fact]
    public void Rules3_and_4_each_astronaut_has_exactly_one_current_duty_and_it_is_the_latest()
    {
        Assert.Equal(0, Count("SELECT COUNT(*) FROM (SELECT PersonId FROM AstronautDuty GROUP BY PersonId " +
                              "HAVING SUM(DutyEndDate IS NULL) <> 1)"));
        Assert.Equal(0, Count("SELECT COUNT(*) FROM AstronautDuty u WHERE DutyEndDate IS NULL " +
                              "AND DutyStartDate <> (SELECT MAX(DutyStartDate) FROM AstronautDuty x WHERE x.PersonId = u.PersonId)"));
        Assert.True(Count("SELECT COUNT(*) FROM (SELECT PersonId FROM AstronautDuty GROUP BY PersonId HAVING COUNT(*) = 1 AND SUM(DutyEndDate IS NULL) = 1)") >= 2);
    }

    [Fact]
    public void Rule5_each_earlier_duty_ends_the_day_before_the_next_starts()
    {
        Assert.True(Count("SELECT COUNT(*) FROM (SELECT PersonId FROM AstronautDuty GROUP BY PersonId HAVING COUNT(*) >= 3)") >= 1);
        Assert.Equal(0, Count("SELECT COUNT(*) FROM (SELECT DutyEndDate, LEAD(DutyStartDate) OVER (PARTITION BY PersonId ORDER BY DutyStartDate) AS NextStart " +
                              "FROM AstronautDuty) WHERE NextStart IS NOT NULL AND date(DutyEndDate) IS NOT date(NextStart, '-1 day')"));
    }

    [Fact]
    public void Rules6_and_7_retired_person_has_career_end_one_day_before_the_retired_start()
    {
        Assert.True(Count("SELECT COUNT(*) FROM AstronautDetail WHERE CurrentDutyTitle = 'RETIRED'") >= 1);
        Assert.Equal(0, Count("SELECT COUNT(*) FROM AstronautDetail d JOIN AstronautDuty u ON u.PersonId = d.PersonId AND u.DutyEndDate IS NULL " +
                              "WHERE d.CurrentDutyTitle = 'RETIRED' AND (u.DutyTitle <> 'RETIRED' OR date(d.CareerEndDate) IS NOT date(u.DutyStartDate, '-1 day'))"));
        Assert.Equal(0, Count("SELECT COUNT(*) FROM AstronautDetail WHERE CurrentDutyTitle <> 'RETIRED' AND CareerEndDate IS NOT NULL"));
        Assert.Equal(0, Count("SELECT COUNT(*) FROM AstronautDuty u JOIN AstronautDetail d ON d.PersonId = u.PersonId " +
                              "WHERE u.DutyTitle = 'RETIRED' AND d.CurrentDutyTitle <> 'RETIRED'"));
    }

    [Fact]
    public void Detail_agrees_with_the_current_duty_and_the_first_duty_start()
    {
        var astronauts = Count("SELECT COUNT(DISTINCT PersonId) FROM AstronautDuty");
        Assert.True(astronauts > 0);
        Assert.Equal(astronauts, Count("SELECT COUNT(*) FROM AstronautDetail"));
        Assert.Equal(0, Count("SELECT COUNT(*) FROM AstronautDetail d WHERE NOT EXISTS (" +
                              "SELECT 1 FROM AstronautDuty u WHERE u.PersonId = d.PersonId AND u.DutyEndDate IS NULL " +
                              "AND u.Rank = d.CurrentRank AND u.DutyTitle = d.CurrentDutyTitle)"));
        Assert.Equal(0, Count("SELECT COUNT(*) FROM AstronautDetail d WHERE d.CareerStartDate <> " +
                              "(SELECT MIN(DutyStartDate) FROM AstronautDuty u WHERE u.PersonId = d.PersonId)"));
    }

    [Fact]
    public void Stored_dates_are_date_only()
    {
        Assert.Equal(0, Count("SELECT COUNT(*) FROM AstronautDuty WHERE DutyStartDate NOT LIKE '% 00:00:00' " +
                              "OR (DutyEndDate IS NOT NULL AND DutyEndDate NOT LIKE '% 00:00:00')"));
        Assert.Equal(0, Count("SELECT COUNT(*) FROM AstronautDetail WHERE CareerStartDate NOT LIKE '% 00:00:00' " +
                              "OR (CareerEndDate IS NOT NULL AND CareerEndDate NOT LIKE '% 00:00:00')"));
    }

    [Fact]
    public void Dates_are_fixed_literals_not_relative_to_now()
    {
        Assert.Equal("2023-03-31 00:00:00", _db.Strings("SELECT CareerEndDate FROM AstronautDetail WHERE CurrentDutyTitle = 'RETIRED'").Single());
        Assert.Equal("2023-04-01 00:00:00", _db.Strings("SELECT DutyStartDate FROM AstronautDuty WHERE DutyTitle = 'RETIRED'").Single());
        Assert.Equal("2012-01-09 00:00:00", _db.Strings("SELECT CareerStartDate FROM AstronautDetail d JOIN Person p ON p.Id = d.PersonId WHERE p.Name = 'Elena Vasquez'").Single());
        // Every seeded date lies in a fixed window that ends well before any plausible run date.
        Assert.Equal(0, Count("SELECT COUNT(*) FROM (SELECT DutyStartDate AS d FROM AstronautDuty UNION ALL SELECT DutyEndDate FROM AstronautDuty " +
                              "UNION ALL SELECT CareerStartDate FROM AstronautDetail UNION ALL SELECT CareerEndDate FROM AstronautDetail) " +
                              "WHERE d IS NOT NULL AND (d < '2000-01-01' OR d > '2023-12-31')"));
        var now = DateTime.UtcNow.AddYears(-1).ToString("yyyy-MM-dd");
        Assert.Equal(0, Count($"SELECT COUNT(*) FROM AstronautDuty WHERE DutyStartDate > '{now}'"));
    }

    [Fact]
    public async Task Seeding_is_deterministic_across_databases()
    {
        using var other = new TestDatabase();
        await other.CreateInitializer("Development").InitializeAsync();

        Assert.Equal(_db.Dump(), other.Dump());
    }

    [Fact]
    public void People_returns_a_fresh_graph_each_call()
    {
        var first = DevelopmentSeedData.People();
        var second = DevelopmentSeedData.People();

        Assert.NotSame(first[0], second[0]);
        Assert.All(first, p => Assert.Equal(0, p.Id));
    }
}
