# تقرير إثبات الجدوى (PoC): تحديث حقول ملفات Word باستخدام `Open XML SDK` — المهمة `V08` (`OP-03`)

- **السبرنت / المهمة:** `Sprint S09` — المهمة **`V08`** (المرجع التشغيلي: `OP-03`)
- **الحزمة المستخدمة:** `DocumentFormat.OpenXml` (`3.3.0`) ضمن وحدة [ApiGovernance.Modules.VersionsFiles.csproj](file:///d:/api-portal-1/backend/src/Modules/VersionsFiles/ApiGovernance.Modules.VersionsFiles.csproj)
- **الخدمة المنفذة:** [WordGovernanceDerivativeService.cs](file:///d:/api-portal-1/backend/src/Modules/VersionsFiles/OpenXml/WordGovernanceDerivativeService.cs)
- **الحالة:** مُنفَّذ ومُتحقق آليًا عبر اختبارات التكامل على حزم `.docx` حقيقية في [VersionsFilesS09IntegrationTests.cs](file:///d:/api-portal-1/tests/backend/ApiGovernance.Backend.Tests/Integration/VersionsFilesS09IntegrationTests.cs)

---

## 1. ملخص ما تم إثباته (دون تعميم على كل ملفات Word)

يثبت هذا النموذج الأولي (`PoC`) قدرة الخادم في ASP.NET Core 10 على تحديث بيانات الحوكمة (رمز التكامل، عنوان الوثيقة، المزود، المستهلك، المجموعة، رقم الإصدار، رقم المراجعة، الحالة، بصمة الأصل، وتوقيت التوليد) داخل نسخة **مشتقة مستقلة** بصيغة `.docx` عبر `DocumentFormat.OpenXml`, وذلك **فقط للأنماط الثلاثة الصريحة التالية**:

1. **`SDT_CONTROLS` (Structured Document Tags / Content Controls):**
   - البحث عن عناصر `w:sdt` في المتن الرئيسي (`MainDocumentPart`) والرؤوس (`HeaderParts`) والتذييلات (`FooterParts`) عبر `Tag` أو `SdtAlias` المطابق لمفاتيح `APIGOV_*`.
2. **`PLACEHOLDER_TOKENS` (`{{APIGOV_*}}` و`[[APIGOV_*]]`):**
   - معالجة النصوص على مستوى الفقرة كاملةً (`Paragraph`) لإصلاح مشكلة تجزؤ الـ Runs داخل ملفات Word (`Split Runs`) عبر المتن والرؤوس والتذييلات.
3. **`CUSTOM_PROPERTIES` (`docProps/custom.xml`):**
   - تحديث خصائص المستند المخصصة المطابقة لمفاتيح `APIGOV_*` عبر `CustomFilePropertiesPart`، مع مسح أو التحقق المنضبط لنوع `VTLPWSTR`.

---

## 2. ضوابط الحماية وعدم التعديل على الأصل وبوابة عدم النشر (`OP-03`)

1. **حماية الملف الأصلي وبصمته (`SHA-256`):**
   - يُفتح الملف الأصلي السليم (`CLEAN`) للقراءة فقط من المخزن الخاص الثابت، وتُكتب التعديلات إلى مسار مرحلي مؤقت (`staging/`) ثم تُفحص بنيويًا قبل الحفظ النهائي في سجل `word_derivative_records` وكائن `file_objects` مستقل.
   - أثبت الاختبار تطابق بصمة `sourceSha256` وبايتات التنزيل من `/download` قبل وبعد توليد المشتق دون أي تعديل على الكائن الأصلي.
2. **التحقق البنيوي والفحص الأمني قبل الإتاحة:**
   - بعد حفظ المشتق يُفتح للقراءة فقط ويُتحقق منه عبر `OpenXmlValidator(FileFormatVersions.Microsoft365)`.
   - يمر المشتق عبر محرك الفحص الأمني (`IFileSecurityScanner`)؛ وإذا لم يجتز الفحص (`PENDING_SCAN` / `INFECTED` / `INVALID`) أو فشل `OpenXmlValidator` أو خلا الملف من علامات `APIGOV_*` (`NO_RECOGNIZED_FIELDS`)، يُضبط `IsPublishable = false` ويُحجب تنزيله (`422 UnprocessableEntity`) عبر `GET .../word-derivative/download`.

---

## 3. حدود هذا الـ `PoC` (لماذا لا يُعمَّم على كل ملفات Word العشوائية)

- لا يدعم هذا الـ `PoC` تحديث نصوص حرة غير موسومة (`NO_RECOGNIZED_FIELDS`) ولا نماذج الجداول المعقدة غير المرتبطة بمحتوى `w:sdt` أو الرموز المحددة، ولا وحدات الماكرو `VBA` (الممنوعة أصلًا في `V02`).
- يُستنتج من النتيجة العملية أن التبني المؤسسي الشامل لتحرير مستندات Word مستقبلاً يتطلب قوالب قياسية معتمدة موسومة بـ **Content Controls (`w:sdt`)** أو خصائص `custom.xml` مسبقة الإعداد من مالك القوالب قبل أي تعميم.

---

## 4. الحالات المختبرة آليًا في `VersionsFilesS09IntegrationTests`

1. **`pattern1-sdt.docx` (عناصر `SdtRun`):** تحديث `APIGOV_INTEGRATION_CODE` و`APIGOV_VERSION_LABEL` ← `VERIFIED` (`isPublishable = true`, `0` أخطاء `OpenXmlValidator`, تطابق بايتات الأصل).
2. **`pattern2-splitrun.docx` (`Placeholder` مقسم على عدة Runs + Header):** تحديث `{{APIGOV_REVISION_NUMBER}}`, `[[APIGOV_DOCUMENT_TITLE]]`, و`{{APIGOV_VERSION_LABEL}}` ← `VERIFIED`.
3. **`pattern3-customprops.docx` (`CustomFilePropertiesPart`):** تحديث `APIGOV_REVISION_STATUS` و`APIGOV_INTEGRATION_CODE` ← `VERIFIED`.
4. **`pattern4-plain.docx` (ملف Word عشوائي دون علامات):** حالة `NO_RECOGNIZED_FIELDS`، `isPublishable = false`، وحجب التنزيل (`422`).
5. **تعطل الفاحص الأمني أثناء التوليد:** تسجيل حالة `PENDING_SCAN` وحجب التنزيل (`422`) تطبيقًا لسياسة `Fail-Closed` و`OP-03`.
