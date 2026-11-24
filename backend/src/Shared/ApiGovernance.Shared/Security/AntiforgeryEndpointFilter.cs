using ApiGovernance.Shared.Contracts;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace ApiGovernance.Shared.Security;

/// <summary>
/// Central CSRF token verification filter via IAntiforgery across all Modular Monolith endpoints,
/// returning RFC 9457 Problem Details (HTTP 403) without redirecting or leaking internal state.
/// </summary>
public sealed class AntiforgeryValidationFilter(IAntiforgery antiforgery) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(context.HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            throw ApiContractException.Forbidden(
                messageAr: "رمز الحماية الأمني (CSRF) مفقود أو غير صالح.",
                messageEn: "Missing or invalid anti-forgery (CSRF) token.");
        }

        return await next(context);
    }
}

public static class AntiforgeryEndpointExtensions
{
    public static RouteHandlerBuilder WithCsrfValidation(this RouteHandlerBuilder builder)
        => builder.AddEndpointFilter<AntiforgeryValidationFilter>();
}

