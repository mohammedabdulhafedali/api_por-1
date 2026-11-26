import { useCallback, useEffect, useMemo, useState } from 'react';
import { ApiProblemError } from '../../api/authApi';
import {
  ApprovalRouteItem,
  ApprovalRouteRoleOption,
  ApprovalRouteStageInput,
  createApprovalRoute,
  deleteApprovalRoute,
  listApprovalRouteRoles,
  listApprovalRoutes,
  replaceApprovalRouteStages,
  updateApprovalRoute,
} from '../../api/governanceConfigApi';
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
} from '../ui';

interface ApprovalRoutesPageProps {
  lang: AppLanguage;
  permissions: string[];
  onSessionExpired: () => void;
}

type PageMode = 'list' | 'create' | 'edit' | 'stages';

interface StageDraftRow {
  key: string;
  stageName: string;
  responsibleRoleId: string;
}

interface ModalState {
  variant: 'success' | 'error' | 'confirm';
  title: string;
  message: string;
  onConfirmAction?: () => Promise<void>;
}

const COPY = {
  ar: {
    pageTitle: 'مسارات الاعتماد',
    pageDesc:
      'تعريف مسارات الاعتماد بمراحل متسلسلة تحدد اسم المرحلة وترتيبها والدور المسؤول فقط (دون نوع مرحلة أو تعيين مستخدم).',
    noViewTitle: 'صلاحية عرض مسارات الاعتماد غير متوفرة',
    noViewDesc: 'يتطلب عرض مسارات الاعتماد توفر صلاحية routes.view.',
    addBtn: 'إضافة مسار اعتماد',
    searchPlaceholder: 'ابحث باسم مسار الاعتماد...',
    searchAria: 'البحث في مسارات الاعتماد',
    filterAria: 'تصفية حسب الحالة',
    filterAll: 'كل الحالات',
    filterActive: 'الفعّالة فقط',
    filterInactive: 'المعطلة فقط',
    colName: 'اسم المسار',
    colStages: 'المراحل المرتبة والدور المسؤول',
    colLinks: 'الارتباطات',
    colStatus: 'الحالة',
    colActions: 'الإجراءات',
    activeBadge: 'فعّال',
    inactiveBadge: 'معطّل',
    noStagesWarning: 'لا توجد مراحل (مسار غير صالح)',
    editRouteBtn: 'تعديل المسار',
    manageStagesBtn: 'إدارة المراحل',
    deactivateBtn: 'تعطيل',
    activateBtn: 'تفعيل',
    deleteBtn: 'حذف',
    deleteTitle: 'تأكيد حذف مسار الاعتماد',
    deleteConfirmMsg: 'هل أنت متأكد من حذف مسار الاعتماد؟ سيُمنع الحذف إذا كان مرتبطًا بأنواع تغيير أو وثائق أو معاملات.',
    deleteSuccessTitle: 'تم الحذف بنجاح',
    deleteSuccessMsg: 'تم حذف مسار الاعتماد.',
    createBadge: 'إضافة مسار اعتماد',
    editBadge: 'تعديل مسار اعتماد',
    stagesBadge: 'إدارة مراحل المسار',
    createTitle: 'إضافة مسار اعتماد',
    editTitle: 'تعديل مسار الاعتماد',
    stagesTitle: 'مراحل مسار الاعتماد',
    formSub: '',
    backBtn: 'العودة',
    saveCreateBtn: 'حفظ',
    saveEditBtn: 'حفظ التعديلات',
    saveStagesBtn: 'حفظ المراحل',
    cancelBtn: 'إلغاء',
    routeNameLabel: 'اسم مسار الاعتماد',
    stagesSectionTitle: 'المراحل المرتبة للمسار',
    addStageBtn: 'إضافة مرحلة',
    moveUpBtn: 'أعلى ↑',
    moveDownBtn: 'أسفل ↓',
    removeStageBtn: 'حذف المرحلة',
    stageOrderLabel: 'الترتيب',
    stageNameLabel: 'اسم المرحلة',
    responsibleRoleLabel: 'الدور المسؤول',
    selectRole: '-- اختر الدور المسؤول --',
    noRolesNotice: 'لا توجد أدوار نشطة؛ يرجى تعريف الأدوار أولًا.',
    validationRouteName: 'اسم مسار الاعتماد مطلوب (حرفان على الأقل).',
    validationStages: 'يجب إضافة مرحلة واحدة على الأقل باسم صحيح ودور مسؤول نشط دون تكرار اسم المرحلة.',
    createdTitle: 'تم إنشاء مسار الاعتماد',
    createdMsg: 'تم إنشاء مسار الاعتماد وحفظ مراحله المرتبة بنجاح.',
    updatedTitle: 'تم تحديث مسار الاعتماد',
    updatedMsg: 'تم حفظ تعديلات مسار الاعتماد ومراحله دون التأثير في المعاملات الجارية.',
    confirmDeactivateTitle: 'تأكيد تعطيل مسار الاعتماد',
    confirmDeactivateMsg: 'هل تريد تعطيل مسار الاعتماد؟ تُمنع العملية إذا كان مرتبطًا بأنواع تغيير أو وثائق نشطة.',
    linksSummary: (types: number, docs: number) => `أنواع تغيير: ${types} · وثائق: ${docs}`,
    loadingText: 'جارٍ تحميل البيانات...',
    pageWord: 'صفحة',
    ofWord: 'من',
  },
  en: {
    pageTitle: 'Approval Routes',
    pageDesc:
      'Configure multi-stage approval routes with stage name, order, and responsible role ONLY (no stage type or assigned user).',
    noViewTitle: 'Missing approval routes permission',
    noViewDesc: 'Viewing approval routes requires routes.view permission.',
    addBtn: 'Add Approval Route',
    searchPlaceholder: 'Search by route name...',
    searchAria: 'Search approval routes',
    filterAria: 'Filter by status',
    filterAll: 'All statuses',
    filterActive: 'Active only',
    filterInactive: 'Inactive only',
    colName: 'Route Name',
    colStages: 'Ordered Stages & Role',
    colLinks: 'References',
    colStatus: 'Status',
    colActions: 'Actions',
    activeBadge: 'Active',
    inactiveBadge: 'Inactive',
    noStagesWarning: '0 stages (invalid route)',
    editRouteBtn: 'Edit Route',
    manageStagesBtn: 'Manage Stages',
    deactivateBtn: 'Deactivate',
    activateBtn: 'Activate',
    deleteBtn: 'Delete',
    deleteTitle: 'Confirm Route Deletion',
    deleteConfirmMsg: 'Are you sure you want to delete this approval route? Blocked if referenced by change types, documents, or runs.',
    deleteSuccessTitle: 'Deleted Successfully',
    deleteSuccessMsg: 'Approval route has been deleted.',
    createBadge: 'Create Approval Route',
    editBadge: 'Edit Approval Route',
    stagesBadge: 'Manage Route Stages',
    createTitle: 'Add Approval Route',
    editTitle: 'Edit Approval Route',
    stagesTitle: 'Route Stages',
    formSub: '',
    backBtn: 'Back',
    saveCreateBtn: 'Save',
    saveEditBtn: 'Save Changes',
    saveStagesBtn: 'Save Stages',
    cancelBtn: 'Cancel',
    routeNameLabel: 'Route Name',
    stagesSectionTitle: 'Ordered Approval Stages',
    addStageBtn: 'Add Stage',
    moveUpBtn: 'Up ↑',
    moveDownBtn: 'Down ↓',
    removeStageBtn: 'Remove Stage',
    stageOrderLabel: 'Order',
    stageNameLabel: 'Stage Name',
    responsibleRoleLabel: 'Responsible Role',
    selectRole: '-- Select Responsible Role --',
    noRolesNotice: 'No active roles available; please create roles first.',
    validationRouteName: 'Route name is required (at least 2 characters).',
    validationStages: 'Add at least one unique stage with valid name and active responsible role.',
    createdTitle: 'Approval Route Created',
    createdMsg: 'Approval route and ordered stages saved successfully.',
    updatedTitle: 'Approval Route Updated',
    updatedMsg: 'Approval route updates saved without altering running workflow instances.',
    confirmDeactivateTitle: 'Confirm Route Deactivation',
    confirmDeactivateMsg: 'Deactivate this approval route? Blocked if linked to active change types or documents.',
    linksSummary: (types: number, docs: number) => `Change types: ${types} · Docs: ${docs}`,
    loadingText: 'Loading data...',
    pageWord: 'Page',
    ofWord: 'of',
  },
} as const;

export function ApprovalRoutesPage({
  lang,
  permissions,
  onSessionExpired,
}: ApprovalRoutesPageProps) {
  const c = COPY[lang];
  const commonStrings = UI_STRINGS[lang];
  const permSet = useMemo(() => new Set(permissions.map((p) => p.toLowerCase())), [permissions]);
  const canView = permSet.has('routes.view');
  const canCreate = permSet.has('routes.create');
  const canEdit = permSet.has('routes.edit');
  const canDelete = permSet.has('routes.delete');

  const [mode, setMode] = useState<PageMode>('list');
  const [searchQuery, setSearchQuery] = useState('');
  const [statusFilter, setStatusFilter] = useState('');
  const [page, setPage] = useState(1);
  const [items, setItems] = useState<ApprovalRouteItem[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [totalPages, setTotalPages] = useState(1);
  const [loading, setLoading] = useState(false);
  const [errorMsg, setErrorMsg] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [modal, setModal] = useState<ModalState | null>(null);

  const [roleOptions, setRoleOptions] = useState<ApprovalRouteRoleOption[]>([]);
  const [selectedRoute, setSelectedRoute] = useState<ApprovalRouteItem | null>(null);
  const [routeName, setRouteName] = useState('');
  const [stageRows, setStageRows] = useState<StageDraftRow[]>([]);
  const [routeNameError, setRouteNameError] = useState<string | undefined>();
  const [stagesError, setStagesError] = useState<string | undefined>();

  const handleApiError = useCallback(
    (err: unknown, fallbackTitle: string) => {
      if (err instanceof ApiProblemError && err.status === 401) {
        onSessionExpired();
        return;
      }
      const message = err instanceof Error ? err.message : fallbackTitle;
      setModal({ variant: 'error', title: fallbackTitle, message });
    },
    [onSessionExpired]
  );

  const loadRoutes = useCallback(async () => {
    if (!canView) return;
    setLoading(true);
    setErrorMsg(null);
    try {
      const isActive = statusFilter === 'active' ? true : statusFilter === 'inactive' ? false : null;
      const result = await listApprovalRoutes(
        { page, pageSize: 10, search: searchQuery, isActive },
        lang
      );
      setItems(result.items);
      setTotalCount(result.totalCount);
      setTotalPages(Math.max(1, result.totalPages));
    } catch (err) {
      if (err instanceof ApiProblemError && err.status === 401) {
        onSessionExpired();
        return;
      }
      setErrorMsg(err instanceof Error ? err.message : 'Load error');
    } finally {
      setLoading(false);
    }
  }, [canView, lang, onSessionExpired, page, searchQuery, statusFilter]);

  const loadRoleOptions = useCallback(async () => {
    if (!canView) return;
    try {
      const list = await listApprovalRouteRoles(lang);
      setRoleOptions(list);
    } catch {
      // Ignore secondary lookup error until save
    }
  }, [canView, lang]);

  useEffect(() => {
    void loadRoutes();
  }, [loadRoutes]);

  useEffect(() => {
    void loadRoleOptions();
  }, [loadRoleOptions]);

  const openCreate = () => {
    setSelectedRoute(null);
    setRouteName('');
    setStageRows([
      {
        key: `stage-init-${Date.now()}`,
        stageName: '',
        responsibleRoleId: roleOptions[0]?.id ?? '',
      },
    ]);
    setRouteNameError(undefined);
    setStagesError(undefined);
    setMode('create');
  };

  const openEditRouteName = (route: ApprovalRouteItem) => {
    setSelectedRoute(route);
    setRouteName(route.name);
    setRouteNameError(undefined);
    setStagesError(undefined);
    setMode('edit');
  };

  const openManageStages = (route: ApprovalRouteItem) => {
    setSelectedRoute(route);
    setRouteName(route.name);
    setStageRows(
      route.stages.length > 0
        ? route.stages
            .slice()
            .sort((a, b) => a.position - b.position)
            .map((s, idx) => ({
              key: `stage-${s.id}-${idx}`,
              stageName: s.stageName,
              responsibleRoleId: s.responsibleRoleId,
            }))
        : [
            {
              key: `stage-new-${Date.now()}`,
              stageName: '',
              responsibleRoleId: roleOptions[0]?.id ?? '',
            },
          ]
    );
    setRouteNameError(undefined);
    setStagesError(undefined);
    setMode('stages');
  };

  const addStageRow = () => {
    setStageRows((prev) => [
      ...prev,
      {
        key: `stage-${Date.now()}-${prev.length}`,
        stageName: '',
        responsibleRoleId: roleOptions[0]?.id ?? '',
      },
    ]);
  };

  const moveStageRow = (index: number, delta: -1 | 1) => {
    const targetIndex = index + delta;
    if (targetIndex < 0 || targetIndex >= stageRows.length) return;
    setStageRows((prev) => {
      const copy = [...prev];
      const [moved] = copy.splice(index, 1);
      copy.splice(targetIndex, 0, moved);
      return copy;
    });
  };

  const validateAndBuildStages = (): ApprovalRouteStageInput[] | null => {
    if (stageRows.length === 0) {
      setStagesError(c.validationStages);
      return null;
    }

    const seenNames = new Set<string>();
    const payload: ApprovalRouteStageInput[] = [];

    for (let i = 0; i < stageRows.length; i++) {
      const row = stageRows[i];
      const name = row.stageName.trim();
      const key = name.toLowerCase();
      if (name.length < 2 || !row.responsibleRoleId || seenNames.has(key)) {
        setStagesError(c.validationStages);
        return null;
      }
      seenNames.add(key);
      payload.push({
        position: i + 1,
        stageName: name,
        responsibleRoleId: row.responsibleRoleId,
      });
    }
    return payload;
  };

  const handleSubmit = async () => {
    setRouteNameError(undefined);
    setStagesError(undefined);

    if (mode === 'create' || mode === 'edit') {
      const trimmedName = routeName.trim();
      if (trimmedName.length < 2) {
        setRouteNameError(c.validationRouteName);
        return;
      }

      if (mode === 'create') {
        const stages = validateAndBuildStages();
        if (!stages) return;

        setSubmitting(true);
        try {
          await createApprovalRoute({ name: trimmedName, stages }, lang);
          setModal({ variant: 'success', title: c.createdTitle, message: c.createdMsg });
          setMode('list');
          await loadRoutes();
        } catch (err) {
          handleApiError(err, c.createTitle);
        } finally {
          setSubmitting(false);
        }
      } else if (selectedRoute) {
        setSubmitting(true);
        try {
          await updateApprovalRoute(selectedRoute.id, { name: trimmedName }, lang);
          setModal({ variant: 'success', title: c.updatedTitle, message: c.updatedMsg });
          setMode('list');
          await loadRoutes();
        } catch (err) {
          handleApiError(err, c.editTitle);
        } finally {
          setSubmitting(false);
        }
      }
      return;
    }

    if (mode === 'stages' && selectedRoute) {
      const stages = validateAndBuildStages();
      if (!stages) return;

      setSubmitting(true);
      try {
        await replaceApprovalRouteStages(selectedRoute.id, { stages }, lang);
        setModal({ variant: 'success', title: c.updatedTitle, message: c.updatedMsg });
        setMode('list');
        await loadRoutes();
      } catch (err) {
        handleApiError(err, c.stagesTitle);
      } finally {
        setSubmitting(false);
      }
    }
  };

  const handleToggleStatus = (route: ApprovalRouteItem) => {
    const runToggle = async () => {
      setSubmitting(true);
      try {
        await updateApprovalRoute(route.id, { isActive: !route.isActive }, lang);
        setModal({ variant: 'success', title: c.updatedTitle, message: c.updatedMsg });
        await loadRoutes();
      } catch (err) {
        handleApiError(err, c.editTitle);
      } finally {
        setSubmitting(false);
      }
    };

    if (route.isActive) {
      setModal({
        variant: 'confirm',
        title: c.confirmDeactivateTitle,
        message: c.confirmDeactivateMsg,
        onConfirmAction: runToggle,
      });
    } else {
      void runToggle();
    }
  };

  const handleDeleteRoute = (route: ApprovalRouteItem) => {
    setModal({
      variant: 'confirm',
      title: c.deleteTitle,
      message: c.deleteConfirmMsg,
      onConfirmAction: async () => {
        setSubmitting(true);
        try {
          await deleteApprovalRoute(route.id, lang);
          setModal({ variant: 'success', title: c.deleteSuccessTitle, message: c.deleteSuccessMsg });
          await loadRoutes();
        } catch (err) {
          handleApiError(err, c.deleteTitle);
        } finally {
          setSubmitting(false);
        }
      },
    });
  };

  const columns: DataTableColumn<ApprovalRouteItem>[] = [
    {
      key: 'name',
      header: c.colName,
      render: (row) => <strong>{row.name}</strong>,
    },
    {
      key: 'stages',
      header: c.colStages,
      render: (row) =>
        row.stages.length === 0 ? (
          <StatusBadge variant="danger">{c.noStagesWarning}</StatusBadge>
        ) : (
          <div className="workflow">
            {row.stages.map((s, idx) => (
              <span key={s.id} style={{ display: 'inline-flex', alignItems: 'center' }}>
                <span className="step">
                  {`${s.position}. ${s.stageName} (${s.responsibleRoleName})`}
                </span>
                {idx < row.stages.length - 1 && <span className="arrow">‹</span>}
              </span>
            ))}
          </div>
        ),
    },
    {
      key: 'status',
      header: c.colStatus,
      render: (row) => (
        <StatusBadge variant={row.isActive ? 'success' : 'danger'}>
          {row.isActive ? c.activeBadge : c.inactiveBadge}
        </StatusBadge>
      ),
    },
    {
      key: 'actions',
      header: c.colActions,
      render: (row) =>
        canEdit || canDelete ? (
          <div className="row-actions">
            {canEdit && (
              <>
                <RowActionButton
                  icon="edit"
                  label={c.editRouteBtn}
                  onClick={() => openEditRouteName(row)}
                />
                <RowActionButton
                  icon="layers"
                  label={c.manageStagesBtn}
                  onClick={() => openManageStages(row)}
                />
                <RowActionButton
                  icon="power"
                  label={row.isActive ? c.deactivateBtn : c.activateBtn}
                  variant={row.isActive ? 'warning' : 'success'}
                  onClick={() => handleToggleStatus(row)}
                />
              </>
            )}
            {canDelete && (
              <RowActionButton
                icon="trash"
                label={c.deleteBtn}
                variant="danger"
                onClick={() => handleDeleteRoute(row)}
              />
            )}
          </div>
        ) : null,
    },
  ];

  if (!canView) {
    return (
      <section className="surface-card">
        <ErrorState title={c.noViewTitle} message={c.noViewDesc} />
      </section>
    );
  }

  return (
    <>
      {mode !== 'list' ? (
        <StandaloneFormPage
          modeBadgeLabel={
            mode === 'create'
              ? c.createBadge
              : mode === 'edit'
                ? c.editBadge
                : c.stagesBadge
          }
          formTitle={
            mode === 'create'
              ? c.createTitle
              : mode === 'edit'
                ? c.editTitle
                : `${c.stagesTitle}: ${selectedRoute?.name ?? ''}`
          }
          formSubtitle={c.formSub}
          backButtonLabel={c.backBtn}
          submitButtonLabel={
            mode === 'create'
              ? c.saveCreateBtn
              : mode === 'edit'
                ? c.saveEditBtn
                : c.saveStagesBtn
          }
          cancelButtonLabel={c.cancelBtn}
          onBack={() => setMode('list')}
          onCancel={() => setMode('list')}
          onSubmit={() => void handleSubmit()}
        >
          {roleOptions.length === 0 && (
            <p className="form-error-text">{c.noRolesNotice}</p>
          )}

          {(mode === 'create' || mode === 'edit') && (
            <div style={{ maxWidth: 540 }}>
              <FormField
                id="approval-route-name"
                label={c.routeNameLabel}
                required
                error={routeNameError}
              >
                <TextInput
                  id="approval-route-name"
                  value={routeName}
                  hasError={Boolean(routeNameError)}
                  onChange={(e) => setRouteName(e.target.value)}
                />
              </FormField>
            </div>
          )}

          {(mode === 'create' || mode === 'stages') && (
            <div className="compact-builder-section" style={{ marginTop: 14 }}>
              <div className="compact-builder-head">
                <h3 className="card-title heading-font" style={{ fontSize: 14 }}>{c.stagesSectionTitle}</h3>
                <Button variant="outline" size="sm" onClick={addStageRow}>
                  + {c.addStageBtn}
                </Button>
              </div>

              {stagesError && (
                <p className="form-error-text" role="alert">
                  {stagesError}
                </p>
              )}

              <div className="compact-table-scroll">
                <div className="compact-stage-grid">
                  <div className="compact-stage-head">
                    <div>{c.stageOrderLabel}</div>
                    <div>{c.stageNameLabel}</div>
                    <div>{c.responsibleRoleLabel}</div>
                    <div>{c.colActions}</div>
                  </div>

                  {stageRows.map((row, idx) => (
                    <div key={row.key} className="compact-stage-item">
                      <span className="compact-order-badge">#{idx + 1}</span>

                      <TextInput
                        id={`stage-name-${idx}`}
                        value={row.stageName}
                        placeholder={c.stageNameLabel}
                        aria-label={c.stageNameLabel}
                        onChange={(e) => {
                          const value = e.target.value;
                          setStageRows((prev) =>
                            prev.map((item) =>
                              item.key === row.key ? { ...item, stageName: value } : item
                            )
                          );
                        }}
                      />

                      <SelectInput
                        id={`stage-role-${idx}`}
                        value={row.responsibleRoleId}
                        aria-label={c.responsibleRoleLabel}
                        onChange={(e) => {
                          const value = e.target.value;
                          setStageRows((prev) =>
                            prev.map((item) =>
                              item.key === row.key ? { ...item, responsibleRoleId: value } : item
                            )
                          );
                        }}
                        options={[
                          { value: '', label: c.selectRole },
                          ...roleOptions.map((r) => ({ value: r.id, label: r.name })),
                        ]}
                      />

                      <div className="compact-stage-actions">
                        <RowActionButton
                          icon="up"
                          label={c.moveUpBtn}
                          disabled={idx === 0}
                          onClick={() => moveStageRow(idx, -1)}
                        />
                        <RowActionButton
                          icon="down"
                          label={c.moveDownBtn}
                          disabled={idx === stageRows.length - 1}
                          onClick={() => moveStageRow(idx, 1)}
                        />
                        <RowActionButton
                          icon="trash"
                          label={c.removeStageBtn}
                          variant="danger"
                          disabled={stageRows.length <= 1}
                          onClick={() =>
                            setStageRows((prev) => prev.filter((r) => r.key !== row.key))
                          }
                        />
                      </div>
                    </div>
                  ))}
                </div>
              </div>
            </div>
          )}
        </StandaloneFormPage>
      ) : (
        <section className="surface-card panel clean-table-panel">
          {canCreate && (
            <div className="compact-panel-top-actions">
              <button
                type="button"
                className="compact-add-icon-btn"
                title={c.addBtn}
                aria-label={c.addBtn}
                disabled={submitting}
                onClick={openCreate}
              >
                <ReferenceIcon name="plus" size={18} />
              </button>
            </div>
          )}

          {errorMsg ? (
            <ErrorState
              title={commonStrings.tableErrorTitle}
              message={errorMsg}
              retryLabel={commonStrings.tableRetryBtn}
              onRetry={() => void loadRoutes()}
            />
          ) : (
            <>
              <DataTable<ApprovalRouteItem>
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
