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
  ReferenceIcon,
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
      <section className="panel content-card">
        <div className="panel-head">
          <div>
            <span className="sr-only">
              <StatusBadge variant="info">{txt.badge}</StatusBadge>
            </span>
            <h2 className="panel-title section-title">{txt.title}</h2>
            <p className="panel-desc section-subtitle">{txt.subtitle}</p>
          </div>
          {data && <StatusBadge variant="neutral">{txt.countLabel(data.totalCount)}</StatusBadge>}
        </div>

        <form
          className="filter-row monitoring-filter-grid"
          onSubmit={(e) => {
            e.preventDefault();
            void runSearch();
          }}
        >
          <TextInput
            className="searchbar"
            value={query}
            placeholder={txt.queryPlaceholder}
            onChange={(e) => setQuery(e.target.value)}
            aria-label={txt.queryPlaceholder}
          />
          <SelectInput
            className="control"
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
            className="control"
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
          <div className="empty-state-box empty">
            <ReferenceIcon name="search" size={26} />
            <div>{txt.emptyMsg}</div>
          </div>
        ) : (
          <div className="stack">
            {data.items.map((item) => (
              <div key={`${item.resultKind}-${item.id}`} className="item-card">
                <div>
                  <div className="row wrap" style={{ marginBottom: 6 }}>
                    <StatusBadge variant={item.resultKind === 'INTEGRATION' ? 'info' : 'neutral'}>
                      {item.resultKind}
                    </StatusBadge>
                    {item.referenceCode && (
                      <span className="tag ltr-code">{item.referenceCode}</span>
                    )}
                    <StatusBadge variant={item.hasActivePublication ? 'success' : 'warning'}>
                      {item.hasActivePublication ? txt.publishedBadge : txt.notPublished}
                    </StatusBadge>
                    <code className="ltr-code" style={{ fontSize: 11 }}>
                      {item.state}
                    </code>
                  </div>
                  <div className="item-title">
                    <strong>{item.title}</strong>
                  </div>
                  <div className="item-sub">
                    <span>{item.providerName}</span> {'←'} <span>{item.consumerName}</span> ·{' '}
                    <span>{item.apiGroupName}</span>
                    {item.latestVersionLabel ? (
                      <>
                        {' · '}
                        <span>{item.latestVersionLabel}</span>
                      </>
                    ) : null}
                  </div>
                </div>
                <Button
                  variant={item.resultKind === 'INTEGRATION' ? 'secondary' : 'outline'}
                  size="sm"
                  onClick={() => handleOpenItem(item)}
                >
                  {item.resultKind === 'INTEGRATION' ? txt.openIntegBtn : txt.openDocBtn}
                </Button>
              </div>
            ))}
          </div>
        )}
      </section>
    </div>
  );
}
