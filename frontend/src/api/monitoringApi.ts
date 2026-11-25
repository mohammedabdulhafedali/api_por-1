import { AppLanguage } from '../constants/uiBaseline';
import { ApiProblemError, parseProblemError } from './authApi';

export interface AuditEventListItem {
  id: string;
  actorUserId?: string | null;
  actorName?: string | null;
  actorEmail?: string | null;
  operation: string;
  resourceKind: string;
  resourceId?: string | null;
  result: string;
  changeSummaryJson?: string | null;
  recordedAt: string;
}

export interface PagedAuditEventsResponse {
  totalCount: number;
  page: number;
  pageSize: number;
  items: AuditEventListItem[];
  distinctOperations: string[];
  distinctResourceKinds: string[];
  traceId: string;
}

export interface GlobalSearchResultItem {
  resultKind: 'DOCUMENT' | 'INTEGRATION' | string;
  id: string;
  documentId: string;
  integrationId?: string | null;
  referenceCode?: string | null;
  title: string;
  providerName: string;
  consumerName: string;
  apiGroupName: string;
  state: string;
  latestVersionLabel?: string | null;
  hasActivePublication: boolean;
  createdAt: string;
}

export interface GlobalSearchResponse {
  query: string;
  totalCount: number;
  items: GlobalSearchResultItem[];
  traceId: string;
}

export interface DashboardPendingStage {
  runId: string;
  stageId: string;
  stageName: string;
  targetKind: 'REVISION' | 'CHANGE_REQUEST' | string;
  targetId: string;
  documentId: string;
  integrationId: string;
  title: string;
  referenceCode: string;
  roleName: string;
  startedAt: string;
}

export interface DashboardRecentAudit {
  id: string;
  actorName?: string | null;
  operation: string;
  resourceKind: string;
  resourceId?: string | null;
  result: string;
  recordedAt: string;
}

export interface DashboardSummaryResponse {
  visibleDocumentsCount: number;
  visibleIntegrationsCount: number;
  pendingMyApprovalStagesCount: number;
  openChangeRequestsCount: number;
  publishedRevisionsCount: number;
  unreadNotificationsCount: number;
  canViewDocuments: boolean;
  canViewIntegrations: boolean;
  canReviewWorkflows: boolean;
  canViewPublications: boolean;
  canViewAudit: boolean;
  canViewPortal: boolean;
  pendingApprovalStages: DashboardPendingStage[];
  recentAuditEvents: DashboardRecentAudit[];
  traceId: string;
}

async function ensureOk<T>(res: Response, lang: AppLanguage): Promise<T> {
  if (!res.ok) {
    throw await parseProblemError(res, lang);
  }
  return (await res.json()) as T;
}

export async function getAuditEvents(
  params: {
    page?: number;
    pageSize?: number;
    search?: string;
    operation?: string;
    resourceKind?: string;
    result?: string;
  },
  lang: AppLanguage
): Promise<PagedAuditEventsResponse> {
  const qs = new URLSearchParams();
  if (params.page) qs.set('page', String(params.page));
  if (params.pageSize) qs.set('pageSize', String(params.pageSize));
  if (params.search?.trim()) qs.set('search', params.search.trim());
  if (params.operation?.trim()) qs.set('operation', params.operation.trim());
  if (params.resourceKind?.trim()) qs.set('resourceKind', params.resourceKind.trim());
  if (params.result?.trim()) qs.set('result', params.result.trim());

  const res = await fetch(`/api/v1/audit-events?${qs.toString()}`, {
    method: 'GET',
    credentials: 'include',
    headers: { 'Accept-Language': lang },
  });
  return ensureOk<PagedAuditEventsResponse>(res, lang);
}

export async function searchAuthorizedResources(
  params: { query?: string; type?: string; state?: string },
  lang: AppLanguage
): Promise<GlobalSearchResponse> {
  const qs = new URLSearchParams();
  if (params.query?.trim()) qs.set('q', params.query.trim());
  if (params.type?.trim()) qs.set('type', params.type.trim());
  if (params.state?.trim()) qs.set('state', params.state.trim());

  const res = await fetch(`/api/v1/search?${qs.toString()}`, {
    method: 'GET',
    credentials: 'include',
    headers: { 'Accept-Language': lang },
  });
  return ensureOk<GlobalSearchResponse>(res, lang);
}

export async function getDashboardSummary(lang: AppLanguage): Promise<DashboardSummaryResponse> {
  const res = await fetch('/api/v1/dashboard', {
    method: 'GET',
    credentials: 'include',
    headers: { 'Accept-Language': lang },
  });
  return ensureOk<DashboardSummaryResponse>(res, lang);
}

export type { ApiProblemError };

