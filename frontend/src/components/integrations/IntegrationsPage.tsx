import { useCallback, useEffect, useMemo, useState } from 'react';
import { ApiProblemError } from '../../api/authApi';
import {
  EligibleIntegrationDocument,
  IntegrationDetailsResponse,
  IntegrationDeveloperItem,
  IntegrationItem,
  createIntegration,
  getIntegrationDetails,
  listEligibleIntegrationDocuments,
  listIntegrationAssignableUsers,
  listIntegrations,
  replaceIntegrationDevelopers,
} from '../../api/documentsAndIntegrationsApi';
import {
  createDraftDocumentRevision,
  createIntegrationVersion,
  createWorkingCopyRevision,
  deleteDraftRevisionFile,
  downloadBinaryFileVerified,
  generateWordDerivative,
  getIntegrationFilesWorkspace,
  getRevisionFileDownloadUrl,
  getWordDerivativeDownloadUrl,
  IntegrationFilesWorkspaceResponse,
  IntegrationVersionsResponse,
  listIntegrationVersions,
  rescanRevisionFile,
  RevisionFileItem,
  submitDocumentRevision,
  uploadRevisionFile,
  WordDerivativeRecordItem,
} from '../../api/versionsFilesApi';
import {
  getWorkflowHistory,
  getWorkflowInbox,
  getWorkflowRunDetails,
  submitWorkflowStageDecision,
  WorkflowDecisionHistoryEntry,
  WorkflowDecisionKind,
  WorkflowInboxItem,
  WorkflowRunDetails,
  WorkflowRunStageItem,
} from '../../api/workflowsApi';
import { AppLanguage, UI_STRINGS } from '../../constants/uiBaseline';
import {
  Button,
  CentralModalDialog,
  DataTable,
  DataTableColumn,
  ErrorState,
  FormField,
  LoadingState,
  PaginationBar,
  SearchFilterBar,
  SelectInput,
  StandaloneFormPage,
  StatusBadge,
  TextInput,
} from '../ui';

interface IntegrationsPageProps {
  lang: AppLanguage;
  permissions: string[];
  initialMode?: 'list' | 'create';
  onSessionExpired: () => void;
}

type PageMode =
  | 'list'
  | 'create'
  | 'details'
  | 'assign-devs'
  | 'create-version'
  | 'upload-file'
  | 'create-working-copy'
  | 'workflow-inbox'
  | 'decide-stage';
type DetailsTab = 'developers' | 'files' | 'revisions' | 'versions' | 'changes' | 'audit';

interface ModalState {
  variant: 'success' | 'warning' | 'error' | 'confirm';
  title: string;
  message: string;
  traceId?: string;
  onConfirmAction?: () => void;
}

const COPY = {
  ar: {
    pageTitle: 'قائمة التكاملات (UI-22)',
    pageDesc:
      'يعرض التكاملات المرتبطة بنسبة 1:1 مع وثائق API المصرح بعرضها، مع تصفية نطاق التكليف كمطوّر والدخول لصفحة التفاصيل والتبويبات.',
    noViewTitle: 'صلاحية عرض التكاملات غير متوفرة',
    noViewDesc: 'يتطلب عرض قائمة التكاملات توفر صلاحية integrations.view.',
    noCreateTitle: 'صلاحية إنشاء التكامل غير متوفرة',
    noCreateDesc: 'يتطلب إنشاء تكامل جديد توفر صلاحية integrations.create.',
    addBtn: 'إضافة تكامل جديد (1:1)',
    searchPlaceholder: 'ابحث بالرمز المرجعي أو عنوان الوثيقة أو مجموعة API...',
    searchAria: 'البحث في التكاملات',
    scopeFilterAria: 'تصفية حسب نطاق التكليف',
    scopeAll: 'كل التكاملات المصرح بعرضها',
    scopeAssigned: 'التكاملات المكلّف بها أنا فقط',
    colRef: 'الرمز المرجعي',
    colDoc: 'الوثيقة والأطراف',
    colGroup: 'مجموعة API',
    colDevs: 'مطوّرو التكامل والنطاق',
    colVersions: 'الإصدارات',
    colActions: 'الإجراءات',
    detailsBtn: 'التفاصيل والتبويبات (UI-09)',
    assignDevsBtn: 'إدارة المطورين (D05)',
    scopeAssignedBadge: 'مكلّف كمطوّر',
    scopeViewOnlyBadge: 'عرض فقط',
    createBadge: 'إضافة تكامل · UI-08 / D04',
    detailsBadge: 'تفاصيل التكامل والتبويبات · UI-09 / D06',
    assignBadge: 'تعيين مطوري التكامل والنطاق · D05',
    createTitle: 'إنشاء تكامل مرتبط بوثيقة API (1:1)',
    detailsTitle: 'تفاصيل التكامل وملخص الأطراف',
    assignTitle: 'إدارة مطوري التكامل (D05)',
    formSub:
      'تظهر فقط وثائق API غير المرتبطة بتكامل سابق والمخولة لك بـ doc.view وdoc.manage_integration؛ ويظل تعيين المطورين منفصلًا عن صلاحيات الوثيقة.',
    assignFormSub:
      'يحدد هذا التعيين نطاق مسؤولية المطور على التكامل فقط، ولا يمنحه تلقائيًا أي صلاحية doc.* على الوثيقة (تُمنح من شاشة صلاحيات الوثيقة UI-21A).',
    backBtn: 'العودة إلى قائمة التكاملات',
    backDetailsBtn: 'العودة إلى تفاصيل التكامل',
    saveCreateBtn: 'حفظ وإنشاء التكامل',
    saveDevsBtn: 'حفظ مطوري التكامل',
    cancelBtn: 'إلغاء',
    docSelectLabel: 'وثيقة API غير المرتبطة',
    selectDocPrompt: '-- اختر وثيقة API --',
    noEligibleDocsNotice:
      'لا توجد وثائق API غير مرتبطة ومخوّلة لك بإنشاء تكامل حاليًا. أنشئ وثيقة أولًا من UI-07.',
    refCodeLabel: 'الرمز المرجعي للتكامل (اختياري — يُولّد تلقائيًا عند تركه فارغًا)',
    refCodePlaceholder: 'INT-PAYMENTS-001',
    partiesPreviewLabel: 'ملخص الأطراف ومجموعة API:',
    initialDevsLabel: 'مطورو التكامل (D05 — يتطلب doc.assign)',
    noDevsLabel: 'بدون مطورين',
    providerLabel: 'المزوّد',
    consumerLabel: 'المستهلك',
    groupLabel: 'مجموعة API',
    reqTemplateLabel: 'قالب المتطلبات',
    routeLabel: 'مسار الاعتماد',
    policyLabel: 'سياسة التغيير',
    pubOptionLabel: 'خيار النشر',
    pubManual: 'يدوي منضبط (MANUAL)',
    pubPostApproval: 'مهيأ بعد الاعتماد (POST_APPROVAL)',
    stateLabel: 'حالة الوثيقة',
    stateActive: 'فعّالة (ACTIVE)',
    stateDraft: 'مسودة (DRAFT)',
    stateArchived: 'مؤرشفة (ARCHIVED)',
    tabDevelopers: 'المطورون والنطاق',
    tabFiles: 'الملفات (UI-10)',
    tabRevisions: 'المراجعات (UI-11)',
    tabVersions: 'الإصدارات (UI-12)',
    tabChanges: 'طلبات التغيير (UI-13)',
    tabAudit: 'السجل والنشر (UI-14)',
    intDevsTitle: 'المطورون المكلّفون على هذا التكامل (D05)',
    docDevsTitle: 'المطورون المكلّفون على الوثيقة الأم (D02)',
    aclSeparationNote:
      'تنبيه الفصل المعتمد: عرض الشارات يوضّح صلاحية doc.* الفعلية لكل مطور في مصفوفة الوثيقة دون منح تلقائي.',
    aclView: 'doc.view',
    aclFiles: 'doc.files_manage',
    aclVersions: 'doc.version_manage',
    aclChange: 'doc.change',
    filesTabDesc: 'قواعد متطلبات الملفات المعتمدة لهذا التكامل (تُفعّل عمليات الرفع والفحص والتنزيل في Sprint S08/S09):',
    revisionsTabDesc: 'مراحل مسار الاعتماد المرتبط بالوثيقة (تُفعّل دورة المراجعات والقرارات في Sprint S09/S10):',
    versionsTabDesc: 'إدارة إصدارات API بالنص الحر (تُفعّل في Sprint S08):',
    changesTabDesc: 'أنواع طلبات التغيير المسموحة حسب السياسة المرتبطة (تُفعّل في Sprint S11):',
    auditTabDesc: 'ملخص النشر وسجل الأحداث الإلحاقي للتكامل والوثيقة (OP-01):',
    colFileType: 'نوع الملف',
    colExtensions: 'الامتدادات',
    colMode: 'الإلزامية',
    colCounts: 'الحد الأدنى – الأقصى',
    colStageOrder: 'الترتيب',
    colStageName: 'المرحلة',
    colStageRole: 'الدور المسؤول',
    colChangeType: 'نوع التغيير',
    colChangeRoute: 'مسار الاعتماد',
    colAuditOp: 'العملية',
    colAuditActor: 'المنفّذ',
    colAuditTime: 'التوقيت (UTC)',
    colAuditRes: 'النتيجة',
    modeRequired: 'إلزامي',
    modeOptional: 'اختياري',
    modeConditional: 'مشروط',
    countsSummary: (v: number, r: number, cr: number, pub: number) =>
      `إصدارات: ${v} · مراجعات: ${r} · طلبات تغيير: ${cr} · منشورات: ${pub}`,
    emptyRules: 'لا توجد قواعد ملفات مسجلة في القالب المرتبط.',
    emptyStages: 'لا توجد مراحل اعتماد مسجلة في المسار المرتبط.',
    emptyChangeTypes: 'لا توجد أنواع تغيير مرتبطة بالسياسة.',
    emptyAudit: 'لا توجد أحداث مسجلة بعد.',
    validationDoc: 'يرجى اختيار وثيقة API غير مرتبطة لإنشاء التكامل.',
    createdTitle: 'تم إنشاء التكامل بنجاح',
    createdMsg: 'تم ربط التكامل بالوثيقة بنسبة 1:1 وحفظ السجل.',
    devsUpdatedTitle: 'تم تحديث مطوري التكامل',
    devsUpdatedMsg: 'تم حفظ قائمة مطوري التكامل وتسجيل العملية دون تغيير مصفوفة صلاحيات الوثيقة.',
    loadingText: 'جارٍ تحميل التكاملات...',
    pageWord: 'صفحة',
    ofWord: 'من',
    // S08 V01-V04 Strings (AR)
    versionsListTitle: 'إصدارات API المسجلة (V04 — نص حر يدوي)',
    versionsCreateBtn: 'إنشاء إصدار API (V04)',
    versionsCreateBadge: 'إضافة إصدار API · UI-12 / V04',
    versionsCreateTitle: 'إنشاء إصدار API جديد برقم يدوي',
    versionsCreateSub:
      'أدخل رقم أو تسمية الإصدار بالنص الحر حسب فريق التطوير (لا يُفرض نمط SemVer). يظل هذا الإصدار منفصلًا تمامًا عن مراجعات حزم التوثيق (#1، #2...).',
    versionsLabelField: 'رقم / تسمية إصدار API (نص حر)',
    versionsLabelPlaceholder: 'مثال: V2026-Q4 أو 1.1 أو pilot-beta',
    versionsSaveBtn: 'حفظ وإنشاء الإصدار',
    versionsCreatedTitle: 'تم إنشاء إصدار API',
    versionsCreatedMsg: 'تم حفظ رقم الإصدار اليدوي بنجاح ويمكنك الآن تجهيز مسودة التوثيق ورفع الملفات له.',
    versionsValidationEmpty: 'يرجى إدخال رقم أو تسمية إصدار API.',
    versionsNoPermNote: 'لا تملك صلاحية versions.view أو إنشاء إصدارات لهذا التكامل.',
    colVerLabel: 'رقم الإصدار (نص حر)',
    colVerCreated: 'تاريخ الإنشاء',
    colVerRevCount: 'عدد المراجعات',
    colVerLatestRev: 'حالة أحدث مراجعة',
    openFilesForVersionBtn: 'ملفات هذا الإصدار',
    emptyVersions: 'لا توجد إصدارات API مسجلة لهذا التكامل بعد.',
    filesWorkspaceTitle: 'حزمة ملفات التكامل والحجر والفحص الأمني (V01–V03)',
    filesSelectVersionLabel: 'إصدار API المستهدف',
    filesInitDraftBtn: 'تهيئة مسودة حزمة توثيق (#1)',
    filesUploadBtn: 'رفع ملف جديد (V02/V03)',
    filesUploadBadge: 'رفع ملف وفحصه · UI-10 / V02-V03',
    filesUploadTitle: 'رفع ملف توثيقي داخل مسودة المراجعة',
    filesUploadSub:
      'يُحفظ الملف في مخزن خاص معزول خارج مجلدات الويب مع التحقق من الامتداد والحجم وبنية المحتوى وبصمة SHA-256، ويُفحص أمنيًا قبل إتاحته.',
    filesTypeSelectLabel: 'نوع الملف المعتمد (وفق قالب الوثيقة)',
    filesInputFileLabel: 'اختيار الملف من جهازك',
    filesSubmitUploadBtn: 'رفع وفحص وحفظ الملف',
    filesValidationMissing: 'يرجى اختيار نوع الملف وإرفاق الملف قبل الإرسال.',
    filesNoDraftNote: 'لا توجد مسودة مراجعة نشطة لهذا الإصدار بعد. أنشئ مسودة حزمة توثيق (#1) للبدء برفع الملفات.',
    filesDraftInfoBadge: (num: number, status: string) => `مسودة مراجعة #${num} · ${status}`,
    filesEmptyList: 'لم تُرفع ملفات لهذه المراجعة بعد.',
    colFileName: 'اسم الملف',
    colFileSizeSha: 'الحجم وبصمة SHA-256',
    colScanStatus: 'حالة الفحص والتخزين',
    scanClean: 'سليم ومتاح (CLEAN)',
    scanPending: 'محجور — بانتظار الفحص (PENDING_SCAN)',
    scanInfected: 'مرفوض — مصاب (INFECTED)',
    scanInvalid: 'مرفوض — غير صالح (INVALID)',
    tierPrivate: 'تخزين خاص ثابت',
    tierQuarantine: 'حجر أمني معزول',
    rescanBtn: 'إعادة الفحص',
    downloadBtn: 'تنزيل',
    removeBtn: 'إزالة',
    removeConfirmTitle: 'تأكيد إزالة الملف من المسودة',
    removeConfirmMsg: (name: string) => `هل تريد إزالة الملف (${name}) من مسودة المراجعة الحالية؟`,
    uploadCleanTitle: 'تم رفع الملف واجتياز الفحص',
    uploadCleanMsg: (name: string, sha: string) =>
      `تم فحص الملف (${name}) وحفظه في المخزن الخاص الثابت بنجاح.\nSHA-256: ${sha}`,
    uploadPendingTitle: 'تم الحجز بانتظار الفحص الأمني (Fail-Closed)',
    uploadPendingMsg: (name: string, sha: string) =>
      `تم التحقق من سلامة بنية (${name}) وعزله في الحجر الأمني بحالة PENDING_SCAN لعدم توفر محرك فحص فعلي من IT حاليًا. لن يُتاح الملف للتنزيل أو الاعتماد حتى اجتياز الفحص.\nSHA-256: ${sha}`,
    rescanStillPendingTitle: 'لا يزال الملف في الحجر (PENDING_SCAN)',
    rescanStillPendingMsg: 'محرك الفحص الأمني الفعلي غير متصل بعد؛ تم إبقاء الملف معزولًا.',
    fileRemovedTitle: 'تمت إزالة الملف',
    fileRemovedMsg: 'تم حذف ارتباط الملف من مسودة المراجعة وتسجيل الحدث.',
    draftCreatedTitle: 'تمت تهيئة مسودة المراجعة',
    draftCreatedMsg: 'تم فتح مسودة حزمة توثيق (#1) مستقلة عن رقم إصدار API لرفع الملفات.',
    // S09 V05-V08 Strings (AR)
    submitRevisionBtn: 'تقديم المراجعة للاعتماد (V05)',
    submitRevisionConfirmTitle: 'تأكيد تقديم حزمة التوثيق (V05)',
    submitRevisionConfirmMsg: (revNum: number) =>
      `سيتم التحقق من اكتمال جميع الملفات الإلزامية وحالة الفحص ثم قفل المراجعة #${revNum} بحالة SUBMITTED وإحالتها لمسار المراجعة. هل تريد المتابعة؟`,
    submitRevisionSuccessTitle: 'تم تقديم المراجعة بنجاح',
    submitRevisionSuccessMsg: (revNum: number, cleanCount: number) =>
      `تم تقديم حزمة التوثيق #${revNum} بحالة SUBMITTED مع ${cleanCount} ملفًا سليمًا، وأصبحت المراجعة محمية من التعديل (V06).`,
    requirementsChecklistTitle: 'تقدم استيفاء الملفات الإلزامية قبل التقديم (V05)',
    reqSatisfiedBadge: 'مكتمل',
    reqMissingBadge: 'ناقص',
    reqExceedsBadge: 'تجاوز الحد الأقصى',
    protectedRevisionBanner: (status: string) =>
      `هذه المراجعة في حالة ${status} ومحمية بالكامل من رفع أو حذف أو تعديل الملفات (V06). لبدء تعديل بعد الاعتماد، أنشئ نسخة عمل جديدة مرتبطة بطلب تغيير معتمد.`,
    createWorkingCopyBtn: 'إنشاء نسخة عمل جديدة (V06)',
    workingCopyBadge: 'نسخة عمل من مراجعة معتمدة · UI-11 / V06 · OP-05',
    workingCopyTitle: 'إنشاء نسخة عمل جديدة من مراجعة معتمدة',
    workingCopySub:
      'تُفتح نسخة العمل (#التالي) استنادًا إلى مراجعة معتمدة (APPROVED) وطلب تغيير معتمد، مع نسخ الملفات السليمة غير المشمولة بالتغيير ومنع فتح أكثر من مسودة نشطة لنفس الإصدار (OP-05).',
    workingCopyBaseLabel: 'المراجعة المرجعية المعتمدة (Base Revision)',
    workingCopyCrLabel: 'طلب التغيير المعتمد (Approved Change Request)',
    workingCopyNoApprovedBase: 'لا توجد مراجعة معتمدة (APPROVED) لهذا الإصدار بعد.',
    workingCopyNoApprovedCr: 'لا توجد طلبات تغيير معتمدة مرتبطة بمراجعة معتمدة لهذا الإصدار بعد.',
    workingCopySubmitBtn: 'فتح نسخة العمل',
    workingCopyCreatedTitle: 'تم فتح نسخة العمل بنجاح',
    workingCopyCreatedMsg: (revNum: number) =>
      `تم إنشاء مسودة نسخة العمل #${revNum} مع نسخ الملفات الموروثة من المراجعة المعتمدة وتسجيل الحدث.`,
    workingCopyValidationMissing: 'يرجى اختيار المراجعة المعتمدة وطلب التغيير المعتمد قبل الإرسال.',
    revisionsHistoryTitle: 'سجل مراجعات حزمة التوثيق لهذا الإصدار (UI-11 / V05–V06)',
    colRevNumber: 'رقم المراجعة',
    colRevStatus: 'الحالة والحماية',
    colRevLineage: 'الأساس وطلب التغيير',
    colRevFiles: 'الملفات السليمة / قيد الفحص',
    selectRevisionBtn: 'فتح الحزمة',
    selectedRevisionTag: 'المعروضة حاليًا',
    protectedTag: 'محمية (قراءة فقط)',
    editableDraftTag: 'مسودة قابلة للتعديل',
    verifiedDownloadNotice: (name: string, sha: string) =>
      `تم التحقق من الصلاحية وبصمة SHA-256 وتدقيق تنزيل (${name}) — ${sha.slice(0, 16)}…`,
    wordUpdateBtn: 'تحديث حقول Word (V08)',
    wordDerivativesTitle: 'نتائج إثبات جدوى تحديث حقول Word عبر Open XML SDK (V08)',
    wordDerivativesNote:
      'يتم تعديل نسخة مشتقة مستقلة مع بقاء الملف الأصلي سليمًا وبصمته دون أي مساس، ويُحظر تنزيل أي مشتق لم يجتز التحقق البنيوي والفحص الأمني (OP-03).',
    wordStatusVerified: 'مشتق متحقق وقابل للتنزيل (VERIFIED)',
    wordStatusNoFields: 'لا توجد حقول معرّفة (NO_RECOGNIZED_FIELDS)',
    wordStatusPending: 'بانتظار الفحص الأمني (PENDING_SCAN)',
    wordStatusFailed: 'فشل التوليد أو التحقق البنيوي (FAILED)',
    wordStrategyLabel: 'الآلية المكتشفة',
    wordFieldsCountLabel: 'عدد الحقول المحدّثة',
    wordSourceShaLabel: 'بصمة الملف الأصلي (ثابتة)',
    wordDerivedShaLabel: 'بصمة المشتق',
    wordDownloadDerivedBtn: 'تنزيل المشتق المتحقق',
    wordBlockedDownloadTag: 'محجوب من النشر والتنزيل (OP-03)',
    wordGeneratedTitle: 'نتيجة معالجة مشتق Word (V08)',
    wordGeneratedVerifiedMsg: (count: number, kind: string) =>
      `تم توليد مشتق .docx متحقق واجتياز OpenXmlValidator والفحص الأمني دون تعديل الملف الأصلي.\nالآلية: ${kind} · الحقول المحدّثة: ${count}`,
    wordGeneratedBlockedMsg: (status: string, detail: string) =>
      `حُفظ سجل الفحص بحالة ${status} ومُنع نشر المشتق أو تنزيله (OP-03).\nالتفصيل: ${detail}`,
    // S10 W01-W04 Workflow Strings (AR)
    workflowInboxBtn: 'صندوق المراجعات بانتظار قراري (W01–W04)',
    workflowInboxBadge: 'صندوق سير العمل المتسلسل · UI-11 / W01–W04',
    workflowInboxTitle: 'طلبات المراجعة النشطة ضمن أدواري',
    workflowInboxSub:
      'يعرض فقط المراحل النشطة (ACTIVE) المسندة لأي دور نشط تملكه ضمن الوثائق المصرح لك بالوصول إليها (ADR-01). يكفي قرار عضو مؤهل واحد لإتمام المرحلة.',
    workflowInboxEmpty: 'لا توجد حاليًا مراحل اعتماد نشطة بانتظار قرار من أدوارك.',
    colWfIntegration: 'التكامل والوثيقة',
    colWfRevision: 'الإصدار / المراجعة',
    colWfStage: 'المرحلة النشطة والدور',
    colWfSubmittedBy: 'مُقدَّمة من',
    openReviewDetailsBtn: 'فتح التبويب',
    decideStageBtn: 'اتخاذ قرار المرحلة (W02)',
    workflowRunTitle: 'مسار الاعتماد التنفيذي المجمد (W01–W04)',
    workflowSnapshotNote:
      'ملاحظة W01: نسخة المراحل هنا مجمدة لحظة التقديم، ولا تتأثر بأي تعديل لاحق في إعدادات مسار الاعتماد.',
    workflowActiveStageTag: 'المرحلة النشطة حاليًا',
    workflowReworkAlertTitle: 'إعادة للعمل (RETURNED — W04)',
    workflowRejectedAlertTitle: 'مرفوضة نهائيًا (REJECTED — W04)',
    workflowReworkInstruction:
      'يمكنك تعديل ملفات هذه المراجعة أو استبدالها ثم إعادة التقديم لتشغيل دورة مراجعة جديدة مع بقاء السجل السابق.',
    workflowDecisionBadge: 'تسجيل قرار مرحلة الاعتماد · UI-11 / W02–W04',
    workflowDecisionTitle: 'اتخاذ قرار ذري على المرحلة النشطة',
    workflowDecisionSub:
      'قرار واحد ذري لكل مرحلة. اختيار الإعادة للعمل (RETURN) أو الرفض (REJECT) يتطلب كتابة ملاحظات إلزامية.',
    decisionChoiceLabel: 'القرار المطلوب',
    decisionApproveOpt: 'اعتماد المرحلة والانتقال / الإكمال (APPROVE)',
    decisionReturnOpt: 'إعادة للعمل بملاحظات إلزامية (RETURN)',
    decisionRejectOpt: 'رفض نهائي بملاحظات إلزامية (REJECT)',
    decisionNotesLabel: 'ملاحظات المراجع (إلزامية عند الإعادة أو الرفض)',
    decisionNotesPlaceholder: 'اذكر التعديلات المطلوبة أو أسباب الرفض بوضوح...',
    decisionSubmitBtn: 'تأكيد وتسجيل القرار',
    decisionNotesRequiredErr: 'الملاحظات إلزامية عند اختيار الإعادة للعمل أو الرفض (W04).',
    decisionRecordedTitle: 'تم تسجيل قرار المرحلة بنجاح',
    decisionRecordedMsg: (outcome: string) =>
      `تم تسجيل القرار الذري للمرحلة وتحديث حالة التشغيل والمراجعة إلى: ${outcome}`,
    workflowHistoryTitle: 'سجل قرارات المراحل ومحاولات المراجعة (W04)',
    workflowHistoryEmpty: 'لم تُسجل قرارات مراحل على هذه المراجعة بعد.',
  },
  en: {
    pageTitle: 'Integrations Directory (UI-22)',
    pageDesc:
      'Displays 1:1 integrations linked to authorized API documents with developer scope filtering and multi-tab details workspace.',
    noViewTitle: 'Missing integrations view permission',
    noViewDesc: 'Viewing integrations requires integrations.view permission.',
    noCreateTitle: 'Missing integration create permission',
    noCreateDesc: 'Creating an integration requires integrations.create permission.',
    addBtn: 'Create Integration (1:1)',
    searchPlaceholder: 'Search by reference code, document title, or API group...',
    searchAria: 'Search integrations',
    scopeFilterAria: 'Filter by developer scope',
    scopeAll: 'All visible integrations',
    scopeAssigned: 'Assigned to me only',
    colRef: 'Reference Code',
    colDoc: 'Document & Parties',
    colGroup: 'API Group',
    colDevs: 'Developers & Scope',
    colVersions: 'Versions',
    colActions: 'Actions',
    detailsBtn: 'Details & Tabs (UI-09)',
    assignDevsBtn: 'Manage Developers (D05)',
    scopeAssignedBadge: 'Assigned Dev',
    scopeViewOnlyBadge: 'View Access',
    createBadge: 'Create Integration · UI-08 / D04',
    detailsBadge: 'Integration Details & Tabs · UI-09 / D06',
    assignBadge: 'Assign Integration Developers · D05',
    createTitle: 'Create 1:1 Integration from API Document',
    detailsTitle: 'Integration Summary & Parties',
    assignTitle: 'Manage Integration Developers (D05)',
    formSub:
      'Lists only unlinked API documents where you hold doc.view and doc.manage_integration; developer assignment remains independent of ACL permissions.',
    assignFormSub:
      'Assigning a user here sets integration developer scope ONLY and does not grant any doc.* ACL permissions (configure in UI-21A).',
    backBtn: 'Back to Integrations',
    backDetailsBtn: 'Back to Integration Details',
    saveCreateBtn: 'Save & Create Integration',
    saveDevsBtn: 'Save Integration Developers',
    cancelBtn: 'Cancel',
    docSelectLabel: 'Unlinked API Document',
    selectDocPrompt: '-- Select API Document --',
    noEligibleDocsNotice:
      'No unlinked authorized API documents found. Create an API document in UI-07 first.',
    refCodeLabel: 'Integration Reference Code (optional — auto-generated if blank)',
    refCodePlaceholder: 'INT-PAYMENTS-001',
    partiesPreviewLabel: 'Parties & API Group Summary:',
    initialDevsLabel: 'Integration Developers (D05 — requires doc.assign)',
    noDevsLabel: 'None assigned',
    providerLabel: 'Provider',
    consumerLabel: 'Consumer',
    groupLabel: 'API Group',
    reqTemplateLabel: 'Requirement Template',
    routeLabel: 'Approval Route',
    policyLabel: 'Change Policy',
    pubOptionLabel: 'Publication Option',
    pubManual: 'Manual Controlled (MANUAL)',
    pubPostApproval: 'Post-Approval (POST_APPROVAL)',
    stateLabel: 'Document State',
    stateActive: 'Active (ACTIVE)',
    stateDraft: 'Draft (DRAFT)',
    stateArchived: 'Archived (ARCHIVED)',
    tabDevelopers: 'Developers & Scope',
    tabFiles: 'Files (UI-10)',
    tabRevisions: 'Revisions (UI-11)',
    tabVersions: 'Versions (UI-12)',
    tabChanges: 'Change Requests (UI-13)',
    tabAudit: 'History & Publish (UI-14)',
    intDevsTitle: 'Assigned Integration Developers (D05)',
    docDevsTitle: 'Assigned Document Developers (D02)',
    aclSeparationNote:
      'Separation note: Badges reflect each developer’s actual Document ACL state from UI-21A without auto-granting.',
    aclView: 'doc.view',
    aclFiles: 'doc.files_manage',
    aclVersions: 'doc.version_manage',
    aclChange: 'doc.change',
    filesTabDesc: 'Linked file requirement rules (file uploads, scanning, and downloads activate in Sprint S08/S09):',
    revisionsTabDesc: 'Linked approval route stages (revisions & stage decisions activate in Sprint S09/S10):',
    versionsTabDesc: 'Free-text API versioning workspace (activates in Sprint S08):',
    changesTabDesc: 'Allowed change request types per linked policy (activates in Sprint S11):',
    auditTabDesc: 'Publishing summary & append-only audit trail for this integration/document (OP-01):',
    colFileType: 'File Type',
    colExtensions: 'Extensions',
    colMode: 'Requirement Mode',
    colCounts: 'Min – Max',
    colStageOrder: 'Order',
    colStageName: 'Stage Name',
    colStageRole: 'Responsible Role',
    colChangeType: 'Change Type',
    colChangeRoute: 'Approval Route',
    colAuditOp: 'Operation',
    colAuditActor: 'Actor',
    colAuditTime: 'Timestamp (UTC)',
    colAuditRes: 'Result',
    modeRequired: 'Required',
    modeOptional: 'Optional',
    modeConditional: 'Conditional',
    countsSummary: (v: number, r: number, cr: number, pub: number) =>
      `Versions: ${v} · Revisions: ${r} · Changes: ${cr} · Publications: ${pub}`,
    emptyRules: 'No file requirement rules configured on linked template.',
    emptyStages: 'No approval stages configured on linked route.',
    emptyChangeTypes: 'No change types linked to policy.',
    emptyAudit: 'No audit events recorded yet.',
    validationDoc: 'Please select an unlinked API document to create the integration.',
    createdTitle: 'Integration Created',
    createdMsg: '1:1 Integration linked to API document successfully.',
    devsUpdatedTitle: 'Integration Developers Updated',
    devsUpdatedMsg: 'Assigned integration developers updated and audited cleanly without changing Document ACL.',
    loadingText: 'Loading integrations...',
    pageWord: 'Page',
    ofWord: 'of',
    // S08 V01-V04 Strings (EN)
    versionsListTitle: 'API Versions (V04 — Manual Free-Text)',
    versionsCreateBtn: 'Create API Version (V04)',
    versionsCreateBadge: 'Create API Version · UI-12 / V04',
    versionsCreateTitle: 'Create Manual Free-Text API Version',
    versionsCreateSub:
      'Enter any developer-defined free-text version label (SemVer is NOT enforced). API versions remain strictly separate from documentation package revisions (#1, #2...).',
    versionsLabelField: 'API Version Label (free text)',
    versionsLabelPlaceholder: 'e.g. V2026-Q4, 1.1, or pilot-beta',
    versionsSaveBtn: 'Save & Create Version',
    versionsCreatedTitle: 'API Version Created',
    versionsCreatedMsg: 'Manual free-text API version saved cleanly. You can now initialize a draft package and upload files.',
    versionsValidationEmpty: 'Please enter an API version label.',
    versionsNoPermNote: 'Missing versions.view permission or read-only version scope.',
    colVerLabel: 'Version Label (Free-Text)',
    colVerCreated: 'Created At',
    colVerRevCount: 'Revisions',
    colVerLatestRev: 'Latest Revision Status',
    openFilesForVersionBtn: 'Open Version Files',
    emptyVersions: 'No API versions recorded for this integration yet.',
    filesWorkspaceTitle: 'Integration Files, Quarantine & Security Scan (V01–V03)',
    filesSelectVersionLabel: 'Target API Version',
    filesInitDraftBtn: 'Initialize Draft Package (#1)',
    filesUploadBtn: 'Upload File (V02/V03)',
    filesUploadBadge: 'Upload & Scan File · UI-10 / V02-V03',
    filesUploadTitle: 'Upload Documentation File to Draft Revision',
    filesUploadSub:
      'Stored in private non-web immutable storage with extension, size, content signature & SHA-256 verification, and fail-closed malware scanning.',
    filesTypeSelectLabel: 'Approved File Type (from Document Template)',
    filesInputFileLabel: 'Select File from Computer',
    filesSubmitUploadBtn: 'Upload, Scan & Store',
    filesValidationMissing: 'Please select both a file type and a local file.',
    filesNoDraftNote: 'No active DRAFT revision exists for this version yet. Initialize Draft Package (#1) to upload files.',
    filesDraftInfoBadge: (num: number, status: string) => `Draft Revision #${num} · ${status}`,
    filesEmptyList: 'No files uploaded to this revision yet.',
    colFileName: 'File Name',
    colFileSizeSha: 'Size & SHA-256',
    colScanStatus: 'Scan & Storage State',
    scanClean: 'Clean & Available (CLEAN)',
    scanPending: 'Quarantined — Pending Scan (PENDING_SCAN)',
    scanInfected: 'Rejected — Infected (INFECTED)',
    scanInvalid: 'Rejected — Invalid (INVALID)',
    tierPrivate: 'Private Immutable Store',
    tierQuarantine: 'Isolated Quarantine',
    rescanBtn: 'Re-scan',
    downloadBtn: 'Download',
    removeBtn: 'Remove',
    removeConfirmTitle: 'Confirm File Removal from Draft',
    removeConfirmMsg: (name: string) => `Remove file (${name}) from the current draft revision?`,
    uploadCleanTitle: 'File Uploaded & Verified Clean',
    uploadCleanMsg: (name: string, sha: string) =>
      `File (${name}) passed security scan and was committed to immutable private storage.\nSHA-256: ${sha}`,
    uploadPendingTitle: 'File Quarantined Pending Scan (Fail-Closed)',
    uploadPendingMsg: (name: string, sha: string) =>
      `File (${name}) passed structural checks and is held in quarantine as PENDING_SCAN because no real AV scan engine from IT is active. Download and approval remain blocked until scanned.\nSHA-256: ${sha}`,
    rescanStillPendingTitle: 'Still Pending Security Scan',
    rescanStillPendingMsg: 'No active AV scanner responded cleanly; file remains quarantined.',
    fileRemovedTitle: 'File Removed from Draft',
    fileRemovedMsg: 'File link removed from the draft revision and audited.',
    draftCreatedTitle: 'Draft Revision Initialized',
    draftCreatedMsg: 'Initialized independent Documentation Revision (#1) for uploading files.',
    // S09 V05-V08 Strings (EN)
    submitRevisionBtn: 'Submit Revision (V05)',
    submitRevisionConfirmTitle: 'Confirm Revision Submission (V05)',
    submitRevisionConfirmMsg: (revNum: number) =>
      `All mandatory files and security scan states will be verified, and Revision #${revNum} will be locked as SUBMITTED. Proceed?`,
    submitRevisionSuccessTitle: 'Revision Submitted',
    submitRevisionSuccessMsg: (revNum: number, cleanCount: number) =>
      `Documentation package Revision #${revNum} submitted cleanly (${cleanCount} clean file(s)) and locked from edits (V06).`,
    requirementsChecklistTitle: 'Mandatory File Readiness Checklist (V05)',
    reqSatisfiedBadge: 'Complete',
    reqMissingBadge: 'Missing',
    reqExceedsBadge: 'Exceeds Max',
    protectedRevisionBanner: (status: string) =>
      `This revision is ${status} and strictly protected from file uploads, deletions, or edits (V06). To modify after approval, create a new working copy linked to an approved change request.`,
    createWorkingCopyBtn: 'Create Working Copy (V06)',
    workingCopyBadge: 'Working Copy from Approved Revision · UI-11 / V06 · OP-05',
    workingCopyTitle: 'Create Working Copy from Approved Revision',
    workingCopySub:
      'Opens the next draft working copy (#N) branched from an APPROVED revision and an APPROVED change request, carrying forward unaffected clean files while enforcing a single active draft per version (OP-05).',
    workingCopyBaseLabel: 'Approved Base Revision',
    workingCopyCrLabel: 'Approved Change Request',
    workingCopyNoApprovedBase: 'No APPROVED revision exists for this API version yet.',
    workingCopyNoApprovedCr: 'No APPROVED change request exists for an approved revision on this version yet.',
    workingCopySubmitBtn: 'Open Working Copy',
    workingCopyCreatedTitle: 'Working Copy Opened',
    workingCopyCreatedMsg: (revNum: number) =>
      `Opened draft Working Copy Revision #${revNum} carrying forward unaffected approved files.`,
    workingCopyValidationMissing: 'Please select both an approved base revision and an approved change request.',
    revisionsHistoryTitle: 'Documentation Revisions Package History (UI-11 / V05–V06)',
    colRevNumber: 'Revision #',
    colRevStatus: 'Status & Protection',
    colRevLineage: 'Base & Change Request',
    colRevFiles: 'Clean / Pending Files',
    selectRevisionBtn: 'Open Package',
    selectedRevisionTag: 'Active View',
    protectedTag: 'Protected (Read-Only)',
    editableDraftTag: 'Editable Draft',
    verifiedDownloadNotice: (name: string, sha: string) =>
      `Verified permissions, SHA-256 integrity, and audit log for (${name}) — ${sha.slice(0, 16)}…`,
    wordUpdateBtn: 'Update Word Fields (V08)',
    wordDerivativesTitle: 'Open XML SDK Word Governance Derivative PoC (V08)',
    wordDerivativesNote:
      'Updates are written strictly to a separate derivative while keeping the original file and SHA-256 untouched. Unverified or failed derivatives are blocked from publishing/download (OP-03).',
    wordStatusVerified: 'Verified & Publishable (VERIFIED)',
    wordStatusNoFields: 'No Recognized Fields (NO_RECOGNIZED_FIELDS)',
    wordStatusPending: 'Pending Security Scan (PENDING_SCAN)',
    wordStatusFailed: 'Generation or Validation Failed (FAILED)',
    wordStrategyLabel: 'Strategy Detected',
    wordFieldsCountLabel: 'Fields Updated',
    wordSourceShaLabel: 'Original SHA-256 (Unchanged)',
    wordDerivedShaLabel: 'Derived SHA-256',
    wordDownloadDerivedBtn: 'Download Verified Derivative',
    wordBlockedDownloadTag: 'Publish / Download Blocked (OP-03)',
    wordGeneratedTitle: 'Word Derivative PoC Result (V08)',
    wordGeneratedVerifiedMsg: (count: number, kind: string) =>
      `Generated verified .docx derivative passing OpenXmlValidator and security scan without modifying the original file.\nStrategy: ${kind} · Fields updated: ${count}`,
    wordGeneratedBlockedMsg: (status: string, detail: string) =>
      `Diagnostic recorded as ${status}; derivative publish & download remain blocked (OP-03).\nDetails: ${detail}`,
    // S10 W01-W04 Workflow Strings (EN)
    workflowInboxBtn: 'Pending Approvals Inbox (W01–W04)',
    workflowInboxBadge: 'Sequential Workflow Inbox · UI-11 / W01–W04',
    workflowInboxTitle: 'Active Review Stages Across My Roles',
    workflowInboxSub:
      'Lists only ACTIVE workflow stages matching any of your active roles on authorized API documents (ADR-01). A single eligible member decision completes each stage.',
    workflowInboxEmpty: 'No active workflow stages currently await a decision from your roles.',
    colWfIntegration: 'Integration & Document',
    colWfRevision: 'Version / Revision',
    colWfStage: 'Active Stage & Role',
    colWfSubmittedBy: 'Submitted By',
    openReviewDetailsBtn: 'Open Tab',
    decideStageBtn: 'Record Stage Decision (W02)',
    workflowRunTitle: 'Frozen Approval Workflow Execution (W01–W04)',
    workflowSnapshotNote:
      'W01 Snapshot Guarantee: Stage order & assigned roles were frozen upon submission and remain immune to subsequent route template edits.',
    workflowActiveStageTag: 'Currently Active Stage',
    workflowReworkAlertTitle: 'Returned for Rework (RETURNED — W04)',
    workflowRejectedAlertTitle: 'Workflow Rejected (REJECTED — W04)',
    workflowReworkInstruction:
      'You may update/replace files on this returned revision and re-submit to initiate a new review run while preserving prior decision history.',
    workflowDecisionBadge: 'Record Stage Decision · UI-11 / W02–W04',
    workflowDecisionTitle: 'Atomic Stage Decision',
    workflowDecisionSub:
      'Only one atomic decision is permitted per stage. Returning for rework (RETURN) or rejecting (REJECT) requires mandatory reviewer notes.',
    decisionChoiceLabel: 'Stage Decision',
    decisionApproveOpt: 'Approve & Advance / Complete (APPROVE)',
    decisionReturnOpt: 'Return for Rework with Mandatory Notes (RETURN)',
    decisionRejectOpt: 'Reject with Mandatory Notes (REJECT)',
    decisionNotesLabel: 'Reviewer Notes (Mandatory for RETURN or REJECT)',
    decisionNotesPlaceholder: 'Specify required fixes or rejection rationale...',
    decisionSubmitBtn: 'Confirm & Submit Decision',
    decisionNotesRequiredErr: 'Reviewer notes are mandatory when returning for rework or rejecting (W04).',
    decisionRecordedTitle: 'Stage Decision Recorded',
    decisionRecordedMsg: (outcome: string) =>
      `Atomic stage decision saved cleanly. Current workflow / revision outcome: ${outcome}`,
    workflowHistoryTitle: 'Stage Decisions & Rework Attempts History (W04)',
    workflowHistoryEmpty: 'No stage decisions have been recorded for this revision yet.',
  },
} as const;

export function IntegrationsPage({
  lang,
  permissions,
  initialMode = 'list',
  onSessionExpired,
}: IntegrationsPageProps) {
  const c = COPY[lang];
  const commonStrings = UI_STRINGS[lang];
  const permSet = useMemo(() => new Set(permissions), [permissions]);
  const canViewGlobal = permSet.has('integrations.view');
  const canCreateGlobal = permSet.has('integrations.create');
  const canAssignGlobal = permSet.has('integrations.assign_developer');
  const canViewVersionsGlobal = permSet.has('versions.view');
  const canViewFilesGlobal = permSet.has('files.view');
  const canViewWorkflowsGlobal = permSet.has('workflows.view');
  const canDecideWorkflowsGlobal = permSet.has('workflows.decide');

  const [mode, setMode] = useState<PageMode>(initialMode);
  useEffect(() => {
    setMode(initialMode);
  }, [initialMode]);

  const [activeTab, setActiveTab] = useState<DetailsTab>('developers');
  const [searchQuery, setSearchQuery] = useState('');
  const [scopeFilter, setScopeFilter] = useState('');
  const [page, setPage] = useState(1);
  const [items, setItems] = useState<IntegrationItem[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [totalPages, setTotalPages] = useState(1);
  const [loading, setLoading] = useState(false);
  const [errorMsg, setErrorMsg] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [modal, setModal] = useState<ModalState | null>(null);

  const [eligibleDocs, setEligibleDocs] = useState<EligibleIntegrationDocument[]>([]);
  const [assignableUsers, setAssignableUsers] = useState<Array<{ id: string; fullName: string; email: string }>>([]);
  const [selectedDocId, setSelectedDocId] = useState('');
  const [refCode, setRefCode] = useState('');
  const [selectedDevIds, setSelectedDevIds] = useState<Set<string>>(new Set());
  const [details, setDetails] = useState<IntegrationDetailsResponse | null>(null);
  const [detailsLoading, setDetailsLoading] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);

  // S08 / S09 Versions, Revisions & Files workspace state
  const [versionsData, setVersionsData] = useState<IntegrationVersionsResponse | null>(null);
  const [versionsLoading, setVersionsLoading] = useState(false);
  const [filesWorkspace, setFilesWorkspace] = useState<IntegrationFilesWorkspaceResponse | null>(null);
  const [filesLoading, setFilesLoading] = useState(false);
  const [selectedWorkspaceVersionId, setSelectedWorkspaceVersionId] = useState('');
  const [selectedWorkspaceRevisionId, setSelectedWorkspaceRevisionId] = useState('');
  const [newVersionLabel, setNewVersionLabel] = useState('');
  const [selectedUploadFileTypeId, setSelectedUploadFileTypeId] = useState('');
  const [selectedLocalFile, setSelectedLocalFile] = useState<File | null>(null);
  const [fileInputResetKey, setFileInputResetKey] = useState(0);
  const [workingCopyBaseRevisionId, setWorkingCopyBaseRevisionId] = useState('');
  const [workingCopyChangeRequestId, setWorkingCopyChangeRequestId] = useState('');
  const [verifiedDownloadNotice, setVerifiedDownloadNotice] = useState<string | null>(null);
  // S10 W01-W04 Workflow Engine state
  const [workflowInbox, setWorkflowInbox] = useState<WorkflowInboxItem[]>([]);
  const [inboxLoading, setInboxLoading] = useState(false);
  const [selectedWorkflowRun, setSelectedWorkflowRun] = useState<WorkflowRunDetails | null>(null);
  const [workflowHistory, setWorkflowHistory] = useState<WorkflowDecisionHistoryEntry[]>([]);
  const [workflowLoading, setWorkflowLoading] = useState(false);
  const [decisionRunId, setDecisionRunId] = useState('');
  const [decisionStage, setDecisionStage] = useState<WorkflowRunStageItem | null>(null);
  const [decisionTitleContext, setDecisionTitleContext] = useState('');
  const [decisionKind, setDecisionKind] = useState<WorkflowDecisionKind>('APPROVE');
  const [decisionNotes, setDecisionNotes] = useState('');
  const [decisionReturnTarget, setDecisionReturnTarget] = useState<'details' | 'workflow-inbox'>('details');

  const handleApiError = useCallback(
    (err: unknown, fallbackTitle: string) => {
      if (err instanceof ApiProblemError && err.status === 401) {
        onSessionExpired();
        return;
      }
      setModal({
        variant: 'error',
        title: fallbackTitle,
        message: err instanceof Error ? err.message : fallbackTitle,
        traceId: err instanceof ApiProblemError ? err.traceId : undefined,
      });
    },
    [onSessionExpired]
  );

  const loadIntegrations = useCallback(async () => {
    if (!canViewGlobal) return;
    setLoading(true);
    setErrorMsg(null);
    try {
      const assignedToMe = scopeFilter === 'assigned' ? true : null;
      const res = await listIntegrations(
        { page, pageSize: 10, search: searchQuery, assignedToMe },
        lang
      );
      setItems(res.items);
      setTotalCount(res.totalCount);
      setTotalPages(Math.max(1, res.totalPages));
    } catch (err) {
      if (err instanceof ApiProblemError && err.status === 401) {
        onSessionExpired();
        return;
      }
      setErrorMsg(err instanceof Error ? err.message : 'Load error');
    } finally {
      setLoading(false);
    }
  }, [canViewGlobal, lang, onSessionExpired, page, scopeFilter, searchQuery]);

  const loadCreateLookups = useCallback(async () => {
    if (!canCreateGlobal && !canViewGlobal) return;
    try {
      const [docs, users] = await Promise.all([
        canCreateGlobal ? listEligibleIntegrationDocuments(lang) : Promise.resolve([]),
        canViewGlobal || canCreateGlobal ? listIntegrationAssignableUsers(lang) : Promise.resolve([]),
      ]);
      setEligibleDocs(docs);
      setAssignableUsers(users);
      setSelectedDocId((prev) => prev || docs[0]?.documentId || '');
    } catch {
      // Secondary load failures surface on save attempt
    }
  }, [canCreateGlobal, canViewGlobal, lang]);

  useEffect(() => {
    void loadIntegrations();
  }, [loadIntegrations]);

  useEffect(() => {
    void loadCreateLookups();
  }, [loadCreateLookups]);

  const openCreate = () => {
    setFormError(null);
    setRefCode('');
    setSelectedDevIds(new Set());
    void loadCreateLookups();
    setMode('create');
  };

  const loadVersionsTab = useCallback(
    async (integrationId: string) => {
      if (!canViewVersionsGlobal) {
        setVersionsData(null);
        return;
      }
      setVersionsLoading(true);
      try {
        const res = await listIntegrationVersions(integrationId, lang);
        setVersionsData(res);
      } catch {
        setVersionsData(null);
      } finally {
        setVersionsLoading(false);
      }
    },
    [canViewVersionsGlobal, lang]
  );

  const loadWorkflowForRun = useCallback(
    async (runId?: string | null) => {
      if (!canViewWorkflowsGlobal || !runId) {
        setSelectedWorkflowRun(null);
        setWorkflowHistory([]);
        return;
      }
      setWorkflowLoading(true);
      try {
        const [runDetails, historyRes] = await Promise.all([
          getWorkflowRunDetails(runId, lang),
          getWorkflowHistory(runId, lang),
        ]);
        setSelectedWorkflowRun(runDetails);
        setWorkflowHistory(historyRes.entries);
      } catch {
        setSelectedWorkflowRun(null);
        setWorkflowHistory([]);
      } finally {
        setWorkflowLoading(false);
      }
    },
    [canViewWorkflowsGlobal, lang]
  );

  const loadFilesWorkspaceTab = useCallback(
    async (integrationId: string, versionId?: string, revisionId?: string) => {
      if (!canViewFilesGlobal) {
        setFilesWorkspace(null);
        setSelectedWorkflowRun(null);
        setWorkflowHistory([]);
        return;
      }
      setFilesLoading(true);
      try {
        const res = await getIntegrationFilesWorkspace(
          integrationId,
          { versionId, revisionId },
          lang
        );
        setFilesWorkspace(res);
        setSelectedWorkspaceVersionId(res.selectedVersionId ?? '');
        setSelectedWorkspaceRevisionId(res.selectedRevision?.id ?? '');
        await loadWorkflowForRun(res.selectedRevision?.latestWorkflowRunId);
      } catch {
        setFilesWorkspace(null);
        setSelectedWorkflowRun(null);
        setWorkflowHistory([]);
      } finally {
        setFilesLoading(false);
      }
    },
    [canViewFilesGlobal, lang, loadWorkflowForRun]
  );

  useEffect(() => {
    if (mode !== 'details' || !details?.summary.id) return;
    if (activeTab === 'versions') {
      void loadVersionsTab(details.summary.id);
    } else if (activeTab === 'files' || activeTab === 'revisions') {
      void loadFilesWorkspaceTab(
        details.summary.id,
        selectedWorkspaceVersionId || undefined,
        selectedWorkspaceRevisionId || undefined
      );
    }
  }, [
    activeTab,
    details?.summary.id,
    loadFilesWorkspaceTab,
    loadVersionsTab,
    mode,
    selectedWorkspaceRevisionId,
    selectedWorkspaceVersionId,
  ]);

  const openDetails = async (integrationId: string, initialTab: DetailsTab = 'developers') => {
    setMode('details');
    setActiveTab(initialTab);
    setVersionsData(null);
    setFilesWorkspace(null);
    setSelectedWorkspaceVersionId('');
    setSelectedWorkspaceRevisionId('');
    setVerifiedDownloadNotice(null);
    setDetailsLoading(true);
    try {
      const full = await getIntegrationDetails(integrationId, lang);
      setDetails(full);
    } catch (err) {
      setMode('list');
      handleApiError(err, c.detailsTitle);
    } finally {
      setDetailsLoading(false);
    }
  };

  const openAssignDevelopers = async (row: IntegrationItem) => {
    setSelectedDevIds(new Set((row.assignedDevelopers ?? []).map((d) => d.userId)));
    setFormError(null);
    setMode('assign-devs');
    setDetailsLoading(true);
    try {
      const full = await getIntegrationDetails(row.id, lang);
      setDetails(full);
      setSelectedDevIds(new Set(full.summary.assignedDevelopers.map((d) => d.userId)));
      if (assignableUsers.length === 0) {
        const users = await listIntegrationAssignableUsers(lang);
        setAssignableUsers(users);
      }
    } catch (err) {
      setMode('list');
      handleApiError(err, c.assignTitle);
    } finally {
      setDetailsLoading(false);
    }
  };

  const selectedEligibleDoc = useMemo(
    () => eligibleDocs.find((d) => d.documentId === selectedDocId) ?? null,
    [eligibleDocs, selectedDocId]
  );

  const handleCreateSubmit = async () => {
    setFormError(null);
    if (!selectedDocId) {
      setFormError(c.validationDoc);
      return;
    }

    setSubmitting(true);
    try {
      await createIntegration(
        {
          documentId: selectedDocId,
          referenceCode: refCode.trim() ? refCode.trim() : null,
          developerUserIds: Array.from(selectedDevIds),
        },
        lang
      );
      setModal({ variant: 'success', title: c.createdTitle, message: c.createdMsg });
      setMode('list');
      await Promise.all([loadIntegrations(), loadCreateLookups()]);
    } catch (err) {
      handleApiError(err, c.createTitle);
    } finally {
      setSubmitting(false);
    }
  };

  const handleSaveDevelopers = async () => {
    if (!details) return;
    setSubmitting(true);
    try {
      await replaceIntegrationDevelopers(details.summary.id, Array.from(selectedDevIds), lang);
      setModal({ variant: 'success', title: c.devsUpdatedTitle, message: c.devsUpdatedMsg });
      const refreshed = await getIntegrationDetails(details.summary.id, lang);
      setDetails(refreshed);
      setMode('details');
      setActiveTab('developers');
      await loadIntegrations();
    } catch (err) {
      handleApiError(err, c.assignTitle);
    } finally {
      setSubmitting(false);
    }
  };

  const openCreateVersion = () => {
    setFormError(null);
    setNewVersionLabel('');
    setMode('create-version');
  };

  const handleCreateVersionSubmit = async () => {
    if (!details) return;
    setFormError(null);
    if (!newVersionLabel.trim()) {
      setFormError(c.versionsValidationEmpty);
      return;
    }

    setSubmitting(true);
    try {
      const created = await createIntegrationVersion(details.summary.id, newVersionLabel.trim(), lang);
      setSelectedWorkspaceVersionId(created.id);
      setModal({ variant: 'success', title: c.versionsCreatedTitle, message: c.versionsCreatedMsg });
      const refreshed = await getIntegrationDetails(details.summary.id, lang);
      setDetails(refreshed);
      setMode('details');
      setActiveTab('versions');
      await Promise.all([loadVersionsTab(details.summary.id), loadIntegrations()]);
    } catch (err) {
      handleApiError(err, c.versionsCreateTitle);
    } finally {
      setSubmitting(false);
    }
  };

  const handleInitializeDraftRevision = async (versionId: string) => {
    if (!details) return;
    setSubmitting(true);
    try {
      await createDraftDocumentRevision(versionId, lang);
      setModal({ variant: 'success', title: c.draftCreatedTitle, message: c.draftCreatedMsg });
      const refreshed = await getIntegrationDetails(details.summary.id, lang);
      setDetails(refreshed);
      await loadFilesWorkspaceTab(details.summary.id, versionId);
    } catch (err) {
      handleApiError(err, c.filesInitDraftBtn);
    } finally {
      setSubmitting(false);
    }
  };

  const openUploadFile = () => {
    setFormError(null);
    setSelectedLocalFile(null);
    setFileInputResetKey((k) => k + 1);
    const firstTypeId =
      filesWorkspace?.selectedRevision?.requirements[0]?.fileTypeId ??
      details?.requirementRules[0]?.fileTypeId ??
      '';
    setSelectedUploadFileTypeId(firstTypeId);
    setMode('upload-file');
  };

  const handleUploadFileSubmit = async () => {
    if (!details || !filesWorkspace?.selectedRevision) return;
    setFormError(null);
    if (!selectedUploadFileTypeId || !selectedLocalFile) {
      setFormError(c.filesValidationMissing);
      return;
    }

    setSubmitting(true);
    try {
      const result = await uploadRevisionFile(
        filesWorkspace.selectedRevision.id,
        selectedUploadFileTypeId,
        selectedLocalFile,
        lang
      );

      if (result.statusCode === 202 || result.file.scanStatus === 'PENDING_SCAN') {
        setModal({
          variant: 'warning',
          title: c.uploadPendingTitle,
          message: c.uploadPendingMsg(result.file.originalName, result.file.sha256),
        });
      } else {
        setModal({
          variant: 'success',
          title: c.uploadCleanTitle,
          message: c.uploadCleanMsg(result.file.originalName, result.file.sha256),
        });
      }

      setMode('details');
      setActiveTab('files');
      await loadFilesWorkspaceTab(details.summary.id, selectedWorkspaceVersionId || undefined);
    } catch (err) {
      handleApiError(err, c.filesUploadTitle);
    } finally {
      setSubmitting(false);
    }
  };

  const handleRescanFile = async (file: RevisionFileItem) => {
    if (!details) return;
    setSubmitting(true);
    try {
      const result = await rescanRevisionFile(file.revisionId, file.id, lang);
      if (result.statusCode === 202 || result.file.scanStatus === 'PENDING_SCAN') {
        setModal({
          variant: 'warning',
          title: c.rescanStillPendingTitle,
          message: c.rescanStillPendingMsg,
        });
      } else {
        setModal({
          variant: 'success',
          title: c.uploadCleanTitle,
          message: c.uploadCleanMsg(result.file.originalName, result.file.sha256),
        });
      }
      await loadFilesWorkspaceTab(details.summary.id, selectedWorkspaceVersionId || undefined);
    } catch (err) {
      handleApiError(err, c.rescanBtn);
      await loadFilesWorkspaceTab(details.summary.id, selectedWorkspaceVersionId || undefined);
    } finally {
      setSubmitting(false);
    }
  };

  const promptRemoveFile = (file: RevisionFileItem) => {
    if (!details) return;
    setModal({
      variant: 'confirm',
      title: c.removeConfirmTitle,
      message: c.removeConfirmMsg(file.originalName),
      onConfirmAction: () => {
        void (async () => {
          setSubmitting(true);
          try {
            await deleteDraftRevisionFile(file.revisionId, file.id, lang);
            setModal({ variant: 'success', title: c.fileRemovedTitle, message: c.fileRemovedMsg });
            await loadFilesWorkspaceTab(details.summary.id, selectedWorkspaceVersionId || undefined);
          } catch (err) {
            handleApiError(err, c.removeConfirmTitle);
          } finally {
            setSubmitting(false);
          }
        })();
      },
    });
  };

  const promptSubmitRevision = (revisionId: string, revisionNumber: number) => {
    if (!details) return;
    setModal({
      variant: 'confirm',
      title: c.submitRevisionConfirmTitle,
      message: c.submitRevisionConfirmMsg(revisionNumber),
      onConfirmAction: () => {
        void (async () => {
          setSubmitting(true);
          try {
            const result = await submitDocumentRevision(revisionId, lang);
            setModal({
              variant: 'success',
              title: c.submitRevisionSuccessTitle,
              message: c.submitRevisionSuccessMsg(result.revision.revisionNumber, result.cleanFilesCount),
            });
            const refreshed = await getIntegrationDetails(details.summary.id, lang);
            setDetails(refreshed);
            await loadFilesWorkspaceTab(details.summary.id, selectedWorkspaceVersionId || undefined, result.revision.id);
          } catch (err) {
            handleApiError(err, c.submitRevisionConfirmTitle);
          } finally {
            setSubmitting(false);
          }
        })();
      },
    });
  };

  const openCreateWorkingCopy = (preferredBaseId?: string) => {
    setFormError(null);
    const approvedRevs = (filesWorkspace?.revisions ?? []).filter((r) => r.status === 'APPROVED');
    const chosenBaseId =
      preferredBaseId && approvedRevs.some((r) => r.id === preferredBaseId)
        ? preferredBaseId
        : approvedRevs[0]?.id ?? '';
    setWorkingCopyBaseRevisionId(chosenBaseId);

    const matchingCrs = (filesWorkspace?.approvedChangeRequests ?? []).filter(
      (cr) => !chosenBaseId || cr.sourceRevisionId === chosenBaseId
    );
    setWorkingCopyChangeRequestId(matchingCrs[0]?.id ?? '');
    setMode('create-working-copy');
  };

  const handleCreateWorkingCopySubmit = async () => {
    if (!details || !selectedWorkspaceVersionId) return;
    setFormError(null);
    if (!workingCopyBaseRevisionId || !workingCopyChangeRequestId) {
      setFormError(c.workingCopyValidationMissing);
      return;
    }

    setSubmitting(true);
    try {
      const created = await createWorkingCopyRevision(
        selectedWorkspaceVersionId,
        workingCopyBaseRevisionId,
        workingCopyChangeRequestId,
        lang
      );
      setSelectedWorkspaceRevisionId(created.id);
      setModal({
        variant: 'success',
        title: c.workingCopyCreatedTitle,
        message: c.workingCopyCreatedMsg(created.revisionNumber),
      });
      const refreshed = await getIntegrationDetails(details.summary.id, lang);
      setDetails(refreshed);
      setMode('details');
      setActiveTab('files');
      await loadFilesWorkspaceTab(details.summary.id, selectedWorkspaceVersionId, created.id);
    } catch (err) {
      handleApiError(err, c.workingCopyTitle);
    } finally {
      setSubmitting(false);
    }
  };

  const handleDownloadRevisionFile = async (file: RevisionFileItem) => {
    try {
      const result = await downloadBinaryFileVerified(
        getRevisionFileDownloadUrl(file.revisionId, file.id),
        file.originalName,
        lang
      );
      setVerifiedDownloadNotice(
        c.verifiedDownloadNotice(result.filename || file.originalName, result.sha256 || file.sha256)
      );
    } catch (err) {
      handleApiError(err, c.downloadBtn);
    }
  };

  const handleGenerateWordDerivative = async (file: RevisionFileItem) => {
    if (!details) return;
    setSubmitting(true);
    try {
      const record = await generateWordDerivative(file.revisionId, file.id, lang);
      if (record.isPublishable) {
        setModal({
          variant: 'success',
          title: c.wordGeneratedTitle,
          message: c.wordGeneratedVerifiedMsg(record.fieldsUpdatedCount, record.templateKindDetected),
        });
      } else {
        const detail =
          record.validationErrors.length > 0
            ? record.validationErrors.join(' | ')
            : record.errorMessage || record.templateKindDetected;
        setModal({
          variant: 'warning',
          title: c.wordGeneratedTitle,
          message: c.wordGeneratedBlockedMsg(record.status, detail),
        });
      }
      await loadFilesWorkspaceTab(details.summary.id, selectedWorkspaceVersionId || undefined, file.revisionId);
    } catch (err) {
      handleApiError(err, c.wordUpdateBtn);
    } finally {
      setSubmitting(false);
    }
  };

  const handleDownloadWordDerivative = async (record: WordDerivativeRecordItem, fallbackName: string) => {
    try {
      const result = await downloadBinaryFileVerified(
        getWordDerivativeDownloadUrl(record.revisionId, record.revisionFileId),
        fallbackName,
        lang
      );
      setVerifiedDownloadNotice(
        c.verifiedDownloadNotice(result.filename || fallbackName, result.sha256 || record.derivedSha256 || '')
      );
    } catch (err) {
      handleApiError(err, c.wordDownloadDerivedBtn);
    }
  };

  const formatFileSize = (bytes: number) => {
    if (bytes >= 1024 * 1024) return `${(bytes / (1024 * 1024)).toFixed(2)} MB`;
    if (bytes >= 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${bytes} B`;
  };

  const loadWorkflowInbox = useCallback(async () => {
    if (!canViewWorkflowsGlobal) return;
    setInboxLoading(true);
    try {
      const res = await getWorkflowInbox(lang);
      setWorkflowInbox(res.items);
    } catch (err) {
      handleApiError(err, c.workflowInboxTitle);
    } finally {
      setInboxLoading(false);
    }
  }, [canViewWorkflowsGlobal, c.workflowInboxTitle, handleApiError, lang]);

  const openWorkflowInbox = () => {
    setMode('workflow-inbox');
    void loadWorkflowInbox();
  };

  const openDecideStage = (
    runId: string,
    stage: WorkflowRunStageItem,
    titleContext: string,
    returnTarget: 'details' | 'workflow-inbox'
  ) => {
    setFormError(null);
    setDecisionRunId(runId);
    setDecisionStage(stage);
    setDecisionTitleContext(titleContext);
    setDecisionKind('APPROVE');
    setDecisionNotes('');
    setDecisionReturnTarget(returnTarget);
    setMode('decide-stage');
  };

  const handleSubmitStageDecision = async () => {
    if (!decisionRunId || !decisionStage) return;
    setFormError(null);
    const trimmed = decisionNotes.trim();
    if ((decisionKind === 'RETURN' || decisionKind === 'REJECT') && !trimmed) {
      setFormError(c.decisionNotesRequiredErr);
      return;
    }

    setSubmitting(true);
    try {
      const res = await submitWorkflowStageDecision(
        decisionRunId,
        decisionStage.id,
        { decision: decisionKind, notes: trimmed || null },
        lang
      );
      setModal({
        variant: 'success',
        title: c.decisionRecordedTitle,
        message: c.decisionRecordedMsg(res.outcomeStatus),
      });

      if (decisionReturnTarget === 'workflow-inbox') {
        setMode('workflow-inbox');
        await loadWorkflowInbox();
      } else if (details) {
        const refreshed = await getIntegrationDetails(details.summary.id, lang);
        setDetails(refreshed);
        setMode('details');
        setActiveTab('revisions');
        await loadFilesWorkspaceTab(
          details.summary.id,
          selectedWorkspaceVersionId || undefined,
          selectedWorkspaceRevisionId || undefined
        );
      } else {
        setMode('list');
      }
    } catch (err) {
      handleApiError(err, c.workflowDecisionTitle);
    } finally {
      setSubmitting(false);
    }
  };

  const renderScanStatusBadge = (status: string) => {
    switch (status) {
      case 'CLEAN':
        return <StatusBadge variant="success">{c.scanClean}</StatusBadge>;
      case 'PENDING_SCAN':
        return <StatusBadge variant="warning">{c.scanPending}</StatusBadge>;
      case 'INFECTED':
        return <StatusBadge variant="danger">{c.scanInfected}</StatusBadge>;
      default:
        return <StatusBadge variant="danger">{c.scanInvalid}</StatusBadge>;
    }
  };
  const formatStateBadge = (rawState: string) => {
    switch (rawState.toUpperCase()) {
      case 'ACTIVE':
        return { variant: 'success' as const, label: c.stateActive };
      case 'DRAFT':
        return { variant: 'warning' as const, label: c.stateDraft };
      default:
        return { variant: 'danger' as const, label: c.stateArchived };
    }
  };

  const formatModeLabel = (rawMode: string) => {
    switch (rawMode.toUpperCase()) {
      case 'REQUIRED':
        return c.modeRequired;
      case 'OPTIONAL':
        return c.modeOptional;
      default:
        return c.modeConditional;
    }
  };

  if (mode === 'create' && !canCreateGlobal) {
    return (
      <section className="surface-card">
        <ErrorState title={c.noCreateTitle} message={c.noCreateDesc} />
      </section>
    );
  }

  if (mode !== 'create' && !canViewGlobal) {
    return (
      <section className="surface-card">
        <ErrorState title={c.noViewTitle} message={c.noViewDesc} />
      </section>
    );
  }

  const renderDeveloperRow = (dev: IntegrationDeveloperItem) => (
    <tr key={dev.userId}>
      <td>
        <strong>{dev.fullName}</strong>
        <div className="muted-cell-text">
          <code className="ltr-code">{dev.email}</code>
        </div>
      </td>
      <td>
        <div className="badge-wrap-list">
          <StatusBadge variant={dev.hasDocView ? 'success' : 'danger'}>
            {`${c.aclView}: ${dev.hasDocView ? '✓' : '✕'}`}
          </StatusBadge>
          <StatusBadge variant={dev.hasDocFilesManage ? 'success' : 'neutral'}>
            {`${c.aclFiles}: ${dev.hasDocFilesManage ? '✓' : '✕'}`}
          </StatusBadge>
          <StatusBadge variant={dev.hasDocVersionManage ? 'success' : 'neutral'}>
            {`${c.aclVersions}: ${dev.hasDocVersionManage ? '✓' : '✕'}`}
          </StatusBadge>
          <StatusBadge variant={dev.hasDocChange ? 'success' : 'neutral'}>
            {`${c.aclChange}: ${dev.hasDocChange ? '✓' : '✕'}`}
          </StatusBadge>
        </div>
      </td>
    </tr>
  );

  const columns: DataTableColumn<IntegrationItem>[] = [
    {
      key: 'ref',
      header: c.colRef,
      render: (row) => <code className="ltr-code">{row.referenceCode}</code>,
    },
    {
      key: 'doc',
      header: c.colDoc,
      render: (row) => {
        const st = formatStateBadge(row.documentState);
        return (
          <div>
            <strong>{row.derivedTitle}</strong>
            <div className="badge-wrap-list" style={{ marginTop: 4 }}>
              <StatusBadge variant={st.variant}>{st.label}</StatusBadge>
              <StatusBadge variant={row.isCurrentUserAssignedDeveloper ? 'info' : 'neutral'}>
                {row.isCurrentUserAssignedDeveloper ? c.scopeAssignedBadge : c.scopeViewOnlyBadge}
              </StatusBadge>
            </div>
          </div>
        );
      },
    },
    { key: 'group', header: c.colGroup, render: (row) => row.apiGroupName },
    {
      key: 'devs',
      header: c.colDevs,
      render: (row) =>
        (row.assignedDevelopers ?? []).length === 0 ? (
          <span className="muted-cell-text">{c.noDevsLabel}</span>
        ) : (
          <div className="badge-wrap-list">
            {(row.assignedDevelopers ?? []).map((dev) => (
              <StatusBadge key={dev.userId} variant="info">
                {dev.fullName}
              </StatusBadge>
            ))}
          </div>
        ),
    },
    {
      key: 'versions',
      header: c.colVersions,
      render: (row) => <code className="ltr-code">{row.versionsCount}</code>,
    },
    {
      key: 'actions',
      header: c.colActions,
      render: (row) => {
        const myDocPerms = new Set(row.myDocumentPermissions ?? []);
        return (
          <div className="button-row">
            <Button variant="outline" size="sm" onClick={() => void openDetails(row.id)}>
              {c.detailsBtn}
            </Button>
            {canAssignGlobal && myDocPerms.has('doc.assign') && (
              <Button variant="secondary" size="sm" onClick={() => void openAssignDevelopers(row)}>
                {c.assignDevsBtn}
              </Button>
            )}
          </div>
        );
      },
    },
  ];

  return (
    <>
      {mode === 'create-version' && details ? (
        <StandaloneFormPage
          modeBadgeLabel={c.versionsCreateBadge}
          formTitle={`${c.versionsCreateTitle}: ${details.summary.derivedTitle} (${details.summary.referenceCode})`}
          formSubtitle={c.versionsCreateSub}
          backButtonLabel={c.backDetailsBtn}
          submitButtonLabel={c.versionsSaveBtn}
          cancelButtonLabel={c.cancelBtn}
          isSubmitting={submitting}
          onBack={() => {
            setMode('details');
            setActiveTab('versions');
          }}
          onCancel={() => {
            setMode('details');
            setActiveTab('versions');
          }}
          onSubmit={() => void handleCreateVersionSubmit()}
        >
          {formError && (
            <p className="form-error-text" role="alert">
              {formError}
            </p>
          )}
          <div className="slot-stat-box" style={{ marginBottom: 14 }}>
            <div className="slot-stat-label">{c.partiesPreviewLabel}</div>
            <div className="slot-stat-value">
              {details.summary.providerName} → {details.summary.consumerName} ({details.summary.apiGroupName})
            </div>
          </div>
          <FormField id="ver-free-text-label" label={c.versionsLabelField} required>
            <TextInput
              id="ver-free-text-label"
              ltrCode
              value={newVersionLabel}
              placeholder={c.versionsLabelPlaceholder}
              onChange={(e) => setNewVersionLabel(e.target.value)}
            />
          </FormField>
        </StandaloneFormPage>
      ) : mode === 'upload-file' && details && filesWorkspace?.selectedRevision ? (
        <StandaloneFormPage
          modeBadgeLabel={c.filesUploadBadge}
          formTitle={`${c.filesUploadTitle}: ${details.summary.derivedTitle} (${filesWorkspace.selectedRevision.versionLabel} · #${filesWorkspace.selectedRevision.revisionNumber})`}
          formSubtitle={c.filesUploadSub}
          backButtonLabel={c.backDetailsBtn}
          submitButtonLabel={c.filesSubmitUploadBtn}
          cancelButtonLabel={c.cancelBtn}
          isSubmitting={submitting}
          onBack={() => {
            setMode('details');
            setActiveTab('files');
          }}
          onCancel={() => {
            setMode('details');
            setActiveTab('files');
          }}
          onSubmit={() => void handleUploadFileSubmit()}
        >
          {formError && (
            <p className="form-error-text" role="alert">
              {formError}
            </p>
          )}
          <FormField id="upload-file-type" label={c.filesTypeSelectLabel} required>
            <SelectInput
              id="upload-file-type"
              value={selectedUploadFileTypeId}
              onChange={(e) => setSelectedUploadFileTypeId(e.target.value)}
              options={filesWorkspace.selectedRevision.requirements.map((req) => ({
                value: req.fileTypeId,
                label: `${req.fileTypeName} (${req.extensions.join(', ')} · ≤ ${formatFileSize(req.maxSizeBytes)})`,
              }))}
            />
          </FormField>
          <div style={{ marginTop: 14 }}>
            <FormField id="upload-local-file" label={c.filesInputFileLabel} required>
              <input
                key={fileInputResetKey}
                id="upload-local-file"
                type="file"
                onChange={(e) => setSelectedLocalFile(e.target.files?.[0] ?? null)}
              />
            </FormField>
          </div>
        </StandaloneFormPage>
      ) : mode === 'create-working-copy' && details && filesWorkspace ? (
        <StandaloneFormPage
          modeBadgeLabel={c.workingCopyBadge}
          formTitle={`${c.workingCopyTitle}: ${details.summary.derivedTitle}`}
          formSubtitle={c.workingCopySub}
          backButtonLabel={c.backDetailsBtn}
          submitButtonLabel={c.workingCopySubmitBtn}
          cancelButtonLabel={c.cancelBtn}
          isSubmitting={submitting}
          onBack={() => setMode('details')}
          onCancel={() => setMode('details')}
          onSubmit={() => void handleCreateWorkingCopySubmit()}
        >
          {(() => {
            const approvedRevs = filesWorkspace.revisions.filter((r) => r.status === 'APPROVED');
            const matchingCrs = (filesWorkspace.approvedChangeRequests ?? []).filter(
              (cr) => !workingCopyBaseRevisionId || cr.sourceRevisionId === workingCopyBaseRevisionId
            );
            const selectedCr = matchingCrs.find((cr) => cr.id === workingCopyChangeRequestId) ?? null;

            return (
              <div style={{ display: 'grid', gap: 14 }}>
                {formError && (
                  <p className="form-error-text" role="alert">
                    {formError}
                  </p>
                )}
                <FormField id="wc-base-rev" label={c.workingCopyBaseLabel} required>
                  {approvedRevs.length === 0 ? (
                    <p className="card-subtitle">{c.workingCopyNoApprovedBase}</p>
                  ) : (
                    <SelectInput
                      id="wc-base-rev"
                      value={workingCopyBaseRevisionId}
                      onChange={(e) => {
                        const nextBase = e.target.value;
                        setWorkingCopyBaseRevisionId(nextBase);
                        const nextCrs = (filesWorkspace.approvedChangeRequests ?? []).filter(
                          (cr) => cr.sourceRevisionId === nextBase
                        );
                        setWorkingCopyChangeRequestId(nextCrs[0]?.id ?? '');
                      }}
                      options={approvedRevs.map((r) => ({
                        value: r.id,
                        label: `#${r.revisionNumber} · ${r.versionLabel} (${r.filesCount} files)`,
                      }))}
                    />
                  )}
                </FormField>

                <FormField id="wc-change-req" label={c.workingCopyCrLabel} required>
                  {matchingCrs.length === 0 ? (
                    <p className="card-subtitle">{c.workingCopyNoApprovedCr}</p>
                  ) : (
                    <SelectInput
                      id="wc-change-req"
                      value={workingCopyChangeRequestId}
                      onChange={(e) => setWorkingCopyChangeRequestId(e.target.value)}
                      options={matchingCrs.map((cr) => ({
                        value: cr.id,
                        label: `${cr.changeTypeName} — ${cr.reason.slice(0, 70)}`,
                      }))}
                    />
                  )}
                </FormField>

                {selectedCr && (
                  <div className="slot-stat-box">
                    <div className="slot-stat-label">{selectedCr.changeTypeName}</div>
                    <div className="slot-stat-value" style={{ fontSize: '0.92rem' }}>
                      {selectedCr.reason}
                    </div>
                  </div>
                )}
              </div>
            );
          })()}
        </StandaloneFormPage>
      ) : mode === 'decide-stage' && decisionStage ? (
        <StandaloneFormPage
          modeBadgeLabel={c.workflowDecisionBadge}
          formTitle={`${c.workflowDecisionTitle}: ${decisionTitleContext}`}
          formSubtitle={c.workflowDecisionSub}
          backButtonLabel={c.backBtn}
          submitButtonLabel={c.decisionSubmitBtn}
          cancelButtonLabel={c.cancelBtn}
          isSubmitting={submitting}
          onBack={() => setMode(decisionReturnTarget)}
          onCancel={() => setMode(decisionReturnTarget)}
          onSubmit={() => void handleSubmitStageDecision()}
        >
          {formError && (
            <p className="form-error-text" role="alert">
              {formError}
            </p>
          )}
          <div style={{ display: 'grid', gap: 14 }}>
            <div className="slot-stat-box">
              <div className="slot-stat-label">{c.colWfStage}</div>
              <div className="slot-stat-value">
                {`#${decisionStage.position} · ${decisionStage.stageNameSnapshot} (${decisionStage.responsibleRoleName})`}
              </div>
            </div>

            <FormField id="wf-decision-choice" label={c.decisionChoiceLabel} required>
              <SelectInput
                id="wf-decision-choice"
                value={decisionKind}
                onChange={(e) => setDecisionKind(e.target.value as WorkflowDecisionKind)}
                options={[
                  { value: 'APPROVE', label: c.decisionApproveOpt },
                  { value: 'RETURN', label: c.decisionReturnOpt },
                  { value: 'REJECT', label: c.decisionRejectOpt },
                ]}
              />
            </FormField>

            <FormField
              id="wf-decision-notes"
              label={c.decisionNotesLabel}
              required={decisionKind === 'RETURN' || decisionKind === 'REJECT'}
            >
              <textarea
                id="wf-decision-notes"
                className="form-control"
                rows={4}
                style={{ width: '100%', padding: '10px 12px', borderRadius: 8, border: '1px solid var(--border-subtle)' }}
                value={decisionNotes}
                placeholder={c.decisionNotesPlaceholder}
                onChange={(e) => setDecisionNotes(e.target.value)}
              />
            </FormField>
          </div>
        </StandaloneFormPage>
      ) : mode === 'workflow-inbox' ? (
        <section className="surface-card">
          <div className="card-header-row">
            <div>
              <span className="mode-pill">{c.workflowInboxBadge}</span>
              <h2 className="card-title heading-font" style={{ marginTop: 8 }}>
                {c.workflowInboxTitle}
              </h2>
              <p className="card-subtitle">{c.workflowInboxSub}</p>
            </div>
            <Button variant="outline" onClick={() => setMode('list')}>
              {c.backBtn}
            </Button>
          </div>

          {inboxLoading ? (
            <LoadingState title={commonStrings.tableLoadingTitle} />
          ) : workflowInbox.length === 0 ? (
            <p className="card-subtitle">{c.workflowInboxEmpty}</p>
          ) : (
            <div className="table-scroll-container">
              <table className="data-table">
                <thead>
                  <tr>
                    <th>{c.colWfIntegration}</th>
                    <th>{c.colWfRevision}</th>
                    <th>{c.colWfStage}</th>
                    <th>{c.colWfSubmittedBy}</th>
                    <th>{c.colActions}</th>
                  </tr>
                </thead>
                <tbody>
                  {workflowInbox.map((item) => (
                    <tr key={item.stageId}>
                      <td>
                        <strong>{item.derivedTitle}</strong>
                        <div className="muted-cell-text">
                          <code className="ltr-code">{item.referenceCode}</code>
                        </div>
                      </td>
                      <td>
                        <code className="ltr-code">
                          {`${item.versionLabel ?? '-'} · #${item.revisionNumber ?? '-'}`}
                        </code>
                      </td>
                      <td>
                        <div>
                          <strong>{`#${item.stagePosition} · ${item.stageNameSnapshot}`}</strong>
                        </div>
                        <StatusBadge variant="info">{item.responsibleRoleName}</StatusBadge>
                      </td>
                      <td>{item.submittedByName}</td>
                      <td>
                        <div className="button-row">
                          <Button
                            variant="outline"
                            size="sm"
                            onClick={() => void openDetails(item.integrationId, 'revisions')}
                          >
                            {c.openReviewDetailsBtn}
                          </Button>
                          {item.canDecide && (
                            <Button
                              variant="primary"
                              size="sm"
                              disabled={submitting}
                              onClick={() =>
                                openDecideStage(
                                  item.runId,
                                  {
                                    id: item.stageId,
                                    runId: item.runId,
                                    position: item.stagePosition,
                                    stageNameSnapshot: item.stageNameSnapshot,
                                    responsibleRoleIdSnapshot: item.responsibleRoleIdSnapshot,
                                    responsibleRoleName: item.responsibleRoleName,
                                    status: 'ACTIVE',
                                  },
                                  `${item.derivedTitle} (#${item.revisionNumber ?? ''})`,
                                  'workflow-inbox'
                                )
                              }
                            >
                              {c.decideStageBtn}
                            </Button>
                          )}
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </section>
      ) : mode === 'assign-devs' && details ? (
        <StandaloneFormPage
          modeBadgeLabel={c.assignBadge}
          formTitle={`${c.assignTitle}: ${details.summary.derivedTitle} (${details.summary.referenceCode})`}
          formSubtitle={c.assignFormSub}
          backButtonLabel={c.backDetailsBtn}
          submitButtonLabel={c.saveDevsBtn}
          cancelButtonLabel={c.cancelBtn}
          isSubmitting={submitting}
          onBack={() => setMode('details')}
          onCancel={() => setMode('details')}
          onSubmit={() => void handleSaveDevelopers()}
        >
          {detailsLoading ? (
            <LoadingState title={commonStrings.tableLoadingTitle} />
          ) : (
            <div className="badge-wrap-list" style={{ gap: 12 }}>
              {assignableUsers.map((u) => (
                <label key={u.id} style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}>
                  <input
                    type="checkbox"
                    checked={selectedDevIds.has(u.id)}
                    onChange={() =>
                      setSelectedDevIds((prev) => {
                        const next = new Set(prev);
                        if (next.has(u.id)) next.delete(u.id);
                        else next.add(u.id);
                        return next;
                      })
                    }
                  />
                  <span>
                    {u.fullName} ({u.email})
                  </span>
                </label>
              ))}
            </div>
          )}
        </StandaloneFormPage>
      ) : mode === 'details' ? (
        <section className="surface-card">
          {detailsLoading || !details ? (
            <LoadingState title={commonStrings.tableLoadingTitle} />
          ) : (
            <>
              <div className="card-header-row">
                <div>
                  <span className="mode-pill">{c.detailsBadge}</span>
                  <h2 className="card-title heading-font" style={{ marginTop: 8 }}>
                    {details.summary.derivedTitle} ·{' '}
                    <code className="ltr-code">{details.summary.referenceCode}</code>
                  </h2>
                  <div className="badge-wrap-list" style={{ marginTop: 6 }}>
                    <StatusBadge variant={details.summary.isCurrentUserAssignedDeveloper ? 'info' : 'neutral'}>
                      {details.summary.isCurrentUserAssignedDeveloper ? c.scopeAssignedBadge : c.scopeViewOnlyBadge}
                    </StatusBadge>
                    <StatusBadge variant="neutral">
                      {c.countsSummary(
                        details.summary.versionsCount,
                        details.revisionsCount,
                        details.changeRequestsCount,
                        details.publicationsCount
                      )}
                    </StatusBadge>
                  </div>
                </div>
                <Button variant="outline" onClick={() => setMode('list')}>
                  {c.backBtn}
                </Button>
              </div>

              {/* Persistent Read-Only Party & Governance Summary */}
              <div className="slot-grid-3" style={{ marginBottom: 18 }}>
                <div className="slot-stat-box">
                  <div className="slot-stat-label">{c.providerLabel}</div>
                  <div className="slot-stat-value">{details.summary.providerName}</div>
                </div>
                <div className="slot-stat-box">
                  <div className="slot-stat-label">{c.consumerLabel}</div>
                  <div className="slot-stat-value">{details.summary.consumerName}</div>
                </div>
                <div className="slot-stat-box">
                  <div className="slot-stat-label">{c.groupLabel}</div>
                  <div className="slot-stat-value">{details.summary.apiGroupName}</div>
                </div>
                <div className="slot-stat-box">
                  <div className="slot-stat-label">{c.reqTemplateLabel}</div>
                  <div className="slot-stat-value">{details.summary.requirementTemplateName}</div>
                </div>
                <div className="slot-stat-box">
                  <div className="slot-stat-label">{c.routeLabel}</div>
                  <div className="slot-stat-value">{details.summary.approvalRouteName}</div>
                </div>
                <div className="slot-stat-box">
                  <div className="slot-stat-label">{c.policyLabel}</div>
                  <div className="slot-stat-value">{details.summary.changePolicyName}</div>
                </div>
              </div>

              {/* UI-09 6-Tab Navigation */}
              <div className="button-row" style={{ marginBottom: 16 }}>
                {(
                  [
                    ['developers', c.tabDevelopers],
                    ['files', c.tabFiles],
                    ['revisions', c.tabRevisions],
                    ['versions', c.tabVersions],
                    ['changes', c.tabChanges],
                    ['audit', c.tabAudit],
                  ] as Array<[DetailsTab, string]>
                ).map(([tabKey, label]) => (
                  <Button
                    key={tabKey}
                    variant={activeTab === tabKey ? 'primary' : 'outline'}
                    size="sm"
                    onClick={() => setActiveTab(tabKey)}
                  >
                    {label}
                  </Button>
                ))}
              </div>

              {/* Tab Content */}
              {activeTab === 'developers' && (
                <div>
                  <div className="card-header-row" style={{ marginBottom: 10 }}>
                    <div>
                      <h3 className="card-title heading-font">{c.intDevsTitle}</h3>
                      <p className="card-subtitle">{c.aclSeparationNote}</p>
                    </div>
                    {canAssignGlobal && (details.summary.myDocumentPermissions ?? []).includes('doc.assign') && (
                      <Button variant="primary" size="sm" onClick={() => void openAssignDevelopers(details.summary)}>
                        {c.assignDevsBtn}
                      </Button>
                    )}
                  </div>
                  <div className="table-scroll-container" style={{ marginBottom: 18 }}>
                    <table className="data-table">
                      <tbody>
                        {details.summary.assignedDevelopers.length === 0 ? (
                          <tr>
                            <td>{c.noDevsLabel}</td>
                          </tr>
                        ) : (
                          details.summary.assignedDevelopers.map(renderDeveloperRow)
                        )}
                      </tbody>
                    </table>
                  </div>

                  <h3 className="card-title heading-font" style={{ marginBottom: 8 }}>
                    {c.docDevsTitle}
                  </h3>
                  <div className="table-scroll-container">
                    <table className="data-table">
                      <tbody>
                        {details.documentDevelopers.length === 0 ? (
                          <tr>
                            <td>{c.noDevsLabel}</td>
                          </tr>
                        ) : (
                          details.documentDevelopers.map(renderDeveloperRow)
                        )}
                      </tbody>
                    </table>
                  </div>
                </div>
              )}

              {activeTab === 'files' && (
                <div>
                  <p className="card-subtitle" style={{ marginBottom: 10 }}>
                    {c.filesTabDesc}
                  </p>
                  {details.requirementRules.length === 0 ? (
                    <p className="card-subtitle">{c.emptyRules}</p>
                  ) : (
                    <div className="table-scroll-container" style={{ marginBottom: 18 }}>
                      <table className="data-table">
                        <thead>
                          <tr>
                            <th>{c.colFileType}</th>
                            <th>{c.colExtensions}</th>
                            <th>{c.colMode}</th>
                            <th>{c.colCounts}</th>
                          </tr>
                        </thead>
                        <tbody>
                          {details.requirementRules.map((r) => (
                            <tr key={r.fileTypeId}>
                              <td>{r.fileTypeName}</td>
                              <td>
                                <code className="ltr-code">{r.extensions.join(', ')}</code>
                              </td>
                              <td>{formatModeLabel(r.requirementMode)}</td>
                              <td>
                                <code className="ltr-code">
                                  {r.minCount} – {r.maxCount}
                                </code>
                              </td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>
                  )}

                  {canViewFilesGlobal && (
                    <div style={{ borderTop: '1px solid var(--border-subtle)', paddingTop: 14 }}>
                      <div className="card-header-row" style={{ marginBottom: 10 }}>
                        <div>
                          <h3 className="card-title heading-font">{c.filesWorkspaceTitle}</h3>
                          {filesWorkspace && (
                            <p className="card-subtitle" style={{ marginTop: 4 }}>
                              <StatusBadge variant={filesWorkspace.scannerConfigured ? 'success' : 'warning'}>
                                {lang === 'ar' ? filesWorkspace.scannerSummaryAr : filesWorkspace.scannerSummaryEn}
                              </StatusBadge>
                            </p>
                          )}
                        </div>
                        {filesWorkspace?.selectedRevision &&
                          (filesWorkspace.selectedRevision.status === 'DRAFT' ||
                            filesWorkspace.selectedRevision.status === 'RETURNED') &&
                          filesWorkspace.canUploadFiles && (
                            <Button variant="primary" size="sm" disabled={submitting} onClick={openUploadFile}>
                              {c.filesUploadBtn}
                            </Button>
                          )}
                      </div>

                      {filesLoading ? (
                        <LoadingState title={commonStrings.tableLoadingTitle} />
                      ) : filesWorkspace ? (
                        <>
                          {filesWorkspace.versions.length === 0 ? (
                            <p className="card-subtitle">{c.emptyVersions}</p>
                          ) : (
                            <>
                              <div className="button-row" style={{ marginBottom: 12, alignItems: 'flex-end' }}>
                                <div style={{ minWidth: 220 }}>
                                  <FormField id="ws-version-select" label={c.filesSelectVersionLabel}>
                                    <SelectInput
                                      id="ws-version-select"
                                      value={selectedWorkspaceVersionId}
                                      onChange={(e) => {
                                        const verId = e.target.value;
                                        setSelectedWorkspaceVersionId(verId);
                                        setSelectedWorkspaceRevisionId('');
                                        if (details) {
                                          void loadFilesWorkspaceTab(details.summary.id, verId);
                                        }
                                      }}
                                      options={filesWorkspace.versions.map((v) => ({
                                        value: v.id,
                                        label: v.versionLabel,
                                      }))}
                                    />
                                  </FormField>
                                </div>

                                {filesWorkspace.revisions.length > 1 && filesWorkspace.selectedRevision && (
                                  <div style={{ minWidth: 210 }}>
                                    <FormField id="ws-revision-select" label={c.colRevNumber}>
                                      <SelectInput
                                        id="ws-revision-select"
                                        value={filesWorkspace.selectedRevision.id}
                                        onChange={(e) => {
                                          const revId = e.target.value;
                                          setSelectedWorkspaceRevisionId(revId);
                                          if (details) {
                                            void loadFilesWorkspaceTab(
                                              details.summary.id,
                                              selectedWorkspaceVersionId || undefined,
                                              revId
                                            );
                                          }
                                        }}
                                        options={filesWorkspace.revisions.map((rev) => ({
                                          value: rev.id,
                                          label: `#${rev.revisionNumber} · ${rev.status}`,
                                        }))}
                                      />
                                    </FormField>
                                  </div>
                                )}

                                {filesWorkspace.selectedRevision ? (
                                  <div className="button-row" style={{ alignItems: 'center' }}>
                                    <StatusBadge
                                      variant={
                                        filesWorkspace.selectedRevision.status === 'APPROVED'
                                          ? 'success'
                                          : filesWorkspace.selectedRevision.status === 'DRAFT'
                                            ? 'info'
                                            : 'warning'
                                      }
                                    >
                                      {c.filesDraftInfoBadge(
                                        filesWorkspace.selectedRevision.revisionNumber,
                                        filesWorkspace.selectedRevision.status
                                      )}
                                    </StatusBadge>

                                    {(filesWorkspace.selectedRevision.status === 'DRAFT' ||
                                      filesWorkspace.selectedRevision.status === 'RETURNED') &&
                                      filesWorkspace.canSubmitRevision && (
                                        <Button
                                          variant="primary"
                                          size="sm"
                                          disabled={submitting || filesWorkspace.selectedRevision.isReadyForSubmission === false}
                                          onClick={() =>
                                            promptSubmitRevision(
                                              filesWorkspace.selectedRevision!.id,
                                              filesWorkspace.selectedRevision!.revisionNumber
                                            )
                                          }
                                        >
                                          {c.submitRevisionBtn}
                                        </Button>
                                      )}

                                    {filesWorkspace.canCreateWorkingCopy &&
                                      filesWorkspace.revisions.some((r) => r.status === 'APPROVED') &&
                                      !filesWorkspace.revisions.some((r) => r.status === 'DRAFT') && (
                                        <Button
                                          variant="secondary"
                                          size="sm"
                                          disabled={submitting}
                                          onClick={() =>
                                            openCreateWorkingCopy(
                                              filesWorkspace.selectedRevision?.status === 'APPROVED'
                                                ? filesWorkspace.selectedRevision.id
                                                : undefined
                                            )
                                          }
                                        >
                                          {c.createWorkingCopyBtn}
                                        </Button>
                                      )}
                                  </div>
                                ) : (
                                  filesWorkspace.canCreateDraftRevision &&
                                  selectedWorkspaceVersionId && (
                                    <Button
                                      variant="secondary"
                                      size="sm"
                                      disabled={submitting}
                                      onClick={() => void handleInitializeDraftRevision(selectedWorkspaceVersionId)}
                                    >
                                      {c.filesInitDraftBtn}
                                    </Button>
                                  )
                                )}
                              </div>

                              {filesWorkspace.selectedRevision &&
                                (filesWorkspace.selectedRevision.isProtected ??
                                  (filesWorkspace.selectedRevision.status !== 'DRAFT' &&
                                    filesWorkspace.selectedRevision.status !== 'RETURNED')) && (
                                  <div
                                    className="slot-stat-box"
                                    style={{ marginBottom: 12, borderInlineStart: '4px solid var(--brand-gold, #c68d28)' }}
                                  >
                                    <div className="slot-stat-label">{c.protectedTag}</div>
                                    <div className="slot-stat-value" style={{ fontSize: '0.9rem' }}>
                                      {c.protectedRevisionBanner(filesWorkspace.selectedRevision.status)}
                                    </div>
                                  </div>
                                )}

                              {filesWorkspace.selectedRevision?.status === 'RETURNED' && (
                                <div
                                  className="slot-stat-box"
                                  style={{ marginBottom: 12, borderInlineStart: '4px solid var(--brand-gold, #c68d28)' }}
                                >
                                  <div className="slot-stat-label">{c.workflowReworkAlertTitle}</div>
                                  {filesWorkspace.selectedRevision.latestDecisionNotes && (
                                    <div className="slot-stat-value" style={{ fontSize: '0.9rem' }}>
                                      {filesWorkspace.selectedRevision.latestDecisionNotes}
                                    </div>
                                  )}
                                  <p className="card-subtitle" style={{ marginTop: 4, marginBottom: 0 }}>
                                    {c.workflowReworkInstruction}
                                  </p>
                                </div>
                              )}

                              {filesWorkspace.selectedRevision &&
                                filesWorkspace.selectedRevision.requirements.length > 0 && (
                                  <div className="slot-stat-box" style={{ marginBottom: 12 }}>
                                    <div className="slot-stat-label" style={{ marginBottom: 8 }}>
                                      {c.requirementsChecklistTitle}
                                    </div>
                                    <div className="badge-wrap-list">
                                      {filesWorkspace.selectedRevision.requirements.map((req) => {
                                        const isDone =
                                          req.isSatisfied ??
                                          (req.requirementMode.toUpperCase() !== 'REQUIRED' ||
                                            req.uploadedCleanCount >= req.minCount);
                                        const exceeds = req.exceedsMaxCount ?? req.uploadedCleanCount > req.maxCount;
                                        return (
                                          <StatusBadge
                                            key={req.fileTypeId}
                                            variant={exceeds ? 'danger' : isDone ? 'success' : 'warning'}
                                          >
                                            {`${req.fileTypeName}: ${req.uploadedCleanCount}/${req.minCount}–${req.maxCount} (${
                                              exceeds
                                                ? c.reqExceedsBadge
                                                : isDone
                                                  ? c.reqSatisfiedBadge
                                                  : c.reqMissingBadge
                                            })`}
                                          </StatusBadge>
                                        );
                                      })}
                                    </div>
                                    {filesWorkspace.selectedRevision.status === 'DRAFT' &&
                                      (lang === 'ar'
                                        ? filesWorkspace.selectedRevision.submissionBlockersAr
                                        : filesWorkspace.selectedRevision.submissionBlockersEn
                                      )?.length ? (
                                      <ul style={{ marginTop: 8, marginBottom: 0, paddingInlineStart: 18, fontSize: '0.86rem' }}>
                                        {(lang === 'ar'
                                          ? filesWorkspace.selectedRevision.submissionBlockersAr ?? []
                                          : filesWorkspace.selectedRevision.submissionBlockersEn ?? []
                                        ).map((blocker, idx) => (
                                          <li key={idx}>{blocker}</li>
                                        ))}
                                      </ul>
                                    ) : null}
                                  </div>
                                )}

                              {verifiedDownloadNotice && (
                                <div className="slot-stat-box" style={{ marginBottom: 12 }}>
                                  <div className="slot-stat-value" style={{ fontSize: '0.88rem' }}>
                                    {verifiedDownloadNotice}
                                  </div>
                                </div>
                              )}

                              {!filesWorkspace.selectedRevision ? (
                                <p className="card-subtitle">{c.filesNoDraftNote}</p>
                              ) : filesWorkspace.files.length === 0 ? (
                                <p className="card-subtitle">{c.filesEmptyList}</p>
                              ) : (
                                <div className="table-scroll-container">
                                  <table className="data-table">
                                    <thead>
                                      <tr>
                                        <th>{c.colFileName}</th>
                                        <th>{c.colFileType}</th>
                                        <th>{c.colFileSizeSha}</th>
                                        <th>{c.colScanStatus}</th>
                                        <th>{c.colActions}</th>
                                      </tr>
                                    </thead>
                                    <tbody>
                                      {filesWorkspace.files.map((f) => {
                                        const isDocx = f.originalName.toLowerCase().endsWith('.docx');
                                        return (
                                          <tr key={f.id}>
                                            <td>
                                              <strong>{f.originalName}</strong>
                                            </td>
                                            <td>{f.fileTypeName}</td>
                                            <td>
                                              <div>
                                                <code className="ltr-code">{formatFileSize(f.sizeBytes)}</code>
                                              </div>
                                              <div className="muted-cell-text">
                                                <code className="ltr-code">{f.sha256.slice(0, 16)}…</code>
                                              </div>
                                            </td>
                                            <td>
                                              <div className="badge-wrap-list">
                                                {renderScanStatusBadge(f.scanStatus)}
                                                <StatusBadge variant={f.isQuarantined ? 'warning' : 'neutral'}>
                                                  {f.isQuarantined ? c.tierQuarantine : c.tierPrivate}
                                                </StatusBadge>
                                              </div>
                                            </td>
                                            <td>
                                              <div className="button-row">
                                                {f.scanStatus === 'PENDING_SCAN' && filesWorkspace.canUploadFiles && (
                                                  <Button
                                                    variant="secondary"
                                                    size="sm"
                                                    disabled={submitting}
                                                    onClick={() => void handleRescanFile(f)}
                                                  >
                                                    {c.rescanBtn}
                                                  </Button>
                                                )}
                                                {f.isAvailableForUse && filesWorkspace.canDownloadFiles && (
                                                  <a
                                                    className="btn btn-outline btn-sm"
                                                    href={getRevisionFileDownloadUrl(f.revisionId, f.id)}
                                                    onClick={(e) => {
                                                      e.preventDefault();
                                                      void handleDownloadRevisionFile(f);
                                                    }}
                                                  >
                                                    {c.downloadBtn}
                                                  </a>
                                                )}
                                                {f.isAvailableForUse &&
                                                  isDocx &&
                                                  filesWorkspace.canUploadFiles && (
                                                    <Button
                                                      variant="secondary"
                                                      size="sm"
                                                      disabled={submitting}
                                                      onClick={() => void handleGenerateWordDerivative(f)}
                                                    >
                                                      {c.wordUpdateBtn}
                                                    </Button>
                                                  )}
                                                {filesWorkspace.canDeleteFiles &&
                                                  (filesWorkspace.selectedRevision?.status === 'DRAFT' ||
                                                    filesWorkspace.selectedRevision?.status === 'RETURNED') && (
                                                    <Button
                                                      variant="danger"
                                                      size="sm"
                                                      disabled={submitting}
                                                      onClick={() => promptRemoveFile(f)}
                                                    >
                                                      {c.removeBtn}
                                                    </Button>
                                                  )}
                                              </div>
                                            </td>
                                          </tr>
                                        );
                                      })}
                                    </tbody>
                                  </table>
                                </div>
                              )}

                              {(filesWorkspace.wordDerivatives ?? []).length > 0 && (
                                <div style={{ marginTop: 18, borderTop: '1px solid var(--border-subtle)', paddingTop: 14 }}>
                                  <h4 className="card-title heading-font">{c.wordDerivativesTitle}</h4>
                                  <p className="card-subtitle" style={{ marginBottom: 10 }}>
                                    {c.wordDerivativesNote}
                                  </p>
                                  <div style={{ display: 'grid', gap: 10 }}>
                                    {(filesWorkspace.wordDerivatives ?? []).map((wd) => {
                                      const statusBadge =
                                        wd.status === 'VERIFIED' ? (
                                          <StatusBadge variant="success">{c.wordStatusVerified}</StatusBadge>
                                        ) : wd.status === 'NO_RECOGNIZED_FIELDS' ? (
                                          <StatusBadge variant="warning">{c.wordStatusNoFields}</StatusBadge>
                                        ) : wd.status === 'PENDING_SCAN' ? (
                                          <StatusBadge variant="warning">{c.wordStatusPending}</StatusBadge>
                                        ) : (
                                          <StatusBadge variant="danger">{c.wordStatusFailed}</StatusBadge>
                                        );
                                      const matchingSourceFile = filesWorkspace.files.find(
                                        (f) => f.id === wd.revisionFileId
                                      );
                                      const derivedName = matchingSourceFile
                                        ? `${matchingSourceFile.originalName.replace(/\.docx$/i, '')}-governance-updated.docx`
                                        : 'governance-updated.docx';

                                      return (
                                        <div key={wd.id} className="slot-stat-box">
                                          <div className="card-header-row" style={{ marginBottom: 6 }}>
                                            <div className="badge-wrap-list">
                                              {statusBadge}
                                              <StatusBadge variant="neutral">
                                                {`${c.wordStrategyLabel}: ${wd.templateKindDetected}`}
                                              </StatusBadge>
                                              <StatusBadge variant="info">
                                                {`${c.wordFieldsCountLabel}: ${wd.fieldsUpdatedCount}`}
                                              </StatusBadge>
                                            </div>
                                            {wd.isPublishable && filesWorkspace.canDownloadFiles ? (
                                              <Button
                                                variant="primary"
                                                size="sm"
                                                disabled={submitting}
                                                onClick={() => void handleDownloadWordDerivative(wd, derivedName)}
                                              >
                                                {c.wordDownloadDerivedBtn}
                                              </Button>
                                            ) : (
                                              <StatusBadge variant="danger">{c.wordBlockedDownloadTag}</StatusBadge>
                                            )}
                                          </div>
                                          <div style={{ fontSize: '0.84rem', display: 'grid', gap: 4 }}>
                                            <div>
                                              {c.wordSourceShaLabel}:{' '}
                                              <code className="ltr-code">{wd.sourceSha256}</code>
                                            </div>
                                            {wd.derivedSha256 && (
                                              <div>
                                                {c.wordDerivedShaLabel}:{' '}
                                                <code className="ltr-code">{wd.derivedSha256}</code>
                                              </div>
                                            )}
                                            {wd.matchedFields.length > 0 && (
                                              <div className="badge-wrap-list" style={{ marginTop: 4 }}>
                                                {wd.matchedFields.map((mf, i) => (
                                                  <StatusBadge key={`${mf.fieldKey}-${i}`} variant="neutral">
                                                    {`${mf.fieldKey} (${mf.mechanism} · ${mf.location})`}
                                                  </StatusBadge>
                                                ))}
                                              </div>
                                            )}
                                            {wd.validationErrors.length > 0 && (
                                              <div style={{ color: 'var(--status-danger, #b42318)' }}>
                                                {wd.validationErrors.join(' · ')}
                                              </div>
                                            )}
                                          </div>
                                        </div>
                                      );
                                    })}
                                  </div>
                                </div>
                              )}
                            </>
                          )}
                        </>
                      ) : null}
                    </div>
                  )}
                </div>
              )}

              {activeTab === 'revisions' && (
                <div>
                  {canViewFilesGlobal && filesWorkspace && filesWorkspace.versions.length > 0 && (
                    <div style={{ marginBottom: 18 }}>
                      <div className="card-header-row" style={{ marginBottom: 10 }}>
                        <h3 className="card-title heading-font">{c.revisionsHistoryTitle}</h3>
                        {filesWorkspace.canCreateWorkingCopy &&
                          filesWorkspace.revisions.some((r) => r.status === 'APPROVED') &&
                          !filesWorkspace.revisions.some((r) => r.status === 'DRAFT') && (
                            <Button
                              variant="secondary"
                              size="sm"
                              disabled={submitting}
                              onClick={() => openCreateWorkingCopy()}
                            >
                              {c.createWorkingCopyBtn}
                            </Button>
                          )}
                      </div>
                      <div className="table-scroll-container">
                        <table className="data-table">
                          <thead>
                            <tr>
                              <th>{c.colRevNumber}</th>
                              <th>{c.colRevStatus}</th>
                              <th>{c.colRevLineage}</th>
                              <th>{c.colRevFiles}</th>
                              <th>{c.colActions}</th>
                            </tr>
                          </thead>
                          <tbody>
                            {filesWorkspace.revisions.map((rev) => (
                              <tr key={rev.id}>
                                <td>
                                  <code className="ltr-code">{`#${rev.revisionNumber}`}</code>{' '}
                                  {filesWorkspace.selectedRevision?.id === rev.id && (
                                    <StatusBadge variant="info">{c.selectedRevisionTag}</StatusBadge>
                                  )}
                                </td>
                                <td>
                                  <div className="badge-wrap-list">
                                    <StatusBadge
                                      variant={
                                        rev.status === 'APPROVED'
                                          ? 'success'
                                          : rev.status === 'DRAFT'
                                            ? 'info'
                                            : 'warning'
                                      }
                                    >
                                      {rev.status}
                                    </StatusBadge>
                                    <StatusBadge
                                      variant={
                                        rev.status === 'DRAFT' || rev.status === 'RETURNED' ? 'neutral' : 'warning'
                                      }
                                    >
                                      {rev.status === 'DRAFT' || rev.status === 'RETURNED'
                                        ? c.editableDraftTag
                                        : c.protectedTag}
                                    </StatusBadge>
                                  </div>
                                </td>
                                <td>
                                  <code className="ltr-code">
                                    {rev.baseRevisionId ? `Base:${rev.baseRevisionId.slice(0, 8)}` : 'Initial'}
                                    {rev.changeRequestId ? ` · CR:${rev.changeRequestId.slice(0, 8)}` : ''}
                                  </code>
                                </td>
                                <td>
                                  <code className="ltr-code">
                                    {rev.filesCount} / {rev.pendingScanCount}
                                  </code>
                                </td>
                                <td>
                                  <div className="button-row">
                                    <Button
                                      variant="outline"
                                      size="sm"
                                      onClick={() => {
                                        setSelectedWorkspaceRevisionId(rev.id);
                                        setActiveTab('files');
                                        if (details) {
                                          void loadFilesWorkspaceTab(
                                            details.summary.id,
                                            selectedWorkspaceVersionId || undefined,
                                            rev.id
                                          );
                                        }
                                      }}
                                    >
                                      {c.selectRevisionBtn}
                                    </Button>
                                    {(rev.status === 'DRAFT' || rev.status === 'RETURNED') &&
                                      filesWorkspace.canSubmitRevision && (
                                        <Button
                                          variant="primary"
                                          size="sm"
                                          disabled={submitting || rev.isReadyForSubmission === false}
                                          onClick={() => promptSubmitRevision(rev.id, rev.revisionNumber)}
                                        >
                                          {c.submitRevisionBtn}
                                        </Button>
                                      )}
                                    {rev.status === 'APPROVED' &&
                                      filesWorkspace.canCreateWorkingCopy &&
                                      !filesWorkspace.revisions.some((r) => r.status === 'DRAFT') && (
                                        <Button
                                          variant="secondary"
                                          size="sm"
                                          disabled={submitting}
                                          onClick={() => openCreateWorkingCopy(rev.id)}
                                        >
                                          {c.createWorkingCopyBtn}
                                        </Button>
                                      )}
                                  </div>
                                </td>
                              </tr>
                            ))}
                          </tbody>
                        </table>
                      </div>

                      {filesWorkspace.selectedRevision?.latestDecisionNotes &&
                        (filesWorkspace.selectedRevision.status === 'RETURNED' ||
                          filesWorkspace.selectedRevision.status === 'REJECTED') && (
                          <div
                            className="slot-stat-box"
                            style={{
                              marginTop: 14,
                              borderInlineStart:
                                filesWorkspace.selectedRevision.status === 'RETURNED'
                                  ? '4px solid var(--brand-gold, #c68d28)'
                                  : '4px solid var(--status-danger, #b42318)',
                            }}
                          >
                            <div className="slot-stat-label">
                              {filesWorkspace.selectedRevision.status === 'RETURNED'
                                ? c.workflowReworkAlertTitle
                                : c.workflowRejectedAlertTitle}
                            </div>
                            <div className="slot-stat-value" style={{ fontSize: '0.92rem' }}>
                              {filesWorkspace.selectedRevision.latestDecisionNotes}
                            </div>
                            {filesWorkspace.selectedRevision.status === 'RETURNED' && (
                              <p className="card-subtitle" style={{ marginTop: 6, marginBottom: 0 }}>
                                {c.workflowReworkInstruction}
                              </p>
                            )}
                          </div>
                        )}

                      {canViewWorkflowsGlobal && selectedWorkflowRun && (
                        <div style={{ marginTop: 18, borderTop: '1px solid var(--border-subtle)', paddingTop: 14 }}>
                          <div className="card-header-row" style={{ marginBottom: 8 }}>
                            <div>
                              <h4 className="card-title heading-font">{c.workflowRunTitle}</h4>
                              <p className="card-subtitle">{c.workflowSnapshotNote}</p>
                            </div>
                            <StatusBadge
                              variant={
                                selectedWorkflowRun.status === 'APPROVED'
                                  ? 'success'
                                  : selectedWorkflowRun.status === 'REJECTED'
                                    ? 'danger'
                                    : selectedWorkflowRun.status === 'RETURNED'
                                      ? 'warning'
                                      : 'info'
                              }
                            >
                              {selectedWorkflowRun.status}
                            </StatusBadge>
                          </div>

                          {workflowLoading ? (
                            <LoadingState title={commonStrings.tableLoadingTitle} />
                          ) : (
                            <>
                              <div className="table-scroll-container" style={{ marginBottom: 14 }}>
                                <table className="data-table">
                                  <thead>
                                    <tr>
                                      <th>{c.colStageOrder}</th>
                                      <th>{c.colStageName}</th>
                                      <th>{c.colStageRole}</th>
                                      <th>{c.colRevStatus}</th>
                                      <th>{c.colActions}</th>
                                    </tr>
                                  </thead>
                                  <tbody>
                                    {selectedWorkflowRun.stages.map((st) => (
                                      <tr key={st.id}>
                                        <td>
                                          <code className="ltr-code">{st.position}</code>
                                        </td>
                                        <td>
                                          <strong>{st.stageNameSnapshot}</strong>
                                          {st.decision && (
                                            <div className="muted-cell-text">
                                              {`${st.decision.decision} · ${st.decision.actorUserName}${
                                                st.decision.notes ? ` — ${st.decision.notes}` : ''
                                              }`}
                                            </div>
                                          )}
                                        </td>
                                        <td>{st.responsibleRoleName}</td>
                                        <td>
                                          <StatusBadge
                                            variant={
                                              st.status === 'APPROVED'
                                                ? 'success'
                                                : st.status === 'ACTIVE'
                                                  ? 'info'
                                                  : st.status === 'REJECTED'
                                                    ? 'danger'
                                                    : 'neutral'
                                            }
                                          >
                                            {st.status === 'ACTIVE' ? c.workflowActiveStageTag : st.status}
                                          </StatusBadge>
                                        </td>
                                        <td>
                                          {st.status === 'ACTIVE' &&
                                            selectedWorkflowRun.canCurrentUserDecideActiveStage &&
                                            canDecideWorkflowsGlobal && (
                                              <Button
                                                variant="primary"
                                                size="sm"
                                                disabled={submitting}
                                                onClick={() =>
                                                  openDecideStage(
                                                    selectedWorkflowRun.id,
                                                    st,
                                                    `${selectedWorkflowRun.derivedTitle} (#${selectedWorkflowRun.revisionNumber ?? ''})`,
                                                    'details'
                                                  )
                                                }
                                              >
                                                {c.decideStageBtn}
                                              </Button>
                                            )}
                                        </td>
                                      </tr>
                                    ))}
                                  </tbody>
                                </table>
                              </div>

                              <h4 className="card-title heading-font" style={{ marginBottom: 8 }}>
                                {c.workflowHistoryTitle}
                              </h4>
                              {workflowHistory.length === 0 ? (
                                <p className="card-subtitle">{c.workflowHistoryEmpty}</p>
                              ) : (
                                <div className="table-scroll-container">
                                  <table className="data-table">
                                    <thead>
                                      <tr>
                                        <th>{c.colStageOrder}</th>
                                        <th>{c.colStageName}</th>
                                        <th>{c.colAuditActor}</th>
                                        <th>{c.colAuditRes}</th>
                                        <th>{c.colAuditTime}</th>
                                      </tr>
                                    </thead>
                                    <tbody>
                                      {workflowHistory.map((h) => (
                                        <tr key={h.decision.id}>
                                          <td>
                                            <code className="ltr-code">{h.stagePosition}</code>
                                          </td>
                                          <td>{`${h.stageNameSnapshot} (${h.responsibleRoleName})`}</td>
                                          <td>{h.decision.actorUserName}</td>
                                          <td>
                                            <strong>{h.decision.decision}</strong>
                                            {h.decision.notes && (
                                              <div className="muted-cell-text">{h.decision.notes}</div>
                                            )}
                                          </td>
                                          <td>
                                            <code className="ltr-code">
                                              {new Date(h.decision.decidedAt).toISOString().replace('T', ' ').slice(0, 19)}
                                            </code>
                                          </td>
                                        </tr>
                                      ))}
                                    </tbody>
                                  </table>
                                </div>
                              )}
                            </>
                          )}
                        </div>
                      )}
                    </div>
                  )}

                  <p className="card-subtitle" style={{ marginBottom: 10 }}>
                    {c.revisionsTabDesc}
                  </p>
                  {details.approvalStages.length === 0 ? (
                    <p className="card-subtitle">{c.emptyStages}</p>
                  ) : (
                    <div className="table-scroll-container">
                      <table className="data-table">
                        <thead>
                          <tr>
                            <th>{c.colStageOrder}</th>
                            <th>{c.colStageName}</th>
                            <th>{c.colStageRole}</th>
                          </tr>
                        </thead>
                        <tbody>
                          {details.approvalStages.map((s) => (
                            <tr key={s.position}>
                              <td>
                                <code className="ltr-code">{s.position}</code>
                              </td>
                              <td>{s.stageName}</td>
                              <td>{s.responsibleRoleName}</td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>
                  )}
                </div>
              )}

              {activeTab === 'versions' && (
                <div>
                  <div className="slot-stat-box" style={{ marginBottom: 14 }}>
                    <div className="slot-stat-label">{c.versionsTabDesc}</div>
                    <div className="slot-stat-value">
                      {c.colVersions}: <code className="ltr-code">{details.summary.versionsCount}</code> ·{' '}
                      {c.pubOptionLabel}:{' '}
                      {details.summary.publicationOption === 'POST_APPROVAL'
                        ? c.pubPostApproval
                        : c.pubManual}
                    </div>
                  </div>

                  {canViewVersionsGlobal ? (
                    <>
                      <div className="card-header-row" style={{ marginBottom: 10 }}>
                        <div>
                          <h3 className="card-title heading-font">{c.versionsListTitle}</h3>
                          <p className="card-subtitle">
                            {details.summary.providerName} → {details.summary.consumerName} ({details.summary.apiGroupName})
                          </p>
                        </div>
                        {versionsData?.canCreateVersion && (
                          <Button variant="primary" size="sm" disabled={submitting} onClick={openCreateVersion}>
                            {c.versionsCreateBtn}
                          </Button>
                        )}
                      </div>

                      {versionsLoading ? (
                        <LoadingState title={commonStrings.tableLoadingTitle} />
                      ) : !versionsData || versionsData.versions.length === 0 ? (
                        <p className="card-subtitle">{c.emptyVersions}</p>
                      ) : (
                        <div className="table-scroll-container">
                          <table className="data-table">
                            <thead>
                              <tr>
                                <th>{c.colVerLabel}</th>
                                <th>{c.colVerCreated}</th>
                                <th>{c.colVerRevCount}</th>
                                <th>{c.colVerLatestRev}</th>
                                <th>{c.colActions}</th>
                              </tr>
                            </thead>
                            <tbody>
                              {versionsData.versions.map((ver) => (
                                <tr key={ver.id}>
                                  <td>
                                    <code className="ltr-code">{ver.versionLabel}</code>
                                  </td>
                                  <td>
                                    <code className="ltr-code">{new Date(ver.createdAt).toISOString()}</code>
                                  </td>
                                  <td>
                                    <code className="ltr-code">{ver.revisionsCount}</code>
                                  </td>
                                  <td>{ver.latestRevisionStatus ?? '—'}</td>
                                  <td>
                                    <Button
                                      variant="outline"
                                      size="sm"
                                      onClick={() => {
                                        setSelectedWorkspaceVersionId(ver.id);
                                        setActiveTab('files');
                                      }}
                                    >
                                      {c.openFilesForVersionBtn}
                                    </Button>
                                  </td>
                                </tr>
                              ))}
                            </tbody>
                          </table>
                        </div>
                      )}
                    </>
                  ) : (
                    <p className="card-subtitle">{c.versionsNoPermNote}</p>
                  )}
                </div>
              )}

              {activeTab === 'changes' && (
                <div>
                  <p className="card-subtitle" style={{ marginBottom: 10 }}>
                    {c.changesTabDesc}
                  </p>
                  {details.allowedChangeTypes.length === 0 ? (
                    <p className="card-subtitle">{c.emptyChangeTypes}</p>
                  ) : (
                    <div className="table-scroll-container">
                      <table className="data-table">
                        <thead>
                          <tr>
                            <th>{c.colChangeType}</th>
                            <th>{c.colChangeRoute}</th>
                          </tr>
                        </thead>
                        <tbody>
                          {details.allowedChangeTypes.map((ct) => (
                            <tr key={ct.changeTypeId}>
                              <td>{ct.changeTypeName}</td>
                              <td>{ct.routeName}</td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>
                  )}
                </div>
              )}

              {activeTab === 'audit' && (
                <div>
                  <p className="card-subtitle" style={{ marginBottom: 10 }}>
                    {c.auditTabDesc}
                  </p>
                  {details.recentAuditEvents.length === 0 ? (
                    <p className="card-subtitle">{c.emptyAudit}</p>
                  ) : (
                    <div className="table-scroll-container">
                      <table className="data-table">
                        <thead>
                          <tr>
                            <th>{c.colAuditOp}</th>
                            <th>{c.colAuditActor}</th>
                            <th>{c.colAuditRes}</th>
                            <th>{c.colAuditTime}</th>
                          </tr>
                        </thead>
                        <tbody>
                          {details.recentAuditEvents.map((ev) => (
                            <tr key={ev.id}>
                              <td>
                                <code className="ltr-code">{ev.operation}</code>
                              </td>
                              <td>{ev.actorName}</td>
                              <td>
                                <StatusBadge variant={ev.result === 'SUCCESS' ? 'success' : 'danger'}>
                                  {ev.result}
                                </StatusBadge>
                              </td>
                              <td>
                                <code className="ltr-code">{new Date(ev.recordedAt).toISOString()}</code>
                              </td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>
                  )}
                </div>
              )}
            </>
          )}
        </section>
      ) : mode === 'create' ? (
        <StandaloneFormPage
          modeBadgeLabel={c.createBadge}
          formTitle={c.createTitle}
          formSubtitle={c.formSub}
          backButtonLabel={c.backBtn}
          submitButtonLabel={c.saveCreateBtn}
          cancelButtonLabel={c.cancelBtn}
          isSubmitting={submitting}
          onBack={() => setMode('list')}
          onCancel={() => setMode('list')}
          onSubmit={() => void handleCreateSubmit()}
        >
          {eligibleDocs.length === 0 && <p className="form-error-text">{c.noEligibleDocsNotice}</p>}
          {formError && (
            <p className="form-error-text" role="alert">
              {formError}
            </p>
          )}

          <FormField id="int-doc-select" label={c.docSelectLabel} required>
            <SelectInput
              id="int-doc-select"
              value={selectedDocId}
              onChange={(e) => setSelectedDocId(e.target.value)}
              options={[
                { value: '', label: c.selectDocPrompt },
                ...eligibleDocs.map((d) => ({
                  value: d.documentId,
                  label: `${d.derivedTitle} — ${d.apiGroupName}`,
                })),
              ]}
            />
          </FormField>

          {selectedEligibleDoc && (
            <div className="slot-stat-box" style={{ marginTop: 12, marginBottom: 12 }}>
              <div className="slot-stat-label">{c.partiesPreviewLabel}</div>
              <div className="slot-stat-value">
                {selectedEligibleDoc.providerName} → {selectedEligibleDoc.consumerName} (
                {selectedEligibleDoc.apiGroupName})
              </div>
            </div>
          )}

          <FormField id="int-ref-code" label={c.refCodeLabel}>
            <TextInput
              id="int-ref-code"
              ltrCode
              value={refCode}
              placeholder={c.refCodePlaceholder}
              onChange={(e) => setRefCode(e.target.value)}
            />
          </FormField>

          <div style={{ marginTop: 14 }}>
            <FormField id="int-initial-devs" label={c.initialDevsLabel}>
              <div className="badge-wrap-list" style={{ gap: 10 }}>
                {assignableUsers.map((u) => (
                  <label key={u.id} style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}>
                    <input
                      type="checkbox"
                      checked={selectedDevIds.has(u.id)}
                      onChange={() =>
                        setSelectedDevIds((prev) => {
                          const next = new Set(prev);
                          if (next.has(u.id)) next.delete(u.id);
                          else next.add(u.id);
                          return next;
                        })
                      }
                    />
                    <span>{u.fullName}</span>
                  </label>
                ))}
              </div>
            </FormField>
          </div>
        </StandaloneFormPage>
      ) : (
        <section className="surface-card">
          <div className="card-header-row">
            <div>
              <h2 className="card-title heading-font">{c.pageTitle}</h2>
              <p className="card-subtitle">{c.pageDesc}</p>
            </div>
            <div className="button-row">
              {canViewWorkflowsGlobal && (
                <Button variant="secondary" disabled={submitting} onClick={openWorkflowInbox}>
                  {c.workflowInboxBtn}
                </Button>
              )}
              {canCreateGlobal && (
                <Button variant="primary" disabled={submitting} onClick={openCreate}>
                  {c.addBtn}
                </Button>
              )}
            </div>
          </div>

          <SearchFilterBar
            searchValue={searchQuery}
            searchPlaceholder={c.searchPlaceholder}
            searchAriaLabel={c.searchAria}
            onSearchChange={(val) => {
              setSearchQuery(val);
              setPage(1);
            }}
            filterValue={scopeFilter}
            filterAriaLabel={c.scopeFilterAria}
            filterPlaceholder={c.scopeAll}
            filterOptions={[{ value: 'assigned', label: c.scopeAssigned }]}
            onFilterChange={(val) => {
              setScopeFilter(val);
              setPage(1);
            }}
          />

          {errorMsg ? (
            <ErrorState
              title={commonStrings.tableErrorTitle}
              message={errorMsg}
              retryLabel={commonStrings.tableRetryBtn}
              onRetry={() => void loadIntegrations()}
            />
          ) : (
            <>
              <DataTable<IntegrationItem>
                columns={columns}
                rows={items}
                getRowKey={(row) => row.id}
                isLoading={loading}
                loadingTitle={c.loadingText}
                emptyTitle={commonStrings.tableEmptyTitle}
                emptyDescription={commonStrings.tableEmptyDesc}
              />

              {totalCount > 0 && (
                <PaginationBar
                  currentPage={page}
                  totalPages={totalPages}
                  totalItems={totalCount}
                  pageLabel={`${c.pageWord} ${page} ${c.ofWord} ${totalPages}`}
                  previousLabel={commonStrings.paginationPrev}
                  nextLabel={commonStrings.paginationNext}
                  onPageChange={setPage}
                />
              )}
            </>
          )}
        </section>
      )}

      <CentralModalDialog
        isOpen={modal !== null}
        variant={modal?.variant ?? 'info'}
        title={modal?.title ?? ''}
        message={modal?.message ?? ''}
        traceId={modal?.traceId}
        confirmLabel={commonStrings.modalConfirmBtn}
        cancelLabel={modal?.variant === 'confirm' ? c.cancelBtn : undefined}
        closeAriaLabel={commonStrings.modalCloseAria}
        onConfirm={() => {
          const act = modal?.onConfirmAction;
          setModal(null);
          if (act) act();
        }}
        onCancel={() => setModal(null)}
      />
    </>
  );
}
