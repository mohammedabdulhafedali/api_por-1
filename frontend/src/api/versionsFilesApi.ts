import { AppLanguage } from '../constants/uiBaseline';
import { ApiProblemError, fetchCsrfToken, parseProblemError } from './authApi';

export type FileScanStatus = 'CLEAN' | 'PENDING_SCAN' | 'INFECTED' | 'INVALID';
export type WordDerivativeStatus = 'VERIFIED' | 'PENDING_SCAN' | 'NO_RECOGNIZED_FIELDS' | 'FAILED';

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
  isSatisfied?: boolean;
  missingRequiredCount?: number;
  exceedsMaxCount?: boolean;
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
  isProtected?: boolean;
  isReadyForSubmission?: boolean;
  missingRequiredTypesCount?: number;
  submissionBlockersAr?: string[];
  submissionBlockersEn?: string[];
  latestWorkflowRunId?: string | null;
  latestWorkflowRunStatus?: string | null;
  latestDecisionNotes?: string | null;
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

export interface ApprovedChangeRequestSummary {
  id: string;
  sourceRevisionId: string;
  changeTypeId: string;
  changeTypeName: string;
  reason: string;
  affectedSourceFileIds: string[];
  createdAt: string;
}

export interface WordFieldUpdateEntry {
  fieldKey: string;
  mechanism: string;
  valueApplied: string;
  location: string;
}

export interface WordDerivativeRecordItem {
  id: string;
  revisionId: string;
  revisionFileId: string;
  sourceObjectId: string;
  sourceSha256: string;
  derivedObjectId?: string | null;
  derivedSha256?: string | null;
  derivedSizeBytes?: number | null;
  status: WordDerivativeStatus;
  templateKindDetected: string;
  fieldsUpdatedCount: number;
  matchedFields: WordFieldUpdateEntry[];
  validationErrors: string[];
  errorMessage?: string | null;
  isPublishable: boolean;
  createdBy: string;
  createdByName: string;
  createdAt: string;
  traceId?: string;
}

export interface RevisionDetailsResponse {
  revision: DocumentRevisionItem;
  integrationId: string;
  documentId: string;
  referenceCode: string;
  derivedTitle: string;
  canSubmit: boolean;
  canCreateWorkingCopy: boolean;
  canUploadFiles: boolean;
  canDeleteFiles: boolean;
  canDownloadFiles: boolean;
  files: RevisionFileItem[];
  wordDerivatives: WordDerivativeRecordItem[];
  traceId?: string;
}

export interface SubmitRevisionResponse {
  revision: DocumentRevisionItem;
  cleanFilesCount: number;
  traceId?: string;
  workflowRunId?: string | null;
}

export interface IntegrationFilesWorkspaceResponse {
  canRescanFiles?: boolean;
  canGenerateWordDerivatives?: boolean;
  integrationId: string;
  documentId: string;
  referenceCode: string;
  derivedTitle: string;
  canCreateDraftRevision: boolean;
  canSubmitRevision?: boolean;
  canCreateWorkingCopy?: boolean;
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
  approvedChangeRequests?: ApprovedChangeRequestSummary[];
  wordDerivatives?: WordDerivativeRecordItem[];
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

export async function listVersionRevisions(
  versionId: string,
  lang: AppLanguage
): Promise<DocumentRevisionItem[]> {
  const res = await fetch(`/api/v1/versions/${versionId}/revisions`, {
    method: 'GET',
    credentials: 'include',
    headers: { 'Accept-Language': lang },
  });
  return ensureOk<DocumentRevisionItem[]>(res, lang);
}

export async function getRevisionDetails(
  revisionId: string,
  lang: AppLanguage
): Promise<RevisionDetailsResponse> {
  const res = await fetch(`/api/v1/revisions/${revisionId}`, {
    method: 'GET',
    credentials: 'include',
    headers: { 'Accept-Language': lang },
  });
  return ensureOk<RevisionDetailsResponse>(res, lang);
}

export async function createWorkingCopyRevision(
  versionId: string,
  baseRevisionId: string,
  changeRequestId: string,
  lang: AppLanguage
): Promise<DocumentRevisionItem> {
  const csrf = await fetchCsrfToken(lang);
  const res = await fetch(`/api/v1/versions/${versionId}/revisions`, {
    method: 'POST',
    credentials: 'include',
    headers: {
      'Content-Type': 'application/json',
      [csrf.headerName]: csrf.token,
      'Accept-Language': lang,
    },
    body: JSON.stringify({ baseRevisionId, changeRequestId }),
  });
  return ensureOk<DocumentRevisionItem>(res, lang);
}

export async function submitDocumentRevision(
  revisionId: string,
  lang: AppLanguage
): Promise<SubmitRevisionResponse> {
  const csrf = await fetchCsrfToken(lang);
  const res = await fetch(`/api/v1/revisions/${revisionId}/submit`, {
    method: 'POST',
    credentials: 'include',
    headers: {
      [csrf.headerName]: csrf.token,
      'Accept-Language': lang,
    },
  });
  return ensureOk<SubmitRevisionResponse>(res, lang);
}

export async function generateWordDerivative(
  revisionId: string,
  fileId: string,
  lang: AppLanguage
): Promise<WordDerivativeRecordItem> {
  const csrf = await fetchCsrfToken(lang);
  const res = await fetch(`/api/v1/revisions/${revisionId}/files/${fileId}/word-derivative`, {
    method: 'POST',
    credentials: 'include',
    headers: {
      [csrf.headerName]: csrf.token,
      'Accept-Language': lang,
    },
  });
  return ensureOk<WordDerivativeRecordItem>(res, lang);
}

export async function getWordDerivativeReport(
  revisionId: string,
  fileId: string,
  lang: AppLanguage
): Promise<WordDerivativeRecordItem | null> {
  const res = await fetch(`/api/v1/revisions/${revisionId}/files/${fileId}/word-derivative`, {
    method: 'GET',
    credentials: 'include',
    headers: { 'Accept-Language': lang },
  });
  if (res.status === 204) {
    return null;
  }
  return ensureOk<WordDerivativeRecordItem>(res, lang);
}

export function getRevisionFileDownloadUrl(revisionId: string, fileId: string): string {
  return `/api/v1/revisions/${revisionId}/files/${fileId}/download`;
}

export function getWordDerivativeDownloadUrl(revisionId: string, fileId: string): string {
  return `/api/v1/revisions/${revisionId}/files/${fileId}/word-derivative/download`;
}

function extractFilenameFromDisposition(disposition: string | null, fallbackName: string): string {
  if (!disposition) return fallbackName;
  const utf8Match = /filename\*=UTF-8''([^;]+)/i.exec(disposition);
  if (utf8Match && utf8Match[1]) {
    try {
      return decodeURIComponent(utf8Match[1].trim());
    } catch {
      return utf8Match[1].trim();
    }
  }
  const plainMatch = /filename="?([^";]+)"?/i.exec(disposition);
  return plainMatch && plainMatch[1] ? plainMatch[1].trim() : fallbackName;
}

export async function downloadBinaryFileVerified(
  url: string,
  fallbackFilename: string,
  lang: AppLanguage
): Promise<{ filename: string; sha256?: string | null }> {
  const res = await fetch(url, {
    method: 'GET',
    credentials: 'include',
    headers: { 'Accept-Language': lang },
  });
  if (!res.ok) {
    throw await parseProblemError(res, lang);
  }
  const blob = await res.blob();
  const filename = extractFilenameFromDisposition(res.headers.get('Content-Disposition'), fallbackFilename);
  const sha256 = res.headers.get('X-File-Sha256');

  const objectUrl = URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  anchor.href = objectUrl;
  anchor.download = filename;
  document.body.appendChild(anchor);
  anchor.click();
  document.body.removeChild(anchor);
  URL.revokeObjectURL(objectUrl);

  return { filename, sha256 };
}

export type { ApiProblemError };
