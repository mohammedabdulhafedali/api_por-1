import { AppLanguage } from '../constants/uiBaseline';
import { fetchCsrfToken, parseProblemError } from './authApi';

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  traceId?: string;
}

export type CompanySystemUsageType = 'NONE' | 'PROVIDER' | 'CONSUMER' | 'BOTH';

export interface CompanySystemItem {
  id: string;
  name: string;
  description?: string | null;
  isActive: boolean;
  computedUsageType: CompanySystemUsageType;
  isRegisteredInternalProvider: boolean;
  documentsAsProviderCount: number;
  documentsAsConsumerCount: number;
}

export interface InternalProviderItem {
  id: string;
  companySystemId: string;
  companySystemName: string;
  companySystemDescription?: string | null;
  companySystemIsActive: boolean;
  isActive: boolean;
  documentsCount: number;
}

export interface ExternalProviderItem {
  id: string;
  name: string;
  description?: string | null;
  isActive: boolean;
  documentsCount: number;
}

export interface ConsumerCategoryItem {
  id: string;
  name: string;
  isActive: boolean;
  consumersCount: number;
}

export interface ConsumerItem {
  id: string;
  categoryId: string;
  categoryName: string;
  categoryIsActive: boolean;
  name: string;
  isActive: boolean;
  documentsCount: number;
  assignedUsersCount: number;
}

export interface ApiGroupItem {
  id: string;
  name: string;
  description?: string | null;
  isActive: boolean;
  documentsCount: number;
}

export interface FileTypeCatalogEntry {
  catalogCode: string;
  defaultNameAr: string;
  defaultNameEn: string;
  allowedExtensions: string[];
  defaultMaxSizeBytes: number;
  supportsWordTemplate: boolean;
}

export interface FileTypeItem {
  id: string;
  catalogCode: string;
  displayName: string;
  maxSizeBytes: number;
  wordTemplateObjectId?: string | null;
  isActive: boolean;
  supportsWordTemplate: boolean;
  extensions: string[];
  requirementRulesCount: number;
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
  method: 'POST' | 'PATCH',
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

function buildListQs(params: {
  page: number;
  pageSize: number;
  search?: string;
  isActive?: '' | 'true' | 'false';
  extra?: Record<string, string | undefined>;
}): string {
  const qs = new URLSearchParams();
  qs.set('page', String(params.page));
  qs.set('pageSize', String(params.pageSize));
  if (params.search?.trim()) qs.set('search', params.search.trim());
  if (params.isActive === 'true' || params.isActive === 'false') qs.set('isActive', params.isActive);
  if (params.extra) {
    for (const [k, v] of Object.entries(params.extra)) {
      if (v) qs.set(k, v);
    }
  }
  return qs.toString();
}

// M01 Company Systems
export const listCompanySystems = (params: {
  page: number;
  pageSize: number;
  search?: string;
  isActive?: '' | 'true' | 'false';
  usageType?: string;
  lang: AppLanguage;
}) =>
  getJson<PagedResult<CompanySystemItem>>(
    `/api/v1/company-systems?${buildListQs({ ...params, extra: { usageType: params.usageType } })}`,
    params.lang
  );

export const createCompanySystem = (
  payload: { name: string; description?: string | null },
  lang: AppLanguage
) => sendCsrfJson<CompanySystemItem>('POST', '/api/v1/company-systems', payload, lang);

export const updateCompanySystem = (
  systemId: string,
  payload: { name?: string; description?: string | null; clearDescription?: boolean; isActive?: boolean },
  lang: AppLanguage
) => sendCsrfJson<CompanySystemItem>('PATCH', `/api/v1/company-systems/${systemId}`, payload, lang);

// M02 Internal & External Providers
export const listInternalProviders = (params: {
  page: number;
  pageSize: number;
  search?: string;
  isActive?: '' | 'true' | 'false';
  lang: AppLanguage;
}) =>
  getJson<PagedResult<InternalProviderItem>>(
    `/api/v1/internal-providers?${buildListQs(params)}`,
    params.lang
  );

export const createInternalProvider = (companySystemId: string, lang: AppLanguage) =>
  sendCsrfJson<InternalProviderItem>('POST', '/api/v1/internal-providers', { companySystemId }, lang);

export const updateInternalProvider = (providerId: string, isActive: boolean, lang: AppLanguage) =>
  sendCsrfJson<InternalProviderItem>('PATCH', `/api/v1/internal-providers/${providerId}`, { isActive }, lang);

export const listExternalProviders = (params: {
  page: number;
  pageSize: number;
  search?: string;
  isActive?: '' | 'true' | 'false';
  lang: AppLanguage;
}) =>
  getJson<PagedResult<ExternalProviderItem>>(
    `/api/v1/external-providers?${buildListQs(params)}`,
    params.lang
  );

export const createExternalProvider = (
  payload: { name: string; description?: string | null },
  lang: AppLanguage
) => sendCsrfJson<ExternalProviderItem>('POST', '/api/v1/external-providers', payload, lang);

export const updateExternalProvider = (
  providerId: string,
  payload: { name?: string; description?: string | null; clearDescription?: boolean; isActive?: boolean },
  lang: AppLanguage
) => sendCsrfJson<ExternalProviderItem>('PATCH', `/api/v1/external-providers/${providerId}`, payload, lang);

// M03 Consumer Categories & Consumers
export const listConsumerCategories = (params: {
  page: number;
  pageSize: number;
  search?: string;
  isActive?: '' | 'true' | 'false';
  lang: AppLanguage;
}) =>
  getJson<PagedResult<ConsumerCategoryItem>>(
    `/api/v1/consumer-categories?${buildListQs(params)}`,
    params.lang
  );

export const createConsumerCategory = (name: string, lang: AppLanguage) =>
  sendCsrfJson<ConsumerCategoryItem>('POST', '/api/v1/consumer-categories', { name }, lang);

export const updateConsumerCategory = (
  categoryId: string,
  payload: { name?: string; isActive?: boolean },
  lang: AppLanguage
) => sendCsrfJson<ConsumerCategoryItem>('PATCH', `/api/v1/consumer-categories/${categoryId}`, payload, lang);

export const listConsumers = (params: {
  page: number;
  pageSize: number;
  search?: string;
  isActive?: '' | 'true' | 'false';
  categoryId?: string;
  lang: AppLanguage;
}) =>
  getJson<PagedResult<ConsumerItem>>(
    `/api/v1/consumers?${buildListQs({ ...params, extra: { categoryId: params.categoryId } })}`,
    params.lang
  );

export const createConsumer = (
  payload: { categoryId: string; name: string },
  lang: AppLanguage
) => sendCsrfJson<ConsumerItem>('POST', '/api/v1/consumers', payload, lang);

export const updateConsumer = (
  consumerId: string,
  payload: { categoryId?: string; name?: string; isActive?: boolean },
  lang: AppLanguage
) => sendCsrfJson<ConsumerItem>('PATCH', `/api/v1/consumers/${consumerId}`, payload, lang);

// M04 API Groups
export const listApiGroups = (params: {
  page: number;
  pageSize: number;
  search?: string;
  isActive?: '' | 'true' | 'false';
  lang: AppLanguage;
}) =>
  getJson<PagedResult<ApiGroupItem>>(
    `/api/v1/api-groups?${buildListQs(params)}`,
    params.lang
  );

export const createApiGroup = (
  payload: { name: string; description?: string | null },
  lang: AppLanguage
) => sendCsrfJson<ApiGroupItem>('POST', '/api/v1/api-groups', payload, lang);

export const updateApiGroup = (
  groupId: string,
  payload: { name?: string; description?: string | null; clearDescription?: boolean; isActive?: boolean },
  lang: AppLanguage
) => sendCsrfJson<ApiGroupItem>('PATCH', `/api/v1/api-groups/${groupId}`, payload, lang);

// M04 File Types Dictionary
export const getCommonFileTypesCatalog = (lang: AppLanguage) =>
  getJson<FileTypeCatalogEntry[]>('/api/v1/file-types/catalog', lang);

export const listFileTypes = (params: {
  page: number;
  pageSize: number;
  search?: string;
  isActive?: '' | 'true' | 'false';
  lang: AppLanguage;
}) =>
  getJson<PagedResult<FileTypeItem>>(
    `/api/v1/file-types?${buildListQs(params)}`,
    params.lang
  );

export const createFileType = (
  payload: {
    catalogCode: string;
    displayName: string;
    maxSizeBytes: number;
    wordTemplateObjectId?: string | null;
    extensions?: string[];
  },
  lang: AppLanguage
) => sendCsrfJson<FileTypeItem>('POST', '/api/v1/file-types', payload, lang);

export const updateFileType = (
  fileTypeId: string,
  payload: {
    displayName?: string;
    maxSizeBytes?: number;
    wordTemplateObjectId?: string | null;
    clearWordTemplate?: boolean;
    isActive?: boolean;
    extensions?: string[];
  },
  lang: AppLanguage
) => sendCsrfJson<FileTypeItem>('PATCH', `/api/v1/file-types/${fileTypeId}`, payload, lang);

