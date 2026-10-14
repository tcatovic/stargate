using StargateAPI.Business.Data;

namespace StargateAPI.Tests;

/// <summary>The people fixture from docs/contracts/001/tests.md, written out explicitly (not taken from the app's seed data).</summary>
public static class PeopleFixture
{
    public static readonly string[] AllNames =
        ["Mira Okafor", "Kenji Watanabe", "Tomas Lindqvist", "Priya Raman", "Elena Vasquez", "Desmond Achebe"];

    public static TestDatabase Create(params Person[] extra)
    {
        var db = new TestDatabase();
        db.Migrate();
        using var context = db.CreateContext();
        context.People.AddRange(
            new Person { Name = "Mira Okafor" },
            new Person { Name = "Kenji Watanabe" },
            Assigned("Tomas Lindqvist", "CPT", "Pilot", new DateTime(2021, 6, 14), null),
            Assigned("Priya Raman", "MAJ", "Mission Specialist", new DateTime(2019, 9, 2), null),
            Assigned("Elena Vasquez", "MAJ", "Commander", new DateTime(2012, 1, 9), null),
            Assigned("Desmond Achebe", "LTC", "RETIRED", new DateTime(2008, 2, 4), new DateTime(2023, 3, 31)));
        // Extras are added after the fixture, in the order given, so their Ids are higher.
        foreach (var person in extra)
        {
            context.People.Add(person);
            context.SaveChanges();
        }
        context.SaveChanges();
        return db;
    }

    public static Person Assigned(string name, string rank, string title, DateTime start, DateTime? end) => new()
    {
        Name = name,
        AstronautDetail = new AstronautDetail
        {
            CurrentRank = rank,
            CurrentDutyTitle = title,
            CareerStartDate = start,
            CareerEndDate = end
        }
    };

    public static (long People, long Details) Counts(TestDatabase db) =>
        (db.Scalar<long>("SELECT COUNT(*) FROM Person"), db.Scalar<long>("SELECT COUNT(*) FROM AstronautDetail"));
}
