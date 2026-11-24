import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { ApiProblemError } from '../../api/authApi';
import {
  PermissionsCatalogResult,
  getPermissionsCatalog,
  getRolePermissions,
  replaceRolePermissions,
} from '../../api/permissionsApi';
import {
  RoleDetails,
  createRole,
  deleteRole,
  listRoles,
  updateRole,
} from '../../api/rolesApi';
import { AppLanguage, UI_STRINGS } from '../../constants/uiBaseline';
import {
  toggleMatrixAllSelection,
  toggleMatrixCellSelection,
  toggleMatrixColumnSelection,
  toggleMatrixGroupSelection,
  toggleMatrixRowSelection,
} from '../../utils/matrixSelection';
import {
  Button,
  CentralModalDialog,
  DataTable,
  DataTableColumn,
  ErrorState,
  FormField,
  MatrixColumnDef,
  MatrixRowDef,
  PaginationBar,
  SearchFilterBar,
  SelectionMatrix,
  StandaloneFormPage,
  StatusBadge,
  TextInput,
  makeMatrixCellKey,
} from '../ui';

type PageMode = 'list' | 'create' | 'edit' | 'matrix';

interface ModalState {
  variant: 'success' | 'error' | 'confirm';
  title: string;
  message: string;
  onConfirmAction?: () => Promise<void>;
}

export interface RolesManagementPageProps {
  lang: AppLanguage;
  permissions: string[];
  onSessionExpired: () => void;
  onPermissionsChanged?: () => void;
}

const COPY = {
  ar: {
    sectionTitle: 'إدارة الأدوار ومصفوفة الصلاحيات (UI-25 · I04)',
    sectionDesc:
      'إنشاء الأدوار وتعديلها وتثبيتها وتعطيلها، وضبط مصفوفة الصلاحيات الشاملة (كل الشاشات في صفوف وكل الإجراءات في أعمدة) دون منح أي امتيازات ثابتة باسم الدور.',
    noViewTitle: 'صلاحية عرض الأدوار غير متوفرة',
    noViewDesc: 'يتطلب عرض الأدوار توفر صلاحية roles.view على حسابك.',
    addRoleBtn: 'إضافة دور جديد',
    searchPlaceholder: 'ابحث باسم الدور...',
    searchAria: 'البحث في قائمة الأدوار',
    filterAria: 'تصفية الأدوار حسب الحالة',
    filterAll: 'كل الأدوار',
    filterActive: 'الأدوار الفعّالة',
    filterInactive: 'الأدوار المعطلة',
    colName: 'اسم الدور',
    colStatus: 'الحالة',
    colPinned: 'التثبيت',
    colUsersCount: 'المستخدمون المسندون',
    colPermsCount: 'الصلاحيات الممنوحة',
    colActions: 'الإجراءات',
    activeBadge: 'فعّال',
    inactiveBadge: 'معطّل',
    pinnedBadge: 'مثبّت',
    standardBadge: 'اعتيادي',
    matrixBtn: 'مصفوفة الصلاحيات',
    editBtn: 'تعديل',
    deactivateBtn: 'تعطيل',
    activateBtn: 'تفعيل',
    deleteBtn: 'حذف',
    createBadge: 'صفحة إضافة دور · I03',
    createTitle: 'إضافة دور تنظيمي جديد',
    createSubtitle:
      'يبدأ الدور الجديد بدون أي صلاحيات (0 صلاحية)، ولا يحمل اسم الدور أي امتياز ثابت تلقائيًا.',
    editBadge: 'صفحة تعديل دور · I03',
    editTitle: 'تعديل بيانات الدور',
    editSubtitle: 'تعديل الاسم أو حالة التثبيت أو التفعيل.',
    matrixBadge: 'مصفوفة صلاحيات الدور · UI-25 / I04',
    matrixTitle: 'مصفوفة الصلاحيات الكاملة للدور',
    matrixSubtitle:
      'تحديد أو إلغاء تحديد الخلايا والصفوف والأعمدة والأقسام أو الكل لحفظ منح الدور في قاعدة البيانات دون حذف أي صلاحية من قاموس النظام.',
    backBtn: 'العودة إلى قائمة الأدوار',
    saveCreateBtn: 'إنشاء الدور',
    saveEditBtn: 'حفظ التعديلات',
    saveMatrixBtn: 'حفظ مصفوفة الدور',
    cancelBtn: 'إلغاء',
    roleNameLabel: 'اسم الدور',
    pinnedLabel: 'دور مثبت (محمي من الحذف والتعطيل المباشر)',
    activeLabel: 'الدور فعّال (متاح للإسناد)',
    requiredNameError: 'يرجى إدخال اسم الدور (حرفان على الأقل).',
    validationTitle: 'تعذر حفظ الدور',
    validationMessage: 'يرجى مراجعة الحقول المطلوبة ثم إعادة المحاولة.',
    createSuccessTitle: 'تم إنشاء الدور بنجاح',
    createSuccessMsg: 'تم إنشاء الدور الجديد بدون صلاحيات افتراضية وتسجيله في سجل التدقيق.',
    editSuccessTitle: 'تم تحديث الدور بنجاح',
    editSuccessMsg: 'تم حفظ تعديلات الدور في قاعدة البيانات وتسجيلها في سجل التدقيق.',
    matrixConfirmTitle: 'تأكيد حفظ مصفوفة صلاحيات الدور',
    matrixConfirmMsg:
      'سيتم تحديث صلاحيات الدور في قاعدة البيانات وتسجيل العملية في سجل التدقيق (ولا يُحذف أي رمز من قاموس النظام). هل تؤكد الحفظ؟',
    matrixSuccessTitle: 'تم حفظ مصفوفة الدور بنجاح',
    matrixSuccessMsg: 'تم تحديث صلاحيات الدور في قاعدة البيانات وتسجيلها في سجل التدقيق.',
    matrixReadOnlyHint: 'وضع القراءة فقط: يلزم توفر permissions.manage لحفظ تعديلات المصفوفة.',
    matrixSelectedBadge: 'الصلاحيات المحددة للدور:',
    matrixLoadingText: 'جارٍ تحميل قاموس الصلاحيات ومصفوفة الدور...',
    deleteConfirmTitle: 'تأكيد حذف الدور غير المستخدم',
    deleteConfirmMsg: 'هل تؤكد حذف هذا الدور غير المرتبط بأي مستخدم أو صلاحية أو مسار؟',
    deleteSuccessTitle: 'تم حذف الدور',
    deleteSuccessMsg: 'تم حذف الدور غير المستخدم بنجاح دون المساس بقاموس الصلاحيات.',
    toggleConfirmTitle: 'تأكيد تغيير حالة الدور',
    deactivateConfirmMsg: 'سيؤدي تعطيل الدور إلى وقف صلاحياته فورًا لدى جميع حامليه. هل تؤكد؟',
    activateConfirmMsg: 'هل تريد إعادة تفعيل هذا الدور؟',
    loadingTitle: 'جارٍ تحميل الأدوار...',
    emptyTitle: 'لا توجد أدوار مطابقة',
    emptyDesc: 'عدّل معايير البحث أو أضف دورًا جديدًا.',
    errorTitle: 'تعذر تحميل الأدوار',
    retryBtn: 'إعادة المحاولة',
    pagePrefix: 'الصفحة',
    ofWord: 'من',
    prevBtn: 'السابق',
    nextBtn: 'التالي',
  },
  en: {
    sectionTitle: 'Roles & Permissions Matrix (UI-25 · I04)',
    sectionDesc:
      'Manage organizational roles and configure the full Screen × Action permission matrix without hardcoded role-name privileges.',
    noViewTitle: 'Role View Permission Unavailable',
    noViewDesc: 'Your account requires roles.view permission to view organizational roles.',
    addRoleBtn: 'Add New Role',
    searchPlaceholder: 'Search by role name...',
    searchAria: 'Search roles directory',
    filterAria: 'Filter roles by status',
    filterAll: 'All Roles',
    filterActive: 'Active Roles',
    filterInactive: 'Inactive Roles',
    colName: 'Role Name',
    colStatus: 'Status',
    colPinned: 'Pinning',
    colUsersCount: 'Assigned Users',
    colPermsCount: 'Granted Permissions',
    colActions: 'Actions',
    activeBadge: 'Active',
    inactiveBadge: 'Inactive',
    pinnedBadge: 'Pinned',
    standardBadge: 'Standard',
    matrixBtn: 'Permissions Matrix',
    editBtn: 'Edit',
    deactivateBtn: 'Deactivate',
    activateBtn: 'Activate',
    deleteBtn: 'Delete',
    createBadge: 'Standalone Create Role · I03',
    createTitle: 'Create Organizational Role',
    createSubtitle:
      'New roles start with 0 permissions; role names never confer hardcoded privileges.',
    editBadge: 'Standalone Edit Role · I03',
    editTitle: 'Edit Role',
    editSubtitle: 'Update role name, pinned protection, or active state.',
    matrixBadge: 'Role Permission Matrix · UI-25 / I04',
    matrixTitle: 'Full Role Permission Matrix',
    matrixSubtitle:
      'Select or clear individual cells, rows, columns, groups, or all permissions without deleting any code from the system dictionary.',
    backBtn: 'Back to Roles List',
    saveCreateBtn: 'Create Role',
    saveEditBtn: 'Save Changes',
    saveMatrixBtn: 'Save Role Permissions',
    cancelBtn: 'Cancel',
    roleNameLabel: 'Role Name',
    pinnedLabel: 'Pinned Role (protected against deletion and direct deactivation)',
    activeLabel: 'Role is Active (available for assignment)',
    requiredNameError: 'Role name is required (at least 2 characters).',
    validationTitle: 'Unable to Save Role',
    validationMessage: 'Please review the highlighted fields and try again.',
    createSuccessTitle: 'Role Created',
    createSuccessMsg: 'Role was created with 0 default permissions and logged in the audit trail.',
    editSuccessTitle: 'Role Updated',
    editSuccessMsg: 'Role updates were saved and recorded in the audit trail.',
    matrixConfirmTitle: 'Confirm Saving Role Permission Matrix',
    matrixConfirmMsg:
      'The selected permission grants for this role will be persisted in the database and logged in the audit trail. Continue?',
    matrixSuccessTitle: 'Role Permissions Saved',
    matrixSuccessMsg: 'Role permission matrix was saved and recorded in the audit trail.',
    matrixReadOnlyHint: 'Read-only view: permissions.manage is required to edit role grants.',
    matrixSelectedBadge: 'Selected role permissions:',
    matrixLoadingText: 'Loading system permission catalog and role grants...',
    deleteConfirmTitle: 'Confirm Deleting Unused Role',
    deleteConfirmMsg: 'Delete this unused role? The platform permission dictionary remains intact.',
    deleteSuccessTitle: 'Role Deleted',
    deleteSuccessMsg: 'The unused role was deleted cleanly.',
    toggleConfirmTitle: 'Confirm Role Status Change',
    deactivateConfirmMsg: 'Deactivating this role immediately removes its inherited permissions for holders. Continue?',
    activateConfirmMsg: 'Re-activate this role?',
    loadingTitle: 'Loading roles...',
    emptyTitle: 'No matching roles found',
    emptyDesc: 'Adjust your search or create a new role.',
    errorTitle: 'Failed to Load Roles',
    retryBtn: 'Retry',
    pagePrefix: 'Page',
    ofWord: 'of',
    prevBtn: 'Previous',
    nextBtn: 'Next',
  },
} as const;

export function RolesManagementPage({
  lang,
  permissions,
  onSessionExpired,
  onPermissionsChanged,
}: RolesManagementPageProps) {
  const copy = COPY[lang];
  const t = UI_STRINGS[lang];

  const canView = permissions.some((p) => p.toLowerCase() === 'roles.view');
  const canCreate = permissions.some((p) => p.toLowerCase() === 'roles.create');
  const canEdit = permissions.some((p) => p.toLowerCase() === 'roles.edit');
  const canDelete = permissions.some((p) => p.toLowerCase() === 'roles.delete');
  const canViewMatrix = permissions.some((p) => p.toLowerCase() === 'permissions.view');
  const canManageMatrix = permissions.some((p) => p.toLowerCase() === 'permissions.manage');

  const [mode, setMode] = useState<PageMode>('list');
  const [editingRole, setEditingRole] = useState<RoleDetails | null>(null);

  const [roles, setRoles] = useState<RoleDetails[]>([]);
  const [page, setPage] = useState(1);
  const [totalPages, setTotalPages] = useState(1);
  const [totalCount, setTotalCount] = useState(0);
  const [search, setSearch] = useState('');
  const [activeFilter, setActiveFilter] = useState<'' | 'true' | 'false'>('');
  const [loading, setLoading] = useState(false);
  const [loadError, setLoadError] = useState<{ message: string; traceId?: string } | null>(null);

  const [formName, setFormName] = useState('');
  const [formPinned, setFormPinned] = useState(false);
  const [formActive, setFormActive] = useState(true);
  const [nameError, setNameError] = useState('');
  const [modal, setModal] = useState<ModalState | null>(null);

  // Task I04: Role Permissions Matrix state (UI-25)
  const [matrixLoading, setMatrixLoading] = useState(false);
  const [matrixCatalog, setMatrixCatalog] = useState<PermissionsCatalogResult | null>(null);
  const [selectedCells, setSelectedCells] = useState<Set<string>>(() => new Set());

  const nameRef = useRef<HTMLInputElement | null>(null);

  const loadDirectory = useCallback(async () => {
    if (!canView) return;
    setLoading(true);
    setLoadError(null);
    try {
      const res = await listRoles({ page, pageSize: 10, search, isActive: activeFilter, lang });
      setRoles(res.items);
      setTotalPages(res.totalPages);
      setTotalCount(res.totalCount);
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
  }, [canView, page, search, activeFilter, lang, onSessionExpired, copy.errorTitle]);

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
      message: apiErr?.message ?? copy.validationMessage,
    });
  };

  const handleSave = async () => {
    if (formName.trim().length < 2) {
      setNameError(copy.requiredNameError);
      setModal({ variant: 'error', title: copy.validationTitle, message: copy.validationMessage });
      return;
    }

    try {
      if (mode === 'create') {
        await createRole({ name: formName, isPinned: formPinned }, lang);
        setMode('list');
        setModal({ variant: 'success', title: copy.createSuccessTitle, message: copy.createSuccessMsg });
      } else if (editingRole) {
        await updateRole(editingRole.id, { name: formName, isPinned: formPinned, isActive: formActive }, lang);
        setMode('list');
        setModal({ variant: 'success', title: copy.editSuccessTitle, message: copy.editSuccessMsg });
      }
      await loadDirectory();
      onPermissionsChanged?.();
    } catch (err) {
      handleApiError(err);
    }
  };

  const openRoleMatrixPage = async (role: RoleDetails) => {
    setEditingRole(role);
    setMode('matrix');
    setMatrixLoading(true);
    try {
      const [catalog, rolePerms] = await Promise.all([
        getPermissionsCatalog(lang),
        getRolePermissions(role.id, lang),
      ]);
      setMatrixCatalog(catalog);
      const grantedSet = new Set(rolePerms.permissionIds);
      const nextSelected = new Set<string>();
      for (const perm of catalog.permissions) {
        if (grantedSet.has(perm.id)) {
          nextSelected.add(makeMatrixCellKey(perm.screenId, perm.columnId));
        }
      }
      setSelectedCells(nextSelected);
    } catch (err) {
      setMode('list');
      handleApiError(err);
    } finally {
      setMatrixLoading(false);
    }
  };

  const matrixColumns: MatrixColumnDef[] = useMemo(() => {
    if (!matrixCatalog) return [];
    return matrixCatalog.columns.map((c) => ({
      id: c.id,
      label: lang === 'ar' ? c.labelAr : c.labelEn,
    }));
  }, [matrixCatalog, lang]);

  const matrixRows: MatrixRowDef[] = useMemo(() => {
    if (!matrixCatalog) return [];
    return matrixCatalog.screens.map((s) => ({
      id: s.id,
      groupId: s.groupId,
      groupLabel: lang === 'ar' ? s.groupLabelAr : s.groupLabelEn,
      title: lang === 'ar' ? s.titleAr : s.titleEn,
      technicalPath: s.technicalPath,
      applicableColumnIds: s.applicableColumnIds,
    }));
  }, [matrixCatalog, lang]);

  const permissionIdByCell = useMemo(() => {
    const map = new Map<string, string>();
    if (!matrixCatalog) return map;
    for (const perm of matrixCatalog.permissions) {
      // Preserve first/canonical permission mapping per (screenId, columnId)
      const key = makeMatrixCellKey(perm.screenId, perm.columnId);
      if (!map.has(key)) {
        map.set(key, perm.id);
      }
    }
    return map;
  }, [matrixCatalog]);

  const selectedPermissionIds = useMemo(() => {
    const ids: string[] = [];
    for (const cellKey of selectedCells) {
      const permId = permissionIdByCell.get(cellKey);
      if (permId) ids.push(permId);
    }
    return ids;
  }, [selectedCells, permissionIdByCell]);

  const handleSaveMatrix = async () => {
    if (!editingRole) return;
    try {
      await replaceRolePermissions(editingRole.id, selectedPermissionIds, lang);
      setMode('list');
      setModal({
        variant: 'success',
        title: copy.matrixSuccessTitle,
        message: copy.matrixSuccessMsg,
      });
      await loadDirectory();
      onPermissionsChanged?.();
    } catch (err) {
      handleApiError(err);
    }
  };

  const columns: DataTableColumn<RoleDetails>[] = useMemo(
    () => [
      { key: 'name', header: copy.colName, render: (row) => <strong>{row.name}</strong> },
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
        key: 'pinned',
        header: copy.colPinned,
        render: (row) => (
          <StatusBadge variant={row.isPinned ? 'info' : 'neutral'}>
            {row.isPinned ? copy.pinnedBadge : copy.standardBadge}
          </StatusBadge>
        ),
      },
      {
        key: 'users',
        header: copy.colUsersCount,
        render: (row) => <code className="ltr-code">{row.assignedUsersCount}</code>,
      },
      {
        key: 'perms',
        header: copy.colPermsCount,
        render: (row) => <code className="ltr-code">{row.grantedPermissionsCount}</code>,
      },
      {
        key: 'actions',
        header: copy.colActions,
        render: (row) => (
          <div className="button-row" style={{ gap: 6 }}>
            {canViewMatrix && (
              <Button variant="primary" size="sm" onClick={() => void openRoleMatrixPage(row)}>
                {copy.matrixBtn}
              </Button>
            )}
            {canEdit && (
              <>
                <Button
                  variant="outline"
                  size="sm"
                  onClick={() => {
                    setEditingRole(row);
                    setFormName(row.name);
                    setFormPinned(row.isPinned);
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
                  disabled={row.isPinned && row.isActive}
                  onClick={() =>
                    setModal({
                      variant: 'confirm',
                      title: copy.toggleConfirmTitle,
                      message: row.isActive ? copy.deactivateConfirmMsg : copy.activateConfirmMsg,
                      onConfirmAction: async () => {
                        try {
                          await updateRole(row.id, { isActive: !row.isActive }, lang);
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
              </>
            )}
            {canDelete && !row.isPinned && row.assignedUsersCount === 0 && (
              <Button
                variant="secondary"
                size="sm"
                onClick={() =>
                  setModal({
                    variant: 'confirm',
                    title: copy.deleteConfirmTitle,
                    message: copy.deleteConfirmMsg,
                    onConfirmAction: async () => {
                      try {
                        await deleteRole(row.id, lang);
                        setModal({ variant: 'success', title: copy.deleteSuccessTitle, message: copy.deleteSuccessMsg });
                        await loadDirectory();
                      } catch (err) {
                        handleApiError(err);
                      }
                    },
                  })
                }
              >
                {copy.deleteBtn}
              </Button>
            )}
          </div>
        ),
      },
    ],
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [copy, canEdit, canDelete, canViewMatrix, lang]
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
        <section className="surface-card" aria-labelledby="ui25-roles-heading">
          <div className="card-header-row">
            <div>
              <h2 id="ui25-roles-heading" className="card-title heading-font">
                {copy.sectionTitle}
              </h2>
              <p className="card-subtitle">{copy.sectionDesc}</p>
            </div>
            {canCreate && (
              <Button
                variant="primary"
                onClick={() => {
                  setEditingRole(null);
                  setFormName('');
                  setFormPinned(false);
                  setFormActive(true);
                  setNameError('');
                  setMode('create');
                }}
              >
                {copy.addRoleBtn}
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
              onRetry={() => {
                void loadDirectory();
              }}
            />
          ) : (
            <>
              <DataTable
                columns={columns}
                rows={roles}
                getRowKey={(r) => r.id}
                isLoading={loading}
                loadingTitle={copy.loadingTitle}
                emptyTitle={copy.emptyTitle}
                emptyDescription={copy.emptyDesc}
              />
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
      ) : mode === 'matrix' && editingRole ? (
        <StandaloneFormPage
          modeBadgeLabel={copy.matrixBadge}
          formTitle={`${copy.matrixTitle}: ${editingRole.name}`}
          formSubtitle={copy.matrixSubtitle}
          backButtonLabel={copy.backBtn}
          submitButtonLabel={canManageMatrix ? copy.saveMatrixBtn : copy.backBtn}
          cancelButtonLabel={copy.cancelBtn}
          onBack={() => setMode('list')}
          onCancel={() => setMode('list')}
          onSubmit={() => {
            if (!canManageMatrix) {
              setMode('list');
              return;
            }
            setModal({
              variant: 'confirm',
              title: copy.matrixConfirmTitle,
              message: copy.matrixConfirmMsg,
              onConfirmAction: handleSaveMatrix,
            });
          }}
        >
          {matrixLoading ? (
            <p>{copy.matrixLoadingText}</p>
          ) : (
            <div style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
              <div
                style={{
                  display: 'flex',
                  flexWrap: 'wrap',
                  alignItems: 'center',
                  justifyContent: 'space-between',
                  gap: 10,
                }}
              >
                <div style={{ display: 'flex', flexWrap: 'wrap', gap: 8, alignItems: 'center' }}>
                  <strong>{editingRole.name}</strong>
                  <StatusBadge variant={editingRole.isActive ? 'success' : 'danger'}>
                    {editingRole.isActive ? copy.activeBadge : copy.inactiveBadge}
                  </StatusBadge>
                  {editingRole.isPinned && (
                    <StatusBadge variant="info">{copy.pinnedBadge}</StatusBadge>
                  )}
                </div>
                <StatusBadge variant="info">
                  {copy.matrixSelectedBadge} {selectedPermissionIds.length} /{' '}
                  {matrixCatalog?.permissions.length ?? 0}
                </StatusBadge>
              </div>

              {!canManageMatrix && (
                <p style={{ fontSize: 13, color: 'var(--text-muted)', margin: 0 }}>
                  {copy.matrixReadOnlyHint}
                </p>
              )}

              <SelectionMatrix
                rowHeaderLabel={t.matrixRowHeader}
                selectAllLabel={t.matrixSelectAll}
                selectGroupLabel={t.matrixSelectGroup}
                selectRowAriaPrefix={t.matrixSelectRowPrefix}
                selectColAriaPrefix={t.matrixSelectColPrefix}
                columns={matrixColumns}
                rows={matrixRows}
                selectedCells={selectedCells}
                onToggleCell={(rowId, colId) => {
                  if (!canManageMatrix) return;
                  setSelectedCells((prev) => toggleMatrixCellSelection(prev, rowId, colId));
                }}
                onToggleRow={(rowId, checked) => {
                  if (!canManageMatrix) return;
                  setSelectedCells((prev) =>
                    toggleMatrixRowSelection(prev, matrixRows, matrixColumns, rowId, checked)
                  );
                }}
                onToggleColumn={(colId, checked) => {
                  if (!canManageMatrix) return;
                  setSelectedCells((prev) =>
                    toggleMatrixColumnSelection(prev, matrixRows, colId, checked)
                  );
                }}
                onToggleGroup={(groupId, checked) => {
                  if (!canManageMatrix) return;
                  setSelectedCells((prev) =>
                    toggleMatrixGroupSelection(prev, matrixRows, matrixColumns, groupId, checked)
                  );
                }}
                onToggleAll={(checked) => {
                  if (!canManageMatrix) return;
                  setSelectedCells(toggleMatrixAllSelection(matrixRows, matrixColumns, checked));
                }}
              />
            </div>
          )}
        </StandaloneFormPage>
      ) : (
        <StandaloneFormPage
          modeBadgeLabel={mode === 'create' ? copy.createBadge : copy.editBadge}
          formTitle={mode === 'create' ? copy.createTitle : copy.editTitle}
          formSubtitle={mode === 'create' ? copy.createSubtitle : copy.editSubtitle}
          backButtonLabel={copy.backBtn}
          submitButtonLabel={mode === 'create' ? copy.saveCreateBtn : copy.saveEditBtn}
          cancelButtonLabel={copy.cancelBtn}
          onBack={() => setMode('list')}
          onCancel={() => setMode('list')}
          onSubmit={() => {
            void handleSave();
          }}
        >
          <div className="form-grid-2">
            <FormField id="role-name-input" label={copy.roleNameLabel} required error={nameError}>
              <TextInput
                ref={nameRef}
                id="role-name-input"
                value={formName}
                hasError={Boolean(nameError)}
                onChange={(e) => {
                  setFormName(e.target.value);
                  setNameError('');
                }}
              />
            </FormField>

            <div style={{ display: 'flex', flexDirection: 'column', gap: 10, paddingTop: 20 }}>
              <label style={{ display: 'flex', alignItems: 'center', gap: 8, fontSize: 13.5 }}>
                <input
                  type="checkbox"
                  checked={formPinned}
                  onChange={(e) => setFormPinned(e.target.checked)}
                />
                <span>{copy.pinnedLabel}</span>
              </label>

              {mode === 'edit' && (
                <label style={{ display: 'flex', alignItems: 'center', gap: 8, fontSize: 13.5 }}>
                  <input
                    type="checkbox"
                    disabled={formPinned}
                    checked={formActive}
                    onChange={(e) => setFormActive(e.target.checked)}
                  />
                  <span>{copy.activeLabel}</span>
                </label>
              )}
            </div>
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

