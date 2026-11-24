import { AppLanguage } from '../constants/uiBaseline';
import { RoleSummary, fetchCsrfToken, parseProblemError } from './authApi';

export interface UserSummary {
  id: string;
  fullName: string;
  email: string;
  isActive: boolean;
  isActivated: boolean;
  isLockedOut: boolean;
  lockoutEnd?: string | null;
  consumerEntityId?: string | null;
  createdAt: string;
  roles: RoleSummary[];
  traceId?: string;
}

export interface PagedUsersResult {
  items: UserSummary[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  traceId?: string;
}

export interface CreateUserPayload {
  fullName: string;
  email: string;
  consumerEntityId?: string | null;
  triggerActivationToken: boolean;
  roleIds?: string[];
}

export interface UpdateUserPayload {
  fullName?: string;
  isActive?: boolean;
  consumerEntityId?: string | null;
  clearConsumerEntity?: boolean;
}

export async function listUsers(params: {
  page: number;
  pageSize: number;
  search?: string;
  isActive?: '' | 'true' | 'false';
  lang: AppLanguage;
}): Promise<PagedUsersResult> {
  const qs = new URLSearchParams();
  qs.set('page', String(params.page));
  qs.set('pageSize', String(params.pageSize));
  if (params.search?.trim()) qs.set('search', params.search.trim());
  if (params.isActive === 'true' || params.isActive === 'false') {
    qs.set('isActive', params.isActive);
  }

  const response = await fetch(`/api/v1/users?${qs.toString()}`, {
    method: 'GET',
    credentials: 'include',
    headers: {
      'Accept-Language': params.lang,
    },
  });

  if (!response.ok) {
    throw await parseProblemError(response, params.lang);
  }

  return (await response.json()) as PagedUsersResult;
}

export async function createUserAccount(
  payload: CreateUserPayload,
  lang: AppLanguage
): Promise<UserSummary> {
  const csrf = await fetchCsrfToken(lang);

  const response = await fetch('/api/v1/users', {
    method: 'POST',
    credentials: 'include',
    headers: {
      'Content-Type': 'application/json',
      'Accept-Language': lang,
      [csrf.headerName]: csrf.token,
    },
    body: JSON.stringify({
      fullName: payload.fullName.trim(),
      email: payload.email.trim(),
      consumerEntityId: payload.consumerEntityId?.trim() || null,
      triggerActivationToken: payload.triggerActivationToken,
      roleIds: payload.roleIds ?? [],
    }),
  });

  if (!response.ok) {
    throw await parseProblemError(response, lang);
  }

  return (await response.json()) as UserSummary;
}

export async function updateUserAccount(
  userId: string,
  payload: UpdateUserPayload,
  lang: AppLanguage
): Promise<UserSummary> {
  const csrf = await fetchCsrfToken(lang);

  const response = await fetch(`/api/v1/users/${userId}`, {
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

  return (await response.json()) as UserSummary;
}

export async function triggerUserActivationOrReset(
  userId: string,
  lang: AppLanguage
): Promise<void> {
  const csrf = await fetchCsrfToken(lang);

  const response = await fetch(`/api/v1/users/${userId}/password/reset`, {
    method: 'POST',
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

