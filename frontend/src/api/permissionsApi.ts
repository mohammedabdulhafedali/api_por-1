import { AppLanguage } from '../constants/uiBaseline';
import { RoleSummary, fetchCsrfToken, parseProblemError } from './authApi';

export interface MatrixActionColumn {
  id: string;
  labelAr: string;
  labelEn: string;
}

export interface MatrixScreenRow {
  id: string;
  groupId: string;
  groupLabelAr: string;
  groupLabelEn: string;
  titleAr: string;
  titleEn: string;
  technicalPath: string;
  applicableColumnIds: string[];
}

export interface PermissionDictionaryItem {
  id: string;
  code: string;
  resource: string;
  action: string;
  screenId: string;
  groupId: string;
  columnId: string;
}

export interface PermissionsCatalogResult {
  permissions: PermissionDictionaryItem[];
  columns: MatrixActionColumn[];
  screens: MatrixScreenRow[];
  traceId?: string;
}

export interface RolePermissionsResult {
  roleId: string;
  roleName: string;
  isActive: boolean;
  isPinned: boolean;
  permissionIds: string[];
  permissionCodes: string[];
  traceId?: string;
}

export type OverrideDecision = 'GRANT' | 'REVOKE';
export type PermissionResolutionSource =
  | 'USER_REVOKE'
  | 'USER_GRANT'
  | 'ROLE_INHERITED'
  | 'DENIED_DEFAULT';

export interface UserPermissionEntry {
  permissionId: string;
  code: string;
  resource: string;
  action: string;
  screenId: string;
  groupId: string;
  columnId: string;
  isInherited: boolean;
  inheritedFromRoles: RoleSummary[];
  overrideDecision?: OverrideDecision | null;
  effectiveAllowed: boolean;
  resolutionSource: PermissionResolutionSource;
}

export interface UserPermissionsMatrixResult {
  userId: string;
  fullName: string;
  email: string;
  isActive: boolean;
  activeRoles: RoleSummary[];
  permissions: UserPermissionEntry[];
  columns: MatrixActionColumn[];
  screens: MatrixScreenRow[];
  effectiveAllowedCount: number;
  traceId?: string;
}

export async function getPermissionsCatalog(
  lang: AppLanguage
): Promise<PermissionsCatalogResult> {
  const response = await fetch('/api/v1/permissions', {
    method: 'GET',
    credentials: 'include',
    headers: { 'Accept-Language': lang },
  });

  if (!response.ok) {
    throw await parseProblemError(response, lang);
  }

  return (await response.json()) as PermissionsCatalogResult;
}

export async function getRolePermissions(
  roleId: string,
  lang: AppLanguage
): Promise<RolePermissionsResult> {
  const response = await fetch(`/api/v1/roles/${roleId}/permissions`, {
    method: 'GET',
    credentials: 'include',
    headers: { 'Accept-Language': lang },
  });

  if (!response.ok) {
    throw await parseProblemError(response, lang);
  }

  return (await response.json()) as RolePermissionsResult;
}

export async function replaceRolePermissions(
  roleId: string,
  permissionIds: string[],
  lang: AppLanguage
): Promise<RolePermissionsResult> {
  const csrf = await fetchCsrfToken(lang);

  const response = await fetch(`/api/v1/roles/${roleId}/permissions`, {
    method: 'PUT',
    credentials: 'include',
    headers: {
      'Content-Type': 'application/json',
      'Accept-Language': lang,
      [csrf.headerName]: csrf.token,
    },
    body: JSON.stringify({ permissionIds }),
  });

  if (!response.ok) {
    throw await parseProblemError(response, lang);
  }

  return (await response.json()) as RolePermissionsResult;
}

export async function getUserPermissionsMatrix(
  userId: string,
  lang: AppLanguage
): Promise<UserPermissionsMatrixResult> {
  const response = await fetch(`/api/v1/users/${userId}/permissions`, {
    method: 'GET',
    credentials: 'include',
    headers: { 'Accept-Language': lang },
  });

  if (!response.ok) {
    throw await parseProblemError(response, lang);
  }

  return (await response.json()) as UserPermissionsMatrixResult;
}

export async function replaceUserPermissionOverrides(
  userId: string,
  overrides: Array<{ permissionId: string; decision: OverrideDecision }>,
  lang: AppLanguage
): Promise<UserPermissionsMatrixResult> {
  const csrf = await fetchCsrfToken(lang);

  const response = await fetch(`/api/v1/users/${userId}/permission-overrides`, {
    method: 'PUT',
    credentials: 'include',
    headers: {
      'Content-Type': 'application/json',
      'Accept-Language': lang,
      [csrf.headerName]: csrf.token,
    },
    body: JSON.stringify({ overrides }),
  });

  if (!response.ok) {
    throw await parseProblemError(response, lang);
  }

  return (await response.json()) as UserPermissionsMatrixResult;
}

