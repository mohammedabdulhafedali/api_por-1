import { fireEvent, render, screen, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { App } from '../App';
import { AuthenticatedUser } from '../api/authApi';

const FULL_ADMIN_USER: AuthenticatedUser = {
  id: '10000000-0000-0000-0000-000000000001',
  fullName: 'مدير النظام الأول',
  email: 'admin@api-governance.local',
  isActive: true,
  authProvider: 'LocalIdentity',
  roles: [{ id: '20000000-0000-0000-0000-000000000001', name: 'SystemAdmin' }],
  permissions: [
    'users.view',
    'users.create',
    'users.edit',
    'users.assign_roles',
    'roles.view',
    'roles.create',
    'roles.edit',
    'roles.delete',
  ],
};

const LIMITED_USER: AuthenticatedUser = {
  ...FULL_ADMIN_USER,
  id: '10000000-0000-0000-0000-000000000099',
  roles: [{ id: '20000000-0000-0000-0000-000000000099', name: 'EmptyRole' }],
  permissions: [],
};

describe('Task I03 Roles Management (UI-25) & Multi-Role Assignment (UI-15)', () => {
  beforeEach(() => {
    window.localStorage.clear();
    document.documentElement.lang = 'ar';
    document.documentElement.dir = 'rtl';
    document.documentElement.setAttribute('data-theme', 'light');
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('denies Roles directory when roles.view permission is absent (ADR-01 dynamic permissions)', () => {
    render(<App initialAuthenticatedUser={LIMITED_USER} />);

    fireEvent.click(screen.getByRole('button', { name: /الإدارة/ }));
    fireEvent.click(screen.getByRole('button', { name: /الأدوار والصلاحيات/ }));

    expect(screen.getByText('صلاحية عرض الأدوار غير متوفرة')).toBeInTheDocument();
  });

  it('lists roles, creates a 0-permission role in StandaloneFormPage with CSRF, and deletes unused role via confirm Modal', async () => {
    let rolesList = [
      {
        id: '20000000-0000-0000-0000-000000000001',
        name: 'SystemAdmin',
        isActive: true,
        isPinned: true,
        assignedUsersCount: 1,
        grantedPermissionsCount: 14,
      },
    ];

    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString();

      if (url.startsWith('/api/v1/roles?')) {
        return new Response(
          JSON.stringify({
            items: rolesList,
            page: 1,
            pageSize: 10,
            totalCount: rolesList.length,
            totalPages: 1,
          }),
          { status: 200, headers: { 'Content-Type': 'application/json' } }
        );
      }

      if (url.endsWith('/api/v1/auth/csrf')) {
        return new Response(
          JSON.stringify({ token: 'csrf-i03', headerName: 'X-CSRF-TOKEN' }),
          { status: 200, headers: { 'Content-Type': 'application/json' } }
        );
      }

      if (url === '/api/v1/roles' && init?.method === 'POST') {
        const headers = init.headers as Record<string, string>;
        expect(headers['X-CSRF-TOKEN']).toBe('csrf-i03');
        const body = JSON.parse(String(init.body)) as { name: string; isPinned: boolean };
        const created = {
          id: '20000000-0000-0000-0000-000000000002',
          name: body.name,
          isActive: true,
          isPinned: body.isPinned,
          assignedUsersCount: 0,
          grantedPermissionsCount: 0,
        };
        rolesList = [...rolesList, created];
        return new Response(JSON.stringify(created), {
          status: 201,
          headers: { 'Content-Type': 'application/json' },
        });
      }

      if (url === '/api/v1/roles/20000000-0000-0000-0000-000000000002' && init?.method === 'DELETE') {
        const headers = init.headers as Record<string, string>;
        expect(headers['X-CSRF-TOKEN']).toBe('csrf-i03');
        rolesList = rolesList.filter((r) => r.id !== '20000000-0000-0000-0000-000000000002');
        return new Response(null, { status: 204 });
      }

      return new Response('{}', { status: 404 });
    });

    vi.stubGlobal('fetch', fetchMock);

    render(<App initialAuthenticatedUser={FULL_ADMIN_USER} />);

    fireEvent.click(screen.getByRole('button', { name: /الإدارة/ }));
    fireEvent.click(screen.getByRole('button', { name: /الأدوار والصلاحيات/ }));

    expect(await screen.findByText('SystemAdmin')).toBeInTheDocument();

    // Open Standalone Create Role page (never inside a dialog/modal)
    fireEvent.click(screen.getByRole('button', { name: /إضافة دور جديد/ }));
    expect(screen.getByText('إضافة دور تنظيمي جديد')).toBeInTheDocument();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();

    fireEvent.change(screen.getByLabelText(/اسم الدور/), {
      target: { value: 'SecurityReviewer' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'إنشاء الدور' }));

    const createDialog = await screen.findByRole('dialog');
    expect(within(createDialog).getByText('تم إنشاء الدور بنجاح')).toBeInTheDocument();
    fireEvent.click(within(createDialog).getByRole('button', { name: 'إغلاق نافذة الرسالة' }));

    expect(await screen.findByText('SecurityReviewer')).toBeInTheDocument();

    // Delete unused unpinned role via confirm Modal
    const deleteButtons = screen.getAllByRole('button', { name: 'حذف' });
    fireEvent.click(deleteButtons[0]);

    const confirmDialog = await screen.findByRole('dialog');
    expect(within(confirmDialog).getByText('تأكيد حذف الدور غير المستخدم')).toBeInTheDocument();
    fireEvent.click(within(confirmDialog).getByRole('button', { name: 'إغلاق نافذة الرسالة' }));

    const deletedDialog = await screen.findByRole('dialog');
    expect(within(deletedDialog).getByText('تم حذف الدور')).toBeInTheDocument();
  });

  it('assigns multiple roles atomically to a single user via StandaloneFormPage and renders multi-role badges', async () => {
    const rolesCatalog = [
      {
        id: '20000000-0000-0000-0000-000000000001',
        name: 'SystemAdmin',
        isActive: true,
        isPinned: true,
        assignedUsersCount: 1,
        grantedPermissionsCount: 14,
      },
      {
        id: '20000000-0000-0000-0000-000000000010',
        name: 'ApiReviewer',
        isActive: true,
        isPinned: false,
        assignedUsersCount: 0,
        grantedPermissionsCount: 3,
      },
      {
        id: '20000000-0000-0000-0000-000000000011',
        name: 'ComplianceAuditor',
        isActive: true,
        isPinned: false,
        assignedUsersCount: 0,
        grantedPermissionsCount: 2,
      },
    ];

    let usersList = [
      {
        id: '10000000-0000-0000-0000-000000000002',
        fullName: 'مهندسة حوكمة',
        email: 'governance@api-governance.local',
        isActive: true,
        isActivated: true,
        isLockedOut: false,
        createdAt: '2026-11-24T09:00:00Z',
        roles: [{ id: '20000000-0000-0000-0000-000000000010', name: 'ApiReviewer' }],
      },
    ];

    let submittedRoleIds: string[] = [];

    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString();

      if (url.startsWith('/api/v1/users?')) {
        return new Response(
          JSON.stringify({
            items: usersList,
            page: 1,
            pageSize: 10,
            totalCount: usersList.length,
            totalPages: 1,
          }),
          { status: 200, headers: { 'Content-Type': 'application/json' } }
        );
      }

      if (url.startsWith('/api/v1/roles?')) {
        return new Response(
          JSON.stringify({
            items: rolesCatalog,
            page: 1,
            pageSize: 100,
            totalCount: rolesCatalog.length,
            totalPages: 1,
          }),
          { status: 200, headers: { 'Content-Type': 'application/json' } }
        );
      }

      if (url.endsWith('/api/v1/auth/csrf')) {
        return new Response(
          JSON.stringify({ token: 'csrf-multi', headerName: 'X-CSRF-TOKEN' }),
          { status: 200, headers: { 'Content-Type': 'application/json' } }
        );
      }

      if (url === '/api/v1/users/10000000-0000-0000-0000-000000000002/roles' && init?.method === 'PUT') {
        const headers = init.headers as Record<string, string>;
        expect(headers['X-CSRF-TOKEN']).toBe('csrf-multi');
        const body = JSON.parse(String(init.body)) as { roleIds: string[] };
        submittedRoleIds = body.roleIds;
        const assignedRoles = rolesCatalog
          .filter((r) => submittedRoleIds.includes(r.id))
          .map((r) => ({ id: r.id, name: r.name }));
        usersList = [{ ...usersList[0], roles: assignedRoles }];
        return new Response(
          JSON.stringify({
            userId: '10000000-0000-0000-0000-000000000002',
            roles: assignedRoles,
            effectivePermissionsCount: 5,
          }),
          { status: 200, headers: { 'Content-Type': 'application/json' } }
        );
      }

      return new Response('{}', { status: 404 });
    });

    vi.stubGlobal('fetch', fetchMock);

    render(<App initialAuthenticatedUser={FULL_ADMIN_USER} />);

    fireEvent.click(screen.getByRole('button', { name: /الإدارة/ }));
    fireEvent.click(screen.getByRole('button', { name: /المستخدمون/ }));

    expect(await screen.findByText('مهندسة حوكمة')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'إسناد الأدوار' }));
    expect(await screen.findByText('إسناد الأدوار للمستخدم')).toBeInTheDocument();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();

    const reviewerCheckbox = await screen.findByLabelText(/ApiReviewer/);
    const auditorCheckbox = await screen.findByLabelText(/ComplianceAuditor/);

    expect(reviewerCheckbox).toBeChecked();
    expect(auditorCheckbox).not.toBeChecked();

    fireEvent.click(auditorCheckbox);
    expect(auditorCheckbox).toBeChecked();

    fireEvent.click(screen.getByRole('button', { name: 'حفظ الأدوار المسندة' }));

    const successDialog = await screen.findByRole('dialog');
    expect(within(successDialog).getByText('تم تحديث الأدوار المسندة')).toBeInTheDocument();
    expect(submittedRoleIds).toEqual([
      '20000000-0000-0000-0000-000000000010',
      '20000000-0000-0000-0000-000000000011',
    ]);

    fireEvent.click(within(successDialog).getByRole('button', { name: 'إغلاق نافذة الرسالة' }));

    expect(await screen.findByText('ApiReviewer')).toBeInTheDocument();
    expect(screen.getByText('ComplianceAuditor')).toBeInTheDocument();
  });
});

