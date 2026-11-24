# تقرير تسليم المهمة F04 — Sprint S02

- **المهمة:** `F04` — ربط PostgreSQL 18 بـ EF Core 10 وNpgsql 10
- **الفرع:** `task/F04` (متفرع من `develop`)
- **التاريخ:** 24 نوفمبر 2026
- **الحالة:** مكتملة محليًا وجاهزة لمراجعة المالك واعتماده

---

## 1. ما تم تنفيذه ضمن نطاق F04
1. **ربط `ASP.NET Core 10` مع `PostgreSQL 18.7` باستخدام `EF Core 10.0.15` و`Npgsql 10.0.4`:**
   - إضافة الحزم الرسمية المستقرة الخالية من الثغرات (`0 vulnerable packages`) إلى `ApiGovernance.Shared.csproj`:
     - `Microsoft.EntityFrameworkCore` (`10.0.15`)
     - `Microsoft.EntityFrameworkCore.Relational` (`10.0.15`)
     - `Npgsql.EntityFrameworkCore.PostgreSQL` (`10.0.4`)
2. **تأسيس طبقة الوصول إلى قاعدة البيانات وفق معمارية `Modular Monolith`:**
   - إنشاء `ApiGovernanceDbContext` وعقد `IModuleModelConfiguration` داخل `ApiGovernance.Shared.Persistence` لتمكين كل وحدة من الوحدات العشر المعزولة في المهمة التالية `F05` من تسجيل تكوينات جداولها دون خرق حدود العزل المعماري.
   - الالتزام الصارم بعدم إنشاء أي جداول أعمال أو ترحيلات في `F04` (التحقق الآلي يثبت أن عدد الجداول في المخطط `public` يساوي `0`).
3. **توحيد مصدر إعدادات الاتصال ومنع استخدام الحساب الإداري (`DatabaseConnectionResolver`):**
   - قراءة سلسلة الاتصال من `.NET User Secrets` (`ConnectionStrings:DefaultConnection`) أو متغير البيئة (`API_GOVERNANCE_DB_CONNECTION`) خارج Git، مع ترك القيمة في `appsettings.json` فارغة `""`.
   - فرض ضابط أمني برمجي (`ValidateLeastPrivilegeConnectionString`) يرفض تشغيل التطبيق بحساب `postgres` الإداري ويلزم باستخدام حساب التطبيق المخصص الأقل صلاحية (`api_gov_app`).
   - توفير دالة `RedactConnectionString` لحجب كلمات المرور (`***REDACTED***`) في أي مخرجات تشخيصية.
4. **إضافة فحص جاهزية الاتصال بقاعدة البيانات (`PostgreSqlReadinessHealthCheck`):**
   - تسجيل فحص الجاهزية `database` ضمن `/health` و`/api/v1/health`:
     - يختبر الاتصال الفعلي بـ `PostgreSQL 18.7` عبر `ApiGovernanceDbContext` واستعلام `SELECT 1;`.
     - يعيد `200 OK` (`Healthy`) عند نجاح الاتصال، ويعيد `503 Service Unavailable` (`Unhealthy`) عند فشل الاتصال أو محاولة استخدام حساب `postgres` الإداري.
     - لا يكشف أبدًا كلمة المرور أو اسم المستخدم أو المنفذ أو تفاصيل المضيف الداخلي عند الفشل، وفي بيئة `Production` يعيد فقط `{ "status": "Healthy" | "Unhealthy", "timestampUtc": "..." }`.
5. **إنشاء قاعدة اختبار معزولة (`api_governance_test`) واختبارات التكامل:**
   - إنشاء قاعدة بيانات اختبار محلية معزولة `api_governance_test` مملوكة لحساب التطبيق غير الإداري `api_gov_app` (`rolsuper = false`).
   - إضافة 6 اختبارات جديدة في `DatabaseConnectivityIntegrationTests.cs` تغطي النجاح والفشل ومنع الحساب الإداري وعدم تسريب البيانات الحساسة.

---

## 2. الملفات المنشأة والمعدلة
- `backend/src/Shared/ApiGovernance.Shared/ApiGovernance.Shared.csproj` (محدث بإضافة `EF Core 10.0.15` و`Npgsql 10.0.4`)
- `backend/src/Shared/ApiGovernance.Shared/Persistence/DatabaseConnectionResolver.cs` (جديد)
- `backend/src/Shared/ApiGovernance.Shared/Persistence/ApiGovernanceDbContext.cs` (جديد)
- `backend/src/Shared/ApiGovernance.Shared/Persistence/DatabaseConnectivityProbe.cs` (جديد)
- `backend/src/ApiGovernance.Host/Program.cs` (محدث بتسجيل طبقة البيانات وفحص صحة `database` واستجابة `503` عند الفشل)
- `backend/src/ApiGovernance.Host/appsettings.json` (محدث بمفتاح اتصال فارغ آمن `""`)
- `tests/backend/ApiGovernance.Backend.Tests/Integration/BackendContractIntegrationTests.cs` (محدث للربط بقاعدة الاختبار المعزولة `api_governance_test` والتحقق من فحص `database`)
- `tests/backend/ApiGovernance.Backend.Tests/Integration/DatabaseConnectivityIntegrationTests.cs` (جديد — 6 اختبارات)
- `backend/README.md` و`README.md` و`docs/delivery/S01_status.md` (محدثة)
- `docs/delivery/F04_delivery.md` (هذا الملف)

---

## 3. نتائج بوابة الجودة والاختبارات الآلية (`tests/run-quality-gate.ps1`)
- **`[1/6]` ESLint (`npm run lint`):** ناجح (`0 errors, 0 warnings`).
- **`[2/6]` TypeScript (`npm run typecheck`):** ناجح (`0 errors`).
- **`[3/6]` اختبارات الواجهة الآلية (`Vitest`):** **9 / 9 ناجحة (`0 فاشل`)**.
- **`[4/6]` بناء الواجهة الإنتاجي (`npm run build`):** ناجح.
- **`[5/6]` بناء حل الخادم (`dotnet build -c Release --warnaserror`):** ناجح (`0 Warning(s), 0 Error(s)`).
- **`[6/6]` اختبارات وحدات وتكامل الخادم (`xUnit`):** **34 / 34 ناجحة (`0 فاشل`)** (ارتفاع من 28 إلى 34 اختبارًا).
- **فحص ثغرات NuGet (`dotnet list package --vulnerable --include-transitive`):** **`0 vulnerable packages`**.
- **إجمالي الاختبارات الآلية في المشروع:** **43 اختبارًا آليًا ناجحًا (`9 واجهة + 34 خادم`)، `0 فاشل`**.

