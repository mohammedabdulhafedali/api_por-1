# تقرير تسليم السبرنت السابع — Sprint S07 (مطوّرو التكامل وتفاصيله والتعريب وتوحيد الرسائل)

- **السبرنت:** `Sprint S07` — استكمال التكاملات والتعريب وتوحيد تجربة النظام (`D05`, `D06`, `U01`, `U03`)
- **الفرع التكاملي:** `develop` (بعد دمج `task/S07-integrations-i18n`)
- **التاريخ:** 24 نوفمبر 2026
- **الحالة:** مكتمل 100% (`4/4` مهام ضمن نطاق MVP) ومجتاز لبوابة الجودة الشاملة وفحوص الثغرات — جاهز للاختبار اليدوي من المالك قبل بدء `Sprint S08`

---

## 1. المهام المكتملة والوظائف المنفذة

1. **`D05` — مطورو التكامل ونطاق العمل والفصل التام عن صلاحيات الوثيقة (`ACL`):**
   - تحديث خدمة التفويض المشتركة [DocumentAuthorization.cs](file:///C:/api_asp/API_Governance_F01/backend/src/Shared/ApiGovernance.Shared/Security/DocumentAuthorization.cs) بإضافة `IsUserAssignedDeveloperAsync` للتحقق من تعيين المطور دون أي دمج مع جدول الصلاحيات الفعلي.
   - إضافة نقطتي `GET /api/v1/integrations/{integrationId}/developers` و`PUT /api/v1/integrations/{integrationId}/developers` (بالتحقق من `CSRF` و`integrations.assign_developer` و`doc.assign` مع تسجيل `OP-01`) في [IntegrationsModule.cs](file:///C:/api_asp/API_Governance_F01/backend/src/Modules/Integrations/IntegrationsModule.cs) و[IntegrationManagementService.cs](file:///C:/api_asp/API_Governance_F01/backend/src/Modules/Integrations/Services/IntegrationManagementService.cs).
   - عرض مؤشرات الصلاحيات الفعلية الحية لكل مطور (`HasDocView`, `HasDocVersionManage`, `HasDocFilesManage`, `HasDocChange`, `HasDocAssign`) وتأكيد أن تعيين المطور يحدد النطاق فقط ولا يمنح أي صلاحية `doc.*` تلقائيًا، بالإضافة إلى فلتر النطاق (`assignedToMe=true`) ومنع IDOR (`404` لمن يفتقد `doc.view`).

2. **`D06` — قائمة التكاملات وصفحة التفاصيل متعددة التبويبات (`UI-22` و`UI-09`):**
   - توسيع [IntegrationsContracts.cs](file:///C:/api_asp/API_Governance_F01/backend/src/Modules/Integrations/Contracts/IntegrationsContracts.cs) و`GetIntegrationDetailsAsync` لإرجاع `IntegrationDetailsDto` من بيانات قاعدة البيانات الحقيقية (ملخص التكامل، المطورون، القواعد المطبّقة من لقطة القالب، مراحل الاعتماد، أنواع التغيير والسياسة، وآخر أحداث التدقيق).
   - بناء واجهة التفاصيل متعددة التبويبات (`UI-09`) في [IntegrationsPage.tsx](file:///C:/api_asp/API_Governance_F01/frontend/src/components/integrations/IntegrationsPage.tsx) و[documentsAndIntegrationsApi.ts](file:///C:/api_asp/API_Governance_F01/frontend/src/api/documentsAndIntegrationsApi.ts) بالتبويبات الستة المعتمدة:
     - `المطورون والنطاق` (`D05`) مع شاشة تعديل مستقلة (`StandaloneFormPage`)،
     - `الملفات` (`UI-10` — قواعد القالب والجاهزية التمهيدية قبل `S08`)،
     - `المراجعات` (`UI-11` — مراحل الاعتماد التمهيدية)،
     - `الإصدارات` (`UI-12` — الإعداد التمهيدي قبل `S08`)،
     - `طلبات التغيير` (`UI-13` — أنواع التغيير والسياسة المطبقة)،
     - `السجل والنشر` (`UI-14` — خيار النشر وحالة التكامل وسجل التدقيق).

3. **`U01` — استكمال التعريب والإنجليزية وعناوين الشاشات التفاعلية (`AR ⇄ EN` و`RTL/LTR`):**
   - تحديث شريط الترويسة ومسار التنقل في [App.tsx](file:///C:/api_asp/API_Governance_F01/frontend/src/App.tsx) لعرض عنوان الشاشة النشطة ومعرّفها باللغتين ديناميكيًا عند الانتقال إلى الشاشات التشغيلية، مع الحفاظ على الترويسة الأولية لصفحة `UI-17`.
   - استكمال تعريب حالات الوثيقة (`ACTIVE`, `DRAFT`, `ARCHIVED`) في [ApiDocumentsPage.tsx](file:///C:/api_asp/API_Governance_F01/frontend/src/components/documents/ApiDocumentsPage.tsx) وتعريب كامل قاموسي `AR` و`EN` في [IntegrationsPage.tsx](file:///C:/api_asp/API_Governance_F01/frontend/src/components/integrations/IntegrationsPage.tsx) ورسائل `ProblemDetails` المستجيبة لترويسة `Accept-Language`.

4. **`U03` — توحيد الرسائل المركزية وصفحات الإضافة والتعديل المستقلة:**
   - تطوير [CentralModalDialog.tsx](file:///C:/api_asp/API_Governance_F01/frontend/src/components/ui/CentralModalDialog.tsx) لدعم عرض `traceId` وإرجاع التركيز تلقائيًا للعنصر السابق عند الإغلاق، مع ربط `traceId` من `ApiProblemError` في صفحتي الوثائق والتكاملات والالتزام التام بعدم استخدام `alert`/`confirm`/`Toast`.
   - تنفيذ نموذج تعيين مطوري التكامل داخل `StandaloneFormPage` مستقلة دون أي نوافذ منبثقة للنموذج.

---

## 2. نتائج بوابة الجودة والفحوص الشاملة (`task-1962` و`task-1978`)

- **فحص وبناء الواجهة (`ESLint` + `TypeScript tsc -b` + `vite build`):** ناجح (`61` وحدة، `0` أخطاء).
- **اختبارات الواجهة (`Vitest`):** **26 / 26 اختبارًا ناجحًا** عبر `9 / 9` ملفات اختبار (`0` فاشل — تشمل [IntegrationsWorkspaceS07.test.tsx](file:///C:/api_asp/API_Governance_F01/frontend/src/__tests__/IntegrationsWorkspaceS07.test.tsx)).
- **بناء الحل الخلفي (`dotnet build -c Release -p:TreatWarningsAsErrors=true`):** ناجح (`0 Warning(s), 0 Error(s)`).
- **اختبارات الخادم والتكامل (`dotnet test -c Release` على `api_governance_test`):** **54 / 54 اختبارًا ناجحًا** (`0` فاشل، `0` متجاوز — يشمل [IntegrationsD05D06S07IntegrationTests.cs](file:///C:/api_asp/API_Governance_F01/tests/backend/ApiGovernance.Backend.Tests/Integration/IntegrationsD05D06S07IntegrationTests.cs))؛ الإجمالي الكلي **`80 / 80` اختبارًا آليًا ناجحًا**.
- **فحوص ثغرات التبعيات (`npm audit --audit-level=low` و`dotnet list ApiGovernance.sln package --vulnerable --include-transitive`):** **`0` ثغرات** في الواجهة وجميع مشاريع الحل الـ 13.

---

## 3. ملاحظات للاختبار اليدوي

- تطبيقًا لمبدأ `ADR-01`، تأكد من منح دورك في **`UI-25`** الصلاحيات العامة الخاصة بالتكاملات (`integrations.view`, `integrations.create`, `integrations.assign_developer`) ومنح حسابك الصلاحيات المطلوبة على الوثيقة من **`UI-21A`** (`doc.view`, `doc.assign`) لاختبار التعيين والتفاصيل.
- لا يبدأ `Sprint S08` قبل اعتمادكم نتائج `Sprint S07`.
