# تقرير تسليم السبرنت الرابع — Sprint S04 (البيانات المرجعية والملفات)

- **السبرنت:** `Sprint S04` — البيانات المرجعية والملفات (`M01`, `M02`, `M03`, `M04`)
- **الفرع التكاملي:** `develop` (بعد دمج `task/S04-master-data`)
- **التاريخ:** 24 نوفمبر 2026
- **الحالة:** مكتمل 100% (`4/4` مهام ضمن نطاق MVP) ومجتاز لبوابة الجودة الشاملة وفحوص الثغرات — جاهز للاختبار اليدوي من المالك قبل بدء `Sprint S05`

---

## 1. المهام المكتملة والوظائف المنفذة

1. **`M01` — أنظمة الشركة (`company_systems`) ونوع الاستخدام المحسوب (`UI-01`):**
   - تنفيذ خدمات ونقاط `/api/v1/company-systems` في [MasterDataService.cs](file:///C:/api_asp/API_Governance_F01/backend/src/Modules/MasterData/Services/MasterDataService.cs) و[MasterDataModule.cs](file:///C:/api_asp/API_Governance_F01/backend/src/Modules/MasterData/MasterDataModule.cs).
   - حساب `computedUsageType` (`NONE`, `PROVIDER`, `CONSUMER`, `BOTH`) تلقائيًا من وثائق `api_documents` (`ProviderInternalSystemId` و`ConsumerCompanySystemId`) دون وجود حقل قابل للتعديل في العقود أو الواجهة.
   - حماية المراجع عند تعطيل نظام شركة مرتبط بوثائق API غير مؤرشفة (`422 Unprocessable Entity`) وبناء واجهة [CompanySystemsPage.tsx](file:///C:/api_asp/API_Governance_F01/frontend/src/components/masterdata/CompanySystemsPage.tsx) بصفحات إنشاء وتعديل مستقلة (`StandaloneFormPage`).

2. **`M02` — المزوّدون الداخليون والخارجيون (`UI-03`):**
   - **المزوّدون الداخليون (`/api/v1/internal-providers`):** ربط مزوّد داخلي بنظام شركة نشط، مع منع التكرار (`409 Conflict`)، وحماية المراجع عند التعطيل مع إبقاء سجل نظام الشركة الأصلي كما هو دون تعطيله.
   - **المزوّدون الخارجيون (`/api/v1/external-providers`):** كيان مستقل بالكامل دون حقل نظام شركة، مع حماية المراجع عند التعطيل، وربط الشاشتين ضمن [ProvidersManagementPage.tsx](file:///C:/api_asp/API_Governance_F01/frontend/src/components/masterdata/ProvidersManagementPage.tsx).

3. **`M03` — تصنيفات الجهات المستهلكة والجهات (`UI-02`):**
   - **تصنيفات الجهات (`/api/v1/consumer-categories`):** تصنيف مسطح من مستوى واحد فقط (دون شجرة متداخلة)، مع منع تعطيل تصنيف مرتبط بجهات مستهلكة نشطة.
   - **الجهات المستهلكة (`/api/v1/consumers`):** إدارة الجهات مع منع ربطها بتصنيف معطّل، ومنع التعطيل عند ارتباطها بمستخدمين أو وثائق API غير مؤرشفة، وبناء واجهة [ConsumersManagementPage.tsx](file:///C:/api_asp/API_Governance_F01/frontend/src/components/masterdata/ConsumersManagementPage.tsx).

4. **`M04` — مجموعات API وقاموس أنواع الملفات (`UI-04`):**
   - **مجموعات API (`/api/v1/api-groups`):** CRUD وحماية المراجع ودمجها كتبويب ضمن شاشة أنظمة الشركة [CompanySystemsPage.tsx](file:///C:/api_asp/API_Governance_F01/frontend/src/components/masterdata/CompanySystemsPage.tsx) للحفاظ على هيكل القائمة الجانبية المعتمد.
   - **قاموس أنواع الملفات (`/api/v1/file-types*`):** تنفيذ [FileTypeManagementService.cs](file:///C:/api_asp/API_Governance_F01/backend/src/Modules/Configuration/Services/FileTypeManagementService.cs) و[ConfigurationModule.cs](file:///C:/api_asp/API_Governance_F01/backend/src/Modules/Configuration/ConfigurationModule.cs) فوق `CommonFileTypeCatalog` القياسي، وتعبئة الامتدادات تلقائيًا، ورفض أي امتدادات غير مصرح بها (`400 Bad Request`)، وحصر إظهار وتخزين `WordTemplateObjectId` بنوع `WORD_DOC` فقط عبر [FileTypesManagementPage.tsx](file:///C:/api_asp/API_Governance_F01/frontend/src/components/masterdata/FileTypesManagementPage.tsx).

5. **الأمان المشترك والتكامل:**
   - نقل [AntiforgeryEndpointFilter.cs](file:///C:/api_asp/API_Governance_F01/backend/src/Shared/ApiGovernance.Shared/Security/AntiforgeryEndpointFilter.cs) و`PaginationHelper` في [PagedResponse.cs](file:///C:/api_asp/API_Governance_F01/backend/src/Shared/ApiGovernance.Shared/Contracts/PagedResponse.cs) إلى `ApiGovernance.Shared`، مع ربط جميع مسارات `MasterData` و`Configuration` بالتفويض المركزي `.RequireGeneralPermission(...)` (`I05`) والتحقق من `CSRF` وتسجيل تدقيق `OP-01`.

---

## 2. نتائج بوابة الجودة والفحوص الشاملة (`task-1550`)

- **فحص الواجهة (`ESLint` + `TypeScript tsc -b` + `vite build`):** ناجح (`53` وحدة، `0` أخطاء).
- **اختبارات الواجهة (`Vitest`):** **22 / 22 اختبارًا ناجحًا** عبر `6 / 6` ملفات اختبار (`App`, `AuthFlow`, `UsersManagement`, `RolesAndMultiRole`, `PermissionsMatrixAndOverrides`, `MasterDataS04`).
- **بناء الحل الخلفي (`dotnet build -c Release --warnaserror`):** ناجح (`0 Warning(s), 0 Error(s)`).
- **اختبارات الخادم والتكامل (`dotnet test -c Release` على `api_governance_test`):** **50 / 50 اختبارًا ناجحًا** — الإجمالي الكلي **`72 / 72` اختبارًا آليًا ناجحًا (`0` فاشل، `0` متجاوز)**.
- **فحوص ثغرات التبعيات (`npm audit --audit-level=low` و`dotnet list ApiGovernance.sln package --vulnerable --include-transitive`):** **`0` ثغرات** في الواجهة وجميع مشاريع الحل الـ 13.

---

## 3. ملاحظات للاختبار اليدوي

- نظرًا لتطبيق `ADR-01` (عدم منح أي صلاحية ثابتة بناءً على اسم الدور)، إذا كان الحساب الإداري التجريبي محفوظًا بصلاحيات التأسيس من `S02` فقط، يلزم أولًا فتح `إدارة النظام ← الأدوار والصلاحيات (UI-25)` ومنح دوره صلاحيات `M01`–`M04` (`company_systems.*`, `providers.*`, `consumers.*`, `api_groups.*`, `file_types.*`) ثم حفظ التغييرات.
- مهام `M05`–`M07` مؤجلة إلى `Sprint S05` ولا تبدأ قبل اعتماد نتائج `Sprint S04`.

