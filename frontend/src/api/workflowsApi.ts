import { AppLanguage } from '../constants/uiBaseline';
import { ApiProblemError, fetchCsrfToken, parseProblemError } from './authApi';

export type WorkflowDecisionKind = 'APPROVE' | 'RETURN' | 'REJECT';

export interface WorkflowDecisionItem {
  id: string;
  runStageId: string;
  actorUserId: string;
  actorUserName: string;
  decision: WorkflowDecisionKind | string;
  notes?: string | null;
  decidedAt: string;
}

export interface WorkflowRunStageItem {
  id: string;
  runId: string;
  position: number;
  stageNameSnapshot: string;
  responsibleRoleIdSnapshot: string;
  responsibleRoleName: string;
  status: string;
  decision?: WorkflowDecisionItem | null;
}

export interface WorkflowRunDetails {
  id: string;
  routeId: string;
  routeName: string;
  revisionId?: string | null;
  changeRequestId?: string | null;
  integrationId: string;
  documentId: string;
  referenceCode: string;
  derivedTitle: string;
  versionLabel?: string | null;
  revisionNumber?: number | null;
  revisionStatus?: string | null;
  status: string;
  submittedBy: string;
  submittedByName: string;
  startedAt: string;
  endedAt?: string | null;
  activeStageId?: string | null;
  activeStageName?: string | null;
  activeStageRoleName?: string | null;
  canCurrentUserDecideActiveStage: boolean;
  allowedDecisions?: WorkflowDecisionKind[];
  stages: WorkflowRunStageItem[];
  traceId?: string;
}

export interface WorkflowInboxItem {
  runId: string;
  stageId: string;
  stagePosition: number;
  stageNameSnapshot: string;
  responsibleRoleIdSnapshot: string;
  responsibleRoleName: string;
  integrationId: string;
  documentId: string;
  referenceCode: string;
  derivedTitle: string;
  revisionId?: string | null;
  changeRequestId?: string | null;
  versionLabel?: string | null;
  revisionNumber?: number | null;
  revisionStatus: string;
  submittedBy: string;
  submittedByName: string;
  startedAt: string;
  canDecide: boolean;
}

export interface WorkflowInboxResponse {
  items: WorkflowInboxItem[];
  traceId?: string;
}

export interface WorkflowDecisionHistoryEntry {
  runId: string;
  stageId: string;
  stagePosition: number;
  stageNameSnapshot: string;
  responsibleRoleName: string;
  runStatus: string;
  revisionId?: string | null;
  revisionNumber?: number | null;
  runStartedAt: string;
  runEndedAt?: string | null;
  decision: WorkflowDecisionItem;
}

export interface WorkflowHistoryResponse {
  runId: string;
  revisionId?: string | null;
  changeRequestId?: string | null;
  entries: WorkflowDecisionHistoryEntry[];
  traceId?: string;
}

export interface SubmitStageDecisionResponse {
  run: WorkflowRunDetails;
  decision: WorkflowDecisionItem;
  outcomeStatus: string;
  traceId?: string;
}

async function ensureOk<T>(res: Response, lang: AppLanguage): Promise<T> {
  if (!res.ok) {
    throw await parseProblemError(res, lang);
  }
  return (await res.json()) as T;
}

export async function getWorkflowInbox(lang: AppLanguage): Promise<WorkflowInboxResponse> {
  const res = await fetch('/api/v1/workflows/inbox', {
    method: 'GET',
    credentials: 'include',
    headers: { 'Accept-Language': lang },
  });
  return ensureOk<WorkflowInboxResponse>(res, lang);
}

export async function getWorkflowRunDetails(runId: string, lang: AppLanguage): Promise<WorkflowRunDetails> {
  const res = await fetch(`/api/v1/workflows/${runId}`, {
    method: 'GET',
    credentials: 'include',
    headers: { 'Accept-Language': lang },
  });
  return ensureOk<WorkflowRunDetails>(res, lang);
}

export async function getWorkflowHistory(runId: string, lang: AppLanguage): Promise<WorkflowHistoryResponse> {
  const res = await fetch(`/api/v1/workflows/${runId}/history`, {
    method: 'GET',
    credentials: 'include',
    headers: { 'Accept-Language': lang },
  });
  return ensureOk<WorkflowHistoryResponse>(res, lang);
}

export async function submitWorkflowStageDecision(
  runId: string,
  stageId: string,
  payload: { decision: WorkflowDecisionKind; notes?: string | null },
  lang: AppLanguage
): Promise<SubmitStageDecisionResponse> {
  const csrf = await fetchCsrfToken(lang);
  const res = await fetch(`/api/v1/workflows/${runId}/stages/${stageId}/decisions`, {
    method: 'POST',
    credentials: 'include',
    headers: {
      'Content-Type': 'application/json',
      [csrf.headerName]: csrf.token,
      'Accept-Language': lang,
    },
    body: JSON.stringify({
      decision: payload.decision,
      notes: payload.notes ?? null,
    }),
  });
  return ensureOk<SubmitStageDecisionResponse>(res, lang);
}

export type { ApiProblemError };
