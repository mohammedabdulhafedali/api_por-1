import { ReactNode } from 'react';
import { AppLanguage, AppTheme, NavGroupSpec, NavItemSpec } from '../../constants/uiBaseline';

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
            AG
          </div>
          <div>
            <div className="brand-title heading-font">{brandTitle}</div>
            <div className="brand-subtitle">{brandSubtitle}</div>
          </div>
        </div>

        <div className="nav-section-caption">{standaloneSectionTitle}</div>
        <div className="nav-standalone-list">
          {standaloneNavItems.map((item) => {
            const isActive = activeNavItem.id === item.id;
            return (
              <button
                key={item.id}
                type="button"
                className={`nav-item-btn ${isActive ? 'active' : ''}`}
                onClick={() => onSelectNavItem(item)}
              >
                <span>{item.label[lang]}</span>
                <span className="nav-item-code ltr-code">{item.id}</span>
              </button>
            );
          })}
        </div>

        <div className="nav-section-caption">{collapsibleSectionTitle}</div>
        <nav className="nav-accordion-list" aria-label={collapsibleSectionTitle}>
          {sidebarGroups.map((group) => {
            const isExpanded = openGroupId === group.id;
            return (
              <div key={group.id} className="nav-group">
                <button
                  type="button"
                  className={`nav-group-toggle heading-font ${isExpanded ? 'expanded' : ''}`}
                  aria-expanded={isExpanded}
                  onClick={() => onToggleGroup(group.id)}
                >
                  <span>{group.label[lang]}</span>
                  <span className="nav-chevron" aria-hidden="true">
                    ▾
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
                          className={`nav-item-btn ${isActive ? 'active' : ''}`}
                          onClick={() => onSelectNavItem(item)}
                        >
                          <span>{item.label[lang]}</span>
                          <span className="nav-item-code ltr-code">{item.id}</span>
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
              ☰
            </button>
            <div className="topbar-titles">
              <div className="topbar-crumb">
                {topbarBreadcrumb} · <span className="ltr-code">{activeNavItem.id}</span>
              </div>
              <h1 className="topbar-page-title heading-font">{topbarPageTitle}</h1>
            </div>
          </div>

          <div className="topbar-actions">
            {userActions}
            <div className="lang-switch-group" role="group" aria-label={langToggleAriaLabel}>
              <button
                type="button"
                className={`lang-switch-btn ${lang === 'ar' ? 'active' : ''}`}
                onClick={() => onChangeLanguage('ar')}
                aria-pressed={lang === 'ar'}
              >
                AR
              </button>
              <button
                type="button"
                className={`lang-switch-btn ${lang === 'en' ? 'active' : ''}`}
                onClick={() => onChangeLanguage('en')}
                aria-pressed={lang === 'en'}
              >
                EN
              </button>
            </div>

            <button
              type="button"
              className="theme-toggle-btn"
              aria-label={themeToggleAriaLabel}
              onClick={onToggleTheme}
            >
              <span aria-hidden="true">{theme === 'light' ? '🌙' : '☀️'}</span>
              <span>{theme === 'light' ? themeDarkLabel : themeLightLabel}</span>
            </button>
          </div>
        </header>

        <main className="app-content">{children}</main>
      </div>
    </div>
  );
}

