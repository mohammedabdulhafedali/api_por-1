import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { ApiProblemError } from '../../api/authApi';
import {
  ApiGroupItem,
  CompanySystemItem,
  CompanySystemUsageType,
  createApiGroup,
  createCompanySystem,
  listApiGroups,
  listCompanySystems,
  updateApiGroup,
  updateCompanySystem,
} from '../../api/masterDataApi';
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
  StandaloneFormPage,
  StatusBadge,
  TextInput,
  TextareaInput,
} from '../ui';

interface CompanySystemsPageProps {
  lang: AppLanguage;
  permissions: string[];
  onSessionExpired: () => void;
}

type ActiveTab = 'systems' | 'api-groups';
type PageMode = 'list' | 'create' | 'edit';

interface ModalState {
  variant: 'success' | 'error' | 'confirm';
  title: string;
  message: string;
  onConfirmAction?: () => Promise<void>;
}

const COPY = {
  ar: {
    tabSystems: 'أنظمة الشركة (M01)',
    tabGroups: 'مجموعات API (M04)',
    sysTitle: 'سجل أنظمة الشركة الداخلية (UI-01)',
    sysDesc: 'تعريف أنظمة الشركة وتعديلها؛ يُشتق نوع الاستخدام (مزوّد / مستهلك / كلاهما) تلقائيًا من وثائق API ولا يُحرر يدويًا.',
    grpTitle: 'مجموعات API التنظيمية (M04)',
    grpDesc: 'تصنيف وتجميع واجهات ووثائق API ضمن مجموعات منظمة مرتبطة بالوثائق.',
    noViewSysTitle: 'صلاحية عرض أنظمة الشركة غير متوفرة',
    noViewSysDesc: 'يتطلب عرض أنظمة الشركة توفر صلاحية company_systems.view.',
    noViewGrpTitle: 'صلاحية عرض مجموعات API غير متوفرة',
    noViewGrpDesc: 'يتطلب عرض مجموعات API توفر صلاحية api_groups.view.',
    addSysBtn: 'إضافة نظام شركة',
    addGrpBtn: 'إضافة مجموعة API',
    searchPlaceholder: 'ابحث بالاسم أو الوصف...',
    searchAria: 'البحث في القائمة',
    filterAria: 'تصفية حسب الحالة',
    filterAll: 'كل الحالات',
    filterActive: 'الفعّالة فقط',
    filterInactive: 'المعطلة فقط',
    colName: 'الاسم',
    colDesc: 'الوصف',
    colUsage: 'نوع الاستخدام المحسوب',
    colInternalProv: 'مزود داخلي؟',
    colDocsCount: 'الوثائق المرتبطة',
    colStatus: 'الحالة',
    colActions: 'الإجراءات',
    usageNone: 'غير مستخدم بعد',
    usageProvider: 'مزوّد',
    usageConsumer: 'مستهلك',
    usageBoth: 'مزوّد ومستهلك معًا',
    yesLabel: 'مفعّل كمزود داخلي',
    noLabel: 'غير مسجل كمزود',
    activeBadge: 'فعّال',
    inactiveBadge: 'معطّل',
    editBtn: 'تعديل',
    deactivateBtn: 'تعطيل',
    activateBtn: 'تفعيل',
    createSysBadge: 'إضافة نظام شركة · UI-01 / M01',
    editSysBadge: 'تعديل نظام شركة · UI-01 / M01',
    createGrpBadge: 'إضافة مجموعة API · M04',
    editGrpBadge: 'تعديل مجموعة API · M04',
    createSysTitle: 'إضافة نظام شركة جديد',
    editSysTitle: 'تعديل بيانات نظام الشركة',
    createGrpTitle: 'إضافة مجموعة API جديدة',
    editGrpTitle: 'تعديل بيانات مجموعة API',
    sysFormSub: 'الاستخدام الفعلي (مزوّد/مستهلك/كلاهما) يُشتق آليًا من وثائق API المعتمدة.',
    grpFormSub: 'تُستخدم مجموعات API لتصنيف الوثائق والتكاملات.',
    backBtn: 'العودة إلى القائمة',
    saveCreateBtn: 'حفظ وإنشاء',
    saveEditBtn: 'حفظ التعديلات',
    cancelBtn: 'إلغاء',
    nameLabel: 'الاسم',
    descLabel: 'الوصف (اختياري)',
    activeStatusLabel: 'السجل فعّال',
    requiredNameError: 'يرجى إدخال الاسم (حرفان على الأقل).',
    validationTitle: 'تعذر الحفظ',
    validationMsg: 'يرجى مراجعة الحقول المطلوبة.',
    createSuccessTitle: 'تمت الإضافة بنجاح',
    createSuccessMsg: 'تم حفظ السجل في قاعدة البيانات وتسجيل العملية في سجل التدقيق.',
    editSuccessTitle: 'تم التحديث بنجاح',
    editSuccessMsg: 'تم حفظ التعديلات وتحديث سجل التدقيق.',
    toggleTitle: 'تأكيد تغيير الحالة',
    deactivateMsg: 'سيؤدي التعطيل إلى إيقاف استخدام هذا السجل في الإضافات الجديدة (مع حماية المراجع النشطة). هل تؤكد؟',
    activateMsg: 'هل تريد إعادة تفعيل هذا السجل؟',
    loadingText: 'جارٍ تحميل البيانات...',
    emptyTitle: 'لا توجد سجلات مطابقة',
    emptyDesc: 'عدّل معايير البحث أو أضف سجلًا جديدًا.',
    errorTitle: 'تعذر تحميل البيانات',
    retryBtn: 'إعادة المحاولة',
    pagePrefix: 'الصفحة',
    ofWord: 'من',
    prevBtn: 'السابق',
    nextBtn: 'التالي',
  },
  en: {
    tabSystems: 'Company Systems (M01)',
    tabGroups: 'API Groups (M04)',
    sysTitle: 'Company Systems Registry (UI-01)',
    sysDesc: 'Manage internal company systems; usage type (Provider / Consumer / Both) is strictly derived from API documents.',
    grpTitle: 'API Groups Registry (M04)',
    grpDesc: 'Organize API documents and integrations into logical API groups.',
    noViewSysTitle: 'Company Systems View Unavailable',
    noViewSysDesc: 'company_systems.view permission is required to view company systems.',
    noViewGrpTitle: 'API Groups View Unavailable',
    noViewGrpDesc: 'api_groups.view permission is required to view API groups.',
    addSysBtn: 'Add Company System',
    addGrpBtn: 'Add API Group',
    searchPlaceholder: 'Search by name or description...',
    searchAria: 'Search records',
    filterAria: 'Filter by status',
    filterAll: 'All Statuses',
    filterActive: 'Active Only',
    filterInactive: 'Inactive Only',
    colName: 'Name',
    colDesc: 'Description',
    colUsage: 'Computed Usage Type',
    colInternalProv: 'Internal Provider?',
    colDocsCount: 'Linked Docs',
    colStatus: 'Status',
    colActions: 'Actions',
    usageNone: 'Unused',
    usageProvider: 'Provider',
    usageConsumer: 'Consumer',
    usageBoth: 'Both (Provider & Consumer)',
    yesLabel: 'Registered Provider',
    noLabel: 'Not Registered',
    activeBadge: 'Active',
    inactiveBadge: 'Inactive',
    editBtn: 'Edit',
    deactivateBtn: 'Deactivate',
    activateBtn: 'Activate',
    createSysBadge: 'Create Company System · UI-01 / M01',
    editSysBadge: 'Edit Company System · UI-01 / M01',
    createGrpBadge: 'Create API Group · M04',
    editGrpBadge: 'Edit API Group · M04',
    createSysTitle: 'Create Company System',
    editSysTitle: 'Edit Company System',
    createGrpTitle: 'Create API Group',
    editGrpTitle: 'Edit API Group',
    sysFormSub: 'Usage classification (Provider / Consumer / Both) is computed automatically from documents.',
    grpFormSub: 'API groups categorize API documents and integrations.',
    backBtn: 'Back to List',
    saveCreateBtn: 'Create Record',
    saveEditBtn: 'Save Changes',
    cancelBtn: 'Cancel',
    nameLabel: 'Name',
    descLabel: 'Description (Optional)',
    activeStatusLabel: 'Record is Active',
    requiredNameError: 'Name is required (at least 2 characters).',
    validationTitle: 'Unable to Save Record',
    validationMsg: 'Please review required fields and try again.',
    createSuccessTitle: 'Record Created',
    createSuccessMsg: 'Record saved to database and logged in the audit trail.',
    editSuccessTitle: 'Record Updated',
    editSuccessMsg: 'Changes saved and logged in the audit trail.',
    toggleTitle: 'Confirm Status Change',
    deactivateMsg: 'Deactivating prevents new links while protecting active references. Continue?',
    activateMsg: 'Re-activate this record?',
    loadingText: 'Loading records...',
    emptyTitle: 'No matching records found',
    emptyDesc: 'Adjust filters or create a new record.',
    errorTitle: 'Failed to Load Records',
    retryBtn: 'Retry',
    pagePrefix: 'Page',
    ofWord: 'of',
    prevBtn: 'Previous',
    nextBtn: 'Next',
  },
} as const;

export function CompanySystemsPage({ lang, permissions, onSessionExpired }: CompanySystemsPageProps) {
  const copy = COPY[lang];
  const t = UI_STRINGS[lang];

  const canViewSys = permissions.some((p) => p.toLowerCase() === 'company_systems.view');
  const canCreateSys = permissions.some((p) => p.toLowerCase() === 'company_systems.create');
  const canEditSys = permissions.some((p) => p.toLowerCase() === 'company_systems.edit');
  const canViewGrp = permissions.some((p) => p.toLowerCase() === 'api_groups.view');
  const canManageGrp = permissions.some((p) => p.toLowerCase() === 'api_groups.manage');

  const [tab, setTab] = useState<ActiveTab>('systems');
  const [mode, setMode] = useState<PageMode>('list');
  const [editingId, setEditingId] = useState<string | null>(null);

  const [systems, setSystems] = useState<CompanySystemItem[]>([]);
  const [groups, setGroups] = useState<ApiGroupItem[]>([]);
  const [page, setPage] = useState(1);
  const [totalPages, setTotalPages] = useState(1);
  const [totalCount, setTotalCount] = useState(0);
  const [search, setSearch] = useState('');
  const [activeFilter, setActiveFilter] = useState<'' | 'true' | 'false'>('');
  const [loading, setLoading] = useState(false);
  const [loadError, setLoadError] = useState<{ message: string; traceId?: string } | null>(null);

  const [formName, setFormName] = useState('');
  const [formDesc, setFormDesc] = useState('');
  const [hadInitialDesc, setHadInitialDesc] = useState(false);
  const [formActive, setFormActive] = useState(true);
  const [nameError, setNameError] = useState('');
  const [modal, setModal] = useState<ModalState | null>(null);

  const nameRef = useRef<HTMLInputElement | null>(null);

  const loadData = useCallback(async () => {
    const canViewTab = tab === 'systems' ? canViewSys : canViewGrp;
    if (!canViewTab) return;
    setLoading(true);
    setLoadError(null);
    try {
      if (tab === 'systems') {
        const res = await listCompanySystems({ page, pageSize: 10, search, isActive: activeFilter, lang });
        setSystems(res.items);
        setTotalPages(res.totalPages);
        setTotalCount(res.totalCount);
      } else {
        const res = await listApiGroups({ page, pageSize: 10, search, isActive: activeFilter, lang });
        setGroups(res.items);
        setTotalPages(res.totalPages);
        setTotalCount(res.totalCount);
      }
    } catch (err) {
      if (err instanceof ApiProblemError && err.status === 401) {
        onSessionExpired();
        return;
      }
      const apiErr = err instanceof ApiProblemError ? err : null;
      setLoadError({ message: apiErr?.message ?? copy.errorTitle, traceId: apiErr?.traceId });
    } finally {
      setLoading(false);
    }
  }, [tab, canViewSys, canViewGrp, page, search, activeFilter, lang, onSessionExpired, copy.errorTitle]);

  useEffect(() => {
    if (mode === 'list') {
      void loadData();
    }
  }, [mode, loadData]);

  const handleApiError = (err: unknown) => {
    if (err instanceof ApiProblemError && err.status === 401) {
      onSessionExpired();
      return;
    }
    const apiErr = err instanceof ApiProblemError ? err : null;
    setModal({
      variant: 'error',
      title: apiErr?.title ?? copy.validationTitle,
      message: apiErr?.message ?? copy.validationMsg,
    });
  };

  const renderUsageBadge = (usage: CompanySystemUsageType) => {
    switch (usage) {
      case 'PROVIDER':
        return <StatusBadge variant="info">{copy.usageProvider}</StatusBadge>;
      case 'CONSUMER':
        return <StatusBadge variant="warning">{copy.usageConsumer}</StatusBadge>;
      case 'BOTH':
        return <StatusBadge variant="success">{copy.usageBoth}</StatusBadge>;
      default:
        return <StatusBadge variant="neutral">{copy.usageNone}</StatusBadge>;
    }
  };

  const handleSave = async () => {
    if (formName.trim().length < 2) {
      setNameError(copy.requiredNameError);
      setModal({ variant: 'error', title: copy.validationTitle, message: copy.validationMsg });
      return;
    }
    try {
      const clearDesc = !formDesc.trim() && hadInitialDesc;
      if (tab === 'systems') {
        if (mode === 'create') {
          await createCompanySystem({ name: formName.trim(), description: formDesc.trim() || null }, lang);
          setModal({ variant: 'success', title: copy.createSuccessTitle, message: copy.createSuccessMsg });
        } else if (editingId) {
          await updateCompanySystem(
            editingId,
            {
              name: formName.trim(),
              description: formDesc.trim() || null,
              clearDescription: clearDesc,
              isActive: formActive,
            },
            lang
          );
          setModal({ variant: 'success', title: copy.editSuccessTitle, message: copy.editSuccessMsg });
        }
      } else {
        if (mode === 'create') {
          await createApiGroup({ name: formName.trim(), description: formDesc.trim() || null }, lang);
          setModal({ variant: 'success', title: copy.createSuccessTitle, message: copy.createSuccessMsg });
        } else if (editingId) {
          await updateApiGroup(
            editingId,
            {
              name: formName.trim(),
              description: formDesc.trim() || null,
              clearDescription: clearDesc,
              isActive: formActive,
            },
            lang
          );
          setModal({ variant: 'success', title: copy.editSuccessTitle, message: copy.editSuccessMsg });
        }
      }
      setMode('list');
      await loadData();
    } catch (err) {
      handleApiError(err);
    }
  };

  const systemColumns: DataTableColumn<CompanySystemItem>[] = useMemo(
    () => [
      { key: 'name', header: copy.colName, render: (row) => <strong>{row.name}</strong> },
      { key: 'desc', header: copy.colDesc, render: (row) => <span>{row.description ?? '—'}</span> },
      {
        key: 'usage',
        header: copy.colUsage,
        render: (row) => renderUsageBadge(row.computedUsageType),
      },
      {
        key: 'prov',
        header: copy.colInternalProv,
        render: (row) => (
          <StatusBadge variant={row.isRegisteredInternalProvider ? 'info' : 'neutral'}>
            {row.isRegisteredInternalProvider ? copy.yesLabel : copy.noLabel}
          </StatusBadge>
        ),
      },
      {
        key: 'status',
        header: copy.colStatus,
        render: (row) => (
          <StatusBadge variant={row.isActive ? 'success' : 'danger'}>
            {row.isActive ? copy.activeBadge : copy.inactiveBadge}
          </StatusBadge>
        ),
      },
      {
        key: 'actions',
        header: copy.colActions,
        render: (row) =>
          canEditSys ? (
            <div className="button-row" style={{ gap: 6 }}>
              <Button
                variant="outline"
                size="sm"
                onClick={() => {
                  setEditingId(row.id);
                  setFormName(row.name);
                  setFormDesc(row.description ?? '');
                  setHadInitialDesc(Boolean(row.description));
                  setFormActive(row.isActive);
                  setNameError('');
                  setMode('edit');
                }}
              >
                {copy.editBtn}
              </Button>
              <Button
                variant="outline"
                size="sm"
                onClick={() =>
                  setModal({
                    variant: 'confirm',
                    title: copy.toggleTitle,
                    message: row.isActive ? copy.deactivateMsg : copy.activateMsg,
                    onConfirmAction: async () => {
                      try {
                        await updateCompanySystem(row.id, { isActive: !row.isActive }, lang);
                        setModal({ variant: 'success', title: copy.editSuccessTitle, message: copy.editSuccessMsg });
                        await loadData();
                      } catch (err) {
                        handleApiError(err);
                      }
                    },
                  })
                }
              >
                {row.isActive ? copy.deactivateBtn : copy.activateBtn}
              </Button>
            </div>
          ) : null,
      },
    ],
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [copy, canEditSys, lang]
  );

  const groupColumns: DataTableColumn<ApiGroupItem>[] = useMemo(
    () => [
      { key: 'name', header: copy.colName, render: (row) => <strong>{row.name}</strong> },
      { key: 'desc', header: copy.colDesc, render: (row) => <span>{row.description ?? '—'}</span> },
      {
        key: 'docs',
        header: copy.colDocsCount,
        render: (row) => <code className="ltr-code">{row.documentsCount}</code>,
      },
      {
        key: 'status',
        header: copy.colStatus,
        render: (row) => (
          <StatusBadge variant={row.isActive ? 'success' : 'danger'}>
            {row.isActive ? copy.activeBadge : copy.inactiveBadge}
          </StatusBadge>
        ),
      },
      {
        key: 'actions',
        header: copy.colActions,
        render: (row) =>
          canManageGrp ? (
            <div className="button-row" style={{ gap: 6 }}>
              <Button
                variant="outline"
                size="sm"
                onClick={() => {
                  setEditingId(row.id);
                  setFormName(row.name);
                  setFormDesc(row.description ?? '');
                  setHadInitialDesc(Boolean(row.description));
                  setFormActive(row.isActive);
                  setNameError('');
                  setMode('edit');
                }}
              >
                {copy.editBtn}
              </Button>
              <Button
                variant="outline"
                size="sm"
                onClick={() =>
                  setModal({
                    variant: 'confirm',
                    title: copy.toggleTitle,
                    message: row.isActive ? copy.deactivateMsg : copy.activateMsg,
                    onConfirmAction: async () => {
                      try {
                        await updateApiGroup(row.id, { isActive: !row.isActive }, lang);
                        setModal({ variant: 'success', title: copy.editSuccessTitle, message: copy.editSuccessMsg });
                        await loadData();
                      } catch (err) {
                        handleApiError(err);
                      }
                    },
                  })
                }
              >
                {row.isActive ? copy.deactivateBtn : copy.activateBtn}
              </Button>
            </div>
          ) : null,
      },
    ],
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [copy, canManageGrp, lang]
  );

  const canViewActiveTab = tab === 'systems' ? canViewSys : canViewGrp;
  const canCreateActiveTab = tab === 'systems' ? canCreateSys : canManageGrp;

  return (
    <>
      {mode === 'list' ? (
        <>
          {canCreateActiveTab && (
            <div className="page-actions">
              <Button
                variant="primary"
                onClick={() => {
                  setEditingId(null);
                  setFormName('');
                  setFormDesc('');
                  setHadInitialDesc(false);
                  setFormActive(true);
                  setNameError('');
                  setMode('create');
                }}
              >
                {tab === 'systems' ? copy.addSysBtn : copy.addGrpBtn}
              </Button>
            </div>
          )}
          <section className="surface-card panel">
            <div className="tabs">
              <button
                type="button"
                className={`tab ${tab === 'systems' ? 'active' : ''}`}
                onClick={() => {
                  setTab('systems');
                  setPage(1);
                  setSearch('');
                }}
              >
                {copy.tabSystems}
              </button>
              <button
                type="button"
                className={`tab ${tab === 'api-groups' ? 'active' : ''}`}
                onClick={() => {
                  setTab('api-groups');
                  setPage(1);
                  setSearch('');
                }}
              >
                {copy.tabGroups}
              </button>
            </div>

            {!canViewActiveTab ? (
              <ErrorState
                title={tab === 'systems' ? copy.noViewSysTitle : copy.noViewGrpTitle}
                message={tab === 'systems' ? copy.noViewSysDesc : copy.noViewGrpDesc}
              />
            ) : (
              <>
                <div className="panel-head">
                  <div>
                    <h2 className="panel-title heading-font">
                      {tab === 'systems' ? copy.sysTitle : copy.grpTitle}
                    </h2>
                    <p className="panel-desc">
                      {tab === 'systems' ? copy.sysDesc : copy.grpDesc}
                    </p>
                  </div>
                </div>

              <SearchFilterBar
                searchValue={search}
                searchPlaceholder={copy.searchPlaceholder}
                searchAriaLabel={copy.searchAria}
                onSearchChange={(val) => {
                  setSearch(val);
                  setPage(1);
                }}
                filterValue={activeFilter}
                filterAriaLabel={copy.filterAria}
                filterPlaceholder={copy.filterAll}
                filterOptions={[
                  { value: 'true', label: copy.filterActive },
                  { value: 'false', label: copy.filterInactive },
                ]}
                onFilterChange={(val) => {
                  setActiveFilter((val as '' | 'true' | 'false') || '');
                  setPage(1);
                }}
              />

              {loadError ? (
                <ErrorState
                  title={copy.errorTitle}
                  message={loadError.message}
                  traceId={loadError.traceId}
                  retryLabel={copy.retryBtn}
                  onRetry={() => void loadData()}
                />
              ) : (
                <>
                  {tab === 'systems' ? (
                    <DataTable
                      columns={systemColumns}
                      rows={systems}
                      getRowKey={(r) => r.id}
                      isLoading={loading}
                      loadingTitle={copy.loadingText}
                      emptyTitle={copy.emptyTitle}
                      emptyDescription={copy.emptyDesc}
                    />
                  ) : (
                    <DataTable
                      columns={groupColumns}
                      rows={groups}
                      getRowKey={(r) => r.id}
                      isLoading={loading}
                      loadingTitle={copy.loadingText}
                      emptyTitle={copy.emptyTitle}
                      emptyDescription={copy.emptyDesc}
                    />
                  )}
                  {totalCount > 0 && (
                    <PaginationBar
                      currentPage={page}
                      totalPages={totalPages}
                      totalItems={totalCount}
                      pageLabel={`${copy.pagePrefix} ${page} ${copy.ofWord} ${totalPages}`}
                      previousLabel={copy.prevBtn}
                      nextLabel={copy.nextBtn}
                      onPageChange={setPage}
                    />
                  )}
                </>
              )}
            </>
          )}
        </section>
        </>
      ) : (
        <StandaloneFormPage
          modeBadgeLabel={
            tab === 'systems'
              ? mode === 'create'
                ? copy.createSysBadge
                : copy.editSysBadge
              : mode === 'create'
                ? copy.createGrpBadge
                : copy.editGrpBadge
          }
          formTitle={
            tab === 'systems'
              ? mode === 'create'
                ? copy.createSysTitle
                : copy.editSysTitle
              : mode === 'create'
                ? copy.createGrpTitle
                : copy.editGrpTitle
          }
          formSubtitle={tab === 'systems' ? copy.sysFormSub : copy.grpFormSub}
          backButtonLabel={copy.backBtn}
          submitButtonLabel={mode === 'create' ? copy.saveCreateBtn : copy.saveEditBtn}
          cancelButtonLabel={copy.cancelBtn}
          onBack={() => setMode('list')}
          onCancel={() => setMode('list')}
          onSubmit={() => void handleSave()}
        >
          <div className="form-grid-2">
            <FormField id="master-name-input" label={copy.nameLabel} required error={nameError}>
              <TextInput
                ref={nameRef}
                id="master-name-input"
                value={formName}
                hasError={Boolean(nameError)}
                onChange={(e) => {
                  setFormName(e.target.value);
                  setNameError('');
                }}
              />
            </FormField>

            {mode === 'edit' && (
              <div style={{ display: 'flex', alignItems: 'center', gap: 8, paddingTop: 24 }}>
                <input
                  id="master-active-input"
                  type="checkbox"
                  checked={formActive}
                  onChange={(e) => setFormActive(e.target.checked)}
                />
                <label htmlFor="master-active-input" style={{ fontSize: 13.5 }}>
                  {copy.activeStatusLabel}
                </label>
              </div>
            )}
          </div>

          <div style={{ marginTop: 14 }}>
            <FormField id="master-desc-input" label={copy.descLabel}>
              <TextareaInput
                id="master-desc-input"
                value={formDesc}
                onChange={(e) => setFormDesc(e.target.value)}
              />
            </FormField>
          </div>
        </StandaloneFormPage>
      )}

      <CentralModalDialog
        isOpen={modal !== null}
        variant={modal?.variant ?? 'info'}
        title={modal?.title ?? ''}
        message={modal?.message ?? ''}
        confirmLabel={t.modalConfirmBtn}
        cancelLabel={modal?.variant === 'confirm' ? t.modalCancelBtn : undefined}
        closeAriaLabel={t.modalCloseAria}
        onConfirm={() => {
          const active = modal;
          if (!active) return;
          if (active.variant === 'confirm' && active.onConfirmAction) {
            void active.onConfirmAction();
            return;
          }
          setModal(null);
          if (active.variant === 'error') {
            nameRef.current?.focus();
          }
        }}
        onCancel={modal?.variant === 'confirm' ? () => setModal(null) : undefined}
      />
    </>
  );
}

