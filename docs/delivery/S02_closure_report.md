# تقرير إغلاق السبرنت الثاني (Sprint S02) — منصة حوكمة وثائق APIs والتكاملات

- **تاريخ الإغلاق:** 24 نوفمبر 2026
- **الفرع التكاملي:** `develop`
- **المهام المشمولة:** `F04`, `F05`, `I01`, `I02` (بالإضافة إلى تجهيز بيئة `PostgreSQL 18.7` المحلية)
- **الحالة:** **مكتمل محليًا (4 / 4 مهام — 100%)** بانتظار اعتماد المالك لفتح `Sprint S03`

---

## 1. ملخص تنفيذ مهام Sprint S02

| المهمة | الوصف | الحالة النهائية | الدليل |
|---|---|---|---|
| **تحضير DB** | تثبيت `PostgreSQL 18.7-2` محليًا (`127.0.0.1:5432`)، وحفظ الأسرار في `User Secrets` خارج Git | مكتمل ومعتمد | [S02_DB_readiness.md](file:///C:/api_asp/API_Governance_F01/docs/delivery/S02_DB_readiness.md) |
| **F04** | ربط `ASP.NET Core 10` بـ `PostgreSQL 18` عبر `EF Core 10.0.15` و`Npgsql 10.0.4` وفحص الجاهزية الآمن | مكتملة ومعتمدة ومدمجة | [F04_delivery.md](file:///C:/api_asp/API_Governance_F01/docs/delivery/F04_delivery.md) |
| **F05** | مطابقة المخطط (`43` جدولًا، `77` علاقة `RESTRICT`، `17` قيد `CHECK`، `148` فهرسًا)، حماية `OP-01` الإلحاقية، وفصل صلاحيات التشغيل عن الترحيلات | مكتملة ومعتمدة ومدمجة | [F05_delivery.md](file:///C:/api_asp/API_Governance_F01/docs/delivery/F05_delivery.md) |
| **I01** | المصادقة المحلية عبر `ASP.NET Core Identity` وجدول `users`، جلسات `auth_sessions` القابلة للإبطال، حماية `CSRF`، سياسة كلمات المرور (`15+` خانة) والقفل (`5` محاولات / `429`)، تدوير الجلسة (`SEC-T-006`)، وتأجيل البريد المؤسسي عبر `IAuthTokenDeliveryChannel`، وواجهة `UI-24` | مكتملة ومعتمدة ومدمجة | [I01_delivery.md](file:///C:/api_asp/API_Governance_F01/docs/delivery/I01_delivery.md) |
| **I02** | إدارة المستخدمين (`GET/POST/PATCH /api/v1/users`)، الإبطال الفوري لجلسات المعطلين، التدشين الآمن لمرة واحدة لأول مدير (`bootstrap-admin` + تعطيله فور الاستخدام)، وواجهة `UI-15` | مكتملة ومحققة ميدانيًا ومدمجة | [I02_delivery.md](file:///C:/api_asp/API_Governance_F01/docs/delivery/I02_delivery.md) |

---

## 2. التحقق من معايير قبول Sprint S02

1. **الترحيل والتراجع في بيئة اختبار معزولة (`api_governance_test`):**
   - التحقق الآلي من دورة `MigrateAsync("0") → MigrateAsync() → MigrateAsync("0") → MigrateAsync()` لجميع الـ `44` جدولًا (`43` جدول أعمال + `auth_sessions`) و`78` مفتاحًا أجنبيًا ومشغلات `OP-01` دون المساس بقاعدة التطوير `api_governance_dev`.
2. **فصل صلاحيات قاعدة البيانات:**
   - التحقق الفعلي من `pg_roles` و`has_schema_privilege` لحساب التطبيق `api_gov_app` (`CREATE=f`) مقابل حساب الترحيلات `api_gov_migrator` (`CREATE=t`, `rolsuper=f`).
3. **سياسة كلمات المرور والقفل والجلسات وCSRF:**
   - فرض حد أدنى **15 حرفًا** مع أحرف كبيرة وصغيرة وأرقام ورموز خاصة و`4` أحرف فريدة على الأقل (مختبر برفض كلمة مرور من `14` حرفًا وقبول `15+` حرفًا على مستوى الخادم والواجهة).
   - حماية `POST` بـ `X-CSRF-TOKEN` (`Antiforgery`)، وتقييد المحاولات (`429`)، وتدوير معرف الجلسة عند إعادة الدخول (`SEC-T-006`).
4. **تعطيل آلية إنشاء أول مدير بعد الاستخدام الأول وتجهيز الحساب الإداري الأول:**
   - تم تدشين أول حساب إداري محلي (`admin@api-governance.local`, الدور `"مدير النظام / SystemAdmin"`) عبر `bootstrap-admin` مع حفظ كلمة المرور القوية خارج Git في `.NET User Secrets` دون كشفها في المحادثة أو السجلات.
   - التحقق من رفض أي محاولة لاحقة لتنفيذ `bootstrap-admin` أو `BootstrapFirstAdminAsync` (`BOOTSTRAP_ALREADY_INITIALIZED` — Exit Code `1`).
5. **الاختبار الفعلي على الخادم والواجهة الحيين (`http://127.0.0.1:5173/` ⇄ `http://127.0.0.1:5080`):**
   - تنفيذ اختبار حي في متصفح Edge Chromium (CDP): الدخول بالحساب الإداري الأول، الانتقال إلى `UI-15 المستخدمون`، إنشاء مستخدم جديد، تعديله، تعطيله عبر حوار التأكيد `CentralModalDialog`، ثم تسجيل الخروج والعودة إلى `UI-24`.
6. **بوابة الجودة النهائية وفحص ثغرات التبعيات:**
   - `ESLint` + `TypeScript` + بناء الإنتاج `Vite`: ناجح (`0` أخطاء).
   - `Vitest`: **14 / 14 اختبارًا ناجحًا**.
   - `dotnet build -c Release --warnaserror`: ناجح (`0 Warning(s), 0 Error(s)`).
   - `dotnet test -c Release`: **42 / 42 اختبارًا ناجحًا** — الإجمالي الكلي **`56 / 56` اختبارًا آليًا ناجحًا (`0` فاشل)**.
   - `npm audit` و`dotnet list ApiGovernance.sln package --vulnerable --include-transitive`: **`0` ثغرات**.

---

## 3. الدروس المستفادة والبنود المرحّلة (بدون تجاوز نطاق MVP)

- **الدرس 1:** عند استخدام `ITicketStore` مع `WebApplicationFactory` و`RateLimiter` يجب ربط خيارات السياسات عبر `IPostConfigureOptions` واستخدام `app.UseRouting()` صراحة قبل `app.UseRateLimiter()` لضمان سريان الإعدادات النهائية وحدود المعدل في كل البيئات.
- **الدرس 2:** في تدوير معرف الجلسة أثناء إعادة المصادقة (`SEC-T-006`)، يحتفظ `CookieAuthenticationHandler` بمعرف التذكرة ضمن الطلب، لذا فإن إبطال التذكرة السابقة وإصدار تذكرة جديدة عبر `PostgreSqlSessionTicketStore` يمنع إعادة استخدام المعرف السابق بأمان.
- **البنود المؤجلة ضمن حدود النطاق المعتمد:**
  - ربط قناة البريد المؤسسي الفعلي (`IAuthTokenDeliveryChannel`) مؤجل لحين اعتماد مواصفات SMTP/الهوية المؤسسية من IT.
  - مهام إسناد الأدوار المتعددة ومصفوفة الصلاحيات الكاملة وتفويض الخادم الشامل (`I03`–`I06`) محفوظة لـ **`Sprint S03`** بانتظار اعتماد إغلاق `Sprint S02`.

