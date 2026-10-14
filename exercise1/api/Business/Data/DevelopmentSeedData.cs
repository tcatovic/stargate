namespace StargateAPI.Business.Data
{
    /// <summary>
    /// Fictional sample people for the Development environment only.
    /// Dates are fixed on purpose (never DateTime.Now) so the data is deterministic, and they must keep
    /// satisfying the domain rules in exercise1/README.md: unique names, no records without an assignment,
    /// exactly one current duty with no end date, earlier duties ending the day before the next starts,
    /// and CareerEndDate = RETIRED start minus one day.
    /// </summary>
    public static class DevelopmentSeedData
    {
        /// <summary>A fresh entity graph on every call. Ids are left unset so the database assigns them.</summary>
        public static IReadOnlyList<Person> People() =>
        [
            new Person { Name = "Mira Okafor" },
            new Person { Name = "Kenji Watanabe" },
            Astronaut(
                "Tomas Lindqvist",
                careerEnd: null,
                Duty("CPT", "Pilot", new DateTime(2021, 6, 14), null)),
            Astronaut(
                "Priya Raman",
                careerEnd: null,
                Duty("MAJ", "Mission Specialist", new DateTime(2019, 9, 2), null)),
            Astronaut(
                "Elena Vasquez",
                careerEnd: null,
                Duty("2LT", "Flight Engineer", new DateTime(2012, 1, 9), new DateTime(2016, 2, 29)),
                Duty("CPT", "Pilot", new DateTime(2016, 3, 1), new DateTime(2018, 12, 31)),
                Duty("MAJ", "Commander", new DateTime(2019, 1, 1), null)),
            Astronaut(
                "Desmond Achebe",
                careerEnd: new DateTime(2023, 3, 31),
                Duty("MAJ", "Pilot", new DateTime(2008, 2, 4), new DateTime(2014, 7, 14)),
                Duty("LTC", "Commander", new DateTime(2014, 7, 15), new DateTime(2023, 3, 31)),
                Duty("LTC", "RETIRED", new DateTime(2023, 4, 1), null)),
        ];

        private static AstronautDuty Duty(string rank, string title, DateTime start, DateTime? end) =>
            new() { Rank = rank, DutyTitle = title, DutyStartDate = start, DutyEndDate = end };

        // The detail mirrors the latest duty (current rank/title) and the first duty's start date.
        private static Person Astronaut(string name, DateTime? careerEnd, params AstronautDuty[] duties)
        {
            var current = duties[^1];
            var person = new Person
            {
                Name = name,
                AstronautDetail = new AstronautDetail
                {
                    CurrentRank = current.Rank,
                    CurrentDutyTitle = current.DutyTitle,
                    CareerStartDate = duties[0].DutyStartDate,
                    CareerEndDate = careerEnd,
                },
            };
            foreach (var duty in duties)
            {
                person.AstronautDuties.Add(duty);
            }
            return person;
        }
    }
}
