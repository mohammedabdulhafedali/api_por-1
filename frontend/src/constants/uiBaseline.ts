export type AppLanguage = 'ar' | 'en';
export type AppTheme = 'light' | 'dark';
export type DialogVariant = 'info' | 'success' | 'warning' | 'error' | 'confirm';
export type ShellWorkspaceMode = 'catalog' | 'create' | 'edit';
export type TablePreviewState = 'ready' | 'loading' | 'empty' | 'error';

export interface NavItemSpec {
  id: string;
  label: {
    ar: string;
    en: string;
  };
  routePreview: string;
  defaultMode?: ShellWorkspaceMode;
}

export interface NavGroupSpec {
  id: string;
  label: {
    ar: string;
    en: string;
  };
  items: NavItemSpec[];
}

export interface UiCatalogSpecRow {
  id: string;
  category: 'navigation' | 'configuration' | 'documents' | 'integrations' | 'administration';
  title: {
    ar: string;
    en: string;
  };
  routePreview: string;
  pagePattern: 'list' | 'standalone-form' | 'matrix' | 'workspace-tabs';
}

export const STANDALONE_NAV_ITEMS: NavItemSpec[] = [
  {
    id: 'UI-17',
    label: { ar: 'لوحة التحكم', en: 'Dashboard' },
    routePreview: '/dashboard',
  },
  {
    id: 'UI-18',
    label: { ar: 'البحث والاستعراض', en: 'Search & Browse' },
    routePreview: '/search',
  },
  {
    id: 'UI-19',
    label: { ar: 'بوابة المستهلك', en: 'Consumer Portal' },
    routePreview: '/portal',
  },
];

/**
 * Approved sidebar group order per PRD-06 & 02_UX_UI_v1.2.md:
 * 1. الأنظمة والجهات (Systems & Entities)
 * 2. الإعدادات (Settings)
 * 3. الإدارة (Administration)
 * 4. وثائق API (API Documents)
 * 5. التكاملات (Integrations)
 */
export const SIDEBAR_GROUPS: NavGroupSpec[] = [
  {
    id: 'systems',
    label: { ar: 'الأنظمة والجهات', en: 'Systems & Entities' },
    items: [
      {
        id: 'UI-01',
        label: { ar: 'أنظمة الشركة', en: 'Company Systems' },
        routePreview: '/systems/company',
      },
      {
        id: 'UI-02',
        label: { ar: 'الجهات المستهلكة', en: 'Consumer Entities' },
        routePreview: '/systems/consumers',
      },
      {
        id: 'UI-03',
        label: { ar: 'الأنظمة المزودة', en: 'Provider Systems' },
        routePreview: '/systems/providers',
      },
    ],
  },
  {
    id: 'settings',
    label: { ar: 'الإعدادات', en: 'Settings' },
    items: [
      {
        id: 'UI-04',
        label: { ar: 'أنواع الملفات', en: 'File Types' },
        routePreview: '/settings/file-types',
      },
      {
        id: 'UI-23',
        label: { ar: 'قوالب متطلبات الملفات', en: 'File Requirement Templates' },
        routePreview: '/settings/requirement-templates',
      },
      {
        id: 'UI-05',
        label: { ar: 'مسارات الاعتماد', en: 'Approval Routes' },
        routePreview: '/settings/approval-routes',
      },
      {
        id: 'UI-06',
        label: { ar: 'أنواع طلبات التغيير', en: 'Change Request Types' },
        routePreview: '/settings/change-types',
      },
    ],
  },
  {
    id: 'administration',
    label: { ar: 'الإدارة', en: 'Administration' },
    items: [
      {
        id: 'UI-15',
        label: { ar: 'المستخدمون', en: 'Users' },
        routePreview: '/admin/users',
      },
      {
        id: 'UI-25',
        label: { ar: 'الأدوار والصلاحيات', en: 'Roles & Permissions' },
        routePreview: '/admin/roles-permissions',
      },
      {
        id: 'UI-16',
        label: { ar: 'سجل التدقيق', en: 'Audit Log' },
        routePreview: '/admin/audit-events',
      },
    ],
  },
  {
    id: 'api-documents',
    label: { ar: 'وثائق API', en: 'API Documents' },
    items: [
      {
        id: 'UI-21',
        label: { ar: 'قائمة وثائق API', en: 'API Documents List' },
        routePreview: '/api-documents',
      },
      {
        id: 'UI-07',
        label: { ar: 'إضافة وثيقة API', en: 'Create API Document' },
        routePreview: '/api-documents/new',
        defaultMode: 'create',
      },
    ],
  },
  {
    id: 'integrations',
    label: { ar: 'التكاملات', en: 'Integrations' },
    items: [
      {
        id: 'UI-22',
        label: { ar: 'قائمة التكاملات', en: 'Integrations List' },
        routePreview: '/integrations',
      },
      {
        id: 'UI-08',
        label: { ar: 'إضافة تكامل', en: 'Create Integration' },
        routePreview: '/integrations/new',
        defaultMode: 'create',
      },
      {
        id: 'UI-09',
        label: { ar: 'تفاصيل التكامل والتبويبات', en: 'Integration Details & Tabs' },
        routePreview: '/integrations/:integrationId',
      },
    ],
  },
];

/**
 * Pure UX Specification Metadata Catalog (from 02_UX_UI_v1.2.md) used to exercise
 * the shared DataTable, SearchFilterBar, and PaginationBar without mock business records.
 */
export const UX_SPEC_CATALOG_ROWS: UiCatalogSpecRow[] = [
  {
    id: 'UI-01',
    category: 'configuration',
    title: { ar: 'أنظمة الشركة', en: 'Company Systems' },
    routePreview: '/systems/company',
    pagePattern: 'list',
  },
  {
    id: 'UI-02',
    category: 'configuration',
    title: { ar: 'الجهات المستهلكة', en: 'Consumer Entities' },
    routePreview: '/systems/consumers',
    pagePattern: 'list',
  },
  {
    id: 'UI-03',
    category: 'configuration',
    title: { ar: 'الأنظمة المزودة', en: 'Provider Systems' },
    routePreview: '/systems/providers',
    pagePattern: 'list',
  },
  {
    id: 'UI-04',
    category: 'configuration',
    title: { ar: 'أنواع الملفات', en: 'File Types' },
    routePreview: '/settings/file-types',
    pagePattern: 'list',
  },
  {
    id: 'UI-05',
    category: 'configuration',
    title: { ar: 'مسارات الاعتماد', en: 'Approval Routes' },
    routePreview: '/settings/approval-routes',
    pagePattern: 'list',
  },
  {
    id: 'UI-05A',
    category: 'configuration',
    title: { ar: 'إضافة/تحرير مرحلة اعتماد', en: 'Add / Edit Approval Stage' },
    routePreview: '/settings/approval-routes/stages/edit',
    pagePattern: 'standalone-form',
  },
  {
    id: 'UI-06',
    category: 'configuration',
    title: { ar: 'أنواع طلبات التغيير', en: 'Change Request Types' },
    routePreview: '/settings/change-types',
    pagePattern: 'list',
  },
  {
    id: 'UI-07',
    category: 'documents',
    title: { ar: 'إضافة وثيقة API', en: 'Create API Document' },
    routePreview: '/api-documents/new',
    pagePattern: 'standalone-form',
  },
  {
    id: 'UI-21',
    category: 'documents',
    title: { ar: 'قائمة وثائق API', en: 'API Documents List' },
    routePreview: '/api-documents',
    pagePattern: 'list',
  },
  {
    id: 'UI-21A',
    category: 'documents',
    title: { ar: 'صلاحيات وثيقة API', en: 'Document Access Matrix' },
    routePreview: '/api-documents/:documentId/access',
    pagePattern: 'matrix',
  },
  {
    id: 'UI-08',
    category: 'integrations',
    title: { ar: 'إضافة تكامل', en: 'Create Integration' },
    routePreview: '/integrations/new',
    pagePattern: 'standalone-form',
  },
  {
    id: 'UI-09',
    category: 'integrations',
    title: { ar: 'تفاصيل التكامل والتبويبات', en: 'Integration Details & Tabs' },
    routePreview: '/integrations/:integrationId',
    pagePattern: 'workspace-tabs',
  },
  {
    id: 'UI-15',
    category: 'administration',
    title: { ar: 'المستخدمون', en: 'Users' },
    routePreview: '/admin/users',
    pagePattern: 'list',
  },
  {
    id: 'UI-15A',
    category: 'administration',
    title: { ar: 'صلاحيات مستخدم', en: 'User Effective Permissions' },
    routePreview: '/admin/users/:userId/permissions',
    pagePattern: 'matrix',
  },
  {
    id: 'UI-25',
    category: 'administration',
    title: { ar: 'مصفوفة الأدوار والصلاحيات', en: 'Roles & Permissions Matrix' },
    routePreview: '/admin/roles-permissions',
    pagePattern: 'matrix',
  },
];

export const UI_STRINGS = {
  ar: {
    brandTitle: 'حوكمة وثائق APIs',
    brandSubtitle: 'منصة حوكمة الوثائق والتكاملات',
    topbarBreadcrumb: 'Sprint S01 · المكونات المشتركة F02/F08',
    topbarPageTitle: 'قالب الصفحة الأولي والهوية البصرية المعتمدة',
    standaloneSectionTitle: 'الروابط المستقلة',
    collapsibleSectionTitle: 'الأقسام الرئيسية (قسم واحد مفتوح)',
    themeLight: 'نهاري',
    themeDark: 'ليلي',
    themeToggleLabel: 'تبديل المظهر النهاري/الليلي',
    langToggleLabel: 'تبديل لغة الواجهة',
    mobileMenuToggle: 'فتح/إغلاق القائمة الجانبية',
    baselineBadge: 'خط الأساس v1.2 — F02 / F08',
    shellNoticeTitle: 'هيكل UI المشترك ومكتبة المكونات القابلة لإعادة الاستخدام (F02 & F08)',
    shellNoticeBody:
      'يتضمن هذا القالب مكتبة المكونات المشتركة المعتمدة في 02_UX_UI_v1.2.md (هيكل الصفحات، الأزرار والحقول، الجداول والبحث والترقيم، مصفوفات التحديد، صفحات الإضافة/التعديل المستقلة، النوافذ المركزية للرسائل، وحالات التحميل والفراغ والأخطاء) دون منطق أعمال أو مصادقة أو صلاحيات وهمية.',
    selectedNavLabel: 'الموضع المحدد في القالب',
    selectedRouteLabel: 'المسار المرجعي المخطط',
    plannedInSprintLabel: 'نمط عرض القالب الحالي',
    plannedInSprintValue: 'هيكل ومكونات UI مشتركة فقط — تُنفذ الشاشات والربط الخادمي في مهامها المعتمدة.',
    openCreatePageBtn: 'فتح صفحة إضافة مستقلة (بدون Modal)',
    openEditPageBtn: 'فتح صفحة تعديل مستقلة (بدون Modal)',
    returnToCatalogBtn: 'العودة إلى صفحة استعراض المكونات',
    standaloneCreateBadge: 'صفحة إضافة مستقلة (Non-Modal)',
    standaloneEditBadge: 'صفحة تعديل مستقلة (Non-Modal)',
    standaloneCreateTitle: 'قالب صفحة الإضافة المستقلة داخل التطبيق',
    standaloneEditTitle: 'قالب صفحة التعديل المستقلة داخل التطبيق',
    standaloneFormSubtitle:
      'تُعرض نماذج الإضافة والتعديل كصفحات كاملة مستقلة وفق PRD-06 و02_UX_UI_v1.2.md، وتُستخدم النوافذ المركزية (Modal) للرسائل والتأكيدات فقط.',
    standaloneSaveBtn: 'حفظ ومعاينة رسالة التأكيد',
    standaloneCancelBtn: 'إلغاء والعودة',
    identityPanelTitle: 'التحقق من الهوية البصرية المعتمدة (02_UX_UI_v1.2.md)',
    identityPanelDesc: 'القيم اللونية والخطوط وسلوك الواجهة المطبقة في المكونات المشتركة:',
    tokenSurfaceLabel: 'عنصر الواجهة',
    tokenLightLabel: 'الوضع النهاري (Light)',
    tokenDarkLabel: 'الوضع الليلي (Dark)',
    tokenPageBg: 'خلفية الصفحة',
    tokenSidebarTopbar: 'القائمة الجانبية والشريط العلوي (Sidebar / Topbar)',
    tokenCardSurface: 'البطاقات واللوحات (Cards / Panels)',
    tokenInputBg: 'حقول الإدخال (Form Controls)',
    tokenPrimaryBtn: 'الأزرار الرئيسية (Primary Actions)',
    tokenStatusColors: 'ألوان الحالة (نجاح / فشل / تحذير)',
    formPreviewTitle: 'معاينة عناصر النماذج والقوائم المنسدلة وحوار الرسائل المركزي (Modal)',
    formPreviewDesc:
      'تُنفذ صفحات الإضافة والتعديل كصفحات مستقلة داخل التطبيق (وليست Modals)، بينما تُعرض رسائل النظام والتأكيد عبر حوار مركزي موحد (Modal) بدلًا من شريط Toast.',
    sampleInputLabel: 'حقل نصي تجريبي (فحص تباين الحقول والخطوط)',
    sampleInputPlaceholder: 'أدخل نصًا لمعاينة خط Readex Pro / Outfit وتباين الحقل...',
    sampleInputHint: 'يحتفظ الحقل بقيمته عند تبديل اللغة أو المظهر أو طي القائمة الجانبية.',
    sampleInputRequiredError: 'هذا الحقل إلزامي عند التحقق من النموذج.',
    sampleCodeInputLabel: 'معرّف تقني معزول باتجاه LTR (JetBrains Mono)',
    sampleSelectLabel: 'قائمة منسدلة مشتركة (SelectInput)',
    sampleSelectPlaceholder: 'اختر نمط صفحة من المواصفة...',
    sampleTextareaLabel: 'حقل وصف متعدد الأسطر (TextareaInput)',
    sampleTextareaPlaceholder: 'ملاحظات هيكلية لمعاينة مكون النص متعدد الأسطر...',
    openSuccessModal: 'معاينة رسالة نجاح (Modal)',
    openWarningModal: 'معاينة رسالة تحذير (Modal)',
    openErrorModal: 'معاينة رسالة خطأ (Modal)',
    openConfirmModal: 'معاينة حوار تأكيد (Modal)',
    modalConfirmBtn: 'حسنًا',
    modalCancelBtn: 'إلغاء',
    modalCloseAria: 'إغلاق نافذة الرسالة',
    tableSectionTitle: 'مكون الجداول المشترك والبحث والتصفية والترقيم وحالات العرض',
    tableSectionDesc:
      'يعرض فهرس شاشات المواصفة المرجعية (02_UX_UI_v1.2.md) لاختبار البحث، التصفية، الترقيم، وحالات التحميل والفراغ والخطأ.',
    tableSearchPlaceholder: 'ابحث بمعرف الشاشة (مثل UI-25) أو العنوان...',
    tableSearchAria: 'البحث في جدول فهرس المواصفة',
    tableFilterAria: 'تصفية الجدول حسب الفئة',
    tableFilterAll: 'جميع الفئات',
    tableColId: 'معرف الشاشة',
    tableColTitle: 'عنوان الشاشة في المواصفة',
    tableColRoute: 'المسار المرجعي',
    tableColPattern: 'نمط الواجهة المعتمد',
    tableEmptyTitle: 'لا توجد عناصر مطابقة للبحث أو التصفية',
    tableEmptyDesc: 'جرّب تغيير عبارة البحث أو إعادة ضبط فلتر الفئة.',
    tableLoadingTitle: 'جارٍ تحميل بيانات الجدول...',
    tableErrorTitle: 'تعذر تحميل البيانات',
    tableErrorMsg: 'نموذج معاينة لحالة الخطأ القياسية (ErrorState) مع إمكانية إعادة المحاولة.',
    tableRetryBtn: 'إعادة المحاولة',
    tableStateReadyBtn: 'حالة البيانات',
    tableStateLoadingBtn: 'حالة التحميل',
    tableStateEmptyBtn: 'حالة الفراغ',
    tableStateErrorBtn: 'حالة الخطأ',
    paginationPrev: 'السابق',
    paginationNext: 'التالي',
    paginationSummaryPrefix: 'الصفحة',
    paginationOf: 'من',
    matrixSectionTitle: 'مكون مصفوفة الاختيار ومربعات التحديد المشترك (SelectionMatrix)',
    matrixSectionDesc:
      'مكون مصفوفة متجاوب قابل لإعادة الاستخدام يدعم تحديد خلية، صف كامل، عمود كامل، قسم كامل، أو تحديد الكل مع إزاحة أفقية وتثبيت العمود الأول.',
    matrixRowHeader: 'المسار / الشاشة',
    matrixSelectAll: 'تحديد الكل',
    matrixSelectGroup: 'تحديد القسم',
    matrixSelectRowPrefix: 'تحديد صف',
    matrixSelectColPrefix: 'تحديد عمود',
    matrixSelectedCountLabel: 'عدد الخلايا المحددة في المعاينة:',
    patternLabels: {
      list: 'قائمة وجدول',
      'standalone-form': 'صفحة نموذج مستقلة',
      matrix: 'مصفوفة شاملة',
      'workspace-tabs': 'تفاصيل وتبويبات',
    },
    categoryLabels: {
      navigation: 'التنقل العام',
      configuration: 'الأنظمة والإعدادات',
      documents: 'وثائق API',
      integrations: 'التكاملات',
      administration: 'الإدارة',
    },
    scopeGuardTitle: 'حدود النطاق الملتزم بها في F02 وF08',
    scopeGuards: [
      'لا يوجد منطق مصادقة أو جلسات أو صلاحيات وهمية في الواجهة؛ التفويض الفعلي خادمي بالكامل في مهامه (I01–I07).',
      'لا يوجد تكرار لكتلة العنوان والوصف أعلى المحتوى ما دام عنوان الصفحة ظاهرًا في الشريط العلوي (PRD-06).',
      'صفحات الإضافة والتعديل صفحات مستقلة داخل التطبيق (StandaloneFormPage) وليست نوافذ منبثقة (Modals).',
      'القائمة الجانبية قابلة للطي وتسمح بفتح قسم رئيسي واحد فقط في كل لحظة بالترتيب المعتمد.',
      'يتم حفظ تفضيلات اللغة (AR/EN مع RTL/LTR) والمظهر (نهاري/ليلي) محليًا في المتصفح.',
    ],
    modalSamples: {
      info: {
        title: 'معلومة النظام',
        message: 'هذا حوار مركزي موحد (Modal) مخصص لرسائل النظام والتأكيدات وفق المواصفة 02_UX_UI_v1.2.md.',
      },
      success: {
        title: 'تم التحقق من القالب بنجاح',
        message: 'يعمل حوار الرسائل المركزي الموحد (Modal) باللون المعتمد (#10b981) دون استخدام شريط Toast سفلي.',
      },
      warning: {
        title: 'تنبيه حدود المهمة F08',
        message: 'هذه الواجهة تمثل هيكل ومكونات UI المشتركة (F02/F08) فقط؛ لا تتضمن عمليات حفظ أو منطق أعمال قبل السبرنتات اللاحقة.',
      },
      error: {
        title: 'نموذج رسالة خطأ تحقق',
        message: 'تُعرض أخطاء التحقق الحرجة للمستخدم داخل حوار مركزي واضح باللون المعتمد (#f43f5e) مع إعادة التركيز للحقل المعني.',
      },
      confirm: {
        title: 'تأكيد الإجراء',
        message: 'هل ترغب في تأكيد هذا الإجراء التجريبي في معاينة مكون النافذة المركزية؟',
      },
    },
  },
  en: {
    brandTitle: 'API Docs Governance',
    brandSubtitle: 'API Documentation & Integrations Platform',
    topbarBreadcrumb: 'Sprint S01 · Shared UI Components F02/F08',
    topbarPageTitle: 'Initial Page Template & Approved Visual Identity',
    standaloneSectionTitle: 'Standalone Links',
    collapsibleSectionTitle: 'Main Sections (Single Open Group)',
    themeLight: 'Light',
    themeDark: 'Dark',
    themeToggleLabel: 'Toggle Light/Dark theme',
    langToggleLabel: 'Switch interface language',
    mobileMenuToggle: 'Toggle sidebar menu',
    baselineBadge: 'Baseline v1.2 — F02 / F08',
    shellNoticeTitle: 'Shared UI Shell & Reusable Component Library (F02 & F08)',
    shellNoticeBody:
      'This template provides the shared UI component library approved in 02_UX_UI_v1.2.md (page shell, buttons & form controls, tables with search/filter/pagination, selection matrices, standalone create/edit pages, central message modals, and loading/empty/error states) without business logic, auth, or mock permissions.',
    selectedNavLabel: 'Selected Template Slot',
    selectedRouteLabel: 'Planned Reference Route',
    plannedInSprintLabel: 'Current Template View Mode',
    plannedInSprintValue: 'Shared UI shell and components only — business screens and server integration are built in their designated tasks.',
    openCreatePageBtn: 'Open Standalone Create Page (No Modal)',
    openEditPageBtn: 'Open Standalone Edit Page (No Modal)',
    returnToCatalogBtn: 'Return to Component Showcase',
    standaloneCreateBadge: 'Standalone Create Page (Non-Modal)',
    standaloneEditBadge: 'Standalone Edit Page (Non-Modal)',
    standaloneCreateTitle: 'Standalone Create Page Template',
    standaloneEditTitle: 'Standalone Edit Page Template',
    standaloneFormSubtitle:
      'Create and Edit forms render as full standalone pages per PRD-06 and 02_UX_UI_v1.2.md, while central Modals are reserved strictly for system messages and confirmations.',
    standaloneSaveBtn: 'Save & Preview Confirmation Modal',
    standaloneCancelBtn: 'Cancel & Return',
    identityPanelTitle: 'Approved Visual Identity Verification (02_UX_UI_v1.2.md)',
    identityPanelDesc: 'Color tokens, typography, and layout rules active across shared components:',
    tokenSurfaceLabel: 'UI Surface / Element',
    tokenLightLabel: 'Light Theme',
    tokenDarkLabel: 'Dark Theme',
    tokenPageBg: 'Page Background',
    tokenSidebarTopbar: 'Sidebar & Topbar',
    tokenCardSurface: 'Cards & Panels',
    tokenInputBg: 'Input Controls',
    tokenPrimaryBtn: 'Primary Action Buttons',
    tokenStatusColors: 'Status Colors (Success / Error / Warning)',
    formPreviewTitle: 'Form Controls, Dropdowns & Centralized Modal Dialog Preview',
    formPreviewDesc:
      'Create and Edit views are standalone pages (not modals), whereas system feedback and confirmations use a unified central Modal instead of bottom toasts.',
    sampleInputLabel: 'Sample Text Input (Typography & Input Surface Contrast Check)',
    sampleInputPlaceholder: 'Type here to preview Readex Pro / Outfit font and field contrast...',
    sampleInputHint: 'Input state is preserved across language, theme, and mobile sidebar toggles.',
    sampleInputRequiredError: 'This field is required when validating the form.',
    sampleCodeInputLabel: 'LTR-Isolated Technical Identifier (JetBrains Mono)',
    sampleSelectLabel: 'Shared Dropdown Select (SelectInput)',
    sampleSelectPlaceholder: 'Select a specification page pattern...',
    sampleTextareaLabel: 'Multi-line Description Input (TextareaInput)',
    sampleTextareaPlaceholder: 'Structural notes to preview the multi-line textarea control...',
    openSuccessModal: 'Preview Success Modal',
    openWarningModal: 'Preview Warning Modal',
    openErrorModal: 'Preview Error Modal',
    openConfirmModal: 'Preview Confirm Modal',
    modalConfirmBtn: 'OK',
    modalCancelBtn: 'Cancel',
    modalCloseAria: 'Close message dialog',
    tableSectionTitle: 'Shared DataTable, Search, Filter, Pagination & State Views',
    tableSectionDesc:
      'Displays the reference UX screen catalog (02_UX_UI_v1.2.md) to exercise search, category filtering, pagination, and loading/empty/error states.',
    tableSearchPlaceholder: 'Search by screen ID (e.g. UI-25) or title...',
    tableSearchAria: 'Search UX specification catalog table',
    tableFilterAria: 'Filter table by category',
    tableFilterAll: 'All Categories',
    tableColId: 'Screen ID',
    tableColTitle: 'Specification Screen Title',
    tableColRoute: 'Reference Route',
    tableColPattern: 'Approved UI Pattern',
    tableEmptyTitle: 'No matching items found',
    tableEmptyDesc: 'Try adjusting your search query or resetting the category filter.',
    tableLoadingTitle: 'Loading table data...',
    tableErrorTitle: 'Unable to load data',
    tableErrorMsg: 'Preview of the standardized ErrorState component with retry action.',
    tableRetryBtn: 'Retry',
    tableStateReadyBtn: 'Data State',
    tableStateLoadingBtn: 'Loading State',
    tableStateEmptyBtn: 'Empty State',
    tableStateErrorBtn: 'Error State',
    paginationPrev: 'Previous',
    paginationNext: 'Next',
    paginationSummaryPrefix: 'Page',
    paginationOf: 'of',
    matrixSectionTitle: 'Shared Selection Matrix & Bulk Checkboxes (SelectionMatrix)',
    matrixSectionDesc:
      'Reusable responsive matrix supporting cell, full-row, full-column, section, and select-all checkboxes with horizontal scrolling and sticky first column.',
    matrixRowHeader: 'Route / Screen',
    matrixSelectAll: 'Select All',
    matrixSelectGroup: 'Select Section',
    matrixSelectRowPrefix: 'Select row',
    matrixSelectColPrefix: 'Select column',
    matrixSelectedCountLabel: 'Selected cells in preview:',
    patternLabels: {
      list: 'List & Table',
      'standalone-form': 'Standalone Form Page',
      matrix: 'Full Matrix',
      'workspace-tabs': 'Details & Tabs',
    },
    categoryLabels: {
      navigation: 'Global Navigation',
      configuration: 'Systems & Settings',
      documents: 'API Documents',
      integrations: 'Integrations',
      administration: 'Administration',
    },
    scopeGuardTitle: 'F02 & F08 Scope Boundaries Enforced',
    scopeGuards: [
      'No mock authentication, sessions, or client-side permission logic; authorization is strictly server-side in tasks I01–I07.',
      'No duplicate page title/description block at the top of content when the title is displayed in the topbar (PRD-06).',
      'Create and Edit views are standalone pages inside the app (StandaloneFormPage), never modal popups.',
      'Collapsible sidebar with a single expanded group at a time following the approved section sequence.',
      'Language (AR/EN with RTL/LTR) and theme (Light/Dark) preferences are persisted locally.',
    ],
    modalSamples: {
      info: {
        title: 'System Information',
        message: 'This is a unified central Modal dialog dedicated to system messages and confirmations per 02_UX_UI_v1.2.md.',
      },
      success: {
        title: 'Template Verification Succeeded',
        message: 'The unified central Modal dialog operates with the approved success token (#10b981) without bottom toasts.',
      },
      warning: {
        title: 'Task F08 Scope Notice',
        message: 'This view represents the shared UI shell and component library (F02/F08) only; no data persistence or business logic exists prior to subsequent sprints.',
      },
      error: {
        title: 'Validation Error Sample',
        message: 'Critical validation errors are presented in a clear central Modal (#f43f5e) and return focus to the relevant field.',
      },
      confirm: {
        title: 'Confirm Action',
        message: 'Do you want to confirm this sample action in the centralized modal dialog preview?',
      },
    },
  },
} as const;
