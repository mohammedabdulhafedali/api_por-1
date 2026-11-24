import { fireEvent, render, screen, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { App } from '../App';
import { AuthenticatedUser } from '../api/authApi';

const S05_ADMIN: AuthenticatedUser = {
  id: '10000000-0000-0000-0000-000000000005',
  fullName: 'مدير الإعدادات S05',
  email: 'admin-s05@api-governance.local',
  isActive: true,
  authProvider: 'LocalIdentity',
  roles: [{ id: '20000000-0000-0000-0000-000000000005', name: 'ConfigAdmin' }],
  permissions: [
    'requirements.view',
    'requirements.manage',
    'routes.view',
    'routes.manage',
    'changes.config.view',
    'changes.config.manage',
  ],
};

describe('Sprint S05 Governance Configuration Screens (UI-23, UI-05/05A, UI-06)', () => {
  beforeEach(() => {
    window.localStorage.clear();
    document.documentElement.lang = 'ar';
    document.documentElement.dir = 'rtl';
    document.documentElement.setAttribute('data-theme', 'light');
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('UI-23 (M05): creates requirement template in StandaloneFormPage and inspects historical snapshots', async () => {
    let templates: Array<Record<string, unknown>> = [];
    const fileTypeOpts = [
      {
        id: 'ft-openapi',
        catalogCode: 'OPENAPI_SPEC',
        displayName: 'مواصفة OpenAPI',
        extensions: ['.yaml', '.yml'],
        maxSizeBytes: 10485760,
        isActive: true,
      },
    ];
    const snapshots = [
      {
        auditEventId: 'aud-1',
        versionNumber: 1,
        operation: 'REQUIREMENT_TEMPLATE_CREATED',
        capturedAtUtc: '2026-11-24T12:00:00Z',
        actorUserId: S05_ADMIN.id,
        actorName: S05_ADMIN.fullName,
        templateName: 'قالب OpenAPI الأساسي',
        isActive: true,
        rules: [
          {
            id: 'r-1',
            fileTypeId: 'ft-openapi',
            fileTypeName: 'مواصفة OpenAPI',
            catalogCode: 'OPENAPI_SPEC',
            extensions: ['.yaml', '.yml'],
            fileTypeIsActive: true,
            requirementMode: 'REQUIRED',
            minCount: 1,
            maxCount: 2,
            conditionSpecJson: null,
          },
        ],
      },
    ];

    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url.endsWith('/api/v1/auth/csrf')) {
        return new Response(JSON.stringify({ token: 'csrf-s05', headerName: 'X-CSRF-TOKEN' }), { status: 200 });
      }
      if (url === '/api/v1/requirement-templates/file-types') {
        return new Response(JSON.stringify(fileTypeOpts), { status: 200 });
      }
      if (url.startsWith('/api/v1/requirement-templates?')) {
        return new Response(
          JSON.stringify({ items: templates, page: 1, pageSize: 10, totalCount: templates.length, totalPages: 1 }),
          { status: 200 }
        );
      }
      if (url === '/api/v1/requirement-templates' && init?.method === 'POST') {
        const body = JSON.parse(String(init.body)) as { name: string };
        const created = {
          id: 'tpl-1',
          name: body.name,
          isActive: true,
          rules: snapshots[0].rules,
          documentsCount: 0,
          snapshotCount: 1,
          latestSnapshotAt: '2026-11-24T12:00:00Z',
        };
        templates = [created];
        return new Response(JSON.stringify(created), { status: 201 });
      }
      if (url === '/api/v1/requirement-templates/tpl-1/snapshots') {
        return new Response(JSON.stringify(snapshots), { status: 200 });
      }
      return new Response('{}', { status: 404 });
    });

    vi.stubGlobal('fetch', fetchMock);
    render(<App initialAuthenticatedUser={S05_ADMIN} />);

    fireEvent.click(screen.getByRole('button', { name: /الإعدادات/ }));
    fireEvent.click(screen.getByRole('button', { name: /قوالب متطلبات الملفات/ }));
    await screen.findByText(/قوالب متطلبات الملفات واللقطات التاريخية/);

    fireEvent.click(screen.getByRole('button', { name: 'إضافة قالب متطلبات' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();

    fireEvent.change(screen.getByLabelText(/اسم القالب/), { target: { value: 'قالب OpenAPI الأساسي' } });
    fireEvent.click(screen.getByRole('button', { name: 'حفظ وإنشاء' }));

    const dlg = await screen.findByRole('dialog');
    fireEvent.click(within(dlg).getByRole('button', { name: 'إغلاق نافذة الرسالة' }));
    expect(await screen.findByText('قالب OpenAPI الأساسي')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'اللقطات' }));
    expect(await screen.findByText('v1')).toBeInTheDocument();
    expect(screen.getByText('REQUIREMENT_TEMPLATE_CREATED')).toBeInTheDocument();
  });

  it('UI-05/UI-05A & UI-06 (M06/M07): creates route without stageType/assignedUser, reorders stages, and links Change Type + Policy', async () => {
    const roles = [
      { id: 'role-arch', name: 'مهندس معماري', isActive: true },
      { id: 'role-sec', name: 'أمن المعلومات', isActive: true },
    ];
    let routes: Array<Record<string, unknown>> = [
      {
        id: 'route-1',
        name: 'مسار الاعتماد الثنائي',
        isActive: true,
        stages: [
          {
            id: 'st-1',
            position: 1,
            stageName: 'المراجعة المعمارية',
            responsibleRoleId: 'role-arch',
            responsibleRoleName: 'مهندس معماري',
            responsibleRoleIsActive: true,
          },
          {
            id: 'st-2',
            position: 2,
            stageName: 'الاعتماد الأمني',
            responsibleRoleId: 'role-sec',
            responsibleRoleName: 'أمن المعلومات',
            responsibleRoleIsActive: true,
          },
        ],
        changeTypesCount: 0,
        documentsCount: 0,
        activeWorkflowRunsCount: 0,
      },
    ];
    let changeTypes: Array<Record<string, unknown>> = [];
    let changePolicies: Array<Record<string, unknown>> = [];
    const captured: {
      stagesPutBody: { stages: Array<{ position: number; stageName: string }> } | null;
    } = { stagesPutBody: null };

    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url.endsWith('/api/v1/auth/csrf')) {
        return new Response(JSON.stringify({ token: 'csrf-m06-m07', headerName: 'X-CSRF-TOKEN' }), { status: 200 });
      }
      if (url === '/api/v1/approval-routes/roles') {
        return new Response(JSON.stringify(roles), { status: 200 });
      }
      if (url.startsWith('/api/v1/approval-routes?')) {
        return new Response(
          JSON.stringify({ items: routes, page: 1, pageSize: 10, totalCount: routes.length, totalPages: 1 }),
          { status: 200 }
        );
      }
      if (url === '/api/v1/approval-routes/route-1/stages' && init?.method === 'PUT') {
        const parsed = JSON.parse(String(init.body)) as { stages: Array<{ position: number; stageName: string }> };
        captured.stagesPutBody = parsed;
        routes = [
          {
            ...routes[0],
            stages: parsed.stages.map((s, idx) => ({
              id: `st-u-${idx}`,
              position: s.position,
              stageName: s.stageName,
              responsibleRoleId: s.stageName === 'الاعتماد الأمني' ? 'role-sec' : 'role-arch',
              responsibleRoleName: s.stageName === 'الاعتماد الأمني' ? 'أمن المعلومات' : 'مهندس معماري',
              responsibleRoleIsActive: true,
            })),
          },
        ];
        return new Response(JSON.stringify(routes[0]), { status: 200 });
      }
      if (url === '/api/v1/change-types/routes') {
        return new Response(
          JSON.stringify([{ id: 'route-1', name: 'مسار الاعتماد الثنائي', stageCount: 2, isActive: true }]),
          { status: 200 }
        );
      }
      if (url.startsWith('/api/v1/change-types?')) {
        return new Response(
          JSON.stringify({ items: changeTypes, page: 1, pageSize: 10, totalCount: changeTypes.length, totalPages: 1 }),
          { status: 200 }
        );
      }
      if (url === '/api/v1/change-types' && init?.method === 'POST') {
        const body = JSON.parse(String(init.body)) as { name: string; routeId: string };
        const created = {
          id: 'ct-1',
          name: body.name,
          routeId: body.routeId,
          routeName: 'مسار الاعتماد الثنائي',
          routeIsActive: true,
          routeStageCount: 2,
          isActive: true,
          policiesCount: 0,
          changeRequestsCount: 0,
        };
        changeTypes = [created];
        return new Response(JSON.stringify(created), { status: 201 });
      }
      if (url.startsWith('/api/v1/change-policies?')) {
        return new Response(
          JSON.stringify({ items: changePolicies, page: 1, pageSize: 10, totalCount: changePolicies.length, totalPages: 1 }),
          { status: 200 }
        );
      }
      if (url === '/api/v1/change-policies' && init?.method === 'POST') {
        const body = JSON.parse(String(init.body)) as { name: string };
        const created = {
          id: 'cp-1',
          name: body.name,
          isActive: true,
          changeTypes: [
            {
              changeTypeId: 'ct-1',
              changeTypeName: 'تحديث حزمة OpenAPI',
              routeId: 'route-1',
              routeName: 'مسار الاعتماد الثنائي',
              changeTypeIsActive: true,
            },
          ],
          documentsCount: 0,
        };
        changePolicies = [created];
        return new Response(JSON.stringify(created), { status: 201 });
      }
      return new Response('{}', { status: 404 });
    });

    vi.stubGlobal('fetch', fetchMock);
    render(<App initialAuthenticatedUser={S05_ADMIN} />);

    // Open UI-05 Approval Routes -> UI-05A Manage Stages
    fireEvent.click(screen.getByRole('button', { name: /الإعدادات/ }));
    fireEvent.click(screen.getByRole('button', { name: /مسارات الاعتماد/ }));
    expect(await screen.findByText('مسار الاعتماد الثنائي')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'إدارة المراحل (UI-05A)' }));
    await screen.findByText(/ترتيب وتحرير مراحل المسار/);

    // Verify strictly NO stageType or assignedUser controls exist
    expect(screen.queryByLabelText(/نوع المرحلة/)).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/مستخدم مكلّف/)).not.toBeInTheDocument();

    // Move first stage down -> reverses order and saves cleanly
    const downButtons = screen.getAllByRole('button', { name: 'أسفل ↓' });
    fireEvent.click(downButtons[0]);
    fireEvent.click(screen.getByRole('button', { name: 'حفظ ترتيب المراحل' }));

    const dlgStages = await screen.findByRole('dialog');
    fireEvent.click(within(dlgStages).getByRole('button', { name: 'إغلاق نافذة الرسالة' }));
    expect(captured.stagesPutBody?.stages[0].stageName).toBe('الاعتماد الأمني');
    expect(captured.stagesPutBody?.stages[0].position).toBe(1);

    // Switch to UI-06 Change Types & Policies
    fireEvent.click(screen.getByRole('button', { name: /أنواع طلبات التغيير/ }));
    await screen.findByText(/أنواع طلبات التغيير وربطها بمسارات الاعتماد/);

    fireEvent.click(screen.getByRole('button', { name: 'إضافة نوع تغيير' }));
    fireEvent.change(screen.getByLabelText(/اسم نوع التغيير/), { target: { value: 'تحديث حزمة OpenAPI' } });
    fireEvent.click(screen.getByRole('button', { name: 'حفظ وإنشاء' }));
    const dlgCt = await screen.findByRole('dialog');
    fireEvent.click(within(dlgCt).getByRole('button', { name: 'إغلاق نافذة الرسالة' }));
    expect(await screen.findByText('تحديث حزمة OpenAPI')).toBeInTheDocument();

    // Switch to Change Policies tab
    fireEvent.click(screen.getByRole('button', { name: /سياسات التغيير/ }));
    await screen.findByText(/سياسات التغيير المسموحة لوثائق API/);
    fireEvent.click(screen.getByRole('button', { name: 'إضافة سياسة تغيير' }));
    fireEvent.change(screen.getByLabelText(/اسم سياسة التغيير/), { target: { value: 'سياسة التحديثات المعتمدة' } });
    fireEvent.click(screen.getByRole('checkbox'));
    fireEvent.click(screen.getByRole('button', { name: 'حفظ وإنشاء' }));
    const dlgCp = await screen.findByRole('dialog');
    fireEvent.click(within(dlgCp).getByRole('button', { name: 'إغلاق نافذة الرسالة' }));
    expect(await screen.findByText('سياسة التحديثات المعتمدة')).toBeInTheDocument();
  });
});

