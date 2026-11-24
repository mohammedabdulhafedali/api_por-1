import { fireEvent, render, screen, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { App } from '../App';
import { AuthenticatedUser } from '../api/authApi';

const S04_ADMIN: AuthenticatedUser = {
  id: '10000000-0000-0000-0000-000000000001',
  fullName: 'مدير النظام S04',
  email: 'admin@api-governance.local',
  isActive: true,
  authProvider: 'LocalIdentity',
  roles: [{ id: '20000000-0000-0000-0000-000000000001', name: 'SystemAdmin' }],
  permissions: [
    'company_systems.view',
    'company_systems.create',
    'company_systems.edit',
    'providers.view',
    'providers.create',
    'providers.edit',
    'consumers.view',
    'consumers.manage',
    'api_groups.view',
    'api_groups.manage',
    'file_types.view',
    'file_types.manage',
  ],
};

describe('Sprint S04 Master Data & File Types Screens (UI-01..UI-04)', () => {
  beforeEach(() => {
    window.localStorage.clear();
    document.documentElement.lang = 'ar';
    document.documentElement.dir = 'rtl';
    document.documentElement.setAttribute('data-theme', 'light');
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('UI-01 & M04: renders computed usage types, creates company system (no editable usage field), and creates API group', async () => {
    let systems: Array<Record<string, unknown>> = [
      {
        id: 'sys-1',
        name: 'CoreBilling',
        description: 'Billing Engine',
        isActive: true,
        computedUsageType: 'BOTH',
        isRegisteredInternalProvider: true,
        documentsAsProviderCount: 2,
        documentsAsConsumerCount: 1,
      },
    ];
    let apiGroups: Array<Record<string, unknown>> = [
      { id: 'grp-1', name: 'Payments', description: 'Payment APIs', isActive: true, documentsCount: 0 },
    ];

    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url.endsWith('/api/v1/auth/csrf')) {
        return new Response(JSON.stringify({ token: 'csrf-s04', headerName: 'X-CSRF-TOKEN' }), { status: 200 });
      }
      if (url.startsWith('/api/v1/company-systems?')) {
        return new Response(
          JSON.stringify({ items: systems, page: 1, pageSize: 10, totalCount: systems.length, totalPages: 1 }),
          { status: 200 }
        );
      }
      if (url === '/api/v1/company-systems' && init?.method === 'POST') {
        const body = JSON.parse(String(init.body)) as { name: string };
        const created = {
          id: 'sys-2',
          name: body.name,
          description: null,
          isActive: true,
          computedUsageType: 'NONE',
          isRegisteredInternalProvider: false,
          documentsAsProviderCount: 0,
          documentsAsConsumerCount: 0,
        };
        systems = [...systems, created];
        return new Response(JSON.stringify(created), { status: 201 });
      }
      if (url.startsWith('/api/v1/api-groups?')) {
        return new Response(
          JSON.stringify({ items: apiGroups, page: 1, pageSize: 10, totalCount: apiGroups.length, totalPages: 1 }),
          { status: 200 }
        );
      }
      if (url === '/api/v1/api-groups' && init?.method === 'POST') {
        const body = JSON.parse(String(init.body)) as { name: string };
        const created = { id: 'grp-2', name: body.name, description: null, isActive: true, documentsCount: 0 };
        apiGroups = [...apiGroups, created];
        return new Response(JSON.stringify(created), { status: 201 });
      }
      return new Response('{}', { status: 404 });
    });

    vi.stubGlobal('fetch', fetchMock);
    render(<App initialAuthenticatedUser={S04_ADMIN} />);

    fireEvent.click(screen.getByRole('button', { name: /أنظمة الشركة/ }));
    expect(await screen.findByText('CoreBilling')).toBeInTheDocument();
    expect(screen.getByText('مزوّد ومستهلك معًا')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'إضافة نظام شركة' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    fireEvent.change(screen.getByLabelText(/الاسم/), { target: { value: 'LedgerService' } });
    fireEvent.click(screen.getByRole('button', { name: 'حفظ وإنشاء' }));

    const dlg1 = await screen.findByRole('dialog');
    fireEvent.click(within(dlg1).getByRole('button', { name: 'إغلاق نافذة الرسالة' }));
    expect(await screen.findByText('LedgerService')).toBeInTheDocument();

    // Switch to API Groups tab (M04)
    fireEvent.click(screen.getByRole('button', { name: /مجموعات API/ }));
    expect(await screen.findByText('Payments')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'إضافة مجموعة API' }));
    fireEvent.change(screen.getByLabelText(/الاسم/), { target: { value: 'Settlement' } });
    fireEvent.click(screen.getByRole('button', { name: 'حفظ وإنشاء' }));
    const dlg2 = await screen.findByRole('dialog');
    fireEvent.click(within(dlg2).getByRole('button', { name: 'إغلاق نافذة الرسالة' }));
    expect(await screen.findByText('Settlement')).toBeInTheDocument();
  });

  it('UI-03 & UI-04: manages internal/external providers (no company field on external) and file types catalog with conditional Word template field', async () => {
    const fileTypesCatalog = [
      {
        catalogCode: 'OPENAPI_SPEC',
        defaultNameAr: 'مواصفة OpenAPI / Swagger',
        defaultNameEn: 'OpenAPI / Swagger Specification',
        allowedExtensions: ['.yaml', '.yml', '.json'],
        defaultMaxSizeBytes: 10485760,
        supportsWordTemplate: false,
      },
      {
        catalogCode: 'WORD_DOC',
        defaultNameAr: 'مستند Word التوثيقي',
        defaultNameEn: 'Microsoft Word Document (.docx)',
        allowedExtensions: ['.docx'],
        defaultMaxSizeBytes: 20971520,
        supportsWordTemplate: true,
      },
    ];
    let fileTypes: Array<Record<string, unknown>> = [];
    let externalProviders: Array<Record<string, unknown>> = [];

    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url.endsWith('/api/v1/auth/csrf')) {
        return new Response(JSON.stringify({ token: 'csrf-ui04', headerName: 'X-CSRF-TOKEN' }), { status: 200 });
      }
      if (url.startsWith('/api/v1/internal-providers?')) {
        return new Response(JSON.stringify({ items: [], page: 1, pageSize: 10, totalCount: 0, totalPages: 1 }), { status: 200 });
      }
      if (url.startsWith('/api/v1/external-providers?')) {
        return new Response(
          JSON.stringify({ items: externalProviders, page: 1, pageSize: 10, totalCount: externalProviders.length, totalPages: 1 }),
          { status: 200 }
        );
      }
      if (url === '/api/v1/external-providers' && init?.method === 'POST') {
        const body = JSON.parse(String(init.body)) as { name: string };
        const created = { id: 'ext-1', name: body.name, description: null, isActive: true, documentsCount: 0 };
        externalProviders = [created];
        return new Response(JSON.stringify(created), { status: 201 });
      }
      if (url === '/api/v1/file-types/catalog') {
        return new Response(JSON.stringify(fileTypesCatalog), { status: 200 });
      }
      if (url.startsWith('/api/v1/file-types?')) {
        return new Response(
          JSON.stringify({ items: fileTypes, page: 1, pageSize: 10, totalCount: fileTypes.length, totalPages: 1 }),
          { status: 200 }
        );
      }
      if (url === '/api/v1/file-types' && init?.method === 'POST') {
        const body = JSON.parse(String(init.body)) as {
          catalogCode: string;
          displayName: string;
          maxSizeBytes: number;
          extensions: string[];
        };
        const created = {
          id: 'ft-1',
          catalogCode: body.catalogCode,
          displayName: body.displayName,
          maxSizeBytes: body.maxSizeBytes,
          wordTemplateObjectId: null,
          isActive: true,
          supportsWordTemplate: body.catalogCode === 'WORD_DOC',
          extensions: body.extensions,
          requirementRulesCount: 0,
        };
        fileTypes = [created];
        return new Response(JSON.stringify(created), { status: 201 });
      }
      return new Response('{}', { status: 404 });
    });

    vi.stubGlobal('fetch', fetchMock);
    render(<App initialAuthenticatedUser={S04_ADMIN} />);

    // UI-03 External provider: strictly NO company system selector
    fireEvent.click(screen.getByRole('button', { name: /الأنظمة المزودة/ }));
    fireEvent.click(screen.getByRole('button', { name: /مزودون خارجيون/ }));
    fireEvent.click(screen.getByRole('button', { name: 'إضافة مزود خارجي' }));
    expect(screen.queryByLabelText(/نظام الشركة/)).not.toBeInTheDocument();
    fireEvent.change(screen.getByLabelText(/اسم المزوّد الخارجي/), { target: { value: 'PartnerBankAPI' } });
    fireEvent.click(screen.getByRole('button', { name: 'حفظ وإنشاء' }));
    const extDlg = await screen.findByRole('dialog');
    fireEvent.click(within(extDlg).getByRole('button', { name: 'إغلاق نافذة الرسالة' }));
    expect(await screen.findByText('PartnerBankAPI')).toBeInTheDocument();

    // UI-04 File types: auto extensions + conditional Word template field ONLY for WORD_DOC
    fireEvent.click(screen.getByRole('button', { name: /الإعدادات/ }));
    fireEvent.click(screen.getByRole('button', { name: /أنواع الملفات/ }));
    await screen.findByText(/قاموس أنواع الملفات/);

    fireEvent.click(screen.getByRole('button', { name: 'تعريف نوع ملف من القاموس' }));
    const catalogSelect = screen.getByLabelText(/نوع الملف القياسي/);

    // Select OPENAPI_SPEC -> auto extensions shown, NO Word template input
    fireEvent.change(catalogSelect, { target: { value: 'OPENAPI_SPEC' } });
    expect(screen.getByText('.yaml')).toBeInTheDocument();
    expect(screen.getByText('.yml')).toBeInTheDocument();
    expect(screen.queryByLabelText(/معرّف كائن قالب Word/)).not.toBeInTheDocument();

    // Switch to WORD_DOC -> Word template field appears!
    fireEvent.change(catalogSelect, { target: { value: 'WORD_DOC' } });
    expect(screen.getByText('.docx')).toBeInTheDocument();
    expect(screen.getByLabelText(/معرّف كائن قالب Word/)).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'حفظ وتعريف النوع' }));
    const ftDlg = await screen.findByRole('dialog');
    expect(within(ftDlg).getByText('تم تعريف نوع الملف بنجاح')).toBeInTheDocument();
  });
});
