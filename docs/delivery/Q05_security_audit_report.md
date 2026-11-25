# تقرير التدقيق الأمني المستقل — Task Q05 (`Sprint S14`)

- **نطاق التدقيق:** التحقق الآلي والهيكلي لجميع مسارات الخادم (`/api/v1/*`)، ضوابط الجلسة وCSRF، قواعد `ADR-01` وDocument ACL ومكافحة IDOR، حجر الملفات وتكامل الفحص الأمني، عدم القابلية للتعديل `OP-01`، وفحص ثغرات حزم `npm` و`NuGet`.

## 1. نتائج التدقيق الآلي لنقاط النهاية (Endpoint Security Sweep)
- الأداة المرجعية: `EndpointAuthorizationCatalogVerifier.AuditVersionedApiEndpoints` داخل [ResilienceAndSecurityAuditS14Tests.cs](file:///C:/api_asp/API_Governance_F01_CleanGitHub/tests/backend/ApiGovernance.Backend.Tests/Integration/ResilienceAndSecurityAuditS14Tests.cs).
- **النتيجة:** **`0` ثغرات / `0` مسارات غير محمية** عبر جميع نقاط `/api/v1/*` (أكثر من `60` نقطة نهاية مسجلة؛ كل نقطة غير مصنّفة بـ `AllowAnonymous` صريح مقيّدة إما بـ `RequireGeneralPermission(...)` أو `RequireActiveSession()`).

## 2. ضوابط الجلسات وCSRF وتحديد المحاولات (Auth & CSRF)
- **الجلسات:** حفظ الجلسات عبر `PostgreSqlSessionTicketStore` وملف تعريف ارتباط `HttpOnly` مع تحقق دوري من `SecurityStamp` (إبطال فوري عند إيقاف المستخدم أو تغيير كلمة المرور أو الصلاحيات).
- **حماية CSRF:** فرض `X-CSRF-TOKEN` عبر `WithCsrfValidation()` على جميع نقاط التعديل؛ طلبات `POST` بدون الرمز تُرفض برموز `400`/`403`.
- **تحديد المعدل والقفل:** قفل الحساب بعد فشل متكرر (`MaxFailedAccessAttempts = 5` لمدة `15` دقيقة) مع Rate Limiting على مسارات الدخول وإعادة التعيين.

## 3. تفويض الموارد ومنع `IDOR` (`ADR-01` & `Document ACL`)
- **التفويض العام:** قاعدة `User REVOKE > User GRANT > Active Role Union` منفذة مركزيًا في `ServerAuthorizationService`.
- **صلاحيات الوثيقة (`Document ACL`):** رفض افتراضي لكل وثيقة؛ فصل كامل بين تعيين مطوري التكامل (`integration_developers`) وصلاحيات الوصول (`document_role_permissions`, `document_user_permissions`)؛ منع التسريب بإرجاع `404`/`403` وفق سياق `doc.view`.
- **البحث الشامل واللوحة التكيّفية (`UI-17`/`UI-18`):** ترشيح خادمي لكل النتائج والعدادات على مستوى `EvaluateDocumentPermissionsBatchAsync` دون تسريب أرقام أو وجود موارد غير مصرح بها.
- **بوابة المستهلك الخارجي (`UI-19`):** تقييد إجباري بـ `ConsumerEntityId` وحالة النشر النشط `PUBLISHED` فقط مع حجب `WITHDRAWN`.

## 4. أمن الملفات وعدم القابلية للتعديل (`OP-01`)
- **تخزين الملفات الخاص:** جميع الملفات في `PrivateStorage` خارج مجلدات الويب مع `SHA-256`، وتنزيل حصري عبر الخادم مع `X-Content-Type-Options: nosniff` و`Content-Security-Policy: sandbox`.
- **سجل التدقيق والتاريخ (`OP-01`):** حظر التعديل أو الحذف على `AuditEvents` و`ReleaseHistoryEvents` داخل `ApiGovernanceDbContext` (تم اختباره فعليًا بـ `InvalidOperationException` في `Q04`), دون أي نقاط `POST`/`PUT`/`PATCH`/`DELETE` على `/api/v1/audit-events`.

## 5. فحص ثغرات التبعيات وخلو الأسرار
- **`npm audit --audit-level=low`:** **`found 0 vulnerabilities`**.
- **`dotnet list ApiGovernance.sln package --vulnerable --include-transitive --no-restore`:** **`0` حزم مصابة بالثغرات** في جميع مشاريع الحل الـ **`13`**.
- **الأسرار:** خلو المستودع من كلمات المرور أو المفاتيح أو أسماء الجهات المؤسسية الحقيقية؛ حفظ سلاسل الاتصال في `.NET User Secrets` ومتغيرات بيئة الخادم فقط.

