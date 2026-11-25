# حالة المشروع ودليل النقل والاستكمال على جهاز جديد

- **المشروع:** منصة حوكمة وثائق APIs والتكاملات (`API_Governance_F01`)
- **آخر نقطة إغلاق:** **نهاية Sprint S14 (`S01`–`S14` — `59 / 59` مهمة = `100%` تقنيًا)**
- **الفرع التكاملي:** `develop`
- **الحالة الحالية:** **جميع المهام الـ `59/59` مكتملة ومحققة عبر بوابة الجودة الشاملة وفحوص الأمان؛ القبول الوظيفي اليدوي للمالك (`UAT`) وقرار النشر للإنتاج منفصلان بانتظار توقيع المالك في [UAT_EXECUTION_GUIDE.md](UAT_EXECUTION_GUIDE.md).**
- **تقارير الإغلاق النهائية:** `docs/delivery/S13_closure_report.md`, `docs/delivery/S14_closure_report.md`, `docs/delivery/Q05_security_audit_report.md`, `docs/delivery/DEPLOYMENT_WINDOWS_IIS_RUNBOOK.md`, `docs/delivery/UAT_EXECUTION_GUIDE.md`

---

## 1. جدول السبرنتات المنفذة والمتبقية (`59` مهمة إجمالًا)

### السبرنتات المنفذة (`S01`–`S12` — `50` مهمة)
| السبرنت | المهام | الحالة | التقرير المرجعي |
|---|---|---|---|
| **S01** | `F01`, `F02`, `F03`, `F06`, `F07`, `F08` | مكتمل ومعتمد | `docs/delivery/S01_closure_report.md` |
| **S02** | `F04`, `F05`, `I01`, `I02` | مكتمل ومعتمد | `docs/delivery/S02_closure_report.md` |
| **S03** | `I03`, `I04`, `I05`, `I06` | مكتمل ومعتمد | `docs/delivery/S03_closure_report.md` |
| **S04** | `M01`, `M02`, `M03`, `M04` | مكتمل ومعتمد | `docs/delivery/S04_closure_report.md` |
| **S05** | `M05`, `M06`, `M07` | مكتمل ومعتمد | `docs/delivery/S05_closure_report.md` |
| **S06** | `D01`, `D02`, `I07`, `D03`, `D04`, `U02` | مكتمل ومعتمد | `docs/delivery/S06_closure_report.md` |
| **S07** | `D05`, `D06`, `U01`, `U03` | مكتمل ومعتمد | `docs/delivery/S07_closure_report.md` |
| **S08** | `V01`, `V02`, `V03`, `V04` | مكتمل ومعتمد | `docs/delivery/S08_closure_report.md` |
| **S09** | `V05`, `V06`, `V07`, `V08` | مكتمل ومعتمد | `docs/delivery/S09_closure_report.md` / `docs/delivery/V08_word_poc_report.md` |
| **S10** | `W01`, `W02`, `W03`, `W04` | مكتمل ومدمج | `docs/delivery/S01_status.md` |
| **S11** | `W05`, `W06`, `W07` | مكتمل ومدمج | `docs/delivery/S01_status.md` |
| **S12** | `P01`, `P02`, `P03`, `P04` | مكتمل ومدمج | `docs/delivery/S01_status.md` |
| **S13** | `P05`, `U04`, `Q01`, `Q02`, `Q03` | مكتمل ومدمج | `docs/delivery/S13_closure_report.md` |
| **S14** | `Q04`, `Q05`, `Q06`, `Q07` | مكتمل تقنيًا (بانتظار توقيع UAT للمالك قبل الإنتاج) | `docs/delivery/S14_closure_report.md` / `docs/delivery/UAT_EXECUTION_GUIDE.md` |

### وضع القبول النهائي (`Q07`)
- كافة السبرنتات الـ `14` (`59/59` مهمة) منجزة وموثقة وتجتاز البوابة الآلية، دون تجاوز قبول المالك أو إطلاق على الإنتاج.

---

## 2. دليل تجهيز البيئة على الجهاز الجديد (دون نقل أسرار أو قواعد فعلية)

يتبع الانتقال إلى الجهاز الجديد نهج **EF Core Migrations + بيانات تجريبية اصطناعية (`scripts/seed-clean-dev-data.sql`)** دون نقل أي مستخدمين أو بصمات كلمات مرور أو جلسات أو سجلات تدقيق قديمة:

1. **تثبيت المتطلبات:** `Git`، و`.NET 10 SDK`، و`Node.js LTS` (`>= 20`)، و`PostgreSQL 18.x` محليًا (`127.0.0.1:5432`)، واختياريًا `ClamAV clamd` أو أداة الفحص المعتمدة.
2. **الاستنساخ والوقوف على فرع التطوير:**
   ```powershell
   git clone <PRIVATE_REPO_URL> API_Governance_F01
   cd API_Governance_F01
   git checkout develop
   ```
3. **تشغيل سكربت التجهيز الآمن:**
   ```powershell
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\setup-dev-environment.ps1 -SeedDemoData -BootstrapAdminEmail "admin@api-governance.local" -BootstrapAdminName "System Administrator"
   ```
   يقوم السكربت تلقائيًا بما يلي:
   - إنشاء حسابي `api_gov_app` و`api_gov_migrator` بكلمات مرور عشوائية جديدة وتخزينها في `.NET User Secrets` فقط.
   - إنشاء قاعدتي `api_governance_dev` و`api_governance_test` وتطبيق ترحيلات EF Core.
   - تهيئة مجلدات `PrivateStorage` (`objects/`, `quarantine/`, `staging/`) و`DataProtectionKeys` خارج مجلدات الويب.
   - تعبئة بيانات مرجعية تجريبية اصطناعية خالية من الأسرار، وإنشاء أول حساب إداري محلي جديد بأمان (`>= 15` حرفًا).
   - تشغيل خط الفحص الآلي الموحد (`tests/run-quality-gate.ps1`).
4. **تشغيل الواجهة والخادم:**
   ```powershell
   # نافذة 1 (الخادم): http://127.0.0.1:5080
   cd backend
   dotnet run --project src\ApiGovernance.Host\ApiGovernance.Host.csproj --launch-profile http

   # نافذة 2 (الواجهة): http://127.0.0.1:5173
   cd frontend
   npm run dev
   ```
