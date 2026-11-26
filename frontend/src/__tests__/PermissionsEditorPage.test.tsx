import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { PermissionsEditorPage } from '../components/permissions/PermissionsEditorPage';
import * as generalApi from '../api/permissionsApi';
import * as documentsApi from '../api/subjectDocumentPermissionsApi';

vi.mock('../api/permissionsApi');
vi.mock('../api/subjectDocumentPermissionsApi');

const catalog: generalApi.PermissionsCatalogResult = {
  columns: [{ id: 'view', labelAr: 'عرض', labelEn: 'View' }, { id: 'create', labelAr: 'إضافة', labelEn: 'Create' }],
  screens: [
    { id: 'systems', groupId: 'systems', groupLabelAr: 'الأنظمة والجهات', groupLabelEn: 'Systems', titleAr: 'أنظمة الشركة', titleEn: 'Company systems', technicalPath: '', applicableColumnIds: ['view', 'create'] },
    { id: 'users', groupId: 'administration', groupLabelAr: 'الإدارة', groupLabelEn: 'Administration', titleAr: 'المستخدمون', titleEn: 'Users', technicalPath: '', applicableColumnIds: ['view'] },
  ],
  permissions: [
    { id: 'p-view', code: 'company_systems.view', resource: 'company_systems', action: 'view', screenId: 'systems', groupId: 'systems', columnId: 'view' },
    { id: 'p-create', code: 'company_systems.create', resource: 'company_systems', action: 'create', screenId: 'systems', groupId: 'systems', columnId: 'create' },
    { id: 'p-users', code: 'users.view', resource: 'users', action: 'view', screenId: 'users', groupId: 'administration', columnId: 'view' },
    { id: 'p-preserve', code: 'doc.view', resource: 'document', action: 'view', screenId: 'users', groupId: 'administration', columnId: 'view' },
  ],
};
const actorPermissions = ['permissions.view', 'permissions.manage', 'documents.permissions.view', 'documents.permissions.manage'];
function documentPage(page = 1): documentsApi.SubjectDocumentPage {
  const codes = ['doc.view', 'doc.files.view', 'doc.files.download', 'doc.files.upload', 'doc.files.delete', 'doc.review.approve'];
  const labels = ['عرض الوثيقة', 'عرض الملفات', 'تنزيل الملفات', 'رفع الملفات', 'إزالة الملفات', 'اعتماد'];
  return {
    page, totalPages: 2, totalCount: 16,
    permissions: codes.map((code, i) => ({ code, actionKey: code, labelAr: labels[i], labelEn: labels[i] })),
    items: [{ documentId: `doc-${page}`, title: `وثيقة تجريبية ${page}`, groupName: 'مجموعة تجريبية', canManage: true,
      cells: Object.fromEntries(codes.map(code => [code, { decision: null, defaultAllowed: true, allowed: true, source: 'ROLE_GRANT' }])) }],
  };
}
function userMatrix(): generalApi.UserPermissionsMatrixResult {
  return { userId: 'user-1', fullName: 'مستخدم تجريبي', email: 'demo@example.test', isActive: true,
    activeRoles: [{ id: 'role-1', name: 'دور تجريبي' }], screens: catalog.screens, columns: catalog.columns, effectiveAllowedCount: 1,
    permissions: catalog.permissions.map(p => ({ ...p, permissionId: p.id, isInherited: p.id === 'p-view', inheritedFromRoles: [],
      overrideDecision: null, effectiveAllowed: p.id === 'p-view', resolutionSource: p.id === 'p-view' ? 'ROLE_INHERITED' : 'DENIED_DEFAULT' })) };
}
function mount(kind: 'users' | 'roles' = 'users', permissions = actorPermissions) {
  return render(<PermissionsEditorPage kind={kind} subjectId={kind === 'users' ? 'user-1' : 'role-1'} lang="ar"
    actorPermissions={permissions} onBack={vi.fn()} onSessionExpired={vi.fn()} />);
}
async function confirmSave() {
  fireEvent.click(screen.getByRole('button', { name: 'حفظ' }));
  const dialog = await screen.findByRole('dialog');
  fireEvent.click(within(dialog).getByRole('button', { name: 'إغلاق نافذة الرسالة' }));
}

describe('Subject permission editor', () => {
  beforeEach(() => {
    vi.mocked(generalApi.getPermissionsCatalog).mockResolvedValue(catalog);
    vi.mocked(generalApi.getRolePermissions).mockResolvedValue({ roleId: 'role-1', roleName: 'دور تجريبي', isActive: true, isPinned: false,
      permissionIds: ['p-view', 'p-preserve'], permissionCodes: ['company_systems.view', 'doc.view'] });
    vi.mocked(generalApi.getUserPermissionsMatrix).mockResolvedValue(userMatrix());
    vi.mocked(generalApi.replaceUserPermissionOverrides).mockResolvedValue(userMatrix());
    vi.mocked(generalApi.replaceRolePermissions).mockImplementation(async (_, ids) => ({ roleId: 'role-1', roleName: 'دور تجريبي', isActive: true, isPinned: false, permissionIds: ids, permissionCodes: [] }));
    vi.mocked(documentsApi.getSubjectDocumentPermissions).mockImplementation(async (_kind, _id, page) => documentPage(page));
    vi.mocked(documentsApi.patchSubjectDocumentPermissions).mockResolvedValue(undefined);
  });
  afterEach(() => { cleanup(); vi.clearAllMocks(); });

  it('opens the selected role without another selector and preserves grants outside the visible section', async () => {
    mount('roles');
    expect(await screen.findByRole('heading', { name: 'صلاحيات الدور: دور تجريبي' })).toBeInTheDocument();
    expect(screen.queryByRole('combobox')).not.toBeInTheDocument();
    fireEvent.click(screen.getByLabelText('أنظمة الشركة — إضافة'));
    fireEvent.click(screen.getByRole('button', { name: 'الإدارة' }));
    expect(screen.getByLabelText('المستخدمون — عرض')).not.toBeChecked();
    fireEvent.click(screen.getByRole('button', { name: 'الأنظمة والجهات' }));
    expect(screen.getByLabelText('أنظمة الشركة — إضافة')).toBeChecked();
    await confirmSave();
    await waitFor(() => expect(generalApi.replaceRolePermissions).toHaveBeenCalledWith('role-1', ['p-view', 'p-create', 'p-preserve'], 'ar'));
  });

  it('revokes inherited user access and resets the override independently', async () => {
    mount();
    const cell = await screen.findByLabelText('أنظمة الشركة — عرض');
    expect(cell).toBeChecked();
    fireEvent.click(cell);
    expect(cell).not.toBeChecked();
    expect(screen.getByText('منع فردي')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'حسب الأدوار: أنظمة الشركة — عرض' }));
    expect(cell).toBeChecked();
    expect(screen.getByRole('button', { name: 'حفظ' })).toBeDisabled();
    fireEvent.click(cell);
    await confirmSave();
    await waitFor(() => expect(generalApi.replaceUserPermissionOverrides).toHaveBeenCalledWith('user-1', [{ permissionId: 'p-view', decision: 'REVOKE' }], 'ar'));
  });

  it('retains independent document cell changes across action groups and pagination and sends only changed cells', async () => {
    mount(); await screen.findByLabelText('أنظمة الشركة — عرض');
    fireEvent.click(screen.getByRole('tab', { name: 'صلاحيات الوثائق' }));
    await screen.findByText('وثيقة تجريبية 1');
    fireEvent.click(screen.getByRole('button', { name: 'الملفات' }));
    fireEvent.click(screen.getByLabelText('وثيقة تجريبية 1 — تنزيل الملفات'));
    expect(screen.getByLabelText('وثيقة تجريبية 1 — رفع الملفات')).toBeChecked();
    fireEvent.click(screen.getByRole('button', { name: 'الاعتماد' }));
    expect(screen.queryByLabelText('وثيقة تجريبية 1 — تنزيل الملفات')).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'التالي' }));
    await screen.findByText('وثيقة تجريبية 2');
    fireEvent.click(screen.getByRole('button', { name: 'السابق' }));
    await screen.findByText('وثيقة تجريبية 1');
    fireEvent.click(screen.getByRole('button', { name: 'الملفات' }));
    expect(screen.getByLabelText('وثيقة تجريبية 1 — تنزيل الملفات')).not.toBeChecked();
    await confirmSave();
    await waitFor(() => expect(documentsApi.patchSubjectDocumentPermissions).toHaveBeenCalledWith('users', 'user-1', [
      { documentId: 'doc-1', permissionCode: 'doc.files.download', expectedDecision: null, decision: 'REVOKE' },
    ], 'ar'));
    expect(generalApi.replaceUserPermissionOverrides).not.toHaveBeenCalled();
  });

  it('keeps pending edits after a save conflict and warns before leaving', async () => {
    vi.mocked(documentsApi.patchSubjectDocumentPermissions).mockRejectedValue(new Error('تغيرت الصلاحيات؛ أعد التحميل.'));
    mount(); await screen.findByLabelText('أنظمة الشركة — عرض');
    fireEvent.click(screen.getByRole('tab', { name: 'صلاحيات الوثائق' }));
    await screen.findByText('وثيقة تجريبية 1');
    fireEvent.click(screen.getByLabelText('وثيقة تجريبية 1 — عرض الوثيقة'));
    await confirmSave();
    expect(await screen.findByText('تغيرت الصلاحيات؛ أعد التحميل.')).toBeInTheDocument();
    fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'إغلاق نافذة الرسالة' }));
    expect(screen.getByLabelText('وثيقة تجريبية 1 — عرض الوثيقة')).not.toBeChecked();
    fireEvent.click(screen.getByRole('button', { name: 'إلغاء' }));
    expect(await screen.findByText('تغييرات غير محفوظة')).toBeInTheDocument();
  });

  it('supports document ACL read-only access without displaying edit controls as enabled', async () => {
    mount('roles', ['permissions.view', 'documents.permissions.view']);
    await screen.findByLabelText('أنظمة الشركة — عرض');
    fireEvent.click(screen.getByRole('tab', { name: 'صلاحيات الوثائق' }));
    expect(await screen.findByLabelText('وثيقة تجريبية 1 — عرض الوثيقة')).toBeDisabled();
    expect(screen.getByRole('button', { name: 'حفظ' })).toBeDisabled();
    expect(documentsApi.getSubjectDocumentPermissions).toHaveBeenCalledWith('roles', 'role-1', 1, '', 'ar', expect.any(AbortSignal));
  });
});
