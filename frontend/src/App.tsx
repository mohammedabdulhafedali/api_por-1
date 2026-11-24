import { useEffect, useMemo, useRef, useState } from 'react';
import {
  AuthenticatedUser,
  fetchCurrentIdentity,
  logoutCurrentSession,
} from './api/authApi';
import { ChangePasswordView } from './components/auth/ChangePasswordView';
import { LoginPage } from './components/auth/LoginPage';
import { ApprovalRoutesPage } from './components/configuration/ApprovalRoutesPage';
import { ChangeTypesAndPoliciesPage } from './components/configuration/ChangeTypesAndPoliciesPage';
import { RequirementTemplatesPage } from './components/configuration/RequirementTemplatesPage';
import { ApiDocumentsPage } from './components/documents/ApiDocumentsPage';
import { IntegrationsPage } from './components/integrations/IntegrationsPage';
import { CompanySystemsPage } from './components/masterdata/CompanySystemsPage';
import { ConsumersManagementPage } from './components/masterdata/ConsumersManagementPage';
import { FileTypesManagementPage } from './components/masterdata/FileTypesManagementPage';
import { ProvidersManagementPage } from './components/masterdata/ProvidersManagementPage';
import { RolesManagementPage } from './components/roles/RolesManagementPage';
import { UsersManagementPage } from './components/users/UsersManagementPage';
import {
  AppLayoutShell,
  Button,
  CentralModalDialog,
  DataTable,
  DataTableColumn,
  ErrorState,
  FormField,
  LoadingState,
  MatrixColumnDef,
  MatrixRowDef,
  PaginationBar,
  SearchFilterBar,
  SelectInput,
  SelectionMatrix,
  StandaloneFormPage,
  StatusBadge,
  TextInput,
  TextareaInput,
  makeMatrixCellKey,
} from './components/ui';
import {
  toggleMatrixAllSelection,
  toggleMatrixCellSelection,
  toggleMatrixColumnSelection,
  toggleMatrixGroupSelection,
  toggleMatrixRowSelection,
} from './utils/matrixSelection';
import {
  AppLanguage,
  AppTheme,
  DialogVariant,
  NavItemSpec,
  SIDEBAR_GROUPS,
  STANDALONE_NAV_ITEMS,
  ShellWorkspaceMode,
  TablePreviewState,
  UI_STRINGS,
  UX_SPEC_CATALOG_ROWS,
  UiCatalogSpecRow,
} from './constants/uiBaseline';

const STORAGE_LANG_KEY = 'api_gov_ui_lang';
const STORAGE_THEME_KEY = 'api_gov_ui_theme';
const PAGE_SIZE = 5;

function getInitialLanguage(): AppLanguage {
  try {
    const saved = window.localStorage.getItem(STORAGE_LANG_KEY);
    if (saved === 'ar' || saved === 'en') {
      return saved;
    }
  } catch {
    // Fallback to Arabic default
  }
  return 'ar';
}

function getInitialTheme(): AppTheme {
  try {
    const saved = window.localStorage.getItem(STORAGE_THEME_KEY);
    if (saved === 'light' || saved === 'dark') {
      return saved;
    }
  } catch {
    // Fallback to light theme default
  }
  return 'light';
}

export interface AppProps {
  initialAuthenticatedUser?: AuthenticatedUser | null;
}

export function App({ initialAuthenticatedUser }: AppProps = {}) {
  const [lang, setLang] = useState<AppLanguage>(getInitialLanguage);
  const [theme, setTheme] = useState<AppTheme>(getInitialTheme);
  const [sessionUser, setSessionUser] = useState<AuthenticatedUser | null>(
    initialAuthenticatedUser ?? null
  );
  const [sessionLoading, setSessionLoading] = useState<boolean>(
    initialAuthenticatedUser === undefined
  );
  const [showChangePassword, setShowChangePassword] = useState<boolean>(false);

  // Single open collapsible section rule (PRD-06 & 02_UX_UI_v1.2.md)
  const [openGroupId, setOpenGroupId] = useState<string>(SIDEBAR_GROUPS[0].id);
  const [activeNavItem, setActiveNavItem] = useState<NavItemSpec>(STANDALONE_NAV_ITEMS[0]);
  const [mobileSidebarOpen, setMobileSidebarOpen] = useState<boolean>(false);

  // Standalone page mode ('catalog' | 'create' | 'edit') — Create/Edit are standalone pages, NOT modals (PRD-06)
  const [workspaceMode, setWorkspaceMode] = useState<ShellWorkspaceMode>('catalog');

  // Shared form controls state
  const [sampleText, setSampleText] = useState<string>('');
  const [sampleCode, setSampleCode] = useState<string>('/api/v1/api-documents');
  const [samplePattern, setSamplePattern] = useState<string>('list');
  const [sampleNotes, setSampleNotes] = useState<string>('');
  const [showSampleError, setShowSampleError] = useState<boolean>(false);

  // Shared DataTable search, filter, pagination & state views
  const [searchQuery, setSearchQuery] = useState<string>('');
  const [categoryFilter, setCategoryFilter] = useState<string>('');
  const [currentPage, setCurrentPage] = useState<number>(1);
  const [tableState, setTableState] = useState<TablePreviewState>('ready');

  // Shared SelectionMatrix state (pure UI interaction, no mock business rules)
  const matrixColumns: MatrixColumnDef[] = useMemo(
    () => [
      { id: 'view', label: lang === 'ar' ? 'عرض' : 'View' },
      { id: 'create', label: lang === 'ar' ? 'إضافة' : 'Create' },
      { id: 'edit', label: lang === 'ar' ? 'تعديل' : 'Edit' },
      { id: 'delete', label: lang === 'ar' ? 'حذف' : 'Delete' },
    ],
    [lang]
  );

  const matrixRows: MatrixRowDef[] = useMemo(
    () => [
      {
        id: 'UI-01',
        groupId: 'systems',
        groupLabel: lang === 'ar' ? 'الأنظمة والجهات' : 'Systems & Entities',
        title: lang === 'ar' ? 'أنظمة الشركة' : 'Company Systems',
        technicalPath: '/api/v1/company-systems',
        applicableColumnIds: ['view', 'create', 'edit'],
      },
      {
        id: 'UI-02',
        groupId: 'systems',
        groupLabel: lang === 'ar' ? 'الأنظمة والجهات' : 'Systems & Entities',
        title: lang === 'ar' ? 'الجهات المستهلكة' : 'Consumer Entities',
        technicalPath: '/api/v1/consumers',
        applicableColumnIds: ['view', 'create', 'edit'],
      },
      {
        id: 'UI-21',
        groupId: 'documents',
        groupLabel: lang === 'ar' ? 'وثائق API' : 'API Documents',
        title: lang === 'ar' ? 'وثائق API' : 'API Documents',
        technicalPath: '/api/v1/api-documents',
        applicableColumnIds: ['view', 'create', 'edit', 'delete'],
      },
    ],
    [lang]
  );

  const [selectedMatrixCells, setSelectedMatrixCells] = useState<Set<string>>(
    () => new Set([makeMatrixCellKey('UI-01', 'view'), makeMatrixCellKey('UI-21', 'view')])
  );

  // Unified central Modal dialog state (no bottom Toast per PRD-06)
  const [activeModal, setActiveModal] = useState<DialogVariant | null>(null);
  const sampleInputRef = useRef<HTMLInputElement | null>(null);

  const t = UI_STRINGS[lang];
  const dir = lang === 'ar' ? 'rtl' : 'ltr';

  useEffect(() => {
    const root = document.documentElement;
    root.lang = lang;
    root.dir = dir;
    try {
      window.localStorage.setItem(STORAGE_LANG_KEY, lang);
    } catch {
      // Ignore storage write errors
    }
  }, [lang, dir]);

  useEffect(() => {
    const root = document.documentElement;
    root.setAttribute('data-theme', theme);
    try {
      window.localStorage.setItem(STORAGE_THEME_KEY, theme);
    } catch {
      // Ignore storage write errors
    }
  }, [theme]);

  useEffect(() => {
    if (initialAuthenticatedUser !== undefined) {
      return;
    }

    let cancelled = false;
    void fetchCurrentIdentity(getInitialLanguage())
      .then((user) => {
        if (!cancelled) {
          setSessionUser(user);
        }
      })
      .catch(() => {
        if (!cancelled) {
          setSessionUser(null);
        }
      })
      .finally(() => {
        if (!cancelled) {
          setSessionLoading(false);
        }
      });

    return () => {
      cancelled = true;
    };
  }, [initialAuthenticatedUser]);

  const handleLogout = async () => {
    try {
      await logoutCurrentSession(lang);
    } finally {
      setShowChangePassword(false);
      setSessionUser(null);
    }
  };

  const handleToggleGroup = (groupId: string) => {
    setOpenGroupId((current) => (current === groupId ? '' : groupId));
  };

  const handleSelectNavItem = (item: NavItemSpec) => {
    setActiveNavItem(item);
    setWorkspaceMode(item.defaultMode ?? 'catalog');
    setMobileSidebarOpen(false);
  };

  const handleCloseModal = () => {
    const previousVariant = activeModal;
    setActiveModal(null);
    if (previousVariant === 'error') {
      sampleInputRef.current?.focus();
    }
  };

  // Filtered and paginated UX specification rows
  const filteredRows = useMemo(() => {
    const q = searchQuery.trim().toLowerCase();
    return UX_SPEC_CATALOG_ROWS.filter((row) => {
      const matchesCategory = !categoryFilter || row.category === categoryFilter;
      const matchesQuery =
        !q ||
        row.id.toLowerCase().includes(q) ||
        row.title.ar.toLowerCase().includes(q) ||
        row.title.en.toLowerCase().includes(q) ||
        row.routePreview.toLowerCase().includes(q);
      return matchesCategory && matchesQuery;
    });
  }, [searchQuery, categoryFilter]);

  const totalPages = Math.max(1, Math.ceil(filteredRows.length / PAGE_SIZE));
  const safePage = Math.min(currentPage, totalPages);
  const pagedRows = useMemo(() => {
    const start = (safePage - 1) * PAGE_SIZE;
    return filteredRows.slice(start, start + PAGE_SIZE);
  }, [filteredRows, safePage]);

  const tableColumns: DataTableColumn<UiCatalogSpecRow>[] = useMemo(
    () => [
      {
        key: 'id',
        header: t.tableColId,
        render: (row) => <code className="ltr-code">{row.id}</code>,
      },
      {
        key: 'title',
        header: t.tableColTitle,
        render: (row) => <strong>{row.title[lang]}</strong>,
      },
      {
        key: 'route',
        header: t.tableColRoute,
        render: (row) => <code className="ltr-code">{row.routePreview}</code>,
      },
      {
        key: 'pattern',
        header: t.tableColPattern,
        render: (row) => (
          <StatusBadge
            variant={
              row.pagePattern === 'standalone-form'
                ? 'info'
                : row.pagePattern === 'matrix'
                  ? 'warning'
                  : 'success'
            }
          >
            {t.patternLabels[row.pagePattern]}
          </StatusBadge>
        ),
      },
    ],
    [lang, t]
  );

  // Matrix selection handlers (reusing shared matrixSelection helpers)
  const handleToggleMatrixCell = (rowId: string, colId: string) => {
    setSelectedMatrixCells((prev) => toggleMatrixCellSelection(prev, rowId, colId));
  };

  const handleToggleMatrixRow = (rowId: string, checked: boolean) => {
    setSelectedMatrixCells((prev) =>
      toggleMatrixRowSelection(prev, matrixRows, matrixColumns, rowId, checked)
    );
  };

  const handleToggleMatrixColumn = (colId: string, checked: boolean) => {
    setSelectedMatrixCells((prev) =>
      toggleMatrixColumnSelection(prev, matrixRows, colId, checked)
    );
  };

  const handleToggleMatrixGroup = (groupId: string, checked: boolean) => {
    setSelectedMatrixCells((prev) =>
      toggleMatrixGroupSelection(prev, matrixRows, matrixColumns, groupId, checked)
    );
  };

  const handleToggleMatrixAll = (checked: boolean) => {
    setSelectedMatrixCells(toggleMatrixAllSelection(matrixRows, matrixColumns, checked));
  };

  const handleStandaloneFormSubmit = () => {
    if (!sampleText.trim()) {
      setShowSampleError(true);
      setActiveModal('error');
      return;
    }
    setShowSampleError(false);
    setActiveModal('success');
  };

  if (sessionLoading) {
    return (
      <div className="auth-page-shell">
        <div className="auth-card-container">
          <div className="auth-card">
            <LoadingState
              title={lang === 'ar' ? 'جارٍ التحقق من الجلسة...' : 'Verifying session...'}
            />
          </div>
        </div>
      </div>
    );
  }

  if (!sessionUser) {
    return (
      <LoginPage
        lang={lang}
        theme={theme}
        onChangeLanguage={setLang}
        onToggleTheme={() => setTheme((current) => (current === 'light' ? 'dark' : 'light'))}
        onAuthenticated={(user) => {
          setSessionUser(user);
          setShowChangePassword(false);
        }}
      />
    );
  }

  return (
    <>
      <AppLayoutShell
        lang={lang}
        theme={theme}
        brandTitle={t.brandTitle}
        brandSubtitle={t.brandSubtitle}
        standaloneSectionTitle={t.standaloneSectionTitle}
        collapsibleSectionTitle={t.collapsibleSectionTitle}
        topbarBreadcrumb={
          activeNavItem.id === 'UI-17'
            ? t.topbarBreadcrumb
            : `${t.brandTitle} · ${activeNavItem.id}`
        }
        topbarPageTitle={
          showChangePassword
            ? lang === 'ar'
              ? 'تغيير كلمة المرور'
              : 'Change Password'
            : activeNavItem.id === 'UI-17'
              ? t.topbarPageTitle
              : activeNavItem.label[lang]
        }
        themeLightLabel={t.themeLight}
        themeDarkLabel={t.themeDark}
        themeToggleAriaLabel={t.themeToggleLabel}
        langToggleAriaLabel={t.langToggleLabel}
        mobileMenuAriaLabel={t.mobileMenuToggle}
        standaloneNavItems={STANDALONE_NAV_ITEMS}
        sidebarGroups={SIDEBAR_GROUPS}
        openGroupId={openGroupId}
        activeNavItem={activeNavItem}
        mobileSidebarOpen={mobileSidebarOpen}
        onToggleGroup={handleToggleGroup}
        onSelectNavItem={(item) => {
          setShowChangePassword(false);
          handleSelectNavItem(item);
        }}
        onToggleMobileSidebar={() => setMobileSidebarOpen((open) => !open)}
        onCloseMobileSidebar={() => setMobileSidebarOpen(false)}
        onChangeLanguage={setLang}
        onToggleTheme={() => setTheme((current) => (current === 'light' ? 'dark' : 'light'))}
        userActions={
          <div className="topbar-user-actions">
            <span className="topbar-user-pill" data-testid="authenticated-user-pill">
              <strong>{sessionUser.fullName}</strong>
              <code className="ltr-code">{sessionUser.email}</code>
            </span>
            <Button
              variant="outline"
              size="sm"
              onClick={() => setShowChangePassword(true)}
            >
              {lang === 'ar' ? 'تغيير كلمة المرور' : 'Change Password'}
            </Button>
            <Button
              variant="outline"
              size="sm"
              onClick={() => {
                void handleLogout();
              }}
            >
              {lang === 'ar' ? 'تسجيل الخروج' : 'Logout'}
            </Button>
          </div>
        }
      >
        {showChangePassword ? (
          <ChangePasswordView
            lang={lang}
            userEmail={sessionUser.email}
            onBack={() => setShowChangePassword(false)}
            onPasswordChangedSuccess={() => {
              setShowChangePassword(false);
              setSessionUser(null);
            }}
          />
        ) : activeNavItem.id === 'UI-01' ? (
          <CompanySystemsPage
            lang={lang}
            permissions={sessionUser.permissions}
            onSessionExpired={() => {
              setShowChangePassword(false);
              setSessionUser(null);
            }}
          />
        ) : activeNavItem.id === 'UI-02' ? (
          <ConsumersManagementPage
            lang={lang}
            permissions={sessionUser.permissions}
            onSessionExpired={() => {
              setShowChangePassword(false);
              setSessionUser(null);
            }}
          />
        ) : activeNavItem.id === 'UI-03' ? (
          <ProvidersManagementPage
            lang={lang}
            permissions={sessionUser.permissions}
            onSessionExpired={() => {
              setShowChangePassword(false);
              setSessionUser(null);
            }}
          />
        ) : activeNavItem.id === 'UI-04' ? (
          <FileTypesManagementPage
            lang={lang}
            permissions={sessionUser.permissions}
            onSessionExpired={() => {
              setShowChangePassword(false);
              setSessionUser(null);
            }}
          />
        ) : activeNavItem.id === 'UI-23' ? (
          <RequirementTemplatesPage
            lang={lang}
            permissions={sessionUser.permissions}
            onSessionExpired={() => {
              setShowChangePassword(false);
              setSessionUser(null);
            }}
          />
        ) : activeNavItem.id === 'UI-05' ? (
          <ApprovalRoutesPage
            lang={lang}
            permissions={sessionUser.permissions}
            onSessionExpired={() => {
              setShowChangePassword(false);
              setSessionUser(null);
            }}
          />
        ) : activeNavItem.id === 'UI-06' ? (
          <ChangeTypesAndPoliciesPage
            lang={lang}
            permissions={sessionUser.permissions}
            onSessionExpired={() => {
              setShowChangePassword(false);
              setSessionUser(null);
            }}
          />
        ) : activeNavItem.id === 'UI-21' || activeNavItem.id === 'UI-07' ? (
          <ApiDocumentsPage
            lang={lang}
            permissions={sessionUser.permissions}
            initialMode={activeNavItem.id === 'UI-07' ? 'create' : 'list'}
            onSessionExpired={() => {
              setShowChangePassword(false);
              setSessionUser(null);
            }}
          />
        ) : activeNavItem.id === 'UI-22' || activeNavItem.id === 'UI-08' || activeNavItem.id === 'UI-09' ? (
          <IntegrationsPage
            lang={lang}
            permissions={sessionUser.permissions}
            initialMode={activeNavItem.id === 'UI-08' ? 'create' : 'list'}
            onSessionExpired={() => {
              setShowChangePassword(false);
              setSessionUser(null);
            }}
          />
        ) : activeNavItem.id === 'UI-15' ? (
          <UsersManagementPage
            lang={lang}
            permissions={sessionUser.permissions}
            currentUserId={sessionUser.id}
            onSessionExpired={() => {
              setShowChangePassword(false);
              setSessionUser(null);
            }}
            onPermissionsChanged={() => {
              if (initialAuthenticatedUser !== undefined) return;
              void fetchCurrentIdentity(lang)
                .then(setSessionUser)
                .catch(() => {});
            }}
          />
        ) : activeNavItem.id === 'UI-25' ? (
          <RolesManagementPage
            lang={lang}
            permissions={sessionUser.permissions}
            onSessionExpired={() => {
              setShowChangePassword(false);
              setSessionUser(null);
            }}
            onPermissionsChanged={() => {
              if (initialAuthenticatedUser !== undefined) return;
              void fetchCurrentIdentity(lang)
                .then(setSessionUser)
                .catch(() => {});
            }}
          />
        ) : workspaceMode !== 'catalog' ? (
          <StandaloneFormPage
            modeBadgeLabel={
              workspaceMode === 'create' ? t.standaloneCreateBadge : t.standaloneEditBadge
            }
            formTitle={
              workspaceMode === 'create' ? t.standaloneCreateTitle : t.standaloneEditTitle
            }
            formSubtitle={t.standaloneFormSubtitle}
            backButtonLabel={t.returnToCatalogBtn}
            submitButtonLabel={t.standaloneSaveBtn}
            cancelButtonLabel={t.standaloneCancelBtn}
            onBack={() => setWorkspaceMode('catalog')}
            onCancel={() => setWorkspaceMode('catalog')}
            onSubmit={handleStandaloneFormSubmit}
          >
            <div className="form-grid-2">
              <FormField
                id="sample-text-input"
                label={t.sampleInputLabel}
                required
                hint={t.sampleInputHint}
                error={showSampleError ? t.sampleInputRequiredError : undefined}
              >
                <TextInput
                  ref={sampleInputRef}
                  id="sample-text-input"
                  value={sampleText}
                  hasError={showSampleError}
                  placeholder={t.sampleInputPlaceholder}
                  onChange={(e) => {
                    setSampleText(e.target.value);
                    if (showSampleError && e.target.value.trim()) {
                      setShowSampleError(false);
                    }
                  }}
                />
              </FormField>

              <FormField id="sample-code-input" label={t.sampleCodeInputLabel} required>
                <TextInput
                  id="sample-code-input"
                  ltrCode
                  value={sampleCode}
                  onChange={(e) => setSampleCode(e.target.value)}
                />
              </FormField>

              <FormField id="sample-select-input" label={t.sampleSelectLabel}>
                <SelectInput
                  id="sample-select-input"
                  value={samplePattern}
                  onChange={(e) => setSamplePattern(e.target.value)}
                  options={[
                    { value: 'list', label: t.patternLabels.list },
                    { value: 'standalone-form', label: t.patternLabels['standalone-form'] },
                    { value: 'matrix', label: t.patternLabels.matrix },
                    { value: 'workspace-tabs', label: t.patternLabels['workspace-tabs'] },
                  ]}
                />
              </FormField>

              <FormField id="sample-textarea-input" label={t.sampleTextareaLabel}>
                <TextareaInput
                  id="sample-textarea-input"
                  value={sampleNotes}
                  placeholder={t.sampleTextareaPlaceholder}
                  onChange={(e) => setSampleNotes(e.target.value)}
                />
              </FormField>
            </div>
          </StandaloneFormPage>
        ) : (
          <>
            {/* Baseline F02 / F08 Status & Standalone Page Switcher Card */}
            <section className="surface-card" aria-labelledby="f02-status-heading">
              <div className="card-header-row">
                <div>
                  <h2 id="f02-status-heading" className="card-title heading-font">
                    {t.shellNoticeTitle}
                  </h2>
                  <p className="card-subtitle">{t.shellNoticeBody}</p>
                </div>
                <span className="status-badge">{t.baselineBadge}</span>
              </div>

              <div className="slot-summary-grid">
                <div className="slot-stat-box">
                  <div className="slot-stat-label">{t.selectedNavLabel}</div>
                  <div className="slot-stat-value">
                    {activeNavItem.label[lang]} (<span className="ltr-code">{activeNavItem.id}</span>)
                  </div>
                </div>
                <div className="slot-stat-box">
                  <div className="slot-stat-label">{t.selectedRouteLabel}</div>
                  <div className="slot-stat-value ltr-code">{activeNavItem.routePreview}</div>
                </div>
                <div className="slot-stat-box">
                  <div className="slot-stat-label">{t.plannedInSprintLabel}</div>
                  <div className="slot-stat-value">{t.plannedInSprintValue}</div>
                </div>
              </div>

              <div className="button-row" style={{ marginTop: 14 }}>
                <Button variant="primary" onClick={() => setWorkspaceMode('create')}>
                  {t.openCreatePageBtn}
                </Button>
                <Button variant="secondary" onClick={() => setWorkspaceMode('edit')}>
                  {t.openEditPageBtn}
                </Button>
              </div>
            </section>

            {/* Form Controls & Unified Central Modal Preview */}
            <section className="surface-card" aria-labelledby="f02-form-preview-heading">
              <div className="card-header-row">
                <div>
                  <h2 id="f02-form-preview-heading" className="card-title heading-font">
                    {t.formPreviewTitle}
                  </h2>
                  <p className="card-subtitle">{t.formPreviewDesc}</p>
                </div>
              </div>

              <div className="form-grid-2">
                <FormField
                  id="sample-text-input"
                  label={t.sampleInputLabel}
                  required
                  hint={t.sampleInputHint}
                  error={showSampleError ? t.sampleInputRequiredError : undefined}
                >
                  <TextInput
                    ref={sampleInputRef}
                    id="sample-text-input"
                    value={sampleText}
                    hasError={showSampleError}
                    placeholder={t.sampleInputPlaceholder}
                    onChange={(e) => {
                      setSampleText(e.target.value);
                      if (showSampleError && e.target.value.trim()) {
                        setShowSampleError(false);
                      }
                    }}
                  />
                </FormField>

                <FormField id="sample-code-input" label={t.sampleCodeInputLabel}>
                  <TextInput
                    id="sample-code-input"
                    ltrCode
                    value={sampleCode}
                    onChange={(e) => setSampleCode(e.target.value)}
                  />
                </FormField>

                <FormField id="sample-select-input" label={t.sampleSelectLabel}>
                  <SelectInput
                    id="sample-select-input"
                    value={samplePattern}
                    onChange={(e) => setSamplePattern(e.target.value)}
                    options={[
                      { value: 'list', label: t.patternLabels.list },
                      { value: 'standalone-form', label: t.patternLabels['standalone-form'] },
                      { value: 'matrix', label: t.patternLabels.matrix },
                      { value: 'workspace-tabs', label: t.patternLabels['workspace-tabs'] },
                    ]}
                  />
                </FormField>

                <FormField id="sample-textarea-input" label={t.sampleTextareaLabel}>
                  <TextareaInput
                    id="sample-textarea-input"
                    rows={2}
                    value={sampleNotes}
                    placeholder={t.sampleTextareaPlaceholder}
                    onChange={(e) => setSampleNotes(e.target.value)}
                  />
                </FormField>
              </div>

              <div className="button-row">
                <Button variant="primary" onClick={() => setActiveModal('success')}>
                  {t.openSuccessModal}
                </Button>
                <Button variant="outline" onClick={() => setActiveModal('warning')}>
                  {t.openWarningModal}
                </Button>
                <Button
                  variant="outline"
                  onClick={() => {
                    setShowSampleError(true);
                    setActiveModal('error');
                  }}
                >
                  {t.openErrorModal}
                </Button>
                <Button variant="secondary" onClick={() => setActiveModal('confirm')}>
                  {t.openConfirmModal}
                </Button>
              </div>
            </section>

            {/* Shared DataTable, Search, Filter, Pagination & State Views */}
            <section className="surface-card" aria-labelledby="f08-datatable-heading">
              <div className="card-header-row">
                <div>
                  <h2 id="f08-datatable-heading" className="card-title heading-font">
                    {t.tableSectionTitle}
                  </h2>
                  <p className="card-subtitle">{t.tableSectionDesc}</p>
                </div>
                <div className="button-row">
                  <Button
                    variant={tableState === 'ready' ? 'primary' : 'outline'}
                    size="sm"
                    onClick={() => setTableState('ready')}
                  >
                    {t.tableStateReadyBtn}
                  </Button>
                  <Button
                    variant={tableState === 'loading' ? 'primary' : 'outline'}
                    size="sm"
                    onClick={() => setTableState('loading')}
                  >
                    {t.tableStateLoadingBtn}
                  </Button>
                  <Button
                    variant={tableState === 'empty' ? 'primary' : 'outline'}
                    size="sm"
                    onClick={() => setTableState('empty')}
                  >
                    {t.tableStateEmptyBtn}
                  </Button>
                  <Button
                    variant={tableState === 'error' ? 'primary' : 'outline'}
                    size="sm"
                    onClick={() => setTableState('error')}
                  >
                    {t.tableStateErrorBtn}
                  </Button>
                </div>
              </div>

              <SearchFilterBar
                searchValue={searchQuery}
                searchPlaceholder={t.tableSearchPlaceholder}
                searchAriaLabel={t.tableSearchAria}
                onSearchChange={(val) => {
                  setSearchQuery(val);
                  setCurrentPage(1);
                }}
                filterValue={categoryFilter}
                filterAriaLabel={t.tableFilterAria}
                filterPlaceholder={t.tableFilterAll}
                filterOptions={[
                  { value: 'configuration', label: t.categoryLabels.configuration },
                  { value: 'documents', label: t.categoryLabels.documents },
                  { value: 'integrations', label: t.categoryLabels.integrations },
                  { value: 'administration', label: t.categoryLabels.administration },
                ]}
                onFilterChange={(val) => {
                  setCategoryFilter(val);
                  setCurrentPage(1);
                }}
              />

              {tableState === 'error' ? (
                <ErrorState
                  title={t.tableErrorTitle}
                  message={t.tableErrorMsg}
                  traceId="trace-ui-f08-preview"
                  retryLabel={t.tableRetryBtn}
                  onRetry={() => setTableState('ready')}
                />
              ) : (
                <>
                  <DataTable
                    columns={tableColumns}
                    rows={tableState === 'empty' ? [] : pagedRows}
                    getRowKey={(row) => row.id}
                    isLoading={tableState === 'loading'}
                    loadingTitle={t.tableLoadingTitle}
                    emptyTitle={t.tableEmptyTitle}
                    emptyDescription={t.tableEmptyDesc}
                  />
                  {tableState === 'ready' && filteredRows.length > 0 && (
                    <PaginationBar
                      currentPage={safePage}
                      totalPages={totalPages}
                      totalItems={filteredRows.length}
                      pageLabel={`${t.paginationSummaryPrefix} ${safePage} ${t.paginationOf} ${totalPages}`}
                      previousLabel={t.paginationPrev}
                      nextLabel={t.paginationNext}
                      onPageChange={setCurrentPage}
                    />
                  )}
                </>
              )}
            </section>

            {/* Shared SelectionMatrix Component */}
            <section className="surface-card" aria-labelledby="f08-matrix-heading">
              <div className="card-header-row">
                <div>
                  <h2 id="f08-matrix-heading" className="card-title heading-font">
                    {t.matrixSectionTitle}
                  </h2>
                  <p className="card-subtitle">{t.matrixSectionDesc}</p>
                </div>
                <StatusBadge variant="info">
                  {t.matrixSelectedCountLabel} {selectedMatrixCells.size}
                </StatusBadge>
              </div>

              <SelectionMatrix
                rowHeaderLabel={t.matrixRowHeader}
                selectAllLabel={t.matrixSelectAll}
                selectGroupLabel={t.matrixSelectGroup}
                selectRowAriaPrefix={t.matrixSelectRowPrefix}
                selectColAriaPrefix={t.matrixSelectColPrefix}
                columns={matrixColumns}
                rows={matrixRows}
                selectedCells={selectedMatrixCells}
                onToggleCell={handleToggleMatrixCell}
                onToggleRow={handleToggleMatrixRow}
                onToggleColumn={handleToggleMatrixColumn}
                onToggleGroup={handleToggleMatrixGroup}
                onToggleAll={handleToggleMatrixAll}
              />
            </section>

            {/* Visual Identity Tokens Verification Card */}
            <section className="surface-card" aria-labelledby="f02-tokens-heading">
              <div className="card-header-row">
                <div>
                  <h2 id="f02-tokens-heading" className="card-title heading-font">
                    {t.identityPanelTitle}
                  </h2>
                  <p className="card-subtitle">{t.identityPanelDesc}</p>
                </div>
              </div>

              <div className="table-scroll">
                <table className="token-table">
                  <thead>
                    <tr>
                      <th>{t.tokenSurfaceLabel}</th>
                      <th>{t.tokenLightLabel}</th>
                      <th>{t.tokenDarkLabel}</th>
                    </tr>
                  </thead>
                  <tbody>
                    <tr>
                      <td>{t.tokenPageBg}</td>
                      <td>
                        <span className="token-swatch-cell">
                          <span className="color-dot" style={{ backgroundColor: '#f8fafc' }} />
                          <code className="ltr-code">#f8fafc</code>
                        </span>
                      </td>
                      <td>
                        <span className="token-swatch-cell">
                          <span className="color-dot" style={{ backgroundColor: '#1a2d45' }} />
                          <code className="ltr-code">#1a2d45</code>
                        </span>
                      </td>
                    </tr>
                    <tr>
                      <td>{t.tokenSidebarTopbar}</td>
                      <td>
                        <span className="token-swatch-cell">
                          <span className="color-dot" style={{ backgroundColor: '#ffffff' }} />
                          <code className="ltr-code">#ffffff</code>
                        </span>
                      </td>
                      <td>
                        <span className="token-swatch-cell">
                          <span className="color-dot" style={{ backgroundColor: '#243d5c' }} />
                          <code className="ltr-code">#243d5c</code>
                        </span>
                      </td>
                    </tr>
                    <tr>
                      <td>{t.tokenCardSurface}</td>
                      <td>
                        <span className="token-swatch-cell">
                          <span className="color-dot" style={{ backgroundColor: '#ffffff' }} />
                          <code className="ltr-code">#ffffff</code>
                        </span>
                      </td>
                      <td>
                        <span className="token-swatch-cell">
                          <span className="color-dot" style={{ backgroundColor: '#2b4870' }} />
                          <code className="ltr-code">#2b4870</code>
                        </span>
                      </td>
                    </tr>
                    <tr>
                      <td>{t.tokenInputBg}</td>
                      <td>
                        <span className="token-swatch-cell">
                          <span className="color-dot" style={{ backgroundColor: '#ffffff' }} />
                          <code className="ltr-code">#ffffff</code>
                        </span>
                      </td>
                      <td>
                        <span className="token-swatch-cell">
                          <span className="color-dot" style={{ backgroundColor: '#1c3250' }} />
                          <code className="ltr-code">#1c3250</code>
                        </span>
                      </td>
                    </tr>
                    <tr>
                      <td>{t.tokenPrimaryBtn}</td>
                      <td>
                        <span className="token-swatch-cell">
                          <span className="color-dot" style={{ backgroundColor: '#4f46e5' }} />
                          <code className="ltr-code">#4f46e5</code>
                        </span>
                      </td>
                      <td>
                        <span className="token-swatch-cell">
                          <span className="color-dot" style={{ backgroundColor: '#6366f1' }} />
                          <code className="ltr-code">#6366f1</code>
                        </span>
                      </td>
                    </tr>
                    <tr>
                      <td>{t.tokenStatusColors}</td>
                      <td colSpan={2}>
                        <span className="token-swatch-cell" style={{ marginInlineEnd: 14 }}>
                          <span className="color-dot" style={{ backgroundColor: '#10b981' }} />
                          <code className="ltr-code">#10b981</code>
                        </span>
                        <span className="token-swatch-cell" style={{ marginInlineEnd: 14 }}>
                          <span className="color-dot" style={{ backgroundColor: '#f43f5e' }} />
                          <code className="ltr-code">#f43f5e</code>
                        </span>
                        <span className="token-swatch-cell">
                          <span className="color-dot" style={{ backgroundColor: '#f59e0b' }} />
                          <code className="ltr-code">#f59e0b</code>
                        </span>
                      </td>
                    </tr>
                  </tbody>
                </table>
              </div>
            </section>

            {/* F02 & F08 Scope Boundaries */}
            <section className="surface-card" aria-labelledby="f02-scope-heading">
              <div className="card-header-row">
                <h2 id="f02-scope-heading" className="card-title heading-font">
                  {t.scopeGuardTitle}
                </h2>
              </div>
              <ul className="scope-list">
                {t.scopeGuards.map((guard, index) => (
                  <li key={index}>{guard}</li>
                ))}
              </ul>
            </section>
          </>
        )}
      </AppLayoutShell>

      {/* Unified Central Modal Dialog (PRD-06 & 02_UX_UI_v1.2.md) */}
      <CentralModalDialog
        isOpen={activeModal !== null}
        variant={activeModal ?? 'info'}
        title={activeModal ? t.modalSamples[activeModal].title : ''}
        message={activeModal ? t.modalSamples[activeModal].message : ''}
        confirmLabel={t.modalConfirmBtn}
        cancelLabel={activeModal === 'confirm' ? t.modalCancelBtn : undefined}
        closeAriaLabel={t.modalCloseAria}
        onConfirm={handleCloseModal}
        onCancel={activeModal === 'confirm' ? () => setActiveModal(null) : undefined}
      />
    </>
  );
}
