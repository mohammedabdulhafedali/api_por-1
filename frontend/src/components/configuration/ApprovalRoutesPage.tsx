import { useCallback, useEffect, useMemo, useState } from 'react';
import { ApiProblemError } from '../../api/authApi';
import {
  ApprovalRouteItem,
  ApprovalRouteRoleOption,
  ApprovalRouteStageInput,
  createApprovalRoute,
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
  SearchFilterBar,
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
    pageTitle: 'مسارات الاعتماد ومراحلها (UI-05)',
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
    manageStagesBtn: 'إدارة المراحل (UI-05A)',
    deactivateBtn: 'تعطيل',
    activateBtn: 'تفعيل',
    createBadge: 'إضافة مسار اعتماد · UI-05 / M06',
    editBadge: 'تعديل مسار اعتماد · UI-05 / M06',
    stagesBadge: 'إدارة مراحل المسار · UI-05A / M06',
    createTitle: 'إضافة مسار اعتماد جديد ومراحله',
    editTitle: 'تعديل اسم مسار الاعتماد',
    stagesTitle: 'ترتيب وتحرير مراحل المسار',
    formSub: 'تحتوي كل مرحلة على الاسم والترتيب والدور المسؤول فقط؛ ويكفي اعتماد عضو نشط واحد من الدور لإكمال المرحلة.',
    backBtn: 'العودة إلى القائمة',
    saveCreateBtn: 'حفظ وإنشاء المسار',
    saveEditBtn: 'حفظ التعديلات',
    saveStagesBtn: 'حفظ ترتيب المراحل',
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
    noRolesNotice: 'لا توجد أدوار نشطة؛ عرّف الأدوار من UI-25 أولًا.',
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
    pageTitle: 'Approval Routes & Stages (UI-05)',
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
    manageStagesBtn: 'Manage Stages (UI-05A)',
    deactivateBtn: 'Deactivate',
    activateBtn: 'Activate',
    createBadge: 'Create Approval Route · UI-05 / M06',
    editBadge: 'Edit Approval Route · UI-05 / M06',
    stagesBadge: 'Manage Route Stages · UI-05A / M06',
    createTitle: 'Create Approval Route & Stages',
    editTitle: 'Edit Approval Route Name',
    stagesTitle: 'Order & Edit Route Stages',
    formSub: 'Each stage defines only Name, Order, and Responsible Role; one qualified member decision completes a stage.',
    backBtn: 'Back to List',
    saveCreateBtn: 'Save & Create Route',
    saveEditBtn: 'Save Changes',
    saveStagesBtn: 'Save Ordered Stages',
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
    noRolesNotice: 'No active roles available; create roles in UI-25 first.',
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
  const permSet = useMemo(() => new Set(permissions), [permissions]);
  const canView = permSet.has('routes.view');
  const canManage = permSet.has('routes.manage');

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

  if (!canView) {
    return (
      <section className="surface-card">
        <ErrorState title={c.noViewTitle} message={c.noViewDesc} />
      </section>
    );
  }

  const columns: DataTableColumn<ApprovalRouteItem>[] = [
    { key: 'name', header: c.colName, render: (row) => <strong>{row.name}</strong> },
    {
      key: 'stages',
      header: c.colStages,
      render: (row) =>
        row.stages.length === 0 ? (
          <StatusBadge variant="danger">{c.noStagesWarning}</StatusBadge>
        ) : (
          <div className="badge-wrap-list">
            {row.stages.map((s) => (
              <StatusBadge key={s.id} variant="info">
                {`${s.position}. ${s.stageName} (${s.responsibleRoleName})`}
              </StatusBadge>
            ))}
          </div>
        ),
    },
    {
      key: 'links',
      header: c.colLinks,
      render: (row) => (
        <span className="muted-cell-text">
          {c.linksSummary(row.changeTypesCount, row.documentsCount)}
        </span>
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
      render: (row) => (
        <div className="button-row">
          {canManage && (
            <>
              <Button variant="outline" size="sm" onClick={() => openEditRouteName(row)}>
                {c.editRouteBtn}
              </Button>
              <Button variant="secondary" size="sm" onClick={() => openManageStages(row)}>
                {c.manageStagesBtn}
              </Button>
              <Button
                variant={row.isActive ? 'danger' : 'secondary'}
                size="sm"
                onClick={() => handleToggleStatus(row)}
              >
                {row.isActive ? c.deactivateBtn : c.activateBtn}
              </Button>
            </>
          )}
        </div>
      ),
    },
  ];

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
          )}

          {(mode === 'create' || mode === 'stages') && (
            <div style={{ marginTop: 16 }}>
              <div className="card-header-row" style={{ marginBottom: 10 }}>
                <h3 className="card-title heading-font">{c.stagesSectionTitle}</h3>
                <Button variant="outline" size="sm" onClick={addStageRow}>
                  {c.addStageBtn}
                </Button>
              </div>

              {stagesError && (
                <p className="form-error-text" role="alert">
                  {stagesError}
                </p>
              )}

              <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
                {stageRows.map((row, idx) => (
                  <div key={row.key} className="surface-card" style={{ padding: 14 }}>
                    <div className="card-header-row" style={{ marginBottom: 10 }}>
                      <StatusBadge variant="info">{`${c.stageOrderLabel} #${idx + 1}`}</StatusBadge>
                      <div className="button-row">
                        <Button
                          variant="outline"
                          size="sm"
                          disabled={idx === 0}
                          onClick={() => moveStageRow(idx, -1)}
                        >
                          {c.moveUpBtn}
                        </Button>
                        <Button
                          variant="outline"
                          size="sm"
                          disabled={idx === stageRows.length - 1}
                          onClick={() => moveStageRow(idx, 1)}
                        >
                          {c.moveDownBtn}
                        </Button>
                        {stageRows.length > 1 && (
                          <Button
                            variant="danger"
                            size="sm"
                            onClick={() =>
                              setStageRows((prev) => prev.filter((r) => r.key !== row.key))
                            }
                          >
                            {c.removeStageBtn}
                          </Button>
                        )}
                      </div>
                    </div>

                    <div className="form-grid-2">
                      <FormField id={`stage-name-${idx}`} label={c.stageNameLabel} required>
                        <TextInput
                          id={`stage-name-${idx}`}
                          value={row.stageName}
                          onChange={(e) => {
                            const value = e.target.value;
                            setStageRows((prev) =>
                              prev.map((item) =>
                                item.key === row.key ? { ...item, stageName: value } : item
                              )
                            );
                          }}
                        />
                      </FormField>

                      <FormField
                        id={`stage-role-${idx}`}
                        label={c.responsibleRoleLabel}
                        required
                      >
                        <SelectInput
                          id={`stage-role-${idx}`}
                          value={row.responsibleRoleId}
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
                      </FormField>
                    </div>
                  </div>
                ))}
              </div>
            </div>
          )}
        </StandaloneFormPage>
      ) : (
        <section className="surface-card">
          <div className="card-header-row">
            <div>
              <h2 className="card-title heading-font">{c.pageTitle}</h2>
              <p className="card-subtitle">{c.pageDesc}</p>
            </div>
            {canManage && (
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
            filterValue={statusFilter}
            filterAriaLabel={c.filterAria}
            filterPlaceholder={c.filterAll}
            filterOptions={[
              { value: 'active', label: c.filterActive },
              { value: 'inactive', label: c.filterInactive },
            ]}
            onFilterChange={(val) => {
              setStatusFilter(val);
              setPage(1);
            }}
          />

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
