import { useCallback, useEffect, useMemo, useState } from 'react';
import { ApiProblemError } from '../../api/authApi';
import {
  ApiDocumentItem,
  DocumentConsumerKind,
  DocumentLookups,
  DocumentProviderKind,
  DocumentPublicationOption,
  DocumentState,
  createApiDocument,
  deleteApiDocument,
  getDocumentLookups,
  listApiDocuments,
  replaceDocumentDevelopers,
  updateApiDocument,
} from '../../api/documentsAndIntegrationsApi';
import { AppLanguage, UI_STRINGS } from '../../constants/uiBaseline';
import {
  Button,
  CentralModalDialog,
  DataTable,
  DataTableColumn,
  ErrorState,
  FormField,
  PaginationBar,
  SearchFilterBar,
  SelectInput,
  StandaloneFormPage,
  StatusBadge,
} from '../ui';
import { DocumentAccessMatrixPage } from './DocumentAccessMatrixPage';

interface ApiDocumentsPageProps {
  lang: AppLanguage;
  permissions: string[];
  initialMode?: 'list' | 'create';
  onSessionExpired: () => void;
}

type PageMode = 'list' | 'create' | 'edit' | 'details' | 'developers' | 'access';

interface ModalState {
  variant: 'success' | 'error' | 'confirm';
  title: string;
  message: string;
  traceId?: string;
  onConfirmAction?: () => Promise<void>;
}

const COPY = {
  ar: {
    pageTitle: 'قائمة وثائق API وعلاقاتها (UI-21)',
    pageDesc:
      'الوثائق مقيدة افتراضيًا وفق صلاحيات كل وثيقة؛ العنوان يُشتق تلقائيًا من المزوّد والمستهلك ولا تُرفع الملفات هنا بل داخل التكامل.',
    noViewTitle: 'صلاحية عرض وثائق API غير متوفرة',
    noViewDesc: 'يتطلب عرض قائمة الوثائق توفر صلاحية api_documents.view.',
    noCreateTitle: 'صلاحية إضافة وثيقة API غير متوفرة',
    noCreateDesc: 'يتطلب إنشاء وثيقة API توفر صلاحية api_documents.create.',
    addBtn: 'إضافة وثيقة API',
    searchPlaceholder: 'ابحث بالعنوان المشتق أو المزود أو المستهلك أو مجموعة API...',
    searchAria: 'البحث في وثائق API',
    filterAria: 'تصفية حسب الحالة',
    filterAll: 'كل الحالات',
    filterActive: 'فعّالة (ACTIVE)',
    filterDraft: 'مسودة (DRAFT)',
    filterArchived: 'مؤرشفة (ARCHIVED)',
    colTitle: 'العنوان المشتق والأطراف',
    colGroup: 'مجموعة API والتهيئة',
    colDevs: 'المطورون المكلّفون',
    colIntegration: 'التكامل (1:1)',
    colState: 'الحالة',
    colActions: 'الإجراءات',
    linkedBadge: 'مرتبطة بتكامل',
    unlinkedBadge: 'غير مرتبطة',
    noDevsLabel: 'بدون مطورين',
    detailsBtn: 'التفاصيل',
    editBtn: 'تعديل',
    devsBtn: 'المطورون',
    accessBtn: 'الصلاحيات',
    deleteBtn: 'حذف',
    createBadge: 'إضافة وثيقة API · UI-07 / D01-D02',
    editBadge: 'تعديل وثيقة API · D03',
    devsBadge: 'تعيين مطوري الوثيقة · D02',
    detailsBadge: 'تفاصيل وثيقة API · D03',
    createTitle: 'إضافة وثيقة API جديدة',
    editTitle: 'تعديل إعدادات وثيقة API',
    devsTitle: 'تعيين المطورين المسؤولين عن الوثيقة',
    detailsTitle: 'تفاصيل وثيقة API',
    formSub:
      'يُشتق عنوان الوثيقة تلقائيًا ولا يقبل النظام اختيار النظام نفسه مزودًا ومستهلكًا. إدارة الملفات تتم لاحقًا من داخل التكامل.',
    devsFormSub:
      'الفصل المعتمد: تعيين المطور في الوثيقة يحدد مسؤوليته التقنية ولا يمنح تلقائيًا صلاحيات الوصول (تُضبط من شاشة الصلاحيات UI-21A).',
    backBtn: 'العودة إلى القائمة',
    saveCreateBtn: 'حفظ وإنشاء الوثيقة',
    saveEditBtn: 'حفظ التعديلات',
    saveDevsBtn: 'حفظ تعيين المطورين',
    cancelBtn: 'إلغاء',
    derivedPreviewLabel: 'عنوان الوثيقة المشتق تلقائيًا (للقراءة فقط):',
    providerKindLabel: 'نوع المزوّد',
    providerKindInternal: 'نظام شركة داخلي (مزوّد)',
    providerKindExternal: 'مزوّد خارجي مستقل',
    providerLabel: 'المزوّد',
    consumerKindLabel: 'نوع المستهلك',
    consumerKindCompany: 'نظام شركة داخلي (مستهلك)',
    consumerKindEntity: 'جهة مستهلكة',
    consumerLabel: 'المستهلك',
    selectPrompt: '-- اختر --',
    apiGroupLabel: 'مجموعة API',
    reqTemplateLabel: 'قالب متطلبات الملفات',
    approvalRouteLabel: 'مسار اعتماد الوثيقة',
    changePolicyLabel: 'سياسة التغيير',
    pubOptionLabel: 'خيار النشر',
    pubManual: 'يدوي منضبط (MANUAL)',
    pubPostApproval: 'مهيأ بعد الاعتماد (POST_APPROVAL)',
    stateLabel: 'حالة الوثيقة',
    developersLabel: 'المطورون المكلّفون (D02 — منفصل عن الصلاحيات)',
    lockedPartiesHint:
      'هذه الوثيقة مرتبطة بتكامل (1:1): الأطراف ومجموعة API مقفولة، ويمكن تعديل القالب/المسار/السياسة/الحالة فقط.',
    noUploadNote: 'ملاحظة: لا توجد عملية رفع ملفات داخل الوثيقة؛ تُرفع الملفات وتُدار داخل التكامل في S08.',
    validationRequired: 'يرجى استكمال جميع حقول الأطراف والإعدادات المطلوبة.',
    validationSameParty: 'لا يمكن اختيار نظام الشركة نفسه مزودًا ومستهلكًا للوثيقة ذاتها.',
    createdTitle: 'تم إنشاء وثيقة API',
    createdMsg: 'تم حفظ الوثيقة المقيدة افتراضيًا للمنشئ وتوليد عنوانها تلقائيًا.',
    updatedTitle: 'تم تحديث الوثيقة',
    updatedMsg: 'تم حفظ التعديلات بنجاح.',
    deletedTitle: 'تم حذف الوثيقة',
    deletedMsg: 'تم حذف الوثيقة غير المرتبطة بتكامل وتسجيل الحدث.',
    confirmDeleteTitle: 'تأكيد حذف وثيقة API',
    confirmDeleteMsg: 'هل تريد حذف هذه الوثيقة؟ سيمنع الخادم الحذف إذا كانت مرتبطة بتكامل فعلي (1:1).',
    loadingText: 'جارٍ تحميل الوثائق...',
    pageWord: 'صفحة',
    ofWord: 'من',
  },
  en: {
    pageTitle: 'API Documents Directory (UI-21)',
    pageDesc:
      'Documents are restricted by default via Document ACL. Title is auto-derived from provider & consumer; documentation files are managed inside Integrations.',
    noViewTitle: 'Missing API documents view permission',
    noViewDesc: 'Viewing API documents requires api_documents.view permission.',
    noCreateTitle: 'Missing API document create permission',
    noCreateDesc: 'Creating an API document requires api_documents.create permission.',
    addBtn: 'Create API Document',
    searchPlaceholder: 'Search by derived title, provider, consumer, or API group...',
    searchAria: 'Search API documents',
    filterAria: 'Filter by document state',
    filterAll: 'All states',
    filterActive: 'Active (ACTIVE)',
    filterDraft: 'Draft (DRAFT)',
    filterArchived: 'Archived (ARCHIVED)',
    colTitle: 'Derived Title & Parties',
    colGroup: 'API Group & Governance',
    colDevs: 'Assigned Developers',
    colIntegration: 'Integration (1:1)',
    colState: 'State',
    colActions: 'Actions',
    linkedBadge: 'Linked',
    unlinkedBadge: 'Unlinked',
    noDevsLabel: 'None assigned',
    detailsBtn: 'Details',
    editBtn: 'Edit',
    devsBtn: 'Developers',
    accessBtn: 'Access Control',
    deleteBtn: 'Delete',
    createBadge: 'Create API Document · UI-07 / D01-D02',
    editBadge: 'Edit API Document · D03',
    devsBadge: 'Assign Developers · D02',
    detailsBadge: 'API Document Details · D03',
    createTitle: 'Create New API Document',
    editTitle: 'Edit API Document Settings',
    devsTitle: 'Assign Document Developers',
    detailsTitle: 'API Document Details',
    formSub:
      'Derived title is generated automatically; same internal system cannot be both provider and consumer. Files are managed later inside Integrations.',
    devsFormSub:
      'Separation guarantee: assigning developers sets responsibility only and never grants Document ACL permissions automatically.',
    backBtn: 'Back to List',
    saveCreateBtn: 'Save & Create Document',
    saveEditBtn: 'Save Changes',
    saveDevsBtn: 'Save Assigned Developers',
    cancelBtn: 'Cancel',
    derivedPreviewLabel: 'Auto-Derived Document Title (Read-Only):',
    providerKindLabel: 'Provider Kind',
    providerKindInternal: 'Internal Company Provider',
    providerKindExternal: 'External Provider',
    providerLabel: 'Provider',
    consumerKindLabel: 'Consumer Kind',
    consumerKindCompany: 'Internal Company System',
    consumerKindEntity: 'External Consumer Entity',
    consumerLabel: 'Consumer',
    selectPrompt: '-- Select --',
    apiGroupLabel: 'API Group',
    reqTemplateLabel: 'Requirement Template',
    approvalRouteLabel: 'Approval Route',
    changePolicyLabel: 'Change Policy',
    pubOptionLabel: 'Publication Option',
    pubManual: 'Manual Controlled (MANUAL)',
    pubPostApproval: 'Post-Approval (POST_APPROVAL)',
    stateLabel: 'Document State',
    developersLabel: 'Assigned Developers (D02 — Separate from ACL)',
    lockedPartiesHint:
      'Document is linked 1:1 to an Integration: provider, consumer, and API group are locked.',
    noUploadNote: 'Note: File uploads are intentionally omitted here and belong inside Integrations (S08).',
    validationRequired: 'Please complete all required provider, consumer, and governance selections.',
    validationSameParty: 'The same internal company system cannot be both provider and consumer.',
    createdTitle: 'API Document Created',
    createdMsg: 'Restricted document created with creator default ACL and auto-derived title.',
    updatedTitle: 'API Document Updated',
    updatedMsg: 'Document changes saved successfully.',
    deletedTitle: 'API Document Deleted',
    deletedMsg: 'Unlinked document deleted and audited.',
    confirmDeleteTitle: 'Confirm Document Deletion',
    confirmDeleteMsg: 'Delete this API document? Deletion is blocked if a 1:1 integration link exists.',
    loadingText: 'Loading API documents...',
    pageWord: 'Page',
    ofWord: 'of',
  },
} as const;

export function ApiDocumentsPage({
  lang,
  permissions,
  initialMode = 'list',
  onSessionExpired,
}: ApiDocumentsPageProps) {
  const c = COPY[lang];
  const commonStrings = UI_STRINGS[lang];
  const permSet = useMemo(() => new Set(permissions), [permissions]);
  const canViewGlobal = permSet.has('api_documents.view');
  const canCreateGlobal = permSet.has('api_documents.create');
  const canEditGlobal = permSet.has('api_documents.edit');
  const canDeleteGlobal = permSet.has('api_documents.delete');
  const canAssignGlobal = permSet.has('api_documents.assign_developer');
  const canViewAclGlobal = permSet.has('documents.permissions.view');
  const canManageAclGlobal = permSet.has('documents.permissions.manage');

  const [mode, setMode] = useState<PageMode>(initialMode);
  useEffect(() => {
    setMode(initialMode);
  }, [initialMode]);

  const [searchQuery, setSearchQuery] = useState('');
  const [stateFilter, setStateFilter] = useState('');
  const [page, setPage] = useState(1);
  const [items, setItems] = useState<ApiDocumentItem[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [totalPages, setTotalPages] = useState(1);
  const [loading, setLoading] = useState(false);
  const [errorMsg, setErrorMsg] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [modal, setModal] = useState<ModalState | null>(null);

  const [lookups, setLookups] = useState<DocumentLookups | null>(null);
  const [selectedDoc, setSelectedDoc] = useState<ApiDocumentItem | null>(null);

  // Form fields (UI-07 & D01-D03)
  const [providerKind, setProviderKind] = useState<DocumentProviderKind>('INTERNAL');
  const [providerId, setProviderId] = useState('');
  const [consumerKind, setConsumerKind] = useState<DocumentConsumerKind>('CONSUMER_ENTITY');
  const [consumerId, setConsumerId] = useState('');
  const [apiGroupId, setApiGroupId] = useState('');
  const [reqTemplateId, setReqTemplateId] = useState('');
  const [approvalRouteId, setApprovalRouteId] = useState('');
  const [changePolicyId, setChangePolicyId] = useState('');
  const [publicationOption, setPublicationOption] = useState<DocumentPublicationOption>('MANUAL');
  const [docState, setDocState] = useState<DocumentState>('ACTIVE');
  const [selectedDevIds, setSelectedDevIds] = useState<Set<string>>(new Set());
  const [formError, setFormError] = useState<string | null>(null);

  const handleApiError = useCallback(
    (err: unknown, fallbackTitle: string) => {
      if (err instanceof ApiProblemError && err.status === 401) {
        onSessionExpired();
        return;
      }
      const message = err instanceof Error ? err.message : fallbackTitle;
      setModal({
        variant: 'error',
        title: fallbackTitle,
        message,
        traceId: err instanceof ApiProblemError ? err.traceId : undefined,
      });
    },
    [onSessionExpired]
  );

  const loadDocuments = useCallback(async () => {
    if (!canViewGlobal) return;
    setLoading(true);
    setErrorMsg(null);
    try {
      const res = await listApiDocuments(
        { page, pageSize: 10, search: searchQuery, state: stateFilter },
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
  }, [canViewGlobal, lang, onSessionExpired, page, searchQuery, stateFilter]);

  const loadLookups = useCallback(async () => {
    if (!canViewGlobal && !canCreateGlobal) return;
    try {
      const data = await getDocumentLookups(lang, !canViewGlobal && canCreateGlobal);
      setLookups(data);
    } catch {
      // Secondary lookup errors surface cleanly if user opens form
    }
  }, [canCreateGlobal, canViewGlobal, lang]);

  useEffect(() => {
    void loadDocuments();
  }, [loadDocuments]);

  useEffect(() => {
    void loadLookups();
  }, [loadLookups]);

  const initCreateDefaults = useCallback((lk: DocumentLookups | null) => {
    setSelectedDoc(null);
    setProviderKind(lk?.internalProviders.length ? 'INTERNAL' : 'EXTERNAL');
    setProviderId(lk?.internalProviders[0]?.id ?? lk?.externalProviders[0]?.id ?? '');
    setConsumerKind(lk?.consumerEntities.length ? 'CONSUMER_ENTITY' : 'COMPANY_SYSTEM');
    setConsumerId(lk?.consumerEntities[0]?.id ?? lk?.consumerCompanySystems[0]?.id ?? '');
    setApiGroupId(lk?.apiGroups[0]?.id ?? '');
    setReqTemplateId(lk?.requirementTemplates[0]?.id ?? '');
    setApprovalRouteId(lk?.approvalRoutes[0]?.id ?? '');
    setChangePolicyId(lk?.changePolicies[0]?.id ?? '');
    setPublicationOption('MANUAL');
    setDocState('ACTIVE');
    setSelectedDevIds(new Set());
    setFormError(null);
  }, []);

  useEffect(() => {
    if (mode === 'create' && lookups && !apiGroupId && !reqTemplateId) {
      initCreateDefaults(lookups);
    }
  }, [apiGroupId, initCreateDefaults, lookups, mode, reqTemplateId]);

  const openCreate = () => {
    initCreateDefaults(lookups);
    setMode('create');
  };

  const openEdit = (doc: ApiDocumentItem) => {
    setSelectedDoc(doc);
    setProviderKind(doc.providerKind);
    setProviderId(
      doc.providerKind === 'INTERNAL'
        ? doc.providerInternalSystemId ?? ''
        : doc.providerExternalSystemId ?? ''
    );
    setConsumerKind(doc.consumerKind);
    setConsumerId(
      doc.consumerKind === 'COMPANY_SYSTEM'
        ? doc.consumerCompanySystemId ?? ''
        : doc.consumerEntityId ?? ''
    );
    setApiGroupId(doc.apiGroupId);
    setReqTemplateId(doc.requirementTemplateId);
    setApprovalRouteId(doc.approvalRouteId);
    setChangePolicyId(doc.changePolicyId);
    setPublicationOption(doc.publicationOption);
    setDocState(doc.state);
    setFormError(null);
    setMode('edit');
  };

  const openDevelopers = (doc: ApiDocumentItem) => {
    setSelectedDoc(doc);
    setSelectedDevIds(new Set(doc.assignedDevelopers.map((d) => d.userId)));
    setFormError(null);
    setMode('developers');
  };

  const derivedTitlePreview = useMemo(() => {
    const provList =
      providerKind === 'INTERNAL' ? lookups?.internalProviders ?? [] : lookups?.externalProviders ?? [];
    const consList =
      consumerKind === 'COMPANY_SYSTEM'
        ? lookups?.consumerCompanySystems ?? []
        : lookups?.consumerEntities ?? [];
    const provName = provList.find((p) => p.id === providerId)?.name ?? '—';
    const consName = consList.find((x) => x.id === consumerId)?.name ?? '—';
    return `${provName} ← ${consName}`;
  }, [consumerId, consumerKind, lookups, providerId, providerKind]);

  const toggleDeveloper = (userId: string) => {
    setSelectedDevIds((prev) => {
      const next = new Set(prev);
      if (next.has(userId)) next.delete(userId);
      else next.add(userId);
      return next;
    });
  };

  const handleSubmitForm = async () => {
    setFormError(null);

    if (mode === 'developers' && selectedDoc) {
      setSubmitting(true);
      try {
        await replaceDocumentDevelopers(selectedDoc.id, Array.from(selectedDevIds), lang);
        setModal({ variant: 'success', title: c.updatedTitle, message: c.updatedMsg });
        setMode('list');
        await loadDocuments();
      } catch (err) {
        handleApiError(err, c.devsTitle);
      } finally {
        setSubmitting(false);
      }
      return;
    }

    if (!providerId || !consumerId || !apiGroupId || !reqTemplateId || !approvalRouteId || !changePolicyId) {
      setFormError(c.validationRequired);
      return;
    }

    if (providerKind === 'INTERNAL' && consumerKind === 'COMPANY_SYSTEM' && providerId === consumerId) {
      setFormError(c.validationSameParty);
      return;
    }

    setSubmitting(true);
    try {
      if (mode === 'create') {
        await createApiDocument(
          {
            providerInternalSystemId: providerKind === 'INTERNAL' ? providerId : null,
            providerExternalSystemId: providerKind === 'EXTERNAL' ? providerId : null,
            consumerCompanySystemId: consumerKind === 'COMPANY_SYSTEM' ? consumerId : null,
            consumerEntityId: consumerKind === 'CONSUMER_ENTITY' ? consumerId : null,
            apiGroupId,
            requirementTemplateId: reqTemplateId,
            approvalRouteId,
            changePolicyId,
            publicationOption,
            state: docState,
            developerUserIds: Array.from(selectedDevIds),
          },
          lang
        );
        setModal({ variant: 'success', title: c.createdTitle, message: c.createdMsg });
      } else if (mode === 'edit' && selectedDoc) {
        const payload = selectedDoc.hasIntegration
          ? {
              requirementTemplateId: reqTemplateId,
              approvalRouteId,
              changePolicyId,
              publicationOption,
              state: docState,
            }
          : {
              providerInternalSystemId: providerKind === 'INTERNAL' ? providerId : null,
              providerExternalSystemId: providerKind === 'EXTERNAL' ? providerId : null,
              consumerCompanySystemId: consumerKind === 'COMPANY_SYSTEM' ? consumerId : null,
              consumerEntityId: consumerKind === 'CONSUMER_ENTITY' ? consumerId : null,
              apiGroupId,
              requirementTemplateId: reqTemplateId,
              approvalRouteId,
              changePolicyId,
              publicationOption,
              state: docState,
            };
        await updateApiDocument(selectedDoc.id, payload, lang);
        setModal({ variant: 'success', title: c.updatedTitle, message: c.updatedMsg });
      }
      setMode('list');
      if (canViewGlobal) {
        await loadDocuments();
      }
    } catch (err) {
      handleApiError(err, mode === 'create' ? c.createTitle : c.editTitle);
    } finally {
      setSubmitting(false);
    }
  };

  const handleDelete = (doc: ApiDocumentItem) => {
    setModal({
      variant: 'confirm',
      title: c.confirmDeleteTitle,
      message: c.confirmDeleteMsg,
      onConfirmAction: async () => {
        setSubmitting(true);
        try {
          await deleteApiDocument(doc.id, lang);
          setModal({ variant: 'success', title: c.deletedTitle, message: c.deletedMsg });
          await loadDocuments();
        } catch (err) {
          handleApiError(err, c.confirmDeleteTitle);
        } finally {
          setSubmitting(false);
        }
      },
    });
  };

  if (mode === 'access' && selectedDoc) {
    return (
      <DocumentAccessMatrixPage
        lang={lang}
        documentId={selectedDoc.id}
        canManageAcl={canManageAclGlobal && selectedDoc.myDocumentPermissions.includes('doc.access.manage')}
        onBack={() => setMode('list')}
        onSessionExpired={onSessionExpired}
        onAccessUpdated={() => void loadDocuments()}
      />
    );
  }

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

  const columns: DataTableColumn<ApiDocumentItem>[] = [
    {
      key: 'title',
      header: c.colTitle,
      render: (row) => (
        <div>
          <strong>{row.derivedTitle}</strong>
          <div className="muted-cell-text">
            {row.providerName} → {row.consumerName}
          </div>
        </div>
      ),
    },
    {
      key: 'group',
      header: c.colGroup,
      render: (row) => (
        <div>
          <div>{row.apiGroupName}</div>
          <div className="muted-cell-text">
            {row.requirementTemplateName} · {row.approvalRouteName}
          </div>
        </div>
      ),
    },
    {
      key: 'devs',
      header: c.colDevs,
      render: (row) =>
        row.assignedDevelopers.length === 0 ? (
          <span className="muted-cell-text">{c.noDevsLabel}</span>
        ) : (
          <div className="badge-wrap-list">
            {row.assignedDevelopers.map((dev) => (
              <StatusBadge key={dev.userId} variant="info">
                {dev.fullName}
              </StatusBadge>
            ))}
          </div>
        ),
    },
    {
      key: 'integration',
      header: c.colIntegration,
      render: (row) =>
        row.hasIntegration ? (
          <StatusBadge variant="success">
            {`${c.linkedBadge} (${row.integrationReferenceCode ?? ''})`}
          </StatusBadge>
        ) : (
          <StatusBadge variant="neutral">{c.unlinkedBadge}</StatusBadge>
        ),
    },
    {
      key: 'state',
      header: c.colState,
      render: (row) => (
        <StatusBadge
          variant={
            row.state === 'ACTIVE' ? 'success' : row.state === 'DRAFT' ? 'warning' : 'danger'
          }
        >
          {row.state === 'ACTIVE'
            ? c.filterActive
            : row.state === 'DRAFT'
              ? c.filterDraft
              : c.filterArchived}
        </StatusBadge>
      ),
    },
    {
      key: 'actions',
      header: c.colActions,
      render: (row) => {
        const myDocPerms = new Set(row.myDocumentPermissions);
        return (
          <div className="button-row">
            <Button
              variant="outline"
              size="sm"
              onClick={() => {
                setSelectedDoc(row);
                setMode('details');
              }}
            >
              {c.detailsBtn}
            </Button>
            {canEditGlobal && myDocPerms.has('doc.edit') && (
              <Button variant="outline" size="sm" onClick={() => openEdit(row)}>
                {c.editBtn}
              </Button>
            )}
            {canAssignGlobal && myDocPerms.has('doc.assign') && (
              <Button variant="outline" size="sm" onClick={() => openDevelopers(row)}>
                {c.devsBtn}
              </Button>
            )}
            {canViewAclGlobal && myDocPerms.has('doc.access.manage') && (
              <Button
                variant="secondary"
                size="sm"
                onClick={() => {
                  setSelectedDoc(row);
                  setMode('access');
                }}
              >
                {c.accessBtn}
              </Button>
            )}
            {canDeleteGlobal && myDocPerms.has('doc.delete') && (
              <Button variant="danger" size="sm" onClick={() => handleDelete(row)}>
                {c.deleteBtn}
              </Button>
            )}
          </div>
        );
      },
    },
  ];

  const isPartiesLocked = mode === 'edit' && Boolean(selectedDoc?.hasIntegration);

  return (
    <>
      {mode === 'details' && selectedDoc ? (
        <section className="surface-card">
          <div className="card-header-row">
            <div>
              <span className="mode-pill">{c.detailsBadge}</span>
              <h2 className="card-title heading-font" style={{ marginTop: 8 }}>
                {selectedDoc.derivedTitle}
              </h2>
              <p className="card-subtitle">{c.noUploadNote}</p>
            </div>
            <Button variant="outline" onClick={() => setMode('list')}>
              {c.backBtn}
            </Button>
          </div>

          <div className="slot-grid-3">
            <div className="slot-stat-box">
              <div className="slot-stat-label">{c.providerLabel}</div>
              <div className="slot-stat-value">{selectedDoc.providerName}</div>
            </div>
            <div className="slot-stat-box">
              <div className="slot-stat-label">{c.consumerLabel}</div>
              <div className="slot-stat-value">{selectedDoc.consumerName}</div>
            </div>
            <div className="slot-stat-box">
              <div className="slot-stat-label">{c.apiGroupLabel}</div>
              <div className="slot-stat-value">{selectedDoc.apiGroupName}</div>
            </div>
            <div className="slot-stat-box">
              <div className="slot-stat-label">{c.reqTemplateLabel}</div>
              <div className="slot-stat-value">{selectedDoc.requirementTemplateName}</div>
            </div>
            <div className="slot-stat-box">
              <div className="slot-stat-label">{c.approvalRouteLabel}</div>
              <div className="slot-stat-value">{selectedDoc.approvalRouteName}</div>
            </div>
            <div className="slot-stat-box">
              <div className="slot-stat-label">{c.changePolicyLabel}</div>
              <div className="slot-stat-value">{selectedDoc.changePolicyName}</div>
            </div>
          </div>
        </section>
      ) : mode === 'developers' && selectedDoc ? (
        <StandaloneFormPage
          modeBadgeLabel={c.devsBadge}
          formTitle={`${c.devsTitle}: ${selectedDoc.derivedTitle}`}
          formSubtitle={c.devsFormSub}
          backButtonLabel={c.backBtn}
          submitButtonLabel={submitting ? commonStrings.tableLoadingTitle : c.saveDevsBtn}
          cancelButtonLabel={c.cancelBtn}
          onBack={() => setMode('list')}
          onCancel={() => setMode('list')}
          onSubmit={() => void handleSubmitForm()}
        >
          <div className="badge-wrap-list" style={{ gap: 12 }}>
            {(lookups?.activeUsers ?? []).map((u) => (
              <label
                key={u.id}
                style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}
              >
                <input
                  type="checkbox"
                  checked={selectedDevIds.has(u.id)}
                  onChange={() => toggleDeveloper(u.id)}
                />
                <span>
                  {u.name} ({u.subtitle})
                </span>
              </label>
            ))}
          </div>
        </StandaloneFormPage>
      ) : mode === 'create' || mode === 'edit' ? (
        <StandaloneFormPage
          modeBadgeLabel={mode === 'create' ? c.createBadge : c.editBadge}
          formTitle={mode === 'create' ? c.createTitle : c.editTitle}
          formSubtitle={c.formSub}
          backButtonLabel={c.backBtn}
          submitButtonLabel={mode === 'create' ? c.saveCreateBtn : c.saveEditBtn}
          cancelButtonLabel={c.cancelBtn}
          onBack={() => setMode('list')}
          onCancel={() => setMode('list')}
          onSubmit={() => void handleSubmitForm()}
        >
          <div className="slot-stat-box" style={{ marginBottom: 14 }}>
            <div className="slot-stat-label">{c.derivedPreviewLabel}</div>
            <div className="slot-stat-value">{derivedTitlePreview}</div>
          </div>

          {isPartiesLocked && (
            <p className="card-subtitle" style={{ marginBottom: 12 }}>
              {c.lockedPartiesHint}
            </p>
          )}

          {formError && (
            <p className="form-error-text" role="alert" style={{ marginBottom: 12 }}>
              {formError}
            </p>
          )}

          <div className="form-grid-2">
            <FormField id="doc-prov-kind" label={c.providerKindLabel} required>
              <SelectInput
                id="doc-prov-kind"
                disabled={isPartiesLocked}
                value={providerKind}
                onChange={(e) => {
                  const nextKind = e.target.value as DocumentProviderKind;
                  setProviderKind(nextKind);
                  const first =
                    nextKind === 'INTERNAL'
                      ? lookups?.internalProviders[0]?.id ?? ''
                      : lookups?.externalProviders[0]?.id ?? '';
                  setProviderId(first);
                }}
                options={[
                  { value: 'INTERNAL', label: c.providerKindInternal },
                  { value: 'EXTERNAL', label: c.providerKindExternal },
                ]}
              />
            </FormField>

            <FormField id="doc-prov-id" label={c.providerLabel} required>
              <SelectInput
                id="doc-prov-id"
                disabled={isPartiesLocked}
                value={providerId}
                onChange={(e) => setProviderId(e.target.value)}
                options={[
                  { value: '', label: c.selectPrompt },
                  ...(providerKind === 'INTERNAL'
                    ? lookups?.internalProviders ?? []
                    : lookups?.externalProviders ?? []
                  ).map((o) => ({ value: o.id, label: o.name })),
                ]}
              />
            </FormField>

            <FormField id="doc-cons-kind" label={c.consumerKindLabel} required>
              <SelectInput
                id="doc-cons-kind"
                disabled={isPartiesLocked}
                value={consumerKind}
                onChange={(e) => {
                  const nextKind = e.target.value as DocumentConsumerKind;
                  setConsumerKind(nextKind);
                  const first =
                    nextKind === 'COMPANY_SYSTEM'
                      ? lookups?.consumerCompanySystems[0]?.id ?? ''
                      : lookups?.consumerEntities[0]?.id ?? '';
                  setConsumerId(first);
                }}
                options={[
                  { value: 'COMPANY_SYSTEM', label: c.consumerKindCompany },
                  { value: 'CONSUMER_ENTITY', label: c.consumerKindEntity },
                ]}
              />
            </FormField>

            <FormField id="doc-cons-id" label={c.consumerLabel} required>
              <SelectInput
                id="doc-cons-id"
                disabled={isPartiesLocked}
                value={consumerId}
                onChange={(e) => setConsumerId(e.target.value)}
                options={[
                  { value: '', label: c.selectPrompt },
                  ...(consumerKind === 'COMPANY_SYSTEM'
                    ? lookups?.consumerCompanySystems ?? []
                    : lookups?.consumerEntities ?? []
                  ).map((o) => ({ value: o.id, label: o.name })),
                ]}
              />
            </FormField>

            <FormField id="doc-api-group" label={c.apiGroupLabel} required>
              <SelectInput
                id="doc-api-group"
                disabled={isPartiesLocked}
                value={apiGroupId}
                onChange={(e) => setApiGroupId(e.target.value)}
                options={[
                  { value: '', label: c.selectPrompt },
                  ...(lookups?.apiGroups ?? []).map((o) => ({ value: o.id, label: o.name })),
                ]}
              />
            </FormField>

            <FormField id="doc-req-tpl" label={c.reqTemplateLabel} required>
              <SelectInput
                id="doc-req-tpl"
                value={reqTemplateId}
                onChange={(e) => setReqTemplateId(e.target.value)}
                options={[
                  { value: '', label: c.selectPrompt },
                  ...(lookups?.requirementTemplates ?? []).map((o) => ({ value: o.id, label: o.name })),
                ]}
              />
            </FormField>

            <FormField id="doc-route" label={c.approvalRouteLabel} required>
              <SelectInput
                id="doc-route"
                value={approvalRouteId}
                onChange={(e) => setApprovalRouteId(e.target.value)}
                options={[
                  { value: '', label: c.selectPrompt },
                  ...(lookups?.approvalRoutes ?? []).map((o) => ({ value: o.id, label: o.name })),
                ]}
              />
            </FormField>

            <FormField id="doc-policy" label={c.changePolicyLabel} required>
              <SelectInput
                id="doc-policy"
                value={changePolicyId}
                onChange={(e) => setChangePolicyId(e.target.value)}
                options={[
                  { value: '', label: c.selectPrompt },
                  ...(lookups?.changePolicies ?? []).map((o) => ({ value: o.id, label: o.name })),
                ]}
              />
            </FormField>

            <FormField id="doc-pub-opt" label={c.pubOptionLabel} required>
              <SelectInput
                id="doc-pub-opt"
                value={publicationOption}
                onChange={(e) => setPublicationOption(e.target.value as DocumentPublicationOption)}
                options={[
                  { value: 'MANUAL', label: c.pubManual },
                  { value: 'POST_APPROVAL', label: c.pubPostApproval },
                ]}
              />
            </FormField>

            <FormField id="doc-state" label={c.stateLabel} required>
              <SelectInput
                id="doc-state"
                value={docState}
                onChange={(e) => setDocState(e.target.value as DocumentState)}
                options={[
                  { value: 'ACTIVE', label: 'ACTIVE' },
                  { value: 'DRAFT', label: 'DRAFT' },
                  { value: 'ARCHIVED', label: 'ARCHIVED' },
                ]}
              />
            </FormField>
          </div>

          {mode === 'create' && (
            <div style={{ marginTop: 16 }}>
              <FormField id="doc-initial-devs" label={c.developersLabel}>
                <div className="badge-wrap-list" style={{ gap: 10 }}>
                  {(lookups?.activeUsers ?? []).map((u) => (
                    <label
                      key={u.id}
                      style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}
                    >
                      <input
                        type="checkbox"
                        checked={selectedDevIds.has(u.id)}
                        onChange={() => toggleDeveloper(u.id)}
                      />
                      <span>{u.name}</span>
                    </label>
                  ))}
                </div>
              </FormField>
            </div>
          )}

          <p className="card-subtitle" style={{ marginTop: 12 }}>
            {c.noUploadNote}
          </p>
        </StandaloneFormPage>
      ) : (
        <section className="surface-card">
          <div className="card-header-row">
            <div>
              <h2 className="card-title heading-font">{c.pageTitle}</h2>
              <p className="card-subtitle">{c.pageDesc}</p>
            </div>
            {canCreateGlobal && (
              <Button variant="primary" disabled={submitting} onClick={openCreate}>
                {c.addBtn}
              </Button>
            )}
          </div>

          <SearchFilterBar
            searchValue={searchQuery}
            searchPlaceholder={c.searchPlaceholder}
            searchAriaLabel={c.searchAria}
            onSearchChange={(val) => {
              setSearchQuery(val);
              setPage(1);
            }}
            filterValue={stateFilter}
            filterAriaLabel={c.filterAria}
            filterPlaceholder={c.filterAll}
            filterOptions={[
              { value: 'ACTIVE', label: c.filterActive },
              { value: 'DRAFT', label: c.filterDraft },
              { value: 'ARCHIVED', label: c.filterArchived },
            ]}
            onFilterChange={(val) => {
              setStateFilter(val);
              setPage(1);
            }}
          />

          {errorMsg ? (
            <ErrorState
              title={commonStrings.tableErrorTitle}
              message={errorMsg}
              retryLabel={commonStrings.tableRetryBtn}
              onRetry={() => void loadDocuments()}
            />
          ) : (
            <>
              <DataTable<ApiDocumentItem>
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
        cancelLabel={modal?.variant === 'confirm' ? commonStrings.modalCancelBtn : undefined}
        closeAriaLabel={commonStrings.modalCloseAria}
        onConfirm={() => {
          const fn = modal?.onConfirmAction;
          setModal(null);
          if (fn) void fn();
        }}
        onCancel={modal?.variant === 'confirm' ? () => setModal(null) : undefined}
      />
    </>
  );
}
