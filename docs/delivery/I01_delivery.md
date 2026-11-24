# تقرير تسليم المهمة I01 — Sprint S02

- **المهمة:** `I01` — طبقة هوية وجلسة بخيار موفر هوية قابل للتبديل
- **الفرع:** `task/I01` → مدمج في `develop`
- **التاريخ:** 24 نوفمبر 2026
- **الحالة:** مكتملة ومعتمدة ومدمجة في `develop`

---

## 1. التحقق من اتساق GRANT / REVOKE ودمج F05

- تم فحص اتساق قيم القرار **`GRANT`** و**`REVOKE`** عبر جميع الطبقات قبل الدمج:
  - المواصفات المعتمدة (`Database_v0.1.zip`، و`08_DB_and_plan_delta.md`، و`03_AUTHZ_ADR.md`).
  - الكيانات (`UserPermissionOverrideEntity`, `DocumentRolePermissionEntity`, `DocumentUserPermissionEntity`).
  - تكوين `EF Core` ([BaselineSchemaConfiguration.cs](file:///C:/api_asp/API_Governance_F01/backend/src/Shared/ApiGovernance.Shared/Persistence/Configurations/BaselineSchemaConfiguration.cs)) والترحيل (`20261124080348_InitialBaselineSchema.cs`).
  - قيود PostgreSQL الفعلية (`pg_constraint`: `ck_user_permission_overrides_decision`, `ck_document_role_permissions_decision`, `ck_document_user_permissions_decision` — جميعها `CHECK (decision IN ('GRANT', 'REVOKE'))`).
  - اختبارات التكامل (`DatabaseConnectivityIntegrationTests.cs`).
- دُمجت `F05` بنجاح في `develop` (`71bbc17`) وأُنشئ فرع `task/I01`.

---

## 2. المصادقة المحلية وإدارة الجلسات والسياسات المعتمدة (نطاق MVP)

1. **تكامل `ASP.NET Core Identity` مع جدول `users` الحالي دون ازدواجية:**
   - إنشاء مخزن مستخدمين مخصص `PlatformUserStore` (`IUserPasswordStore`, `IUserEmailStore`, `IUserSecurityStampStore`, `IUserLockoutStore`) مربوط مباشرة بجدول `users` (`UserEntity`) عبر `ApiGovernanceDbContext` (مع تجزئة كلمات المرور عبر `PasswordHasher` وتحديث `SecurityStamp`).
   - توفير واجهة `IIdentityAuthService` قابلة للتبديل (`Provider = "LocalIdentity"` في `AuthenticationPolicyOptions`) لتسهيل إضافة مزودي هوية مؤسسية مستقبلًا.
2. **مخزن جلسات خادمي قابل للإبطال (`PostgreSqlSessionTicketStore`):**
   - إضافة جدول التذاكر `auth_sessions` بترحيل `20261124082014_AddIdentityAuthSessions` لتخزين تذاكر Cookie Sessions على الخادم، وحفظ مفاتيح `Data Protection` خارج مستودع Git (`%LOCALAPPDATA%\ApiGovernance\DataProtectionKeys` أو المسار المحدد بالتكوين).
   - تطبيق التحقق الفوري عند كل طلب (`OnValidatePrincipal`): فحص `IsActive == true` ووجود `PasswordHash` ومطابقة `SecurityStamp` وعدم القفل وعدم الإبطال (`RevokedAt == null`) وصلاحية المدة (`ExpiresAt`, `AbsoluteExpiresAt`).
   - إبطال التذكرة فورًا عند تسجيل الخروج (`POST /api/v1/auth/logout`) وإبطال جميع جلسات المستخدم عند تغيير كلمة المرور أو إتمام إعادة التعيين.
3. **سياسات الأمان المطبقة (`AuthenticationPolicyOptions` في `appsettings.json`):**
   - **كلمات المرور:** حد أدنى `15` خانة، حرف كبير، حرف صغير، رقم، رمز خاص، و`4` أحرف فريدة على الأقل، مع منع مطابقة كلمة المرور الجديدة للحالية عند التغيير.
   - **القفل وتقييد المحاولات:** قفل تلقائي للحساب بعد `5` محاولات فاشلة متتالية لمدة `15` دقيقة (`429 Too Many Requests` مع تسجيل التدقيق)، مع رد موحد `401 Unauthorized` لمنع كشف وجود الحساب، وحماية إضافية عبر `RateLimiter` (`AuthStrictRateLimit`)، ورفض أي تسريب لأسرار أو كلمات مرور أو رموز.
   - **الجلسات وCSRF:** جلسة Cookie بخصائص `HttpOnly`, `SameSite=Lax`, `Secure` في `Production` على كل من Cookie الجلسة وCookie `Antiforgery`، ومهلة خمول `30` دقيقة وحد أقصى مطلق `480` دقيقة مضبوط في كل من التذكرة وقاعدة البيانات، وتدوير معرف الجلسة عند إعادة المصادقة (`SEC-T-006`)، وتحقق `IAntiforgery` (`X-CSRF-TOKEN`) إلزاميًا لكل عمليات التغيير، وتفعيل `app.UseRouting()` قبل `app.UseRateLimiter()` لضمان تطبيق حدود المحاولات (`429`).
   - **تفعيل الحسابات وإعادة تعيين كلمات المرور مع تأجيل البريد المؤسسي:** تنفيذ المسارين `POST /api/v1/users/{userId}/password/reset` (بإذن `users.password.reset` الصريح وفق أولوية `ADR-01` دون إعادة الرمز أو كلمة المرور في الرد `204`) و`POST /api/v1/auth/password/reset/complete` برمز أحادي الاستخدام ومحدد الغرض والمدة (`30` دقيقة)، مع تمرير التسليم عبر الواجهة القابلة للربط `IAuthTokenDeliveryChannel` (`DeferredEnterpriseEmailDeliveryChannel`).
4. **حماية المسارات والعقود (7 نقاط نهاية):**
   - فرض `.RequireAuthorization()` افتراضيًا على مجموعة `/api/v1` (عدا `/health`, `/api/v1/auth/csrf`, `/api/v1/auth/login`, `/api/v1/auth/password/reset/complete`, ونقاط التشخيص غير الإنتاجية).
   - جميع أخطاء `401`/`403`/`429` تعيد `RFC 9457 Problem Details` (`application/problem+json`) ثنائية اللغة دون تحويل HTML.
   - تسجيل أحداث التدقيق الإلحاقي (`OP-01`) لجميع عمليات الدخول والخروج والفشل والتفعيل وتغيير وإعادة تعيين كلمة المرور في `audit_events` خالية من الأسرار.

---

## 3. واجهة المستخدم (UI-24 وربط حماية المسارات في React)

- إنشاء صفحة `UI-24` الموحدة للدخول وتفعيل الحساب/إعادة التعيين (`LoginPage.tsx`) وفق الهوية البصرية المعتمدة (`#f8fafc`/`#1a2d45`، `Light`/`Dark`، `AR RTL`/`EN LTR`) مع **عدم وجود أي تسجيل ذاتي**.
- إنشاء صفحة تغيير كلمة المرور المستقلة (`ChangePasswordView.tsx` باستخدام `StandaloneFormPage`) وعناصر جلسة المستخدم والخروج في الشريط العلوي (`AppLayoutShell.tsx`).
- حماية مسارات الواجهة في `App.tsx` بالتحقق من `GET /api/v1/me` ومنع عرض لوحة العمل عند غياب الجلسة، مع عدم تخزين أي كلمات مرور أو رموز CSRF/جلسة في `localStorage` أو `sessionStorage`، وإضافة `/api` proxy في `vite.config.ts` لتوحيد الـ origin محليًا.

---

## 4. نتائج الاختبارات وبوابة الجودة (`tests/run-quality-gate.ps1`)

- **بوابة الجودة الموحدة:**
  - `ESLint` + `TypeScript` + بناء Vite الإنتاجي: ناجح (`0` أخطاء).
  - اختبارات الواجهة (`Vitest`): **12 / 12 ناجحة** (`9` مكونات UI + `3` مسارات المصادقة وحماية المسارات في `AuthFlow.test.tsx`).
  - بناء الخادم (`dotnet build -c Release --warnaserror`): ناجح (`0 Warning(s), 0 Error(s)`).
  - اختبارات الخادم (`xUnit` على `api_governance_test`): **41 / 41 ناجحة** (بما فيها اختبارات دورة الترحيل لـ 44 جدولًا، ومنع الوصول غير الموثق، وفحص CSRF، والتفعيل والرمز أحادي الاستخدام، وإبطال الجلسات عند الخروج والتغيير والتعطيل، والقفل `429`، وخاصية `Secure` في `Production`).
  - **إجمالي الاختبارات الآلية الناجحة:** **`53 / 53` (`0` فاشل)**.
- **فحص ثغرات الحزم:**
  - `npm audit`: **`0 vulnerabilities`**.
  - `dotnet list ApiGovernance.sln package --vulnerable --include-transitive`: **`0 vulnerable packages`**.

