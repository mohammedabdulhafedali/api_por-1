# تقرير تسليم السبرنت السادس — Sprint S06 (وثائق API وصلاحيات كل وثيقة والتكامل 1:1)

- **السبرنت:** `Sprint S06` — وثائق API وصلاحيات كل وثيقة (`D01`, `D02`, `I07`, `D03`, `D04`, `U02`)
- **الفرع التكاملي:** `develop` (بعد دمج `task/S06-documents-acl`)
- **التاريخ:** 24 نوفمبر 2026
- **الحالة:** مكتمل 100% (`6/6` مهام ضمن نطاق MVP) ومجتاز لبوابة الجودة الشاملة وفحوص الثغرات — جاهز للاختبار اليدوي من المالك قبل بدء `Sprint S07`

---

## 1. المهام المكتملة والوظائف المنفذة

1. **`D01` — إنشاء وثيقة API بعلاقات المزوّد والمستهلك والمجموعة (`UI-07`):**
   - تنفيذ خدمات ونقاط `/api/v1/api-documents*` في [DocumentManagementService.cs](file:///C:/api_asp/API_Governance_F01/backend/src/Modules/Documents/Services/DocumentManagementService.cs) و[DocumentsContracts.cs](file:///C:/api_asp/API_Governance_F01/backend/src/Modules/Documents/Contracts/DocumentsContracts.cs) و[DocumentsModule.cs](file:///C:/api_asp/API_Governance_F01/backend/src/Modules/Documents/DocumentsModule.cs).
   - اشتقاق عنوان الوثيقة تلقائيًا على الخادم (`DerivedTitle`: المزوّد ← المستهلك) دون حقل إدخال يدوي، ومنع اختيار النظام الداخلي نفسه مزوّدًا ومستهلكًا (`400`/`422`)، وربط القالب والمسار والسياسة النشطة، وحظر رفع الملفات داخل الوثيقة عبر [ApiDocumentsPage.tsx](file:///C:/api_asp/API_Governance_F01/frontend/src/components/documents/ApiDocumentsPage.tsx).

2. **`D02` — تعيين المطورين للوثيقة والفصل عن الصلاحيات:**
   - إتاحة تعيين `document_developers` أثناء إنشاء الوثيقة أو عبر `PUT /api/v1/api-documents/{documentId}/developers` (`api_documents.assign_developer` + `doc.assign`).
   - التحقق الصريح من الفصل التام بين تعيين المطور ومنح/سحب صلاحيات الوصول على الوثيقة (تعيين المطور لا يضيف صلاحية وصول تلقائيًا).

3. **`I07` و`U02` — محرك صلاحيات الوثيقة (`ADR-01`) والمصفوفة الشاملة (`UI-21A`):**
   - بناء مكون التفويض المركزي على مستوى الوثيقة [DocumentAuthorization.cs](file:///C:/api_asp/API_Governance_F01/backend/src/Shared/ApiGovernance.Shared/Security/DocumentAuthorization.cs) في الطبقة المشتركة لجميع الوحدات (`IDocumentAuthorizationService`) بجميع الصلاحيات الـ 11 (`doc.view`..`doc.publish`)، وبقواعد `ADR-01`: الوثيقة مقيدة افتراضيًا (منح ابتدائية للمنشئ فقط)، وتطبيق أولوية `User REVOKE > User GRANT > Role REVOKE > Role GRANT > Default Deny`.
   - منع هجمات `IDOR` بإخفاء الوثائق غير المصرح بها من القوائم وإرجاع `404` متطابقة عند طلب أي معرف مباشر بدون `doc.view` و`403` عند نقص صلاحية الفعل بعد `doc.view`.
   - بناء مصفوفة الصلاحيات الشاملة للأدوار والمستخدمين (`GET/PUT /api/v1/api-documents/{documentId}/access`) في [DocumentAccessMatrixPage.tsx](file:///C:/api_asp/API_Governance_F01/frontend/src/components/documents/DocumentAccessMatrixPage.tsx) مع منع ترك الوثيقة بلا أي مالك لصلاحية `doc.access.manage`.

4. **`D03` — قائمة وتعديل الوثائق وحماية الحذف (`UI-21`):**
   - تنفيذ القائمة المصفّاة بصلاحيات الوثيقة، وقفل تعديل الأطراف ومجموعة API بعد الارتباط بتكامل (`422`) مع إتاحة تعديل الإعدادات والحالة، ومنع الحذف عند الارتباط بتكامل (`422 Unprocessable Entity`).

5. **`D04` — إنشاء تكامل مرتبط بالوثيقة `1:1` (`UI-22` و`UI-08`):**
   - تنفيذ خدمات ونقاط `/api/v1/integrations*` في [IntegrationManagementService.cs](file:///C:/api_asp/API_Governance_F01/backend/src/Modules/Integrations/Services/IntegrationManagementService.cs) و[IntegrationsContracts.cs](file:///C:/api_asp/API_Governance_F01/backend/src/Modules/Integrations/Contracts/IntegrationsContracts.cs) و[IntegrationsModule.cs](file:///C:/api_asp/API_Governance_F01/backend/src/Modules/Integrations/IntegrationsModule.cs).
   - تصفية الوثائق المؤهلة غير المرتبطة ومنع تكرار التكامل لنفس الوثيقة (`409 Conflict`) وبناء [IntegrationsPage.tsx](file:///C:/api_asp/API_Governance_F01/frontend/src/components/integrations/IntegrationsPage.tsx) وربط جميع الشاشات في [App.tsx](file:///C:/api_asp/API_Governance_F01/frontend/src/App.tsx).

---

## 2. نتائج بوابة الجودة والفحوص الشاملة (`task-1858` و`task-1868`)

- **فحص وبناء الواجهة (`ESLint` + `TypeScript tsc -b` + `vite build`):** ناجح (`61` وحدة، `0` أخطاء).
- **اختبارات الواجهة (`Vitest`):** **25 / 25 اختبارًا ناجحًا** عبر `8 / 8` ملفات اختبار (تشمل [DocumentsAndIntegrationsS06.test.tsx](file:///C:/api_asp/API_Governance_F01/frontend/src/__tests__/DocumentsAndIntegrationsS06.test.tsx)).
- **بناء الحل الخلفي (`dotnet build -c Release --warnaserror`):** ناجح (`0 Warning(s), 0 Error(s)`).
- **اختبارات الخادم والتكامل (`dotnet test -c Release` على `api_governance_test`):** **53 / 53 اختبارًا ناجحًا** (`0` فاشل، `0` متجاوز — يشمل [DocumentsAclAndIntegrationsS06IntegrationTests.cs](file:///C:/api_asp/API_Governance_F01/tests/backend/ApiGovernance.Backend.Tests/Integration/DocumentsAclAndIntegrationsS06IntegrationTests.cs))؛ الإجمالي الكلي **`78 / 78` اختبارًا آليًا ناجحًا**.
- **فحوص ثغرات التبعيات (`npm audit --audit-level=low` و`dotnet list ApiGovernance.sln package --vulnerable --include-transitive`):** **`0` ثغرات** في الواجهة وجميع مشاريع الحل الـ 13.

---

## 3. ملاحظات للاختبار اليدوي

- تطبيقًا لمبدأ `ADR-01`، تأكد من منح دورك في **`UI-25`** الصلاحيات العامة: `api_documents.view`, `api_documents.create`, `api_documents.edit`, `api_documents.delete`, `api_documents.assign_developer`, `documents.permissions.view`, `documents.permissions.manage`, `integrations.view`, `integrations.create` قبل اختبار الشاشات إن لم تكن ممنوحة مسبقًا.
- لا يبدأ `Sprint S07` (`D05`, `D06`, `U01`, `U03`) قبل اعتمادكم نتائج `Sprint S06`.

