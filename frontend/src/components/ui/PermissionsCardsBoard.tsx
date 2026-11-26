import { useMemo, useState } from 'react';
import { AppLanguage } from '../../constants/uiBaseline';
import { Button } from './FormControls';

export type PermissionChipMode = 'standard' | 'inherited' | 'grant' | 'revoke';

export interface PermissionBoardItem {
  id: string;
  columnId: string;
  label: string;
  checked: boolean;
  mode: PermissionChipMode;
  statusTag?: string;
  title?: string;
  canReset?: boolean;
}

export interface PermissionBoardScreen {
  id: string;
  groupId: string;
  groupLabel: string;
  title: string;
  items: PermissionBoardItem[];
  hasOverrides?: boolean;
}

export interface PermissionBoardActionColumn {
  id: string;
  label: string;
}

export interface PermissionsCardsBoardProps {
  lang: AppLanguage;
  disabled?: boolean;
  screens: PermissionBoardScreen[];
  actionColumns: PermissionBoardActionColumn[];
  showOverridesFilter?: boolean;
  onToggleItem: (itemId: string, checked: boolean) => void;
  onResetItem?: (itemId: string) => void;
  onToggleScreen: (screenId: string, checked: boolean) => void;
  onResetScreen?: (screenId: string) => void;
  onToggleGroup: (groupId: string, checked: boolean) => void;
  onToggleColumn?: (columnId: string, checked: boolean) => void;
  onToggleAll: (checked: boolean) => void;
  extraToolbarActions?: React.ReactNode;
}

const COPY = {
  ar: {
    searchPlaceholder: 'ابحث باسم الشاشة أو الصلاحية...',
    searchAria: 'بحث في شاشات الصلاحيات',
    selectAll: 'تحديد الكل',
    overridesOnly: 'الاستثناءات الفردية فقط',
    quickActionsTitle: 'تحديد جماعي حسب الإجراء',
    selectColPrefix: 'تحديد عمود',
    selectGroupPrefix: 'تحديد القسم',
    selectGroupShort: 'تحديد القسم بالكامل',
    selectRowPrefix: 'تحديد صف',
    selectRowShort: 'الكل',
    resetRow: 'إرجاع الصف للموروث',
    resetCellTitle: 'إرجاع للموروث',
    emptyTitle: 'لا توجد شاشات مطابقة للبحث أو التصفية.',
  },
  en: {
    searchPlaceholder: 'Search by screen or action name...',
    searchAria: 'Search permission screens',
    selectAll: 'Select All',
    overridesOnly: 'Overrides Only',
    quickActionsTitle: 'Bulk Select by Action',
    selectColPrefix: 'Select column',
    selectGroupPrefix: 'Select Section',
    selectGroupShort: 'Select Section',
    selectRowPrefix: 'Select row',
    selectRowShort: 'All',
    resetRow: 'Reset Row',
    resetCellTitle: 'Revert to inherited',
    emptyTitle: 'No screens match the current filter.',
  },
} as const;

/**
 * Clean Option-1 card-and-chip permissions board.
 * Renders each group as a collapsible card and each screen with ONLY its applicable
 * permission pills—eliminating wide tables, horizontal page overflow, and empty '—' cells.
 */
export function PermissionsCardsBoard({
  lang,
  disabled = false,
  screens,
  actionColumns,
  showOverridesFilter = false,
  onToggleItem,
  onResetItem,
  onToggleScreen,
  onResetScreen,
  onToggleGroup,
  onToggleColumn,
  onToggleAll,
  extraToolbarActions,
}: PermissionsCardsBoardProps) {
  const c = COPY[lang];
  const [searchQuery, setSearchQuery] = useState('');
  const [onlyOverrides, setOnlyOverrides] = useState(false);
  const [collapsedGroups, setCollapsedGroups] = useState<Record<string, boolean>>({});
  const [showBulkActions, setShowBulkActions] = useState(false);

  const allItems = useMemo(() => screens.flatMap((s) => s.items), [screens]);
  const allChecked = allItems.length > 0 && allItems.every((item) => item.checked);

  const filteredScreens = useMemo(() => {
    const term = searchQuery.trim().toLowerCase();
    return screens.filter((scr) => {
      if (onlyOverrides && !scr.hasOverrides) {
        return false;
      }
      if (!term) return true;
      if (
        scr.title.toLowerCase().includes(term) ||
        scr.groupLabel.toLowerCase().includes(term)
      ) {
        return true;
      }
      return scr.items.some((item) => item.label.toLowerCase().includes(term));
    });
  }, [screens, searchQuery, onlyOverrides]);

  const groups = useMemo(() => {
    const list: Array<{
      groupId: string;
      groupLabel: string;
      screens: PermissionBoardScreen[];
    }> = [];
    for (const scr of filteredScreens) {
      let grp = list.find((g) => g.groupId === scr.groupId);
      if (!grp) {
        grp = { groupId: scr.groupId, groupLabel: scr.groupLabel, screens: [] };
        list.push(grp);
      }
      grp.screens.push(scr);
    }
    return list;
  }, [filteredScreens]);

  const toggleGroupCollapse = (groupId: string) => {
    setCollapsedGroups((prev) => ({ ...prev, [groupId]: !prev[groupId] }));
  };

  return (
    <div className="perm-workspace">
      <div className="perm-toolbar">
        <div className="perm-toolbar-main">
          <input
            type="search"
            className="text-input perm-search-input"
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
            placeholder={c.searchPlaceholder}
            aria-label={c.searchAria}
          />

          <label className="perm-toolbar-check">
            <input
              type="checkbox"
              className="perm-check"
              disabled={disabled || allItems.length === 0}
              aria-label={c.selectAll}
              checked={allChecked}
              onChange={(e) => onToggleAll(e.target.checked)}
            />
            <span>{c.selectAll}</span>
          </label>

          {showOverridesFilter && (
            <label className="perm-toolbar-check">
              <input
                type="checkbox"
                className="perm-check"
                checked={onlyOverrides}
                onChange={(e) => setOnlyOverrides(e.target.checked)}
              />
              <span>{c.overridesOnly}</span>
            </label>
          )}
        </div>

        <div className="perm-toolbar-actions">
          {onToggleColumn && actionColumns.length > 0 && (
            <Button
              type="button"
              variant="outline"
              size="sm"
              onClick={() => setShowBulkActions((v) => !v)}
            >
              {c.quickActionsTitle} {showBulkActions ? '▴' : '▾'}
            </Button>
          )}
          {extraToolbarActions}
        </div>
      </div>

      {onToggleColumn && actionColumns.length > 0 && (
        <div
          className="perm-bulk-actions-bar"
          style={{ display: showBulkActions ? 'flex' : 'none' }}
        >
          {actionColumns.map((col) => {
            const colItems = allItems.filter((item) => item.columnId === col.id);
            const colAllChecked =
              colItems.length > 0 && colItems.every((item) => item.checked);
            return (
              <label key={col.id} className="perm-bulk-pill">
                <input
                  type="checkbox"
                  className="perm-check"
                  disabled={disabled || colItems.length === 0}
                  aria-label={`${c.selectColPrefix}: ${col.label}`}
                  checked={colAllChecked}
                  onChange={(e) => onToggleColumn(col.id, e.target.checked)}
                />
                <span>{col.label}</span>
              </label>
            );
          })}
        </div>
      )}

      {groups.length === 0 ? (
        <div className="perm-empty-box">{c.emptyTitle}</div>
      ) : (
        <div className="perm-groups-stack">
          {groups.map((group) => {
            const groupItems = group.screens.flatMap((s) => s.items);
            const groupEnabledCount = groupItems.filter((i) => i.checked).length;
            const groupAllChecked =
              groupItems.length > 0 && groupItems.every((i) => i.checked);
            const isCollapsed = Boolean(collapsedGroups[group.groupId]);

            return (
              <section key={group.groupId} className="perm-group-card">
                <header className="perm-group-header">
                  <button
                    type="button"
                    className="perm-group-toggle-btn"
                    aria-expanded={!isCollapsed}
                    onClick={() => toggleGroupCollapse(group.groupId)}
                  >
                    <span className="perm-group-chevron">{isCollapsed ? '▸' : '▾'}</span>
                    <span className="perm-group-title heading-font">{group.groupLabel}</span>
                    <span className="perm-group-counter ltr-code">
                      {groupEnabledCount}/{groupItems.length}
                    </span>
                  </button>

                  <label className="perm-group-select-all">
                    <input
                      type="checkbox"
                      className="perm-check"
                      disabled={disabled || groupItems.length === 0}
                      aria-label={`${c.selectGroupPrefix}: ${group.groupLabel}`}
                      checked={groupAllChecked}
                      onChange={(e) => onToggleGroup(group.groupId, e.target.checked)}
                    />
                    <span>{c.selectGroupShort}</span>
                  </label>
                </header>

                {!isCollapsed && (
                  <div className="perm-screens-list">
                    {group.screens.map((scr) => {
                      const rowAllChecked =
                        scr.items.length > 0 && scr.items.every((i) => i.checked);
                      const rowEnabledCount = scr.items.filter((i) => i.checked).length;

                      return (
                        <div key={scr.id} className="perm-screen-row">
                          <div className="perm-screen-meta">
                            <div className="perm-screen-title-wrap">
                              <strong className="perm-screen-title">{scr.title}</strong>
                              <span className="perm-screen-count ltr-code">
                                {rowEnabledCount}/{scr.items.length}
                              </span>
                            </div>

                            <div className="perm-screen-row-controls">
                              <label className="perm-row-select-all">
                                <input
                                  type="checkbox"
                                  className="perm-check"
                                  disabled={disabled || scr.items.length === 0}
                                  aria-label={`${c.selectRowPrefix}: ${scr.title}`}
                                  checked={rowAllChecked}
                                  onChange={(e) => onToggleScreen(scr.id, e.target.checked)}
                                />
                                <span>{c.selectRowShort}</span>
                              </label>

                              {!disabled && scr.hasOverrides && onResetScreen && (
                                <Button
                                  type="button"
                                  variant="outline"
                                  size="sm"
                                  onClick={() => onResetScreen(scr.id)}
                                >
                                  {c.resetRow}
                                </Button>
                              )}
                            </div>
                          </div>

                          <div className="perm-chips-wrap">
                            {scr.items.map((item) => {
                              const chipClass = [
                                'perm-chip',
                                item.checked ? 'is-checked' : 'is-unchecked',
                                `perm-chip--${item.mode}`,
                              ].join(' ');

                              return (
                                <div key={item.id} className={chipClass} title={item.title}>
                                  <label className="perm-chip-label">
                                    <input
                                      type="checkbox"
                                      className="perm-check"
                                      disabled={disabled}
                                      aria-label={`${scr.title} - ${item.label}`}
                                      checked={item.checked}
                                      onChange={(e) => onToggleItem(item.id, e.target.checked)}
                                    />
                                    <span className="perm-chip-text">{item.label}</span>
                                  </label>

                                  {item.statusTag && (
                                    <span className="perm-chip-tag">{item.statusTag}</span>
                                  )}

                                  {!disabled && item.canReset && onResetItem && (
                                    <button
                                      type="button"
                                      className="perm-chip-reset"
                                      title={c.resetCellTitle}
                                      aria-label={`${c.resetCellTitle}: ${scr.title} - ${item.label}`}
                                      onClick={() => onResetItem(item.id)}
                                    >
                                      ↺
                                    </button>
                                  )}
                                </div>
                              );
                            })}
                          </div>
                        </div>
                      );
                    })}
                  </div>
                )}
              </section>
            );
          })}
        </div>
      )}
    </div>
  );
}
