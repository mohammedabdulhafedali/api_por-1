# تقرير تسليم السبرنت الخامس — Sprint S05 (القوالب ومسارات الاعتماد والتغيير)

- **السبرنت:** `Sprint S05` — القوالب ومسارات الاعتماد والتغيير (`M05`, `M06`, `M07`)
- **الفرع التكاملي:** `develop` (بعد دمج `task/S05-governance-config`)
- **التاريخ:** 24 نوفمبر 2026
- **الحالة:** مكتمل 100% (`3/3` مهام ضمن نطاق MVP) ومجتاز لبوابة الجودة الشاملة وفحوص الثغرات — جاهز للاختبار اليدوي من المالك قبل بدء `Sprint S06`

---

## 1. المهام المكتملة والوظائف المنفذة

1. **`M05` — قوالب متطلبات الملفات مع اللقطات التاريخية (`UI-23`):**
   - تنفيذ خدمات ونقاط `/api/v1/requirement-templates*` في [RequirementTemplateService.cs](file:///C:/api_asp/API_Governance_F01/backend/src/Modules/Configuration/Services/RequirementTemplateService.cs) و[GovernanceConfigContracts.cs](file:///C:/api_asp/API_Governance_F01/backend/src/Modules/Configuration/Contracts/GovernanceConfigContracts.cs) و[ConfigurationModule.cs](file:///C:/api_asp/API_Governance_F01/backend/src/Modules/Configuration/ConfigurationModule.cs).
   - تعريف قواعد أنواع الملفات (`REQUIRED`, `OPTIONAL`, `CONDITIONAL`) والتحقق التعاودي من الأعداد (`MinCount`, `MaxCount`, `ConditionSpecJson`)، وتسجيل لقطات تاريخية إلحاقية غير قابلة للتعديل في `audit_events` (`GET /api/v1/requirement-templates/{templateId}/snapshots`) عند كل إنشاء أو تحديث.
   - حماية القوالب المرتبطة بوثائق API نشطة من التعديل البنيوي أو التعطيل (`422 Unprocessable Entity`) وبناء واجهة [RequirementTemplatesPage.tsx](file:///C:/api_asp/API_Governance_F01/frontend/src/components/configuration/RequirementTemplatesPage.tsx) بصفحات إضافة وتعديل مستقلة وعارض اللقطات التاريخية.

2. **`M06` — مسارات الاعتماد ومراحلها المرتبة حسب الدور (`UI-05` و`UI-05A`):**
   - تنفيذ خدمات ونقاط `/api/v1/approval-routes*` في [ApprovalRouteAndChangeConfigService.cs](file:///C:/api_asp/API_Governance_F01/backend/src/Modules/Configuration/Services/ApprovalRouteAndChangeConfigService.cs).
   - تقييد كل مرحلة بـ **الاسم + الترتيب + الدور المسؤول** فقط (`ApprovalRouteStageInput` مزوّد بـ `JsonUnmappedMemberHandling.Disallow` لرفض أي حقل غير معتمد مثل `stageType` أو `assignedUserId` بـ `400 Bad Request`).
   - منع تفعيل أو حفظ مسار فارغ (`0` مراحل) أو ترتيب متقطع أو أدوار غير نشطة، وحماية المراجع النشطة عند التعطيل، وبناء واجهة [ApprovalRoutesPage.tsx](file:///C:/api_asp/API_Governance_F01/frontend/src/components/configuration/ApprovalRoutesPage.tsx) مع صفحة `UI-05A` المستقلة لترتيب وتحرير المراحل (`أعلى ↑` / `أسفل ↓`).

3. **`M07` — أنواع طلبات التغيير وسياسات التغيير (`UI-06`):**
   - تنفيذ خدمات ونقاط `/api/v1/change-types*` و`/api/v1/change-policies*` وربط كل نوع تغيير بمسار اعتماد فعّال يمتلك مرحلة واحدة على الأقل، مع منع تعطيل نوع التغيير أو السياسة عند وجود ارتباطات نشطة.
   - بناء واجهة [ChangeTypesAndPoliciesPage.tsx](file:///C:/api_asp/API_Governance_F01/frontend/src/components/configuration/ChangeTypesAndPoliciesPage.tsx) بتبويبي أنواع التغيير وسياسات التغيير ضمن عنصر القائمة `UI-06` وربطها في [App.tsx](file:///C:/api_asp/API_Governance_F01/frontend/src/App.tsx).

4. **الأمان المشترك:**
   - ربط كل نقاط `M05`–`M07` بالتفويض المركزي `.RequireGeneralPermission(...)` (`requirements.*`, `routes.*`, `changes.config.*`) وحماية `CSRF` وتسجيل تدقيق `OP-01`.

---

## 2. نتائج بوابة الجودة والفحوص الشاملة (`task-1707` و`task-1722`)

- **فحص وبناء الواجهة (`ESLint` + `TypeScript tsc -b` + `vite build`):** ناجح (`57` وحدة، `0` أخطاء).
- **اختبارات الواجهة (`Vitest`):** **24 / 24 اختبارًا ناجحًا** عبر `7 / 7` ملفات اختبار (تشمل [GovernanceConfigS05.test.tsx](file:///C:/api_asp/API_Governance_F01/frontend/src/__tests__/GovernanceConfigS05.test.tsx)).
- **بناء الحل الخلفي (`dotnet build -c Release --warnaserror`):** ناجح (`0 Warning(s), 0 Error(s)`).
- **اختبارات الخادم والتكامل (`dotnet test -c Release` على `api_governance_test`):** **52 / 52 اختبارًا ناجحًا** (`0` فاشل، `0` متجاوز — يشمل [GovernanceConfigS05IntegrationTests.cs](file:///C:/api_asp/API_Governance_F01/tests/backend/ApiGovernance.Backend.Tests/Integration/GovernanceConfigS05IntegrationTests.cs))؛ الإجمالي الكلي **`76 / 76` اختبارًا آليًا ناجحًا**.
- **فحوص ثغرات التبعيات (`npm audit --audit-level=low` و`dotnet list ApiGovernance.sln package --vulnerable --include-transitive`):** **`0` ثغرات** في الواجهة وجميع مشاريع الحل الـ 13.

---

## 3. ملاحظات للاختبار اليدوي

- تطبيقًا لمبدأ `ADR-01` (عدم منح صلاحيات ثابتة باسم الدور)، يُرجى فتح **`إدارة النظام ← الأدوار والصلاحيات (UI-25)`** ومنح دورك صلاحيات `requirements.view`, `requirements.manage`, `routes.view`, `routes.manage`, `changes.config.view`, `changes.config.manage` قبل اختبار الشاشات الجديدة إن لم تكن ممنوحة مسبقًا.
- لا يبدأ `Sprint S06` (`D01`–`D04`, `I07`, `U02`) قبل اعتمادكم نتائج `Sprint S05`.

