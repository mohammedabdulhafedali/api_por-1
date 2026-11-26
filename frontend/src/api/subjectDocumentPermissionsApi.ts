import { AppLanguage } from '../constants/uiBaseline';
import { fetchCsrfToken, parseProblemError } from './authApi';

export type PermissionSubjectKind = 'users' | 'roles';
export type AclDecision = 'GRANT' | 'REVOKE' | null;
export interface SubjectDocumentCell {
  decision: AclDecision;
  defaultAllowed: boolean;
  allowed: boolean;
  source: string;
}
export interface SubjectDocumentRow {
  documentId: string;
  title: string;
  groupName: string;
  canManage: boolean;
  cells: Record<string, SubjectDocumentCell>;
}
export interface SubjectDocumentPage {
  permissions: Array<{ code: string; actionKey: string; labelAr: string; labelEn: string }>;
  items: SubjectDocumentRow[];
  page: number;
  totalPages: number;
  totalCount: number;
}
export interface SubjectDocumentChange {
  documentId: string;
  permissionCode: string;
  expectedDecision: AclDecision;
  decision: AclDecision;
}

export async function getSubjectDocumentPermissions(kind: PermissionSubjectKind, id: string,
  page: number, search: string, lang: AppLanguage, signal?: AbortSignal): Promise<SubjectDocumentPage> {
  const query = new URLSearchParams({ page: String(page), search });
  const response = await fetch(`/api/v1/document-permissions/${kind}/${id}/?${query}`, {
    credentials: 'include', headers: { 'Accept-Language': lang }, signal,
  });
  if (!response.ok) throw await parseProblemError(response, lang);
  return response.json() as Promise<SubjectDocumentPage>;
}

export async function patchSubjectDocumentPermissions(kind: PermissionSubjectKind, id: string,
  changes: SubjectDocumentChange[], lang: AppLanguage): Promise<void> {
  const csrf = await fetchCsrfToken(lang);
  const response = await fetch(`/api/v1/document-permissions/${kind}/${id}/`, {
    method: 'PATCH', credentials: 'include', headers: {
      'Content-Type': 'application/json', 'Accept-Language': lang, [csrf.headerName]: csrf.token,
    }, body: JSON.stringify({ changes }),
  });
  if (!response.ok) throw await parseProblemError(response, lang);
}
