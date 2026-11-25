import { fireEvent, render, screen, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { App } from '../App';
import { AuthenticatedUser } from '../api/authApi';

const FULL_PERMISSIONS_ADMIN: AuthenticatedUser = {
  id: '10000000-0000-0000-0000-000000000001',
  fullName: 'مدير النظام الأول',
  email: 'admin@api-governance.local',
  isActive: true,
  authProvider: 'LocalIdentity',
  roles: [{ id: '20000000-0000-0000-0000-000000000001', name: 'SystemAdmin' }],
  permissions: [
    'users.view',
    'users.edit',
    'users.assign_roles',
    'roles.view',
    'roles.edit',
    'permissions.view',
    'permissions.manage',
  ],
};

const SAMPLE_COLUMNS = [
  { id: 'view', labelAr: 'عرض', labelEn: 'View' },
  { id: 'create', labelAr: 'إضافة', labelEn: 'Create' },
];

const SAMPLE_SCREENS = [
  {
    id: 'UI-17',
    groupId: 'navigation',
    groupLabelAr: 'التنقل العام والمراقبة',
    groupLabelEn: 'Navigation',
    titleAr: 'لوحة التحكم',
    titleEn: 'Dashboard',
    technicalPath: '/api/v1/dashboard',
    applicableColumnIds: ['view'],
  },
  {
    id: 'UI-15',
    groupId: 'administration',
    groupLabelAr: 'الإدارة والهوية',
    groupLabelEn: 'Administration',
    titleAr: 'المستخدمون',
    titleEn: 'Users',
    technicalPath: '/api/v1/users',
    applicableColumnIds: ['view', 'create'],
  },
  {
    id: 'UI-25-ROLES',
    groupId: 'administration',
    groupLabelAr: 'الإدارة والهوية',
    groupLabelEn: 'Administration',
    titleAr: 'الأدوار التنظيمية',
    titleEn: 'Roles',
    technicalPath: '/api/v1/roles',
    applicableColumnIds: ['view'],
  },
];

const SAMPLE_PERMISSIONS = [
  {
    id: '30000000-0000-0000-0000-000000000001',
    code: 'dashboard.view',
    resource: 'dashboard',
    action: 'view',
    screenId: 'UI-17',
    groupId: 'navigation',
    columnId: 'view',
  },
  {
    id: '30000000-0000-0000-0000-000000000002',
    code: 'users.view',
    resource: 'users',
    action: 'view',
    screenId: 'UI-15',
    groupId: 'administration',
    columnId: 'view',
  },
  {
    id: '30000000-0000-0000-0000-000000000003',
    code: 'users.create',
    resource: 'users',
    action: 'create',
    screenId: 'UI-15',
    groupId: 'administration',
    columnId: 'create',
  },
  {
    id: '30000000-0000-0000-0000-000000000004',
    code: 'roles.view',
    resource: 'roles',
    action: 'view',
    screenId: 'UI-25-ROLES',
    groupId: 'administration',
    columnId: 'view',
  },
];

describe('Task I04 Role Permission Matrix (UI-25) & User Overrides (UI-15A)', () => {
  beforeEach(() => {
    window.localStorage.clear();
    document.documentElement.lang = 'ar';
    document.documentElement.dir = 'rtl';
    document.documentElement.setAttribute('data-theme', 'light');
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('supports UI-25 full Screen x Action bulk selection (cell, row, column, group, all) and persists role permissions with CSRF', async () => {
    let savedRolePermIds: string[] = [];

    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString();

      if (url.startsWith('/api/v1/roles?')) {
        return new Response(
          JSON.stringify({
            items: [
              {
                id: '20000000-0000-0000-0000-000000000010',
                name: 'GovernanceReviewer',
                isActive: true,
                isPinned: false,
                assignedUsersCount: 1,
                grantedPermissionsCount: savedRolePermIds.length,
              },
            ],
            page: 1,
            pageSize: 10,
            totalCount: 1,
            totalPages: 1,
          }),
          { status: 200, headers: { 'Content-Type': 'application/json' } }
        );
      }

      if (url === '/api/v1/permissions') {
        return new Response(
          JSON.stringify({
            permissions: SAMPLE_PERMISSIONS,
            columns: SAMPLE_COLUMNS,
            screens: SAMPLE_SCREENS,
          }),
          { status: 200, headers: { 'Content-Type': 'application/json' } }
        );
      }

      if (url === '/api/v1/roles/20000000-0000-0000-0000-000000000010/permissions' && (!init?.method || init.method === 'GET')) {
        return new Response(
          JSON.stringify({
            roleId: '20000000-0000-0000-0000-000000000010',
            roleName: 'GovernanceReviewer',
            isActive: true,
            isPinned: false,
            permissionIds: ['30000000-0000-0000-0000-000000000001'],
            permissionCodes: ['dashboard.view'],
          }),
          { status: 200, headers: { 'Content-Type': 'application/json' } }
        );
      }

      if (url.endsWith('/api/v1/auth/csrf')) {
        return new Response(
          JSON.stringify({ token: 'csrf-i04', headerName: 'X-CSRF-TOKEN' }),
          { status: 200, headers: { 'Content-Type': 'application/json' } }
        );
      }

      if (url === '/api/v1/roles/20000000-0000-0000-0000-000000000010/permissions' && init?.method === 'PUT') {
        const headers = init.headers as Record<string, string>;
        expect(headers['X-CSRF-TOKEN']).toBe('csrf-i04');
        const body = JSON.parse(String(init.body)) as { permissionIds: string[] };
        savedRolePermIds = body.permissionIds;
        return new Response(
          JSON.stringify({
            roleId: '20000000-0000-0000-0000-000000000010',
            roleName: 'GovernanceReviewer',
            isActive: true,
            isPinned: false,
            permissionIds: savedRolePermIds,
            permissionCodes: [],
          }),
          { status: 200, headers: { 'Content-Type': 'application/json' } }
        );
      }

      return new Response('{}', { status: 404 });
    });

    vi.stubGlobal('fetch', fetchMock);
    render(<App initialAuthenticatedUser={FULL_PERMISSIONS_ADMIN} />);

    fireEvent.click(screen.getByRole('button', { name: /الإدارة/ }));
    fireEvent.click(screen.getByRole('button', { name: /الأدوار والصلاحيات/ }));

    expect(await screen.findByText('GovernanceReviewer')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'مصفوفة الصلاحيات' }));
    expect(await screen.findByText(/مصفوفة الصلاحيات الكاملة للدور/)).toBeInTheDocument();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();

    // Initially only dashboard.view is checked
    const dashCell = await screen.findByLabelText('لوحة التحكم - عرض');
    const usersViewCell = screen.getByLabelText('المستخدمون - عرض');
    const usersCreateCell = screen.getByLabelText('المستخدمون - إضافة');
    const rolesViewCell = screen.getByLabelText('الأدوار التنظيمية - عرض');

    expect(dashCell).toBeChecked();
    expect(usersViewCell).not.toBeChecked();

    // Select All -> checks all 4 cells
    fireEvent.click(screen.getByLabelText('تحديد الكل'));
    expect(usersViewCell).toBeChecked();
    expect(usersCreateCell).toBeChecked();
    expect(rolesViewCell).toBeChecked();

    // Clear All -> unchecks all 4 cells
    fireEvent.click(screen.getByLabelText('تحديد الكل'));
    expect(dashCell).not.toBeChecked();

    // Select Column ('عرض') -> checks view cells across groups, leaves 'إضافة' unchecked
    fireEvent.click(screen.getByLabelText('تحديد عمود: عرض'));
    expect(dashCell).toBeChecked();
    expect(usersViewCell).toBeChecked();
    expect(rolesViewCell).toBeChecked();
    expect(usersCreateCell).not.toBeChecked();

    // Group ('الإدارة والهوية') is currently partial (usersCreateCell unchecked), so first click selects entire group, second click clears it
    const adminGroupCheckbox = screen.getByLabelText('تحديد القسم: الإدارة والهوية');
    fireEvent.click(adminGroupCheckbox);
    expect(usersCreateCell).toBeChecked();
    expect(rolesViewCell).toBeChecked();

    fireEvent.click(adminGroupCheckbox);
    expect(dashCell).toBeChecked();
    expect(usersViewCell).not.toBeChecked();
    expect(usersCreateCell).not.toBeChecked();

    // Select Row ('المستخدمون') -> checks users.view + users.create
    fireEvent.click(screen.getByLabelText('تحديد صف: المستخدمون'));
    expect(usersViewCell).toBeChecked();
    expect(usersCreateCell).toBeChecked();

    // Save via CentralModalDialog confirmation
    fireEvent.click(screen.getByRole('button', { name: 'حفظ مصفوفة الدور' }));
    const confirmModal = await screen.findByRole('dialog');
    expect(within(confirmModal).getByText('تأكيد حفظ مصفوفة صلاحيات الدور')).toBeInTheDocument();
    fireEvent.click(within(confirmModal).getByRole('button', { name: 'إغلاق نافذة الرسالة' }));

    const successModal = await screen.findByRole('dialog');
    expect(within(successModal).getByText('تم حفظ مصفوفة الدور بنجاح')).toBeInTheDocument();
    expect(savedRolePermIds.sort()).toEqual([
      '30000000-0000-0000-0000-000000000001',
      '30000000-0000-0000-0000-000000000002',
      '30000000-0000-0000-0000-000000000003',
    ]);
  });

  it('manages UI-15A user individual GRANT/REVOKE overrides and resets back to inherited state with confirmation', async () => {
    let submittedOverrides: Array<{ permissionId: string; decision: string }> = [];
    let currentOverrides: Array<{ permissionId: string; decision: 'GRANT' | 'REVOKE' }> = [];

    const buildMatrixResponse = () => ({
      userId: '10000000-0000-0000-0000-000000000002',
      fullName: 'مطور تكامل',
      email: 'dev@api-governance.local',
      isActive: true,
      activeRoles: [{ id: '20000000-0000-0000-0000-000000000010', name: 'GovernanceReviewer' }],
      columns: SAMPLE_COLUMNS,
      screens: SAMPLE_SCREENS,
      effectiveAllowedCount: 1,
      permissions: SAMPLE_PERMISSIONS.map((p) => {
        const isInherited = p.code === 'users.view';
        const ov = currentOverrides.find((o) => o.permissionId === p.id)?.decision ?? null;
        const effectiveAllowed = ov === 'REVOKE' ? false : ov === 'GRANT' ? true : isInherited;
        return {
          permissionId: p.id,
          code: p.code,
          resource: p.resource,
          action: p.action,
          screenId: p.screenId,
          groupId: p.groupId,
          columnId: p.columnId,
          isInherited,
          inheritedFromRoles: isInherited
            ? [{ id: '20000000-0000-0000-0000-000000000010', name: 'GovernanceReviewer' }]
            : [],
          overrideDecision: ov,
          effectiveAllowed,
          resolutionSource:
            ov === 'REVOKE'
              ? 'USER_REVOKE'
              : ov === 'GRANT'
                ? 'USER_GRANT'
                : isInherited
                  ? 'ROLE_INHERITED'
                  : 'DENIED_DEFAULT',
        };
      }),
    });

    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString();

      if (url.startsWith('/api/v1/users?')) {
        return new Response(
          JSON.stringify({
            items: [
              {
                id: '10000000-0000-0000-0000-000000000002',
                fullName: 'مطور تكامل',
                email: 'dev@api-governance.local',
                isActive: true,
                isActivated: true,
                isLockedOut: false,
                createdAt: '2026-11-24T08:00:00Z',
                roles: [{ id: '20000000-0000-0000-0000-000000000010', name: 'GovernanceReviewer' }],
              },
            ],
            page: 1,
            pageSize: 10,
            totalCount: 1,
            totalPages: 1,
          }),
          { status: 200, headers: { 'Content-Type': 'application/json' } }
        );
      }

      if (url === '/api/v1/users/10000000-0000-0000-0000-000000000002/permissions') {
        return new Response(JSON.stringify(buildMatrixResponse()), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        });
      }

      if (url.endsWith('/api/v1/auth/csrf')) {
        return new Response(
          JSON.stringify({ token: 'csrf-ui15a', headerName: 'X-CSRF-TOKEN' }),
          { status: 200, headers: { 'Content-Type': 'application/json' } }
        );
      }

      if (url === '/api/v1/users/10000000-0000-0000-0000-000000000002/permission-overrides' && init?.method === 'PUT') {
        const headers = init.headers as Record<string, string>;
        expect(headers['X-CSRF-TOKEN']).toBe('csrf-ui15a');
        const body = JSON.parse(String(init.body)) as {
          overrides: Array<{ permissionId: string; decision: 'GRANT' | 'REVOKE' }>;
        };
        submittedOverrides = body.overrides;
        currentOverrides = body.overrides;
        return new Response(JSON.stringify(buildMatrixResponse()), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        });
      }

      return new Response('{}', { status: 404 });
    });

    vi.stubGlobal('fetch', fetchMock);
    render(<App initialAuthenticatedUser={FULL_PERMISSIONS_ADMIN} />);

    fireEvent.click(screen.getByRole('button', { name: /الإدارة/ }));
    fireEvent.click(screen.getByRole('button', { name: /المستخدمون/ }));
    expect(await screen.findByText('مطور تكامل')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'الصلاحيات' }));
    expect(await screen.findByText(/مصفوفة الصلاحيات الفردية للمستخدم/)).toBeInTheDocument();

    // Revoke inherited 'users.view' (uncheck) and Grant 'roles.view' (check)
    const usersViewCheckbox = (await screen.findByLabelText('المستخدمون - عرض')) as HTMLInputElement;
    const rolesViewCheckbox = screen.getByLabelText('الأدوار التنظيمية - عرض') as HTMLInputElement;

    expect(usersViewCheckbox).toBeChecked();
    expect(rolesViewCheckbox).not.toBeChecked();

    fireEvent.click(usersViewCheckbox);
    fireEvent.click(rolesViewCheckbox);

    expect(usersViewCheckbox).not.toBeChecked();
    expect(rolesViewCheckbox).toBeChecked();
    expect(screen.getByText(/سحب فردي \(REVOKE\): 1/)).toBeInTheDocument();
    expect(screen.getByText(/منح فردي \(GRANT\): 1/)).toBeInTheDocument();

    // Save overrides via CentralModalDialog confirmation
    fireEvent.click(screen.getByRole('button', { name: 'حفظ الصلاحيات الفردية' }));
    const confirmModal = await screen.findByRole('dialog');
    fireEvent.click(within(confirmModal).getByRole('button', { name: 'إغلاق نافذة الرسالة' }));

    const successModal = await screen.findByRole('dialog');
    expect(within(successModal).getByText('تم حفظ الصلاحيات الفردية')).toBeInTheDocument();
    expect(submittedOverrides).toEqual([
      { permissionId: '30000000-0000-0000-0000-000000000002', decision: 'REVOKE' },
      { permissionId: '30000000-0000-0000-0000-000000000004', decision: 'GRANT' },
    ]);
    fireEvent.click(within(successModal).getByRole('button', { name: 'إغلاق نافذة الرسالة' }));

    // Reset all overrides back to inherited state and save empty overrides array
    fireEvent.click(screen.getByRole('button', { name: 'إعادة الكل إلى الموروث' }));
    expect(usersViewCheckbox).toBeChecked();
    expect(rolesViewCheckbox).not.toBeChecked();

    fireEvent.click(screen.getByRole('button', { name: 'حفظ الصلاحيات الفردية' }));
    const confirmResetModal = await screen.findByRole('dialog');
    fireEvent.click(within(confirmResetModal).getByRole('button', { name: 'إغلاق نافذة الرسالة' }));
    await screen.findByText('تم حفظ الصلاحيات الفردية');
    expect(submittedOverrides).toEqual([]);
  });

  it('surfaces server 403 ProblemDetails inside CentralModalDialog when saving matrix fails due to anti-escalation or revoked permission', async () => {
    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString();

      if (url.startsWith('/api/v1/roles?')) {
        return new Response(
          JSON.stringify({
            items: [
              {
                id: '20000000-0000-0000-0000-000000000010',
                name: 'GovernanceReviewer',
                isActive: true,
                isPinned: false,
                assignedUsersCount: 1,
                grantedPermissionsCount: 1,
              },
            ],
            page: 1,
            pageSize: 10,
            totalCount: 1,
            totalPages: 1,
          }),
          { status: 200, headers: { 'Content-Type': 'application/json' } }
        );
      }

      if (url === '/api/v1/permissions') {
        return new Response(
          JSON.stringify({
            permissions: SAMPLE_PERMISSIONS,
            columns: SAMPLE_COLUMNS,
            screens: SAMPLE_SCREENS,
          }),
          { status: 200, headers: { 'Content-Type': 'application/json' } }
        );
      }

      if (url === '/api/v1/roles/20000000-0000-0000-0000-000000000010/permissions' && (!init?.method || init.method === 'GET')) {
        return new Response(
          JSON.stringify({
            roleId: '20000000-0000-0000-0000-000000000010',
            roleName: 'GovernanceReviewer',
            isActive: true,
            isPinned: false,
            permissionIds: ['30000000-0000-0000-0000-000000000001'],
            permissionCodes: ['dashboard.view'],
          }),
          { status: 200, headers: { 'Content-Type': 'application/json' } }
        );
      }

      if (url.endsWith('/api/v1/auth/csrf')) {
        return new Response(
          JSON.stringify({ token: 'csrf-403', headerName: 'X-CSRF-TOKEN' }),
          { status: 200, headers: { 'Content-Type': 'application/json' } }
        );
      }

      if (url === '/api/v1/roles/20000000-0000-0000-0000-000000000010/permissions' && init?.method === 'PUT') {
        return new Response(
          JSON.stringify({
            status: 403,
            code: 'FORBIDDEN',
            title: 'صلاحية غير كافية',
            message: 'Privilege escalation blocked by central server authorization.',
            traceId: 'trace-403-test',
          }),
          { status: 403, headers: { 'Content-Type': 'application/problem+json' } }
        );
      }

      return new Response('{}', { status: 404 });
    });

    vi.stubGlobal('fetch', fetchMock);
    render(<App initialAuthenticatedUser={FULL_PERMISSIONS_ADMIN} />);

    fireEvent.click(screen.getByRole('button', { name: /الإدارة/ }));
    fireEvent.click(screen.getByRole('button', { name: /الأدوار والصلاحيات/ }));
    fireEvent.click(await screen.findByRole('button', { name: 'مصفوفة الصلاحيات' }));

    await screen.findByLabelText('لوحة التحكم - عرض');
    fireEvent.click(screen.getByRole('button', { name: 'حفظ مصفوفة الدور' }));
    const confirmModal = await screen.findByRole('dialog');
    fireEvent.click(within(confirmModal).getByRole('button', { name: 'إغلاق نافذة الرسالة' }));

    const errorModal = await screen.findByRole('dialog');
    expect(within(errorModal).getByText(/Privilege escalation blocked/)).toBeInTheDocument();
  });
});

