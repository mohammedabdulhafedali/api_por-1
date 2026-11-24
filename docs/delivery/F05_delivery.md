# تقرير تسليم المهمة F05 — Sprint S02

- **المهمة:** `F05` — إنشاء ترحيلات البنية الأساسية بعد مطابقة نموذج البيانات المعتمد
- **الفرع:** `task/F05` (متفرع من `develop` بعد دمج `task/F04`)
- **التاريخ:** 24 نوفمبر 2026
- **الحالة:** مكتملة محليًا وجاهزة لمراجعة المالك واعتماده (بدون دمج في `develop` قبل الاعتماد)

---

## 1. ملخص المطابقة الفعلية مع المخطط المعتمد وقرارات v1.2

تمت مطابقة نموذج البيانات الفعلي بالكامل مع المخطط المعتمد (`تصميم_قاعدة_البيانات_منصة_APIs_v0.1.dbml` و`مواصفات_قاعدة_البيانات_منصة_APIs_v0.1.md`) ومع الفروقات والقرارات المعتمدة في `08_DB_and_plan_delta.md` و`docs/decisions/G3_F01.md` دون اختراع قرارات جديدة:

- **الجداول الفعلية (43 جدولًا + `__EFMigrationsHistory`):**
  1. **مجموعة الهوية والصلاحيات (6):** `users`, `roles`, `user_roles`, `permissions`, `role_permissions`, `user_permission_overrides`.
  2. **مجموعة الأنظمة والجهات (6):** `company_systems`, `internal_provider_systems`, `external_provider_systems`, `consumer_categories`, `consumer_entities`, `api_groups`.
  3. **مجموعة قوالب الملفات ومسارات الحوكمة (9):** `file_types`, `file_type_extensions`, `file_requirement_templates`, `file_requirement_rules`, `approval_routes`, `approval_route_stages`, `change_types`, `change_policies`, `change_policy_types`.
  4. **مجموعة وثائق API والتكاملات والوصول (6):** `api_documents`, `document_developers`, `document_role_permissions`, `document_user_permissions`, `integrations`, `integration_developers`.
  5. **مجموعة الإصدارات والمراجعات والملفات والتغيير (7):** `api_versions`, `document_revisions`, `revision_file_requirements`, `file_objects`, `revision_files`, `change_requests`, `change_request_files`.
  6. **مجموعة قرارات سير العمل (3):** `workflow_runs`, `workflow_run_stages`, `workflow_decisions`.
  7. **مجموعة النشر والإصدار المستخدم (3):** `publications`, `publication_files`, `consumer_version_usage`.
  8. **مجموعة الإشعارات والتاريخ والتدقيق الإلحاقي (3):** `notifications`, `audit_events`, `release_history_events`.
- **العلاقات والقيود الفعلية المثبتة في PostgreSQL 18.7:**
  - **77 مفتاحًا أجنبيًا (`FOREIGN KEY`)** مضبوطة جميعها بسياسة `ON DELETE RESTRICT` لمنع الحذف العارض للكيانات المرتبطة (بما فيها `DB-03` بين `api_documents` و`integrations`).
  - **17 قيد تحقق (`CHECK Constraint`)** على قاعدة البيانات: شاملة قيود `XOR` لأطراف `api_documents` (`DB-01`) ومنع تساوي المزود والمستهلك الداخليين (`DB-02`)، وقيد `XOR` لهدف `workflow_runs` (`DB-09`)، وقيم قرارات التفويض (`GRANT`/`REVOKE`)، وصحة حدود الأعداد ونطاقات التواريخ وطول `sha256`.
  - **148 فهرسًا (`INDEX`)** على الجداول الـ 43 تشمل **86 فهرسًا فريدًا (`CREATE UNIQUE INDEX`)**: ضمنها 43 فهرس مفتاح أساسي و43 فهرس تفرد (منها **7 فهارس تفرد مشروطة** تغطي سياسات `OP-05` لوثيقة واحدة لكل مزود ومستهلك ومجموعة ولكل مسودة نشطة، وسياسة `DB-15` لإصدار نشط واحد فقط لكل تكامل عند `valid_to IS NULL`).

---

## 2. الفروقات الموثقة عن التصميم المنطقي الأولي (`v0.1 → v1.2`)

1. **تنسيق جدول الهوية `users` مع `ASP.NET Core Identity` (`08_DB_and_plan_delta.md`):**
   - أُدمجت حقول المصادقة والقفل (`normalized_email`, `password_hash`, `security_stamp`, `concurrency_stamp`, `lockout_enabled`, `lockout_end`, `access_failed_count`) وقيد `access_failed_count >= 0` مباشرة في جدول `users` (بمعرف `UUID`) لمنع إنشاء جداول مستخدمين مزدوجة (`AspNetUsers`) في المهمة اللاحقة `I01`.
2. **سجل التدقيق الإلحاقي المزدوج الحماية (`OP-01` و`DB-16`):**
   - فرض الحماية الإلحاقية برمجيًا في `ApiGovernanceDbContext` (واجهة `IAppendOnlyEntity` على `AuditEventEntity` و`ReleaseHistoryEventEntity` تمنع `Modified`/`Deleted` في `SaveChanges`/`SaveChangesAsync`).
   - فرض الحماية على مستوى PostgreSQL عبر الدالة والمشغلات (`prevent_append_only_mutation`, `trg_audit_events_append_only`, `trg_release_history_events_append_only`) لمنع `UPDATE` و`DELETE` حتى لحساب المالك، مع سحب `UPDATE/DELETE/TRUNCATE` من حساب التطبيق `api_gov_app`.
3. **استبعاد الجداول المقترحة غير المعتمدة:**
   - لم تُضف جداول المقترحات غير المعتمدة (`file_upload_sessions`, `file_scan_attempts`, `word_derivatives`, `outbox_events`) التزامًا بحدود المواصفات المعتمدة.

---

## 3. فصل الصلاحيات والتحقق من الصلاحيات الفعلية (البند 8)

- **حساب التشغيل (`api_gov_app`):** مقيد بصلاحيات تشغيل البيانات فقط (`rolsuper=f`, `rolcreatedb=f`, `rolcreaterole=f`, `has_schema_privilege('public','USAGE')=t`, `has_schema_privilege('public','CREATE')=f`)، ومرتبط عبر `ConnectionStrings:DefaultConnection` و`ConnectionStrings:TestConnection`.
- **حساب الترحيلات (`api_gov_migrator`):** مخصص لتنفيذ DDL والترحيلات ومالك للجداول دون صلاحيات إدارية عليا (`rolsuper=f`, `rolcreatedb=f`, `rolcreaterole=f`, `has_schema_privilege('public','CREATE')=t`)، ومرتبط عبر `ConnectionStrings:MigrationsConnection` و`ConnectionStrings:TestMigrationsConnection` خارج Git (في `.NET User Secrets` ومتغيرات بيئة المستخدم).
- **فحص الصلاحيات الفعلية لا الأسماء (`DatabaseConnectivityProbe`):**
  - يستعلم من `pg_roles` و`has_schema_privilege(current_user, 'public', 'CREATE'/'USAGE')` ويرفض أي حساب تشغيل يمتلك `SUPERUSER` أو `CREATEDB` أو `CREATEROLE` أو `REPLICATION` أو `BYPASSRLS` أو صلاحية `CREATE` على المخطط `public` بغض النظر عن اسم الحساب.

---

## 4. الملفات المنشأة والمعدلة

- `backend/src/Shared/ApiGovernance.Shared/Persistence/Entities/BaselineEntities.cs` (جديد — 43 كيانًا + `IAppendOnlyEntity`)
- `backend/src/Shared/ApiGovernance.Shared/Persistence/Configurations/BaselineSchemaConfiguration.cs` (جديد — تكوين Fluent API الكامل للـ 43 جدولًا و77 علاقة و17 CHECK و148 فهرسًا)
- `backend/src/Shared/ApiGovernance.Shared/Persistence/ApiGovernanceDbContext.cs` (محدث بـ 43 `DbSet` وحماية `OP-01` في `SaveChanges`)
- `backend/src/Shared/ApiGovernance.Shared/Persistence/DatabaseConnectionResolver.cs` (محدث بإضافة `ResolveMigrationsConnectionString`)
- `backend/src/Shared/ApiGovernance.Shared/Persistence/DatabaseConnectivityProbe.cs` (محدث بالاستعلام والتحقق الفعلي من صلاحيات PostgreSQL)
- `backend/src/Shared/ApiGovernance.Shared/Persistence/ApiGovernanceDesignTimeDbContextFactory.cs` (جديد — لتوليد وتنفيذ ترحيلات EF Core عبر `api_gov_migrator`)
- `backend/src/Shared/ApiGovernance.Shared/Persistence/Migrations/20261124080348_InitialBaselineSchema.cs` و`.Designer.cs` و`ApiGovernanceDbContextModelSnapshot.cs` (جديدة — الترحيل الأساسي الكامل + مشغلات `OP-01` + تراجع `Down` نظيف)
- `backend/src/ApiGovernance.Host/ApiGovernance.Host.csproj` (محدث بإضافة `Microsoft.EntityFrameworkCore.Design 10.0.15`)
- `backend/src/ApiGovernance.Host/appsettings.json` و`Program.cs` (تحديث مؤشر المهمة إلى `F05`)
- `tests/backend/ApiGovernance.Backend.Tests/Integration/BackendContractIntegrationTests.cs` و`DatabaseConnectivityIntegrationTests.cs` (اختبارات دورة الترحيل `Up`/`Down`/`Up`، وفحص الصلاحيات الفعلية، واختبارات الإلحاق `OP-01`، واختبارات القيود والفهارس الشرطية و`Restrict`)
- `docs/delivery/F05_delivery.md` و`docs/delivery/S01_status.md` و`backend/README.md` و`README.md`

---

## 5. نتائج الاختبارات وبوابة الجودة وفحص الثغرات

- **بوابة الجودة (`tests/run-quality-gate.ps1`):** ناجحة بالكامل (`[1/6]`..`[6/6]`):
  - ESLint + TypeCheck + Vitest (**9 / 9**) + Vite Production Build: ناجح.
  - `dotnet build ApiGovernance.sln -c Release --warnaserror`: ناجح (`0 Warning(s), 0 Error(s)`).
  - `dotnet test ApiGovernance.sln -c Release`: **37 / 37 اختبارًا ناجحًا (`0` فاشل)** — تشمل اختبارات الترحيل والتراجع على `api_governance_test` دون المساس بـ `api_governance_dev`.
  - **إجمالي الاختبارات الآلية في المشروع:** **46 اختبارًا آليًا ناجحًا (`9 واجهة + 37 خادم`)، `0 فاشل`**.
- **فحص ثغرات الحزم:**
  - `npm audit`: **`0 vulnerabilities`**.
  - `dotnet list ApiGovernance.sln package --vulnerable --include-transitive`: **`0 vulnerable packages`** عبر جميع الـ 13 مشروعًا.

