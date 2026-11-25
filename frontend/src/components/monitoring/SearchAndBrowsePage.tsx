import { useCallback, useEffect, useState } from 'react';
import { ApiProblemError } from '../../api/authApi';
import {
  GlobalSearchResponse,
  GlobalSearchResultItem,
  searchAuthorizedResources,
} from '../../api/monitoringApi';
import { AppLanguage } from '../../constants/uiBaseline';
import {
  Button,
  ErrorState,
  LoadingState,
  SelectInput,
  StatusBadge,
  TextInput,
} from '../ui';

interface SearchAndBrowsePageProps {
  lang: AppLanguage;
  permissions: string[];
  onSessionExpired: () => void;
  onNavigateToScreen: (navId: string) => void;
}

const SEARCH_TEXT = {
  ar: {
    badge: 'البحث والاستعراض المصرح به · UI-18 / P05',
    title: 'البحث الشامل عن وثائق API والتكاملات',
    subtitle:
      'تُصفّى جميع النتائج داخل الخادم وفق صلاحيات الدور وصلاحيات كل وثيقة (doc.view) لمنع كشف أي مورد غير مصرح به.',
    noPermission: 'ليس لديك صلاحية البحث والاستعراض (search.view).',
    queryPlaceholder: 'ابحث باسم المزود أو المستهلك أو مجموعة API أو رمز التكامل أو الإصدار...',
    allTypes: 'كل النتائج (الوثائق + التكاملات)',
    typeDocument: 'وثائق API فقط',
    typeIntegration: 'التكاملات فقط',
    allStates: 'جميع الحالات',
    searchBtn: 'بحث',
    colKind: 'النوع',
    colTitle: 'العنوان / النظام',
    colGroup: 'المجموعة والرمز',
    colState: 'الحالة والنشر',
    colAction: 'الانتقال',
    openDocBtn: 'فتح قائمة الوثائق',
    openIntegBtn: 'فتح التكاملات',
    emptyMsg: 'لم يتم العثور على وثائق أو تكاملات مصرح بها تطابق البحث.',
    loadingText: 'جارٍ البحث...',
    loadErrorTitle: 'تعذر تنفيذ البحث',
    retryLabel: 'إعادة المحاولة',
    countLabel: (count: number) => `النتائج المصرح بها: ${count}`,
    publishedBadge: 'منشور نشط (PUBLISHED)',
    notPublished: 'غير منشور بعد',
  },
  en: {
    badge: 'Authorized Search & Browse · UI-18 / P05',
    title: 'Global Authorized Search',
    subtitle:
      'All results are strictly server-filtered by effective RBAC permissions and Document ACL (doc.view) to prevent unauthorized discovery.',
    noPermission: 'You lack permission to access global search (search.view).',
    queryPlaceholder: 'Search provider, consumer, API group, reference code, or version...',
    allTypes: 'All Types (Documents + Integrations)',
    typeDocument: 'API Documents Only',
    typeIntegration: 'Integrations Only',
    allStates: 'All States',
    searchBtn: 'Search',
    colKind: 'Kind',
    colTitle: 'Title & Parties',
    colGroup: 'Group & Reference',
    colState: 'State & Publication',
    colAction: 'Navigate',
    openDocBtn: 'Open Documents',
    openIntegBtn: 'Open Integrations',
    emptyMsg: 'No authorized documents or integrations match your search criteria.',
    loadingText: 'Searching...',
    loadErrorTitle: 'Search Failed',
    retryLabel: 'Retry',
    countLabel: (count: number) => `Authorized Matches: ${count}`,
    publishedBadge: 'PUBLISHED Active',
    notPublished: 'Not Published Yet',
  },
} as const;

export function SearchAndBrowsePage({
  lang,
  permissions,
  onSessionExpired,
  onNavigateToScreen,
}: SearchAndBrowsePageProps) {
  const txt = SEARCH_TEXT[lang];
  const canView = permissions.includes('search.view');

  const [query, setQuery] = useState('');
  const [typeFilter, setTypeFilter] = useState('ALL');
  const [stateFilter, setStateFilter] = useState('');
  const [data, setData] = useState<GlobalSearchResponse | null>(null);
  const [loading, setLoading] = useState(false);
  const [errorMsg, setErrorMsg] = useState<string | null>(null);

  const runSearch = useCallback(
    async (q = query, t = typeFilter, s = stateFilter) => {
      if (!canView) return;
      setLoading(true);
      setErrorMsg(null);
      try {
        const res = await searchAuthorizedResources({ query: q, type: t, state: s }, lang);
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
    [canView, lang, onSessionExpired, query, stateFilter, txt.loadErrorTitle, typeFilter]
  );

  useEffect(() => {
    void runSearch('', 'ALL', '');
  }, [canView, lang]); // eslint-disable-line react-hooks/exhaustive-deps

  if (!canView) {
    return (
      <div className="content-card">
        <ErrorState title={txt.badge} message={txt.noPermission} />
      </div>
    );
  }

  const handleOpenItem = (item: GlobalSearchResultItem) => {
    if (item.resultKind === 'INTEGRATION') {
      onNavigateToScreen('UI-22');
    } else {
      onNavigateToScreen('UI-21');
    }
  };

  return (
    <div className="module-stack" data-testid="search-browse-page">
      <div className="content-card">
        <div className="card-header-row">
          <div>
            <StatusBadge variant="info">{txt.badge}</StatusBadge>
            <h2 className="section-title" style={{ marginTop: 8 }}>
              {txt.title}
            </h2>
            <p className="section-subtitle">{txt.subtitle}</p>
          </div>
          {data && <StatusBadge variant="neutral">{txt.countLabel(data.totalCount)}</StatusBadge>}
        </div>

        <form
          className="monitoring-filter-grid"
          style={{ marginTop: 14 }}
          onSubmit={(e) => {
            e.preventDefault();
            void runSearch();
          }}
        >
          <TextInput
            value={query}
            placeholder={txt.queryPlaceholder}
            onChange={(e) => setQuery(e.target.value)}
            aria-label={txt.queryPlaceholder}
          />
          <SelectInput
            value={typeFilter}
            options={[
              { value: 'ALL', label: txt.allTypes },
              { value: 'DOCUMENT', label: txt.typeDocument },
              { value: 'INTEGRATION', label: txt.typeIntegration },
            ]}
            onChange={(e) => setTypeFilter(e.target.value)}
            aria-label={txt.colKind}
          />
          <SelectInput
            value={stateFilter}
            placeholder={txt.allStates}
            options={[
              { value: 'ACTIVE', label: 'ACTIVE' },
              { value: 'DRAFT', label: 'DRAFT' },
              { value: 'PUBLISHED', label: 'PUBLISHED' },
            ]}
            onChange={(e) => setStateFilter(e.target.value)}
            aria-label={txt.colState}
          />
          <div className="monitoring-filter-actions">
            <Button type="submit" variant="primary" size="sm">
              {txt.searchBtn}
            </Button>
          </div>
        </form>
      </div>

      <div className="content-card">
        {loading && !data ? (
          <LoadingState title={txt.loadingText} />
        ) : errorMsg ? (
          <ErrorState
            title={txt.loadErrorTitle}
            message={errorMsg}
            retryLabel={txt.retryLabel}
            onRetry={() => void runSearch()}
          />
        ) : !data || data.items.length === 0 ? (
          <div className="empty-state-box">{txt.emptyMsg}</div>
        ) : (
          <div className="data-table-wrap">
            <table className="data-table">
              <thead>
                <tr>
                  <th>{txt.colKind}</th>
                  <th>{txt.colTitle}</th>
                  <th>{txt.colGroup}</th>
                  <th>{txt.colState}</th>
                  <th>{txt.colAction}</th>
                </tr>
              </thead>
              <tbody>
                {data.items.map((item) => (
                  <tr key={`${item.resultKind}-${item.id}`}>
                    <td>
                      <StatusBadge variant={item.resultKind === 'INTEGRATION' ? 'info' : 'neutral'}>
                        {item.resultKind}
                      </StatusBadge>
                    </td>
                    <td>
                      <strong>{item.title}</strong>
                      <div style={{ fontSize: 12, color: 'var(--text-muted)' }}>
                        {item.providerName} {'->'} {item.consumerName}
                      </div>
                    </td>
                    <td>
                      <div>{item.apiGroupName}</div>
                      {item.referenceCode && (
                        <code className="ltr-code">{item.referenceCode}</code>
                      )}
                      {item.latestVersionLabel && (
                        <span style={{ marginInlineStart: 8, fontSize: 11.5 }}>
                          v{item.latestVersionLabel}
                        </span>
                      )}
                    </td>
                    <td>
                      <div className="status-stack">
                        <StatusBadge variant={item.hasActivePublication ? 'success' : 'warning'}>
                          {item.hasActivePublication ? txt.publishedBadge : txt.notPublished}
                        </StatusBadge>
                        <code className="ltr-code" style={{ fontSize: 11 }}>
                          {item.state}
                        </code>
                      </div>
                    </td>
                    <td>
                      <Button
                        variant="outline"
                        size="sm"
                        onClick={() => handleOpenItem(item)}
                      >
                        {item.resultKind === 'INTEGRATION' ? txt.openIntegBtn : txt.openDocBtn}
                      </Button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  );
}
