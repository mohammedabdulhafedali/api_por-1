# frontend — واجهة الويب ومكتبة المكونات المشتركة (React 19 + TypeScript + Vite)

الحالة: **Sprint S01 / المهام F02 & F06 & F08** — تهيئة مشروع الواجهة الأمامية (F02)، وإعداد اختبارات الواجهة الآلية وESLint (F06)، وتأسيس مكتبة مكونات UI المشتركة القابلة لإعادة الاستخدام والهوية البصرية المعتمدة (`02_UX_UI_v1.2.md`) (F08). لا يتضمن منطق أعمال أو مصادقة أو صلاحيات وهمية بعد.

## مكتبة المكونات المشتركة (`src/components/ui/`)
- `AppLayoutShell.tsx`: هيكل الصفحات المشترك، القائمة الجانبية القابلة للطي (قسم رئيسي واحد مفتوح بالترتيب المعتمد، مع درج هاتف متجاوب)، والشريط العلوي الذي يعرض عنوان الصفحة (دون تكرار كتلة عنوان أعلى المحتوى وفق `PRD-06`) وأزرار تبديل اللغة (`AR ⇄ EN` مع `RTL ⇄ LTR`) والمظهر (`Light ⇄ Dark`).
- `StandaloneFormPage.tsx`: قالب صفحات الإضافة والتعديل المستقلة داخل التطبيق (`Create` / `Edit` كصفحات كاملة مستقلة وليست نوافذ منبثقة `Modals` وفق `PRD-06`).
- `FormControls.tsx`: مكونات `Button`, `FormField`, `TextInput`, `SelectInput`, `TextareaInput` مع دعم الحقول الإلزامية والنصوص الإرشادية ورسائل أخطاء التحقق (`role="alert"`, `aria-invalid`) وعزل المعرفات التقنية بـ `LTR` وخط `JetBrains Mono`.
- `DataTable.tsx`: مكونات `SearchFilterBar`, `DataTable`, و`PaginationBar` مع دعم البحث والتصفية والترقيم وحالات التحميل والفراغ.
- `SelectionMatrix.tsx`: مكون مصفوفة الاختيار ومربعات التحديد القابل لإعادة الاستخدام (لطبقات `UI-25`, `UI-15A`, `UI-21A`) مع تحديد خلية، صف، عمود، قسم، أو الكل، وإزاحة أفقية وتثبيت العمود الأول.
- `CentralModalDialog.tsx`: حوار الرسائل المركزي الموحد (`Modal` — `role="dialog"`, `aria-modal="true"`) لرسائل النجاح والخطأ والتحذير والمعلومات والتأكيد (مع دعم `Escape` وإعادة التركيز للحقل عند إغلاق الخطأ، دون استخدام `Toast`).
- `StateViews.tsx`: مكونات `LoadingState`, `EmptyState`, `ErrorState`, و`StatusBadge`.

## الأوامر المتاحة
من داخل مجلد `frontend/`:

```powershell
npm install
npm run dev        # تشغيل خادم التطوير على http://127.0.0.1:5173/
npm run lint       # فحص ESLint
npm run typecheck  # فحص TypeScript
npm test           # تشغيل اختبارات Vitest (9 اختبارات)
npm run build      # البناء الإنتاجي
```
