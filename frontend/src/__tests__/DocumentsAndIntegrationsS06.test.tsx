import { fireEvent, render, screen, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { App } from '../App';
import { AuthenticatedUser } from '../api/authApi';

const S06_USER: AuthenticatedUser = {
  id: '10000000-0000-0000-0000-000000000006',
  fullName: 'منشئ الوثائق S06',
  email: 'docadmin-s06@api-governance.local',
  isActive: true,
  authProvider: 'LocalIdentity',
  roles: [{ id: 'role-admin-s06', name: 'DocumentOwner' }],
  permissions: [
    'api_documents.view',
    'api_documents.create',
    'api_documents.edit',
    'api_documents.delete',
    'api_documents.assign_developer',
    'documents.permissions.view',
    'documents.permissions.manage',
    'integrations.view',
    'integrations.create',
  ],
};

describe('Sprint S06 API Documents, Document ACL & 1:1 Integrations (UI-21, UI-07, UI-21A/U02, UI-22/08)', () => {
  beforeEach(() => {
    window.localStorage.clear();
    document.documentElement.lang = 'ar';
    document.documentElement.dir = 'rtl';
    document.documentElement.setAttribute('data-theme', 'light');
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('UI-21 / UI-07 / UI-21A / UI-22 / UI-08: derives title, omits upload, opens U02 full Document ACL matrix, and creates 1:1 integration', async () => {
    const lookups = {
      internalProviders: [{ id: 'sys-bill', name: 'BillingCore', subtitle: 'Internal Provider' }],
      externalProviders: [],
      consumerCompanySystems: [{ id: 'sys-bill', name: 'BillingCore', subtitle: null }],
      consumerEntities: [{ id: 'ent-bank', name: 'مصرف تجريبي أ', subtitle: 'البنوك' }],
      apiGroups: [{ id: 'grp-pay', name: 'المدفوعات', subtitle: null }],
      requirementTemplates: [{ id: 'tpl-1', name: 'قالب OpenAPI', subtitle: null }],
      approvalRoutes: [{ id: 'rt-1', name: 'مسار الحوكمة', subtitle: null }],
      changePolicies: [{ id: 'cp-1', name: 'سياسة التغيير القياسية', subtitle: null }],
      activeUsers: [
        { id: S06_USER.id, name: S06_USER.fullName, subtitle: S06_USER.email },
        { id: 'dev-1', name: 'مطور التكامل', subtitle: 'dev@api-governance.local' },
      ],
    };

    let docs: Array<Record<string, unknown>> = [];
    let integrations: Array<Record<string, unknown>> = [];

    const docPermsCatalog = [
      { id: 'p-1', code: 'doc.view', actionKey: 'view', labelAr: 'عرض الوثيقة والتكامل', labelEn: 'View' },
      { id: 'p-2', code: 'doc.edit', actionKey: 'edit', labelAr: 'تعديل إعدادات الوثيقة', labelEn: 'Edit' },
      { id: 'p-3', code: 'doc.delete', actionKey: 'delete', labelAr: 'حذف الوثيقة غير المرتبطة', labelEn: 'Delete' },
      { id: 'p-4', code: 'doc.access.manage', actionKey: 'access.manage', labelAr: 'إدارة صلاحيات الوثيقة', labelEn: 'Access' },
      { id: 'p-5', code: 'doc.assign', actionKey: 'assign', labelAr: 'تعيين المطورين', labelEn: 'Assign' },
      { id: 'p-6', code: 'doc.manage_integration', actionKey: 'manage_integration', labelAr: 'إنشاء وإدارة التكامل', labelEn: 'Integration' },
    ];

    const accessMatrixResponse = {
      documentId: 'doc-1',
      derivedTitle: 'BillingCore ← مصرف تجريبي أ',
      apiGroupName: 'المدفوعات',
      createdBy: S06_USER.id,
      createdByName: S06_USER.fullName,
      permissions: docPermsCatalog,
      roles: [
        {
          roleId: 'role-admin-s06',
          roleName: 'DocumentOwner',
          isActive: true,
          decisions: { 'doc.view': 'GRANT' },
        },
      ],
      users: [
        {
          userId: S06_USER.id,
          fullName: S06_USER.fullName,
          email: S06_USER.email,
          isActive: true,
          roleNames: ['DocumentOwner'],
          isAssignedDeveloper: false,
          cells: Object.fromEntries(
            docPermsCatalog.map((p) => [
              p.code,
              { allowed: true, resolutionSource: 'USER_GRANT', explicitDecision: 'GRANT' },
            ])
          ),
        },
        {
          userId: 'dev-1',
          fullName: 'مطور التكامل',
          email: 'dev@api-governance.local',
          isActive: true,
          roleNames: ['DocumentOwner'],
          isAssignedDeveloper: true,
          cells: Object.fromEntries(
            docPermsCatalog.map((p) => [
              p.code,
              p.code === 'doc.view'
                ? { allowed: true, resolutionSource: 'ROLE_GRANT', explicitDecision: null }
                : { allowed: false, resolutionSource: 'DENIED_DEFAULT', explicitDecision: null },
            ])
          ),
        },
      ],
    };

    const captured = {
      aclSaved: false,
      integrationCreated: false,
    };

    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url.endsWith('/api/v1/auth/csrf')) {
        return new Response(JSON.stringify({ token: 'csrf-s06', headerName: 'X-CSRF-TOKEN' }), { status: 200 });
      }
      if (url === '/api/v1/api-documents/lookups' || url === '/api/v1/api-documents/form-lookups') {
        return new Response(JSON.stringify(lookups), { status: 200 });
      }
      if (url.startsWith('/api/v1/api-documents?')) {
        return new Response(
          JSON.stringify({ items: docs, page: 1, pageSize: 10, totalCount: docs.length, totalPages: 1 }),
          { status: 200 }
        );
      }
      if (url === '/api/v1/api-documents' && init?.method === 'POST') {
        const created = {
          id: 'doc-1',
          derivedTitle: 'BillingCore ← مصرف تجريبي أ',
          providerKind: 'INTERNAL',
          providerInternalSystemId: 'sys-bill',
          providerExternalSystemId: null,
          providerName: 'BillingCore',
          consumerKind: 'CONSUMER_ENTITY',
          consumerCompanySystemId: null,
          consumerEntityId: 'ent-bank',
          consumerName: 'مصرف تجريبي أ',
          apiGroupId: 'grp-pay',
          apiGroupName: 'المدفوعات',
          requirementTemplateId: 'tpl-1',
          requirementTemplateName: 'قالب OpenAPI',
          approvalRouteId: 'rt-1',
          approvalRouteName: 'مسار الحوكمة',
          changePolicyId: 'cp-1',
          changePolicyName: 'سياسة التغيير القياسية',
          publicationOption: 'MANUAL',
          state: 'ACTIVE',
          createdBy: S06_USER.id,
          createdByName: S06_USER.fullName,
          createdAt: '2026-11-24T13:00:00Z',
          hasIntegration: false,
          integrationId: null,
          integrationReferenceCode: null,
          assignedDevelopers: [{ userId: 'dev-1', fullName: 'مطور التكامل', email: 'dev@api-governance.local', isActive: true }],
          myDocumentPermissions: docPermsCatalog.map((p) => p.code),
        };
        docs = [created];
        return new Response(JSON.stringify(created), { status: 201 });
      }
      if (url === '/api/v1/api-documents/doc-1/access' && (!init?.method || init.method === 'GET')) {
        return new Response(JSON.stringify(accessMatrixResponse), { status: 200 });
      }
      if (url === '/api/v1/api-documents/doc-1/access' && init?.method === 'PUT') {
        captured.aclSaved = true;
        return new Response(JSON.stringify(accessMatrixResponse), { status: 200 });
      }
      if (url === '/api/v1/integrations/eligible-documents') {
        return new Response(
          JSON.stringify(
            docs.length > 0 && integrations.length === 0
              ? [
                  {
                    documentId: 'doc-1',
                    derivedTitle: 'BillingCore ← مصرف تجريبي أ',
                    providerKind: 'INTERNAL',
                    providerName: 'BillingCore',
                    consumerKind: 'CONSUMER_ENTITY',
                    consumerName: 'مصرف تجريبي أ',
                    apiGroupId: 'grp-pay',
                    apiGroupName: 'المدفوعات',
                    state: 'ACTIVE',
                  },
                ]
              : []
          ),
          { status: 200 }
        );
      }
      if (url === '/api/v1/integrations/assignable-users') {
        return new Response(JSON.stringify([]), { status: 200 });
      }
      if (url.startsWith('/api/v1/integrations?')) {
        return new Response(
          JSON.stringify({ items: integrations, page: 1, pageSize: 10, totalCount: integrations.length, totalPages: 1 }),
          { status: 200 }
        );
      }
      if (url === '/api/v1/integrations' && init?.method === 'POST') {
        captured.integrationCreated = true;
        const createdInt = {
          id: 'int-1',
          documentId: 'doc-1',
          referenceCode: 'INT-20261124-ABC123',
          derivedTitle: 'BillingCore ← مصرف تجريبي أ',
          providerKind: 'INTERNAL',
          providerName: 'BillingCore',
          consumerKind: 'CONSUMER_ENTITY',
          consumerName: 'مصرف تجريبي أ',
          apiGroupId: 'grp-pay',
          apiGroupName: 'المدفوعات',
          requirementTemplateName: 'قالب OpenAPI',
          approvalRouteName: 'مسار الحوكمة',
          changePolicyName: 'سياسة التغيير القياسية',
          documentState: 'ACTIVE',
          createdBy: S06_USER.id,
          createdByName: S06_USER.fullName,
          createdAt: '2026-11-24T13:10:00Z',
          versionsCount: 0,
          assignedDevelopers: [],
          myDocumentPermissions: docPermsCatalog.map((p) => p.code),
        };
        integrations = [createdInt];
        return new Response(JSON.stringify(createdInt), { status: 201 });
      }
      return new Response('{}', { status: 404 });
    });

    vi.stubGlobal('fetch', fetchMock);
    render(<App initialAuthenticatedUser={S06_USER} />);

    // Open UI-07 Create API Document -> verifies derived title preview & NO file upload input
    fireEvent.click(screen.getByRole('button', { name: /وثائق API/ }));
    fireEvent.click(screen.getByRole('button', { name: /إضافة وثيقة API/ }));

    expect(await screen.findByText(/BillingCore ← مصرف تجريبي أ/)).toBeInTheDocument();
    expect(document.querySelector('input[type="file"]')).toBeNull();

    // Assign developer checkbox and submit
    fireEvent.click(screen.getByRole('checkbox', { name: 'مطور التكامل' }));
    fireEvent.click(screen.getByRole('button', { name: 'حفظ وإنشاء الوثيقة' }));

    const docDlg = await screen.findByRole('dialog');
    fireEvent.click(within(docDlg).getByRole('button', { name: 'إغلاق نافذة الرسالة' }));
    expect(await screen.findByText('BillingCore ← مصرف تجريبي أ')).toBeInTheDocument();

    // Open UI-21A / U02 Document Access Matrix -> verifies assigned developer badge + all matrix cells
    fireEvent.click(screen.getByRole('button', { name: 'الصلاحيات' }));
    expect(await screen.findByText(/مصفوفة المستخدمين الكاملة والنتيجة الفعلية/)).toBeInTheDocument();
    expect(screen.getByText('مطوّر مكلّف')).toBeInTheDocument();
    expect(screen.getAllByText('doc.manage_integration').length).toBeGreaterThanOrEqual(2);

    fireEvent.click(screen.getByRole('button', { name: 'حفظ صلاحيات الوثيقة' }));
    const confirmAclDlg = await screen.findByRole('dialog');
    fireEvent.click(within(confirmAclDlg).getByRole('button', { name: 'إغلاق نافذة الرسالة' }));
    await screen.findByText('تم تحديث صلاحيات الوثيقة');
    const savedAclDlg = await screen.findByRole('dialog');
    fireEvent.click(within(savedAclDlg).getByRole('button', { name: 'إغلاق نافذة الرسالة' }));
    expect(captured.aclSaved).toBe(true);

    // Open Integrations UI-08 -> create 1:1 Integration from eligible doc
    fireEvent.click(screen.getByRole('button', { name: /التكاملات/ }));
    fireEvent.click(screen.getByRole('button', { name: /إضافة تكامل/ }));
    expect(await screen.findByText(/إنشاء تكامل مرتبط بوثيقة API/)).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'حفظ وإنشاء التكامل' }));
    const intDlg = await screen.findByRole('dialog');
    fireEvent.click(within(intDlg).getByRole('button', { name: 'إغلاق نافذة الرسالة' }));
    expect(captured.integrationCreated).toBe(true);
    expect(await screen.findByText('INT-20261124-ABC123')).toBeInTheDocument();
  });
});

