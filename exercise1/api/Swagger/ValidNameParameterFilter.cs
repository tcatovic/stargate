using Microsoft.OpenApi;
using StargateAPI.Business.Validation;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace StargateAPI.Swagger
{
    /// <summary>
    /// Swashbuckle derives maxLength and pattern from [StringLength] and [RegularExpression] but cannot read
    /// <see cref="ValidNameAttribute"/>. This keeps the documented limits on every parameter that carries the shared name rule.
    /// </summary>
    public sealed class ValidNameParameterFilter : IParameterFilter
    {
        public void Apply(IOpenApiParameter parameter, ParameterFilterContext context)
        {
            var hasRule = context.PropertyInfo is { } property
                ? Attribute.IsDefined(property, typeof(ValidNameAttribute))
                : context.ParameterInfo is { } info && Attribute.IsDefined(info, typeof(ValidNameAttribute));

            if (hasRule && parameter.Schema is OpenApiSchema schema)
            {
                schema.MaxLength = InputRules.MaxLength;
                schema.Pattern = InputRules.AllowedCharactersPattern;
            }
        }
    }
}
