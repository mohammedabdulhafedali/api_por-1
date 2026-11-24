# تقرير تسليم المهمة F07 — Sprint S01

- **المهمة:** `F07` — وضع معيار عقود API والأخطاء والنسخ والتوثيق (`OpenAPI`)
- **الفرع:** `task/F07` (معتمد ومدمج في `develop`)
- **التاريخ:** 24 نوفمبر 2026
- **الحالة:** مكتملة ومعتمدة ومدمجة في `develop` (`Approved & Merged`)

## 1. ما تم تنفيذه ضمن نطاق F07
1. **توحيد معالجة الأخطاء (`RFC 9457 / RFC 7807 Problem Details` + عقد المعمارية المعتمد):**
   - جميع الأخطاء (`400`, `401`, `403`, `404`, `405`, `409`, `413`, `422`, `429`, `500`) تُعاد بنوع المحتوى `application/problem+json; charset=utf-8`.
   - تتضمن الاستجابة حقول `ProblemDetails` القياسية (`type`, `title`, `status`, `detail`, `instance`) بالإضافة إلى الحقول الأربعة المعتمدة في `تصميم_الخدمات_الخلفية_APIs_v0.1.md`:
     `{ code, message, details, traceId }`.
   - دعم ثنائية اللغة (`AR` / `EN`) تلقائيًا لرسائل وعناوين الأخطاء بناءً على ترويسة `Accept-Language` (مع جعل العربية هي الافتراضية).
   - **ملاحظة نطاق بخصوص `413` و`429`:** تم اختبار وتثبيت عقد استجابة الخطأ (`Problem Details`) لرمزي `413 Payload Too Large` (`ERR_PAYLOAD_TOO_LARGE`) و`429 Too Many Requests` (`ERR_TOO_MANY_REQUESTS`) ضمن `F07` فقط؛ أما التنفيذ الفعلي لقيود حجم رفع الملفات فسيتم في المهمة `V02` (`S08`)، وتقييد معدل محاولات المصادقة في المهمة `I01` (`S02`).
2. **معرفات تتبع الطلبات وإصدار العقد (`Correlation ID & API Versioning`):**
   - إنشاء `RequestCorrelationMiddleware` لقراءة وتمرير `X-Correlation-Id` (مع التحقق من سلامة الصيغة عبر Regex لمنع حقن الترويسات) أو توليد معرف تتبع آمن وربطه بـ `HttpContext.TraceIdentifier` وحقل `traceId` في كل استجابة.
   - إضافة ترويسة `X-API-Version: 1.0` وتوحيد بادئة المسارات تحت `/api/v1`.
3. **آلية التحقق من المدخلات (`RequestValidationFilter<TRequest>`):**
   - فلتر قياسي يتحقق من نماذج الطلبات عبر `DataAnnotations` و`IValidatableObject` ويعيد `400 Bad Request` برمز `ERR_VALIDATION_FAILED` وخريطة الحقول المخالفة في `details`.
   - تأسيس نماذج الترقيم القياسية `PagedQueryRequest` و`PagedResult<T>`.
4. **توثيق `OpenAPI 3.1.1` وتعطيل مسارات الفحص التجريبية في الإنتاج:**
   - استخدام الحزمة الرسمية الآمنة `Microsoft.AspNetCore.OpenApi` (`10.0.15` — `0 warnings / 0 vulnerabilities`) لتوليد وثيقة `/openapi/v1.json` و`/api/v1/openapi/v1.json`.
   - تعطيل نشر مسارات `OpenAPI` ومسارات الفحص التجريبية (`/api/v1/contracts/metadata` و`/api/v1/contracts/validate-probe`) كليًا في بيئة `Production` (`!app.Environment.IsProduction()`).
5. **منع كشف تفاصيل الخادم والاستثناءات في بيئة الإنتاج (`Production Hardening`):**
   - إزالة ترويسة `Server` من Kestrel (`options.AddServerHeader = false`) وإزالة `Server` و`X-Powered-By` في الـ Middleware.
   - عند وقوع أي استثناء غير متوقع (`500 Internal Server Error`)، يسجل `GlobalProblemDetailsExceptionHandler` الخطأ داخليًا في `ILogger` مع `TraceId`، ويعيد للعميل رسالة عامة موحدة فقط (`ERR_INTERNAL_SERVER_ERROR`) دون كشف نص الاستثناء الداخلي أو الـ Stack Trace.
   - تقييد استجابة `Health Check` (`/health` و`/api/v1/health`) في بيئة `Production` لتعيد فقط `{ "status": "Healthy", "timestampUtc": "..." }` دون كشف أسماء الوحدات الداخلية أو بيئة الخادم.

## 2. نتائج البناء والاختبارات الفعلية (بما فيها 413 و429 وتعطيل مسارات الفحص في الإنتاج)
- **البناء (`dotnet build ApiGovernance.sln -c Release`):** `Build succeeded. 0 Warning(s), 0 Error(s)`.
- **اختبار `413 Payload Too Large` (`Accept-Language: ar`):**
  - الحالة: `413`، الرمز: `ERR_PAYLOAD_TOO_LARGE`، العنوان: `"حجم الطلب يتجاوز الحد المسموح"`, `traceId: "corr-f07-413-ar"`.
- **اختبار `429 Too Many Requests` (`Accept-Language: en`):**
  - الحالة: `429`، الرمز: `ERR_TOO_MANY_REQUESTS`، العنوان: `"Too Many Requests"`, `traceId: "corr-f07-429-en"`.
- **التحقق من تعطيل جميع مسارات الفحص وOpenAPI في بيئة `Production`:**
  - `GET /openapi/v1.json` في `Production` → `404 Not Found` (`ERR_NOT_FOUND`).
  - `GET /api/v1/contracts/metadata` في `Production` → `404 Not Found` (`ERR_NOT_FOUND`).
  - `POST /api/v1/contracts/validate-probe` في `Production` → `404 Not Found` (`ERR_NOT_FOUND`).
  - `GET /api/v1/health` في `Production` → `200 OK` بمحتوى مقتضب فقط `{"status":"Healthy","timestampUtc":"..."}`.
  - فحص تسرب نص الاستثناء الحساس في `500 Internal Server Error`: `LeaksSensitiveMessage = false`.
