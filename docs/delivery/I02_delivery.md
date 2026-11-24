# تقرير تسليم المهمة I02 — Sprint S02

- **المهمة:** `I02` — إدارة المستخدمين وحالات التفعيل وإنشاء أول حساب إداري بطريقة آمنة
- **الفرع:** `task/I02` → مدمج في `develop`
- **التاريخ:** 24 نوفمبر 2026
- **الحالة:** مكتملة ومحققة ميدانيًا ومدمجة في `develop` (ضمن نطاق MVP فقط)

---

## 1. ملخص ما تم تنفيذه في I02 (نطاق MVP)

1. **الإنشاء التأسيسي الآمن لأول حساب إداري (`InitialAdminBootstrapService`):**
   - بذر قاموس الصلاحيات الأساسي لطبقة الهوية (`users.view`, `users.create`, `users.edit`, `users.assign_roles`, `users.password.reset`, `roles.view`, `roles.create`, `roles.edit`, `permissions.view`, `permissions.manage`, `audit_events.view`) والدور المثبت `"مدير النظام / SystemAdmin"` (`IsPinned = true`) عبر `role_permissions` دون أي صلاحيات ثابتة باسم الدور.
   - تنفيذ أمر CLI التشغيلي الآمن:
     `dotnet run --project backend/src/ApiGovernance.Host/ApiGovernance.Host.csproj -- bootstrap-admin --email <email> --full-name "<name>" [--password-env <ENV_VAR>]`
     بالإضافة إلى التفعيل الاختياري المحجوز عبر `BootstrapAdmin` خارج Git (`User Secrets` / متغيرات البيئة فقط).
   - **ضوابط الأمان:** يرفض التكرار تلقائيًا (`BOOTSTRAP_ALREADY_INITIALIZED`) فور وجود أي حساب يحمل صلاحية `users.create`، ولا يضع أي كلمة مرور افتراضية في الكود أو المستودع، ويدعم التفعيل بالرمز أحادي الاستخدام عبر `IAuthTokenDeliveryChannel` أو كلمة مرور مشفرة عبر متغير بيئة، مع تسجيل `BOOTSTRAP_ADMIN_CREATED` في `audit_events`.
2. **واجهات برمجة إدارة المستخدمين (`IdentityAccessModule` + `UserManagementService`):**
   - `GET /api/v1/users`: يتطلب `users.view`، يدعم البحث (`search`) والتصفية (`isActive`) والترقيم (`page`, `pageSize`) دون تسريب `PasswordHash` أو `SecurityStamp`.
   - `POST /api/v1/users`: يتطلب `users.create` و`CSRF` (و`users.password.reset` عند طلب إرسال رمز التفعيل فورًا)؛ ينشئ الحساب بدون كلمة مرور مسبقة (`PasswordHash = null`)، يمنع تكرار البريد (`409 Conflict`)، يتحقق من وجود وتفعيل `ConsumerEntityId` (`422`)، ويسجل `USER_CREATED`.
   - `PATCH /api/v1/users/{userId}`: يتطلب `users.edit` و`CSRF`؛ يدعم تعديل الاسم وحالة التفعيل ونطاق الجهة المستهلكة، ويمنع تعطيل الحساب الشخصي الحالي أو آخر حساب فعّال يملك صلاحية `users.edit` (`422`)، وعند التعطيل يدوّر `SecurityStamp` ويبطل جميع جلسات المستخدم في `auth_sessions` فورًا مع تسجيل `USER_DEACTIVATED` / `USER_ACTIVATED` / `USER_UPDATED`.
3. **إزالة التكرار المعماري:** استخراج [IdentityAccessAuthorizationHelper.cs](file:///C:/api_asp/API_Governance_F01/backend/src/Modules/IdentityAccess/Services/IdentityAccessAuthorizationHelper.cs) لتوحيد التحقق الفعلي من المستخدم النشط وحساب صلاحيات `ADR-01` وتسجيل التدقيق `OP-01` بين `I01` و`I02`.
4. **واجهة إدارة المستخدمين (`UI-15`):**
   - تنفيذ [UsersManagementPage.tsx](file:///C:/api_asp/API_Governance_F01/frontend/src/components/users/UsersManagementPage.tsx) و[usersApi.ts](file:///C:/api_asp/API_Governance_F01/frontend/src/api/usersApi.ts) وربطهما بقسم `الإدارة -> المستخدمون` في `App.tsx`:
     - جدول البيانات مع البحث والتصفية والترقيم وحالات التحميل والفراغ والأخطاء.
     - صفحات الإضافة والتعديل كصفحات مستقلة (`StandaloneFormPage`) لا كـ Modal.
     - حوارات التأكيد للتعطيل/التفعيل/إرسال رمز التفعيل ورسائل الحالة عبر `CentralModalDialog`.

---

## 2. نتائج الاختبارات وبوابة الجودة (`tests/run-quality-gate.ps1`)

- **اختبارات الخادم الجديدة ([UserManagementIntegrationTests.cs](file:///C:/api_asp/API_Governance_F01/tests/backend/ApiGovernance.Backend.Tests/Integration/UserManagementIntegrationTests.cs)):** تغطي البذر التأسيسي لمرة واحدة، ومنع تكرار التأسيس الإداري، والتفعيل، والتحقق من الصلاحيات (`401`/`403`/`409`/`422`)، والبحث والتصفية، وحماية آخر حساب إداري، والإبطال الفوري لجلسات الحساب المعطل ومنع إعادة دخوله ثم إعادة تفعيله، وتسجيل أحداث `OP-01`.
- **اختبارات الواجهة الجديدة ([UsersManagement.test.tsx](file:///C:/api_asp/API_Governance_F01/frontend/src/__tests__/UsersManagement.test.tsx)):** تغطي حجب الشاشة دون `users.view`، وعرض القائمة، وصفحة الإضافة المستقلة، وCSRF، وتأكيد التعطيل عبر `CentralModalDialog`.
- **نتيجة بوابة الجودة الشاملة:**
  - `ESLint` + `TypeScript` + بناء Vite الإنتاجي: ناجح (`0` أخطاء).
  - `Vitest`: **14 / 14 اختبارًا ناجحًا**.
  - `dotnet build -c Release --warnaserror`: ناجح (`0 Warning(s), 0 Error(s)`).
  - `dotnet test -c Release`: **42 / 42 اختبارًا ناجحًا** — الإجمالي الكلي **`56 / 56` اختبارًا آليًا ناجحًا (`0` فاشل)**.
  - فحص ثغرات التبعيات (`npm audit` و`dotnet list ApiGovernance.sln package --vulnerable --include-transitive`): **`0` ثغرات**.

