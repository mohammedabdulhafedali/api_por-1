# تقرير تسليم السبرنت التاسع — Sprint S09 (مراجعات حزم التوثيق، الحماية والتزامن، التنزيل الموثق، وإثبات جدوى Word Open XML)

- **السبرنت:** `Sprint S09` — مراجعات حزم التوثيق (`V05`), حماية النسخ المعتمدة وإنشاء نسخ العمل مع ضبط التزامن (`V06` / `OP-05`), التنزيل عبر الخادم مع الصلاحية والبصمة والتدقيق (`V07`), وإثبات جدوى تحديث حقول Word بـ `Open XML SDK` (`V08` / `OP-03`)
- **فرع العمل والدمج:** `task/S09-revisions-word-poc` ← `develop`
- **تاريخ الاعتماد المرجعي للسبرنت الثامن (`S08`):** 25 نوفمبر 2026 (مُعتمد رسميًا من مالك المشروع قبل فتح `S09`)
- **الحالة:** **مكتمل 100% (`4/4` مهام ضمن نطاق MVP) ومجتاز لبوابة الجودة الشاملة وفحوص الثغرات — متوقف عند نهاية `S09` بانتظار اختبار المالك واعتماده قبل بدء `Sprint S10` (`W01`–`W04`)**

---

## 1. المهام المكتملة والوظائف المنفذة (`V05`–`V08`)

1. **`V05` — مراجعات حزم التوثيق والتحقق من اكتمال الملفات الإلزامية قبل التقديم (`UI-10`, `UI-11`):**
   - تنفيذ نقاط `GET /api/v1/versions/{versionId}/revisions`, `GET /api/v1/revisions/{revisionId}`, و`POST /api/v1/revisions/{revisionId}/submit` في [VersionsFilesModule.cs](file:///d:/api-portal-1/backend/src/Modules/VersionsFiles/VersionsFilesModule.cs) و[VersionsFilesService.cs](file:///d:/api-portal-1/backend/src/Modules/VersionsFiles/Services/VersionsFilesService.cs).
   - حساب مصفوفة استيفاء المتطلبات لكل مراجعة (`IsReadyForSubmission`, `MissingRequiredTypesCount`, `SubmissionBlockersAr/En`) استنادًا إلى **نسخة متطلبات المراجعة المجمدة** (`revision_file_requirements`) وحظر تقديم أي مراجعة (`422 UnprocessableEntity`) عند نقص أي نوع ملف إلزامي أو وجود ملفات في حالة `PENDING_SCAN`/`INFECTED`/`INVALID` أو تجاوز `MaxCount`.
   - تسجيل حدث تدقيق `REVISION_SUBMIT_BLOCKED` عند تعذر التقديم أو `REVISION_SUBMITTED` وسجل تاريخي `ReleaseHistoryEventEntity` عند نجاح الانتقال من `DRAFT` إلى `SUBMITTED`.

2. **`V06` — حماية النسخ المعتمدة من التعديل وإنشاء نسخة عمل جديدة مع ضبط التزامن (`OP-05`):**
   - حماية جميع المراجعات غير `DRAFT` (بما فيها `SUBMITTED` و`APPROVED`) برفض رفع الملفات أو حذفها أو إعادة فحصها أو إعادة تقديمها (`422` وتسجيل `REVISION_IMMUTABILITY_VIOLATION`).
   - إتاحة فتح نسخة عمل جديدة (`baseRevisionId` + `changeRequestId`) فقط انطلاقًا من مراجعة `APPROVED` وطلب تغيير `APPROVED` مرتبط بها، مع نسخ الملفات السليمة الموروثة مرتبطةً عبر `SourceRevisionFileId`.
   - ضبط التزامن عبر طبقتين: فحص برمجي في [VersionsFilesService.cs](file:///d:/api-portal-1/backend/src/Modules/VersionsFiles/Services/VersionsFilesService.cs) وفهرس فريد جزئي في قاعدة البيانات (`ux_document_revisions_active_draft_per_api_version` حيث `status = 'DRAFT'`) في الترحيل [20261124221501_AddS09RevisionConcurrencyAndWordDerivatives.cs](file:///d:/api-portal-1/backend/src/Shared/ApiGovernance.Shared/Persistence/Migrations/20261124221501_AddS09RevisionConcurrencyAndWordDerivatives.cs)، مع استرجاع `409 Conflict` وتسجيل `REVISION_WORKING_COPY_CONFLICT` عند أي تزامن متنافس (`OP-05`).

3. **`V07` — تنزيل الملفات عبر الخادم مع التحقق من الصلاحيات وبصمة `SHA-256` والتدقيق:**
   - تقوية `GET /api/v1/revisions/{revisionId}/files/{fileId}/download` و`GET .../word-derivative/download` بفحوص التفويض المركزي والصلاحية (`files.download` + `doc.view`), حظر التنزيل لأي حالة غير `CLEAN`, التحقق الحي من بصمة `SHA-256` الفعلية على القرص قبل البث (إرجاع `422` وتسجيل `FILE_DOWNLOAD_INTEGRITY_FAILED` عند اكتشاف أي تلاعب)، إضافة ترويسات الأمان (`X-Content-Type-Options: nosniff`, `Content-Security-Policy: sandbox`, `ETag`, `X-File-Sha256`, `Cache-Control: no-store, no-cache`), وتسجيل حدث التدقيق `FILE_DOWNLOADED`.
   - تحديث عميل التنزيل في [versionsFilesApi.ts](file:///d:/api-portal-1/frontend/src/api/versionsFilesApi.ts) وواجهة [IntegrationsPage.tsx](file:///d:/api-portal-1/frontend/src/components/integrations/IntegrationsPage.tsx) للحفاظ على توافق `S08` وعرض أخطاء النزاهة والصلاحيات داخل `CentralModalDialog`.

4. **`V08` — إثبات جدوى تحديث حقول Word باستخدام `Open XML SDK` (`OP-03`):**
   - دمج حزمة `DocumentFormat.OpenXml` (`3.3.0`) وتنفيذ [WordGovernanceDerivativeService.cs](file:///d:/api-portal-1/backend/src/Modules/VersionsFiles/OpenXml/WordGovernanceDerivativeService.cs) مع جدول التتبع الدائم `word_derivative_records` (`WordDerivativeRecordEntity`).
   - توليد مشتق `.docx` مستقل دون أي مساس بالملف الأصلي أو بصمته، وتمريره عبر `OpenXmlValidator(FileFormatVersions.Microsoft365)` ثم الفحص الأمني (`IFileSecurityScanner`) وحظر نشر/تنزيل أي مشتق بحالة غير `VERIFIED` (`NO_RECOGNIZED_FIELDS`, `PENDING_SCAN`, `FAILED`) وفق `OP-03`.
   - توثيق حدود النموذج وعدم تعميمه على ملفات Word العشوائية في [V08_word_poc_report.md](file:///d:/api-portal-1/docs/delivery/V08_word_poc_report.md).

---

## 2. نتائج بوابة الجودة والفحوص الشاملة (`run-quality-gate.ps1`)

- **فحص وبناء الواجهة (`ESLint` + `tsc -b` + `vite build`):** ناجح (`0` أخطاء، بناء الإنتاج اكتمل في `5.04s`).
- **اختبارات الواجهة (`Vitest`):** **`28 / 28` اختبارًا ناجحًا** عبر **`11 / 11` ملف اختبار** (`0` فاشل — يشمل [VersionsFilesS08.test.tsx](file:///d:/api-portal-1/frontend/src/__tests__/VersionsFilesS08.test.tsx) و[VersionsFilesS09.test.tsx](file:///d:/api-portal-1/frontend/src/__tests__/VersionsFilesS09.test.tsx)).
- **بناء الحل الخلفي (`dotnet build -c Release --no-restore --warnaserror`):** ناجح (`0 Warning(s), 0 Error(s)`).
- **اختبارات الخادم والتكامل (`dotnet test -c Release --no-build` على `api_governance_test`):** **`60 / 60` اختبارًا ناجحًا** (`0` فاشل، `0` متجاوز — يشمل [VersionsFilesS09IntegrationTests.cs](file:///d:/api-portal-1/tests/backend/ApiGovernance.Backend.Tests/Integration/VersionsFilesS09IntegrationTests.cs) بجميع حالاته `V05`–`V08` واختبار المخطط `45` جدولًا / `83` علاقة `FK` في [DatabaseConnectivityIntegrationTests.cs](file:///d:/api-portal-1/tests/backend/ApiGovernance.Backend.Tests/Integration/DatabaseConnectivityIntegrationTests.cs)).
- **إجمالي الاختبارات الآلية الناجحة:** **`88 / 88` اختبارًا آليًا ناجحًا (`28` واجهة + `60` خادم)**.
- **فحوص ثغرات التبعيات:**
  - `npm audit --audit-level=low`: **`found 0 vulnerabilities`**.
  - `dotnet list ApiGovernance.sln package --vulnerable --include-transitive --no-restore`: **`0` ثغرات** في جميع مشاريع الحل الـ **`13`** مقابل `https://api.nuget.org/v3/index.json`.

---

## 3. المرحلة التالية (متوقفة بانتظار الاعتماد)

- **المرحلة القادمة:** **`Sprint S10` (`W01`–`W04`)** — محرك مسارات الاعتماد المتسلسل، القرارات (اعتماد/إعادة/رفض)، والانتقال الآمن بعد اعتماد المالك لنتائج `Sprint S09`.
