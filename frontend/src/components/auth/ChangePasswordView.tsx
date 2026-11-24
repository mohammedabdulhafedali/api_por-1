import { useRef, useState } from 'react';
import { ApiProblemError, changeOwnPassword } from '../../api/authApi';
import { AppLanguage, UI_STRINGS } from '../../constants/uiBaseline';
import { CentralModalDialog, FormField, StandaloneFormPage, TextInput } from '../ui';

export interface ChangePasswordViewProps {
  lang: AppLanguage;
  userEmail: string;
  onBack: () => void;
  onPasswordChangedSuccess: () => void;
}

const COPY = {
  ar: {
    badge: 'أمان الحساب · I01',
    title: 'تغيير كلمة المرور',
    subtitle:
      'بعد تغيير كلمة المرور، سيتم إبطال الجلسات النشطة فورًا وإعادة توجيهك لتسجيل الدخول من جديد.',
    backBtn: 'العودة إلى لوحة العمل',
    submitBtn: 'حفظ كلمة المرور الجديدة',
    cancelBtn: 'إلغاء',
    currentLabel: 'كلمة المرور الحالية',
    newLabel: 'كلمة المرور الجديدة',
    newHint: '15 خانة على الأقل تشمل أحرفًا كبيرة وصغيرة وأرقامًا ورموزًا خاصة.',
    confirmLabel: 'تأكيد كلمة المرور الجديدة',
    requiredCurrent: 'كلمة المرور الحالية مطلوبة.',
    minLengthNew: 'يجب ألا تقل كلمة المرور الجديدة عن 15 حرفًا.',
    mustDiffer: 'يجب أن تختلف كلمة المرور الجديدة عن كلمة المرور الحالية.',
    mismatch: 'كلمتا المرور الجديدتان غير متطابقتين.',
    errorTitle: 'تعذر تغيير كلمة المرور',
    errorMsg: 'يرجى التحقق من الحقول المطلوبة ثم إعادة المحاولة.',
    successTitle: 'تم تغيير كلمة المرور بنجاح',
    successMsg: 'تم تحديث كلمة المرور وإبطال الجلسات السابقة. يرجى تسجيل الدخول مرة أخرى.',
  },
  en: {
    badge: 'Account Security · I01',
    title: 'Change Password',
    subtitle:
      'Changing your password immediately revokes active sessions and requires signing in again.',
    backBtn: 'Back to Workspace',
    submitBtn: 'Save New Password',
    cancelBtn: 'Cancel',
    currentLabel: 'Current Password',
    newLabel: 'New Password',
    newHint: 'Minimum 15 characters with uppercase, lowercase, digit, and symbol.',
    confirmLabel: 'Confirm New Password',
    requiredCurrent: 'Current password is required.',
    minLengthNew: 'New password must be at least 15 characters.',
    mustDiffer: 'New password must differ from current password.',
    mismatch: 'New passwords do not match.',
    errorTitle: 'Password Change Failed',
    errorMsg: 'Please verify all required fields and try again.',
    successTitle: 'Password Changed Successfully',
    successMsg: 'Your password has been updated and prior sessions revoked. Please sign in again.',
  },
} as const;

export function ChangePasswordView({
  lang,
  userEmail,
  onBack,
  onPasswordChangedSuccess,
}: ChangePasswordViewProps) {
  const [currentPassword, setCurrentPassword] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [modal, setModal] = useState<{
    variant: 'success' | 'error';
    title: string;
    message: string;
  } | null>(null);

  const currentRef = useRef<HTMLInputElement | null>(null);
  const newRef = useRef<HTMLInputElement | null>(null);

  const t = UI_STRINGS[lang];
  const copy = COPY[lang];

  const handleSubmit = async () => {
    const nextErrors: Record<string, string> = {};
    if (!currentPassword) nextErrors.currentPassword = copy.requiredCurrent;
    if (newPassword.length < 15) nextErrors.newPassword = copy.minLengthNew;
    else if (newPassword === currentPassword) nextErrors.newPassword = copy.mustDiffer;
    if (newPassword !== confirmPassword) nextErrors.confirmPassword = copy.mismatch;

    if (Object.keys(nextErrors).length > 0) {
      setErrors(nextErrors);
      setModal({
        variant: 'error',
        title: copy.errorTitle,
        message: copy.errorMsg,
      });
      return;
    }

    try {
      await changeOwnPassword(currentPassword, newPassword, lang);
      setCurrentPassword('');
      setNewPassword('');
      setConfirmPassword('');
      setErrors({});
      setModal({
        variant: 'success',
        title: copy.successTitle,
        message: copy.successMsg,
      });
    } catch (err) {
      const apiErr = err instanceof ApiProblemError ? err : null;
      const serverErrors: Record<string, string> = {};
      if (apiErr?.details?.newPassword) {
        const val = apiErr.details.newPassword;
        serverErrors.newPassword = Array.isArray(val) ? val[0] : String(val);
      }
      if (apiErr?.details?.currentPassword) {
        const val = apiErr.details.currentPassword;
        serverErrors.currentPassword = Array.isArray(val) ? val[0] : String(val);
      }
      if (Object.keys(serverErrors).length > 0) {
        setErrors(serverErrors);
      }

      setModal({
        variant: 'error',
        title: apiErr?.title ?? copy.errorTitle,
        message: apiErr?.message ?? copy.errorMsg,
      });
    }
  };

  const handleCloseModal = () => {
    const current = modal;
    setModal(null);
    if (current?.variant === 'success') {
      onPasswordChangedSuccess();
    } else if (errors.currentPassword) {
      currentRef.current?.focus();
    } else if (errors.newPassword) {
      newRef.current?.focus();
    }
  };

  return (
    <>
      <StandaloneFormPage
        modeBadgeLabel={copy.badge}
        formTitle={copy.title}
        formSubtitle={`${copy.subtitle} (${userEmail})`}
        backButtonLabel={copy.backBtn}
        submitButtonLabel={copy.submitBtn}
        cancelButtonLabel={copy.cancelBtn}
        onBack={onBack}
        onCancel={onBack}
        onSubmit={() => {
          void handleSubmit();
        }}
      >
        <div className="form-grid-2">
          <FormField
            id="cp-current"
            label={copy.currentLabel}
            required
            error={errors.currentPassword}
          >
            <TextInput
              ref={currentRef}
              id="cp-current"
              type="password"
              autoComplete="current-password"
              ltrCode
              value={currentPassword}
              hasError={Boolean(errors.currentPassword)}
              onChange={(e) => {
                setCurrentPassword(e.target.value);
                setErrors((prev) => ({ ...prev, currentPassword: '' }));
              }}
            />
          </FormField>

          <FormField
            id="cp-new"
            label={copy.newLabel}
            required
            hint={copy.newHint}
            error={errors.newPassword}
          >
            <TextInput
              ref={newRef}
              id="cp-new"
              type="password"
              autoComplete="new-password"
              ltrCode
              value={newPassword}
              hasError={Boolean(errors.newPassword)}
              onChange={(e) => {
                setNewPassword(e.target.value);
                setErrors((prev) => ({ ...prev, newPassword: '' }));
              }}
            />
          </FormField>

          <FormField
            id="cp-confirm"
            label={copy.confirmLabel}
            required
            error={errors.confirmPassword}
          >
            <TextInput
              id="cp-confirm"
              type="password"
              autoComplete="new-password"
              ltrCode
              value={confirmPassword}
              hasError={Boolean(errors.confirmPassword)}
              onChange={(e) => {
                setConfirmPassword(e.target.value);
                setErrors((prev) => ({ ...prev, confirmPassword: '' }));
              }}
            />
          </FormField>
        </div>
      </StandaloneFormPage>

      <CentralModalDialog
        isOpen={modal !== null}
        variant={modal?.variant ?? 'info'}
        title={modal?.title ?? ''}
        message={modal?.message ?? ''}
        confirmLabel={t.modalConfirmBtn}
        closeAriaLabel={t.modalCloseAria}
        onConfirm={handleCloseModal}
      />
    </>
  );
}
