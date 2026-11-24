using Microsoft.AspNetCore.Http;

namespace ApiGovernance.Shared.Contracts;

/// <summary>
/// Standardized API Versioning and Header constants per Backend API Specification v0.1 / Baseline v1.2.
/// </summary>
public static class ApiContractDefaults
{
    public const string CurrentVersion = "1.0";
    public const string BasePathV1 = "/api/v1";
    public const string ApiVersionHeaderName = "X-API-Version";
    public const string CorrelationIdHeaderName = "X-Correlation-Id";
    public const string TraceIdExtensionKey = "traceId";
    public const string CodeExtensionKey = "code";
    public const string MessageExtensionKey = "message";
    public const string DetailsExtensionKey = "details";
}

/// <summary>
/// Canonical machine-readable error codes mapped to HTTP status codes.
/// </summary>
public static class ApiErrorCodes
{
    public const string BadRequest = "ERR_BAD_REQUEST";
    public const string ValidationFailed = "ERR_VALIDATION_FAILED";
    public const string Unauthorized = "ERR_UNAUTHORIZED";
    public const string Forbidden = "ERR_FORBIDDEN";
    public const string NotFound = "ERR_NOT_FOUND";
    public const string MethodNotAllowed = "ERR_METHOD_NOT_ALLOWED";
    public const string Conflict = "ERR_CONFLICT";
    public const string PayloadTooLarge = "ERR_PAYLOAD_TOO_LARGE";
    public const string UnprocessableEntity = "ERR_UNPROCESSABLE_ENTITY";
    public const string TooManyRequests = "ERR_TOO_MANY_REQUESTS";
    public const string InternalError = "ERR_INTERNAL_SERVER_ERROR";

    public static string FromStatusCode(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => BadRequest,
        StatusCodes.Status401Unauthorized => Unauthorized,
        StatusCodes.Status403Forbidden => Forbidden,
        StatusCodes.Status404NotFound => NotFound,
        StatusCodes.Status405MethodNotAllowed => MethodNotAllowed,
        StatusCodes.Status409Conflict => Conflict,
        StatusCodes.Status413PayloadTooLarge => PayloadTooLarge,
        StatusCodes.Status422UnprocessableEntity => UnprocessableEntity,
        StatusCodes.Status429TooManyRequests => TooManyRequests,
        _ => InternalError
    };
}

/// <summary>
/// Bilingual (AR/EN) localized default error titles and messages based on Accept-Language header.
/// Never exposes database, file system, or internal stack trace information.
/// </summary>
public static class ApiErrorLocalizer
{
    public static bool PrefersArabic(HttpContext? httpContext)
    {
        var acceptLanguage = httpContext?.Request.Headers.AcceptLanguage.ToString();
        if (string.IsNullOrWhiteSpace(acceptLanguage))
        {
            return true; // Arabic is primary enterprise language when unspecified
        }

        foreach (var segment in acceptLanguage.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var langTag = segment.Split(';')[0].Trim();
            if (langTag.StartsWith("ar", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (langTag.StartsWith("en", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    public static (string Title, string Message) GetLocalizedDefaults(int statusCode, bool isArabic, string? errorCode = null)
    {
        if (string.Equals(errorCode, ApiErrorCodes.ValidationFailed, StringComparison.Ordinal))
        {
            return isArabic
                ? ("فشل التحقق من المدخلات", "البيانات المرسلة في الطلب غير صالحة أو ناقصة.")
                : ("Input Validation Failed", "One or more request parameters or payload fields failed validation.");
        }

        return statusCode switch
        {
            StatusCodes.Status400BadRequest => isArabic
                ? ("طلب غير صالح", "صيغة الطلب أو المدخلات المرسلة غير سليمة.")
                : ("Bad Request", "The request syntax or payload format is invalid."),
            StatusCodes.Status401Unauthorized => isArabic
                ? ("غير مصادق عليه", "يلزم وجود جلسة مصادقة صالحة لتنفيذ هذا الطلب.")
                : ("Unauthorized", "An active authenticated session is required to access this resource."),
            StatusCodes.Status403Forbidden => isArabic
                ? ("إجراء غير مصرح به", "لا تملك الصلاحية اللازمة لتنفيذ هذا الإجراء.")
                : ("Forbidden", "You do not have sufficient permissions to perform this action."),
            StatusCodes.Status404NotFound => isArabic
                ? ("المورد غير موجود", "المورد المطلوب غير موجود أو غير متاح ضمن نطاق صلاحياتك.")
                : ("Not Found", "The requested resource was not found or is not visible within your scope."),
            StatusCodes.Status405MethodNotAllowed => isArabic
                ? ("طريقة HTTP غير مدعومة", "طريقة الطلب المستخدمة غير مسموحة لهذا المسار.")
                : ("Method Not Allowed", "The HTTP method is not supported for this endpoint."),
            StatusCodes.Status409Conflict => isArabic
                ? ("تعارض في حالة المورد", "يتعارض هذا الإجراء مع الحالة الحالية للمورد أو مع عملية متزامنة.")
                : ("Resource State Conflict", "The request conflicts with the current state or version of the resource."),
            StatusCodes.Status413PayloadTooLarge => isArabic
                ? ("حجم الطلب يتجاوز الحد المسموح", "حجم البيانات أو الملف المرفوع يتجاوز الحد الأقصى المسموح به.")
                : ("Payload Too Large", "The request payload or uploaded file exceeds the maximum allowed size."),
            StatusCodes.Status422UnprocessableEntity => isArabic
                ? ("شروط العمل غير متحققة", "لا يمكن معالجة الطلب لعدم استيفاء قواعد العمل المعتمدة.")
                : ("Business Rule Violation", "The request could not be processed because business rules were not satisfied."),
            StatusCodes.Status429TooManyRequests => isArabic
                ? ("تجاوز حد المحاولات", "تم تجاوز الحد المسموح به من الطلبات. يرجى المحاولة لاحقًا.")
                : ("Too Many Requests", "Rate limit exceeded. Please try again later."),
            _ => isArabic
                ? ("خطأ داخلي في الخادم", "حدث خطأ غير متوقع أثناء معالجة الطلب. يرجى مراجعة معرف التتبع.")
                : ("Internal Server Error", "An unexpected error occurred while processing the request. Refer to the traceId.")
        };
    }
}

