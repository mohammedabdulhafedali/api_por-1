import { AppLanguage } from '../constants/uiBaseline';
import { fetchCsrfToken, parseProblemError } from './authApi';
import { PagedResult } from './masterDataApi';

export type DocumentProviderKind = 'INTERNAL' | 'EXTERNAL';
export type DocumentConsumerKind = 'COMPANY_SYSTEM' | 'CONSUMER_ENTITY';
export type DocumentPublicationOption = 'MANUAL' | 'POST_APPROVAL';
export type DocumentState = 'DRAFT' | 'ACTIVE' | 'ARCHIVED';
export type DocumentAclDecision = 'GRANT' | 'REVOKE';

export interface DocumentLookupOption {
  id: string;
  name: string;
  subtitle?: string | null;
}

export interface DocumentLookups {
  internalProviders: DocumentLookupOption[];
  externalProviders: DocumentLookupOption[];
  consumerCompanySystems: DocumentLookupOption[];
  consumerEntities: DocumentLookupOption[];
  apiGroups: DocumentLookupOption[];
  requirementTemplates: DocumentLookupOption[];
  approvalRoutes: DocumentLookupOption[];
  changePolicies: DocumentLookupOption[];
  activeUsers: DocumentLookupOption[];
}

export interface AssignedDeveloperItem {
  userId: string;
  fullName: string;
  email: string;
  isActive: boolean;
}

export interface IntegrationDeveloperItem extends AssignedDeveloperItem {
  hasDocView?: boolean;
  hasDocVersionManage?: boolean;
  hasDocFilesManage?: boolean;
  hasDocChange?: boolean;
  hasDocAssign?: boolean;
}

export interface ApiDocumentItem {
  id: string;
  derivedTitle: string;
  providerKind: DocumentProviderKind;
  providerInternalSystemId?: string | null;
  providerExternalSystemId?: string | null;
  providerName: string;
  consumerKind: DocumentConsumerKind;
  consumerCompanySystemId?: string | null;
  consumerEntityId?: string | null;
  consumerName: string;
  apiGroupId: string;
  apiGroupName: string;
  requirementTemplateId: string;
  requirementTemplateName: string;
  approvalRouteId: string;
  approvalRouteName: string;
  changePolicyId: string;
  changePolicyName: string;
  publicationOption: DocumentPublicationOption;
  state: DocumentState;
  createdBy: string;
  createdByName: string;
  createdAt: string;
  hasIntegration: boolean;
  integrationId?: string | null;
  integrationReferenceCode?: string | null;
  assignedDevelopers: AssignedDeveloperItem[];
  myDocumentPermissions: string[];
}

export interface CreateApiDocumentPayload {
  providerInternalSystemId?: string | null;
  providerExternalSystemId?: string | null;
  consumerCompanySystemId?: string | null;
  consumerEntityId?: string | null;
  apiGroupId: string;
  requirementTemplateId: string;
  approvalRouteId: string;
  changePolicyId: string;
  publicationOption: DocumentPublicationOption;
  state: DocumentState;
  developerUserIds?: string[];
}

export interface UpdateApiDocumentPayload {
  providerInternalSystemId?: string | null;
  providerExternalSystemId?: string | null;
  consumerCompanySystemId?: string | null;
  consumerEntityId?: string | null;
  apiGroupId?: string;
  requirementTemplateId?: string;
  approvalRouteId?: string;
  changePolicyId?: string;
  publicationOption?: DocumentPublicationOption;
  state?: DocumentState;
}

export interface DocumentPermissionColumn {
  id: string;
  code: string;
  actionKey: string;
  labelAr: string;
  labelEn: string;
}

export interface DocumentRoleAccessRow {
  roleId: string;
  roleName: string;
  isActive: boolean;
  decisions: Record<string, DocumentAclDecision | null>;
}

export interface DocumentUserEffectiveCell {
  allowed: boolean;
  resolutionSource: string;
  explicitDecision: DocumentAclDecision | null;
}

export interface DocumentUserAccessRow {
  userId: string;
  fullName: string;
  email: string;
  isActive: boolean;
  roleNames: string[];
  isAssignedDeveloper: boolean;
  cells: Record<string, DocumentUserEffectiveCell>;
}

export interface DocumentAccessMatrixResponse {
  documentId: string;
  derivedTitle: string;
  apiGroupName: string;
  createdBy: string;
  createdByName: string;
  permissions: DocumentPermissionColumn[];
  roles: DocumentRoleAccessRow[];
  users: DocumentUserAccessRow[];
}

export interface EligibleIntegrationDocument {
  documentId: string;
  derivedTitle: string;
  providerKind: DocumentProviderKind;
  providerName: string;
  consumerKind: DocumentConsumerKind;
  consumerName: string;
  apiGroupId: string;
  apiGroupName: string;
  state: DocumentState;
}

export interface IntegrationItem {
  id: string;
  documentId: string;
  referenceCode: string;
  derivedTitle: string;
  providerKind: DocumentProviderKind;
  providerName: string;
  consumerKind: DocumentConsumerKind;
  consumerName: string;
  apiGroupId: string;
  apiGroupName: string;
  requirementTemplateName: string;
  approvalRouteName: string;
  changePolicyName: string;
  publicationOption?: string;
  documentState: string;
  createdBy: string;
  createdByName: string;
  createdAt: string;
  versionsCount: number;
  isCurrentUserAssignedDeveloper?: boolean;
  assignedDevelopers: IntegrationDeveloperItem[];
  myDocumentPermissions: string[];
}

export interface IntegrationRequirementRuleSummary {
  fileTypeId: string;
  fileTypeName: string;
  catalogCode: string;
  extensions: string[];
  requirementMode: string;
  minCount: number;
  maxCount: number;
}

export interface IntegrationApprovalStageSummary {
  position: number;
  stageName: string;
  responsibleRoleId: string;
  responsibleRoleName: string;
}

export interface IntegrationChangeTypeSummary {
  changeTypeId: string;
  changeTypeName: string;
  routeId: string;
  routeName: string;
}

export interface IntegrationAuditEventSummary {
  id: string;
  operation: string;
  resourceKind: string;
  result: string;
  actorUserId?: string | null;
  actorName: string;
  recordedAt: string;
}

export interface IntegrationDetailsResponse {
  summary: IntegrationItem;
  documentDevelopers: IntegrationDeveloperItem[];
  requirementRules: IntegrationRequirementRuleSummary[];
  approvalStages: IntegrationApprovalStageSummary[];
  allowedChangeTypes: IntegrationChangeTypeSummary[];
  revisionsCount: number;
  changeRequestsCount: number;
  publicationsCount: number;
  recentAuditEvents: IntegrationAuditEventSummary[];
}

export interface IntegrationDevelopersResponse {
  integrationId: string;
  documentId: string;
  referenceCode: string;
  derivedTitle: string;
  isCurrentUserAssignedToIntegration: boolean;
  isCurrentUserAssignedToDocument: boolean;
  integrationDevelopers: IntegrationDeveloperItem[];
  documentDevelopers: IntegrationDeveloperItem[];
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
  method: 'POST' | 'PATCH' | 'PUT' | 'DELETE',
  url: string,
  body: unknown | null,
  lang: AppLanguage
): Promise<T> {
  const csrf = await fetchCsrfToken(lang);
  const headers: Record<string, string> = {
    'Accept-Language': lang,
    [csrf.headerName]: csrf.token,
  };
  if (body !== null) {
    headers['Content-Type'] = 'application/json';
  }
  const response = await fetch(url, {
    method,
    credentials: 'include',
    headers,
    body: body !== null ? JSON.stringify(body) : undefined,
  });
  if (!response.ok) {
    throw await parseProblemError(response, lang);
  }
  if (response.status === 204) {
    return undefined as T;
  }
  return (await response.json()) as T;
}

// D01-D03 • API Documents
export const getDocumentLookups = (lang: AppLanguage, createOnly = false) =>
  getJson<DocumentLookups>(
    createOnly ? '/api/v1/api-documents/form-lookups' : '/api/v1/api-documents/lookups',
    lang
  );

export const listApiDocuments = (
  params: { page: number; pageSize: number; search?: string; state?: string; hasIntegration?: boolean | null },
  lang: AppLanguage
) => {
  const q = new URLSearchParams();
  q.set('page', String(params.page));
  q.set('pageSize', String(params.pageSize));
  if (params.search?.trim()) q.set('search', params.search.trim());
  if (params.state?.trim()) q.set('state', params.state.trim());
  if (params.hasIntegration !== undefined && params.hasIntegration !== null) {
    q.set('hasIntegration', String(params.hasIntegration));
  }
  return getJson<PagedResult<ApiDocumentItem>>(`/api/v1/api-documents?${q.toString()}`, lang);
};

export const getApiDocument = (documentId: string, lang: AppLanguage) =>
  getJson<ApiDocumentItem>(`/api/v1/api-documents/${documentId}`, lang);

export const createApiDocument = (payload: CreateApiDocumentPayload, lang: AppLanguage) =>
  sendCsrfJson<ApiDocumentItem>('POST', '/api/v1/api-documents', payload, lang);

export const updateApiDocument = (
  documentId: string,
  payload: UpdateApiDocumentPayload,
  lang: AppLanguage
) => sendCsrfJson<ApiDocumentItem>('PATCH', `/api/v1/api-documents/${documentId}`, payload, lang);

export const deleteApiDocument = (documentId: string, lang: AppLanguage) =>
  sendCsrfJson<void>('DELETE', `/api/v1/api-documents/${documentId}`, null, lang);

export const replaceDocumentDevelopers = (
  documentId: string,
  developerUserIds: string[],
  lang: AppLanguage
) =>
  sendCsrfJson<ApiDocumentItem>(
    'PUT',
    `/api/v1/api-documents/${documentId}/developers`,
    { developerUserIds },
    lang
  );

// I07 & U02 • Document Access Control Matrix (UI-21A)
export const getDocumentAccessMatrix = (documentId: string, lang: AppLanguage) =>
  getJson<DocumentAccessMatrixResponse>(`/api/v1/api-documents/${documentId}/access`, lang);

export const replaceDocumentAccessMatrix = (
  documentId: string,
  payload: {
    rolePermissions: Array<{ roleId: string; permissionCode: string; decision: DocumentAclDecision }>;
    userPermissions: Array<{ userId: string; permissionCode: string; decision: DocumentAclDecision }>;
  },
  lang: AppLanguage
) =>
  sendCsrfJson<DocumentAccessMatrixResponse>(
    'PUT',
    `/api/v1/api-documents/${documentId}/access`,
    payload,
    lang
  );

// D04-D06 • Integrations 1:1, Developer Scope & Multi-Tab Details
export const listEligibleIntegrationDocuments = (lang: AppLanguage) =>
  getJson<EligibleIntegrationDocument[]>('/api/v1/integrations/eligible-documents', lang);

export const listIntegrationAssignableUsers = (lang: AppLanguage) =>
  getJson<Array<{ id: string; fullName: string; email: string }>>('/api/v1/integrations/assignable-users', lang);

export const listIntegrations = (
  params: { page: number; pageSize: number; search?: string; assignedToMe?: boolean | null; state?: string },
  lang: AppLanguage
) => {
  const q = new URLSearchParams();
  q.set('page', String(params.page));
  q.set('pageSize', String(params.pageSize));
  if (params.search?.trim()) q.set('search', params.search.trim());
  if (params.assignedToMe !== undefined && params.assignedToMe !== null) {
    q.set('assignedToMe', String(params.assignedToMe));
  }
  if (params.state?.trim()) q.set('state', params.state.trim());
  return getJson<PagedResult<IntegrationItem>>(`/api/v1/integrations?${q.toString()}`, lang);
};

export const getIntegrationDetails = (integrationId: string, lang: AppLanguage) =>
  getJson<IntegrationDetailsResponse>(`/api/v1/integrations/${integrationId}`, lang);

export const getIntegrationDevelopers = (integrationId: string, lang: AppLanguage) =>
  getJson<IntegrationDevelopersResponse>(`/api/v1/integrations/${integrationId}/developers`, lang);

export const replaceIntegrationDevelopers = (
  integrationId: string,
  developerUserIds: string[],
  lang: AppLanguage
) =>
  sendCsrfJson<IntegrationDevelopersResponse>(
    'PUT',
    `/api/v1/integrations/${integrationId}/developers`,
    { developerUserIds },
    lang
  );

export const createIntegration = (
  payload: { documentId: string; referenceCode?: string | null; developerUserIds?: string[] },
  lang: AppLanguage
) => sendCsrfJson<IntegrationItem>('POST', '/api/v1/integrations', payload, lang);

export const deleteIntegration = (integrationId: string, lang: AppLanguage) =>
  sendCsrfJson<void>('DELETE', `/api/v1/integrations/${integrationId}`, null, lang);

