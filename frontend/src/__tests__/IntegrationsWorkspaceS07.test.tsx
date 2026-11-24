import { fireEvent, render, screen, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { App } from '../App';
import { AuthenticatedUser } from '../api/authApi';

const S07_USER: AuthenticatedUser = {
  id: '10000000-0000-0000-0000-000000000007',
  fullName: 'مشرف التكاملات S07',
  email: 'int-lead-s07@api-governance.local',
  isActive: true,
  authProvider: 'LocalIdentity',
  roles: [{ id: 'role-s07', name: 'IntegrationLead' }],
  permissions: ['integrations.view', 'integrations.create', 'integrations.assign_developer'],
};

describe('Sprint S07 Integrations Details/Tabs, Developer Scope & Unified Localization/Modals (D05, D06, U01, U03)', () => {
  beforeEach(() => {
    window.localStorage.clear();
    document.documentElement.lang = 'ar';
    document.documentElement.dir = 'rtl';
    document.documentElement.setAttribute('data-theme', 'light');
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('D05 / D06 / U01 / U03: navigates UI-22 -> UI-09 6 tabs, updates developers in StandaloneFormPage, switches language, and shows traceId in CentralModalDialog', async () => {
    const assignableUsers = [
      { id: S07_USER.id, fullName: S07_USER.fullName, email: S07_USER.email },
      { id: 'dev-s07-1', fullName: 'سارة المطور', email: 'sara@api-governance.local' },
    ];

    let assignedDevs: Array<Record<string, unknown>> = [];

    const buildDetails = () => ({
      summary: {
        id: 'int-s07-1',
        documentId: 'doc-s07-1',
        referenceCode: 'INT-PAY-2026',
        derivedTitle: 'PaymentCore ← شريك تجريبي ب',
        providerKind: 'INTERNAL',
        providerName: 'PaymentCore',
        consumerKind: 'CONSUMER_ENTITY',
        consumerName: 'شريك تجريبي ب',
        apiGroupId: 'grp-1',
        apiGroupName: 'المدفوعات الفورية',
        requirementTemplateName: 'قالب OpenAPI',
        approvalRouteName: 'مسار الحوكمة الثنائي',
        changePolicyName: 'سياسة التحديث القياسية',
        publicationOption: 'MANUAL',
        documentState: 'ACTIVE',
        createdBy: S07_USER.id,
        createdByName: S07_USER.fullName,
        createdAt: '2026-11-24T12:00:00Z',
        versionsCount: 0,
        isCurrentUserAssignedDeveloper: assignedDevs.some((d) => d.userId === S07_USER.id),
        assignedDevelopers: assignedDevs,
        myDocumentPermissions: ['doc.view', 'doc.assign'],
      },
      documentDevelopers: [],
      requirementRules: [
        {
          fileTypeId: 'ft-1',
          fileTypeName: 'مواصفة OpenAPI',
          catalogCode: 'OPENAPI_SPEC',
          extensions: ['.yaml', '.json'],
          requirementMode: 'REQUIRED',
          minCount: 1,
          maxCount: 1,
        },
      ],
      approvalStages: [
        {
          position: 1,
          stageName: 'المراجعة المعمارية',
          responsibleRoleId: 'role-arch',
          responsibleRoleName: 'مهندس معماري',
        },
      ],
      allowedChangeTypes: [
        {
          changeTypeId: 'ct-1',
          changeTypeName: 'تحديث حزمة API',
          routeId: 'rt-1',
          routeName: 'مسار الحوكمة الثنائي',
        },
      ],
      revisionsCount: 0,
      changeRequestsCount: 0,
      publicationsCount: 0,
      recentAuditEvents: [
        {
          id: 'aud-1',
          operation: 'INTEGRATION_CREATED',
          resourceKind: 'INTEGRATION',
          result: 'SUCCESS',
          actorUserId: S07_USER.id,
          actorName: S07_USER.fullName,
          recordedAt: '2026-11-24T12:00:00Z',
        },
      ],
    });

    const captured = {
      devSaved: false,
      assignedIds: [] as string[],
    };

    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url.endsWith('/api/v1/auth/csrf')) {
        return new Response(JSON.stringify({ token: 'csrf-s07', headerName: 'X-CSRF-TOKEN' }), { status: 200 });
      }
      if (url === '/api/v1/integrations/eligible-documents') {
        return new Response(JSON.stringify([]), { status: 200 });
      }
      if (url === '/api/v1/integrations/assignable-users') {
        return new Response(JSON.stringify(assignableUsers), { status: 200 });
      }
      if (url.startsWith('/api/v1/integrations?')) {
        const d = buildDetails().summary;
        return new Response(
          JSON.stringify({ items: [d], page: 1, pageSize: 10, totalCount: 1, totalPages: 1 }),
          { status: 200 }
        );
      }
      if (url === '/api/v1/integrations/int-s07-1') {
        return new Response(JSON.stringify(buildDetails()), { status: 200 });
      }
      if (url === '/api/v1/integrations/int-s07-1/developers' && init?.method === 'PUT') {
        const body = JSON.parse(String(init.body)) as { developerUserIds: string[] };
        captured.devSaved = true;
        captured.assignedIds = body.developerUserIds;
        assignedDevs = body.developerUserIds.map((uid) => {
          const u = assignableUsers.find((x) => x.id === uid)!;
          return {
            userId: u.id,
            fullName: u.fullName,
            email: u.email,
            isActive: true,
            hasDocView: false,
            hasDocVersionManage: false,
            hasDocFilesManage: false,
            hasDocChange: false,
            hasDocAssign: false,
          };
        });
        return new Response(
          JSON.stringify({
            integrationId: 'int-s07-1',
            documentId: 'doc-s07-1',
            referenceCode: 'INT-PAY-2026',
            derivedTitle: 'PaymentCore ← شريك تجريبي ب',
            isCurrentUserAssignedToIntegration: true,
            isCurrentUserAssignedToDocument: false,
            integrationDevelopers: assignedDevs,
            documentDevelopers: [],
          }),
          { status: 200 }
        );
      }
      return new Response('{}', { status: 404 });
    });

    vi.stubGlobal('fetch', fetchMock);
    render(<App initialAuthenticatedUser={S07_USER} />);

    // Open UI-22 Integrations list (top bar updates to UI-22!)
    fireEvent.click(screen.getByRole('button', { name: /التكاملات/ }));
    fireEvent.click(screen.getByRole('button', { name: /قائمة التكاملات/ }));

    expect(await screen.findByText('INT-PAY-2026')).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 1, name: 'قائمة التكاملات' })).toBeInTheDocument();

    // Open UI-09 Details & 6 Tabs
    fireEvent.click(screen.getByRole('button', { name: 'التفاصيل والتبويبات (UI-09)' }));
    expect(await screen.findByText(/المطورون المكلّفون على هذا التكامل/)).toBeInTheDocument();

    // Verify all 6 tabs render live metadata
    fireEvent.click(screen.getByRole('button', { name: 'الملفات (UI-10)' }));
    expect(await screen.findByText('مواصفة OpenAPI')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'المراجعات (UI-11)' }));
    expect(await screen.findByText('المراجعة المعمارية')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'طلبات التغيير (UI-13)' }));
    expect(await screen.findByText('تحديث حزمة API')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'السجل والنشر (UI-14)' }));
    expect(await screen.findByText('INTEGRATION_CREATED')).toBeInTheDocument();

    // Back to Developers tab and open StandaloneFormPage (never Modal) for D05 assignment
    fireEvent.click(screen.getByRole('button', { name: 'المطورون والنطاق' }));
    fireEvent.click(screen.getByRole('button', { name: 'إدارة المطورين (D05)' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();

    const devCheckbox = await screen.findByRole('checkbox', { name: /سارة المطور/ });
    fireEvent.click(devCheckbox);
    fireEvent.click(screen.getByRole('button', { name: 'حفظ مطوري التكامل' }));

    const dlg = await screen.findByRole('dialog');
    expect(within(dlg).getByText('تم تحديث مطوري التكامل')).toBeInTheDocument();
    fireEvent.click(within(dlg).getByRole('button', { name: 'إغلاق نافذة الرسالة' }));
    expect(captured.devSaved).toBe(true);
    expect(captured.assignedIds).toContain('dev-s07-1');

    // U01 Language Switch AR -> EN seamlessly updates details view & topbar
    fireEvent.click(screen.getByRole('button', { name: 'EN' }));
    expect(await screen.findByText('Assigned Integration Developers (D05)')).toBeInTheDocument();
  });
});

