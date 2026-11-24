import { AppLanguage } from '../constants/uiBaseline';
import { RoleSummary, fetchCsrfToken, parseProblemError } from './authApi';

export interface RoleDetails {
  id: string;
  name: string;
  isActive: boolean;
  isPinned: boolean;
  assignedUsersCount: number;
  grantedPermissionsCount: number;
  traceId?: string;
}

export interface PagedRolesResult {
  items: RoleDetails[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  traceId?: string;
}

export interface ReplaceUserRolesResult {
  userId: string;
  roles: RoleSummary[];
  effectivePermissionsCount: number;
  traceId?: string;
}

export async function listRoles(params: {
  page: number;
  pageSize: number;
  search?: string;
  isActive?: '' | 'true' | 'false';
  lang: AppLanguage;
}): Promise<PagedRolesResult> {
  const qs = new URLSearchParams();
  qs.set('page', String(params.page));
  qs.set('pageSize', String(params.pageSize));
  if (params.search?.trim()) qs.set('search', params.search.trim());
  if (params.isActive === 'true' || params.isActive === 'false') {
    qs.set('isActive', params.isActive);
  }

  const response = await fetch(`/api/v1/roles?${qs.toString()}`, {
    method: 'GET',
    credentials: 'include',
    headers: { 'Accept-Language': params.lang },
  });

  if (!response.ok) {
    throw await parseProblemError(response, params.lang);
  }

  return (await response.json()) as PagedRolesResult;
}

export async function createRole(
  payload: { name: string; isPinned: boolean },
  lang: AppLanguage
): Promise<RoleDetails> {
  const csrf = await fetchCsrfToken(lang);

  const response = await fetch('/api/v1/roles', {
    method: 'POST',
    credentials: 'include',
    headers: {
      'Content-Type': 'application/json',
      'Accept-Language': lang,
      [csrf.headerName]: csrf.token,
    },
    body: JSON.stringify({
      name: payload.name.trim(),
      isPinned: payload.isPinned,
    }),
  });

  if (!response.ok) {
    throw await parseProblemError(response, lang);
  }

  return (await response.json()) as RoleDetails;
}

export async function updateRole(
  roleId: string,
  payload: { name?: string; isActive?: boolean; isPinned?: boolean },
  lang: AppLanguage
): Promise<RoleDetails> {
  const csrf = await fetchCsrfToken(lang);

  const response = await fetch(`/api/v1/roles/${roleId}`, {
    method: 'PATCH',
    credentials: 'include',
    headers: {
      'Content-Type': 'application/json',
      'Accept-Language': lang,
      [csrf.headerName]: csrf.token,
    },
    body: JSON.stringify(payload),
  });

  if (!response.ok) {
    throw await parseProblemError(response, lang);
  }

  return (await response.json()) as RoleDetails;
}

export async function deleteRole(roleId: string, lang: AppLanguage): Promise<void> {
  const csrf = await fetchCsrfToken(lang);

  const response = await fetch(`/api/v1/roles/${roleId}`, {
    method: 'DELETE',
    credentials: 'include',
    headers: {
      'Accept-Language': lang,
      [csrf.headerName]: csrf.token,
    },
  });

  if (!response.ok) {
    throw await parseProblemError(response, lang);
  }
}

export async function replaceUserRoles(
  userId: string,
  roleIds: string[],
  lang: AppLanguage
): Promise<ReplaceUserRolesResult> {
  const csrf = await fetchCsrfToken(lang);

  const response = await fetch(`/api/v1/users/${userId}/roles`, {
    method: 'PUT',
    credentials: 'include',
    headers: {
      'Content-Type': 'application/json',
      'Accept-Language': lang,
      [csrf.headerName]: csrf.token,
    },
    body: JSON.stringify({ roleIds }),
  });

  if (!response.ok) {
    throw await parseProblemError(response, lang);
  }

  return (await response.json()) as ReplaceUserRolesResult;
}

