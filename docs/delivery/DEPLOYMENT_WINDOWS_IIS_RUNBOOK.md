# دليل النشر والتشغيل والنسخ الاحتياطي على Windows Server + IIS — Task Q06

## 1. المتطلبات المسبقة على الخادم (Prerequisites)
1. **نظام التشغيل وخادم الويب:** Windows Server 2022 / 2019 مع تفعيل دور **IIS (Web Server)** وتثبيت **ASP.NET Core 10.0 Hosting Bundle**.
2. **قاعدة البيانات:** PostgreSQL 18.x (مقيّد على الشبكة الداخلية فقط مع TLS وحسابين منفصلين: `api_gov_migrator` للترحيل فقط و`api_gov_app` للتشغيل الأقل صلاحية).
3. **الفاحص الأمني للملفات:** خادم **ClamAV (`clamd`)** عبر TCP (`3310`) أو محرك الفحص المؤسسي المعتمد. لا يُسمح بتعطيل الفحص الأمني في بيئة الإنتاج.
4. **بناء الواجهة الأمامية (`frontend/dist`):** Node.js 22+ أثناء مرحلة البناء فقط لتوليد مخرجات ثابتة تُنسخ إلى `wwwroot` أو تُقدّم عبر IIS Static Files مع قواعد توجيه SPA.

---

## 2. فصل المجلدات والتخزين والأسرار (Strict Security Separation)
- **مجلد تطبيق الويب (IIS Physical Path):** مثل `C:\Services\ApiGovernance\app` (يحتوي مخرجات `dotnet publish` و`web.config` وملفات الواجهة المبنية، دون أي كلمات مرور أو أسرار داخل الكود).
- **مخزن الملفات الخاص (خارج `wwwroot` بالكامل):** مثل `D:\SecureStorage\ApiGovernance\PrivateStorage` (يضم `objects/`, `quarantine/`, `staging/`).
  - يُمنح حساب `IIS AppPool\ApiGovernancePool` صلاحية القراءة/الكتابة على `PrivateStorage` و`DataProtectionKeys` فقط.
  - يُمنع ربط `PrivateStorage` كدليل افتراضي (Virtual Directory) داخل IIS نهائيًا؛ كل التنزيلات تتم حصريًا عبر نقاط `/api/v1/.../download` مع `SHA-256` و`X-Content-Type-Options: nosniff`.
- **حماية الأسرار:** تُمرر سلاسل الاتصال وإعدادات ClamAV وData Protection عبر متغيرات بيئة النظام (Machine/AppPool Environment Variables) أو خزنة الأسرار المؤسسية، وليس داخل `appsettings.json` أو `web.config`.

---

## 3. خطوات النشر لأول مرة والترقيات (Deployment Procedure)
```powershell
# 1) بناء الواجهة الأمامية
cd frontend
npm ci --ignore-scripts
npm run build

# 2) نشر الخادم وإدراج ملف web.config المعتمد
cd ..\backend
dotnet publish src\ApiGovernance.Host\ApiGovernance.Host.csproj -c Release -o C:\Publish\ApiGovernance

# 3) تطبيق ترحيلات قاعدة البيانات باستخدام حساب المهاجر المحدود المدة
$env:ConnectionStrings__DefaultConnection="Host=127.0.0.1;Port=5432;Database=api_governance_prod;Username=api_gov_migrator;Password=<MIGRATOR_SECRET>"
dotnet ef database update --project src\Shared\ApiGovernance.Shared\ApiGovernance.Shared.csproj --startup-project src\ApiGovernance.Host\ApiGovernance.Host.csproj
```

---

## 4. التحقق من صحة النشر (Post-Deploy Smoke Checks)
- تحقق من تفعيل `FileScanning:Provider = "ClamAv"` و`FileStorage:MaxUploadBytesCeiling = 104857600` (مطابق لـ `maxAllowedContentLength="104857600"` في [web.config](file:///C:/api_asp/API_Governance_F01_CleanGitHub/backend/src/ApiGovernance.Host/web.config)).
- تحقق من نقطة الجاهزية: `GET /api/v1/health` يجب أن يُعيد `status: Healthy` وفحص قاعدة البيانات `Healthy`.
- أنشئ أول مدير نظام حصريًا عبر أمر CLI لمرة واحدة `bootstrap-admin` (دون تضمين أي حسابات افتراضية في البناء).

---

## 5. النسخ الاحتياطي والاستعادة وتمرير الصيانة (Backup & Rollback)
1. **النسخ الاحتياطي اليومي المتزامن:**
   - قاعدة البيانات عبر `pg_dump -F c -b` (مؤتمت في السكربت [verify-backup-recovery.ps1](file:///C:/api_asp/API_Governance_F01_CleanGitHub/scripts/verify-backup-recovery.ps1)).
   - نسخ `PrivateStorage/objects` بالتوازي للحفاظ على تطابق `file_objects.storage_key` مع البصمات `sha256_hex`.
2. **خطة التراجع الآمن (Rollback Plan):**
   - إيقاف Pool في IIS ← استعادة مخرجات الإصدار السابق + نسخة القاعدة ما قبل الترقية إذا شمل الإصدار تغييرات مخطط ← إعادة التشغيل والتحقق من `/api/v1/health` وسجل التدقيق.

