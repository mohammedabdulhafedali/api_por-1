# تقرير جاهزية بيئة قاعدة البيانات المحلية — Sprint S02

- **التاريخ:** 24 نوفمبر 2026
- **الحالة:** مكتملة وجاهزة لبدء المهمة `F04` بعد اعتماد المالك (`Ready for F04 Approval`)
- **النطاق المنفذ:** تجهيز بيئة `PostgreSQL 18` المحلية فقط (دون بدء برمجة `F04` أو `F05` أو `I01`).

---

## 1. التحقق من المثبت الرسمي (`PostgreSQL 18.7-2`)
- **المصدر الرسمي (EnterpriseDB / postgresql.org):**
  `https://get.enterprisedb.com/postgresql/postgresql-18.7-2-windows-x64.exe`
- **حجم المثبت:** `418,909,304` بايت
- **البصمات الرقمية المتحققة:**
  - `SHA256`: `6931C6D04E57C2440BC551365958DB91E79B2B041F4F221ADC300A8069BB52D2`
  - `SHA512`: `564B583908C4630446F7C89157A9ADAB843EA17BB4BE4A2BC57B06184FDE3156F76969D109667A17879BC57238E2EF69408A113D57BEE9FFD94A3B8E7FFFEAC9`
- **التوقيع الرقمي (`Authenticode Signature`):**
  - الحالة (`Status`): `Valid`
  - الموقّع (`Subject`): `CN=EnterpriseDB Corporation, O=EnterpriseDB Corporation, L=Wilmington, S=Delaware, C=US`
  - جهة الإصدار (`Issuer`): `CN=DigiCert Trusted G4 Code Signing RSA4096 SHA384 2021 CA1, O="DigiCert, Inc.", C=US`
  - بصمة الشهادة (`Thumbprint`): `7BEDD1269FCCF7A5D95F18274750B79893C06C70`
- **نتيجة التثبيت الصامت:** `ExitCode = 0` (`PostgreSQL 18.7 on x86_64-windows` في المسار `C:\Program Files\PostgreSQL\18`).

---

## 2. تقييد الوصول المحلي (`127.0.0.1` فقط) وتشغيل الخدمة
1. **إعداد `postgresql.conf`:**
   - تم ضبط `listen_addresses = '127.0.0.1'` بحيث لا يستمع المحرك على أي واجهة شبكة خارجية (`0.0.0.0` أو `::`).
2. **إعداد `pg_hba.conf`:**
   - تم تقييد قواعد المصادقة لتسمح فقط بالاتصال من `127.0.0.1/32` باستخدام تشفير `scram-sha-256` ورفض ما عداه.
3. **حالة خدمة Windows ومقبس الشبكة (`Get-Service` & `Get-NetTCPConnection`):**
   - الخدمة: `postgresql-x64-18` — الحالة: `Running` — نوع البدء: `Automatic`.
   - المنفذ المفتوح: `LocalAddress = 127.0.0.1`, `LocalPort = 5432`, `State = Listen` (لا يوجد أي استماع على `0.0.0.0` أو شبكة خارجية).

---

## 3. قاعدة البيانات المخصصة وحساب التطبيق الأقل صلاحية (`Least-Privilege Role`)
- **اسم قاعدة البيانات المحلية:** `api_governance_dev` (`ENCODING = 'UTF8'`)
- **حساب التطبيق المخصص:** `api_gov_app` (لا يُستخدم حساب `postgres` الإداري لتشغيل التطبيق)
- **ضوابط الصلاحيات المطبقة على `api_gov_app` و`api_governance_dev`:**
  - `NOSUPERUSER` (`rolsuper = f`)
  - `NOCREATEDB` (`rolcreatedb = f`)
  - `NOCREATEROLE` (`rolcreaterole = f`)
  - `NOINHERIT`, `NOREPLICATION`, `CONNECTION LIMIT 50`
  - `REVOKE ALL ON DATABASE api_governance_dev FROM PUBLIC;`
  - `REVOKE ALL ON SCHEMA public FROM PUBLIC;`
  - منح `CONNECT, TEMPORARY` على `api_governance_dev` وملكية المخطط `public` لحساب `api_gov_app` فقط لتمكينه من تطبيق ترحيلات `EF Core 10` (`F04/F05`) دون أي صلاحيات إدارية على مستوى الخادم.

---

## 4. حفظ بيانات الاتصال خارج Git
- تم تهيئة `.NET User Secrets` للمضيف `ApiGovernance.Host` بالمعرف `ApiGovernance.Host.LocalDev` وحفظ سلسلة الاتصال تحت المفتاح:
  - `ConnectionStrings:DefaultConnection` (مخزنة في `%APPDATA%\Microsoft\UserSecrets\ApiGovernance.Host.LocalDev\secrets.json` خارج المستودع).
- تم كذلك حفظ متغير البيئة على مستوى المستخدم (`User Environment Variable`):
  - `API_GOVERNANCE_DB_CONNECTION`
- **لا توجد أي كلمة مرور أو سلسلة اتصال داخل أي ملف متتبع في Git.**

---

## 5. دليل التحقق الفعلي من الاتصال بحساب `api_gov_app`
```text
                         postgres_version                          |   database_name    | connected_user | server_addr | server_port 
-------------------------------------------------------------------+--------------------+----------------+-------------+-------------
 PostgreSQL 18.7 on x86_64-windows, compiled by msvc-19.44.35223, 64-bit | api_governance_dev | api_gov_app    | 127.0.0.1   |        5432

   rolname   | rolsuper | rolcreatedb | rolcreaterole | rolcanlogin | rolconnlimit 
-------------+----------+-------------+---------------+-------------+--------------
 api_gov_app | f        | f           | f             | t           |           50

 listen_addresses 
------------------
 127.0.0.1
```

