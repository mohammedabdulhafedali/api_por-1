import { useCallback, useEffect, useState } from 'react';
import { ApiProblemError } from '../../api/authApi';
import {
  IntegrationItem,
  listApiDocuments,
  listIntegrations,
} from '../../api/documentsAndIntegrationsApi';
import {
  CompanySystemItem,
  listCompanySystems,
  listConsumerCategories,
  listConsumers,
  listExternalProviders,
  listInternalProviders,
} from '../../api/masterDataApi';
import { getDashboardSummary } from '../../api/monitoringApi';
import { getIntegrationPublications } from '../../api/publishingPortalApi';
import { listIntegrationVersions } from '../../api/versionsFilesApi';
import { AppLanguage } from '../../constants/uiBaseline';
import { ErrorState, LoadingState } from '../ui';

interface DashboardPageProps {
  lang: AppLanguage;
  userName: string;
  onSessionExpired: () => void;
  onNavigateToScreen: (navId: string) => void;
}

interface SystemCardViewModel {
  id: string;
  name: string;
  providerEntities: string[];
  consumerEntities: string[];
}

export function DashboardPage({
  lang,
  onSessionExpired,
  onNavigateToScreen,
}: DashboardPageProps) {
  const [loading, setLoading] = useState(true);
  const [errorMsg, setErrorMsg] = useState<string | null>(null);

  const [systemsCount, setSystemsCount] = useState(0);
  const [sectorsCount, setSectorsCount] = useState(0);
  const [consumersCount, setConsumersCount] = useState(0);
  const [providersCount, setProvidersCount] = useState(0);
  const [apiRoutesCount, setApiRoutesCount] = useState(0);
  const [systemCards, setSystemCards] = useState<SystemCardViewModel[]>([]);
  const [collapsedMap, setCollapsedMap] = useState<Record<string, boolean>>({});

  const loadDashboardData = useCallback(async () => {
    setLoading(true);
    setErrorMsg(null);
    try {
      const [
        systemsRes,
        categoriesRes,
        consumersRes,
        internalProvRes,
        externalProvRes,
        docsRes,
        integrationsRes,
        summaryRes,
      ] = await Promise.allSettled([
        listCompanySystems({ page: 1, pageSize: 50, lang }),
        listConsumerCategories({ page: 1, pageSize: 50, lang }),
        listConsumers({ page: 1, pageSize: 50, lang }),
        listInternalProviders({ page: 1, pageSize: 50, lang }),
        listExternalProviders({ page: 1, pageSize: 50, lang }),
        listApiDocuments({ page: 1, pageSize: 100 }, lang),
        listIntegrations({ page: 1, pageSize: 100 }, lang),
        getDashboardSummary(lang),
      ]);

      const allResults = [
        systemsRes,
        categoriesRes,
        consumersRes,
        internalProvRes,
        externalProvRes,
        docsRes,
        integrationsRes,
        summaryRes,
      ];
      for (const r of allResults) {
        if (r.status === 'rejected' && (r.reason as ApiProblemError)?.status === 401) {
          onSessionExpired();
          return;
        }
      }

      const systems: CompanySystemItem[] =
        systemsRes.status === 'fulfilled' ? systemsRes.value.items : [];
      const totalSystems =
        systemsRes.status === 'fulfilled' ? systemsRes.value.totalCount : 0;

      const totalCategories =
        categoriesRes.status === 'fulfilled' ? categoriesRes.value.totalCount : 0;

      const totalConsumers =
        consumersRes.status === 'fulfilled' ? consumersRes.value.totalCount : 0;

      const totalProviders =
        (internalProvRes.status === 'fulfilled' ? internalProvRes.value.totalCount : 0) +
        (externalProvRes.status === 'fulfilled' ? externalProvRes.value.totalCount : 0);

      const totalDocs =
        docsRes.status === 'fulfilled'
          ? docsRes.value.totalCount
          : summaryRes.status === 'fulfilled'
            ? summaryRes.value.visibleDocumentsCount
            : 0;

      setSystemsCount(totalSystems);
      setSectorsCount(totalCategories);
      setConsumersCount(totalConsumers);
      setProvidersCount(totalProviders);
      setApiRoutesCount(totalDocs);

      const integrations: IntegrationItem[] =
        integrationsRes.status === 'fulfilled' ? integrationsRes.value.items : [];

      // Verify which integrations have actually passed all stages (APPROVED revision or PUBLISHED publication)
      const candidateIntegrations = integrations.filter(
        (integ) => integ.documentState !== 'ARCHIVED' && integ.versionsCount > 0
      );

      const stageChecks = await Promise.all(
        candidateIntegrations.map(async (integ) => {
          const [versRes, pubsRes] = await Promise.allSettled([
            listIntegrationVersions(integ.id, lang),
            getIntegrationPublications(integ.id, lang),
          ]);

          const hasApprovedVersion =
            versRes.status === 'fulfilled' &&
            versRes.value.versions.some(
              (v) => !v.isArchived && v.latestRevisionStatus?.toUpperCase() === 'APPROVED'
            );

          const hasPublishedPackage =
            pubsRes.status === 'fulfilled' &&
            (Boolean(pubsRes.value.activeConsumerVersion) ||
              pubsRes.value.publications.some((p) => p.state?.toUpperCase() === 'PUBLISHED'));

          return {
            integ,
            isFullyIntegrated: hasApprovedVersion || hasPublishedPackage,
          };
        })
      );

      const completedIntegrations = stageChecks
        .filter((entry) => entry.isFullyIntegrated)
        .map((entry) => entry.integ);

      const cards: SystemCardViewModel[] = systems.map((sys) => {
        const providersSet = new Set<string>();
        const consumersSet = new Set<string>();

        for (const integ of completedIntegrations) {
          // If this system is the CONSUMER in the completed integration, the other side is a PROVIDER to this system
          if (
            integ.consumerName === sys.name &&
            integ.providerName &&
            integ.providerName !== sys.name
          ) {
            providersSet.add(integ.providerName);
          }
          // If this system is the PROVIDER in the completed integration, the other side is a CONSUMER of this system
          if (
            integ.providerName === sys.name &&
            integ.consumerName &&
            integ.consumerName !== sys.name
          ) {
            consumersSet.add(integ.consumerName);
          }
        }

        return {
          id: sys.id,
          name: sys.name,
          providerEntities: Array.from(providersSet),
          consumerEntities: Array.from(consumersSet),
        };
      });

      setSystemCards(cards);
    } catch (err) {
      if ((err as ApiProblemError)?.status === 401) {
        onSessionExpired();
        return;
      }
      setErrorMsg((err as Error).message || 'Error');
    } finally {
      setLoading(false);
    }
  }, [lang, onSessionExpired]);

  useEffect(() => {
    void loadDashboardData();
  }, [loadDashboardData]);

  const toggleCardDrawer = (id: string) => {
    setCollapsedMap((prev) => ({ ...prev, [id]: !prev[id] }));
  };

  if (loading && systemCards.length === 0) {
    return (
      <div className="content-card">
        <LoadingState
          title={lang === 'ar' ? 'جارٍ تحميل لوحة الداشبورد...' : 'Loading dashboard...'}
        />
      </div>
    );
  }

  if (errorMsg) {
    return (
      <div className="content-card">
        <ErrorState
          title={lang === 'ar' ? 'تعذر تحميل لوحة الداشبورد' : 'Unable to Load Dashboard'}
          message={errorMsg}
          retryLabel={lang === 'ar' ? 'إعادة المحاولة' : 'Retry'}
          onRetry={() => void loadDashboardData()}
        />
      </div>
    );
  }

  return (
    <div className="ref-dashboard-page" data-testid="dashboard-page">
      <div className="ref-dashboard-stats-bar">
        <button
          type="button"
          className="ref-stat-pill"
          onClick={() => onNavigateToScreen('UI-01')}
        >
          <span className="ref-stat-label">
            {lang === 'ar' ? 'الأنظمة:' : 'Systems:'}
          </span>
          <strong className="ref-stat-num">{systemsCount}</strong>
        </button>

        <button
          type="button"
          className="ref-stat-pill"
          onClick={() => onNavigateToScreen('UI-02')}
        >
          <span className="ref-stat-label">
            {lang === 'ar' ? 'القطاعات:' : 'Sectors:'}
          </span>
          <strong className="ref-stat-num">{sectorsCount}</strong>
        </button>

        <button
          type="button"
          className="ref-stat-pill"
          onClick={() => onNavigateToScreen('UI-02')}
        >
          <span className="ref-stat-label">
            {lang === 'ar' ? 'جهات مستهلكة:' : 'Consumers:'}
          </span>
          <strong className="ref-stat-num cyan">{consumersCount}</strong>
        </button>

        <button
          type="button"
          className="ref-stat-pill"
          onClick={() => onNavigateToScreen('UI-03')}
        >
          <span className="ref-stat-label">
            {lang === 'ar' ? 'جهات مزوّدة:' : 'Providers:'}
          </span>
          <strong className="ref-stat-num purple">{providersCount}</strong>
        </button>

        <button
          type="button"
          className="ref-stat-pill"
          onClick={() => onNavigateToScreen('UI-21')}
        >
          <span className="ref-stat-label">
            {lang === 'ar' ? 'مسارات الـ API:' : 'API Routes:'}
          </span>
          <strong className="ref-stat-num">{apiRoutesCount}</strong>
        </button>
      </div>

      <div className="ref-systems-grid">
        {systemCards.map((card) => {
          const isProvidersCollapsed = Boolean(collapsedMap[`${card.id}:providers`]);
          const isConsumersCollapsed = Boolean(collapsedMap[`${card.id}:consumers`]);
          const hasProviders = card.providerEntities.length > 0;
          const hasConsumers = card.consumerEntities.length > 0;

          return (
            <article key={card.id} className="ref-system-card">
              <div className="ref-system-card-head">
                <h2 className="ref-system-title heading-font">{card.name}</h2>
              </div>

              {hasProviders && (
                <div className="ref-system-providers-box">
                  <button
                    type="button"
                    className="ref-system-providers-toggle"
                    aria-expanded={!isProvidersCollapsed}
                    onClick={() => toggleCardDrawer(`${card.id}:providers`)}
                  >
                    <span>{lang === 'ar' ? 'جهات مزوّدة' : 'Provider Entities'}</span>
                    <span className={`ref-chevron ${isProvidersCollapsed ? 'collapsed' : ''}`}>
                      ⌄
                    </span>
                  </button>

                  {!isProvidersCollapsed && (
                    <div className="ref-system-chips-wrap">
                      {card.providerEntities.map((ent) => (
                        <span key={ent} className="ref-entity-chip">
                          {ent}
                        </span>
                      ))}
                    </div>
                  )}
                </div>
              )}

              {hasConsumers && (
                <div className="ref-system-providers-box">
                  <button
                    type="button"
                    className="ref-system-providers-toggle"
                    aria-expanded={!isConsumersCollapsed}
                    onClick={() => toggleCardDrawer(`${card.id}:consumers`)}
                  >
                    <span>{lang === 'ar' ? 'جهات مستهلكة' : 'Consumer Entities'}</span>
                    <span className={`ref-chevron ${isConsumersCollapsed ? 'collapsed' : ''}`}>
                      ⌄
                    </span>
                  </button>

                  {!isConsumersCollapsed && (
                    <div className="ref-system-chips-wrap">
                      {card.consumerEntities.map((ent) => (
                        <span key={ent} className="ref-entity-chip">
                          {ent}
                        </span>
                      ))}
                    </div>
                  )}
                </div>
              )}

              {!hasProviders && !hasConsumers && (
                <div className="ref-system-providers-box">
                  <div className="ref-system-chips-wrap">
                    <span className="ref-entity-chip muted">
                      {lang === 'ar' ? 'لا توجد جهات مرتبطة' : 'No linked entities'}
                    </span>
                  </div>
                </div>
              )}
            </article>
          );
        })}
      </div>
    </div>
  );
}
