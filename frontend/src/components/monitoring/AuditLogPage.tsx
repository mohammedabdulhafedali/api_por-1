import { useCallback, useEffect, useState } from 'react';
import { ApiProblemError } from '../../api/authApi';
import {
  AuditEventListItem,
  getAuditEvents,
  PagedAuditEventsResponse,
} from '../../api/monitoringApi';
import { AppLanguage } from '../../constants/uiBaseline';
import {
  Button,
  CentralModalDialog,
  ErrorState,
  LoadingState,
  SelectInput,
  StatusBadge,
  TextInput,
} from '../ui';

interface AuditLogPageProps {
  lang: AppLanguage;
  permissions: string[];
  onSessionExpired: () => void;
}

const AUDIT_TEXT = {
  ar: {
    badge: 'سجل التدقيق',
    title: 'سجل التدقيق الإلحاقي للأمان والعمليات',
    subtitle:
      'استعراض زمني لجميع أحداث المصادقة، الصلاحيات، التغييرات، الاعتمادات، والنشر. السجل للقراءة فقط ولا يقبل التعديل أو الحذف.',
    noPermission: 'ليس لديك صلاحية الاطلاع على سجل التدقيق (audit.view).',
    searchPlaceholder: 'ابحث بالعملية أو المستخدم أو نوع المورد أو التفاصيل...',
    allOperations: 'جميع العمليات',
    allResources: 'جميع أنواع الموارد',
    allResults: 'جميع النتائج',
    applyBtn: 'تصفية',
    resetBtn: 'إعادة ضبط',
    colTime: 'الوقت (UTC / المحلي)',
    colActor: 'المنفذ',
    colOperation: 'العملية',
    colResource: 'المورد',
    colResult: 'النتيجة',
    colDetails: 'التفاصيل',
    viewJsonBtn: 'عرض JSON',
    systemActor: 'النظام / غير مصادق',
    emptyTitle: 'لا توجد أحداث تدقيق مطابقة لمعايير البحث الحالية.',
    modalTitle: 'تفاصيل حدث التدقيق',
    closeBtn: 'إغلاق',
    prevPage: 'السابق',
    nextPage: 'التالي',
    loadingText: 'جارٍ تحميل سجل التدقيق...',
    loadErrorTitle: 'تعذر تحميل سجل التدقيق',
    retryLabel: 'إعادة المحاولة',
    pageSummary: (page: number, totalPages: number, totalCount: number) =>
      `صفحة ${page} من ${totalPages} · إجمالي السجلات: ${totalCount}`,
  },
  en: {
    badge: 'Audit Log',
    title: 'Security & Operations Audit Log',
    subtitle:
      'Chronological ledger of authentication, RBAC/ACL grants, approvals, file scans, and publishing operations. Read-only append-only ledger.',
    noPermission: 'You lack permission to view audit logs (audit.view).',
    searchPlaceholder: 'Search operation, actor, resource kind, or JSON details...',
    allOperations: 'All Operations',
    allResources: 'All Resource Kinds',
    allResults: 'All Results',
    applyBtn: 'Filter',
    resetBtn: 'Reset',
    colTime: 'Recorded At',
    colActor: 'Actor',
    colOperation: 'Operation',
    colResource: 'Resource',
    colResult: 'Result',
    colDetails: 'Details',
    viewJsonBtn: 'Inspect JSON',
    systemActor: 'System / Unauthenticated',
    emptyTitle: 'No audit events match your active filters.',
    modalTitle: 'Audit Event Details',
    closeBtn: 'Close',
    prevPage: 'Previous',
    nextPage: 'Next',
    loadingText: 'Loading audit ledger...',
    loadErrorTitle: 'Unable to Load Audit Log',
    retryLabel: 'Retry',
    pageSummary: (page: number, totalPages: number, totalCount: number) =>
      `Page ${page} of ${totalPages} · Total Events: ${totalCount}`,
  },
} as const;

export function AuditLogPage({ lang, permissions, onSessionExpired }: AuditLogPageProps) {
  const txt = AUDIT_TEXT[lang];
  const canView = permissions.includes('audit.view');

  const [data, setData] = useState<PagedAuditEventsResponse | null>(null);
  const [loading, setLoading] = useState(false);
  const [errorMsg, setErrorMsg] = useState<string | null>(null);
  const [page, setPage] = useState(1);
  const [search, setSearch] = useState('');
  const [operation, setOperation] = useState('');
  const [resourceKind, setResourceKind] = useState('');
  const [resultFilter, setResultFilter] = useState('');
  const [selectedEvent, setSelectedEvent] = useState<AuditEventListItem | null>(null);

  const loadEvents = useCallback(
    async (
      targetPage: number,
      overrides?: { search?: string; op?: string; rk?: string; res?: string }
    ) => {
      if (!canView) return;
      setLoading(true);
      setErrorMsg(null);
      try {
        const res = await getAuditEvents(
          {
            page: targetPage,
            pageSize: 20,
            search: overrides?.search ?? search,
            operation: overrides?.op ?? operation,
            resourceKind: overrides?.rk ?? resourceKind,
            result: overrides?.res ?? resultFilter,
          },
          lang
        );
        setData(res);
      } catch (err) {
        if ((err as ApiProblemError)?.status === 401) {
          onSessionExpired();
          return;
        }
        setErrorMsg((err as Error).message || txt.loadErrorTitle);
      } finally {
        setLoading(false);
      }
    },
    [canView, lang, onSessionExpired, operation, resourceKind, resultFilter, search, txt.loadErrorTitle]
  );

  useEffect(() => {
    void loadEvents(page);
  }, [page, canView, lang]); // eslint-disable-line react-hooks/exhaustive-deps

  if (!canView) {
    return (
      <div className="content-card">
        <ErrorState title={txt.badge} message={txt.noPermission} />
      </div>
    );
  }

  const totalPages = data ? Math.max(1, Math.ceil(data.totalCount / data.pageSize)) : 1;

  const formatJson = (raw?: string | null) => {
    if (!raw) return '{}';
    try {
      return JSON.stringify(JSON.parse(raw), null, 2);
    } catch {
      return raw;
    }
  };

  return (
    <div className="module-stack" data-testid="audit-log-page">
      <section className="surface-card panel clean-table-panel">
        <div className="monitoring-filter-grid" style={{ marginBottom: 14 }}>
          <TextInput
            value={search}
            placeholder={txt.searchPlaceholder}
            onChange={(e) => setSearch(e.target.value)}
            aria-label={txt.searchPlaceholder}
          />
          <SelectInput
            value={operation}
            placeholder={txt.allOperations}
            options={(data?.distinctOperations ?? []).map((op) => ({ value: op, label: op }))}
            onChange={(e) => setOperation(e.target.value)}
            aria-label={txt.colOperation}
          />
          <SelectInput
            value={resourceKind}
            placeholder={txt.allResources}
            options={(data?.distinctResourceKinds ?? []).map((rk) => ({ value: rk, label: rk }))}
            onChange={(e) => setResourceKind(e.target.value)}
            aria-label={txt.colResource}
          />
          <SelectInput
            value={resultFilter}
            placeholder={txt.allResults}
            options={[
              { value: 'SUCCESS', label: 'SUCCESS' },
              { value: 'FORBIDDEN', label: 'FORBIDDEN' },
              { value: 'DENIED', label: 'DENIED' },
              { value: 'FAILED', label: 'FAILED' },
            ]}
            onChange={(e) => setResultFilter(e.target.value)}
            aria-label={txt.colResult}
          />
          <div className="monitoring-filter-actions">
            <Button
              variant="primary"
              size="sm"
              onClick={() => {
                setPage(1);
                void loadEvents(1);
              }}
            >
              {txt.applyBtn}
            </Button>
            <Button
              variant="outline"
              size="sm"
              onClick={() => {
                setSearch('');
                setOperation('');
                setResourceKind('');
                setResultFilter('');
                setPage(1);
                void loadEvents(1, { search: '', op: '', rk: '', res: '' });
              }}
            >
              {txt.resetBtn}
            </Button>
          </div>
        </div>

        {loading && !data ? (
          <LoadingState title={txt.loadingText} />
        ) : errorMsg ? (
          <ErrorState
            title={txt.loadErrorTitle}
            message={errorMsg}
            retryLabel={txt.retryLabel}
            onRetry={() => void loadEvents(page)}
          />
        ) : !data || data.items.length === 0 ? (
          <div className="empty-state-box">{txt.emptyTitle}</div>
        ) : (
          <>
            <div className="data-table-wrap">
              <table className="data-table">
                <thead>
                  <tr>
                    <th>{txt.colTime}</th>
                    <th>{txt.colActor}</th>
                    <th>{txt.colOperation}</th>
                    <th>{txt.colResource}</th>
                    <th>{txt.colResult}</th>
                    <th>{txt.colDetails}</th>
                  </tr>
                </thead>
                <tbody>
                  {data.items.map((item) => (
                    <tr key={item.id}>
                      <td>
                        <code className="ltr-code">{new Date(item.recordedAt).toLocaleString()}</code>
                      </td>
                      <td>
                        {item.actorName ? (
                          <div>
                            <strong>{item.actorName}</strong>
                            <div style={{ fontSize: 11.5, color: 'var(--text-muted)' }}>
                              <code className="ltr-code">{item.actorEmail}</code>
                            </div>
                          </div>
                        ) : (
                          <span style={{ color: 'var(--text-muted)' }}>{txt.systemActor}</span>
                        )}
                      </td>
                      <td>
                        <code className="ltr-code">{item.operation}</code>
                      </td>
                      <td>
                        <span>{item.resourceKind}</span>
                        {item.resourceId && (
                          <div style={{ fontSize: 11, color: 'var(--text-muted)' }}>
                            <code className="ltr-code">{item.resourceId}</code>
                          </div>
                        )}
                      </td>
                      <td>
                        <StatusBadge
                          variant={
                            item.result === 'SUCCESS'
                              ? 'success'
                              : item.result === 'DENIED' || item.result === 'FORBIDDEN'
                                ? 'warning'
                                : 'danger'
                          }
                        >
                          {item.result}
                        </StatusBadge>
                      </td>
                      <td>
                        <Button
                          variant="outline"
                          size="sm"
                          onClick={() => setSelectedEvent(item)}
                        >
                          {txt.viewJsonBtn}
                        </Button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            <div className="pagination-bar" style={{ marginTop: 14 }}>
              <span className="pagination-info">
                {txt.pageSummary(data.page, totalPages, data.totalCount)}
              </span>
              <div className="pagination-controls">
                <Button
                  variant="outline"
                  size="sm"
                  disabled={data.page <= 1}
                  onClick={() => setPage((p) => Math.max(1, p - 1))}
                >
                  {txt.prevPage}
                </Button>
                <Button
                  variant="outline"
                  size="sm"
                  disabled={data.page >= totalPages}
                  onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
                >
                  {txt.nextPage}
                </Button>
              </div>
            </div>
          </>
        )}
      </section>

      {selectedEvent && (
        <CentralModalDialog
          isOpen
          variant="info"
          title={txt.modalTitle}
          message={`${selectedEvent.operation} · ${selectedEvent.resourceKind}\n\n${formatJson(selectedEvent.changeSummaryJson)}`}
          confirmLabel={txt.closeBtn}
          onConfirm={() => setSelectedEvent(null)}
          onCancel={() => setSelectedEvent(null)}
        />
      )}
    </div>
  );
}
