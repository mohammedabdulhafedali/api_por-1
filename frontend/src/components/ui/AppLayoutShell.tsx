import { ReactNode } from 'react';
import { AppLanguage, AppTheme, NavGroupSpec, NavItemSpec } from '../../constants/uiBaseline';

const ICON_PATHS: Record<string, ReactNode> = {
  grid: (
    <>
      <rect x="3" y="3" width="7" height="7" rx="1" />
      <rect x="14" y="3" width="7" height="7" rx="1" />
      <rect x="3" y="14" width="7" height="7" rx="1" />
      <rect x="14" y="14" width="7" height="7" rx="1" />
    </>
  ),
  file: (
    <>
      <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" />
      <path d="M14 2v6h6M8 13h8M8 17h8" />
    </>
  ),
  network: (
    <>
      <rect x="2" y="2" width="7" height="7" rx="1" />
      <rect x="15" y="2" width="7" height="7" rx="1" />
      <rect x="8.5" y="15" width="7" height="7" rx="1" />
      <path d="M5.5 9v4h6.5v2M18.5 9v4H12" />
    </>
  ),
  settings: (
    <>
      <circle cx="12" cy="12" r="3" />
      <path d="M19.4 15a2 2 0 0 0 .4 2.2l-1.7 1.7a2 2 0 0 0-2.2-.4A2 2 0 0 0 14 20h-4a2 2 0 0 0-1.9-1.5 2 2 0 0 0-2.2.4l-1.7-1.7a2 2 0 0 0 .4-2.2 2 2 0 0 0-1.6-1.8v-2.4A2 2 0 0 0 4.6 9a2 2 0 0 0-.4-2.2l1.7-1.7a2 2 0 0 0 2.2.4A2 2 0 0 0 10 4h4a2 2 0 0 0 1.9 1.5 2 2 0 0 0 2.2-.4l1.7 1.7a2 2 0 0 0-.4 2.2 2 2 0 0 0 1.6 1.8v2.4A2 2 0 0 0 19.4 15z" />
    </>
  ),
  users: (
    <>
      <path d="M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2M16 3a4 4 0 0 1 0 8M22 21v-2a4 4 0 0 0-3-3.9" />
      <circle cx="9" cy="7" r="4" />
    </>
  ),
  shield: (
    <>
      <path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z" />
      <path d="m9 12 2 2 4-4" />
    </>
  ),
  search: (
    <>
      <circle cx="10.9" cy="10.9" r="6.9" />
      <path d="m16 16 5 5" />
    </>
  ),
  bell: <path d="M18 8a6 6 0 0 0-12 0c0 7-3 7-3 9h18c0-2-3-2-3-9M10 21h4" />,
  plus: <path d="M12 5v14M5 12h14" />,
  check: <path d="m4 12 5 5L20 6" />,
  clock: (
    <>
      <circle cx="12" cy="12" r="9" />
      <path d="M12 7v5l3 2" />
    </>
  ),
  down: <path d="m7 10 5 5 5-5" />,
  left: <path d="m15 5-7 7 7 7" />,
  right: <path d="m9 5 7 7-7 7" />,
  x: <path d="M18 6 6 18M6 6l12 12" />,
  upload: <path d="M12 16V3m0 0L7 8m5-5 5 5M3 17v3h18v-3" />,
  download: <path d="M12 4v13m0 0-5-5m5 5 5-5M3 20h18" />,
  edit: <path d="m3 21 4-.8L20 7a2.1 2.1 0 0 0-3-3L4 17z" />,
  trash: <path d="M4 7h16M10 3h4M6 7l1 14h10l1-14M10 11v7M14 11v7" />,
  layers: (
    <>
      <rect x="3" y="3" width="18" height="7" rx="2" />
      <rect x="3" y="14" width="18" height="7" rx="2" />
    </>
  ),
  external: (
    <>
      <path d="M14 3h7v7M10 14 21 3" />
      <path d="M20 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2h6" />
    </>
  ),
  list: <path d="M9 6h13M9 12h13M9 18h13M4 6h.01M4 12h.01M4 18h.01" />,
  lock: (
    <>
      <rect x="4" y="10" width="16" height="11" rx="2" />
      <path d="M8 10V7a4 4 0 0 1 8 0v3" />
    </>
  ),
  repeat: <path d="M17 2l4 4-4 4M3 11V9a3 3 0 0 1 3-3h15M7 22l-4-4 4-4M21 13v2a3 3 0 0 1-3 3H3" />,
  server: (
    <>
      <rect x="3" y="3" width="18" height="8" rx="2" />
      <rect x="3" y="13" width="18" height="8" rx="2" />
      <path d="M7 7h.01M7 17h.01" />
    </>
  ),
  menu: <path d="M3 6h18M3 12h18M3 18h18" />,
};

export function ReferenceIcon({ name, size = 17 }: { name: string; size?: number }) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.8"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      {ICON_PATHS[name] ?? ICON_PATHS.file}
    </svg>
  );
}

const NAV_ICON_BY_ID: Record<string, string> = {
  'UI-17': 'grid',
  'UI-18': 'search',
  'UI-19': 'external',
  systems: 'server',
  settings: 'settings',
  administration: 'shield',
  'api-documents': 'file',
  integrations: 'network',
  'UI-01': 'server',
  'UI-02': 'users',
  'UI-03': 'network',
  'UI-04': 'file',
  'UI-23': 'list',
  'UI-05': 'check',
  'UI-06': 'repeat',
  'UI-15': 'users',
  'UI-25': 'shield',
  'UI-16': 'clock',
  'UI-21': 'file',
  'UI-07': 'plus',
  'UI-22': 'network',
  'UI-08': 'plus',
  'UI-09': 'layers',
};

export interface AppLayoutShellProps {
  lang: AppLanguage;
  theme: AppTheme;
  brandTitle: string;
  brandSubtitle: string;
  standaloneSectionTitle: string;
  collapsibleSectionTitle: string;
  topbarBreadcrumb: string;
  topbarPageTitle: string;
  themeLightLabel: string;
  themeDarkLabel: string;
  themeToggleAriaLabel: string;
  langToggleAriaLabel: string;
  mobileMenuAriaLabel: string;
  standaloneNavItems: NavItemSpec[];
  sidebarGroups: NavGroupSpec[];
  openGroupId: string;
  activeNavItem: NavItemSpec;
  mobileSidebarOpen: boolean;
  onToggleGroup: (groupId: string) => void;
  onSelectNavItem: (item: NavItemSpec) => void;
  onToggleMobileSidebar: () => void;
  onCloseMobileSidebar: () => void;
  onChangeLanguage: (lang: AppLanguage) => void;
  onToggleTheme: () => void;
  userActions?: ReactNode;
  children: ReactNode;
}

/**
 * Shared application shell (Sidebar + Topbar + Workspace Main Content).
 * Enforces:
 * - Single open collapsible group in the sidebar in the approved sequence.
 * - Page title in the topbar without duplicating a hero header block in the workspace content (PRD-06).
 * - Responsive mobile drawer and theme synchronization across all surfaces.
 */
export function AppLayoutShell({
  lang,
  theme,
  brandTitle,
  brandSubtitle,
  standaloneSectionTitle,
  collapsibleSectionTitle,
  topbarBreadcrumb,
  topbarPageTitle,
  themeLightLabel,
  themeDarkLabel,
  themeToggleAriaLabel,
  langToggleAriaLabel,
  mobileMenuAriaLabel,
  standaloneNavItems,
  sidebarGroups,
  openGroupId,
  activeNavItem,
  mobileSidebarOpen,
  onToggleGroup,
  onSelectNavItem,
  onToggleMobileSidebar,
  onCloseMobileSidebar,
  onChangeLanguage,
  onToggleTheme,
  userActions,
  children,
}: AppLayoutShellProps) {
  return (
    <div className="app-shell">
      <div
        className={`mobile-backdrop ${mobileSidebarOpen ? 'open' : ''}`}
        onClick={onCloseMobileSidebar}
        aria-hidden="true"
      />

      <aside
        className={`app-sidebar ${mobileSidebarOpen ? 'mobile-open' : ''}`}
        aria-label="Sidebar Navigation"
      >
        <div className="brand-header">
          <div className="brand-mark" aria-hidden="true">
            <ReferenceIcon name="network" size={22} />
            <span className="sr-only">AG</span>
          </div>
          <div>
            <div className="brand-title heading-font">{brandTitle}</div>
            <div className="brand-subtitle">{brandSubtitle}</div>
          </div>
        </div>

        <div className="nav-section-caption sr-only">{standaloneSectionTitle}</div>
        <div className="nav-standalone-list">
          {standaloneNavItems.map((item) => {
            const isActive = activeNavItem.id === item.id;
            return (
              <button
                key={item.id}
                type="button"
                data-screen-id={item.id}
                className={`nav-item-btn ${isActive ? 'active' : ''}`}
                onClick={() => onSelectNavItem(item)}
              >
                <span className="nav-item-main">
                  <ReferenceIcon name={NAV_ICON_BY_ID[item.id] ?? 'file'} size={16} />
                  <span>{item.label[lang]}</span>
                </span>
                <span className="nav-item-code ltr-code sr-only">{item.id}</span>
              </button>
            );
          })}
        </div>

        <div className="nav-section-caption sr-only">{collapsibleSectionTitle}</div>
        <nav className="nav-accordion-list" aria-label={collapsibleSectionTitle}>
          {sidebarGroups.map((group) => {
            const isExpanded = openGroupId === group.id;
            const hasActiveChild = group.items.some((it) => it.id === activeNavItem.id);
            return (
              <div key={group.id} className="nav-group">
                <button
                  type="button"
                  data-nav-group={group.id}
                  className={`nav-group-toggle heading-font ${isExpanded ? 'expanded' : ''} ${hasActiveChild ? 'active-section' : ''}`}
                  aria-expanded={isExpanded}
                  onClick={() => onToggleGroup(group.id)}
                >
                  <span className="nav-item-main">
                    <ReferenceIcon name={NAV_ICON_BY_ID[group.id] ?? 'layers'} size={17} />
                    <span>{group.label[lang]}</span>
                  </span>
                  <span className="nav-chevron" aria-hidden="true">
                    <ReferenceIcon name="down" size={15} />
                  </span>
                </button>
                {isExpanded && (
                  <div className="nav-group-items">
                    {group.items.map((item) => {
                      const isActive = activeNavItem.id === item.id;
                      return (
                        <button
                          key={item.id}
                          type="button"
                          data-screen-id={item.id}
                          className={`nav-item-btn ${isActive ? 'active' : ''}`}
                          onClick={() => onSelectNavItem(item)}
                        >
                          <span className="nav-item-main">
                            <ReferenceIcon name={NAV_ICON_BY_ID[item.id] ?? 'file'} size={15} />
                            <span>{item.label[lang]}</span>
                          </span>
                          <span className="nav-item-code ltr-code sr-only">{item.id}</span>
                        </button>
                      );
                    })}
                  </div>
                )}
              </div>
            );
          })}
        </nav>
      </aside>

      <div className="app-main">
        <header className="app-topbar">
          <div className="topbar-start">
            <button
              type="button"
              className="mobile-menu-btn"
              aria-label={mobileMenuAriaLabel}
              aria-expanded={mobileSidebarOpen}
              onClick={onToggleMobileSidebar}
            >
              <ReferenceIcon name="menu" size={17} />
            </button>
            <div className="topbar-titles">
              <div className="topbar-crumb">
                {topbarBreadcrumb.replace(/\s*·\s*UI-\d+[A-Z]?/g, '')}
                <span className="sr-only"> · <span className="ltr-code">{activeNavItem.id}</span></span>
              </div>
              <h1 className="topbar-page-title heading-font">{topbarPageTitle}</h1>
            </div>
          </div>

          <div className="topbar-actions top-actions">
            {userActions}
            <div className="preference-controls">
              <div className="lang-switch-group language-switch" role="group" aria-label={langToggleAriaLabel}>
                <button
                  type="button"
                  className={`lang-switch-btn language-choice ${lang === 'ar' ? 'active' : ''}`}
                  onClick={() => onChangeLanguage('ar')}
                  aria-pressed={lang === 'ar'}
                >
                  AR
                </button>
                <button
                  type="button"
                  className={`lang-switch-btn language-choice ${lang === 'en' ? 'active' : ''}`}
                  onClick={() => onChangeLanguage('en')}
                  aria-pressed={lang === 'en'}
                >
                  EN
                </button>
              </div>

              <button
                type="button"
                className="theme-toggle-btn icon-btn theme-switch"
                aria-label={themeToggleAriaLabel}
                onClick={onToggleTheme}
              >
                <span aria-hidden="true">{theme === 'light' ? '🌙' : '☀️'}</span>
                <span className="sr-only">{theme === 'light' ? themeDarkLabel : themeLightLabel}</span>
              </button>
            </div>
          </div>
        </header>

        <main className="app-content">{children}</main>
      </div>
    </div>
  );
}

