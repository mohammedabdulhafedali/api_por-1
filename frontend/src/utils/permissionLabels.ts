import { AppLanguage } from '../constants/uiBaseline';

const ATOMIC_LABELS_BY_CODE: Record<string, { ar: string; en: string }> = {
  'users.assign_roles': { ar: 'إسناد أدوار', en: 'Assign Roles' },
  'users.password.reset': { ar: 'إعادة تعيين كلمة المرور', en: 'Reset Password' },
  'permissions.manage': { ar: 'تعديل الصلاحيات', en: 'Manage Permissions' },
  'api_documents.assign_developer': { ar: 'تعيين مطور', en: 'Assign Developer' },
  'integrations.assign_developer': { ar: 'تعيين مطور', en: 'Assign Developer' },
  'versions.usage.manage': { ar: 'تعيين الإصدار', en: 'Set Active Version' },
  'revisions.submit': { ar: 'إرسال للاعتماد', en: 'Submit' },
  'files.upload': { ar: 'رفع', en: 'Upload' },
  'changes.submit': { ar: 'إرسال للاعتماد', en: 'Submit' },
  'workflows.decide': { ar: 'اعتماد', en: 'Approve' },
  'publications.create': { ar: 'نشر', en: 'Publish' },
  'publications.withdraw': { ar: 'إيقاف نشر', en: 'Withdraw' },
};

const ATOMIC_LABELS_BY_COLUMN: Record<string, { ar: string; en: string }> = {
  view: { ar: 'عرض', en: 'View' },
  create: { ar: 'إضافة', en: 'Create' },
  edit: { ar: 'تعديل', en: 'Edit' },
  delete: { ar: 'حذف', en: 'Delete' },
  manage: { ar: 'إدارة', en: 'Manage' },
  assign: { ar: 'تعيين', en: 'Assign' },
  reset_password: { ar: 'إعادة تعيين كلمة المرور', en: 'Reset Password' },
  submit: { ar: 'إرسال', en: 'Submit' },
  decide: { ar: 'اعتماد', en: 'Approve' },
  withdraw: { ar: 'إيقاف نشر', en: 'Withdraw' },
  download: { ar: 'تنزيل', en: 'Download' },
};

/**
 * Returns a clean, single-concept label for each atomic permission chip (never compound "/" labels).
 */
export function resolvePermissionChipLabel(
  code: string | undefined,
  columnId: string,
  fallbackLabel: string | undefined,
  lang: AppLanguage
): string {
  if (code) {
    const byCode = ATOMIC_LABELS_BY_CODE[code.toLowerCase()];
    if (byCode) return byCode[lang];
  }

  const byCol = ATOMIC_LABELS_BY_COLUMN[columnId];
  if (byCol) return byCol[lang];

  return fallbackLabel || columnId;
}

/**
 * Returns a clean single-word label for bulk-action column pills.
 */
export function resolveBulkColumnLabel(
  columnId: string,
  fallbackLabel: string,
  lang: AppLanguage
): string {
  const byCol = ATOMIC_LABELS_BY_COLUMN[columnId];
  return byCol ? byCol[lang] : fallbackLabel;
}
