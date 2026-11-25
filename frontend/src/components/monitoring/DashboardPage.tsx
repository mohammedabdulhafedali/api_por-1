import { useCallback, useEffect, useState } from 'react';
import { ApiProblemError } from '../../api/authApi';
import { DashboardSummaryResponse, getDashboardSummary } from '../../api/monitoringApi';
import { AppLanguage } from '../../constants/uiBaseline';
import { Button, ErrorState, LoadingState, ReferenceIcon, StatusBadge } from '../ui';

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
      <div className="page-actions" style={{ justifyContent: 'space-between' }}>
        <div className="row wrap">
          <span className="sr-only">
            <StatusBadge variant="info">{txt.badge}</StatusBadge>
            <span>{txt.welcome(userName)}</span>
            <span>{txt.subtitle}</span>
          </span>
          <Button variant="outline" size="sm" onClick={() => void loadSummary()}>
            {txt.refreshBtn}
          </Button>
          <Button variant="outline" size="sm" onClick={() => onNavigateToScreen('UI-18')}>
            <ReferenceIcon name="search" size={15} /> {txt.openSearchBtn}
          </Button>
          {data.canViewPortal && (
            <Button variant="outline" size="sm" onClick={() => onNavigateToScreen('UI-19')}>
              <ReferenceIcon name="external" size={15} /> {txt.openPortalBtn}
            </Button>
          )}
        </div>
        <div className="row wrap">
          {data.canViewDocuments && (
            <Button variant="primary" onClick={() => onNavigateToScreen('UI-07')}>
              <ReferenceIcon name="plus" size={15} /> {txt.openDocsBtn}
            </Button>
          )}
        </div>
      </div>

      {/* Exact 5-card reference stat-grid.stat-five */}
      <div className="stat-grid stat-five">
        <div className="stat dashboard-kpi-card">
          <div className="stat-top">
            <span className="dashboard-kpi-label">{txt.kpiDocs}</span>
            <span className="stat-icon">
              <ReferenceIcon name="file" size={18} />
            </span>
          </div>
          <div className="stat-num dashboard-kpi-value">{data.visibleDocumentsCount}</div>
        </div>

        <div className="stat dashboard-kpi-card">
          <div className="stat-top">
            <span className="dashboard-kpi-label">{txt.kpiIntegs}</span>
            <span className="stat-icon">
              <ReferenceIcon name="network" size={18} />
            </span>
          </div>
          <div className="stat-num dashboard-kpi-value">{data.visibleIntegrationsCount}</div>
        </div>

        <div className="stat dashboard-kpi-card">
          <div className="stat-top">
            <span className="dashboard-kpi-label">{txt.kpiPendingStages}</span>
            <span className="stat-icon">
              <ReferenceIcon name="clock" size={18} />
            </span>
          </div>
          <div className="stat-num dashboard-kpi-value">{data.pendingMyApprovalStagesCount}</div>
        </div>

        <div className="stat dashboard-kpi-card">
          <div className="stat-top">
            <span className="dashboard-kpi-label">{txt.kpiOpenChanges}</span>
            <span className="stat-icon">
              <ReferenceIcon name="repeat" size={18} />
            </span>
          </div>
          <div className="stat-num dashboard-kpi-value">{data.openChangeRequestsCount}</div>
        </div>

        <div className="stat dashboard-kpi-card">
          <div className="stat-top">
            <span className="dashboard-kpi-label">{txt.kpiPublished}</span>
            <span className="stat-icon">
              <ReferenceIcon name="external" size={18} />
            </span>
          </div>
          <div className="stat-num dashboard-kpi-value">{data.publishedRevisionsCount}</div>
          <span className="sr-only">
            {txt.kpiUnreadNotifs}: {data.unreadNotificationsCount}
          </span>
        </div>
      </div>

      {/* Available Work Cases Panel (.scenario-grid of .scenario-card) */}
      <section className="panel content-card">
        <div className="panel-head">
          <div>
            <h2 className="panel-title">
              {lang === 'ar' ? 'حالات العمل المتاحة' : 'Available Work Cases'}
            </h2>
          </div>
          {data.canViewIntegrations && (
            <Button variant="secondary" size="sm" onClick={() => onNavigateToScreen('UI-22')}>
              {txt.openIntegsBtn}
            </Button>
          )}
        </div>
        <div className="scenario-grid">
          {data.pendingApprovalStages.map((stage, idx) => (
            <div key={`case-${stage.stageId}`} className="scenario-card">
              <div className="row between">
                <span className="chip ltr-code">{`CASE-0${idx + 1}`}</span>
                <StatusBadge variant="warning">{stage.roleName}</StatusBadge>
              </div>
              <b>{`${stage.title} (${stage.stageName})`}</b>
              <div className="item-sub">{stage.targetKind}</div>
              <div className="row between scenario-foot">
                <span className="chip">{stage.targetKind}</span>
                <Button variant="secondary" size="sm" onClick={() => onNavigateToScreen('UI-22')}>
                  {lang === 'ar' ? 'فتح الحالة' : 'Open Case'}
                </Button>
              </div>
            </div>
          ))}
          {data.canViewDocuments && (
            <div className="scenario-card">
              <div className="row between">
                <span className="chip">API</span>
                <StatusBadge variant="info">{data.visibleDocumentsCount}</StatusBadge>
              </div>
              <b>{txt.kpiDocs}</b>
              <div className="item-sub">{txt.subtitle}</div>
              <div className="row between scenario-foot">
                <span className="chip">DOCS</span>
                <Button variant="secondary" size="sm" onClick={() => onNavigateToScreen('UI-21')}>
                  {txt.openDocsBtn}
                </Button>
              </div>
            </div>
          )}
          {data.canViewIntegrations && (
            <div className="scenario-card">
              <div className="row between">
                <span className="chip">INT</span>
                <StatusBadge variant="success">{data.visibleIntegrationsCount}</StatusBadge>
              </div>
              <b>{txt.kpiIntegs}</b>
              <div className="item-sub">
                {lang === 'ar'
                  ? 'إدارة الإصدارات والملفات والمراجعات وطلبات التغيير'
                  : 'Manage API versions, files, reviews, and change requests'}
              </div>
              <div className="row between scenario-foot">
                <span className="chip">{data.publishedRevisionsCount} PUBLISHED</span>
                <Button variant="secondary" size="sm" onClick={() => onNavigateToScreen('UI-22')}>
                  {txt.openIntegsBtn}
                </Button>
              </div>
            </div>
          )}
        </div>
      </section>

      {/* Two-Column Assigned Actions & Recent Activities (.grid-2) */}
      <div className="grid-2">
        <section className="panel content-card">
          <div className="panel-head">
            <h2 className="panel-title section-title">{txt.pendingTitle}</h2>
          </div>
          {data.pendingApprovalStages.length === 0 ? (
            <div className="empty-state-box empty">
              <ReferenceIcon name="search" size={26} />
              <div>{txt.pendingEmpty}</div>
            </div>
          ) : (
            <div>
              {data.pendingApprovalStages.map((stage) => (
                <div key={stage.stageId} className="row between work-row">
                  <div>
                    <b>{stage.title}</b>
                    <div className="item-sub">
                      <code className="ltr-code">{stage.referenceCode}</code> — {stage.stageName} ({stage.roleName})
                    </div>
                  </div>
                  <Button variant="outline" size="sm" onClick={() => onNavigateToScreen('UI-22')}>
                    {txt.goReviewBtn}
                  </Button>
                </div>
              ))}
            </div>
          )}
        </section>

        <section className="panel content-card">
          <div className="panel-head">
            <h2 className="panel-title section-title">{txt.recentAuditTitle}</h2>
            {data.canViewAudit && (
              <Button variant="ghost" size="sm" onClick={() => onNavigateToScreen('UI-16')}>
                {txt.openAuditBtn}
              </Button>
            )}
          </div>
          {data.recentAuditEvents.length === 0 ? (
            <div className="empty-state-box empty">
              <ReferenceIcon name="clock" size={24} />
              <div>{lang === 'ar' ? 'لا توجد أنشطة مسجلة' : 'No recent activities'}</div>
            </div>
          ) : (
            <div>
              {data.recentAuditEvents.slice(0, 6).map((ev) => (
                <div key={ev.id} className="work-row row between">
                  <div>
                    <b>
                      <code className="ltr-code">{ev.operation}</code>
                    </b>
                    <div className="item-sub">
                      {ev.actorName ?? 'System'} ·{' '}
                      <code className="ltr-code">{new Date(ev.recordedAt).toLocaleString()}</code>
                    </div>
                  </div>
                  <StatusBadge variant={ev.result === 'SUCCESS' ? 'success' : 'warning'}>
                    {ev.result}
                  </StatusBadge>
                </div>
              ))}
            </div>
          )}
        </section>
      </div>
    </div>
  );
}
