import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { ApiProblemError } from '../../api/authApi';
import { RoleDetails, listRoles, replaceUserRoles } from '../../api/rolesApi';
import {
  UserSummary,
  createUserAccount,
  deleteUserAccount,
  listUsers,
  triggerUserActivationOrReset,
  updateUserAccount,
} from '../../api/usersApi';
import { AppLanguage, UI_STRINGS } from '../../constants/uiBaseline';
import { UserPermissionsMatrixPage } from './UserPermissionsMatrixPage';
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
  SearchFilterBar,
  StandaloneFormPage,
  StatusBadge,
  TextInput,
} from '../ui';

type PageMode = 'list' | 'create' | 'edit' | 'assign-roles' | 'permissions';

interface ModalState {
  variant: 'success' | 'error' | 'confirm';
  title: string;
  message: string;
  onConfirmAction?: () => Promise<void>;
}

export interface UsersManagementPageProps {
  lang: AppLanguage;
  permissions: string[];
  currentUserId: string;
  onSessionExpired: () => void;
  onPermissionsChanged?: () => void;
}

const COPY = {
  ar: {
    sectionTitle: 'إدارة المستخدمين',
    sectionDesc:
      'إنشاء حسابات المستخدمين وتحديث بياناتها والتحكم بالتفعيل والتعطيل وإصدار رموز التفعيل دون تخزين كلمات مرور مؤقتة مكشوفة.',
    noViewPermissionTitle: 'صلاحية العرض غير متوفرة',
    noViewPermissionDesc: 'يتطلب عرض قائمة المستخدمين توفر صلاحية users.view على حسابك.',
    addUserBtn: 'إضافة مستخدم جديد',
    searchPlaceholder: 'ابحث بالاسم أو البريد الإلكتروني...',
    searchAria: 'البحث في قائمة المستخدمين',
    filterAria: 'تصفية حسب حالة الحساب',
    filterAll: 'كل الحالات',
    filterActive: 'الحسابات المفعّلة (Active)',
    filterInactive: 'الحسابات المعطلة (Inactive)',
    colName: 'الاسم الكامل',
    colUsername: 'اسم المستخدم',
    colEmail: 'البريد الإلكتروني',
    colStatus: 'حالة الحساب',
    colActivation: 'تفعيل كلمة المرور',
    colRoles: 'الأدوار',
    colActions: 'الإجراءات',
    activeBadge: 'فعّال',
    inactiveBadge: 'معطّل',
    lockedBadge: 'مقفل مؤقتًا',
    activatedBadge: 'مفعّل بكلمة مرور',
    pendingActivationBadge: 'بانتظار التفعيل الأول',
    noRolesLabel: 'بلا أدوار مسندة',
    editBtn: 'تعديل',
    assignRolesBtn: 'الأدوار',
    permissionsBtn: 'تعديل الصلاحيات',
    deactivateBtn: 'تعطيل',
    activateBtn: 'تفعيل',
    deleteBtn: 'حذف',
    deleteConfirmTitle: 'تأكيد حذف المستخدم',
    deleteConfirmMsg: 'هل أنت متأكد من حذف حساب المستخدم؟ سيُمنع الحذف إذا كان مرتبطًا بوثائق أو معاملات قائمة.',
    deleteSuccessTitle: 'تم حذف المستخدم',
    deleteSuccessMsg: 'تم حذف حساب المستخدم بنجاح.',
    sendResetBtn: 'استعادة',
    createBadge: 'إضافة مستخدم',
    createTitle: 'إضافة مستخدم جديد',
    createSubtitle: '',
    editBadge: 'تعديل مستخدم',
    editTitle: 'تعديل بيانات المستخدم',
    editSubtitle: '',
    assignRolesBadge: 'إسناد الأدوار',
    assignRolesTitle: 'إسناد الأدوار للمستخدم',
    assignRolesSubtitle: '',
    backBtn: 'العودة',
    saveCreateBtn: 'حفظ',
    saveEditBtn: 'حفظ التعديلات',
    saveRolesBtn: 'حفظ الأدوار',
    cancelBtn: 'إلغاء',
    fullNameLabel: 'الاسم الكامل',
    usernameLabel: 'اسم المستخدم',
    emailLabel: 'البريد الإلكتروني',
    consumerIdLabel: 'الجهة المستهلكة (اختياري)',
    consumerIdHint: '',
    triggerActivationLabel: 'إصدار رمز تفعيل عند إنشاء الحساب',
    activeStatusLabel: 'الحساب فعّال (مسموح له بتسجيل الدخول)',
    selectRolesLabel: 'الأدوار النشطة المسندة (إسناد متعدد)',
    targetUserAccountLabel: 'حساب المستخدم المستهدف',
    assignRolesLoadingLabel: 'جارٍ تحميل الأدوار النشطة...',
    assignRolesEmptyLabel: 'لا توجد أدوار نشطة متاحة حاليًا.',
    assignRolesPermsHint: 'صلاحيات ممنوحة',
    assignRolesSuccessTitle: 'تم تحديث الأدوار المسندة',
    assignRolesSuccessMsg: 'تم استبدال الأدوار المسندة للمستخدم ذريًا وتسجيل التغيير في سجل التدقيق.',
    requiredNameError: 'يرجى إدخال الاسم الكامل (حرفان على الأقل).',
    requiredUsernameError: 'يرجى إدخال اسم المستخدم (حرفان على الأقل).',
    requiredEmailError: 'يرجى إدخال البريد الإلكتروني.',
    validationTitle: 'تعذر حفظ بيانات المستخدم',
    validationMessage: 'يرجى مراجعة الحقول المطلوبة ثم إعادة المحاولة.',
    createSuccessTitle: 'تم إنشاء حساب المستخدم',
    createSuccessMsg: 'تم حفظ حساب المستخدم الجديد وتسجيل العملية في سجل التدقيق الإلحاقي.',
    editSuccessTitle: 'تم تحديث بيانات المستخدم',
    editSuccessMsg: 'تم حفظ التعديلات بنجاح وتحديث سجل التدقيق.',
    deactivateConfirmTitle: 'تأكيد تعطيل الحساب',
    deactivateConfirmMsg:
      'سيؤدي تعطيل هذا الحساب إلى إبطال جميع جلساته النشطة فورًا ومنعه من الدخول. هل تؤكد المتابعة؟',
    activateConfirmTitle: 'تأكيد إعادة تفعيل الحساب',
    activateConfirmMsg: 'هل تريد إعادة تفعيل هذا الحساب والسماح له بتسجيل الدخول؟',
    resetConfirmTitle: 'بدء مسار التفعيل / إعادة التعيين',
    resetConfirmMsg:
      'سيتم توليد رمز أحادي الاستخدام وإرساله عبر قناة التسليم المعتمدة (دون كشفه في المتصفح). هل تؤكد؟',
    resetSuccessTitle: 'تم بدء مسار التسليم الآمن',
    resetSuccessMsg: 'تم توليد الرمز أحادي الاستخدام وتمريره لقناة التسليم المعتمدة بنجاح.',
    loadingTitle: 'جارٍ تحميل قائمة المستخدمين...',
    emptyTitle: 'لا توجد حسابات مطابقة',
    emptyDesc: 'جرّب تغيير معايير البحث أو إضافة حساب مستخدم جديد.',
    errorTitle: 'تعذر تحميل المستخدمين',
    retryBtn: 'إعادة المحاولة',
    pagePrefix: 'الصفحة',
    ofWord: 'من',
    prevBtn: 'السابق',
    nextBtn: 'التالي',
  },
  en: {
    sectionTitle: 'Users',
    sectionDesc:
      'Create local accounts, edit profile & consumer scope, activate/deactivate accounts, and initiate single-use activation tokens securely.',
    noViewPermissionTitle: 'View Permission Unavailable',
    noViewPermissionDesc: 'Your account requires the users.view permission to view the user directory.',
    addUserBtn: 'Add New User',
    searchPlaceholder: 'Search by full name or email...',
    searchAria: 'Search users directory',
    filterAria: 'Filter by account status',
    filterAll: 'All Statuses',
    filterActive: 'Active Accounts',
    filterInactive: 'Inactive Accounts',
    colName: 'Full Name',
    colUsername: 'Username',
    colEmail: 'Email',
    colStatus: 'Status',
    colActivation: 'Activation',
    colRoles: 'Roles',
    colActions: 'Actions',
    activeBadge: 'Active',
    inactiveBadge: 'Inactive',
    lockedBadge: 'Locked Out',
    activatedBadge: 'Password Activated',
    pendingActivationBadge: 'Pending Initial Activation',
    noRolesLabel: 'No roles assigned',
    editBtn: 'Edit',
    assignRolesBtn: 'Roles',
    permissionsBtn: 'Edit permissions',
    deactivateBtn: 'Deactivate',
    activateBtn: 'Activate',
    deleteBtn: 'Delete',
    deleteConfirmTitle: 'Confirm User Deletion',
    deleteConfirmMsg: 'Are you sure you want to delete this user account? Blocked if referenced by existing documents or records.',
    deleteSuccessTitle: 'User Deleted',
    deleteSuccessMsg: 'User account has been deleted.',
    sendResetBtn: 'Reset',
    createBadge: 'Create User',
    createTitle: 'Add New User',
    createSubtitle: '',
    editBadge: 'Edit User',
    editTitle: 'Edit User Account',
    editSubtitle: '',
    assignRolesBadge: 'Assign Roles',
    assignRolesTitle: 'Assign User Roles',
    assignRolesSubtitle: '',
    backBtn: 'Back',
    saveCreateBtn: 'Save',
    saveEditBtn: 'Save Changes',
    saveRolesBtn: 'Save Roles',
    cancelBtn: 'Cancel',
    fullNameLabel: 'Full Name',
    usernameLabel: 'Username',
    emailLabel: 'Email Address',
    consumerIdLabel: 'Consumer Entity ID (Optional)',
    consumerIdHint: '',
    triggerActivationLabel: 'Issue single-use activation token immediately via delivery channel',
    activeStatusLabel: 'Account is Active (permitted to sign in)',
    selectRolesLabel: 'Assigned Active Roles (Multi-select)',
    targetUserAccountLabel: 'Target User Account',
    assignRolesLoadingLabel: 'Loading active roles...',
    assignRolesEmptyLabel: 'No active roles are currently available.',
    assignRolesPermsHint: 'granted permissions',
    assignRolesSuccessTitle: 'User Roles Updated',
    assignRolesSuccessMsg: 'Assigned user roles were updated atomically and recorded in the audit trail.',
    requiredNameError: 'Full name is required (at least 2 characters).',
    requiredUsernameError: 'Username is required (at least 2 characters).',
    requiredEmailError: 'Email address is required.',
    validationTitle: 'Unable to Save User',
    validationMessage: 'Please review the highlighted fields and try again.',
    createSuccessTitle: 'User Account Created',
    createSuccessMsg: 'The user account was created and logged in the append-only audit trail.',
    editSuccessTitle: 'User Account Updated',
    editSuccessMsg: 'User updates were saved and recorded in the audit trail.',
    deactivateConfirmTitle: 'Confirm Account Deactivation',
    deactivateConfirmMsg:
      'Deactivating this account will immediately revoke all active sessions and block access. Continue?',
    activateConfirmTitle: 'Confirm Account Activation',
    activateConfirmMsg: 'Re-enable this user account so they may sign in?',
    resetConfirmTitle: 'Initiate Activation / Password Reset',
    resetConfirmMsg:
      'Generate a single-use token and dispatch via the configured delivery channel (no secrets returned to browser)?',
    resetSuccessTitle: 'Secure Token Delivery Initiated',
    resetSuccessMsg: 'A single-use activation/reset token was generated and handed to the delivery channel.',
    loadingTitle: 'Loading user accounts...',
    emptyTitle: 'No matching users found',
    emptyDesc: 'Adjust your search/filter or add a new user account.',
    errorTitle: 'Failed to Load Users',
    retryBtn: 'Retry',
    pagePrefix: 'Page',
    ofWord: 'of',
    prevBtn: 'Previous',
    nextBtn: 'Next',
  },
} as const;

export function UsersManagementPage({
  lang,
  permissions,
  currentUserId,
  onSessionExpired,
  onPermissionsChanged,
}: UsersManagementPageProps) {
  const copy = COPY[lang];
  const t = UI_STRINGS[lang];

  const canView = permissions.some((p) => p.toLowerCase() === 'users.view');
  const canCreate = permissions.some((p) => p.toLowerCase() === 'users.create');
  const canEdit = permissions.some((p) => p.toLowerCase() === 'users.edit');
  const canDelete = permissions.some((p) => p.toLowerCase() === 'users.delete');
  const canReset = permissions.some((p) => p.toLowerCase() === 'users.password.reset');
  const canAssignRoles = permissions.some((p) => p.toLowerCase() === 'users.assign_roles');
  const canViewPermissions = permissions.some((p) => p.toLowerCase() === 'permissions.view');
  const canManagePermissions = permissions.some((p) => p.toLowerCase() === 'permissions.manage');

  const [mode, setMode] = useState<PageMode>('list');
  const [editingUser, setEditingUser] = useState<UserSummary | null>(null);

  const [users, setUsers] = useState<UserSummary[]>([]);
  const [page, setPage] = useState(1);
  const [totalPages, setTotalPages] = useState(1);
  const [totalCount, setTotalCount] = useState(0);
  const [search, setSearch] = useState('');
  const [activeFilter, setActiveFilter] = useState<'' | 'true' | 'false'>('');
  const [loading, setLoading] = useState(false);
  const [loadError, setLoadError] = useState<{ message: string; traceId?: string } | null>(null);

  // Standalone create / edit / multi-role assignment form state
  const [formFullName, setFormFullName] = useState('');
  const [formUsername, setFormUsername] = useState('');
  const [formEmail, setFormEmail] = useState('');
  const [formInitialPassword, setFormInitialPassword] = useState('');
  const [formConsumerEntityId, setFormConsumerEntityId] = useState('');
  const [formIsActive, setFormIsActive] = useState(true);
  const [formTriggerActivation, setFormTriggerActivation] = useState(false);
  const [availableRoles, setAvailableRoles] = useState<RoleDetails[]>([]);
  const [rolesLoading, setRolesLoading] = useState(false);
  const [rolesLoadError, setRolesLoadError] = useState<string | null>(null);
  const [selectedRoleIds, setSelectedRoleIds] = useState<string[]>([]);
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
  const [modal, setModal] = useState<ModalState | null>(null);

  const nameInputRef = useRef<HTMLInputElement | null>(null);
  const usernameInputRef = useRef<HTMLInputElement | null>(null);
  const emailInputRef = useRef<HTMLInputElement | null>(null);

  const loadAvailableRoles = useCallback(async () => {
    setRolesLoading(true);
    setRolesLoadError(null);
    try {
      const result = await listRoles({
        page: 1,
        pageSize: 100,
        isActive: 'true',
        lang,
      });
      setAvailableRoles(result.items);
    } catch (err) {
      if (err instanceof ApiProblemError && err.status === 401) {
        onSessionExpired();
        return;
      }
      setRolesLoadError(err instanceof Error ? err.message : copy.errorTitle);
    } finally {
      setRolesLoading(false);
    }
  }, [lang, onSessionExpired, copy.errorTitle]);

  const loadDirectory = useCallback(async () => {
    if (!canView) return;
    setLoading(true);
    setLoadError(null);
    try {
      const result = await listUsers({
        page,
        pageSize: 10,
        search,
        isActive: activeFilter,
        lang,
      });
      setUsers(result.items);
      setTotalPages(result.totalPages);
      setTotalCount(result.totalCount);
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
  }, [canView, page, search, activeFilter, lang, onSessionExpired, copy.errorTitle]);

  useEffect(() => {
    if (mode === 'list' && canView) {
      void loadDirectory();
    }
  }, [mode, canView, loadDirectory]);

  const openCreatePage = () => {
    setEditingUser(null);
    setFormFullName('');
    setFormUsername('');
    setFormEmail('');
    setFormConsumerEntityId('');
    setFormIsActive(true);
    setFormTriggerActivation(canReset);
    setSelectedRoleIds([]);
    setRolesLoadError(null);
    setFieldErrors({});
    setMode('create');
    if (canAssignRoles) {
      void loadAvailableRoles();
    }
  };

  const openEditPage = (user: UserSummary) => {
    setEditingUser(user);
    setFormFullName(user.fullName);
    setFormUsername(user.username ?? '');
    setFormEmail(user.email);
    setFormConsumerEntityId(user.consumerEntityId ?? '');
    setFormIsActive(user.isActive);
    setFieldErrors({});
    setMode('edit');
  };

  const openAssignRolesPage = (user: UserSummary) => {
    setEditingUser(user);
    setSelectedRoleIds(user.roles.map((r) => r.id));
    setRolesLoadError(null);
    setFieldErrors({});
    setMode('assign-roles');
    void loadAvailableRoles();
  };

  const toggleRoleSelection = (roleId: string) => {
    setSelectedRoleIds((prev) =>
      prev.includes(roleId) ? prev.filter((id) => id !== roleId) : [...prev, roleId]
    );
  };

  const handleSaveCreate = async () => {
    const errs: Record<string, string> = {};
    if (formFullName.trim().length < 2) errs.fullName = copy.requiredNameError;
    if (formUsername.trim().length < 2) errs.username = copy.requiredUsernameError;
    if (!formEmail.trim()) errs.email = copy.requiredEmailError;

    if (Object.keys(errs).length > 0) {
      setFieldErrors(errs);
      setModal({
        variant: 'error',
        title: copy.validationTitle,
        message: copy.validationMessage,
      });
      return;
    }

    try {
      await createUserAccount(
        {
          fullName: formFullName,
          username: formUsername.trim(),
          email: formEmail,
          consumerEntityId: formConsumerEntityId.trim() || null,
          triggerActivationToken: formTriggerActivation && canReset,
          initialPassword: formInitialPassword ? formInitialPassword : undefined,
          roleIds: canAssignRoles ? selectedRoleIds : [],
        },
        lang
      );
      setMode('list');
      setModal({
        variant: 'success',
        title: copy.createSuccessTitle,
        message: copy.createSuccessMsg,
      });
      await loadDirectory();
      onPermissionsChanged?.();
    } catch (err) {
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
    }
  };

  const handleSaveAssignRoles = async () => {
    if (!editingUser) return;
    try {
      await replaceUserRoles(editingUser.id, selectedRoleIds, lang);
      setMode('list');
      setModal({
        variant: 'success',
        title: copy.assignRolesSuccessTitle,
        message: copy.assignRolesSuccessMsg,
      });
      await loadDirectory();
      onPermissionsChanged?.();
    } catch (err) {
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
    }
  };

  const handleSaveEdit = async () => {
    if (!editingUser) return;
    const errs: Record<string, string> = {};
    if (formFullName.trim().length < 2) errs.fullName = copy.requiredNameError;
    if (formUsername.trim() && formUsername.trim().length < 2) {
      errs.username = copy.requiredUsernameError;
    }

    if (Object.keys(errs).length > 0) {
      setFieldErrors(errs);
      setModal({
        variant: 'error',
        title: copy.validationTitle,
        message: copy.validationMessage,
      });
      return;
    }

    try {
      await updateUserAccount(
        editingUser.id,
        {
          fullName: formFullName.trim(),
          username: formUsername.trim() || undefined,
          isActive: formIsActive,
          consumerEntityId: formConsumerEntityId.trim() || null,
          clearConsumerEntity: !formConsumerEntityId.trim() && Boolean(editingUser.consumerEntityId),
        },
        lang
      );
      setMode('list');
      setModal({
        variant: 'success',
        title: copy.editSuccessTitle,
        message: copy.editSuccessMsg,
      });
      await loadDirectory();
      onPermissionsChanged?.();
    } catch (err) {
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
    }
  };

  const handleToggleActive = (user: UserSummary) => {
    const nextState = !user.isActive;
    setModal({
      variant: 'confirm',
      title: nextState ? copy.activateConfirmTitle : copy.deactivateConfirmTitle,
      message: nextState ? copy.activateConfirmMsg : copy.deactivateConfirmMsg,
      onConfirmAction: async () => {
        try {
          await updateUserAccount(user.id, { isActive: nextState }, lang);
          setModal({
            variant: 'success',
            title: copy.editSuccessTitle,
            message: copy.editSuccessMsg,
          });
          await loadDirectory();
        } catch (err) {
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
        }
      },
    });
  };

  const handleTriggerReset = (user: UserSummary) => {
    setModal({
      variant: 'confirm',
      title: copy.resetConfirmTitle,
      message: copy.resetConfirmMsg,
      onConfirmAction: async () => {
        try {
          await triggerUserActivationOrReset(user.id, lang);
          setModal({
            variant: 'success',
            title: copy.resetSuccessTitle,
            message: copy.resetSuccessMsg,
          });
        } catch (err) {
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
        }
      },
    });
  };

  const handleDeleteUser = (user: UserSummary) => {
    setModal({
      variant: 'confirm',
      title: copy.deleteConfirmTitle,
      message: copy.deleteConfirmMsg,
      onConfirmAction: async () => {
        try {
          await deleteUserAccount(user.id, lang);
          setModal({
            variant: 'success',
            title: copy.deleteSuccessTitle,
            message: copy.deleteSuccessMsg,
          });
          await loadDirectory();
        } catch (err) {
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
        }
      },
    });
  };

  const columns: DataTableColumn<UserSummary>[] = useMemo(
    () => [
      {
        key: 'fullName',
        header: copy.colName,
        render: (row) => <strong>{row.fullName}</strong>,
      },
      {
        key: 'username',
        header: copy.colUsername,
        render: (row) => <code className="ltr-code">{row.username || '—'}</code>,
      },
      {
        key: 'email',
        header: copy.colEmail,
        render: (row) => <code className="ltr-code">{row.email}</code>,
      },
      {
        key: 'roles',
        header: copy.colRoles,
        render: (row) =>
          row.roles.length > 0 ? (
            <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap' }}>
              {row.roles.map((r) => (
                <StatusBadge key={r.id} variant="neutral">
                  {r.name}
                </StatusBadge>
              ))}
            </div>
          ) : (
            <span style={{ color: 'var(--text-muted)', fontSize: 12 }}>{copy.noRolesLabel}</span>
          ),
      },
      {
        key: 'status',
        header: copy.colStatus,
        render: (row) => (
          <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap' }}>
            <StatusBadge variant={row.isActive ? 'success' : 'danger'}>
              {row.isActive ? copy.activeBadge : copy.inactiveBadge}
            </StatusBadge>
            {row.isLockedOut && <StatusBadge variant="warning">{copy.lockedBadge}</StatusBadge>}
          </div>
        ),
      },
      {
        key: 'actions',
        header: copy.colActions,
        render: (row) => (
          <div className="row-actions">
            {canEdit && (
              <>
                <RowActionButton
                  icon="edit"
                  label={copy.editBtn}
                  onClick={() => openEditPage(row)}
                />
                <RowActionButton
                  icon="power"
                  label={row.isActive ? copy.deactivateBtn : copy.activateBtn}
                  variant={row.isActive ? 'warning' : 'success'}
                  disabled={row.id === currentUserId && row.isActive}
                  onClick={() => handleToggleActive(row)}
                />
              </>
            )}
            {canAssignRoles && (
              <RowActionButton
                icon="users"
                label={copy.assignRolesBtn}
                onClick={() => openAssignRolesPage(row)}
              />
            )}
            {canViewPermissions && (
              <RowActionButton
                icon="shield"
                label={copy.permissionsBtn}
                onClick={() => {
                  setEditingUser(row);
                  setMode('permissions');
                }}
              />
            )}
            {canDelete && row.id !== currentUserId && (
              <RowActionButton
                icon="trash"
                label={copy.deleteBtn}
                variant="danger"
                onClick={() => handleDeleteUser(row)}
              />
            )}
          </div>
        ),
      },
    ],
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [copy, canEdit, canDelete, canAssignRoles, canViewPermissions, canReset, currentUserId]
  );

  const handleCloseOrConfirmModal = () => {
    const active = modal;
    if (!active) return;
    if (active.variant === 'confirm' && active.onConfirmAction) {
      void active.onConfirmAction();
      return;
    }

    setModal(null);
    if (active.variant === 'error') {
      if (fieldErrors.fullName) nameInputRef.current?.focus();
      else if (fieldErrors.username) usernameInputRef.current?.focus();
      else if (fieldErrors.email) emailInputRef.current?.focus();
    }
  };

  if (!canView) {
    return (
      <section className="surface-card">
        <ErrorState
          title={copy.noViewPermissionTitle}
          message={copy.noViewPermissionDesc}
        />
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
                title={copy.addUserBtn}
                aria-label={copy.addUserBtn}
                onClick={openCreatePage}
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
                rows={users}
                getRowKey={(row) => row.id}
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
      ) : mode === 'permissions' && editingUser ? (
        <UserPermissionsMatrixPage
          userId={editingUser.id}
          lang={lang}
          canManage={canManagePermissions}
          actorPermissions={permissions}
          onBack={() => setMode('list')}
          onSessionExpired={onSessionExpired}
          onPermissionsChanged={onPermissionsChanged}
        />
      ) : (
        <StandaloneFormPage
          modeBadgeLabel={
            mode === 'create'
              ? copy.createBadge
              : mode === 'edit'
                ? copy.editBadge
                : copy.assignRolesBadge
          }
          formTitle={
            mode === 'create'
              ? copy.createTitle
              : mode === 'edit'
                ? copy.editTitle
                : copy.assignRolesTitle
          }
          formSubtitle={
            mode === 'create'
              ? copy.createSubtitle
              : mode === 'edit'
                ? copy.editSubtitle
                : copy.assignRolesSubtitle
          }
          backButtonLabel={copy.backBtn}
          submitButtonLabel={
            mode === 'create'
              ? copy.saveCreateBtn
              : mode === 'edit'
                ? copy.saveEditBtn
                : copy.saveRolesBtn
          }
          cancelButtonLabel={copy.cancelBtn}
          onBack={() => setMode('list')}
          onCancel={() => setMode('list')}
          onSubmit={() => {
            if (mode === 'create') void handleSaveCreate();
            else if (mode === 'edit') void handleSaveEdit();
            else void handleSaveAssignRoles();
          }}
        >
          {mode === 'assign-roles' ? (
            <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
              {editingUser && (
                <div
                  style={{
                    padding: 12,
                    borderRadius: 8,
                    border: '1px solid var(--border-color)',
                    background: 'var(--bg-subtle)',
                  }}
                >
                  <div style={{ fontSize: 12, color: 'var(--text-muted)', marginBottom: 4 }}>
                    {copy.targetUserAccountLabel}
                  </div>
                  <div style={{ fontWeight: 600 }}>{editingUser.fullName}</div>
                  <div className="ltr-code" style={{ fontSize: 12.5, color: 'var(--text-muted)' }}>
                    {editingUser.email}
                  </div>
                </div>
              )}

              <div>
                <div style={{ fontWeight: 600, fontSize: 13.5, marginBottom: 8 }}>
                  {copy.selectRolesLabel}
                </div>
                {rolesLoading ? (
                  <p style={{ fontSize: 13, color: 'var(--text-muted)' }}>{copy.assignRolesLoadingLabel}</p>
                ) : rolesLoadError ? (
                  <p role="alert" style={{ fontSize: 13, color: 'var(--status-danger)' }}>
                    {rolesLoadError}
                  </p>
                ) : availableRoles.length === 0 ? (
                  <p style={{ fontSize: 13, color: 'var(--text-muted)' }}>{copy.assignRolesEmptyLabel}</p>
                ) : (
                  <div className="form-grid-2">
                    {availableRoles.map((role) => (
                      <label
                        key={role.id}
                        htmlFor={`assign-role-${role.id}`}
                        style={{
                          display: 'flex',
                          alignItems: 'center',
                          justifyContent: 'space-between',
                          gap: 10,
                          padding: '10px 12px',
                          borderRadius: 8,
                          border: '1px solid var(--border-color)',
                          cursor: 'pointer',
                        }}
                      >
                        <span style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                          <input
                            id={`assign-role-${role.id}`}
                            type="checkbox"
                            checked={selectedRoleIds.includes(role.id)}
                            onChange={() => toggleRoleSelection(role.id)}
                          />
                          <span style={{ fontWeight: 500 }}>{role.name}</span>
                        </span>
                        <span style={{ fontSize: 12, color: 'var(--text-muted)' }}>
                          {role.grantedPermissionsCount} {copy.assignRolesPermsHint}
                        </span>
                      </label>
                    ))}
                  </div>
                )}
              </div>
            </div>
          ) : (
            <>
              <div className="form-grid-2">
                <FormField
                  id="user-fullname-input"
                  label={copy.fullNameLabel}
                  required
                  error={fieldErrors.fullName}
                >
                  <TextInput
                    ref={nameInputRef}
                    id="user-fullname-input"
                    value={formFullName}
                    hasError={Boolean(fieldErrors.fullName)}
                    onChange={(e) => {
                      setFormFullName(e.target.value);
                      setFieldErrors((prev) => ({ ...prev, fullName: '' }));
                    }}
                  />
                </FormField>

                <FormField
                  id="user-username-input"
                  label={copy.usernameLabel}
                  required={mode === 'create'}
                  error={fieldErrors.username}
                >
                  <TextInput
                    ref={usernameInputRef}
                    id="user-username-input"
                    ltrCode
                    value={formUsername}
                    hasError={Boolean(fieldErrors.username)}
                    onChange={(e) => {
                      setFormUsername(e.target.value);
                      setFieldErrors((prev) => ({ ...prev, username: '' }));
                    }}
                  />
                </FormField>

                {mode === 'create' && (
                  <FormField
                    id="user-initial-password-input"
                    label={lang === 'ar' ? 'كلمة المرور الأولية (اختياري للتفعيل الفوري)' : 'Initial Password (optional for immediate login)'}
                  >
                    <TextInput
                      id="user-initial-password-input"
                      type="password"
                      ltrCode
                      value={formInitialPassword}
                      onChange={(e) => setFormInitialPassword(e.target.value)}
                    />
                  </FormField>
                )}

                <FormField
                  id="user-email-input"
                  label={copy.emailLabel}
                  required={mode === 'create'}
                  error={fieldErrors.email}
                >
                  <TextInput
                    ref={emailInputRef}
                    id="user-email-input"
                    type="email"
                    ltrCode
                    disabled={mode === 'edit'}
                    value={formEmail}
                    hasError={Boolean(fieldErrors.email)}
                    onChange={(e) => {
                      setFormEmail(e.target.value);
                      setFieldErrors((prev) => ({ ...prev, email: '' }));
                    }}
                  />
                </FormField>

                <FormField
                  id="user-consumer-input"
                  label={copy.consumerIdLabel}
                >
                  <TextInput
                    id="user-consumer-input"
                    ltrCode
                    value={formConsumerEntityId}
                    onChange={(e) => setFormConsumerEntityId(e.target.value)}
                  />
                </FormField>

                {mode === 'create' && canReset && (
                  <div style={{ display: 'flex', alignItems: 'center', gap: 10, paddingTop: 22 }}>
                    <input
                      id="user-trigger-activation"
                      type="checkbox"
                      checked={formTriggerActivation}
                      onChange={(e) => setFormTriggerActivation(e.target.checked)}
                    />
                    <label htmlFor="user-trigger-activation" style={{ fontSize: 13.5 }}>
                      {copy.triggerActivationLabel}
                    </label>
                  </div>
                )}

                {mode === 'edit' && (
                  <div style={{ display: 'flex', alignItems: 'center', gap: 10, paddingTop: 22 }}>
                    <input
                      id="user-is-active"
                      type="checkbox"
                      disabled={editingUser?.id === currentUserId}
                      checked={formIsActive}
                      onChange={(e) => setFormIsActive(e.target.checked)}
                    />
                    <label htmlFor="user-is-active" style={{ fontSize: 13.5 }}>
                      {copy.activeStatusLabel}
                    </label>
                  </div>
                )}
              </div>

              {mode === 'create' && canAssignRoles && (
                <div style={{ marginTop: 18 }}>
                  <div style={{ fontWeight: 600, fontSize: 13.5, marginBottom: 8 }}>
                    {copy.selectRolesLabel}
                  </div>
                  {rolesLoading ? (
                    <p style={{ fontSize: 13, color: 'var(--text-muted)' }}>
                      {copy.assignRolesLoadingLabel}
                    </p>
                  ) : rolesLoadError ? (
                    <p role="alert" style={{ fontSize: 13, color: 'var(--status-danger)' }}>
                      {rolesLoadError}
                    </p>
                  ) : availableRoles.length === 0 ? (
                    <p style={{ fontSize: 13, color: 'var(--text-muted)' }}>
                      {copy.assignRolesEmptyLabel}
                    </p>
                  ) : (
                    <div className="form-grid-2">
                      {availableRoles.map((role) => (
                        <label
                          key={role.id}
                          htmlFor={`create-role-${role.id}`}
                          style={{
                            display: 'flex',
                            alignItems: 'center',
                            justifyContent: 'space-between',
                            gap: 10,
                            padding: '10px 12px',
                            borderRadius: 8,
                            border: '1px solid var(--border-color)',
                            cursor: 'pointer',
                          }}
                        >
                          <span style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                            <input
                              id={`create-role-${role.id}`}
                              type="checkbox"
                              checked={selectedRoleIds.includes(role.id)}
                              onChange={() => toggleRoleSelection(role.id)}
                            />
                            <span style={{ fontWeight: 500 }}>{role.name}</span>
                          </span>
                          <span style={{ fontSize: 12, color: 'var(--text-muted)' }}>
                            {role.grantedPermissionsCount} {copy.assignRolesPermsHint}
                          </span>
                        </label>
                      ))}
                    </div>
                  )}
                </div>
              )}
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
        onConfirm={handleCloseOrConfirmModal}
        onCancel={modal?.variant === 'confirm' ? () => setModal(null) : undefined}
      />
    </>
  );
}

