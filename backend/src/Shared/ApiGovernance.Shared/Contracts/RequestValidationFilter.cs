using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace ApiGovernance.Shared.Contracts;

/// <summary>
/// Endpoint filter that validates request DTOs annotated with DataAnnotations / IValidatableObject
/// and throws a standardized ApiContractException (HTTP 400 Problem Details) when validation fails.
/// </summary>
public sealed class RequestValidationFilter<TRequest> : IEndpointFilter where TRequest : class
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var request = context.Arguments.OfType<TRequest>().FirstOrDefault();
        if (request is null)
        {
            throw ApiContractException.BadRequest(
                messageAr: "جسم الطلب مطلوب ولا يمكن أن يكون فارغًا.",
                messageEn: "The request body is required and cannot be empty.");
        }

        var validationResults = new List<ValidationResult>();
        var validationContext = new ValidationContext(request, context.HttpContext.RequestServices, items: null);

        if (!Validator.TryValidateObject(request, validationContext, validationResults, validateAllProperties: true))
        {
            var errors = validationResults
                .SelectMany(result =>
                {
                    var members = result.MemberNames.Any() ? result.MemberNames : ["request"];
                    return members.Select(member => (
                        Field: ToCamelCase(member),
                        Error: result.ErrorMessage ?? "Invalid value."));
                })
                .GroupBy(x => x.Field, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(x => x.Error).Distinct().ToArray(),
                    StringComparer.OrdinalIgnoreCase);

            throw ApiContractException.ValidationFailed(errors);
        }

        return await next(context);
    }

    private static string ToCamelCase(string name)
    {
        if (string.IsNullOrEmpty(name) || char.IsLower(name[0]))
        {
            return name;
        }

        return char.ToLowerInvariant(name[0]) + name[1..];
    }
}

public static class EndpointValidationExtensions
{
    public static RouteHandlerBuilder WithRequestValidation<TRequest>(this RouteHandlerBuilder builder)
        where TRequest : class
    {
        return builder.AddEndpointFilter<RequestValidationFilter<TRequest>>();
    }
}

