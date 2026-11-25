import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { ApiProblemError } from '../../api/authApi';
import {
  CompanySystemItem,
  ExternalProviderItem,
  InternalProviderItem,
  createExternalProvider,
  createInternalProvider,
  listCompanySystems,
  listExternalProviders,
  listInternalProviders,
  updateExternalProvider,
  updateInternalProvider,
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
  SelectInput,
  StandaloneFormPage,
  StatusBadge,
  TextInput,
  TextareaInput,
} from '../ui';

interface ProvidersManagementPageProps {
  lang: AppLanguage;
  permissions: string[];
  onSessionExpired: () => void;
}

type ProviderTab = 'internal' | 'external';
type PageMode = 'list' | 'create' | 'edit';

interface ModalState {
  variant: 'success' | 'error' | 'confirm';
  title: string;
  message: string;
  onConfirmAction?: () => Promise<void>;
}

const COPY = {
  ar: {
    tabInternal: 'مزودون داخليون (من أنظمة الشركة)',
    tabExternal: 'مزودون خارجيون (مستقلون)',
    internalTitle: 'الأنظمة المزودة الداخلية (UI-03 · M02)',
    internalDesc: 'تفعيل أحد أنظمة الشركة كمزوّد داخلي دون تكرار؛ تعطيل المزود الداخلي يحافظ على أصل نظام الشركة.',
    externalTitle: 'الأنظمة المزودة الخارجية (UI-03 · M02)',
    externalDesc: 'سجل مستقل للمزودين الخارجيين دون حقل «شركة مزودة».',
    noViewTitle: 'صلاحية عرض المزودين غير متوفرة',
    noViewDesc: 'يتطلب عرض الأنظمة المزودة توفر صلاحية providers.view.',
    addInternalBtn: 'تفعيل نظام شركة كمزود داخلي',
    addExternalBtn: 'إضافة مزود خارجي',
    searchPlaceholder: 'ابحث باسم المزود...',
    searchAria: 'البحث في المزودين',
    filterAria: 'تصفية حسب الحالة',
    filterAll: 'كل الحالات',
    filterActive: 'الفعّالة فقط',
    filterInactive: 'المعطلة فقط',
    colName: 'الاسم',
    colDesc: 'الوصف',
    colCompanyStatus: 'حالة أصل نظام الشركة',
    colDocsCount: 'الوثائق المرتبطة',
    colStatus: 'حالة المزود',
    colActions: 'الإجراءات',
    activeBadge: 'فعّال',
    inactiveBadge: 'معطّل',
    editBtn: 'تعديل',
    deactivateBtn: 'تعطيل',
    activateBtn: 'تفعيل',
    createIntBadge: 'تفعيل مزوّد داخلي · UI-03 / M02',
    createIntTitle: 'تفعيل نظام شركة كمزوّد داخلي',
    createIntSub: 'اختر نظام شركة فعّالًا غير مسجل مسبقًا كمزوّد داخلي (يُمنع التكرار).',
    createExtBadge: 'إضافة مزوّد خارجي · UI-03 / M02',
    editExtBadge: 'تعديل مزوّد خارجي · UI-03 / M02',
    createExtTitle: 'إضافة مزوّد خارجي مستقل',
    editExtTitle: 'تعديل بيانات المزوّد الخارجي',
    extFormSub: 'سجل مستقل لا يرتبط بأي نظام شركة داخلي.',
    companySelectLabel: 'نظام الشركة الفعّال',
    companySelectPlaceholder: 'اختر نظام شركة...',
    nameLabel: 'اسم المزوّد الخارجي',
    descLabel: 'الوصف (اختياري)',
    activeLabel: 'المزود الخارجي فعّال',
    backBtn: 'العودة إلى قائمة المزودين',
    saveCreateBtn: 'حفظ وإنشاء',
    saveEditBtn: 'حفظ التعديلات',
    cancelBtn: 'إلغاء',
    requiredSystemError: 'يرجى اختيار نظام شركة فعّال.',
    requiredNameError: 'يرجى إدخال اسم المزوّد الخارجي (حرفان على الأقل).',
    validationTitle: 'تعذر الحفظ',
    validationMsg: 'يرجى مراجعة الحقول المطلوبة.',
    createSuccessTitle: 'تمت الإضافة بنجاح',
    createSuccessMsg: 'تم حفظ بيانات المزوّد في قاعدة البيانات وتسجيل العملية في سجل التدقيق.',
    editSuccessTitle: 'تم التحديث بنجاح',
    editSuccessMsg: 'تم تحديث بيانات المزوّد وتسجيل التغيير في سجل التدقيق.',
    toggleTitle: 'تأكيد تغيير حالة المزوّد',
    deactivateIntMsg: 'سيتم تعطيل المزوّد الداخلي مع الإبقاء على أصل نظام الشركة سليمًا (ومنع التعطيل عند وجود وثائق نشطة). هل تؤكد؟',
    deactivateExtMsg: 'سيؤدي التعطيل إلى منع اختيار المزود في الوثائق الجديدة (مع حماية المراجع النشطة). هل تؤكد؟',
    activateMsg: 'هل تريد إعادة تفعيل هذا المزوّد؟',
    loadingText: 'جارٍ تحميل قائمة المزودين...',
    emptyTitle: 'لا يوجد مزودون مطابقون',
    emptyDesc: 'عدّل معايير البحث أو أضف مزوّدًا جديدًا.',
    errorTitle: 'تعذر تحميل المزودين',
    retryBtn: 'إعادة المحاولة',
    pagePrefix: 'الصفحة',
    ofWord: 'من',
    prevBtn: 'السابق',
    nextBtn: 'التالي',
  },
  en: {
    tabInternal: 'Internal Providers (Company Systems)',
    tabExternal: 'External Providers (Standalone)',
    internalTitle: 'Internal Provider Systems (UI-03 · M02)',
    internalDesc: 'Activate a company system as an internal provider without duplicates; deactivation preserves the original company system.',
    externalTitle: 'External Provider Systems (UI-03 · M02)',
    externalDesc: 'Standalone external providers registry without any company system parent field.',
    noViewTitle: 'Providers View Unavailable',
    noViewDesc: 'providers.view permission is required to view providers.',
    addInternalBtn: 'Activate Internal Provider',
    addExternalBtn: 'Add External Provider',
    searchPlaceholder: 'Search by provider name...',
    searchAria: 'Search providers',
    filterAria: 'Filter by status',
    filterAll: 'All Statuses',
    filterActive: 'Active Only',
    filterInactive: 'Inactive Only',
    colName: 'Name',
    colDesc: 'Description',
    colCompanyStatus: 'Company System Status',
    colDocsCount: 'Linked Docs',
    colStatus: 'Provider Status',
    colActions: 'Actions',
    activeBadge: 'Active',
    inactiveBadge: 'Inactive',
    editBtn: 'Edit',
    deactivateBtn: 'Deactivate',
    activateBtn: 'Activate',
    createIntBadge: 'Activate Internal Provider · UI-03 / M02',
    createIntTitle: 'Activate Company System as Internal Provider',
    createIntSub: 'Select an active company system not already registered as an internal provider.',
    createExtBadge: 'Create External Provider · UI-03 / M02',
    editExtBadge: 'Edit External Provider · UI-03 / M02',
    createExtTitle: 'Create External Provider',
    editExtTitle: 'Edit External Provider',
    extFormSub: 'Independent record with no company system parent field.',
    companySelectLabel: 'Active Company System',
    companySelectPlaceholder: 'Select a company system...',
    nameLabel: 'External Provider Name',
    descLabel: 'Description (Optional)',
    activeLabel: 'External Provider is Active',
    backBtn: 'Back to Providers List',
    saveCreateBtn: 'Save & Create',
    saveEditBtn: 'Save Changes',
    cancelBtn: 'Cancel',
    requiredSystemError: 'Please select an active company system.',
    requiredNameError: 'External provider name is required (at least 2 characters).',
    validationTitle: 'Unable to Save Provider',
    validationMsg: 'Please review required fields and try again.',
    createSuccessTitle: 'Provider Added',
    createSuccessMsg: 'Provider saved to database and logged in the audit trail.',
    editSuccessTitle: 'Provider Updated',
    editSuccessMsg: 'Changes saved and logged in the audit trail.',
    toggleTitle: 'Confirm Status Change',
    deactivateIntMsg: 'Deactivating internal provider preserves the underlying company system record. Continue?',
    deactivateExtMsg: 'Deactivating blocks new API document links while protecting active references. Continue?',
    activateMsg: 'Re-activate this provider?',
    loadingText: 'Loading providers...',
    emptyTitle: 'No matching providers found',
    emptyDesc: 'Adjust search criteria or add a new provider.',
    errorTitle: 'Failed to Load Providers',
    retryBtn: 'Retry',
    pagePrefix: 'Page',
    ofWord: 'of',
    prevBtn: 'Previous',
    nextBtn: 'Next',
  },
} as const;

export function ProvidersManagementPage({ lang, permissions, onSessionExpired }: ProvidersManagementPageProps) {
  const copy = COPY[lang];
  const t = UI_STRINGS[lang];

  const canView = permissions.some((p) => p.toLowerCase() === 'providers.view');
  const canCreate = permissions.some((p) => p.toLowerCase() === 'providers.create');
  const canEdit = permissions.some((p) => p.toLowerCase() === 'providers.edit');

  const [tab, setTab] = useState<ProviderTab>('internal');
  const [mode, setMode] = useState<PageMode>('list');
  const [editingExt, setEditingExt] = useState<ExternalProviderItem | null>(null);

  const [internalItems, setInternalItems] = useState<InternalProviderItem[]>([]);
  const [externalItems, setExternalItems] = useState<ExternalProviderItem[]>([]);
  const [companySystems, setCompanySystems] = useState<CompanySystemItem[]>([]);
  const [page, setPage] = useState(1);
  const [totalPages, setTotalPages] = useState(1);
  const [totalCount, setTotalCount] = useState(0);
  const [search, setSearch] = useState('');
  const [activeFilter, setActiveFilter] = useState<'' | 'true' | 'false'>('');
  const [loading, setLoading] = useState(false);
  const [loadError, setLoadError] = useState<{ message: string; traceId?: string } | null>(null);

  const [selectedSystemId, setSelectedSystemId] = useState('');
  const [extName, setExtName] = useState('');
  const [extDesc, setExtDesc] = useState('');
  const [extActive, setExtActive] = useState(true);
  const [fieldError, setFieldError] = useState('');
  const [modal, setModal] = useState<ModalState | null>(null);

  const nameRef = useRef<HTMLInputElement | null>(null);

  const loadDirectory = useCallback(async () => {
    if (!canView) return;
    setLoading(true);
    setLoadError(null);
    try {
      if (tab === 'internal') {
        const res = await listInternalProviders({ page, pageSize: 10, search, isActive: activeFilter, lang });
        setInternalItems(res.items);
        setTotalPages(res.totalPages);
        setTotalCount(res.totalCount);
      } else {
        const res = await listExternalProviders({ page, pageSize: 10, search, isActive: activeFilter, lang });
        setExternalItems(res.items);
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
  }, [canView, tab, page, search, activeFilter, lang, onSessionExpired, copy.errorTitle]);

  useEffect(() => {
    if (mode === 'list') {
      void loadDirectory();
    }
  }, [mode, loadDirectory]);

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

  const openCreatePage = async () => {
    setFieldError('');
    if (tab === 'internal') {
      setSelectedSystemId('');
      setMode('create');
      try {
        const res = await listCompanySystems({ page: 1, pageSize: 100, isActive: 'true', lang });
        setCompanySystems(res.items.filter((s) => !s.isRegisteredInternalProvider));
      } catch (err) {
        handleApiError(err);
      }
    } else {
      setEditingExt(null);
      setExtName('');
      setExtDesc('');
      setExtActive(true);
      setMode('create');
    }
  };

  const handleSave = async () => {
    if (tab === 'internal') {
      if (!selectedSystemId) {
        setFieldError(copy.requiredSystemError);
        setModal({ variant: 'error', title: copy.validationTitle, message: copy.validationMsg });
        return;
      }
      try {
        await createInternalProvider(selectedSystemId, lang);
        setMode('list');
        setModal({ variant: 'success', title: copy.createSuccessTitle, message: copy.createSuccessMsg });
        await loadDirectory();
      } catch (err) {
        handleApiError(err);
      }
      return;
    }

    if (extName.trim().length < 2) {
      setFieldError(copy.requiredNameError);
      setModal({ variant: 'error', title: copy.validationTitle, message: copy.validationMsg });
      return;
    }

    try {
      if (mode === 'create') {
        await createExternalProvider({ name: extName.trim(), description: extDesc.trim() || null }, lang);
        setModal({ variant: 'success', title: copy.createSuccessTitle, message: copy.createSuccessMsg });
      } else if (editingExt) {
        await updateExternalProvider(
          editingExt.id,
          {
            name: extName.trim(),
            description: extDesc.trim() || null,
            clearDescription: !extDesc.trim() && Boolean(editingExt.description),
            isActive: extActive,
          },
          lang
        );
        setModal({ variant: 'success', title: copy.editSuccessTitle, message: copy.editSuccessMsg });
      }
      setMode('list');
      await loadDirectory();
    } catch (err) {
      handleApiError(err);
    }
  };

  const internalColumns: DataTableColumn<InternalProviderItem>[] = useMemo(
    () => [
      { key: 'name', header: copy.colName, render: (row) => <strong>{row.companySystemName}</strong> },
      { key: 'desc', header: copy.colDesc, render: (row) => <span>{row.companySystemDescription ?? '—'}</span> },
      {
        key: 'sysStatus',
        header: copy.colCompanyStatus,
        render: (row) => (
          <StatusBadge variant={row.companySystemIsActive ? 'success' : 'danger'}>
            {row.companySystemIsActive ? copy.activeBadge : copy.inactiveBadge}
          </StatusBadge>
        ),
      },
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
          canEdit ? (
            <Button
              variant="outline"
              size="sm"
              onClick={() =>
                setModal({
                  variant: 'confirm',
                  title: copy.toggleTitle,
                  message: row.isActive ? copy.deactivateIntMsg : copy.activateMsg,
                  onConfirmAction: async () => {
                    try {
                      await updateInternalProvider(row.id, !row.isActive, lang);
                      setModal({ variant: 'success', title: copy.editSuccessTitle, message: copy.editSuccessMsg });
                      await loadDirectory();
                    } catch (err) {
                      handleApiError(err);
                    }
                  },
                })
              }
            >
              {row.isActive ? copy.deactivateBtn : copy.activateBtn}
            </Button>
          ) : null,
      },
    ],
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [copy, canEdit, lang]
  );

  const externalColumns: DataTableColumn<ExternalProviderItem>[] = useMemo(
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
          canEdit ? (
            <div className="button-row" style={{ gap: 6 }}>
              <Button
                variant="outline"
                size="sm"
                onClick={() => {
                  setEditingExt(row);
                  setExtName(row.name);
                  setExtDesc(row.description ?? '');
                  setExtActive(row.isActive);
                  setFieldError('');
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
                    message: row.isActive ? copy.deactivateExtMsg : copy.activateMsg,
                    onConfirmAction: async () => {
                      try {
                        await updateExternalProvider(row.id, { isActive: !row.isActive }, lang);
                        setModal({ variant: 'success', title: copy.editSuccessTitle, message: copy.editSuccessMsg });
                        await loadDirectory();
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
    [copy, canEdit, lang]
  );

  if (!canView) {
    return (
      <section className="surface-card">
        <ErrorState title={copy.noViewTitle} message={copy.noViewDesc} />
      </section>
    );
  }

  return (
    <>
      {mode === 'list' ? (
        <section className="surface-card panel">
          <div className="tabs">
            <button
              type="button"
              className={`tab ${tab === 'internal' ? 'active' : ''}`}
              onClick={() => {
                setTab('internal');
                setPage(1);
                setSearch('');
              }}
            >
              {copy.tabInternal}
            </button>
            <button
              type="button"
              className={`tab ${tab === 'external' ? 'active' : ''}`}
              onClick={() => {
                setTab('external');
                setPage(1);
                setSearch('');
              }}
            >
              {copy.tabExternal}
            </button>
          </div>
          {tab === 'internal' && (
            <div className="info-box" style={{ marginBottom: 14 }}>
              {copy.internalDesc}
            </div>
          )}

          <div className="card-header-row">
            <div>
              <h2 className="card-title heading-font">
                {tab === 'internal' ? copy.internalTitle : copy.externalTitle}
              </h2>
              <p className="card-subtitle">
                {tab === 'internal' ? copy.internalDesc : copy.externalDesc}
              </p>
            </div>
            {canCreate && (
              <Button variant="primary" onClick={() => void openCreatePage()}>
                {tab === 'internal' ? copy.addInternalBtn : copy.addExternalBtn}
              </Button>
            )}
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
              onRetry={() => void loadDirectory()}
            />
          ) : (
            <>
              {tab === 'internal' ? (
                <DataTable
                  columns={internalColumns}
                  rows={internalItems}
                  getRowKey={(r) => r.id}
                  isLoading={loading}
                  loadingTitle={copy.loadingText}
                  emptyTitle={copy.emptyTitle}
                  emptyDescription={copy.emptyDesc}
                />
              ) : (
                <DataTable
                  columns={externalColumns}
                  rows={externalItems}
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
        </section>
      ) : (
        <StandaloneFormPage
          modeBadgeLabel={
            tab === 'internal'
              ? copy.createIntBadge
              : mode === 'create'
                ? copy.createExtBadge
                : copy.editExtBadge
          }
          formTitle={
            tab === 'internal'
              ? copy.createIntTitle
              : mode === 'create'
                ? copy.createExtTitle
                : copy.editExtTitle
          }
          formSubtitle={tab === 'internal' ? copy.createIntSub : copy.extFormSub}
          backButtonLabel={copy.backBtn}
          submitButtonLabel={mode === 'create' ? copy.saveCreateBtn : copy.saveEditBtn}
          cancelButtonLabel={copy.cancelBtn}
          onBack={() => setMode('list')}
          onCancel={() => setMode('list')}
          onSubmit={() => void handleSave()}
        >
          {tab === 'internal' ? (
            <FormField id="internal-provider-system" label={copy.companySelectLabel} required error={fieldError}>
              <SelectInput
                id="internal-provider-system"
                placeholder={copy.companySelectPlaceholder}
                value={selectedSystemId}
                hasError={Boolean(fieldError)}
                options={companySystems.map((s) => ({ value: s.id, label: s.name }))}
                onChange={(e) => {
                  setSelectedSystemId(e.target.value);
                  setFieldError('');
                }}
              />
            </FormField>
          ) : (
            <>
              <div className="form-grid-2">
                <FormField id="ext-provider-name" label={copy.nameLabel} required error={fieldError}>
                  <TextInput
                    ref={nameRef}
                    id="ext-provider-name"
                    value={extName}
                    hasError={Boolean(fieldError)}
                    onChange={(e) => {
                      setExtName(e.target.value);
                      setFieldError('');
                    }}
                  />
                </FormField>

                {mode === 'edit' && (
                  <div style={{ display: 'flex', alignItems: 'center', gap: 8, paddingTop: 24 }}>
                    <input
                      id="ext-provider-active"
                      type="checkbox"
                      checked={extActive}
                      onChange={(e) => setExtActive(e.target.checked)}
                    />
                    <label htmlFor="ext-provider-active" style={{ fontSize: 13.5 }}>
                      {copy.activeLabel}
                    </label>
                  </div>
                )}
              </div>

              <div style={{ marginTop: 14 }}>
                <FormField id="ext-provider-desc" label={copy.descLabel}>
                  <TextareaInput
                    id="ext-provider-desc"
                    value={extDesc}
                    onChange={(e) => setExtDesc(e.target.value)}
                  />
                </FormField>
              </div>
            </>
          )}
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

