import { PermissionsEditorPage } from '../permissions/PermissionsEditorPage';
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { ApiProblemError } from '../../api/authApi';
import {
  RoleDetails,
  createRole,
  deleteRole,
  listRoles,
  updateRole,
} from '../../api/rolesApi';
import { AppLanguage, UI_STRINGS } from '../../constants/uiBaseline';
import {
  CentralModalDialog,
  DataTable,
  DataTableColumn,
  ErrorState,
  FormField,
  PaginationBar,
  ReferenceIcon,
  RowActionButton,
  SearchFilterBar,
  StandaloneFormPage,
  StatusBadge,
  TextInput,
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
    sectionTitle: 'إدارة الأدوار',
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
    matrixBtn: 'تعديل الصلاحيات',
    editBtn: 'تعديل',
    deactivateBtn: 'تعطيل',
    activateBtn: 'تفعيل',
    deleteBtn: 'حذف',
    createBadge: 'إضافة دور',
    createTitle: 'إضافة دور جديد',
    createSubtitle: '',
    editBadge: 'تعديل دور',
    editTitle: 'تعديل بيانات الدور',
    editSubtitle: '',
    matrixBadge: 'مصفوفة صلاحيات الدور',
    matrixTitle: 'مصفوفة صلاحيات الدور',
    matrixSubtitle: '',
    backBtn: 'العودة',
    saveCreateBtn: 'حفظ',
    saveEditBtn: 'حفظ التعديلات',
    saveMatrixBtn: 'حفظ الصلاحيات',
    cancelBtn: 'إلغاء',
    roleNameLabel: 'اسم الدور',
    pinnedLabel: 'دور مثبت',
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
    sectionTitle: 'Roles',
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
    matrixBtn: 'Edit permissions',
    editBtn: 'Edit',
    deactivateBtn: 'Deactivate',
    activateBtn: 'Activate',
    deleteBtn: 'Delete',
    createBadge: 'Create Role',
    createTitle: 'Add Role',
    createSubtitle: '',
    editBadge: 'Edit Role',
    editTitle: 'Edit Role',
    editSubtitle: '',
    matrixBadge: 'Role Permissions Matrix',
    matrixTitle: 'Role Permissions Matrix',
    matrixSubtitle: '',
    backBtn: 'Back',
    saveCreateBtn: 'Save',
    saveEditBtn: 'Save Changes',
    saveMatrixBtn: 'Save Permissions',
    cancelBtn: 'Cancel',
    roleNameLabel: 'Role Name',
    pinnedLabel: 'Pinned Role',
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

  const openRoleMatrixPage = (role: RoleDetails) => {
    setEditingRole(role);
    setMode('matrix');
  };

  const columns: DataTableColumn<RoleDetails>[] = useMemo(
    () => [
      { key: 'name', header: copy.colName, render: (row) => <strong>{row.name}</strong> },
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
        render: (row) => (
          <div className="row-actions">
            {canViewMatrix && (
              <RowActionButton
                icon="shield"
                label={copy.matrixBtn}
                onClick={() => void openRoleMatrixPage(row)}
              />
            )}
            {canEdit && (
              <>
                <RowActionButton
                  icon="edit"
                  label={copy.editBtn}
                  onClick={() => {
                    setEditingRole(row);
                    setFormName(row.name);
                    setFormPinned(row.isPinned);
                    setFormActive(row.isActive);
                    setNameError('');
                    setMode('edit');
                  }}
                />
                <RowActionButton
                  icon="power"
                  label={row.isActive ? copy.deactivateBtn : copy.activateBtn}
                  variant={row.isActive ? 'warning' : 'success'}
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
              />
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
        <section className="surface-card panel clean-table-panel">
          <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 12, flexWrap: 'wrap', marginBottom: 12 }}>
            <div style={{ flex: 1, minWidth: 280 }}>
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
            </div>
            {canCreate && (
              <button
                type="button"
                className="compact-add-icon-btn"
                title={copy.addRoleBtn}
                aria-label={copy.addRoleBtn}
                onClick={() => {
                  setEditingRole(null);
                  setFormName('');
                  setFormPinned(false);
                  setFormActive(true);
                  setNameError('');
                  setMode('create');
                }}
              >
                <ReferenceIcon name="plus" size={18} />
              </button>
            )}
          </div>

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
        <PermissionsEditorPage kind="roles" subjectId={editingRole.id} subjectName={editingRole.name}
          lang={lang} actorPermissions={permissions} onBack={() => { setMode('list'); void loadDirectory(); }}
          onSessionExpired={onSessionExpired} onPermissionsChanged={onPermissionsChanged} />
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

