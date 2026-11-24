using Microsoft.AspNetCore.Http;

namespace ApiGovernance.Shared.Contracts;

/// <summary>
/// Controlled domain/API contract exception that maps deterministically to RFC 9457 Problem Details
/// without leaking internal server or infrastructure details.
/// </summary>
public sealed class ApiContractException : Exception
{
    public int StatusCode { get; }
    public string ErrorCode { get; }
    public string MessageAr { get; }
    public string MessageEn { get; }
    public object? Details { get; }

    public ApiContractException(
        int statusCode,
        string errorCode,
        string messageAr,
        string messageEn,
        object? details = null)
        : base(messageEn)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
        MessageAr = messageAr;
        MessageEn = messageEn;
        Details = details;
    }

    public static ApiContractException BadRequest(string messageAr, string messageEn, object? details = null, string? code = null) =>
        new(StatusCodes.Status400BadRequest, code ?? ApiErrorCodes.BadRequest, messageAr, messageEn, details);

    public static ApiContractException ValidationFailed(IDictionary<string, string[]> fieldErrors, string? messageAr = null, string? messageEn = null) =>
        new(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationFailed,
            messageAr ?? "البيانات المرسلة في الطلب غير صالحة أو ناقصة.",
            messageEn ?? "One or more request parameters or payload fields failed validation.",
            fieldErrors);

    public static ApiContractException Unauthorized(string? messageAr = null, string? messageEn = null) =>
        new(
            StatusCodes.Status401Unauthorized,
            ApiErrorCodes.Unauthorized,
            messageAr ?? "يلزم وجود جلسة مصادقة صالحة لتنفيذ هذا الطلب.",
            messageEn ?? "An active authenticated session is required to access this resource.");

    public static ApiContractException Forbidden(string? messageAr = null, string? messageEn = null) =>
        new(
            StatusCodes.Status403Forbidden,
            ApiErrorCodes.Forbidden,
            messageAr ?? "لا تملك الصلاحية اللازمة لتنفيذ هذا الإجراء.",
            messageEn ?? "You do not have sufficient permissions to perform this action.");

    public static ApiContractException NotFound(string? messageAr = null, string? messageEn = null) =>
        new(
            StatusCodes.Status404NotFound,
            ApiErrorCodes.NotFound,
            messageAr ?? "المورد المطلوب غير موجود أو غير متاح ضمن نطاق صلاحياتك.",
            messageEn ?? "The requested resource was not found or is not visible within your scope.");

    public static ApiContractException Conflict(string messageAr, string messageEn, object? details = null, string? code = null) =>
        new(StatusCodes.Status409Conflict, code ?? ApiErrorCodes.Conflict, messageAr, messageEn, details);

    public static ApiContractException PayloadTooLarge(string? messageAr = null, string? messageEn = null) =>
        new(
            StatusCodes.Status413PayloadTooLarge,
            ApiErrorCodes.PayloadTooLarge,
            messageAr ?? "حجم البيانات أو الملف المرفوع يتجاوز الحد الأقصى المسموح به.",
            messageEn ?? "The request payload or uploaded file exceeds the maximum allowed size.");

    public static ApiContractException UnprocessableEntity(string messageAr, string messageEn, object? details = null, string? code = null) =>
        new(StatusCodes.Status422UnprocessableEntity, code ?? ApiErrorCodes.UnprocessableEntity, messageAr, messageEn, details);

    public static ApiContractException TooManyRequests(string? messageAr = null, string? messageEn = null) =>
        new(
            StatusCodes.Status429TooManyRequests,
            ApiErrorCodes.TooManyRequests,
            messageAr ?? "تم تجاوز الحد المسموح به من الطلبات. يرجى المحاولة لاحقًا.",
            messageEn ?? "Rate limit exceeded. Please try again later.");
}

