namespace StargateAPI.Business.Dtos
{
    /// <summary>One duty in GET /AstronautDuty. A dedicated type (not the entity) so no navigation property is serialised.</summary>
    public class AstronautDutyItem
    {
        public int Id { get; set; }

        public int PersonId { get; set; }

        public string Rank { get; set; } = string.Empty;

        public string DutyTitle { get; set; } = string.Empty;

        public DateTime DutyStartDate { get; set; }

        public DateTime? DutyEndDate { get; set; }
    }
}
