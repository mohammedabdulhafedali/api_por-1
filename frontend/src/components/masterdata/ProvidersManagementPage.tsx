import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { ApiProblemError } from '../../api/authApi';
import {
  CompanySystemItem,
  ExternalProviderItem,
  InternalProviderItem,
  createExternalProvider,
  createInternalProvider,
  deleteExternalProvider,
  deleteInternalProvider,
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
  ReferenceIcon,
  RowActionButton,
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
    tabInternal: 'مزودون داخليون',
    tabExternal: 'مزودون خارجيون',
    noViewTitle: 'صلاحية عرض المزودين غير متوفرة',
    noViewDesc: 'يتطلب عرض الأنظمة المزودة توفر صلاحية العرض على حسابك.',
    addInternalAria: 'إضافة مزود داخلي',
    addExternalAria: 'إضافة مزود خارجي',
    colName: 'الاسم',
    colDesc: 'الوصف',
    colCompanyStatus: 'حالة نظام الشركة',
    colStatus: 'الحالة',
    colActions: 'الإجراءات',
    activeBadge: 'فعّال',
    inactiveBadge: 'معطّل',
    editBtn: 'تعديل',
    deactivateBtn: 'تعطيل',
    activateBtn: 'تفعيل',
    deleteBtn: 'حذف',
    createIntBadge: 'تفعيل مزوّد داخلي',
    createIntTitle: 'تفعيل نظام شركة كمزوّد داخلي',
    createExtBadge: 'إضافة مزوّد خارجي',
    editExtBadge: 'تعديل مزوّد خارجي',
    createExtTitle: 'إضافة مزوّد خارجي جديد',
    editExtTitle: 'تعديل بيانات المزوّد الخارجي',
    companySelectLabel: 'نظام الشركة',
    companySelectPlaceholder: 'اختر نظام شركة...',
    nameLabel: 'اسم المزوّد الخارجي',
    descLabel: 'الوصف (اختياري)',
    activeLabel: 'المزود الخارجي فعّال',
    backBtn: 'العودة إلى القائمة',
    saveCreateBtn: 'حفظ وإنشاء',
    saveEditBtn: 'حفظ التعديلات',
    cancelBtn: 'إلغاء',
    requiredSystemError: 'يرجى اختيار نظام شركة فعّال.',
    requiredNameError: 'يرجى إدخال اسم المزوّد الخارجي (حرفان على الأقل).',
    validationTitle: 'تعذر الحفظ',
    validationMsg: 'يرجى مراجعة الحقول المطلوبة.',
    createSuccessTitle: 'تمت الإضافة بنجاح',
    createSuccessMsg: 'تم حفظ بيانات المزوّد بنجاح.',
    editSuccessTitle: 'تم التحديث بنجاح',
    editSuccessMsg: 'تم تحديث بيانات المزوّد بنجاح.',
    toggleTitle: 'تأكيد تغيير الحالة',
    deactivateIntMsg: 'هل تؤكد تعطيل هذا المزوّد الداخلي؟',
    deactivateExtMsg: 'هل تؤكد تعطيل هذا المزوّد الخارجي؟',
    activateMsg: 'هل تريد إعادة تفعيل هذا المزوّد؟',
    deleteTitle: 'تأكيد الحذف',
    deleteMsg: 'هل تؤكد حذف هذا المزوّد نهائيًا؟',
    deleteSuccessTitle: 'تم الحذف بنجاح',
    deleteSuccessMsg: 'تم حذف المزوّد بنجاح.',
    loadingText: 'جارٍ تحميل البيانات...',
    emptyTitle: 'لا يوجد مزودون مسجلون',
    emptyDesc: 'اضغط على زر (+) لإضافة مزوّد جديد.',
    errorTitle: 'تعذر تحميل البيانات',
    retryBtn: 'إعادة المحاولة',
    pagePrefix: 'الصفحة',
    ofWord: 'من',
    prevBtn: 'السابق',
    nextBtn: 'التالي',
  },
  en: {
    tabInternal: 'Internal Providers',
    tabExternal: 'External Providers',
    noViewTitle: 'Providers View Unavailable',
    noViewDesc: 'View permission is required to view providers.',
    addInternalAria: 'Add Internal Provider',
    addExternalAria: 'Add External Provider',
    colName: 'Name',
    colDesc: 'Description',
    colCompanyStatus: 'Company System Status',
    colStatus: 'Status',
    colActions: 'Actions',
    activeBadge: 'Active',
    inactiveBadge: 'Inactive',
    editBtn: 'Edit',
    deactivateBtn: 'Deactivate',
    activateBtn: 'Activate',
    deleteBtn: 'Delete',
    createIntBadge: 'Activate Internal Provider',
    createIntTitle: 'Activate Company System as Internal Provider',
    createExtBadge: 'Create External Provider',
    editExtBadge: 'Edit External Provider',
    createExtTitle: 'Create External Provider',
    editExtTitle: 'Edit External Provider',
    companySelectLabel: 'Company System',
    companySelectPlaceholder: 'Select a company system...',
    nameLabel: 'External Provider Name',
    descLabel: 'Description (Optional)',
    activeLabel: 'External Provider is Active',
    backBtn: 'Back to List',
    saveCreateBtn: 'Save & Create',
    saveEditBtn: 'Save Changes',
    cancelBtn: 'Cancel',
    requiredSystemError: 'Please select an active company system.',
    requiredNameError: 'External provider name is required (at least 2 characters).',
    validationTitle: 'Unable to Save Provider',
    validationMsg: 'Please review required fields and try again.',
    createSuccessTitle: 'Provider Added',
    createSuccessMsg: 'Provider saved successfully.',
    editSuccessTitle: 'Provider Updated',
    editSuccessMsg: 'Changes saved successfully.',
    toggleTitle: 'Confirm Status Change',
    deactivateIntMsg: 'Are you sure you want to deactivate this internal provider?',
    deactivateExtMsg: 'Are you sure you want to deactivate this external provider?',
    activateMsg: 'Re-activate this provider?',
    deleteTitle: 'Confirm Deletion',
    deleteMsg: 'Are you sure you want to permanently delete this provider?',
    deleteSuccessTitle: 'Provider Deleted',
    deleteSuccessMsg: 'Provider has been permanently deleted.',
    loadingText: 'Loading providers...',
    emptyTitle: 'No providers found',
    emptyDesc: 'Click the (+) button to add a new provider.',
    errorTitle: 'Failed to Load Providers',
    retryBtn: 'Retry',
    pagePrefix: 'Page',
    ofWord: 'of',
    prevBtn: 'Previous',
    nextBtn: 'Next',
  },
} as const;

export function ProvidersManagementPage({
  lang,
  permissions,
  onSessionExpired,
}: ProvidersManagementPageProps) {
  const copy = COPY[lang];
  const t = UI_STRINGS[lang];
  const hasPerm = (code: string) => permissions.some((p) => p.toLowerCase() === code.toLowerCase());

  const canView = hasPerm('providers.view');
  const canCreate = hasPerm('providers.create');
  const canEdit = hasPerm('providers.edit');
  const canDelete = hasPerm('providers.delete');

  const [tab, setTab] = useState<ProviderTab>('internal');
  const [mode, setMode] = useState<PageMode>('list');

  const [internalItems, setInternalItems] = useState<InternalProviderItem[]>([]);
  const [externalItems, setExternalItems] = useState<ExternalProviderItem[]>([]);
  const [companySystems, setCompanySystems] = useState<CompanySystemItem[]>([]);
  const [page, setPage] = useState(1);
  const [totalPages, setTotalPages] = useState(1);
  const [totalCount, setTotalCount] = useState(0);
  const [loading, setLoading] = useState(false);
  const [loadError, setLoadError] = useState<{ message: string; traceId?: string } | null>(null);

  const [selectedSystemId, setSelectedSystemId] = useState('');
  const [editingExt, setEditingExt] = useState<ExternalProviderItem | null>(null);
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
        const res = await listInternalProviders({
          page,
          pageSize: 10,
          lang,
        });
        setInternalItems(res.items);
        setTotalPages(res.totalPages);
        setTotalCount(res.totalCount);
      } else {
        const res = await listExternalProviders({
          page,
          pageSize: 10,
          lang,
        });
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
      setLoadError({
        message: apiErr?.message ?? copy.errorTitle,
        traceId: apiErr?.traceId,
      });
    } finally {
      setLoading(false);
    }
  }, [canView, tab, page, lang, onSessionExpired, copy.errorTitle]);

  useEffect(() => {
    if (mode === 'list' && canView) {
      void loadDirectory();
    }
  }, [mode, canView, loadDirectory]);

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
    setEditingExt(null);
    setExtName('');
    setExtDesc('');
    setExtActive(true);
    setSelectedSystemId('');
    if (tab === 'internal') {
      try {
        const sysRes = await listCompanySystems({ page: 1, pageSize: 100, isActive: 'true', lang });
        const eligible = sysRes.items.filter((s) => !s.isRegisteredInternalProvider);
        setCompanySystems(eligible);
        if (eligible.length > 0) {
          setSelectedSystemId(eligible[0].id);
        }
      } catch (err) {
        handleApiError(err);
        return;
      }
    }
    setMode('create');
  };

  const handleSave = async () => {
    if (tab === 'internal') {
      if (!selectedSystemId) {
        setFieldError(copy.requiredSystemError);
        setModal({
          variant: 'error',
          title: copy.validationTitle,
          message: copy.validationMsg,
        });
        return;
      }
      try {
        await createInternalProvider(selectedSystemId, lang);
        setMode('list');
        setModal({
          variant: 'success',
          title: copy.createSuccessTitle,
          message: copy.createSuccessMsg,
        });
        await loadDirectory();
      } catch (err) {
        handleApiError(err);
      }
    } else {
      if (extName.trim().length < 2) {
        setFieldError(copy.requiredNameError);
        setModal({
          variant: 'error',
          title: copy.validationTitle,
          message: copy.validationMsg,
        });
        return;
      }
      try {
        if (mode === 'create') {
          await createExternalProvider(
            { name: extName.trim(), description: extDesc.trim() || null },
            lang
          );
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
        }
        const wasCreate = mode === 'create';
        setMode('list');
        setModal({
          variant: 'success',
          title: wasCreate ? copy.createSuccessTitle : copy.editSuccessTitle,
          message: wasCreate ? copy.createSuccessMsg : copy.editSuccessMsg,
        });
        await loadDirectory();
      } catch (err) {
        handleApiError(err);
      }
    }
  };

  const internalColumns: DataTableColumn<InternalProviderItem>[] = useMemo(
    () => [
      { key: 'name', header: copy.colName, render: (row) => <strong>{row.companySystemName}</strong> },
      {
        key: 'desc',
        header: copy.colDesc,
        render: (row) => row.companySystemDescription || <span style={{ color: 'var(--text-muted)' }}>—</span>,
      },
      {
        key: 'companyStatus',
        header: copy.colCompanyStatus,
        render: (row) => (
          <StatusBadge variant={row.companySystemIsActive ? 'info' : 'warning'}>
            {row.companySystemIsActive ? copy.activeBadge : copy.inactiveBadge}
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
          canEdit || canDelete ? (
            <div className="row-actions">
              {canEdit && (
                <RowActionButton
                  icon="power"
                  label={row.isActive ? copy.deactivateBtn : copy.activateBtn}
                  variant={row.isActive ? 'warning' : 'success'}
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
                />
              )}
              {canDelete && (
                <RowActionButton
                  icon="trash"
                  label={copy.deleteBtn}
                  variant="danger"
                  onClick={() =>
                    setModal({
                      variant: 'confirm',
                      title: copy.deleteTitle,
                      message: copy.deleteMsg,
                      onConfirmAction: async () => {
                        try {
                          await deleteInternalProvider(row.id, lang);
                          setModal({
                            variant: 'success',
                            title: copy.deleteSuccessTitle,
                            message: copy.deleteSuccessMsg,
                          });
                          await loadDirectory();
                        } catch (err) {
                          handleApiError(err);
                        }
                      },
                    })
                  }
                />
              )}
            </div>
          ) : null,
      },
    ],
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [copy, canEdit, canDelete, lang]
  );

  const externalColumns: DataTableColumn<ExternalProviderItem>[] = useMemo(
    () => [
      { key: 'name', header: copy.colName, render: (row) => <strong>{row.name}</strong> },
      {
        key: 'desc',
        header: copy.colDesc,
        render: (row) => row.description || <span style={{ color: 'var(--text-muted)' }}>—</span>,
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
          canEdit || canDelete ? (
            <div className="row-actions">
              {canEdit && (
                <>
                  <RowActionButton
                    icon="edit"
                    label={copy.editBtn}
                    onClick={() => {
                      setEditingExt(row);
                      setExtName(row.name);
                      setExtDesc(row.description ?? '');
                      setExtActive(row.isActive);
                      setFieldError('');
                      setMode('edit');
                    }}
                  />
                  <RowActionButton
                    icon="power"
                    label={row.isActive ? copy.deactivateBtn : copy.activateBtn}
                    variant={row.isActive ? 'warning' : 'success'}
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
                  />
                </>
              )}
              {canDelete && (
                <RowActionButton
                  icon="trash"
                  label={copy.deleteBtn}
                  variant="danger"
                  onClick={() =>
                    setModal({
                      variant: 'confirm',
                      title: copy.deleteTitle,
                      message: copy.deleteMsg,
                      onConfirmAction: async () => {
                        try {
                          await deleteExternalProvider(row.id, lang);
                          setModal({
                            variant: 'success',
                            title: copy.deleteSuccessTitle,
                            message: copy.deleteSuccessMsg,
                          });
                          await loadDirectory();
                        } catch (err) {
                          handleApiError(err);
                        }
                      },
                    })
                  }
                />
              )}
            </div>
          ) : null,
      },
    ],
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [copy, canEdit, canDelete, lang]
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
        <section className="surface-card panel clean-table-panel">
          <div className="clean-tabs-header-row">
            <div className="tabs" style={{ marginBottom: 0 }}>
              <button
                type="button"
                className={`tab ${tab === 'internal' ? 'active' : ''}`}
                onClick={() => {
                  setTab('internal');
                  setPage(1);
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
                }}
              >
                {copy.tabExternal}
              </button>
            </div>

            {canCreate && (
              <button
                type="button"
                className="compact-inline-add-btn"
                aria-label={tab === 'internal' ? copy.addInternalAria : copy.addExternalAria}
                title={tab === 'internal' ? copy.addInternalAria : copy.addExternalAria}
                onClick={() => void openCreatePage()}
              >
                <ReferenceIcon name="plus" size={18} />
              </button>
            )}
          </div>

          {loadError ? (
            <ErrorState
              title={copy.errorTitle}
              message={loadError.message}
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
          formSubtitle=""
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
                <FormField id="external-provider-name" label={copy.nameLabel} required error={fieldError}>
                  <TextInput
                    ref={nameRef}
                    id="external-provider-name"
                    value={extName}
                    hasError={Boolean(fieldError)}
                    onChange={(e) => {
                      setExtName(e.target.value);
                      setFieldError('');
                    }}
                  />
                </FormField>

                {mode === 'edit' && (
                  <div className="form-field" style={{ justifyContent: 'center' }}>
                    <label className="matrix-inline-check">
                      <input
                        type="checkbox"
                        className="matrix-checkbox"
                        checked={extActive}
                        onChange={(e) => setExtActive(e.target.checked)}
                      />
                      <span>{copy.activeLabel}</span>
                    </label>
                  </div>
                )}
              </div>

              <FormField id="external-provider-desc" label={copy.descLabel}>
                <TextareaInput
                  id="external-provider-desc"
                  rows={3}
                  value={extDesc}
                  onChange={(e) => setExtDesc(e.target.value)}
                />
              </FormField>
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
        cancelLabel={t.modalCancelBtn}
        closeAriaLabel={t.modalCloseAria}
        onConfirm={() => {
          const active = modal;
          if (!active) return;
          if (active.variant === 'confirm' && active.onConfirmAction) {
            void active.onConfirmAction();
            return;
          }
          setModal(null);
          if (active.variant === 'error' && fieldError) {
            nameRef.current?.focus();
          }
        }}
        onCancel={() => setModal(null)}
      />
    </>
  );
}
