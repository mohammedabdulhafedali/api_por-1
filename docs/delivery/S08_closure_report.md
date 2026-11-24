# تقرير تسليم السبرنت الثامن — Sprint S08 (تخزين الملفات الخاص والفحص الأمني وإنشاء الإصدارات)

- **السبرنت:** `Sprint S08` — مخزن الملفات الخاص، التحقق والفحص الأمني والحجر، وإصدارات API اليدوية (`V01`, `V02`, `V03`, `V04`)
- **الفرع التكاملي:** `develop` (بعد دمج `task/S08-versions-files`)
- **التاريخ:** 24 نوفمبر 2026
- **الحالة:** مكتمل 100% (`4/4` مهام ضمن نطاق MVP) ومجتاز لبوابة الجودة الشاملة وفحوص الثغرات — جاهز للاختبار اليدوي من المالك قبل بدء `Sprint S09`

---

## 1. المهام المكتملة والوظائف المنفذة

1. **`V01` — كائن الملف الثابت ومخزن الملفات الخاص (`IPrivateFileStore` / `LocalPrivateFileStore`):**
   - تنفيذ المخزن الخاص خارج مجلدات الويب العامة في [FileStorageAndScanOptions.cs](file:///C:/api_asp/API_Governance_F01/backend/src/Modules/VersionsFiles/Configuration/FileStorageAndScanOptions.cs) و[PrivateFileStorageService.cs](file:///C:/api_asp/API_Governance_F01/backend/src/Modules/VersionsFiles/Storage/PrivateFileStorageService.cs)، مع عزل صريح لمنع توجيه المسار داخل `wwwroot` أو `public`.
   - تقسيم التخزين إلى ثلاث مناطق منفصلة: `staging/` للاستقبال المؤقت، و`quarantine/` لعزل الملفات المرفوضة أو غير المفحوصة، و`objects/yyyy/MM/<guid>.bin` كائنات ثابتة (write-once) بمفاتيح خادم عشوائية، مع حفظ اسم الملف الأصلي للعرض فقط وحساب بصمة `SHA-256` أثناء التدفق والتحقق منها عند التثبيت والتنزيل.

2. **`V02` — الرفع والتحقق من الأنواع والامتدادات والأحجام وبنية المحتوى:**
   - بناء المدقق الأمني للملفات [FileUploadContentValidator.cs](file:///C:/api_asp/API_Governance_F01/backend/src/Modules/VersionsFiles/Validation/FileUploadContentValidator.cs) للتحقق الفعلي من التواقيع الثنائية والهيكل الداخلي (دون الاعتماد على ترويسة `Content-Type` من المتصفح)، مع رفض الامتدادات التنفيذية والمركبة (`spec.exe.json`) وتواقيع `MZ/ELF` ووسوم `<script>` و`SVG/HTML` النشطة، ومنع ملفات Office الحاوية على وحدات ماكرو (`vbaProject.bin`) وحماية ZIP/OOXML من هجمات `Zip Slip` و`Zip Bomb`، وإنفاذ حدود الحجم والعدد القصوى في قالب الوثيقة (`M04`/`M05`).

3. **`V03` — ربط الفحص الأمني، العزل، وسياسة الفشل الآمن (`Fail-Closed` دون محاكاة):**
   - تنفيذ [FileSecurityScanner.cs](file:///C:/api_asp/API_Governance_F01/backend/src/Modules/VersionsFiles/Scanning/FileSecurityScanner.cs) ببروتوكول `ClamAV clamd` الفعلي (`zINSTREAM`) ودعم محول أوامر فحص المؤسسة (`CommandLine`)، إضافةً إلى معالجة حظر مضاد الفيروسات الفوري على نظام التشغيل (`ERROR_VIRUS_INFECTED` / `0x800700E1`) في [VersionsFilesService.cs](file:///C:/api_asp/API_Governance_F01/backend/src/Modules/VersionsFiles/Services/VersionsFilesService.cs).
   - **منع محاكاة النجاح:** عند غياب محرك الفحص الفعلي أو تعذر الوصول إليه، لا يُعاد `CLEAN` مطلقًا؛ بل يُحجز الملف داخل `quarantine/` بحالة `PENDING_SCAN` (`202 Accepted`) ويُمنع تنزيله أو استخدامه (`422`) لحين توفر المحرك وإعادة الفحص (`POST .../rescan`)، بينما تُرفض الملفات المصابة (`INFECTED`) وتُعزل في الحجر مع رفض ارتباطها بالمراجعة (`422`).

4. **`V04` — إنشاء إصدارات API برقم نصي يدوي والربط الكامل مع الواجهة والاختصاص:**
   - ربط نقاط `/api/v1/integrations/{integrationId}/versions` و`/files-workspace` و`/versions/{versionId}/revisions` و`/revisions/{revisionId}/files*` و`/files/{fileId}/scan-status` في [VersionsFilesModule.cs](file:///C:/api_asp/API_Governance_F01/backend/src/Modules/VersionsFiles/VersionsFilesModule.cs) بإنفاذ `CSRF` والصلاحيات العامة وصلاحيات الوثيقة (`doc.view`, `doc.version_manage`, `doc.files_manage`) واشتراط **تعيين المطور** (`IsUserAssignedDeveloperAsync`) وتسجيل تدقيق `OP-01`.
   - إنشاء إصدارات API بنص حر يحدده المطور دون فرض `SemVer` ودون خلطها بمراجعات حزم التوثيق (`#1`, `#2`...) عبر تبويبي **`الإصدارات (UI-12)`** و**`الملفات (UI-10)`** وصفحتي التحرير المستقلتين في [IntegrationsPage.tsx](file:///C:/api_asp/API_Governance_F01/frontend/src/components/integrations/IntegrationsPage.tsx) و[versionsFilesApi.ts](file:///C:/api_asp/API_Governance_F01/frontend/src/api/versionsFilesApi.ts).

---

## 2. نتائج بوابة الجودة والفحوص الشاملة (`task-2213`)

- **فحص وبناء الواجهة (`ESLint` + `TypeScript tsc -b` + `vite build`):** ناجح (`62` وحدة، `0` أخطاء).
- **اختبارات الواجهة (`Vitest`):** **27 / 27 اختبارًا ناجحًا** عبر `10 / 10` ملفات اختبار (`0` فاشل — يشمل [VersionsFilesS08.test.tsx](file:///C:/api_asp/API_Governance_F01/frontend/src/__tests__/VersionsFilesS08.test.tsx)).
- **بناء الحل الخلفي (`dotnet build -c Release -p:TreatWarningsAsErrors=true`):** ناجح (`0 Warning(s), 0 Error(s)`).
- **اختبارات الخادم والتكامل (`dotnet test -c Release` على `api_governance_test`):** **56 / 56 اختبارًا ناجحًا** (`0` فاشل، `0` متجاوز — يشمل [VersionsFilesS08IntegrationTests.cs](file:///C:/api_asp/API_Governance_F01/tests/backend/ApiGovernance.Backend.Tests/Integration/VersionsFilesS08IntegrationTests.cs) ضد PostgreSQL وبروتوكول `ClamAV zINSTREAM` فعلي)؛ الإجمالي **`83 / 83` اختبارًا آليًا ناجحًا**.
- **فحوص ثغرات التبعيات (`npm audit --audit-level=low` و`dotnet list ApiGovernance.sln package --vulnerable --include-transitive`):** **`0` ثغرات** في الواجهة وجميع مشاريع الحل الـ 13.

---

## 3. متطلبات فعلية مطلوبة من قسم تقنية المعلومات (IT) قبل التشغيل المؤسسي/الإنتاجي

1. **مخزن الملفات الخاص (`FileStorage:RootPath`):**
   - توفير مسار تخزين مخصص خارج مجلدات خادم الويب (`IIS wwwroot`) على وحدة تخزين موثوقة أو `UNC Share` محمي مع تفعيل أذونات NTFS مقيّدة لحساب خدمة تطبيق IIS فقط (`IIS AppPool`), ومنع تنفيذ البرامج النصية/التنفيذية داخل مجلدات التخزين، وربط `objects/` و`quarantine/` بخطة النسخ الاحتياطي المتزامنة مع قاعدة بيانات PostgreSQL.
2. **محرك الفحص الأمني الفعلي (`FileScanning`):**
   - تثبيت وتشغيل خدمة **ClamAV (`clamd`)** مع تحديث دوري لقواعد التواقيع وتزويد عنوان المنفذ في `FileScanning:ClamAvHost` و`FileScanning:ClamAvPort` (`3310`) — **أو** اعتماد مسار أداة فحص المؤسسة (مثل `Microsoft Defender MpCmdRun.exe`) عبر `FileScanning:Provider = "CommandLine"` و`FileScanning:CommandPath`.
   - استثناء مجلد `staging/` / `quarantine/` أو التنسيق مع سياسة فحص النظام على الخادم لمنع تعارض الإقفال أثناء النقل، مع إبقاء الماسح النشط مفعّلًا قبل إتاحة أي ملف للمراجعة أو الاعتماد.
3. **إعداد حدود حجم الطلب في IIS:**
   - ضبط `maxAllowedContentLength` في `web.config` بما يتوافق مع سقف المنصة المعتمد (`FileStorage:MaxUploadBytesCeiling`, افتراضيًا `100 MB`).
