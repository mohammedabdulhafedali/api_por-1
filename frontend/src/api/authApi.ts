import { AppLanguage } from '../constants/uiBaseline';

export interface RoleSummary {
  id: string;
  name: string;
}

export interface AuthenticatedUser {
  id: string;
  fullName: string;
  username?: string;
  email: string;
  isActive: boolean;
  consumerEntityId?: string | null;
  authProvider: string;
  roles: RoleSummary[];
  permissions: string[];
  traceId?: string;
}

export interface CsrfTokenPayload {
  token: string;
  headerName: string;
}

export class ApiProblemError extends Error {
  public readonly status: number;
  public readonly code: string;
  public readonly title: string;
  public readonly details?: Record<string, string[] | string>;
  public readonly traceId?: string;

  constructor(params: {
    status: number;
    code: string;
    title: string;
    message: string;
    details?: Record<string, string[] | string>;
    traceId?: string;
  }) {
    super(params.message);
    this.name = 'ApiProblemError';
    this.status = params.status;
    this.code = params.code;
    this.title = params.title;
    this.details = params.details;
    this.traceId = params.traceId;
  }
}

export async function parseProblemError(response: Response, lang: AppLanguage): Promise<ApiProblemError> {
  const fallbackTitle = lang === 'ar' ? 'تعذر إتمام العملية' : 'Request Failed';
  const fallbackMsg =
    lang === 'ar'
      ? 'حدث خطأ أثناء التواصل مع الخادم.'
      : 'An error occurred while communicating with the server.';

  try {
    const contentType = response.headers.get('content-type') ?? '';
    if (contentType.includes('json')) {
      const body = (await response.json()) as Record<string, unknown>;
      return new ApiProblemError({
        status: response.status,
        code: typeof body.code === 'string' ? body.code : `HTTP_${response.status}`,
        title: typeof body.title === 'string' ? body.title : fallbackTitle,
        message:
          typeof body.message === 'string'
            ? body.message
            : typeof body.detail === 'string'
              ? body.detail
              : fallbackMsg,
        details:
          body.details && typeof body.details === 'object'
            ? (body.details as Record<string, string[] | string>)
            : undefined,
        traceId: typeof body.traceId === 'string' ? body.traceId : undefined,
      });
    }
  } catch {
    // Ignore malformed error body
  }

  return new ApiProblemError({
    status: response.status,
    code: `HTTP_${response.status}`,
    title: fallbackTitle,
    message: fallbackMsg,
  });
}

export async function fetchCsrfToken(lang: AppLanguage): Promise<CsrfTokenPayload> {
  const response = await fetch('/api/v1/auth/csrf', {
    method: 'GET',
    credentials: 'include',
    headers: {
      'Accept-Language': lang,
    },
  });

  if (!response.ok) {
    throw await parseProblemError(response, lang);
  }

  return (await response.json()) as CsrfTokenPayload;
}

export async function fetchCurrentIdentity(lang: AppLanguage): Promise<AuthenticatedUser | null> {
  const response = await fetch('/api/v1/me', {
    method: 'GET',
    credentials: 'include',
    headers: {
      'Accept-Language': lang,
    },
  });

  if (response.status === 401) {
    return null;
  }

  if (!response.ok) {
    throw await parseProblemError(response, lang);
  }

  return (await response.json()) as AuthenticatedUser;
}

export async function loginWithLocalIdentity(
  username: string,
  password: string,
  lang: AppLanguage
): Promise<AuthenticatedUser> {
  const csrf = await fetchCsrfToken(lang);

  const response = await fetch('/api/v1/auth/login', {
    method: 'POST',
    credentials: 'include',
    headers: {
      'Content-Type': 'application/json',
      'Accept-Language': lang,
      [csrf.headerName]: csrf.token,
    },
    body: JSON.stringify({ username: username.trim(), password }),
  });

  if (!response.ok) {
    throw await parseProblemError(response, lang);
  }

  return (await response.json()) as AuthenticatedUser;
}

export async function logoutCurrentSession(lang: AppLanguage): Promise<void> {
  const csrf = await fetchCsrfToken(lang);

  const response = await fetch('/api/v1/auth/logout', {
    method: 'POST',
    credentials: 'include',
    headers: {
      'Accept-Language': lang,
      [csrf.headerName]: csrf.token,
    },
  });

  if (!response.ok && response.status !== 401) {
    throw await parseProblemError(response, lang);
  }
}

export async function changeOwnPassword(
  currentPassword: string,
  newPassword: string,
  lang: AppLanguage
): Promise<void> {
  const csrf = await fetchCsrfToken(lang);

  const response = await fetch('/api/v1/auth/password/change', {
    method: 'POST',
    credentials: 'include',
    headers: {
      'Content-Type': 'application/json',
      'Accept-Language': lang,
      [csrf.headerName]: csrf.token,
    },
    body: JSON.stringify({ currentPassword, newPassword }),
  });

  if (!response.ok) {
    throw await parseProblemError(response, lang);
  }
}

export async function completePasswordReset(
  email: string,
  token: string,
  newPassword: string,
  lang: AppLanguage
): Promise<void> {
  const csrf = await fetchCsrfToken(lang);

  const response = await fetch('/api/v1/auth/password/reset/complete', {
    method: 'POST',
    credentials: 'include',
    headers: {
      'Content-Type': 'application/json',
      'Accept-Language': lang,
      [csrf.headerName]: csrf.token,
    },
    body: JSON.stringify({
      email: email.trim(),
      token: token.trim(),
      newPassword,
    }),
  });

  if (!response.ok) {
    throw await parseProblemError(response, lang);
  }
}

