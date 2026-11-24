import { fireEvent, render, screen, within } from '@testing-library/react';
import { beforeEach, describe, expect, it } from 'vitest';
import { App } from '../App';
import { AuthenticatedUser } from '../api/authApi';
import { SIDEBAR_GROUPS, STANDALONE_NAV_ITEMS } from '../constants/uiBaseline';

const MOCK_AUTHENTICATED_USER: AuthenticatedUser = {
  id: '00000000-0000-0000-0000-000000000001',
  fullName: 'مستخدم الاختبار المعتمد',
  email: 'tester@api-governance.local',
  isActive: true,
  authProvider: 'LocalIdentity',
  roles: [{ id: '00000000-0000-0000-0000-000000000010', name: 'PlatformAdmin' }],
  permissions: [],
};

describe('API Governance Shared UI Component Library & Shell (F02, F06 & F08)', () => {
  beforeEach(() => {
    window.localStorage.clear();
    document.documentElement.lang = 'ar';
    document.documentElement.dir = 'rtl';
    document.documentElement.setAttribute('data-theme', 'light');
  });

  it('renders initial Arabic RTL state and Light theme with topbar page title without duplicate hero block', () => {
    render(<App initialAuthenticatedUser={MOCK_AUTHENTICATED_USER} />);

    expect(document.documentElement.lang).toBe('ar');
    expect(document.documentElement.dir).toBe('rtl');
    expect(document.documentElement.getAttribute('data-theme')).toBe('light');

    const topbarHeading = screen.getByRole('heading', {
      level: 1,
      name: 'قالب الصفحة الأولي والهوية البصرية المعتمدة',
    });
    expect(topbarHeading).toBeInTheDocument();
    expect(screen.getAllByRole('heading', { level: 1 })).toHaveLength(1);
  });

  it('switches language and direction between Arabic (RTL) and English (LTR) and persists preference', () => {
    render(<App initialAuthenticatedUser={MOCK_AUTHENTICATED_USER} />);

    const enButton = screen.getByRole('button', { name: 'EN' });
    const arButton = screen.getByRole('button', { name: 'AR' });

    fireEvent.click(enButton);

    expect(document.documentElement.lang).toBe('en');
    expect(document.documentElement.dir).toBe('ltr');
    expect(window.localStorage.getItem('api_gov_ui_lang')).toBe('en');
    expect(
      screen.getByRole('heading', {
        level: 1,
        name: 'Initial Page Template & Approved Visual Identity',
      })
    ).toBeInTheDocument();

    fireEvent.click(arButton);

    expect(document.documentElement.lang).toBe('ar');
    expect(document.documentElement.dir).toBe('rtl');
    expect(window.localStorage.getItem('api_gov_ui_lang')).toBe('ar');
  });

  it('switches between Light and Dark themes while preserving controlled form inputs', () => {
    render(<App initialAuthenticatedUser={MOCK_AUTHENTICATED_USER} />);

    const sampleInput = screen.getByLabelText(
      /حقل نصي تجريبي \(فحص تباين الحقول والخطوط\)/
    ) as HTMLInputElement;
    fireEvent.change(sampleInput, { target: { value: 'نص تجريبي محفوظ' } });
    expect(sampleInput.value).toBe('نص تجريبي محفوظ');

    const themeButton = screen.getByRole('button', {
      name: 'تبديل المظهر النهاري/الليلي',
    });
    fireEvent.click(themeButton);

    expect(document.documentElement.getAttribute('data-theme')).toBe('dark');
    expect(window.localStorage.getItem('api_gov_ui_theme')).toBe('dark');
    expect(sampleInput.value).toBe('نص تجريبي محفوظ');

    fireEvent.click(themeButton);
    expect(document.documentElement.getAttribute('data-theme')).toBe('light');
    expect(window.localStorage.getItem('api_gov_ui_theme')).toBe('light');
  });

  it('enforces collapsible sidebar with approved 5-section order and only one open section at a time', () => {
    render(<App initialAuthenticatedUser={MOCK_AUTHENTICATED_USER} />);

    expect(SIDEBAR_GROUPS.map((g) => g.label.ar)).toEqual([
      'الأنظمة والجهات',
      'الإعدادات',
      'الإدارة',
      'وثائق API',
      'التكاملات',
    ]);
    expect(STANDALONE_NAV_ITEMS).toHaveLength(3);

    const systemsGroupBtn = screen.getByRole('button', { name: /الأنظمة والجهات/ });
    const settingsGroupBtn = screen.getByRole('button', { name: /الإعدادات/ });
    const docsGroupBtn = screen.getByRole('button', { name: /وثائق API/ });

    expect(systemsGroupBtn).toHaveAttribute('aria-expanded', 'true');
    expect(settingsGroupBtn).toHaveAttribute('aria-expanded', 'false');

    fireEvent.click(settingsGroupBtn);
    expect(systemsGroupBtn).toHaveAttribute('aria-expanded', 'false');
    expect(settingsGroupBtn).toHaveAttribute('aria-expanded', 'true');
    expect(screen.getByRole('button', { name: /مسارات الاعتماد/ })).toBeInTheDocument();

    fireEvent.click(docsGroupBtn);
    expect(settingsGroupBtn).toHaveAttribute('aria-expanded', 'false');
    expect(docsGroupBtn).toHaveAttribute('aria-expanded', 'true');
  });

  it('opens and closes unified central Modal dialogs (Success, Warning, Error, Confirm) and returns focus on Error close', () => {
    render(<App initialAuthenticatedUser={MOCK_AUTHENTICATED_USER} />);

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();

    // Open Success Modal
    fireEvent.click(screen.getByRole('button', { name: 'معاينة رسالة نجاح (Modal)' }));
    expect(screen.getByRole('dialog')).toBeInTheDocument();
    expect(screen.getByText('تم التحقق من القالب بنجاح')).toBeInTheDocument();

    // Close Success Modal
    fireEvent.click(screen.getByRole('button', { name: 'إغلاق نافذة الرسالة' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();

    // Open Confirm Modal and cancel via Escape / Cancel button
    fireEvent.click(screen.getByRole('button', { name: 'معاينة حوار تأكيد (Modal)' }));
    const confirmDialog = screen.getByRole('dialog');
    expect(within(confirmDialog).getByText('تأكيد الإجراء')).toBeInTheDocument();
    fireEvent.click(within(confirmDialog).getByRole('button', { name: 'إلغاء' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();

    // Open Error Modal and verify focus returns to #sample-text-input when closed
    fireEvent.click(screen.getByRole('button', { name: 'معاينة رسالة خطأ (Modal)' }));
    expect(screen.getByRole('dialog')).toBeInTheDocument();
    expect(screen.getByText('نموذج رسالة خطأ تحقق')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'إغلاق نافذة الرسالة' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(document.activeElement?.id).toBe('sample-text-input');
  });

  it('renders standalone Create and Edit pages as full pages (never Modals) and validates required fields', () => {
    render(<App initialAuthenticatedUser={MOCK_AUTHENTICATED_USER} />);

    fireEvent.click(
      screen.getByRole('button', { name: 'فتح صفحة إضافة مستقلة (بدون Modal)' })
    );

    // Must not open a dialog modal for the create page itself
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(
      screen.getByRole('heading', {
        level: 2,
        name: 'قالب صفحة الإضافة المستقلة داخل التطبيق',
      })
    ).toBeInTheDocument();

    // Submitting empty required field triggers field error + central error modal
    fireEvent.click(
      screen.getByRole('button', { name: 'حفظ ومعاينة رسالة التأكيد' })
    );
    expect(screen.getByRole('alert')).toHaveTextContent(
      'هذا الحقل إلزامي عند التحقق من النموذج.'
    );
    expect(screen.getByRole('dialog')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'إغلاق نافذة الرسالة' }));

    // Fill required field and submit -> opens success modal
    const input = screen.getByLabelText(/حقل نصي تجريبي/);
    fireEvent.change(input, { target: { value: 'وثيقة قياسية' } });
    fireEvent.click(
      screen.getByRole('button', { name: 'حفظ ومعاينة رسالة التأكيد' })
    );
    expect(screen.getByRole('dialog')).toBeInTheDocument();
    expect(screen.getByText('تم التحقق من القالب بنجاح')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'إغلاق نافذة الرسالة' }));

    // Return to catalog view
    fireEvent.click(
      screen.getByRole('button', { name: 'العودة إلى صفحة استعراض المكونات' })
    );
    expect(
      screen.getByRole('heading', {
        level: 2,
        name: /هيكل UI المشترك ومكتبة المكونات/,
      })
    ).toBeInTheDocument();
  });

  it('supports DataTable search, category filtering, pagination, and Loading/Empty/Error state views', () => {
    render(<App initialAuthenticatedUser={MOCK_AUTHENTICATED_USER} />);

    // Pagination initially shows Page 1 of 3 (15 items total, 5 per page)
    expect(screen.getByText(/الصفحة 1 من 3/)).toBeInTheDocument();

    // Navigate to Page 2
    fireEvent.click(screen.getByRole('button', { name: 'التالي' }));
    expect(screen.getByText(/الصفحة 2 من 3/)).toBeInTheDocument();

    // Search for UI-25
    const searchInput = screen.getByRole('searchbox', {
      name: 'البحث في جدول فهرس المواصفة',
    });
    fireEvent.change(searchInput, { target: { value: 'UI-25' } });
    expect(screen.getByText('مصفوفة الأدوار والصلاحيات')).toBeInTheDocument();
    expect(screen.getByText(/الصفحة 1 من 1/)).toBeInTheDocument();

    // Switch through Loading, Empty, and Error states
    fireEvent.click(screen.getByRole('button', { name: 'حالة التحميل' }));
    expect(screen.getByRole('status')).toHaveTextContent('جارٍ تحميل بيانات الجدول...');

    fireEvent.click(screen.getByRole('button', { name: 'حالة الفراغ' }));
    expect(
      screen.getByText('لا توجد عناصر مطابقة للبحث أو التصفية')
    ).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'حالة الخطأ' }));
    expect(screen.getByRole('alert')).toHaveTextContent('تعذر تحميل البيانات');
    fireEvent.click(screen.getByRole('button', { name: 'إعادة المحاولة' }));
    expect(screen.getByText('مصفوفة الأدوار والصلاحيات')).toBeInTheDocument();
  });

  it('supports SelectionMatrix cell, row, column, section, and select-all bulk checkboxes', () => {
    render(<App initialAuthenticatedUser={MOCK_AUTHENTICATED_USER} />);

    expect(
      screen.getByText(/عدد الخلايا المحددة في المعاينة: 2/)
    ).toBeInTheDocument();

    // Select entire row for UI-01 (3 applicable columns: view, create, edit)
    const rowCheckbox = screen.getByRole('checkbox', {
      name: 'تحديد صف: أنظمة الشركة',
    });
    fireEvent.click(rowCheckbox);
    expect(
      screen.getByText(/عدد الخلايا المحددة في المعاينة: 4/)
    ).toBeInTheDocument();

    // Select all cells across matrix (3 + 3 + 4 = 10 applicable cells)
    const selectAllCheckbox = screen.getByRole('checkbox', {
      name: 'تحديد الكل',
    });
    fireEvent.click(selectAllCheckbox);
    expect(
      screen.getByText(/عدد الخلايا المحددة في المعاينة: 10/)
    ).toBeInTheDocument();

    // Deselect all cells
    fireEvent.click(selectAllCheckbox);
    expect(
      screen.getByText(/عدد الخلايا المحددة في المعاينة: 0/)
    ).toBeInTheDocument();
  });

  it('toggles mobile sidebar drawer cleanly without losing form state', () => {
    render(<App initialAuthenticatedUser={MOCK_AUTHENTICATED_USER} />);

    const sidebar = screen.getByLabelText('Sidebar Navigation');
    expect(sidebar.className).not.toContain('mobile-open');

    const mobileToggleBtn = screen.getByRole('button', {
      name: 'فتح/إغلاق القائمة الجانبية',
    });
    fireEvent.click(mobileToggleBtn);
    expect(sidebar.className).toContain('mobile-open');

    fireEvent.click(mobileToggleBtn);
    expect(sidebar.className).not.toContain('mobile-open');
  });
});
