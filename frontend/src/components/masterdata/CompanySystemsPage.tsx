import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { ApiProblemError } from '../../api/authApi';
import {
  ApiGroupItem,
  CompanySystemItem,
  CompanySystemUsageType,
  createApiGroup,
  createCompanySystem,
  deleteApiGroup,
  deleteCompanySystem,
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
  ReferenceIcon,
  RowActionButton,
  StandaloneFormPage,
  StatusBadge,
  TextInput,
  TextareaInput,
} from '../ui';

export type MasterSystemSection = 'systems' | 'api-groups';

interface CompanySystemsPageProps {
  lang: AppLanguage;
  permissions: string[];
  section?: MasterSystemSection;
  onSessionExpired: () => void;
}

type PageMode = 'list' | 'create' | 'edit';

interface ModalState {
  variant: 'success' | 'error' | 'confirm';
  title: string;
  message: string;
  onConfirmAction?: () => Promise<void>;
}

const COPY = {
  ar: {
    noViewSysTitle: 'صلاحية عرض أنظمة الشركة غير متوفرة',
    noViewSysDesc: 'يتطلب عرض أنظمة الشركة توفر صلاحية العرض على حسابك.',
    noViewGrpTitle: 'صلاحية عرض مجموعة واجهات API غير متوفرة',
    noViewGrpDesc: 'يتطلب عرض مجموعة واجهات API توفر صلاحية العرض على حسابك.',
    addSysAria: 'إضافة نظام شركة',
    addGrpAria: 'إضافة مجموعة واجهات API',
    colName: 'الاسم',
    colDesc: 'الوصف',
    colType: 'النوع',
    colDocsCount: 'الوثائق المرتبطة',
    colStatus: 'الحالة',
    colActions: 'الإجراءات',
    typeProvider: 'مزوّد',
    typeConsumer: 'مستهلك',
    typeBoth: 'مزوّد ومستهلك',
    typeNone: '—',
    activeBadge: 'فعّال',
    inactiveBadge: 'معطّل',
    editBtn: 'تعديل',
    deactivateBtn: 'تعطيل',
    activateBtn: 'تفعيل',
    deleteBtn: 'حذف',
    deleteTitle: 'تأكيد الحذف',
    deleteMsg: 'هل تؤكد حذف هذا السجل نهائيًا؟',
    deleteSuccessTitle: 'تم الحذف بنجاح',
    deleteSuccessMsg: 'تم حذف السجل من النظام بنجاح.',
    createSysBadge: 'إضافة نظام شركة',
    editSysBadge: 'تعديل نظام شركة',
    createGrpBadge: 'إضافة مجموعة واجهات API',
    editGrpBadge: 'تعديل مجموعة واجهات API',
    createSysTitle: 'إضافة نظام شركة جديد',
    editSysTitle: 'تعديل بيانات نظام الشركة',
    createGrpTitle: 'إضافة مجموعة واجهات API جديدة',
    editGrpTitle: 'تعديل بيانات مجموعة واجهات API',
    sysFormSub: '',
    grpFormSub: '',
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
    createSuccessMsg: 'تم حفظ السجل بنجاح.',
    editSuccessTitle: 'تم التحديث بنجاح',
    editSuccessMsg: 'تم حفظ التعديلات بنجاح.',
    toggleTitle: 'تأكيد تغيير الحالة',
    deactivateMsg: 'هل تؤكد تعطيل هذا السجل؟',
    activateMsg: 'هل تريد إعادة تفعيل هذا السجل؟',
    loadingText: 'جارٍ تحميل البيانات...',
    emptyTitle: 'لا توجد سجلات',
    emptyDesc: 'اضغط على زر (+) لإضافة سجل جديد.',
    errorTitle: 'تعذر تحميل البيانات',
    retryBtn: 'إعادة المحاولة',
    pagePrefix: 'الصفحة',
    ofWord: 'من',
    prevBtn: 'السابق',
    nextBtn: 'التالي',
  },
  en: {
    noViewSysTitle: 'Company Systems View Unavailable',
    noViewSysDesc: 'View permission is required to view company systems.',
    noViewGrpTitle: 'API Interface Groups View Unavailable',
    noViewGrpDesc: 'View permission is required to view API interface groups.',
    addSysAria: 'Add Company System',
    addGrpAria: 'Add API Interface Group',
    colName: 'Name',
    colDesc: 'Description',
    colType: 'Type',
    colDocsCount: 'Linked Docs',
    colStatus: 'Status',
    colActions: 'Actions',
    typeProvider: 'Provider',
    typeConsumer: 'Consumer',
    typeBoth: 'Provider & Consumer',
    typeNone: '—',
    activeBadge: 'Active',
    inactiveBadge: 'Inactive',
    editBtn: 'Edit',
    deactivateBtn: 'Deactivate',
    activateBtn: 'Activate',
    deleteBtn: 'Delete',
    deleteTitle: 'Confirm Deletion',
    deleteMsg: 'Are you sure you want to permanently delete this record?',
    deleteSuccessTitle: 'Record Deleted',
    deleteSuccessMsg: 'Record has been permanently deleted.',
    createSysBadge: 'Create Company System',
    editSysBadge: 'Edit Company System',
    createGrpBadge: 'Create API Interface Group',
    editGrpBadge: 'Edit API Interface Group',
    createSysTitle: 'Create Company System',
    editSysTitle: 'Edit Company System',
    createGrpTitle: 'Create API Interface Group',
    editGrpTitle: 'Edit API Interface Group',
    sysFormSub: '',
    grpFormSub: '',
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
    createSuccessMsg: 'Record saved successfully.',
    editSuccessTitle: 'Record Updated',
    editSuccessMsg: 'Changes saved successfully.',
    toggleTitle: 'Confirm Status Change',
    deactivateMsg: 'Are you sure you want to deactivate this record?',
    activateMsg: 'Re-enable this record?',
    loadingText: 'Loading records...',
    emptyTitle: 'No records found',
    emptyDesc: 'Click the (+) button to add a new record.',
    errorTitle: 'Unable to Load Records',
    retryBtn: 'Retry',
    pagePrefix: 'Page',
    ofWord: 'of',
    prevBtn: 'Previous',
    nextBtn: 'Next',
  },
} as const;

export function CompanySystemsPage({
  lang,
  permissions,
  section = 'systems',
  onSessionExpired,
}: CompanySystemsPageProps) {
  const copy = COPY[lang];
  const t = UI_STRINGS[lang];
  const hasPerm = (code: string) => permissions.some((p) => p.toLowerCase() === code.toLowerCase());

  const canViewSys = hasPerm('company_systems.view');
  const canCreateSys = hasPerm('company_systems.create');
  const canEditSys = hasPerm('company_systems.edit');
  const canDeleteSys = hasPerm('company_systems.delete');
  const canViewGrp = hasPerm('api_groups.view');
  const canCreateGrp = hasPerm('api_groups.create');
  const canEditGrp = hasPerm('api_groups.edit');
  const canDeleteGrp = hasPerm('api_groups.delete');

  const tab = section;
  const [mode, setMode] = useState<PageMode>('list');
  const [editingId, setEditingId] = useState<string | null>(null);

  const [systems, setSystems] = useState<CompanySystemItem[]>([]);
  const [groups, setGroups] = useState<ApiGroupItem[]>([]);
  const [page, setPage] = useState(1);
  const [totalPages, setTotalPages] = useState(1);
  const [totalCount, setTotalCount] = useState(0);
  const [loading, setLoading] = useState(false);
  const [loadError, setLoadError] = useState<{ message: string; traceId?: string } | null>(null);

  const [formName, setFormName] = useState('');
  const [formDesc, setFormDesc] = useState('');
  const [hadInitialDesc, setHadInitialDesc] = useState(false);
  const [formActive, setFormActive] = useState(true);
  const [nameError, setNameError] = useState('');
  const [modal, setModal] = useState<ModalState | null>(null);
  const nameInputRef = useRef<HTMLInputElement | null>(null);

  const loadData = useCallback(async () => {
    setLoading(true);
    setLoadError(null);
    try {
      if (tab === 'systems') {
        if (!canViewSys) return;
        const res = await listCompanySystems({
          page,
          pageSize: 10,
          lang,
        });
        setSystems(res.items);
        setTotalPages(res.totalPages);
        setTotalCount(res.totalCount);
      } else {
        if (!canViewGrp) return;
        const res = await listApiGroups({
          page,
          pageSize: 10,
          lang,
        });
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
      setLoadError({
        message: apiErr?.message ?? copy.errorTitle,
        traceId: apiErr?.traceId,
      });
    } finally {
      setLoading(false);
    }
  }, [tab, canViewSys, canViewGrp, page, lang, onSessionExpired, copy.errorTitle]);

  useEffect(() => {
    if (mode === 'list') {
      void loadData();
    }
  }, [mode, loadData]);

  const renderSystemTypeBadge = (system: CompanySystemItem) => {
    const isProv = system.isRegisteredInternalProvider || system.documentsAsProviderCount > 0;
    const isCons = system.documentsAsConsumerCount > 0;

    if (isProv && isCons) {
      return <StatusBadge variant="success">{copy.typeBoth}</StatusBadge>;
    }
    if (isProv) {
      return <StatusBadge variant="info">{copy.typeProvider}</StatusBadge>;
    }
    if (isCons) {
      return <StatusBadge variant="warning">{copy.typeConsumer}</StatusBadge>;
    }
    return <span style={{ color: 'var(--text-muted)' }}>{copy.typeNone}</span>;
  };

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

  const handleSaveForm = async () => {
    if (formName.trim().length < 2) {
      setNameError(copy.requiredNameError);
      setModal({
        variant: 'error',
        title: copy.validationTitle,
        message: copy.validationMsg,
      });
      return;
    }

    try {
      if (tab === 'systems') {
        if (mode === 'create') {
          await createCompanySystem(
            { name: formName.trim(), description: formDesc.trim() || null },
            lang
          );
        } else if (editingId) {
          await updateCompanySystem(
            editingId,
            {
              name: formName.trim(),
              description: formDesc.trim() || null,
              clearDescription: !formDesc.trim() && hadInitialDesc,
              isActive: formActive,
            },
            lang
          );
        }
      } else {
        if (mode === 'create') {
          await createApiGroup(
            { name: formName.trim(), description: formDesc.trim() || null },
            lang
          );
        } else if (editingId) {
          await updateApiGroup(
            editingId,
            {
              name: formName.trim(),
              description: formDesc.trim() || null,
              clearDescription: !formDesc.trim() && hadInitialDesc,
              isActive: formActive,
            },
            lang
          );
        }
      }

      const wasCreate = mode === 'create';
      setMode('list');
      setModal({
        variant: 'success',
        title: wasCreate ? copy.createSuccessTitle : copy.editSuccessTitle,
        message: wasCreate ? copy.createSuccessMsg : copy.editSuccessMsg,
      });
      await loadData();
    } catch (err) {
      handleApiError(err);
    }
  };

  const systemColumns: DataTableColumn<CompanySystemItem>[] = useMemo(
    () => [
      { key: 'name', header: copy.colName, render: (row) => <strong>{row.name}</strong> },
      {
        key: 'description',
        header: copy.colDesc,
        render: (row) => row.description || <span style={{ color: 'var(--text-muted)' }}>—</span>,
      },
      {
        key: 'type',
        header: copy.colType,
        render: (row) => renderSystemTypeBadge(row),
      },
      {
        key: 'docs',
        header: copy.colDocsCount,
        render: (row) => (
          <code className="ltr-code">
            P:{row.documentsAsProviderCount} / C:{row.documentsAsConsumerCount}
          </code>
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
          canEditSys || canDeleteSys ? (
            <div className="row-actions">
              {canEditSys && (
                <>
                  <RowActionButton
                    icon="edit"
                    label={copy.editBtn}
                    onClick={() => {
                      setEditingId(row.id);
                      setFormName(row.name);
                      setFormDesc(row.description ?? '');
                      setHadInitialDesc(Boolean(row.description));
                      setFormActive(row.isActive);
                      setNameError('');
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
                  />
                </>
              )}
              {canDeleteSys && (
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
                          await deleteCompanySystem(row.id, lang);
                          setModal({
                            variant: 'success',
                            title: copy.deleteSuccessTitle,
                            message: copy.deleteSuccessMsg,
                          });
                          await loadData();
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
    [copy, canEditSys, canDeleteSys, lang]
  );

  const groupColumns: DataTableColumn<ApiGroupItem>[] = useMemo(
    () => [
      { key: 'name', header: copy.colName, render: (row) => <strong>{row.name}</strong> },
      {
        key: 'description',
        header: copy.colDesc,
        render: (row) => row.description || <span style={{ color: 'var(--text-muted)' }}>—</span>,
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
          canEditGrp || canDeleteGrp ? (
            <div className="row-actions">
              {canEditGrp && (
                <>
                  <RowActionButton
                    icon="edit"
                    label={copy.editBtn}
                    onClick={() => {
                      setEditingId(row.id);
                      setFormName(row.name);
                      setFormDesc(row.description ?? '');
                      setHadInitialDesc(Boolean(row.description));
                      setFormActive(row.isActive);
                      setNameError('');
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
                  />
                </>
              )}
              {canDeleteGrp && (
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
                          await deleteApiGroup(row.id, lang);
                          setModal({
                            variant: 'success',
                            title: copy.deleteSuccessTitle,
                            message: copy.deleteSuccessMsg,
                          });
                          await loadData();
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
    [copy, canEditGrp, canDeleteGrp, lang]
  );

  const canViewActiveTab = tab === 'systems' ? canViewSys : canViewGrp;
  const canCreateActiveTab = tab === 'systems' ? canCreateSys : canCreateGrp;

  return (
    <>
      {mode === 'list' ? (
        <section className="surface-card panel clean-table-panel">
          {canCreateActiveTab && (
            <button
              type="button"
              className="compact-add-icon-btn"
              aria-label={tab === 'systems' ? copy.addSysAria : copy.addGrpAria}
              title={tab === 'systems' ? copy.addSysAria : copy.addGrpAria}
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
              <ReferenceIcon name="plus" size={18} />
            </button>
          )}

          {!canViewActiveTab ? (
            <ErrorState
              title={tab === 'systems' ? copy.noViewSysTitle : copy.noViewGrpTitle}
              message={tab === 'systems' ? copy.noViewSysDesc : copy.noViewGrpDesc}
            />
          ) : loadError ? (
            <ErrorState
              title={copy.errorTitle}
              message={loadError.message}
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
        </section>
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
          onSubmit={() => void handleSaveForm()}
        >
          <div className="form-grid-2">
            <FormField id="m01-name" label={copy.nameLabel} required error={nameError}>
              <TextInput
                ref={nameInputRef}
                id="m01-name"
                value={formName}
                hasError={Boolean(nameError)}
                onChange={(e) => {
                  setFormName(e.target.value);
                  setNameError('');
                }}
              />
            </FormField>

            {mode === 'edit' && (
              <div className="form-field" style={{ justifyContent: 'center' }}>
                <label className="matrix-inline-check">
                  <input
                    type="checkbox"
                    className="matrix-checkbox"
                    checked={formActive}
                    onChange={(e) => setFormActive(e.target.checked)}
                  />
                  <span>{copy.activeStatusLabel}</span>
                </label>
              </div>
            )}
          </div>

          <FormField id="m01-desc" label={copy.descLabel}>
            <TextareaInput
              id="m01-desc"
              rows={3}
              value={formDesc}
              onChange={(e) => setFormDesc(e.target.value)}
            />
          </FormField>
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
          if (active.variant === 'error' && nameError) {
            nameInputRef.current?.focus();
          }
        }}
        onCancel={() => setModal(null)}
      />
    </>
  );
}
