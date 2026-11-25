import { useCallback, useEffect, useState } from 'react';
import { ApiProblemError } from '../../api/authApi';
import { DashboardSummaryResponse, getDashboardSummary } from '../../api/monitoringApi';
import { AppLanguage } from '../../constants/uiBaseline';
import { Button, ErrorState, LoadingState, StatusBadge } from '../ui';

interface DashboardPageProps {
  lang: AppLanguage;
  userName: string;
  onSessionExpired: () => void;
  onNavigateToScreen: (navId: string) => void;
}

const DASH_TEXT = {
  ar: {
    badge: 'لوحة القيادة التكيّفية · UI-17 / P05',
    welcome: (name: string) => `مرحبًا، ${name}`,
    subtitle:
      'تعرض هذه اللوحة المؤشرات والمهام المعلّقة المصرح لك بالوصول إليها فقط وفق صلاحياتك العامة وصلاحيات الوثائق.',
    refreshBtn: 'تحديث اللوحة',
    kpiDocs: 'وثائق API المصرح بها',
    kpiIntegs: 'التكاملات النشطة المصرح بها',
    kpiPendingStages: 'مراحل اعتماد بانتظار قراري',
    kpiOpenChanges: 'طلبات تغيير مفتوحة',
    kpiPublished: 'حزم منشورة نشطة',
    kpiUnreadNotifs: 'إشعارات غير مقروءة',
    pendingTitle: 'مراحل الاعتماد النشطة لدوري وصلاحياتي',
    pendingEmpty: 'لا توجد مراحل اعتماد معلّقة لدورك على وثائق مصرح لك بمراجعتها.',
    recentAuditTitle: 'أحدث أحداث التدقيق المسجلة',
    openSearchBtn: 'البحث الشامل',
    openDocsBtn: 'وثائق API',
    openIntegsBtn: 'التكاملات',
    openPortalBtn: 'بوابة المستهلك',
    openAuditBtn: 'سجل التدقيق',
    colTarget: 'التكامل / المستند',
    colStage: 'المرحلة والدور',
    colType: 'المسار',
    colAction: 'الإجراء',
    goReviewBtn: 'متابعة في التكاملات',
    loadingText: 'جارٍ تحميل لوحة القيادة...',
    loadErrorTitle: 'تعذر تحميل لوحة القيادة',
    retryLabel: 'إعادة المحاولة',
  },
  en: {
    badge: 'Permission-Adaptive Dashboard · UI-17 / P05',
    welcome: (name: string) => `Welcome, ${name}`,
    subtitle:
      'Metrics and action queues below reflect only resources permitted by your active roles, permission overrides, and Document ACL.',
    refreshBtn: 'Refresh Dashboard',
    kpiDocs: 'Authorized API Documents',
    kpiIntegs: 'Authorized Integrations',
    kpiPendingStages: 'Pending Stages For My Role',
    kpiOpenChanges: 'Open Change Requests',
    kpiPublished: 'Active Published Packages',
    kpiUnreadNotifs: 'Unread Notifications',
    pendingTitle: 'Active Approval Stages Awaiting My Decision',
    pendingEmpty: 'No active workflow stages currently require your role on permitted documents.',
    recentAuditTitle: 'Latest Recorded Audit Events',
    openSearchBtn: 'Global Search',
    openDocsBtn: 'API Documents',
    openIntegsBtn: 'Integrations',
    openPortalBtn: 'Consumer Portal',
    openAuditBtn: 'Audit Log',
    colTarget: 'Integration & Document',
    colStage: 'Stage & Role',
    colType: 'Workflow Target',
    colAction: 'Action',
    goReviewBtn: 'Open in Integrations',
    loadingText: 'Loading dashboard...',
    loadErrorTitle: 'Unable to Load Dashboard',
    retryLabel: 'Retry',
  },
} as const;

export function DashboardPage({
  lang,
  userName,
  onSessionExpired,
  onNavigateToScreen,
}: DashboardPageProps) {
  const txt = DASH_TEXT[lang];
  const [data, setData] = useState<DashboardSummaryResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [errorMsg, setErrorMsg] = useState<string | null>(null);

  const loadSummary = useCallback(async () => {
    setLoading(true);
    setErrorMsg(null);
    try {
      const summary = await getDashboardSummary(lang);
      setData(summary);
    } catch (err) {
      if ((err as ApiProblemError)?.status === 401) {
        onSessionExpired();
        return;
      }
      setErrorMsg((err as Error).message || txt.loadErrorTitle);
    } finally {
      setLoading(false);
    }
  }, [lang, onSessionExpired, txt.loadErrorTitle]);

  useEffect(() => {
    void loadSummary();
  }, [loadSummary]);

  if (loading && !data) {
    return (
      <div className="content-card">
        <LoadingState title={txt.loadingText} />
      </div>
    );
  }

  if (errorMsg) {
    return (
      <div className="content-card">
        <ErrorState
          title={txt.loadErrorTitle}
          message={errorMsg}
          retryLabel={txt.retryLabel}
          onRetry={() => void loadSummary()}
        />
      </div>
    );
  }

  if (!data) {
    return null;
  }

  return (
    <div className="module-stack" data-testid="dashboard-page">
      <div className="content-card">
        <div className="card-header-row">
          <div>
            <StatusBadge variant="info">{txt.badge}</StatusBadge>
            <h2 className="section-title" style={{ marginTop: 8 }}>
              {txt.welcome(userName)}
            </h2>
            <p className="section-subtitle">{txt.subtitle}</p>
          </div>
          <div className="monitoring-filter-actions">
            <Button variant="outline" size="sm" onClick={() => void loadSummary()}>
              {txt.refreshBtn}
            </Button>
          </div>
        </div>

        <div className="dashboard-quick-links" style={{ marginTop: 12 }}>
          <Button variant="outline" size="sm" onClick={() => onNavigateToScreen('UI-18')}>
            {txt.openSearchBtn}
          </Button>
          {data.canViewDocuments && (
            <Button variant="outline" size="sm" onClick={() => onNavigateToScreen('UI-21')}>
              {txt.openDocsBtn}
            </Button>
          )}
          {data.canViewIntegrations && (
            <Button variant="outline" size="sm" onClick={() => onNavigateToScreen('UI-22')}>
              {txt.openIntegsBtn}
            </Button>
          )}
          {data.canViewPortal && (
            <Button variant="outline" size="sm" onClick={() => onNavigateToScreen('UI-19')}>
              {txt.openPortalBtn}
            </Button>
          )}
          {data.canViewAudit && (
            <Button variant="outline" size="sm" onClick={() => onNavigateToScreen('UI-16')}>
              {txt.openAuditBtn}
            </Button>
          )}
        </div>
      </div>

      <div className="dashboard-kpi-grid">
        {data.canViewDocuments && (
          <div className="dashboard-kpi-card">
            <span className="dashboard-kpi-label">{txt.kpiDocs}</span>
            <strong className="dashboard-kpi-value">{data.visibleDocumentsCount}</strong>
          </div>
        )}
        {data.canViewIntegrations && (
          <div className="dashboard-kpi-card">
            <span className="dashboard-kpi-label">{txt.kpiIntegs}</span>
            <strong className="dashboard-kpi-value">{data.visibleIntegrationsCount}</strong>
          </div>
        )}
        {data.canReviewWorkflows && (
          <div className="dashboard-kpi-card">
            <span className="dashboard-kpi-label">{txt.kpiPendingStages}</span>
            <strong className="dashboard-kpi-value">{data.pendingMyApprovalStagesCount}</strong>
          </div>
        )}
        {(data.canViewIntegrations || data.canReviewWorkflows) && (
          <div className="dashboard-kpi-card">
            <span className="dashboard-kpi-label">{txt.kpiOpenChanges}</span>
            <strong className="dashboard-kpi-value">{data.openChangeRequestsCount}</strong>
          </div>
        )}
        {(data.canViewPublications || data.canViewPortal || data.canViewIntegrations) && (
          <div className="dashboard-kpi-card">
            <span className="dashboard-kpi-label">{txt.kpiPublished}</span>
            <strong className="dashboard-kpi-value">{data.publishedRevisionsCount}</strong>
          </div>
        )}
        <div className="dashboard-kpi-card">
          <span className="dashboard-kpi-label">{txt.kpiUnreadNotifs}</span>
          <strong className="dashboard-kpi-value">{data.unreadNotificationsCount}</strong>
        </div>
      </div>

      {data.canReviewWorkflows && (
        <div className="content-card">
          <h3 className="section-title">{txt.pendingTitle}</h3>
          {data.pendingApprovalStages.length === 0 ? (
            <div className="empty-state-box" style={{ marginTop: 10 }}>
              {txt.pendingEmpty}
            </div>
          ) : (
            <div className="data-table-wrap" style={{ marginTop: 10 }}>
              <table className="data-table">
                <thead>
                  <tr>
                    <th>{txt.colTarget}</th>
                    <th>{txt.colStage}</th>
                    <th>{txt.colType}</th>
                    <th>{txt.colAction}</th>
                  </tr>
                </thead>
                <tbody>
                  {data.pendingApprovalStages.map((stage) => (
                    <tr key={stage.stageId}>
                      <td>
                        <strong>{stage.title}</strong>
                        <div>
                          <code className="ltr-code">{stage.referenceCode}</code>
                        </div>
                      </td>
                      <td>
                        <div>{stage.stageName}</div>
                        <StatusBadge variant="warning">{stage.roleName}</StatusBadge>
                      </td>
                      <td>
                        <StatusBadge variant="info">{stage.targetKind}</StatusBadge>
                      </td>
                      <td>
                        <Button
                          variant="outline"
                          size="sm"
                          onClick={() => onNavigateToScreen('UI-22')}
                        >
                          {txt.goReviewBtn}
                        </Button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      )}

      {data.canViewAudit && data.recentAuditEvents.length > 0 && (
        <div className="content-card">
          <h3 className="section-title">{txt.recentAuditTitle}</h3>
          <div className="data-table-wrap" style={{ marginTop: 10 }}>
            <table className="data-table">
              <thead>
                <tr>
                  <th>{lang === 'ar' ? 'الوقت' : 'Time'}</th>
                  <th>{lang === 'ar' ? 'المنفذ' : 'Actor'}</th>
                  <th>{lang === 'ar' ? 'العملية' : 'Operation'}</th>
                  <th>{lang === 'ar' ? 'النتيجة' : 'Result'}</th>
                </tr>
              </thead>
              <tbody>
                {data.recentAuditEvents.map((ev) => (
                  <tr key={ev.id}>
                    <td>
                      <code className="ltr-code">{new Date(ev.recordedAt).toLocaleString()}</code>
                    </td>
                    <td>{ev.actorName ?? 'System'}</td>
                    <td>
                      <code className="ltr-code">{ev.operation}</code>
                    </td>
                    <td>
                      <StatusBadge variant={ev.result === 'SUCCESS' ? 'success' : 'warning'}>
                        {ev.result}
                      </StatusBadge>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}
    </div>
  );
}
