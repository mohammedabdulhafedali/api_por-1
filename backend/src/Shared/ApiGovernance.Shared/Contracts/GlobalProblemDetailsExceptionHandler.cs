using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace ApiGovernance.Shared.Contracts;

/// <summary>
/// Centralized exception handler producing RFC 9457 / RFC 7807 Problem Details combined with
/// the approved API contract fields: { type, title, status, detail, instance, code, message, details, traceId }.
/// Never leaks internal exception messages, stack traces, or server internals on 500 errors.
/// </summary>
public sealed class GlobalProblemDetailsExceptionHandler(
    ILogger<GlobalProblemDetailsExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var isArabic = ApiErrorLocalizer.PrefersArabic(httpContext);
        var traceId = httpContext.TraceIdentifier;

        int statusCode;
        string errorCode;
        string title;
        string message;
        object? details = null;

        switch (exception)
        {
            case ApiContractException contractEx:
                statusCode = contractEx.StatusCode;
                errorCode = contractEx.ErrorCode;
                (title, _) = ApiErrorLocalizer.GetLocalizedDefaults(statusCode, isArabic, errorCode);
                message = isArabic ? contractEx.MessageAr : contractEx.MessageEn;
                details = contractEx.Details;
                break;

            case BadHttpRequestException:
                statusCode = StatusCodes.Status400BadRequest;
                errorCode = ApiErrorCodes.BadRequest;
                (title, message) = ApiErrorLocalizer.GetLocalizedDefaults(statusCode, isArabic, errorCode);
                break;

            default:
                statusCode = StatusCodes.Status500InternalServerError;
                errorCode = ApiErrorCodes.InternalError;
                (title, message) = ApiErrorLocalizer.GetLocalizedDefaults(statusCode, isArabic, errorCode);
                logger.LogError(
                    exception,
                    "Unhandled exception on {Method} {Path} with TraceId {TraceId}",
                    httpContext.Request.Method,
                    httpContext.Request.Path.Value,
                    traceId);
                break;
        }

        var problem = CreateProblemDetails(
            httpContext,
            statusCode,
            errorCode,
            title,
            message,
            details,
            traceId);

        httpContext.Response.StatusCode = statusCode;
        httpContext.Response.ContentType = "application/problem+json; charset=utf-8";

        await httpContext.Response.WriteAsJsonAsync(
            problem,
            options: null,
            contentType: "application/problem+json; charset=utf-8",
            cancellationToken: cancellationToken);

        return true;
    }

    public static ProblemDetails CreateProblemDetails(
        HttpContext httpContext,
        int statusCode,
        string errorCode,
        string title,
        string message,
        object? details,
        string traceId)
    {
        var problem = new ProblemDetails
        {
            Type = $"https://urn.api-governance.local/problems/{errorCode.ToLowerInvariant()}",
            Title = title,
            Status = statusCode,
            Detail = message,
            Instance = httpContext.Request.Path.Value
        };

        // Approved Backend v0.1 / Baseline v1.2 unified error contract extensions: { code, message, details, traceId }
        problem.Extensions[ApiContractDefaults.CodeExtensionKey] = errorCode;
        problem.Extensions[ApiContractDefaults.MessageExtensionKey] = message;
        problem.Extensions[ApiContractDefaults.DetailsExtensionKey] = details;
        problem.Extensions[ApiContractDefaults.TraceIdExtensionKey] = traceId;

        return problem;
    }
}

