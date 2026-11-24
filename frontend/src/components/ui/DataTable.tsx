import { ReactNode } from 'react';
import { Button, SelectInput, SelectOption, TextInput } from './FormControls';
import { EmptyState, LoadingState } from './StateViews';

export interface SearchFilterBarProps {
  searchId?: string;
  searchValue: string;
  searchPlaceholder: string;
  searchAriaLabel: string;
  onSearchChange: (value: string) => void;
  filterId?: string;
  filterValue?: string;
  filterAriaLabel?: string;
  filterPlaceholder?: string;
  filterOptions?: SelectOption[];
  onFilterChange?: (value: string) => void;
  actions?: ReactNode;
}

export function SearchFilterBar({
  searchId = 'table-search-input',
  searchValue,
  searchPlaceholder,
  searchAriaLabel,
  onSearchChange,
  filterId = 'table-filter-select',
  filterValue,
  filterAriaLabel,
  filterPlaceholder,
  filterOptions,
  onFilterChange,
  actions,
}: SearchFilterBarProps) {
  return (
    <div className="filter-bar">
      <div className="filter-search-wrap">
        <TextInput
          id={searchId}
          type="search"
          value={searchValue}
          aria-label={searchAriaLabel}
          placeholder={searchPlaceholder}
          onChange={(e) => onSearchChange(e.target.value)}
        />
      </div>
      {filterOptions && onFilterChange && (
        <div className="filter-select-wrap">
          <SelectInput
            id={filterId}
            value={filterValue ?? ''}
            aria-label={filterAriaLabel || filterPlaceholder || 'Filter'}
            placeholder={filterPlaceholder}
            options={filterOptions}
            onChange={(e) => onFilterChange(e.target.value)}
          />
        </div>
      )}
      {actions && <div className="filter-actions-wrap">{actions}</div>}
    </div>
  );
}

export interface DataTableColumn<TRow> {
  key: string;
  header: string;
  render: (row: TRow) => ReactNode;
  align?: 'start' | 'center' | 'end';
}

export interface DataTableProps<TRow> {
  caption?: string;
  columns: DataTableColumn<TRow>[];
  rows: TRow[];
  getRowKey: (row: TRow) => string;
  isLoading?: boolean;
  loadingTitle?: string;
  emptyTitle: string;
  emptyDescription?: string;
}

export function DataTable<TRow>({
  caption,
  columns,
  rows,
  getRowKey,
  isLoading = false,
  loadingTitle = 'Loading...',
  emptyTitle,
  emptyDescription,
}: DataTableProps<TRow>) {
  if (isLoading) {
    return <LoadingState title={loadingTitle} />;
  }

  if (rows.length === 0) {
    return <EmptyState title={emptyTitle} description={emptyDescription} />;
  }

  return (
    <div className="table-scroll">
      <table className="data-table">
        {caption && <caption className="sr-only">{caption}</caption>}
        <thead>
          <tr>
            {columns.map((col) => (
              <th key={col.key} className={col.align ? `align-${col.align}` : ''}>
                {col.header}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => (
            <tr key={getRowKey(row)}>
              {columns.map((col) => (
                <td key={col.key} className={col.align ? `align-${col.align}` : ''}>
                  {col.render(row)}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

export interface PaginationBarProps {
  currentPage: number;
  totalPages: number;
  totalItems: number;
  pageLabel: string;
  previousLabel: string;
  nextLabel: string;
  onPageChange: (page: number) => void;
}

export function PaginationBar({
  currentPage,
  totalPages,
  totalItems,
  pageLabel,
  previousLabel,
  nextLabel,
  onPageChange,
}: PaginationBarProps) {
  const safeTotalPages = Math.max(1, totalPages);

  return (
    <nav className="pagination-bar" aria-label="Pagination">
      <div className="pagination-summary">
        <span>{pageLabel}</span>
        <span className="pagination-total">({totalItems})</span>
      </div>
      <div className="pagination-controls">
        <Button
          variant="outline"
          size="sm"
          disabled={currentPage <= 1}
          onClick={() => onPageChange(currentPage - 1)}
        >
          {previousLabel}
        </Button>
        <Button
          variant="outline"
          size="sm"
          disabled={currentPage >= safeTotalPages}
          onClick={() => onPageChange(currentPage + 1)}
        >
          {nextLabel}
        </Button>
      </div>
    </nav>
  );
}

