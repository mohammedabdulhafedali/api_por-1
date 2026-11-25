import { fetchCsrfToken, parseProblemError } from './authApi';
import { AppLanguage } from '../constants/uiBaseline';

async function ensureOk<T>(res: Response, lang: AppLanguage): Promise<T> {
  if (!res.ok) {
    throw await parseProblemError(res, lang);
  }
  return (await res.json()) as T;
}

export interface PublicationFileItem {
  id: string;
  revisionFileId: string;
  fileTypeId: string;
  fileTypeCode: string;
  fileTypeName: string;
  originalName: string;
  sizeBytes: number;
  mediaType: string;
  sha256: string;
  scanStatus: string;
}

export interface PublicationItem {
  id: string;
  integrationId: string;
  apiVersionId: string;
  versionLabel: string;
  revisionId: string;
  revisionNumber: number;
  state: string;
  publishedBy: string;
  publishedByName: string;
  publishedAt: string;
  withdrawnAt?: string | null;
  files: PublicationFileItem[];
}

export interface ConsumerVersionUsageItem {
  id: string;
  integrationId: string;
  apiVersionId: string;
  versionLabel: string;
  validFrom: string;
  validTo?: string | null;
  assignedBy: string;
  assignedByName: string;
}

export interface IntegrationPublicationsResponse {
  integrationId: string;
  documentId: string;
  documentTitle: string;
  publicationOption: string;
  publications: PublicationItem[];
  activeConsumerVersion?: ConsumerVersionUsageItem | null;
  canCreatePublication: boolean;
  canWithdrawPublication: boolean;
  canManageConsumerVersion: boolean;
  traceId: string;
}

export interface ConsumerVersionResponse {
  integrationId: string;
  activeUsage?: ConsumerVersionUsageItem | null;
  history: ConsumerVersionUsageItem[];
  canManage: boolean;
  traceId: string;
}

export interface PortalIntegrationItem {
  integrationId: string;
  referenceCode: string;
  documentId: string;
  documentTitle: string;
  providerName: string;
  apiGroupName: string;
  activeConsumerVersionLabel?: string | null;
  latestPublicationId: string;
  latestPublicationVersionLabel: string;
  latestPublicationRevisionNumber: number;
  latestPublishedAt: string;
  publishedFilesCount: number;
}

export interface PortalIntegrationsResponse {
  consumerEntityId: string;
  consumerEntityName: string;
  integrations: PortalIntegrationItem[];
  traceId: string;
}

export interface PortalPublicationFilesResponse {
  publicationId: string;
  integrationId: string;
  referenceCode: string;
  documentTitle: string;
  versionLabel: string;
  revisionNumber: number;
  publishedAt: string;
  files: PublicationFileItem[];
  traceId: string;
}

export async function getIntegrationPublications(
  integrationId: string,
  lang: AppLanguage
): Promise<IntegrationPublicationsResponse> {
  const res = await fetch(`/api/v1/integrations/${integrationId}/publications`, {
    method: 'GET',
    credentials: 'include',
    headers: { 'Accept-Language': lang },
  });
  return ensureOk<IntegrationPublicationsResponse>(res, lang);
}

export async function publishApprovedRevision(
  revisionId: string,
  revisionFileIds: string[],
  lang: AppLanguage
): Promise<PublicationItem> {
  const csrf = await fetchCsrfToken(lang);
  const res = await fetch(`/api/v1/revisions/${revisionId}/publications`, {
    method: 'POST',
    credentials: 'include',
    headers: {
      'Content-Type': 'application/json',
      [csrf.headerName]: csrf.token,
      'Accept-Language': lang,
    },
    body: JSON.stringify({ revisionFileIds }),
  });
  return ensureOk<PublicationItem>(res, lang);
}

export async function withdrawPublication(
  publicationId: string,
  lang: AppLanguage
): Promise<PublicationItem> {
  const csrf = await fetchCsrfToken(lang);
  const res = await fetch(`/api/v1/publications/${publicationId}/withdraw`, {
    method: 'POST',
    credentials: 'include',
    headers: {
      [csrf.headerName]: csrf.token,
      'Accept-Language': lang,
    },
  });
  return ensureOk<PublicationItem>(res, lang);
}

export async function getIntegrationConsumerVersion(
  integrationId: string,
  lang: AppLanguage
): Promise<ConsumerVersionResponse> {
  const res = await fetch(`/api/v1/integrations/${integrationId}/consumer-version`, {
    method: 'GET',
    credentials: 'include',
    headers: { 'Accept-Language': lang },
  });
  return ensureOk<ConsumerVersionResponse>(res, lang);
}

export async function setIntegrationConsumerVersion(
  integrationId: string,
  apiVersionId: string,
  lang: AppLanguage
): Promise<ConsumerVersionResponse> {
  const csrf = await fetchCsrfToken(lang);
  const res = await fetch(`/api/v1/integrations/${integrationId}/consumer-version`, {
    method: 'PUT',
    credentials: 'include',
    headers: {
      'Content-Type': 'application/json',
      [csrf.headerName]: csrf.token,
      'Accept-Language': lang,
    },
    body: JSON.stringify({ apiVersionId }),
  });
  return ensureOk<ConsumerVersionResponse>(res, lang);
}

export async function getPortalIntegrations(
  lang: AppLanguage
): Promise<PortalIntegrationsResponse> {
  const res = await fetch('/api/v1/portal/integrations', {
    method: 'GET',
    credentials: 'include',
    headers: { 'Accept-Language': lang },
  });
  return ensureOk<PortalIntegrationsResponse>(res, lang);
}

export async function getPortalPublicationFiles(
  publicationId: string,
  lang: AppLanguage
): Promise<PortalPublicationFilesResponse> {
  const res = await fetch(`/api/v1/portal/publications/${publicationId}/files`, {
    method: 'GET',
    credentials: 'include',
    headers: { 'Accept-Language': lang },
  });
  return ensureOk<PortalPublicationFilesResponse>(res, lang);
}

export function getPortalPublicationFileDownloadUrl(publicationId: string, revisionFileId: string): string {
  return `/api/v1/portal/publications/${publicationId}/files/${revisionFileId}/download`;
}
