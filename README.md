# API Governance | منصة حوكمة وثائق APIs والتكاملات

الحالة: **مكتمل تقنيًا 100% (`S01`–`S14` — `59/59` مهمة) في `develop` وجاهز لقبول المستخدم النهائي (`Owner UAT`)** — يشمل جميع وحدات المنصة: الهوية والصلاحيات، البيانات المرجعية والتهيئة، وثائق API و`ACL` والتكامل `1:1`، مخزن الملفات الخاص وفحص `ClamAV` وإصدارات API والمراجعات وWord PoC، محرك الاعتماد المتسلسل وطلبات التغيير والإشعارات، النشر الانتقائي وبوابة المستهلك الخارجي، سجل التدقيق والبحث واللوحة التكيّفية (`P05`)، تحسينات الاستجابة (`U04`)، وحزمة الجودة والأمان والنشر لـ Windows/IIS (`Q01`–`Q07`). لا يُنشر للإنتاج قبل توقيع المالك في `docs/delivery/UAT_EXECUTION_GUIDE.md`.

## المكدس المعتمد
React 19 + TypeScript + Vite؛ ASP.NET Core 10 LTS؛ PostgreSQL 18؛ EF Core 10 + Npgsql 10؛ ASP.NET Core Identity؛ Open XML SDK؛ Windows Server + IIS (التشغيل المؤسسي بالتنسيق مع IT).

## بنية المستودع
- `frontend/`: واجهة الويب React 19 + TypeScript + Vite ومكتبة مكونات UI المشتركة وواجهات المصادقة والإدارة (`UI-24`, `UI-15`/`UI-15A`, `UI-25`) والبيانات المرجعية والإعدادات (`UI-01`..`UI-06`, `UI-23`) ووثائق API وصلاحياتها (`UI-21`, `UI-07`, `UI-21A`) والتكاملات بتبويباتها وإصداراتها وملفاتها (`UI-22`, `UI-08`, `UI-09`, `UI-10`, `UI-12`) والتعريب الموحد (`AR ⇄ EN`).
- `backend/`: خادم ASP.NET Core 10 LTS ووحداته العشر ضمن هيكل Modular Monolith وطبقة البيانات والترحيلات والمصادقة والتفويض المركزي وإدارة المستخدمين والأدوار والصلاحيات والبيانات المرجعية والتهيئة ووحدات `DocumentsModule` و`IntegrationsModule` و`VersionsFilesModule` (`V01`–`V04`).
- `tests/`: اختبارات الخادم (`xUnit` + `WebApplicationFactory` على قاعدة الاختبار المعزولة `api_governance_test`) وسكربت خط الفحص الآلي الموحد `run-quality-gate.ps1` (`F04`–`S08`).
- `scripts/`: سكربت تهيئة بيئة التطوير للجهاز الجديد `setup-dev-environment.ps1` وبيانات التطوير التجريبية النظيفة `seed-clean-dev-data.sql` (دون مستخدمين أو أسرار).
- `docs/baseline/`: حزمة المواصفات الأصلية v1.2، محفوظة دون تعديل.
- `docs/decisions/`: القرارات اللاحقة الحاكمة وحالة G3.
- `docs/delivery/`: سجل تسليم المهام وتقارير السبرنتات (`S01_closure_report.md`..`S08_closure_report.md`, `PROJECT_HANDOVER_STATUS.md`, `S02_DB_readiness.md`, `F04_delivery.md`, `F05_delivery.md`, `I01_delivery.md`..`I04_delivery.md`, `S01_status.md`).
- `AGENTS.md`: ضوابط وكلاء البرمجة.

## بدء العمل محليًا وتشغيل المكونات والاختبارات
1. لتشغيل خط الفحص الآلي الموحد (`Lint + TypeCheck + Tests + Build` للواجهة والخادم — 83 اختبارًا آليًا):
   ```powershell
   cd C:\api_asp\API_Governance_F01
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\run-quality-gate.ps1
   ```
2. لتشغيل واجهة الويب (مع proxy `/api` الموحد إلى الخادم الخلفي):
   ```powershell
   cd C:\api_asp\API_Governance_F01\frontend
   npm run dev
   ```
   العنوان: `http://127.0.0.1:5173/`
3. لتشغيل الخادم الخلفي:
   ```powershell
   cd C:\api_asp\API_Governance_F01\backend
   dotnet run --project src\ApiGovernance.Host\ApiGovernance.Host.csproj --launch-profile http
   ```
   - نقطتا فحص الصحة: `http://127.0.0.1:5080/health` و`http://127.0.0.1:5080/api/v1/health`
   - وثيقة OpenAPI (في بيئة التطوير): `http://127.0.0.1:5080/openapi/v1.json`

## التجهيز السريع على جهاز جديد واستكمال العمل
1. استنسخ المستودع وانتقل إلى `develop`، ثم شغّل سكربت التجهيز الآمن (ينشئ قاعدة البيانات والأسرار محليًا والبيانات التجريبية النظيفة وأول مدير جديد دون نقل أي أسرار أو قواعد فعلية):
   ```powershell
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\setup-dev-environment.ps1 -SeedDemoData -BootstrapAdminEmail "admin@api-governance.local" -BootstrapAdminName "System Administrator"
   ```
2. راجع [PROJECT_HANDOVER_STATUS.md](docs/delivery/PROJECT_HANDOVER_STATUS.md) و[AGENTS.md](AGENTS.md) و[S01_status.md](docs/delivery/S01_status.md) للتفاصيل الكاملة ومهام السبرنت القادم (**Sprint S09** بعد اعتماد S08).

## الفروع وبيئة التشغيل والأسرار
`main` خط الأساس المحلي؛ `develop` فرع التكامل والتطوير. تُحفظ سلاسل الاتصال ومفاتيح `Data Protection` خارج المستودع عبر `.NET User Secrets` ومتغيرات البيئة دون تخزين أسرار في Git أو `Web Storage`.
