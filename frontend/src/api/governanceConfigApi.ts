import { AppLanguage } from '../constants/uiBaseline';
import { fetchCsrfToken, parseProblemError } from './authApi';
import { PagedResult } from './masterDataApi';

export type RequirementRuleMode = 'REQUIRED' | 'OPTIONAL' | 'CONDITIONAL';

export interface RequirementFileTypeOption {
  id: string;
  catalogCode: string;
  displayName: string;
  extensions: string[];
  maxSizeBytes: number;
  isActive: boolean;
}

export interface RequirementRuleItem {
  id: string;
  fileTypeId: string;
  fileTypeName: string;
  catalogCode: string;
  extensions: string[];
  fileTypeIsActive: boolean;
  requirementMode: RequirementRuleMode | string;
  minCount: number;
  maxCount: number;
  conditionSpecJson?: string | null;
}

export interface FileRequirementTemplateItem {
  id: string;
  name: string;
  isActive: boolean;
  rules: RequirementRuleItem[];
  documentsCount: number;
  snapshotCount: number;
  latestSnapshotAt?: string | null;
}

export interface RequirementTemplateSnapshotItem {
  auditEventId: string;
  versionNumber: number;
  operation: string;
  capturedAtUtc: string;
  actorUserId?: string | null;
  actorName: string;
  templateName: string;
  isActive: boolean;
  rules: RequirementRuleItem[];
}

export interface RequirementRuleInput {
  fileTypeId: string;
  requirementMode: RequirementRuleMode;
  minCount: number;
  maxCount: number;
  conditionSpecJson?: string | null;
}

export interface ApprovalRouteRoleOption {
  id: string;
  name: string;
  isActive: boolean;
}

export interface ApprovalRouteStageItem {
  id: string;
  position: number;
  stageName: string;
  responsibleRoleId: string;
  responsibleRoleName: string;
  responsibleRoleIsActive: boolean;
}

export interface ApprovalRouteItem {
  id: string;
  name: string;
  isActive: boolean;
  stages: ApprovalRouteStageItem[];
  changeTypesCount: number;
  documentsCount: number;
  activeWorkflowRunsCount: number;
}

export interface ApprovalRouteStageInput {
  position: number;
  stageName: string;
  responsibleRoleId: string;
}

export interface ApprovalRouteOption {
  id: string;
  name: string;
  stageCount: number;
  isActive: boolean;
}

export interface ChangeTypeItem {
  id: string;
  name: string;
  routeId: string;
  routeName: string;
  routeIsActive: boolean;
  routeStageCount: number;
  isActive: boolean;
  policiesCount: number;
  changeRequestsCount: number;
}

export interface ChangePolicyTypeSummary {
  changeTypeId: string;
  changeTypeName: string;
  routeId: string;
  routeName: string;
  changeTypeIsActive: boolean;
}

export interface ChangePolicyItem {
  id: string;
  name: string;
  isActive: boolean;
  changeTypes: ChangePolicyTypeSummary[];
  documentsCount: number;
}

async function getJson<T>(url: string, lang: AppLanguage): Promise<T> {
  const response = await fetch(url, {
    method: 'GET',
    credentials: 'include',
    headers: { 'Accept-Language': lang },
  });
  if (!response.ok) {
    throw await parseProblemError(response, lang);
  }
  return (await response.json()) as T;
}

async function sendCsrfJson<T>(
  method: 'POST' | 'PATCH' | 'PUT',
  url: string,
  body: unknown,
  lang: AppLanguage
): Promise<T> {
  const csrf = await fetchCsrfToken(lang);
  const response = await fetch(url, {
    method,
    credentials: 'include',
    headers: {
      'Content-Type': 'application/json',
      'Accept-Language': lang,
      [csrf.headerName]: csrf.token,
    },
    body: JSON.stringify(body),
  });
  if (!response.ok) {
    throw await parseProblemError(response, lang);
  }
  return (await response.json()) as T;
}

function buildListQuery(params: {
  page: number;
  pageSize: number;
  search?: string;
  isActive?: boolean | null;
}): string {
  const q = new URLSearchParams();
  q.set('page', String(params.page));
  q.set('pageSize', String(params.pageSize));
  if (params.search?.trim()) {
    q.set('search', params.search.trim());
  }
  if (params.isActive !== undefined && params.isActive !== null) {
    q.set('isActive', String(params.isActive));
  }
  return q.toString();
}

// M05 • Requirement Templates & Snapshots (UI-23)
export const listRequirementFileTypeOptions = (lang: AppLanguage) =>
  getJson<RequirementFileTypeOption[]>('/api/v1/requirement-templates/file-types', lang);

export const listRequirementTemplates = (
  params: { page: number; pageSize: number; search?: string; isActive?: boolean | null },
  lang: AppLanguage
) => getJson<PagedResult<FileRequirementTemplateItem>>(`/api/v1/requirement-templates?${buildListQuery(params)}`, lang);

export const listRequirementTemplateSnapshots = (templateId: string, lang: AppLanguage) =>
  getJson<RequirementTemplateSnapshotItem[]>(`/api/v1/requirement-templates/${templateId}/snapshots`, lang);

export const createRequirementTemplate = (
  body: { name: string; rules: RequirementRuleInput[] },
  lang: AppLanguage
) => sendCsrfJson<FileRequirementTemplateItem>('POST', '/api/v1/requirement-templates', body, lang);

export const updateRequirementTemplate = (
  templateId: string,
  body: { name?: string; isActive?: boolean; rules?: RequirementRuleInput[] },
  lang: AppLanguage
) => sendCsrfJson<FileRequirementTemplateItem>('PATCH', `/api/v1/requirement-templates/${templateId}`, body, lang);

// M06 • Approval Routes & Stages (UI-05 & UI-05A)
export const listApprovalRouteRoles = (lang: AppLanguage) =>
  getJson<ApprovalRouteRoleOption[]>('/api/v1/approval-routes/roles', lang);

export const listApprovalRoutes = (
  params: { page: number; pageSize: number; search?: string; isActive?: boolean | null },
  lang: AppLanguage
) => getJson<PagedResult<ApprovalRouteItem>>(`/api/v1/approval-routes?${buildListQuery(params)}`, lang);

export const createApprovalRoute = (
  body: { name: string; stages?: ApprovalRouteStageInput[] },
  lang: AppLanguage
) => sendCsrfJson<ApprovalRouteItem>('POST', '/api/v1/approval-routes', body, lang);

export const updateApprovalRoute = (
  routeId: string,
  body: { name?: string; isActive?: boolean },
  lang: AppLanguage
) => sendCsrfJson<ApprovalRouteItem>('PATCH', `/api/v1/approval-routes/${routeId}`, body, lang);

export const replaceApprovalRouteStages = (
  routeId: string,
  body: { stages: ApprovalRouteStageInput[] },
  lang: AppLanguage
) => sendCsrfJson<ApprovalRouteItem>('PUT', `/api/v1/approval-routes/${routeId}/stages`, body, lang);

// M07 • Change Types & Change Policies (UI-06)
export const listAssignableRoutesForChangeTypes = (lang: AppLanguage) =>
  getJson<ApprovalRouteOption[]>('/api/v1/change-types/routes', lang);

export const listChangeTypes = (
  params: { page: number; pageSize: number; search?: string; isActive?: boolean | null },
  lang: AppLanguage
) => getJson<PagedResult<ChangeTypeItem>>(`/api/v1/change-types?${buildListQuery(params)}`, lang);

export const createChangeType = (
  body: { name: string; routeId: string },
  lang: AppLanguage
) => sendCsrfJson<ChangeTypeItem>('POST', '/api/v1/change-types', body, lang);

export const updateChangeType = (
  typeId: string,
  body: { name?: string; routeId?: string; isActive?: boolean },
  lang: AppLanguage
) => sendCsrfJson<ChangeTypeItem>('PATCH', `/api/v1/change-types/${typeId}`, body, lang);

export const listChangePolicies = (
  params: { page: number; pageSize: number; search?: string; isActive?: boolean | null },
  lang: AppLanguage
) => getJson<PagedResult<ChangePolicyItem>>(`/api/v1/change-policies?${buildListQuery(params)}`, lang);

export const createChangePolicy = (
  body: { name: string; changeTypeIds: string[] },
  lang: AppLanguage
) => sendCsrfJson<ChangePolicyItem>('POST', '/api/v1/change-policies', body, lang);

export const updateChangePolicy = (
  policyId: string,
  body: { name?: string; isActive?: boolean; changeTypeIds?: string[] },
  lang: AppLanguage
) => sendCsrfJson<ChangePolicyItem>('PATCH', `/api/v1/change-policies/${policyId}`, body, lang);
