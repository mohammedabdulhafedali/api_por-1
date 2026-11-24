import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { App } from '../App';

describe('Task I01 Authentication & Session Flows (UI-24 & Protected Routes)', () => {
  beforeEach(() => {
    window.localStorage.clear();
    window.sessionStorage.clear();
    document.documentElement.lang = 'ar';
    document.documentElement.dir = 'rtl';
    document.documentElement.setAttribute('data-theme', 'light');
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('protects workspace routes by rendering UI-24 Login when unauthenticated and forbids self-registration', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ status: 401, code: 'ERR_UNAUTHORIZED' }), {
        status: 401,
        headers: { 'Content-Type': 'application/problem+json' },
      })
    );
    vi.stubGlobal('fetch', fetchMock);

    render(<App />);

    expect(
      await screen.findByRole('heading', { level: 1, name: 'تسجيل الدخول إلى المنصة' })
    ).toBeInTheDocument();

    // Protected workspace sidebar must not be rendered to unauthenticated users
    expect(screen.queryByLabelText('Sidebar Navigation')).not.toBeInTheDocument();
    // Self-registration is strictly disabled
    expect(screen.queryByText(/تسجيل ذاتي جديد|إنشاء حساب جديد|Create Account|Sign Up/i)).not.toBeInTheDocument();
  });

  it('validates login inputs with CentralModalDialog and performs CSRF + Cookie login, Change Password, and Logout without storing secrets in Web Storage', async () => {
    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString();

      if (url.endsWith('/api/v1/me')) {
        return new Response(JSON.stringify({ status: 401 }), {
          status: 401,
          headers: { 'Content-Type': 'application/problem+json' },
        });
      }

      if (url.endsWith('/api/v1/auth/csrf')) {
        return new Response(
          JSON.stringify({ token: 'csrf-token-i01', headerName: 'X-CSRF-TOKEN' }),
          { status: 200, headers: { 'Content-Type': 'application/json' } }
        );
      }

      if (url.endsWith('/api/v1/auth/login')) {
        expect(init?.credentials).toBe('include');
        const headers = init?.headers as Record<string, string>;
        expect(headers['X-CSRF-TOKEN']).toBe('csrf-token-i01');

        return new Response(
          JSON.stringify({
            id: '11111111-1111-1111-1111-111111111111',
            fullName: 'مطور موثق',
            email: 'dev@company.local',
            isActive: true,
            authProvider: 'LocalIdentity',
            roles: [{ id: '22222222-2222-2222-2222-222222222222', name: 'Developer' }],
            permissions: ['api_documents.view'],
            traceId: 'trace-login-ok',
          }),
          { status: 200, headers: { 'Content-Type': 'application/json' } }
        );
      }

      if (url.endsWith('/api/v1/auth/password/change')) {
        const headers = init?.headers as Record<string, string>;
        expect(headers['X-CSRF-TOKEN']).toBe('csrf-token-i01');
        return new Response(null, { status: 204 });
      }

      if (url.endsWith('/api/v1/auth/logout')) {
        const headers = init?.headers as Record<string, string>;
        expect(headers['X-CSRF-TOKEN']).toBe('csrf-token-i01');
        return new Response(null, { status: 204 });
      }

      return new Response(null, { status: 404 });
    });

    vi.stubGlobal('fetch', fetchMock);
    render(<App />);

    await screen.findByRole('heading', { level: 1, name: 'تسجيل الدخول إلى المنصة' });

    // Submit empty form -> triggers central Error Modal
    fireEvent.click(screen.getByRole('button', { name: 'دخول إلى المنصة' }));
    expect(screen.getByRole('dialog')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'إغلاق نافذة الرسالة' }));

    // Fill email & password and sign in
    fireEvent.change(screen.getByLabelText(/البريد الإلكتروني/), {
      target: { value: 'dev@company.local' },
    });
    fireEvent.change(screen.getByLabelText(/كلمة المرور/), {
      target: { value: 'StrongP@ssword!2026' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'دخول إلى المنصة' }));

    // Authenticated workspace renders with user pill
    const pill = await screen.findByTestId('authenticated-user-pill');
    expect(pill).toHaveTextContent('مطور موثق');
    expect(pill).toHaveTextContent('dev@company.local');

    // Verify Web Storage only contains approved UI preferences (no passwords, CSRF tokens, or auth secrets)
    expect(Object.keys(window.localStorage).sort()).toEqual([
      'api_gov_ui_lang',
      'api_gov_ui_theme',
    ]);
    expect(JSON.stringify(window.localStorage)).not.toContain('StrongP@ssword!2026');
    expect(JSON.stringify(window.localStorage)).not.toContain('csrf-token-i01');
    expect(window.sessionStorage.length).toBe(0);

    // Click Logout button -> posts /api/v1/auth/logout with CSRF and returns to UI-24 Login
    fireEvent.click(screen.getByRole('button', { name: 'تسجيل الخروج' }));
    await screen.findByRole('heading', { level: 1, name: 'تسجيل الدخول إلى المنصة' });

    // Sign back in to verify Change Password flow -> forced back to UI-24
    fireEvent.change(screen.getByLabelText(/البريد الإلكتروني/), {
      target: { value: 'dev@company.local' },
    });
    fireEvent.change(screen.getByLabelText(/كلمة المرور/), {
      target: { value: 'StrongP@ssword!2026' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'دخول إلى المنصة' }));
    await screen.findByTestId('authenticated-user-pill');

    // Navigate to standalone Change Password view (never a Modal)
    fireEvent.click(screen.getByRole('button', { name: 'تغيير كلمة المرور' }));
    expect(
      screen.getByRole('heading', { level: 2, name: 'تغيير كلمة المرور' })
    ).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText(/كلمة المرور الحالية/), {
      target: { value: 'StrongP@ssword!2026' },
    });

    // Reject 14-char password client-side (policy requires 15+ characters)
    fireEvent.change(screen.getByLabelText(/^كلمة المرور الجديدة/), {
      target: { value: 'Short14Char!99' },
    });
    fireEvent.change(screen.getByLabelText(/تأكيد كلمة المرور الجديدة/), {
      target: { value: 'Short14Char!99' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'حفظ كلمة المرور الجديدة' }));
    expect(
      screen.getByText('يجب ألا تقل كلمة المرور الجديدة عن 15 حرفًا.')
    ).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'إغلاق نافذة الرسالة' }));

    fireEvent.change(screen.getByLabelText(/^كلمة المرور الجديدة/), {
      target: { value: 'NewStrongP@ss!2026' },
    });
    fireEvent.change(screen.getByLabelText(/تأكيد كلمة المرور الجديدة/), {
      target: { value: 'NewStrongP@ss!2026' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'حفظ كلمة المرور الجديدة' }));

    await screen.findByText('تم تغيير كلمة المرور بنجاح');
    fireEvent.click(screen.getByRole('button', { name: 'إغلاق نافذة الرسالة' }));

    // Forced back to Login page after password change
    await screen.findByRole('heading', { level: 1, name: 'تسجيل الدخول إلى المنصة' });
  });

  it('supports bilingual/theme switching on UI-24 and Account Activation / Password Reset completion flow', async () => {
    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString();

      if (url.endsWith('/api/v1/me')) {
        return new Response(null, { status: 401 });
      }
      if (url.endsWith('/api/v1/auth/csrf')) {
        return new Response(
          JSON.stringify({ token: 'csrf-reset-token', headerName: 'X-CSRF-TOKEN' }),
          { status: 200, headers: { 'Content-Type': 'application/json' } }
        );
      }
      if (url.endsWith('/api/v1/auth/password/reset/complete')) {
        const body = JSON.parse(String(init?.body ?? '{}')) as Record<string, string>;
        expect(body.email).toBe('newuser@company.local');
        expect(body.token).toBe('SINGLE_USE_TOKEN_XYZ_12345');
        expect(body.newPassword).toBe('ActivatedP@ssword!2026');
        return new Response(null, { status: 204 });
      }
      return new Response(null, { status: 404 });
    });

    vi.stubGlobal('fetch', fetchMock);
    render(<App />);

    await screen.findByRole('heading', { level: 1, name: 'تسجيل الدخول إلى المنصة' });

    // Switch to English LTR & Dark theme on Login screen
    fireEvent.click(screen.getByRole('button', { name: 'EN' }));
    expect(document.documentElement.dir).toBe('ltr');
    expect(
      screen.getByRole('heading', { level: 1, name: 'Sign In to Platform' })
    ).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Toggle Light/Dark theme' }));
    expect(document.documentElement.getAttribute('data-theme')).toBe('dark');

    // Switch to Activate Account / Reset Password tab
    fireEvent.click(screen.getByRole('tab', { name: 'Activate Account / Reset Password' }));
    fireEvent.change(screen.getByLabelText(/Email Address/), {
      target: { value: 'newuser@company.local' },
    });
    fireEvent.change(screen.getByLabelText(/Single-Use Activation \/ Reset Token/), {
      target: { value: 'SINGLE_USE_TOKEN_XYZ_12345' },
    });
    fireEvent.change(screen.getByLabelText(/^New Password/), {
      target: { value: 'ActivatedP@ssword!2026' },
    });
    fireEvent.change(screen.getByLabelText(/Confirm New Password/), {
      target: { value: 'ActivatedP@ssword!2026' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Activate / Set New Password' }));

    await waitFor(() => {
      expect(screen.getByText('Account Activated & Password Updated')).toBeInTheDocument();
    });
  });
});

