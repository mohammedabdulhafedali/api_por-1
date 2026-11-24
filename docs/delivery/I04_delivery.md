# تقرير تسليم المهمة I04 — Sprint S03

- **المهمة:** `I04` — مصفوفة صلاحيات الأدوار والمنح والسحب الفردي للمستخدمين (`UI-25` و`UI-15A`)
- **الفرع:** `task/I04` (مبني على `develop` بعد دمج `task/I03` عند `aa97a6c` — جاهز للمراجعة والاعتماد قبل الدمج وبدء `I05`)
- **التاريخ:** 24 نوفمبر 2026
- **الحالة:** مكتملة ضمن نطاق MVP ومجتازة لبوابة الجودة الكاملة وفحوص التبعيات (دون بدء `I05`)

---

## 1. الالتزام بالضوابط المعمارية ونطاق MVP

1. **عدم حذف أي صلاحية من قاموس النظام:**
   - يظل جدول `permissions` قاموسًا دائمًا للقراءة فقط (`GET /api/v1/permissions`)، ولا توجد أي نقطة حذف أو تعطيل أو حذف سجلات منه عند تعديل الأدوار أو الاستثناءات.
2. **عدم منح أي امتيازات ثابتة باسم الدور (`ADR-01`):**
   - تستمد كل من مصفوفة الدور وصلاحيات المستخدم الفعلية من جداول `role_permissions` و`user_roles` (الأدوار النشطة فقط) و`user_permission_overrides` حصرًا.
3. **تطبيق قواعد أولوية الصلاحيات المعتمدة (`ADR-01 — القسم أ`):**
   - **`User REVOKE`** (سحب فردي صريح) يغلب دائمًا → منع.
   - يليه **`User GRANT`** (منح فردي صريح) → سماح.
   - وعند غياب استثناء فردي (حالة الموروث) → السماح إذا منحه **أي دور نشط** مسند للمستخدم؛ وإلا منع افتراضي.
4. **منع تصعيد الصلاحيات غير المصرح به وحماية الإدارة:**
   - يرفض الخادم (`403 Forbidden`) محاولة أي مستخدم يحمل `permissions.manage` منح صلاحية لدور أو لمستخدم فردي أو إزالة سحب فردي يستعيد صلاحية **لا يملكها الفاعل نفسه فعليًا**، مع تسجيل `ROLE_PERMISSIONS_ESCALATION_BLOCKED` أو `USER_OVERRIDES_ESCALATION_BLOCKED` في `audit_events`.
   - يمنع الخادم (`422 UnprocessableEntity`) إسقاط صلاحيات الإدارة الحرجة (`permissions.manage`, `permissions.view`, `roles.edit`, `users.assign_roles`, `users.edit`) عن آخر حساب إداري فعّال في النظام.
5. **إيقاف قبل `I05`:** لم يبدأ تفويض الوحدات الأخرى المركزي الخاص بـ `I05` بعد.

---

## 2. ما تم إنجازه في الخادم الخلفي (`backend`)

1. **قاموس المنصة وعقود المصفوفة ([PermissionManagementContracts.cs](file:///C:/api_asp/API_Governance_F01/backend/src/Modules/IdentityAccess/Contracts/PermissionManagementContracts.cs) و[InitialAdminBootstrapService.cs](file:///C:/api_asp/API_Governance_F01/backend/src/Modules/IdentityAccess/Services/InitialAdminBootstrapService.cs)):**
   - تعريف `PlatformPermissionCatalog` شاملًا لجميع الشاشات (24 صفًا ضمن 6 مجموعات وظيفية) والأعمدة الـ 11 المعتمدة (`view`, `create`, `edit`, `delete`, `manage`, `assign`, `reset_password`, `submit`, `decide`, `withdraw`, `download`) وجميع رموز الصلاحيات من `05_API_catalog_v1.2.csv` مع حفظ أي رموز سابقة دون حذف.
   - تعريف عقود `PermissionsCatalogResponse`, `RolePermissionsResponse`, `ReplaceRolePermissionsRequest`, `UserPermissionsMatrixResponse`, `UserPermissionEntryDto`, `ReplaceUserPermissionOverridesRequest`.
2. **خدمة المصفوفة والاستثناءات ([PermissionManagementService.cs](file:///C:/api_asp/API_Governance_F01/backend/src/Modules/IdentityAccess/Services/PermissionManagementService.cs)):**
   - `GetPermissionsCatalogAsync` (`permissions.view`): إرجاع القاموس الدائم وتخطيط الشاشات والإجراءات مع البذر التلقائي المتوازن لأي رمز قياسي ناقص دون حذف أي سجل قائم.
   - `GetRolePermissionsAsync` و`ReplaceRolePermissionsAsync` (`permissions.view` / `permissions.manage`): حفظ ذري داخل معاملة لجدول `role_permissions` مع فحص عدم تصعيد الصلاحيات، وحماية آخر مسار إداري فعّال، وتسجيل `ROLE_PERMISSIONS_UPDATED` (`addedCodes`, `removedCodes`).
   - `GetUserPermissionsMatrixAsync` و`ReplaceUserPermissionOverridesAsync` (`permissions.view` / `permissions.manage`): عرض المصادر الموروثة من الأدوار النشطة (`InheritedFromRoles`) والاستثناءات (`GRANT`/`REVOKE`/`INHERIT`) والقرار النهائي (`USER_REVOKE`, `USER_GRANT`, `ROLE_INHERITED`, `DENIED_DEFAULT`)، وحفظ الاستثناءات ذريًا في `user_permission_overrides` (مع حذف سجل الاستثناء عند الإرجاع إلى الموروث)، وفحص عدم تصعيد الصلاحيات، وتسجيل `USER_PERMISSION_OVERRIDES_UPDATED`.
3. **نقاط النهاية المحمية في [IdentityAccessModule.cs](file:///C:/api_asp/API_Governance_F01/backend/src/Modules/IdentityAccess/IdentityAccessModule.cs):**
   - `GET /api/v1/permissions` (`permissions.view`)
   - `GET /api/v1/roles/{roleId:guid}/permissions` (`permissions.view`)
   - `PUT /api/v1/roles/{roleId:guid}/permissions` (`permissions.manage` + `CSRF`)
   - `GET /api/v1/users/{userId:guid}/permissions` (`permissions.view`)
   - `PUT /api/v1/users/{userId:guid}/permission-overrides` (`permissions.manage` + `CSRF`)

---

## 3. ما تم إنجازه في واجهة المستخدم (`frontend`)

1. **عميل الصلاحيات وأدوات التحديد المجمع ([permissionsApi.ts](file:///C:/api_asp/API_Governance_F01/frontend/src/api/permissionsApi.ts) و[matrixSelection.ts](file:///C:/api_asp/API_Governance_F01/frontend/src/utils/matrixSelection.ts)):**
   - استدعاءات `getPermissionsCatalog`, `getRolePermissions`, `replaceRolePermissions`, `getUserPermissionsMatrix`, `replaceUserPermissionOverrides` مرتبطة بـ `CSRF` و`Problem Details`.
   - دوال مشتركة لتحديد/إلغاء تحديد الخلية والصف والعمود والقسم والكل.
2. **شاشة مصفوفة صلاحيات الدور (`UI-25` — [RolesManagementPage.tsx](file:///C:/api_asp/API_Governance_F01/frontend/src/components/roles/RolesManagementPage.tsx)):**
   - إضافة زر **«مصفوفة الصلاحيات / Permissions Matrix»** لكل دور في دليل الأدوار.
   - عرض المصفوفة الشاملة عبر `SelectionMatrix` (كل الشاشات في صفوف وكل الإجراءات في أعمدة دون قائمة لاختيار شاشة واحدة) مع تحديد الخلية/الصف/العمود/القسم/الكل، وتأكيد الحفظ عبر `CentralModalDialog`.
3. **شاشة الاستثناءات الفردية للمستخدم (`UI-15A` — [UserPermissionsMatrixPage.tsx](file:///C:/api_asp/API_Governance_F01/frontend/src/components/users/UserPermissionsMatrixPage.tsx) و[UsersManagementPage.tsx](file:///C:/api_asp/API_Governance_F01/frontend/src/components/users/UsersManagementPage.tsx)):**
   - إضافة زر **«الصلاحيات / Permissions»** في جدول المستخدمين لفتح صفحة مستقلة تعرض الأدوار النشطة المسندة، وملخصات الموروث/المنح/السحب/الفعّال، وبيان مصدر كل صلاحية، والتحكم الفردي (`موروث` / `منح فردي GRANT` / `سحب فردي REVOKE`)، وأزرار إرجاع الصف أو الكل إلى الحالة الموروثة، مع تأكيد الحفظ عبر `CentralModalDialog`.

---

## 4. نتائج الاختبارات وبوابة الجودة والأمان (`task-1254`)

1. **اختبارات التكامل للخادم ([PermissionManagementIntegrationTests.cs](file:///C:/api_asp/API_Governance_F01/tests/backend/ApiGovernance.Backend.Tests/Integration/PermissionManagementIntegrationTests.cs)) على PostgreSQL 18 المعزول (`api_governance_test`):**
   - التحقق من `GET /api/v1/permissions` (`401`/`403`/`200`) وبقاء عدد سجل قاموس الصلاحيات ثابتًا دون حذف أي صف بعد التعديلات، وفحص `CSRF`، ومنع تصعيد الصلاحيات (`403`) للمدير المفوض الذي لا يملك الصلاحية المطلوب منحها، وحماية آخر حساب إداري فعّال (`422`)، وتسجيل أحداث `OP-01`.
   - التحقق من أولوية `ADR-01` (`User REVOKE` يغلب وراثة الدور و`User GRANT` يمنح دون دور)، وانعكاس ذلك فوريًا في `/api/v1/me` ونقاط النهاية، ومنع إزالة سحب فردي يرفع صلاحية لا يملكها الفاعل (`403`)، ثم إعادة جميع الاستثناءات إلى الحالة الموروثة وحذف صفوف `user_permission_overrides` وعودة الصلاحيات الموروثة فوريًا.
2. **اختبارات الواجهة ([PermissionsMatrixAndOverrides.test.tsx](file:///C:/api_asp/API_Governance_F01/frontend/src/__tests__/PermissionsMatrixAndOverrides.test.tsx)):**
   - اختبار `UI-25` للتحديد المجمع (خلية، صف، عمود، قسم، تحديد الكل وإلغائه) وحفظ مصفوفة الدور مع `CSRF` و`CentralModalDialog`.
   - اختبار `UI-15A` للمنح الفردي `GRANT` والسحب الفردي `REVOKE` وزر «إعادة الكل إلى الموروث» وحفظ الاستثناءات عبر `CentralModalDialog`.
3. **بوابة الجودة الشاملة (`tests/run-quality-gate.ps1`) وفحوص الثغرات:**
   - `ESLint` + `TypeScript (tsc -b)` + بناء `Vite` الإنتاجي (`48` وحدة): ناجح (`0` أخطاء).
   - `Vitest`: **19 / 19 اختبارًا ناجحًا** (`5 / 5` ملفات اختبار).
   - `dotnet build -c Release --warnaserror`: ناجح (`0` تحذيرات، `0` أخطاء).
   - `dotnet test -c Release`: **46 / 46 اختبارًا ناجحًا** — الإجمالي الكلي **`65 / 65` اختبارًا آليًا ناجحًا (`0` فاشل)**.
   - فحص ثغرات التبعيات: `npm audit --audit-level=low` (`found 0 vulnerabilities`)، و`dotnet list ApiGovernance.sln package --vulnerable --include-transitive` (`0` حزم مصابة في جميع المشاريع الـ 13).

