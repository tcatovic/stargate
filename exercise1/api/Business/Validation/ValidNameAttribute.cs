using System.ComponentModel.DataAnnotations;

namespace StargateAPI.Business.Validation
{
    /// <summary>
    /// The shared name rule for writes: valid when <see cref="InputRules.IsValidName"/> accepts the
    /// <see cref="InputRules.Normalize"/>d value. Null is valid (pair with [Required]). The message never includes the value.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter | AttributeTargets.Field)]
    public sealed class ValidNameAttribute : ValidationAttribute
    {
        public ValidNameAttribute() : base(() => InputRules.AllowedCharactersMessage)
        {
        }

        public override bool IsValid(object? value) =>
            value is null || (value is string text && InputRules.IsValidName(InputRules.Normalize(text)));
    }
}
