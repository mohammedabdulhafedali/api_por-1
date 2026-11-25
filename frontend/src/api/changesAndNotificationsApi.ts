import { AppLanguage } from '../constants/uiBaseline';
import { ApiProblemError, fetchCsrfToken, parseProblemError } from './authApi';

export interface AffectedSourceFileItem {
  revisionFileId: string;
  originalName: string;
  fileTypeName: string;
  sha256: string;
  sizeBytes: number;
}

export interface ChangeRequestItem {
  id: string;
  integrationId: string;
  documentId: string;
  referenceCode: string;
  sourceRevisionId: string;
  sourceRevisionNumber: number;
  versionLabel: string;
  changeTypeId: string;
  changeTypeName: string;
  approvalRouteId: string;
  approvalRouteName: string;
  reason: string;
  consumerImpact?: string | null;
  status: string;
  isEditable: boolean;
  requestedBy: string;
  requestedByName: string;
  createdAt: string;
  latestWorkflowRunId?: string | null;
  latestWorkflowRunStatus?: string | null;
  latestDecisionNotes?: string | null;
  affectedFiles: AffectedSourceFileItem[];
  traceId?: string;
}

export interface EligibleApprovedRevisionForChange {
  revisionId: string;
  apiVersionId: string;
  versionLabel: string;
  revisionNumber: number;
  availableFiles: AffectedSourceFileItem[];
}

export interface AllowedChangeTypeOption {
  changeTypeId: string;
  name: string;
  routeId: string;
  routeName: string;
}

export interface IntegrationChangeRequestsWorkspaceResponse {
  integrationId: string;
  documentId: string;
  referenceCode: string;
  derivedTitle: string;
  canCreate: boolean;
  canEdit: boolean;
  canSubmit: boolean;
  eligibleRevisions: EligibleApprovedRevisionForChange[];
  allowedChangeTypes: AllowedChangeTypeOption[];
  items: ChangeRequestItem[];
  traceId?: string;
}

export interface SubmitChangeRequestResponse {
  changeRequest: ChangeRequestItem;
  workflowRunId: string;
  traceId?: string;
}

export interface NotificationItem {
  id: string;
  kind: string;
  resourceKind: string;
  resourceId: string;
  summaryAr: string;
  summaryEn: string;
  isRead: boolean;
  createdAt: string;
  readAt?: string | null;
}

export interface NotificationsListResponse {
  unreadCount: number;
  items: NotificationItem[];
  traceId?: string;
}

async function ensureOk<T>(res: Response, lang: AppLanguage): Promise<T> {
  if (!res.ok) {
    throw await parseProblemError(res, lang);
  }
  return (await res.json()) as T;
}

export async function getIntegrationChangeRequests(
  integrationId: string,
  lang: AppLanguage
): Promise<IntegrationChangeRequestsWorkspaceResponse> {
  const res = await fetch(`/api/v1/integrations/${integrationId}/change-requests`, {
    method: 'GET',
    credentials: 'include',
    headers: { 'Accept-Language': lang },
  });
  return ensureOk<IntegrationChangeRequestsWorkspaceResponse>(res, lang);
}

export async function createChangeRequest(
  integrationId: string,
  payload: {
    sourceRevisionId: string;
    changeTypeId: string;
    reason: string;
    consumerImpact?: string | null;
    affectedSourceFileIds: string[];
  },
  lang: AppLanguage
): Promise<ChangeRequestItem> {
  const csrf = await fetchCsrfToken(lang);
  const res = await fetch(`/api/v1/integrations/${integrationId}/change-requests`, {
    method: 'POST',
    credentials: 'include',
    headers: {
      'Content-Type': 'application/json',
      [csrf.headerName]: csrf.token,
      'Accept-Language': lang,
    },
    body: JSON.stringify(payload),
  });
  return ensureOk<ChangeRequestItem>(res, lang);
}

export async function updateChangeRequest(
  requestId: string,
  payload: {
    changeTypeId: string;
    reason: string;
    consumerImpact?: string | null;
    affectedSourceFileIds: string[];
  },
  lang: AppLanguage
): Promise<ChangeRequestItem> {
  const csrf = await fetchCsrfToken(lang);
  const res = await fetch(`/api/v1/change-requests/${requestId}`, {
    method: 'PATCH',
    credentials: 'include',
    headers: {
      'Content-Type': 'application/json',
      [csrf.headerName]: csrf.token,
      'Accept-Language': lang,
    },
    body: JSON.stringify(payload),
  });
  return ensureOk<ChangeRequestItem>(res, lang);
}

export async function submitChangeRequest(
  requestId: string,
  lang: AppLanguage
): Promise<SubmitChangeRequestResponse> {
  const csrf = await fetchCsrfToken(lang);
  const res = await fetch(`/api/v1/change-requests/${requestId}/submit`, {
    method: 'POST',
    credentials: 'include',
    headers: {
      [csrf.headerName]: csrf.token,
      'Accept-Language': lang,
    },
  });
  return ensureOk<SubmitChangeRequestResponse>(res, lang);
}

export async function getMyNotifications(
  unreadOnly: boolean,
  lang: AppLanguage
): Promise<NotificationsListResponse> {
  const url = unreadOnly ? '/api/v1/notifications?unreadOnly=true' : '/api/v1/notifications';
  const res = await fetch(url, {
    method: 'GET',
    credentials: 'include',
    headers: { 'Accept-Language': lang },
  });
  return ensureOk<NotificationsListResponse>(res, lang);
}

export async function markNotificationAsRead(
  notificationId: string,
  lang: AppLanguage
): Promise<NotificationItem> {
  const csrf = await fetchCsrfToken(lang);
  const res = await fetch(`/api/v1/notifications/${notificationId}/read`, {
    method: 'POST',
    credentials: 'include',
    headers: {
      [csrf.headerName]: csrf.token,
      'Accept-Language': lang,
    },
  });
  return ensureOk<NotificationItem>(res, lang);
}

export type { ApiProblemError };
