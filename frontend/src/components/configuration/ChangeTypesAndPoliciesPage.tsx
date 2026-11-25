import { useCallback, useEffect, useMemo, useState } from 'react';
import { ApiProblemError } from '../../api/authApi';
import {
  ApprovalRouteOption,
  ChangePolicyItem,
  ChangeTypeItem,
  createChangePolicy,
  createChangeType,
  listAssignableRoutesForChangeTypes,
  listChangePolicies,
  listChangeTypes,
  updateChangePolicy,
  updateChangeType,
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

interface ChangeTypesAndPoliciesPageProps {
  lang: AppLanguage;
  permissions: string[];
  onSessionExpired: () => void;
}

type ActiveTab = 'types' | 'policies';
type PageMode = 'list' | 'create' | 'edit';

interface ModalState {
  variant: 'success' | 'error' | 'confirm';
  title: string;
  message: string;
  onConfirmAction?: () => Promise<void>;
}

const COPY = {
  ar: {
    tabTypes: 'أنواع طلبات التغيير (UI-06)',
    tabPolicies: 'سياسات التغيير (M07)',
    typesTitle: 'أنواع طلبات التغيير وربطها بمسارات الاعتماد',
    typesDesc: 'تحديد كل نوع طلب تغيير وربطه بمسار اعتماد فعّال يمتلك مرحلة واحدة على الأقل.',
    policiesTitle: 'سياسات التغيير المسموحة لوثائق API',
    policiesDesc: 'تجميع أنواع طلبات التغيير في سياسات قابلة للاختيار عند إنشاء وثيقة API.',
    noViewTitle: 'صلاحية عرض إعدادات التغيير غير متوفرة',
    noViewDesc: 'يتطلب عرض أنواع وسياسات التغيير توفر صلاحية changes.config.view.',
    addTypeBtn: 'إضافة نوع تغيير',
    addPolicyBtn: 'إضافة سياسة تغيير',
    searchPlaceholder: 'ابحث بالاسم...',
    searchAria: 'البحث في إعدادات التغيير',
    filterAria: 'تصفية حسب الحالة',
    filterAll: 'كل الحالات',
    filterActive: 'الفعّالة فقط',
    filterInactive: 'المعطلة فقط',
    colName: 'الاسم',
    colRoute: 'مسار الاعتماد المرتبط',
    colPoliciesCount: 'السياسات',
    colIncludedTypes: 'أنواع التغيير المشمولة',
    colDocsCount: 'الوثائق',
    colStatus: 'الحالة',
    colActions: 'الإجراءات',
    activeBadge: 'فعّال',
    inactiveBadge: 'معطّل',
    editBtn: 'تعديل',
    deactivateBtn: 'تعطيل',
    activateBtn: 'تفعيل',
    createTypeBadge: 'إضافة نوع تغيير · UI-06 / M07',
    editTypeBadge: 'تعديل نوع تغيير · UI-06 / M07',
    createPolicyBadge: 'إضافة سياسة تغيير · UI-06 / M07',
    editPolicyBadge: 'تعديل سياسة تغيير · UI-06 / M07',
    createTypeTitle: 'إضافة نوع طلب تغيير جديد',
    editTypeTitle: 'تعديل نوع طلب التغيير',
    createPolicyTitle: 'إضافة سياسة تغيير جديدة',
    editPolicyTitle: 'تعديل سياسة التغيير',
    typeFormSub: 'يجب ربط نوع التغيير بمسار اعتماد نشط يحتوي على مرحلة واحدة على الأقل.',
    policyFormSub: 'اختر نوع تغيير واحدًا على الأقل لتضمينه في هذه السياسة.',
    backBtn: 'العودة إلى القائمة',
    saveCreateBtn: 'حفظ وإنشاء',
    saveEditBtn: 'حفظ التعديلات',
    cancelBtn: 'إلغاء',
    typeNameLabel: 'اسم نوع التغيير',
    routeLabel: 'مسار الاعتماد',
    selectRoute: '-- اختر مسار الاعتماد --',
    policyNameLabel: 'اسم سياسة التغيير',
    policyTypesLabel: 'أنواع طلبات التغيير المشمولة',
    noRoutesNotice: 'لا توجد مسارات اعتماد نشطة بمراحل؛ أنشئ مسار اعتماد أولًا من UI-05.',
    noTypesNotice: 'لا توجد أنواع تغيير نشطة بعد؛ أضف نوع تغيير أولًا.',
    validationName: 'الاسم مطلوب ويجب ألا يقل عن حرفين.',
    validationRoute: 'يجب اختيار مسار اعتماد صالح.',
    validationPolicyTypes: 'يجب اختيار نوع تغيير واحد على الأقل.',
    createdTitle: 'تم الحفظ بنجاح',
    createdMsg: 'تم إنشاء السجل بنجاح.',
    updatedTitle: 'تم تحديث السجل بنجاح',
    updatedMsg: 'تم حفظ التعديلات بنجاح.',
    confirmDeactivateTitle: 'تأكيد التعطيل',
    confirmDeactivateMsg: 'هل تريد تعطيل السجل المحدد؟ ستُحفظ المراجع النشطة ويُمنع التعطيل إذا وُجد ارتباط فعّال.',
    stagesBadge: (n: number) => `${n} مراحل`,
    loadingText: 'جارٍ تحميل البيانات...',
    pageWord: 'صفحة',
    ofWord: 'من',
  },
  en: {
    tabTypes: 'Change Request Types (UI-06)',
    tabPolicies: 'Change Policies (M07)',
    typesTitle: 'Change Request Types & Linked Approval Routes',
    typesDesc: 'Define change request types and bind each type to an active approval route with stages.',
    policiesTitle: 'Change Policies for API Documents',
    policiesDesc: 'Group allowed change request types into reusable change policies for API documents.',
    noViewTitle: 'Missing change configuration permission',
    noViewDesc: 'Viewing change types and policies requires changes.config.view permission.',
    addTypeBtn: 'Add Change Type',
    addPolicyBtn: 'Add Change Policy',
    searchPlaceholder: 'Search by name...',
    searchAria: 'Search change configuration',
    filterAria: 'Filter by status',
    filterAll: 'All statuses',
    filterActive: 'Active only',
    filterInactive: 'Inactive only',
    colName: 'Name',
    colRoute: 'Linked Approval Route',
    colPoliciesCount: 'Policies',
    colIncludedTypes: 'Included Change Types',
    colDocsCount: 'Linked Docs',
    colStatus: 'Status',
    colActions: 'Actions',
    activeBadge: 'Active',
    inactiveBadge: 'Inactive',
    editBtn: 'Edit',
    deactivateBtn: 'Deactivate',
    activateBtn: 'Activate',
    createTypeBadge: 'Create Change Type · UI-06 / M07',
    editTypeBadge: 'Edit Change Type · UI-06 / M07',
    createPolicyBadge: 'Create Change Policy · UI-06 / M07',
    editPolicyBadge: 'Edit Change Policy · UI-06 / M07',
    createTypeTitle: 'Create Change Request Type',
    editTypeTitle: 'Edit Change Request Type',
    createPolicyTitle: 'Create Change Policy',
    editPolicyTitle: 'Edit Change Policy',
    typeFormSub: 'Each change type must be bound to an active non-empty approval route.',
    policyFormSub: 'Select at least one active change type for this policy.',
    backBtn: 'Back to List',
    saveCreateBtn: 'Save & Create',
    saveEditBtn: 'Save Changes',
    cancelBtn: 'Cancel',
    typeNameLabel: 'Change Type Name',
    routeLabel: 'Approval Route',
    selectRoute: '-- Select Approval Route --',
    policyNameLabel: 'Change Policy Name',
    policyTypesLabel: 'Included Change Request Types',
    noRoutesNotice: 'No active approval routes with stages found; create one in UI-05 first.',
    noTypesNotice: 'No active change types available yet; create a change type first.',
    validationName: 'Name is required (at least 2 characters).',
    validationRoute: 'Please select a valid approval route.',
    validationPolicyTypes: 'Select at least one change request type.',
    createdTitle: 'Created Successfully',
    createdMsg: 'Record created successfully.',
    updatedTitle: 'Updated Successfully',
    updatedMsg: 'Changes saved successfully.',
    confirmDeactivateTitle: 'Confirm Deactivation',
    confirmDeactivateMsg: 'Deactivate this record? Active downstream references are protected.',
    stagesBadge: (n: number) => `${n} stages`,
    loadingText: 'Loading data...',
    pageWord: 'Page',
    ofWord: 'of',
  },
} as const;

export function ChangeTypesAndPoliciesPage({
  lang,
  permissions,
  onSessionExpired,
}: ChangeTypesAndPoliciesPageProps) {
  const c = COPY[lang];
  const commonStrings = UI_STRINGS[lang];
  const permSet = useMemo(() => new Set(permissions), [permissions]);
  const canView = permSet.has('changes.config.view');
  const canManage = permSet.has('changes.config.manage');

  const [tab, setTab] = useState<ActiveTab>('types');
  const [mode, setMode] = useState<PageMode>('list');
  const [searchQuery, setSearchQuery] = useState('');
  const [statusFilter, setStatusFilter] = useState('');
  const [page, setPage] = useState(1);

  const [changeTypes, setChangeTypes] = useState<ChangeTypeItem[]>([]);
  const [policies, setPolicies] = useState<ChangePolicyItem[]>([]);
  const [routeOptions, setRouteOptions] = useState<ApprovalRouteOption[]>([]);
  const [activeTypeOptions, setActiveTypeOptions] = useState<ChangeTypeItem[]>([]);

  const [totalCount, setTotalCount] = useState(0);
  const [totalPages, setTotalPages] = useState(1);
  const [loading, setLoading] = useState(false);
  const [errorMsg, setErrorMsg] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [modal, setModal] = useState<ModalState | null>(null);

  const [editingType, setEditingType] = useState<ChangeTypeItem | null>(null);
  const [editingPolicy, setEditingPolicy] = useState<ChangePolicyItem | null>(null);
  const [nameInput, setNameInput] = useState('');
  const [selectedRouteId, setSelectedRouteId] = useState('');
  const [selectedTypeIds, setSelectedTypeIds] = useState<Set<string>>(new Set());
  const [nameError, setNameError] = useState<string | undefined>();
  const [relationError, setRelationError] = useState<string | undefined>();

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

  const loadData = useCallback(async () => {
    if (!canView) return;
    setLoading(true);
    setErrorMsg(null);
    try {
      const isActive = statusFilter === 'active' ? true : statusFilter === 'inactive' ? false : null;
      if (tab === 'types') {
        const res = await listChangeTypes({ page, pageSize: 10, search: searchQuery, isActive }, lang);
        setChangeTypes(res.items);
        setTotalCount(res.totalCount);
        setTotalPages(Math.max(1, res.totalPages));
      } else {
        const res = await listChangePolicies({ page, pageSize: 10, search: searchQuery, isActive }, lang);
        setPolicies(res.items);
        setTotalCount(res.totalCount);
        setTotalPages(Math.max(1, res.totalPages));
      }
    } catch (err) {
      if (err instanceof ApiProblemError && err.status === 401) {
        onSessionExpired();
        return;
      }
      setErrorMsg(err instanceof Error ? err.message : 'Load error');
    } finally {
      setLoading(false);
    }
  }, [canView, lang, onSessionExpired, page, searchQuery, statusFilter, tab]);

  const loadLookups = useCallback(async () => {
    if (!canView) return;
    try {
      const [routes, activeTypes] = await Promise.all([
        listAssignableRoutesForChangeTypes(lang),
        listChangeTypes({ page: 1, pageSize: 100, isActive: true }, lang),
      ]);
      setRouteOptions(routes);
      setActiveTypeOptions(activeTypes.items);
    } catch {
      // Ignore secondary lookup error until form submission
    }
  }, [canView, lang]);

  useEffect(() => {
    void loadData();
  }, [loadData]);

  useEffect(() => {
    void loadLookups();
  }, [loadLookups]);

  const openCreate = () => {
    setEditingType(null);
    setEditingPolicy(null);
    setNameInput('');
    setSelectedRouteId(routeOptions[0]?.id ?? '');
    setSelectedTypeIds(new Set());
    setNameError(undefined);
    setRelationError(undefined);
    setMode('create');
  };

  const openEditType = (item: ChangeTypeItem) => {
    setEditingType(item);
    setEditingPolicy(null);
    setNameInput(item.name);
    setSelectedRouteId(item.routeId);
    setNameError(undefined);
    setRelationError(undefined);
    setMode('edit');
  };

  const openEditPolicy = (item: ChangePolicyItem) => {
    setEditingType(null);
    setEditingPolicy(item);
    setNameInput(item.name);
    setSelectedTypeIds(new Set(item.changeTypes.map((ct) => ct.changeTypeId)));
    setNameError(undefined);
    setRelationError(undefined);
    setMode('edit');
  };

  const togglePolicyType = (typeId: string) => {
    setSelectedTypeIds((prev) => {
      const next = new Set(prev);
      if (next.has(typeId)) next.delete(typeId);
      else next.add(typeId);
      return next;
    });
  };

  const handleSubmitForm = async () => {
    setNameError(undefined);
    setRelationError(undefined);

    const trimmedName = nameInput.trim();
    if (trimmedName.length < 2) {
      setNameError(c.validationName);
      return;
    }

    if (tab === 'types') {
      if (!selectedRouteId) {
        setRelationError(c.validationRoute);
        return;
      }

      setSubmitting(true);
      try {
        if (mode === 'create') {
          await createChangeType({ name: trimmedName, routeId: selectedRouteId }, lang);
          setModal({ variant: 'success', title: c.createdTitle, message: c.createdMsg });
        } else if (editingType) {
          await updateChangeType(
            editingType.id,
            { name: trimmedName, routeId: selectedRouteId },
            lang
          );
          setModal({ variant: 'success', title: c.updatedTitle, message: c.updatedMsg });
        }
        setMode('list');
        await Promise.all([loadData(), loadLookups()]);
      } catch (err) {
        handleApiError(err, mode === 'create' ? c.createTypeTitle : c.editTypeTitle);
      } finally {
        setSubmitting(false);
      }
      return;
    }

    const typeIds = Array.from(selectedTypeIds);
    if (typeIds.length === 0) {
      setRelationError(c.validationPolicyTypes);
      return;
    }

    setSubmitting(true);
    try {
      if (mode === 'create') {
        await createChangePolicy({ name: trimmedName, changeTypeIds: typeIds }, lang);
        setModal({ variant: 'success', title: c.createdTitle, message: c.createdMsg });
      } else if (editingPolicy) {
        await updateChangePolicy(
          editingPolicy.id,
          { name: trimmedName, changeTypeIds: typeIds },
          lang
        );
        setModal({ variant: 'success', title: c.updatedTitle, message: c.updatedMsg });
      }
      setMode('list');
      await loadData();
    } catch (err) {
      handleApiError(err, mode === 'create' ? c.createPolicyTitle : c.editPolicyTitle);
    } finally {
      setSubmitting(false);
    }
  };

  const handleToggleTypeStatus = (item: ChangeTypeItem) => {
    const run = async () => {
      setSubmitting(true);
      try {
        await updateChangeType(item.id, { isActive: !item.isActive }, lang);
        setModal({ variant: 'success', title: c.updatedTitle, message: c.updatedMsg });
        await Promise.all([loadData(), loadLookups()]);
      } catch (err) {
        handleApiError(err, c.editTypeTitle);
      } finally {
        setSubmitting(false);
      }
    };

    if (item.isActive) {
      setModal({
        variant: 'confirm',
        title: c.confirmDeactivateTitle,
        message: c.confirmDeactivateMsg,
        onConfirmAction: run,
      });
    } else {
      void run();
    }
  };

  const handleTogglePolicyStatus = (item: ChangePolicyItem) => {
    const run = async () => {
      setSubmitting(true);
      try {
        await updateChangePolicy(item.id, { isActive: !item.isActive }, lang);
        setModal({ variant: 'success', title: c.updatedTitle, message: c.updatedMsg });
        await loadData();
      } catch (err) {
        handleApiError(err, c.editPolicyTitle);
      } finally {
        setSubmitting(false);
      }
    };

    if (item.isActive) {
      setModal({
        variant: 'confirm',
        title: c.confirmDeactivateTitle,
        message: c.confirmDeactivateMsg,
        onConfirmAction: run,
      });
    } else {
      void run();
    }
  };

  if (!canView) {
    return (
      <section className="surface-card">
        <ErrorState title={c.noViewTitle} message={c.noViewDesc} />
      </section>
    );
  }

  const typeColumns: DataTableColumn<ChangeTypeItem>[] = [
    { key: 'name', header: c.colName, render: (row) => <strong>{row.name}</strong> },
    {
      key: 'route',
      header: c.colRoute,
      render: (row) => (
        <div className="badge-wrap-list">
          <StatusBadge variant={row.routeIsActive ? 'info' : 'warning'}>
            {`${row.routeName} (${c.stagesBadge(row.routeStageCount)})`}
          </StatusBadge>
        </div>
      ),
    },
    {
      key: 'policies',
      header: c.colPoliciesCount,
      render: (row) => <span className="ltr-code">{row.policiesCount}</span>,
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
              <Button variant="outline" size="sm" onClick={() => openEditType(row)}>
                {c.editBtn}
              </Button>
              <Button
                variant={row.isActive ? 'danger' : 'secondary'}
                size="sm"
                onClick={() => handleToggleTypeStatus(row)}
              >
                {row.isActive ? c.deactivateBtn : c.activateBtn}
              </Button>
            </>
          )}
        </div>
      ),
    },
  ];

  const policyColumns: DataTableColumn<ChangePolicyItem>[] = [
    { key: 'name', header: c.colName, render: (row) => <strong>{row.name}</strong> },
    {
      key: 'types',
      header: c.colIncludedTypes,
      render: (row) => (
        <div className="badge-wrap-list">
          {row.changeTypes.map((ct) => (
            <StatusBadge
              key={ct.changeTypeId}
              variant={ct.changeTypeIsActive ? 'info' : 'warning'}
            >
              {`${ct.changeTypeName} ← ${ct.routeName}`}
            </StatusBadge>
          ))}
        </div>
      ),
    },
    {
      key: 'docs',
      header: c.colDocsCount,
      render: (row) => <span className="ltr-code">{row.documentsCount}</span>,
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
              <Button variant="outline" size="sm" onClick={() => openEditPolicy(row)}>
                {c.editBtn}
              </Button>
              <Button
                variant={row.isActive ? 'danger' : 'secondary'}
                size="sm"
                onClick={() => handleTogglePolicyStatus(row)}
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
      {mode === 'list' ? (
        <section className="surface-card panel">
          <div className="tabs">
            <button
              type="button"
              className={`tab ${tab === 'types' ? 'active' : ''}`}
              onClick={() => {
                setTab('types');
                setPage(1);
                setSearchQuery('');
              }}
            >
              {c.tabTypes}
            </button>
            <button
              type="button"
              className={`tab ${tab === 'policies' ? 'active' : ''}`}
              onClick={() => {
                setTab('policies');
                setPage(1);
                setSearchQuery('');
              }}
            >
              {c.tabPolicies}
            </button>
          </div>

          <div className="card-header-row">
            <div>
              <h2 className="card-title heading-font">
                {tab === 'types' ? c.typesTitle : c.policiesTitle}
              </h2>
              <p className="card-subtitle">
                {tab === 'types' ? c.typesDesc : c.policiesDesc}
              </p>
            </div>
            {canManage && (
              <Button variant="primary" disabled={submitting} onClick={openCreate}>
                {tab === 'types' ? c.addTypeBtn : c.addPolicyBtn}
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
              onRetry={() => void loadData()}
            />
          ) : (
            <>
              {tab === 'types' ? (
                <DataTable<ChangeTypeItem>
                  columns={typeColumns}
                  rows={changeTypes}
                  getRowKey={(row) => row.id}
                  isLoading={loading}
                  loadingTitle={c.loadingText}
                  emptyTitle={commonStrings.tableEmptyTitle}
                  emptyDescription={commonStrings.tableEmptyDesc}
                />
              ) : (
                <DataTable<ChangePolicyItem>
                  columns={policyColumns}
                  rows={policies}
                  getRowKey={(row) => row.id}
                  isLoading={loading}
                  loadingTitle={c.loadingText}
                  emptyTitle={commonStrings.tableEmptyTitle}
                  emptyDescription={commonStrings.tableEmptyDesc}
                />
              )}

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
      ) : (
        <StandaloneFormPage
          modeBadgeLabel={
            tab === 'types'
              ? mode === 'create'
                ? c.createTypeBadge
                : c.editTypeBadge
              : mode === 'create'
                ? c.createPolicyBadge
                : c.editPolicyBadge
          }
          formTitle={
            tab === 'types'
              ? mode === 'create'
                ? c.createTypeTitle
                : c.editTypeTitle
              : mode === 'create'
                ? c.createPolicyTitle
                : c.editPolicyTitle
          }
          formSubtitle={tab === 'types' ? c.typeFormSub : c.policyFormSub}
          backButtonLabel={c.backBtn}
          submitButtonLabel={mode === 'create' ? c.saveCreateBtn : c.saveEditBtn}
          cancelButtonLabel={c.cancelBtn}
          onBack={() => setMode('list')}
          onCancel={() => setMode('list')}
          onSubmit={() => void handleSubmitForm()}
        >
          <FormField
            id="change-config-name"
            label={tab === 'types' ? c.typeNameLabel : c.policyNameLabel}
            required
            error={nameError}
          >
            <TextInput
              id="change-config-name"
              value={nameInput}
              hasError={Boolean(nameError)}
              onChange={(e) => setNameInput(e.target.value)}
            />
          </FormField>

          {tab === 'types' ? (
            <>
              {routeOptions.length === 0 && (
                <p className="form-error-text">{c.noRoutesNotice}</p>
              )}
              <FormField
                id="change-type-route"
                label={c.routeLabel}
                required
                error={relationError}
              >
                <SelectInput
                  id="change-type-route"
                  value={selectedRouteId}
                  hasError={Boolean(relationError)}
                  onChange={(e) => setSelectedRouteId(e.target.value)}
                  options={[
                    { value: '', label: c.selectRoute },
                    ...routeOptions.map((r) => ({
                      value: r.id,
                      label: `${r.name} (${c.stagesBadge(r.stageCount)})`,
                    })),
                  ]}
                />
              </FormField>
            </>
          ) : (
            <div style={{ marginTop: 14 }}>
              <FormField
                id="change-policy-types"
                label={c.policyTypesLabel}
                required
                error={relationError}
              >
                {activeTypeOptions.length === 0 ? (
                  <p className="form-error-text">{c.noTypesNotice}</p>
                ) : (
                  <div className="badge-wrap-list" style={{ gap: 10 }}>
                    {activeTypeOptions.map((ct) => (
                      <label
                        key={ct.id}
                        className="checkbox-inline"
                        style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}
                      >
                        <input
                          type="checkbox"
                          checked={selectedTypeIds.has(ct.id)}
                          onChange={() => togglePolicyType(ct.id)}
                        />
                        <span>
                          {ct.name} ({ct.routeName})
                        </span>
                      </label>
                    ))}
                  </div>
                )}
              </FormField>
            </div>
          )}
        </StandaloneFormPage>
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
