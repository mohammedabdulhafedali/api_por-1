import { Fragment } from 'react';

export interface MatrixColumnDef {
  id: string;
  label: string;
}

export interface MatrixRowDef {
  id: string;
  groupId: string;
  groupLabel: string;
  title: string;
  technicalPath?: string;
  applicableColumnIds?: string[];
}

export interface SelectionMatrixProps {
  rowHeaderLabel: string;
  selectAllLabel: string;
  selectGroupLabel: string;
  selectRowAriaPrefix: string;
  selectColAriaPrefix: string;
  notApplicableSymbol?: string;
  columns: MatrixColumnDef[];
  rows: MatrixRowDef[];
  selectedCells: ReadonlySet<string>;
  onToggleCell: (rowId: string, colId: string) => void;
  onToggleRow: (rowId: string, checked: boolean) => void;
  onToggleColumn: (colId: string, checked: boolean) => void;
  onToggleGroup: (groupId: string, checked: boolean) => void;
  onToggleAll: (checked: boolean) => void;
}

export function makeMatrixCellKey(rowId: string, colId: string): string {
  return `${rowId}::${colId}`;
}

export function isCellApplicable(row: MatrixRowDef, colId: string): boolean {
  return !row.applicableColumnIds || row.applicableColumnIds.includes(colId);
}

/**
 * Reusable selection matrix component with horizontal scrolling, sticky first column,
 * and bulk row / column / section / select-all checkboxes (02_UX_UI_v1.2.md UI-25/UI-15A/UI-21A).
 * Purely presentational in F08 — contains no business permission rules or mock authorization decisions.
 */
export function SelectionMatrix({
  rowHeaderLabel,
  selectAllLabel,
  selectGroupLabel,
  selectRowAriaPrefix,
  selectColAriaPrefix,
  notApplicableSymbol = '—',
  columns,
  rows,
  selectedCells,
  onToggleCell,
  onToggleRow,
  onToggleColumn,
  onToggleGroup,
  onToggleAll,
}: SelectionMatrixProps) {
  const allApplicableKeys: string[] = [];
  for (const row of rows) {
    for (const col of columns) {
      if (isCellApplicable(row, col.id)) {
        allApplicableKeys.push(makeMatrixCellKey(row.id, col.id));
      }
    }
  }

  const allSelected =
    allApplicableKeys.length > 0 && allApplicableKeys.every((key) => selectedCells.has(key));

  // Group rows while preserving order
  const groups: Array<{ groupId: string; groupLabel: string; items: MatrixRowDef[] }> = [];
  for (const row of rows) {
    let existing = groups.find((g) => g.groupId === row.groupId);
    if (!existing) {
      existing = { groupId: row.groupId, groupLabel: row.groupLabel, items: [] };
      groups.push(existing);
    }
    existing.items.push(row);
  }

  return (
    <div className="permission-matrix-scroll matrix-scroll-container">
      <table className="permission-matrix selection-matrix">
        <thead>
          <tr>
            <th className="perm-screen matrix-sticky-col matrix-corner-header">
              <div className="check-everything matrix-header-row-between">
                <span>{rowHeaderLabel}</span>
                <label className="matrix-inline-check">
                  <input
                    type="checkbox"
                    className="perm-check matrix-checkbox"
                    aria-label={selectAllLabel}
                    checked={allSelected}
                    onChange={(e) => onToggleAll(e.target.checked)}
                  />
                  <span>{selectAllLabel}</span>
                </label>
              </div>
            </th>
            {columns.map((col) => {
              const colApplicableKeys = rows
                .filter((r) => isCellApplicable(r, col.id))
                .map((r) => makeMatrixCellKey(r.id, col.id));
              const colAllSelected =
                colApplicableKeys.length > 0 &&
                colApplicableKeys.every((key) => selectedCells.has(key));

              return (
                <th key={col.id} className="matrix-col-header">
                  <div className="matrix-col-header-stack">
                    <span className="col-title matrix-col-title">{col.label}</span>
                    <input
                      type="checkbox"
                      className="perm-check matrix-checkbox"
                      aria-label={`${selectColAriaPrefix}: ${col.label}`}
                      checked={colAllSelected}
                      onChange={(e) => onToggleColumn(col.id, e.target.checked)}
                    />
                  </div>
                </th>
              );
            })}
          </tr>
        </thead>
        <tbody>
          {groups.map((group) => {
            const groupApplicableKeys: string[] = [];
            for (const row of group.items) {
              for (const col of columns) {
                if (isCellApplicable(row, col.id)) {
                  groupApplicableKeys.push(makeMatrixCellKey(row.id, col.id));
                }
              }
            }
            const groupAllSelected =
              groupApplicableKeys.length > 0 &&
              groupApplicableKeys.every((key) => selectedCells.has(key));

            return (
              <Fragment key={group.groupId}>
                <tr className="perm-group matrix-group-row">
                  <td colSpan={columns.length + 1}>
                    <div className="matrix-group-bar">
                      <span className="section-name heading-font">{group.groupLabel}</span>
                      <label className="group-choice matrix-inline-check">
                        <input
                          type="checkbox"
                          className="perm-check matrix-checkbox"
                          aria-label={`${selectGroupLabel}: ${group.groupLabel}`}
                          checked={groupAllSelected}
                          onChange={(e) => onToggleGroup(group.groupId, e.target.checked)}
                        />
                        <span>{selectGroupLabel}</span>
                      </label>
                    </div>
                  </td>
                </tr>
                {group.items.map((row) => {
                  const rowApplicableKeys = columns
                    .filter((c) => isCellApplicable(row, c.id))
                    .map((c) => makeMatrixCellKey(row.id, c.id));
                  const rowAllSelected =
                    rowApplicableKeys.length > 0 &&
                    rowApplicableKeys.every((key) => selectedCells.has(key));

                  return (
                    <tr key={row.id}>
                      <td className="perm-screen matrix-sticky-col matrix-row-header">
                        <div className="all-row-toggle matrix-header-row-between">
                          <div className="row-title">
                            <b className="matrix-row-title">{row.title}</b>
                            {row.technicalPath && (
                              <span className="perm-path matrix-row-path ltr-code">{row.technicalPath}</span>
                            )}
                          </div>
                          <input
                            type="checkbox"
                            className="perm-check matrix-checkbox"
                            aria-label={`${selectRowAriaPrefix}: ${row.title}`}
                            checked={rowAllSelected}
                            onChange={(e) => onToggleRow(row.id, e.target.checked)}
                          />
                        </div>
                      </td>
                      {columns.map((col) => {
                        const applicable = isCellApplicable(row, col.id);
                        if (!applicable) {
                          return (
                            <td key={col.id} className="not-applicable matrix-cell-na" aria-disabled="true">
                              {notApplicableSymbol}
                            </td>
                          );
                        }
                        const cellKey = makeMatrixCellKey(row.id, col.id);
                        const checked = selectedCells.has(cellKey);
                        return (
                          <td key={col.id} className="matrix-cell">
                            <input
                              type="checkbox"
                              className="perm-check matrix-checkbox"
                              aria-label={`${row.title} - ${col.label}`}
                              checked={checked}
                              onChange={() => onToggleCell(row.id, col.id)}
                            />
                          </td>
                        );
                      })}
                    </tr>
                  );
                })}
              </Fragment>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}

