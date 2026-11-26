import { useEffect, useRef } from 'react';

export interface PermissionMatrixCell {
  id: string;
  checked: boolean;
  disabled?: boolean;
  source?: string;
  explicit?: boolean;
}
export interface PermissionMatrixRow {
  id: string;
  title: string;
  subtitle?: string;
  cells: Record<string, PermissionMatrixCell | undefined>;
}
export interface PermissionMatrixColumn { id: string; label: string }

function SelectionCheckbox({ label, cells, disabled, onChange }: {
  label: string; cells: PermissionMatrixCell[]; disabled: boolean; onChange: (checked: boolean) => void;
}) {
  const ref = useRef<HTMLInputElement>(null);
  const count = cells.filter(c => c.checked).length;
  useEffect(() => { if (ref.current) ref.current.indeterminate = count > 0 && count < cells.length; }, [count, cells.length]);
  return <input ref={ref} type="checkbox" className="perm-check" aria-label={label}
    disabled={disabled || cells.length === 0} checked={cells.length > 0 && count === cells.length}
    onChange={e => onChange(e.target.checked)} />;
}

export function PermissionMatrixTable({ rows, columns, rowLabel, disabled, labels, onChange, onReset }: {
  rows: PermissionMatrixRow[]; columns: PermissionMatrixColumn[]; rowLabel: string; disabled: boolean;
  labels: { rowAll: string; columnAll: string; reset: string; empty: string };
  onChange: (ids: string[], checked: boolean) => void; onReset?: (id: string) => void;
}) {
  const editable = (cells: (PermissionMatrixCell | undefined)[]) => cells.filter((c): c is PermissionMatrixCell => !!c && !c.disabled);
  if (!rows.length) return <p className="permissions-empty" role="status">{labels.empty}</p>;
  return <div className="permissions-table-scroll" role="region" aria-label={rowLabel} tabIndex={0}>
    <table className="data-table permissions-grid">
      <thead><tr><th scope="col" className="permissions-sticky">{rowLabel}</th>
        {columns.map(col => { const cells = editable(rows.map(r => r.cells[col.id])); return <th scope="col" key={col.id}>
          <span className="permissions-column-label">{col.label}</span>
          <SelectionCheckbox label={`${labels.columnAll}: ${col.label}`} cells={cells} disabled={disabled}
            onChange={checked => onChange(cells.map(c => c.id), checked)} />
        </th>; })}</tr></thead>
      <tbody>{rows.map(row => { const cells = editable(columns.map(c => row.cells[c.id])); return <tr key={row.id}>
        <th scope="row" className="permissions-sticky"><div className="permissions-row-heading">
          <SelectionCheckbox label={`${labels.rowAll}: ${row.title}`} cells={cells} disabled={disabled}
            onChange={checked => onChange(cells.map(c => c.id), checked)} />
          <span><span className="permissions-row-title">{row.title}</span>{row.subtitle && <small>{row.subtitle}</small>}</span>
        </div></th>
        {columns.map(col => { const cell = row.cells[col.id]; return <td key={col.id}>{cell ? <div className={`permissions-cell${cell.explicit ? ' permissions-cell--explicit' : ''}`}>
          <input type="checkbox" className="perm-check" checked={cell.checked} disabled={disabled || cell.disabled}
            aria-label={`${row.title} — ${col.label}`} onChange={e => onChange([cell.id], e.target.checked)} />
          {cell.source && <small>{cell.source}</small>}
          {cell.explicit && onReset && <button type="button" className="permissions-reset" disabled={disabled || cell.disabled}
            aria-label={`${labels.reset}: ${row.title} — ${col.label}`} onClick={() => onReset(cell.id)}>{labels.reset}</button>}
        </div> : <span className="permissions-na" aria-label="—">—</span>}</td>; })}
      </tr>; })}</tbody>
    </table>
  </div>;
}
