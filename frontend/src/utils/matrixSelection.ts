import {
  MatrixColumnDef,
  MatrixRowDef,
  isCellApplicable,
  makeMatrixCellKey,
} from '../components/ui/SelectionMatrix';

export function toggleMatrixCellSelection(
  previous: ReadonlySet<string>,
  rowId: string,
  colId: string
): Set<string> {
  const next = new Set(previous);
  const key = makeMatrixCellKey(rowId, colId);
  if (next.has(key)) next.delete(key);
  else next.add(key);
  return next;
}

export function toggleMatrixRowSelection(
  previous: ReadonlySet<string>,
  rows: MatrixRowDef[],
  columns: MatrixColumnDef[],
  rowId: string,
  checked: boolean
): Set<string> {
  const next = new Set(previous);
  const row = rows.find((r) => r.id === rowId);
  if (!row) return next;
  for (const col of columns) {
    if (isCellApplicable(row, col.id)) {
      const key = makeMatrixCellKey(rowId, col.id);
      if (checked) next.add(key);
      else next.delete(key);
    }
  }
  return next;
}

export function toggleMatrixColumnSelection(
  previous: ReadonlySet<string>,
  rows: MatrixRowDef[],
  colId: string,
  checked: boolean
): Set<string> {
  const next = new Set(previous);
  for (const row of rows) {
    if (isCellApplicable(row, colId)) {
      const key = makeMatrixCellKey(row.id, colId);
      if (checked) next.add(key);
      else next.delete(key);
    }
  }
  return next;
}

export function toggleMatrixGroupSelection(
  previous: ReadonlySet<string>,
  rows: MatrixRowDef[],
  columns: MatrixColumnDef[],
  groupId: string,
  checked: boolean
): Set<string> {
  const next = new Set(previous);
  for (const row of rows) {
    if (row.groupId !== groupId) continue;
    for (const col of columns) {
      if (isCellApplicable(row, col.id)) {
        const key = makeMatrixCellKey(row.id, col.id);
        if (checked) next.add(key);
        else next.delete(key);
      }
    }
  }
  return next;
}

export function toggleMatrixAllSelection(
  rows: MatrixRowDef[],
  columns: MatrixColumnDef[],
  checked: boolean
): Set<string> {
  const next = new Set<string>();
  if (!checked) return next;
  for (const row of rows) {
    for (const col of columns) {
      if (isCellApplicable(row, col.id)) {
        next.add(makeMatrixCellKey(row.id, col.id));
      }
    }
  }
  return next;
}

