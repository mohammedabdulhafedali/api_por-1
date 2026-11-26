import { ReactNode, useState } from 'react';
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
  link: (
    <>
      <path d="M10 13a5 5 0 0 0 7.54.54l3-3a5 5 0 0 0-7.07-7.07l-1.72 1.71" />
      <path d="M14 11a5 5 0 0 0-7.54-.54l-3 3a5 5 0 0 0 7.07 7.07l1.71-1.71" />
    </>
  ),
  building: (
    <>
      <rect x="4" y="2" width="16" height="20" rx="2" />
      <path d="M9 22v-4h6v4M8 6h.01M16 6h.01M12 6h.01M12 10h.01M12 14h.01M16 10h.01M16 14h.01M8 10h.01M8 14h.01" />
    </>
  ),
  book: (
    <>
      <path d="M2 3h6a4 4 0 0 1 4 4v14a3 3 0 0 0-3-3H2z" />
      <path d="M22 3h-6a4 4 0 0 0-4 4v14a3 3 0 0 1 3-3h7z" />
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
  user: (
    <>
      <path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2" />
      <circle cx="12" cy="7" r="4" />
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
  up: <path d="m17 14-5-5-5 5" />,
  down: <path d="m7 10 5 5 5-5" />,
  left: <path d="m15 5-7 7 7 7" />,
  right: <path d="m9 5 7 7-7 7" />,
  x: <path d="M18 6 6 18M6 6l12 12" />,
  upload: <path d="M12 16V3m0 0L7 8m5-5 5 5M3 17v3h18v-3" />,
  download: <path d="M12 4v13m0 0-5-5m5 5 5-5M3 20h18" />,
  edit: <path d="m3 21 4-.8L20 7a2.1 2.1 0 0 0-3-3L4 17z" />,
  trash: <path d="M4 7h16M10 3h4M6 7l1 14h10l1-14M10 11v7M14 11v7" />,
  power: (
    <>
      <path d="M18.36 6.64a9 9 0 1 1-12.73 0" />
      <line x1="12" y1="2" x2="12" y2="12" />
    </>
  ),
  key: (
    <>
      <circle cx="7.5" cy="15.5" r="4.5" />
      <path d="m21 2-9.6 9.6M15.5 7.5l3 3L22 7l-3-3" />
    </>
  ),
  layers: (
    <>
      <polygon points="12 2 2 7 12 12 22 7 12 2" />
      <polyline points="2 17 12 22 22 17" />
      <polyline points="2 12 12 17 22 12" />
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
  eye: (
    <>
      <path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z" />
      <circle cx="12" cy="12" r="3" />
    </>
  ),
  'eye-off': (
    <>
      <path d="M17.94 17.94A10.07 10.07 0 0 1 12 20c-7 0-11-8-11-8a18.45 18.45 0 0 1 5.06-5.94" />
      <path d="M9.9 4.24A9.12 9.12 0 0 1 12 4c7 0 11 8 11 8a18.5 18.5 0 0 1-2.16 3.19" />
      <path d="M14.12 14.12a3 3 0 1 1-4.24-4.24" />
      <line x1="1" y1="1" x2="23" y2="23" />
    </>
  ),
  moon: (
    <>
      <path d="M21 12.79A9 9 0 1 1 11.21 3 7 7 0 0 0 21 12.79z" />
      <path d="M17.5 4.5v3M16 6h3" strokeWidth="1.5" />
    </>
  ),
  sun: (
    <>
      <circle cx="12" cy="12" r="4.5" />
      <line x1="12" y1="2" x2="12" y2="4.5" />
      <line x1="12" y1="19.5" x2="12" y2="22" />
      <line x1="4.93" y1="4.93" x2="6.7" y2="6.7" />
      <line x1="17.3" y1="17.3" x2="19.07" y2="19.07" />
      <line x1="2" y1="12" x2="4.5" y2="12" />
      <line x1="19.5" y1="12" x2="22" y2="12" />
      <line x1="4.93" y1="19.07" x2="6.7" y2="17.3" />
      <line x1="17.3" y1="6.7" x2="19.07" y2="4.93" />
    </>
  ),
  cube: (
    <>
      <path d="M21 16V8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16z" />
      <polyline points="3.27 6.96 12 12.01 20.73 6.96" />
      <line x1="12" y1="22.08" x2="12" y2="12" />
    </>
  ),
  globe: (
    <>
      <circle cx="12" cy="12" r="10" />
      <line x1="2" y1="12" x2="22" y2="12" />
      <path d="M12 2a15.3 15.3 0 0 1 4 10 15.3 15.3 0 0 1-4 10 15.3 15.3 0 0 1-4-10 15.3 15.3 0 0 1 4-10z" />
    </>
  ),
  pin: (
    <>
      <path d="M12 17v5" />
      <path d="M9 10.76a2 2 0 0 1-1.11 1.79l-1.78.9A2 2 0 0 0 5 15.24V16a1 1 0 0 0 1 1h12a1 1 0 0 0 1-1v-.76a2 2 0 0 0-1.11-1.79l-1.78-.9A2 2 0 0 1 15 10.76V7a1 1 0 0 1 1-1 1 1 0 0 0 1-1V4a1 1 0 0 0-1-1H8a1 1 0 0 0-1 1v1a1 1 0 0 0 1 1 1 1 0 0 1 1 1z" />
    </>
  ),
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
  'UI-17': 'clock',
  systems: 'server',
  settings: 'settings',
  administration: 'shield',
  'api-documents': 'file',
  integrations: 'network',
  'UI-01': 'layers',
  'UI-02': 'building',
  'UI-03': 'server',
  'UI-04': 'file',
  'UI-23': 'list',
  'UI-05': 'check',
  'UI-06': 'repeat',
  'UI-06P': 'shield',
  'UI-15': 'users',
  'UI-25': 'shield',
  'UI-16': 'clock',
  'UI-21': 'file',
  'UI-07': 'plus',
  'UI-04G': 'layers',
  'UI-22': 'link',
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
  desktopSidebarCollapsed?: boolean;
  onToggleDesktopSidebar?: () => void;
  onToggleGroup: (groupId: string) => void;
  onSelectNavItem: (item: NavItemSpec) => void;
  onToggleMobileSidebar: () => void;
  onCloseMobileSidebar: () => void;
  onChangeLanguage: (lang: AppLanguage) => void;
  onToggleTheme: () => void;
  userActions?: ReactNode;
  children: ReactNode;
}

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
  desktopSidebarCollapsed = false,
  onToggleDesktopSidebar,
  onToggleGroup,
  onSelectNavItem,
  onToggleMobileSidebar,
  onCloseMobileSidebar,
  onChangeLanguage,
  onToggleTheme,
  userActions,
  children,
}: AppLayoutShellProps) {
  const [isPinned, setIsPinned] = useState<boolean>(true);
  const [isMiniRail, setIsMiniRail] = useState<boolean>(false);

  const visibleStandaloneItems = standaloneNavItems;

  const handleNavClick = (item: NavItemSpec) => {
    onSelectNavItem(item);
    if (!isPinned && !desktopSidebarCollapsed) {
      onToggleDesktopSidebar?.();
    }
  };

  const handleGroupToggle = (groupId: string) => {
    if (isMiniRail) {
      setIsMiniRail(false);
    }
    onToggleGroup(groupId);
  };

  const collapseChevronIcon = isMiniRail
    ? lang === 'ar'
      ? 'left'
      : 'right'
    : lang === 'ar'
      ? 'right'
      : 'left';

  return (
    <div
      className={`app-shell app ${desktopSidebarCollapsed ? 'sidebar-collapsed' : ''} ${
        isMiniRail ? 'sidebar-mini-rail' : ''
      }`}
    >
      <div
        className={`mobile-backdrop mobile-curtain ${mobileSidebarOpen ? 'open' : ''}`}
        onClick={onCloseMobileSidebar}
        aria-hidden="true"
      />

      <aside
        className={`app-sidebar sidebar ref-sidebar ${mobileSidebarOpen ? 'mobile-open open' : ''} ${
          desktopSidebarCollapsed ? 'collapsed' : ''
        } ${isMiniRail ? 'mini-rail' : ''}`}
        aria-label="Sidebar Navigation"
      >
        <div className="ref-brand-header brand-header brand">
          <div className="ref-brand-main">
            <div className="brand-mark brand-mark-cube" aria-hidden="true">
              <ReferenceIcon name="cube" size={22} />
              <span className="sr-only">AG</span>
            </div>
            {!isMiniRail && (
              <div className="ref-brand-titles">
                <div className="brand-title brand-name heading-font">
                  {lang === 'ar' ? (
                    <>
                      إدارة توثيق
                      <br />
                      وحوكمة API
                    </>
                  ) : (
                    brandTitle
                  )}
                </div>
                <div className="brand-subtitle brand-sub sr-only">{brandSubtitle}</div>
              </div>
            )}
          </div>

          <div className="brand-header-tools">
            <button
              type="button"
              className={`sidebar-pin-badge ${isPinned ? 'pinned' : 'unpinned'}`}
              onClick={() => setIsPinned((prev) => !prev)}
              aria-pressed={isPinned}
              title={
                lang === 'ar'
                  ? isPinned
                    ? 'القائمة مثبّتة (اضغط لإلغاء التثبيت والطي التلقائي)'
                    : 'تثبيت القائمة الجانبية'
                  : isPinned
                    ? 'Sidebar pinned (click to auto-hide on select)'
                    : 'Pin sidebar'
              }
            >
              <ReferenceIcon name="pin" size={13} />
            </button>
            <button
              type="button"
              className={`sidebar-collapse-mini-btn ${isMiniRail ? 'active' : ''}`}
              onClick={() => setIsMiniRail((prev) => !prev)}
              aria-pressed={isMiniRail}
              title={
                lang === 'ar'
                  ? isMiniRail
                    ? 'توسيع القائمة الجانبية'
                    : 'تصغير القائمة إلى شريط أيقونات'
                  : isMiniRail
                    ? 'Expand sidebar'
                    : 'Collapse to icon rail'
              }
            >
              <ReferenceIcon name={collapseChevronIcon} size={13} />
            </button>
          </div>
        </div>

        <div className="nav-section-caption sr-only">{standaloneSectionTitle}</div>
        <div className="nav-standalone-list nav-direct ref-sidebar-list">
          {visibleStandaloneItems.map((item) => {
            const isActive = activeNavItem.id === item.id;
            return (
              <button
                key={item.id}
                type="button"
                data-screen-id={item.id}
                title={item.label[lang]}
                className={`ref-sidebar-item nav-item-btn nav-item ${isActive ? 'active' : ''}`}
                aria-current={isActive ? 'page' : undefined}
                onClick={() => handleNavClick(item)}
              >
                <span className="nav-item-main">
                  <span className="ref-sidebar-icon">
                    <ReferenceIcon name={NAV_ICON_BY_ID[item.id] ?? 'file'} size={17} />
                  </span>
                  {!isMiniRail && <span className="ref-sidebar-label">{item.label[lang]}</span>}
                </span>
                <span className="nav-item-code ltr-code sr-only">{item.id}</span>
              </button>
            );
          })}
        </div>

        <div className="nav-section-caption sr-only">{collapsibleSectionTitle}</div>
        <nav id="sidebar-navigation" className="nav-accordion-list ref-sidebar-nav" aria-label={collapsibleSectionTitle}>
          {sidebarGroups.map((group) => {
            const isExpanded = openGroupId === group.id;
            const hasActiveChild = group.items.some((it) => it.id === activeNavItem.id);
            return (
              <div key={group.id} className="nav-group nav-accordion">
                <button
                  type="button"
                  data-nav-group={group.id}
                  title={group.label[lang]}
                  className={`nav-group-toggle nav-toggle heading-font ${isExpanded ? 'expanded' : ''} ${
                    hasActiveChild ? 'active-section' : ''
                  }`}
                  aria-expanded={isExpanded}
                  onClick={() => handleGroupToggle(group.id)}
                >
                  <span className="nav-item-main">
                    <span className="ref-sidebar-icon">
                      <ReferenceIcon name={NAV_ICON_BY_ID[group.id] ?? 'layers'} size={17} />
                    </span>
                    {!isMiniRail && <span className="nav-group-label">{group.label[lang]}</span>}
                  </span>
                  {!isMiniRail && (
                    <span className="nav-chevron" aria-hidden="true">
                      <ReferenceIcon name="down" size={15} />
                    </span>
                  )}
                </button>
                {isExpanded && !isMiniRail && (
                  <div className="nav-group-items nav-children">
                    {group.items.map((item) => {
                      const isActive = activeNavItem.id === item.id;
                      return (
                        <button
                          key={item.id}
                          type="button"
                          data-screen-id={item.id}
                          title={item.label[lang]}
                          className={`ref-sidebar-item nav-item-btn nav-item ${isActive ? 'active' : ''}`}
                          aria-current={isActive ? 'page' : undefined}
                          onClick={() => handleNavClick(item)}
                        >
                          <span className="nav-item-main">
                            <span className="ref-sidebar-icon">
                              <ReferenceIcon name={NAV_ICON_BY_ID[item.id] ?? 'file'} size={15} />
                            </span>
                            <span className="ref-sidebar-label">{item.label[lang]}</span>
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

      <div className="app-main main-area">
        <header className="app-topbar topbar ref-topbar">
          <div className="topbar-start top-right">
            <button
              type="button"
              className="mobile-menu-btn icon-btn mobile-menu topbar-hamburger-btn"
              aria-label={mobileMenuAriaLabel}
              aria-expanded={!desktopSidebarCollapsed}
              onClick={() => {
                if (window.innerWidth <= 960) {
                  onToggleMobileSidebar();
                } else if (isMiniRail) {
                  setIsMiniRail(false);
                } else {
                  onToggleDesktopSidebar?.();
                }
              }}
            >
              <ReferenceIcon name="menu" size={18} />
            </button>
            <div className="topbar-titles sr-only">
              <div className="topbar-crumb crumb">
                {topbarBreadcrumb.replace(/\s*·\s*UI-\d+[A-Z]?/g, '')}
                <span className="sr-only"> · <span className="ltr-code">{activeNavItem.id}</span></span>
              </div>
              <h1 className="topbar-page-title page-name heading-font">{topbarPageTitle}</h1>
            </div>
          </div>

          <div className="topbar-actions top-actions ref-topbar-actions">
            <div className="preference-controls ref-pref-controls">
              <button
                type="button"
                className="ref-theme-btn theme-toggle-btn icon-btn theme-switch"
                aria-label={themeToggleAriaLabel}
                onClick={onToggleTheme}
              >
                <ReferenceIcon name="moon" size={17} />
                <span className="sr-only">{theme === 'light' ? themeDarkLabel : themeLightLabel}</span>
              </button>

              <button
                type="button"
                className="ref-lang-dropdown-btn"
                aria-label={langToggleAriaLabel}
                onClick={() => onChangeLanguage(lang === 'ar' ? 'en' : 'ar')}
              >
                <span className="ref-lang-globe">
                  <ReferenceIcon name="globe" size={15} />
                </span>
                <span>{lang === 'ar' ? 'العربية' : 'English'}</span>
                <span className="ref-lang-caret">⌄</span>
              </button>
            </div>
            {userActions}
          </div>
        </header>

        <main id="main" className="app-content content">{children}</main>
      </div>
    </div>
  );
}
