using System.ComponentModel.DataAnnotations;

namespace StargateAPI.Business.Validation
{
    /// <summary>
    /// A duty start date whose date part lies within <see cref="InputRules.MinDutyDate"/> and
    /// <see cref="InputRules.MaxDutyDate"/>. Null is valid (pair with [Required]). The message never includes the value.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter | AttributeTargets.Field)]
    public sealed class ValidDutyDateAttribute : ValidationAttribute
    {
        public ValidDutyDateAttribute() : base(() => InputRules.DutyDateMessage)
        {
        }

        public override bool IsValid(object? value) =>
            value is null || (value is DateTime date && InputRules.IsValidDutyDate(date));
    }
}
