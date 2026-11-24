import { AppLanguage } from '../constants/uiBaseline';
import { ApiProblemError, fetchCsrfToken, parseProblemError } from './authApi';

export type FileScanStatus = 'CLEAN' | 'PENDING_SCAN' | 'INFECTED' | 'INVALID';

export interface ApiVersionItem {
  id: string;
  integrationId: string;
  versionLabel: string;
  isArchived: boolean;
  createdAt: string;
  revisionsCount: number;
  latestRevisionStatus?: string | null;
}

export interface IntegrationVersionsResponse {
  integrationId: string;
  documentId: string;
  referenceCode: string;
  derivedTitle: string;
  providerName: string;
  consumerName: string;
  apiGroupName: string;
  canCreateVersion: boolean;
  isCurrentUserAssignedDeveloper: boolean;
  versions: ApiVersionItem[];
}

export interface RevisionRequirementProgress {
  fileTypeId: string;
  fileTypeName: string;
  catalogCode: string;
  extensions: string[];
  maxSizeBytes: number;
  requirementMode: string;
  minCount: number;
  maxCount: number;
  uploadedCleanCount: number;
  uploadedPendingScanCount: number;
}

export interface DocumentRevisionItem {
  id: string;
  apiVersionId: string;
  versionLabel: string;
  revisionNumber: number;
  baseRevisionId?: string | null;
  changeRequestId?: string | null;
  status: string;
  createdBy: string;
  createdByName: string;
  createdAt: string;
  approvedAt?: string | null;
  filesCount: number;
  pendingScanCount: number;
  requirements: RevisionRequirementProgress[];
}

export interface RevisionFileItem {
  id: string;
  revisionId: string;
  fileTypeId: string;
  fileTypeName: string;
  catalogCode: string;
  objectId: string;
  originalName: string;
  sha256: string;
  sizeBytes: number;
  mediaType?: string | null;
  scanStatus: FileScanStatus;
  isAvailableForUse: boolean;
  isQuarantined: boolean;
  sourceRevisionFileId?: string | null;
  uploadedBy: string;
  uploadedByName: string;
  uploadedAt: string;
}

export interface IntegrationFilesWorkspaceResponse {
  integrationId: string;
  documentId: string;
  referenceCode: string;
  derivedTitle: string;
  canCreateDraftRevision: boolean;
  canUploadFiles: boolean;
  canDeleteFiles: boolean;
  canDownloadFiles: boolean;
  isCurrentUserAssignedDeveloper: boolean;
  scannerConfigured: boolean;
  scannerSummaryAr: string;
  scannerSummaryEn: string;
  versions: ApiVersionItem[];
  selectedVersionId?: string | null;
  revisions: DocumentRevisionItem[];
  selectedRevision?: DocumentRevisionItem | null;
  files: RevisionFileItem[];
}

async function ensureOk<T>(res: Response, lang: AppLanguage): Promise<T> {
  if (!res.ok) {
    throw await parseProblemError(res, lang);
  }
  return (await res.json()) as T;
}

export async function listIntegrationVersions(
  integrationId: string,
  lang: AppLanguage
): Promise<IntegrationVersionsResponse> {
  const res = await fetch(`/api/v1/integrations/${integrationId}/versions`, {
    method: 'GET',
    credentials: 'include',
    headers: { 'Accept-Language': lang },
  });
  return ensureOk<IntegrationVersionsResponse>(res, lang);
}

export async function createIntegrationVersion(
  integrationId: string,
  versionLabel: string,
  lang: AppLanguage
): Promise<ApiVersionItem> {
  const csrf = await fetchCsrfToken(lang);
  const res = await fetch(`/api/v1/integrations/${integrationId}/versions`, {
    method: 'POST',
    credentials: 'include',
    headers: {
      'Content-Type': 'application/json',
      [csrf.headerName]: csrf.token,
      'Accept-Language': lang,
    },
    body: JSON.stringify({ versionLabel }),
  });
  return ensureOk<ApiVersionItem>(res, lang);
}

export async function getIntegrationFilesWorkspace(
  integrationId: string,
  opts: { versionId?: string; revisionId?: string } = {},
  lang: AppLanguage
): Promise<IntegrationFilesWorkspaceResponse> {
  const params = new URLSearchParams();
  if (opts.versionId) params.set('versionId', opts.versionId);
  if (opts.revisionId) params.set('revisionId', opts.revisionId);
  const qs = params.toString();
  const url = qs
    ? `/api/v1/integrations/${integrationId}/files-workspace?${qs}`
    : `/api/v1/integrations/${integrationId}/files-workspace`;

  const res = await fetch(url, {
    method: 'GET',
    credentials: 'include',
    headers: { 'Accept-Language': lang },
  });
  return ensureOk<IntegrationFilesWorkspaceResponse>(res, lang);
}

export async function createDraftDocumentRevision(
  versionId: string,
  lang: AppLanguage
): Promise<DocumentRevisionItem> {
  const csrf = await fetchCsrfToken(lang);
  const res = await fetch(`/api/v1/versions/${versionId}/revisions`, {
    method: 'POST',
    credentials: 'include',
    headers: {
      [csrf.headerName]: csrf.token,
      'Accept-Language': lang,
    },
  });
  return ensureOk<DocumentRevisionItem>(res, lang);
}

export async function uploadRevisionFile(
  revisionId: string,
  fileTypeId: string,
  file: File,
  lang: AppLanguage
): Promise<{ statusCode: number; file: RevisionFileItem }> {
  const csrf = await fetchCsrfToken(lang);
  const formData = new FormData();
  formData.append('fileTypeId', fileTypeId);
  formData.append('file', file);

  const res = await fetch(`/api/v1/revisions/${revisionId}/files`, {
    method: 'POST',
    credentials: 'include',
    headers: {
      [csrf.headerName]: csrf.token,
      'Accept-Language': lang,
    },
    body: formData,
  });

  if (!res.ok) {
    throw await parseProblemError(res, lang);
  }

  const dto = (await res.json()) as RevisionFileItem;
  return { statusCode: res.status, file: dto };
}

export async function rescanRevisionFile(
  revisionId: string,
  fileId: string,
  lang: AppLanguage
): Promise<{ statusCode: number; file: RevisionFileItem }> {
  const csrf = await fetchCsrfToken(lang);
  const res = await fetch(`/api/v1/revisions/${revisionId}/files/${fileId}/rescan`, {
    method: 'POST',
    credentials: 'include',
    headers: {
      [csrf.headerName]: csrf.token,
      'Accept-Language': lang,
    },
  });

  if (!res.ok) {
    throw await parseProblemError(res, lang);
  }

  const dto = (await res.json()) as RevisionFileItem;
  return { statusCode: res.status, file: dto };
}

export async function deleteDraftRevisionFile(
  revisionId: string,
  fileId: string,
  lang: AppLanguage
): Promise<void> {
  const csrf = await fetchCsrfToken(lang);
  const res = await fetch(`/api/v1/revisions/${revisionId}/files/${fileId}`, {
    method: 'DELETE',
    credentials: 'include',
    headers: {
      [csrf.headerName]: csrf.token,
      'Accept-Language': lang,
    },
  });
  if (!res.ok) {
    throw await parseProblemError(res, lang);
  }
}

export function getRevisionFileDownloadUrl(revisionId: string, fileId: string): string {
  return `/api/v1/revisions/${revisionId}/files/${fileId}/download`;
}

export type { ApiProblemError };
