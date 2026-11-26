import { fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { App } from '../App';

describe('Task I01 & S01-T02 Authentication & Session Flows (Clean Username Login & Protected Routes)', () => {
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

  it('protects workspace routes by rendering clean Login screen without experimental or technical copy', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ status: 401, code: 'ERR_UNAUTHORIZED' }), {
        status: 401,
        headers: { 'Content-Type': 'application/problem+json' },
      })
    );
    vi.stubGlobal('fetch', fetchMock);

    render(<App />);

    expect(
      await screen.findByRole('heading', { level: 1, name: 'تسجيل الدخول' })
    ).toBeInTheDocument();

    // Protected workspace sidebar must not be rendered to unauthenticated users
    expect(screen.queryByLabelText('Sidebar Navigation')).not.toBeInTheDocument();
    // Self-registration, activation tabs, and technical debug texts are strictly absent
    expect(screen.queryByRole('tablist')).not.toBeInTheDocument();
    expect(screen.queryByText(/ASP\.NET Core Identity|UI-24|Cookie HttpOnly|تنبيه التفعيل/i)).not.toBeInTheDocument();
  });

  it('validates login inputs with CentralModalDialog, toggles password visibility with eye icon, and performs CSRF + Cookie login by Username, Change Password, and Logout', async () => {
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
        const body = JSON.parse(String(init?.body ?? '{}')) as Record<string, string>;
        expect(body.username).toBe('dev.user');

        return new Response(
          JSON.stringify({
            id: '11111111-1111-1111-1111-111111111111',
            fullName: 'مطور موثق',
            username: 'dev.user',
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

    await screen.findByRole('heading', { level: 1, name: 'تسجيل الدخول' });

    // Submit empty form -> triggers central Error Modal
    fireEvent.click(screen.getByRole('button', { name: 'تسجيل الدخول' }));
    expect(screen.getByRole('dialog')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'إغلاق نافذة الرسالة' }));

    // Fill username & password, test eye icon toggle, and sign in
    fireEvent.change(screen.getByLabelText(/اسم المستخدم/), {
      target: { value: 'dev.user' },
    });
    const pwdInput = screen.getByLabelText(/^كلمة المرور/) as HTMLInputElement;
    expect(pwdInput.type).toBe('password');
    fireEvent.click(screen.getByRole('button', { name: 'إظهار كلمة المرور' }));
    expect(pwdInput.type).toBe('text');
    fireEvent.click(screen.getByRole('button', { name: 'إخفاء كلمة المرور' }));
    expect(pwdInput.type).toBe('password');

    fireEvent.change(pwdInput, {
      target: { value: 'StrongP@ssword!2026' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'تسجيل الدخول' }));

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

    // Click Logout button -> posts /api/v1/auth/logout with CSRF and returns to Login
    fireEvent.click(screen.getByRole('button', { name: 'تسجيل الخروج' }));
    await screen.findByRole('heading', { level: 1, name: 'تسجيل الدخول' });

    // Sign back in to verify Change Password flow -> forced back to Login
    fireEvent.change(screen.getByLabelText(/اسم المستخدم/), {
      target: { value: 'dev.user' },
    });
    fireEvent.change(screen.getByLabelText(/^كلمة المرور/), {
      target: { value: 'StrongP@ssword!2026' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'تسجيل الدخول' }));
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
    await screen.findByRole('heading', { level: 1, name: 'تسجيل الدخول' });
  });

  it('supports bilingual (AR/EN) and SVG Light/Dark theme switching on the Login screen', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 401 }));
    vi.stubGlobal('fetch', fetchMock);
    render(<App />);

    await screen.findByRole('heading', { level: 1, name: 'تسجيل الدخول' });

    // Switch to English LTR & Dark theme on Login screen
    fireEvent.click(screen.getByRole('button', { name: 'EN' }));
    expect(document.documentElement.dir).toBe('ltr');
    expect(
      screen.getByRole('heading', { level: 1, name: 'Sign In' })
    ).toBeInTheDocument();
    expect(screen.getByLabelText(/Username/)).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Toggle Light/Dark theme' }));
    expect(document.documentElement.getAttribute('data-theme')).toBe('dark');
  });

  it('displays end-user friendly error messages without exposing raw technical traceIds or server jargon', async () => {
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url.endsWith('/api/v1/me')) {
        return new Response(null, { status: 401 });
      }
      if (url.endsWith('/api/v1/auth/csrf')) {
        return new Response(
          JSON.stringify({ token: 'csrf-err', headerName: 'X-CSRF-TOKEN' }),
          { status: 200, headers: { 'Content-Type': 'application/json' } }
        );
      }
      if (url.endsWith('/api/v1/auth/login')) {
        return new Response(
          JSON.stringify({
            status: 500,
            code: 'ERR_INTERNAL_SERVER_ERROR',
            title: 'خطأ داخلي في الخادم',
            message: 'حدث خطأ غير متوقع أثناء معالجة الطلب. يرجى مراجعة معرف التتبع.',
            traceId: '00-bbb6e3be15122d7543f83d05047ae361-d915aa4bec61f328-00',
          }),
          { status: 500, headers: { 'Content-Type': 'application/problem+json' } }
        );
      }
      return new Response(null, { status: 404 });
    });

    vi.stubGlobal('fetch', fetchMock);
    render(<App />);

    await screen.findByRole('heading', { level: 1, name: 'تسجيل الدخول' });
    fireEvent.change(screen.getByLabelText(/اسم المستخدم/), { target: { value: 'admin' } });
    fireEvent.change(screen.getByLabelText(/^كلمة المرور/), { target: { value: 'StrongP@ssword!2026' } });
    fireEvent.click(screen.getByRole('button', { name: 'تسجيل الدخول' }));

    const dialog = await screen.findByRole('dialog');
    expect(dialog).toHaveTextContent('تعذر تسجيل الدخول حاليًا');
    expect(dialog).toHaveTextContent(
      'الخدمة غير متاحة مؤقتًا أو تعذر الاتصال. يرجى المحاولة مرة أخرى بعد قليل.'
    );
    expect(dialog).not.toHaveTextContent('00-bbb6e3be15122d7543f83d05047ae361');
    expect(dialog).not.toHaveTextContent('خطأ داخلي في الخادم');
  });
});
