import { fireEvent, render, screen, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { App } from '../App';
import { AuthenticatedUser } from '../api/authApi';

const ADMIN_USER: AuthenticatedUser = {
  id: '10000000-0000-0000-0000-000000000001',
  fullName: 'مدير النظام',
  email: 'admin@api-governance.local',
  isActive: true,
  authProvider: 'LocalIdentity',
  roles: [{ id: '20000000-0000-0000-0000-000000000001', name: 'SystemAdmin' }],
  permissions: ['users.view', 'users.create', 'users.edit', 'users.password.reset'],
};

const REGULAR_USER: AuthenticatedUser = {
  ...ADMIN_USER,
  id: '10000000-0000-0000-0000-000000000099',
  permissions: [],
};

describe('Task I02 Users Management Screen (UI-15)', () => {
  beforeEach(() => {
    window.localStorage.clear();
    document.documentElement.lang = 'ar';
    document.documentElement.dir = 'rtl';
    document.documentElement.setAttribute('data-theme', 'light');
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('denies directory display when user lacks users.view permission', () => {
    render(<App initialAuthenticatedUser={REGULAR_USER} />);

    fireEvent.click(screen.getByRole('button', { name: /الإدارة/ }));
    fireEvent.click(screen.getByRole('button', { name: /المستخدمون/ }));

    expect(screen.getByText('صلاحية العرض غير متوفرة')).toBeInTheDocument();
  });

  it('lists users, opens standalone Create page (never Modal), creates user with CSRF, and toggles deactivation with confirm Modal', async () => {
    let usersList = [
      {
        id: '10000000-0000-0000-0000-000000000001',
        fullName: 'مدير النظام',
        email: 'admin@api-governance.local',
        isActive: true,
        isActivated: true,
        isLockedOut: false,
        createdAt: '2026-11-24T08:00:00Z',
        roles: [{ id: '20000000-0000-0000-0000-000000000001', name: 'SystemAdmin' }],
      },
      {
        id: '10000000-0000-0000-0000-000000000002',
        fullName: 'مطور تكامل',
        email: 'dev@api-governance.local',
        isActive: true,
        isActivated: false,
        isLockedOut: false,
        createdAt: '2026-11-24T08:10:00Z',
        roles: [],
      },
    ];

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

      if (url.endsWith('/api/v1/auth/csrf')) {
        return new Response(
          JSON.stringify({ token: 'csrf-i02', headerName: 'X-CSRF-TOKEN' }),
          { status: 200, headers: { 'Content-Type': 'application/json' } }
        );
      }

      if (url === '/api/v1/users' && init?.method === 'POST') {
        const headers = init.headers as Record<string, string>;
        expect(headers['X-CSRF-TOKEN']).toBe('csrf-i02');
        const body = JSON.parse(String(init.body)) as Record<string, unknown>;
        const created = {
          id: '10000000-0000-0000-0000-000000000003',
          fullName: String(body.fullName),
          email: String(body.email),
          isActive: true,
          isActivated: false,
          isLockedOut: false,
          createdAt: '2026-11-24T08:30:00Z',
          roles: [],
        };
        usersList = [...usersList, created];
        return new Response(JSON.stringify(created), {
          status: 201,
          headers: { 'Content-Type': 'application/json' },
        });
      }

      if (url === '/api/v1/users/10000000-0000-0000-0000-000000000002' && init?.method === 'PATCH') {
        const body = JSON.parse(String(init.body)) as Record<string, unknown>;
        usersList = usersList.map((u) =>
          u.id === '10000000-0000-0000-0000-000000000002'
            ? { ...u, isActive: Boolean(body.isActive) }
            : u
        );
        return new Response(
          JSON.stringify(usersList.find((u) => u.id === '10000000-0000-0000-0000-000000000002')),
          { status: 200, headers: { 'Content-Type': 'application/json' } }
        );
      }

      return new Response(null, { status: 404 });
    });

    vi.stubGlobal('fetch', fetchMock);
    render(<App initialAuthenticatedUser={ADMIN_USER} />);

    // Open Administration -> Users (UI-15)
    fireEvent.click(screen.getByRole('button', { name: /الإدارة/ }));
    fireEvent.click(screen.getByRole('button', { name: /المستخدمون/ }));

    expect(
      await screen.findByText('مطور تكامل')
    ).toBeInTheDocument();
    expect(screen.getByText('بانتظار التفعيل الأول')).toBeInTheDocument();

    // Open Standalone Create User page (NOT a Modal)
    fireEvent.click(screen.getByRole('button', { name: 'إضافة مستخدم جديد' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(
      screen.getByRole('heading', { level: 2, name: 'إضافة مستخدم جديد' })
    ).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText(/الاسم الكامل/), {
      target: { value: 'مراجع معماري' },
    });
    fireEvent.change(screen.getByLabelText(/البريد الإلكتروني/), {
      target: { value: 'arch@api-governance.local' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'إنشاء الحساب' }));

    await screen.findByText('تم إنشاء حساب المستخدم');
    fireEvent.click(screen.getByRole('button', { name: 'إغلاق نافذة الرسالة' }));

    expect(await screen.findByText('مراجع معماري')).toBeInTheDocument();

    // Deactivate second user via CentralModalDialog confirmation
    const row = screen.getByText('مطور تكامل').closest('tr')!;
    fireEvent.click(within(row).getByRole('button', { name: 'تعطيل' }));

    const confirmModal = await screen.findByRole('dialog');
    expect(within(confirmModal).getByText('تأكيد تعطيل الحساب')).toBeInTheDocument();
    fireEvent.click(within(confirmModal).getByRole('button', { name: 'إغلاق نافذة الرسالة' }));

    await screen.findByText('تم تحديث بيانات المستخدم');
  });
});

