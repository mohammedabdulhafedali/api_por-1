# tests — الاختبارات الآلية وبوابة الجودة (Sprint S01 / المهمة F06)

يضم هذا المجلد البنية التأسيسية للاختبارات الآلية وخط الفحص الموحد (`Quality Gate`) للواجهة الأمامية والخادم الخلفي.

## 1. بنية الاختبارات
- `tests/run-quality-gate.ps1`: السكربت الموحد لتشغيل بوابة الفحص الآلي (`Lint + TypeCheck + Frontend Tests + Frontend Build + Backend Build + Backend Tests`). يفشل السكربت فورًا عند أي تحذير أو خطأ لمنع دمج كود غير سليم.
- `tests/backend/ApiGovernance.Backend.Tests/`: مشروع اختبارات الخادم (`xUnit` + `Microsoft.AspNetCore.Mvc.Testing` على `.NET 10.0`)، ويضم:
  - `Unit/ModularityAndContractUnitTests.cs`: اختبارات الوحدات لعزل وترتيب وحدات الـ `Modular Monolith` الـ 10، وخرائط رموز الأخطاء (`ApiErrorCodes`)، والمترجم ثنائي اللغة (`ApiErrorLocalizer` `AR/EN`)، وقواعد التحقق من النماذج (`PagedQueryRequest`, `ContractValidationProbeRequest`).
  - `Integration/BackendContractIntegrationTests.cs`: اختبارات التكامل عبر `WebApplicationFactory<Program>` في بيئتي `Development` و`Production` لفحص نقاط الصحة (`/health` و`/api/v1/health`)، وتوثيق `OpenAPI` (`/openapi/v1.json`)، وتعطيل مسارات الفحص التجريبية في الإنتاج، وجميع رموز عقود الأخطاء (`200`, `400` عربي/إنجليزي، `401`, `403`, `404`, `409`, `413`, `422`, `429`, `500`) مع ضمان عدم تسريب تفاصيل الاستثناءات الداخلية أو ترويسة `Server`.
- `frontend/src/__tests__/App.test.tsx`: اختبارات الواجهة الأمامية الآلية (`Vitest 5.1.1` + `@testing-library/react` + `jsdom`) لفحص الحالة الابتدائية (`AR` + `RTL` + `Light`)، وتبديل اللغة والاتجاه (`AR ⇄ EN`, `RTL ⇄ LTR`)، وتبديل الثيم (`Light ⇄ Dark`) مع حفظ المدخلات، والقائمة الجانبية القابلة للطي بترتيب الأقسام الـ 5 وفتح قسم واحد في كل مرة، وحوار الرسائل المركزي الموحد (`Modal`) مع إعادة التركيز للحقل بعد إغلاق الخطأ.

## 2. أوامر التشغيل
لتشغيل بوابة الجودة والاختبارات الموحدة بالكامل من جذر المستودع:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\run-quality-gate.ps1
```

أو لتشغيل كل حزمة منفردة:
```powershell
# اختبارات وفحوص الواجهة الأمامية
cd .\frontend
npm run lint
npm run typecheck
npm test

# اختبارات الخادم الخلفي
cd ..\backend
dotnet test ApiGovernance.sln -c Release
```
