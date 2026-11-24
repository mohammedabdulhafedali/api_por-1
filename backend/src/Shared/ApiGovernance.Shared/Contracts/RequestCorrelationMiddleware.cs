using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;

namespace ApiGovernance.Shared.Contracts;

/// <summary>
/// Assigns and propagates a safe request correlation / trace ID (`X-Correlation-Id`) and standard
/// API version header (`X-API-Version: 1.0`), while removing internal server fingerprinting headers.
/// </summary>
public sealed partial class RequestCorrelationMiddleware(RequestDelegate next)
{
    private const string CorrelationIdItemKey = "ApiGovernance.CorrelationId";

    [GeneratedRegex(@"^[a-zA-Z0-9\-_\.]{1,64}$")]
    private static partial Regex SafeCorrelationIdPattern();

    public async Task InvokeAsync(HttpContext context)
    {
        var incomingCorrelationId = context.Request.Headers[ApiContractDefaults.CorrelationIdHeaderName].FirstOrDefault();
        var correlationId = !string.IsNullOrWhiteSpace(incomingCorrelationId) && SafeCorrelationIdPattern().IsMatch(incomingCorrelationId)
            ? incomingCorrelationId
            : (Activity.Current?.Id ?? context.TraceIdentifier);

        context.TraceIdentifier = correlationId;
        context.Items[CorrelationIdItemKey] = correlationId;

        context.Response.OnStarting(() =>
        {
            context.Response.Headers[ApiContractDefaults.CorrelationIdHeaderName] = correlationId;
            context.Response.Headers[ApiContractDefaults.ApiVersionHeaderName] = ApiContractDefaults.CurrentVersion;
            context.Response.Headers.Remove("Server");
            context.Response.Headers.Remove("X-Powered-By");
            return Task.CompletedTask;
        });

        await next(context);
    }
}

