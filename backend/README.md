# backend — الخادم الخلفي (ASP.NET Core 10 LTS — Modular Monolith)

الحالة: **Sprint S08 مكتمل (`V01`–`V04`)** — تأسيس حل `ASP.NET Core 10 LTS` ببنية `Modular Monolith` (F03)، وعقود API v1 و`Problem Details` ثنائية اللغة (`Accept-Language`) (F07/U01)، و`PostgreSQL 18.7` + `EF Core 10` (F04/F05)، والمصادقة والمستخدمين والأدوار ومصفوفة الصلاحيات والتفويض المركزي (I01–I05)، والبيانات المرجعية والتهيئة (`MasterDataModule`, `ConfigurationModule` M01–M07)، ومحرك صلاحيات كل وثيقة `IDocumentAuthorizationService` (`I07` / `ADR-01`) في `ApiGovernance.Shared.Security` ووحدات `DocumentsModule` (D01–D03, I07, U02) و`IntegrationsModule` (D04–D06) و`VersionsFilesModule` (`V01`–`V04` للتخزين الخاص، والتحقق، والفحص الآمن `Fail-Closed` عبر `ClamAV`/`CLI`، وإصدارات API اليدوية). لا يبدأ `Sprint S09` قبل الاعتماد.

## هيكل الحل (`ApiGovernance.sln`)
- `Directory.Build.props`: إعدادات البناء الموحدة (`net10.0`, `Nullable=enable`, `TreatWarningsAsErrors=true`).
- `src/Shared/ApiGovernance.Shared`:
  - `Modularity/IBackendModule.cs`: عقود تسجيل وحدات الـ `Modular Monolith`.
  - `Contracts/*`: معيار عقود API v1، `PagedResult<T>` و`PaginationHelper`، معالجة الأخطاء `RFC 9457 Problem Details` ثنائية اللغة، ومعرفات التتبع `X-Correlation-Id`.
  - `Security/CentralServerAuthorization.cs` و`DocumentAuthorization.cs` و`AntiforgeryEndpointFilter.cs`: التفويض العام المركزي `IServerAuthorizationService` والتفويض على مستوى كل وثيقة `IDocumentAuthorizationService` وفق `ADR-01` وتدقيق `OP-01` ومنع IDOR، ومرشح التحقق المشترك لرمز `CSRF`.
  - `Persistence/Entities/*` و`Configurations/BaselineSchemaConfiguration.cs`: نماذج البيانات الـ 43 المعتمدة + جدول جلسات التذاكر `auth_sessions`.
  - `Persistence/Migrations/`: ترحيلا `InitialBaselineSchema` و`AddIdentityAuthSessions`.
  - `Persistence/DatabaseConnectionResolver.cs` و`DatabaseConnectivityProbe.cs`: فصل اتصال التشغيل عن الترحيلات وفحص الصلاحيات الفعلية في PostgreSQL.
- `src/Modules/IdentityAccess`: وحدة الهوية والمصادقة وإدارة المستخدمين والأدوار والصلاحيات (`PlatformUserStore`, `PostgreSqlSessionTicketStore`, `LocalIdentityAuthService`, `UserManagementService`, `RoleManagementService`, `PermissionManagementService`, `InitialAdminBootstrapService`, `IAuthTokenDeliveryChannel`) ونقاط `/api/v1/auth/*` و`/api/v1/me` و`/api/v1/users*` و`/api/v1/roles*` و`/api/v1/permissions`.
- `src/Modules/MasterData`: وحدة البيانات المرجعية (`MasterDataService`, `MasterDataModule`) ونقاط `/api/v1/company-systems*`, `/api/v1/internal-providers*`, `/api/v1/external-providers*`, `/api/v1/consumer-categories*`, `/api/v1/consumers*`, `/api/v1/api-groups*`.
- `src/Modules/Configuration`: وحدة الإعدادات والحوكمة (`FileTypeManagementService`, `CommonFileTypeCatalog`, `RequirementTemplateService`, `ApprovalRouteAndChangeConfigService`, `ConfigurationModule`) ونقاط `/api/v1/file-types*`, `/api/v1/requirement-templates*`, `/api/v1/approval-routes*`, `/api/v1/change-types*`, `/api/v1/change-policies*`.
- `src/Modules/Documents`: وحدة وثائق API ومصفوفة الوصول (`DocumentManagementService`, `DocumentsModule`) ونقاط `/api/v1/api-documents*` (`/developers`, `/access`).
- `src/Modules/Integrations`: وحدة التكاملات 1:1 والمطورين والتفاصيل (`IntegrationManagementService`, `IntegrationsModule`) ونقاط `/api/v1/integrations*` (`/developers`, `assignedToMe`).
- `src/Modules/VersionsFiles`: وحدة التخزين الخاص والحجر والفحص الأمني وإصدارات API والملفات (`LocalPrivateFileStore`, `FileUploadContentValidator`, `ConfiguredFileSecurityScanner`, `VersionsFilesService`, `VersionsFilesModule`) ونقاط `/api/v1/integrations/{id}/versions`, `/files-workspace`, `/versions/{id}/revisions`, `/revisions/{id}/files*`, `/files/{id}/scan-status`.
- `src/ApiGovernance.Host`: تطبيق مضيف `ASP.NET Core 10` الرئيسي (`UserSecretsId = ApiGovernance.Host.LocalDev`) مع حماية افتراضية لمسارات `/api/v1` عبر `RequireAuthorization()` وأمر `bootstrap-admin` لمرة واحدة.

## إعدادات الاتصال والأسرار وإنشاء أول حساب إداري (خارج Git)
1. **تشغيل التطبيق (`api_gov_app`) وتنفيذ الترحيلات (`api_gov_migrator`):** تُحفظ سلاسل الاتصال خارج Git في `.NET User Secrets` أو متغيرات البيئة مع حظر حساب `postgres`.
2. **مفاتيح Data Protection:** تُحفظ تلقائيًا خارج المستودع في `%LOCALAPPDATA%\ApiGovernance\DataProtectionKeys` (أو المسار المحدد في `AuthenticationPolicy:DataProtectionKeyDirectory`).
3. **إنشاء أول حساب إداري بأمان (لمرة واحدة فقط عند فراغ صلاحيات الإدارة):**
   ```powershell
   dotnet run --project src\ApiGovernance.Host\ApiGovernance.Host.csproj -- bootstrap-admin --email admin@company.local --full-name "System Administrator"
   ```
   *(أو بإرفاق `--password-env <ENV_VAR>` لتمرير كلمة مرور أولية من متغير بيئة خارج سجل الأوامر دون تخزين أسرار في Git).*
