import { FormEvent, useRef, useState } from 'react';
import {
  ApiProblemError,
  AuthenticatedUser,
  completePasswordReset,
  loginWithLocalIdentity,
} from '../../api/authApi';
import { AppLanguage, AppTheme, UI_STRINGS } from '../../constants/uiBaseline';
import { Button, CentralModalDialog, FormField, ReferenceIcon, TextInput } from '../ui';

type AuthScreenMode = 'login' | 'activate-reset';

interface ModalState {
  variant: 'success' | 'error';
  title: string;
  message: string;
  traceId?: string;
}

export interface LoginPageProps {
  lang: AppLanguage;
  theme: AppTheme;
  onChangeLanguage: (lang: AppLanguage) => void;
  onToggleTheme: () => void;
  onAuthenticated: (user: AuthenticatedUser) => void;
}

const AUTH_COPY = {
  ar: {
    pageTitle: 'تسجيل الدخول إلى المنصة',
    pageSubtitle:
      'بوابة دخول موحدة للمستخدمين الداخليين والخارجيين عبر ASP.NET Core Identity. لا يتاح التسجيل الذاتي في هذه المنصة.',
    tabLogin: 'تسجيل الدخول',
    tabActivateReset: 'تفعيل الحساب / تعيين كلمة المرور',
    emailLabel: 'البريد الإلكتروني',
    emailPlaceholder: 'name@company.local',
    passwordLabel: 'كلمة المرور',
    passwordPlaceholder: 'أدخل كلمة المرور',
    showPassword: 'إظهار',
    hidePassword: 'إخفاء',
    signInBtn: 'دخول إلى المنصة',
    signingInBtn: 'جارٍ التحقق...',
    tokenLabel: 'رمز التفعيل أو الاستعادة أحادي الاستخدام',
    tokenPlaceholder: 'ألصق الرمز المستلم من قناة التسليم المعتمدة',
    newPasswordLabel: 'كلمة المرور الجديدة',
    newPasswordHint:
      '15 خانة على الأقل تشمل حرفًا كبيرًا وحرفًا صغيرًا ورقمًا ورمزًا خاصًا (دون تكرار ضعيف).',
    confirmPasswordLabel: 'تأكيد كلمة المرور الجديدة',
    completeResetBtn: 'تفعيل / تعيين كلمة المرور',
    completingResetBtn: 'جارٍ الحفظ...',
    deferredNotice:
      'تنبيه التفعيل: تُنشأ الحسابات بواسطة مدير النظام، وتُسلّم رموز التفعيل وإعادة التعيين عبر القناة المؤسسية المعتمدة عند ربطها. لا توجد خاصية تسجيل ذاتي.',
    requiredEmailError: 'يرجى إدخال البريد الإلكتروني.',
    requiredPasswordError: 'يرجى إدخال كلمة المرور.',
    requiredTokenError: 'يرجى إدخال رمز التفعيل أو إعادة التعيين.',
    passwordLengthError: 'يجب ألا تقل كلمة المرور عن 15 حرفًا.',
    passwordMismatchError: 'كلمتا المرور غير متطابقتين.',
    validationTitle: 'تعذر إرسال النموذج',
    validationMessage: 'يرجى استكمال الحقول الإلزامية الصحيحة قبل المتابعة.',
    resetSuccessTitle: 'تم تحديث كلمة المرور وتفعيل الحساب',
    resetSuccessMessage:
      'تم تعيين كلمة المرور الجديدة بنجاح وإبطال الجلسات السابقة. يمكنك الآن تسجيل الدخول باستخدام كلمة المرور الجديدة.',
    footerText: 'UI-24 · مصادقة محلية آمنة (Cookie HttpOnly + CSRF + سياسة قفل وحماية من التكرار)',
  },
  en: {
    pageTitle: 'Sign In to Platform',
    pageSubtitle:
      'Unified sign-in portal for internal and external users via ASP.NET Core Identity. Self-registration is disabled.',
    tabLogin: 'Sign In',
    tabActivateReset: 'Activate Account / Reset Password',
    emailLabel: 'Email Address',
    emailPlaceholder: 'name@company.local',
    passwordLabel: 'Password',
    passwordPlaceholder: 'Enter your password',
    showPassword: 'Show',
    hidePassword: 'Hide',
    signInBtn: 'Sign In',
    signingInBtn: 'Verifying...',
    tokenLabel: 'Single-Use Activation / Reset Token',
    tokenPlaceholder: 'Paste the token delivered via the approved institutional channel',
    newPasswordLabel: 'New Password',
    newPasswordHint:
      'Minimum 15 characters with uppercase, lowercase, digit, and special character.',
    confirmPasswordLabel: 'Confirm New Password',
    completeResetBtn: 'Activate / Set New Password',
    completingResetBtn: 'Saving...',
    deferredNotice:
      'Account Activation Notice: Accounts are provisioned by administrators, and activation/reset tokens are delivered via the approved enterprise channel once connected. Self-registration is disabled.',
    requiredEmailError: 'Email address is required.',
    requiredPasswordError: 'Password is required.',
    requiredTokenError: 'Activation or reset token is required.',
    passwordLengthError: 'New password must be at least 15 characters.',
    passwordMismatchError: 'Passwords do not match.',
    validationTitle: 'Form Validation Error',
    validationMessage: 'Please complete all required fields accurately before submitting.',
    resetSuccessTitle: 'Account Activated & Password Updated',
    resetSuccessMessage:
      'Your new password has been set and prior sessions have been invalidated. You may now sign in with your new password.',
    footerText: 'UI-24 · Local Identity Security (HttpOnly Cookie + CSRF + Lockout & Rate Limiting)',
  },
} as const;

export function LoginPage({
  lang,
  theme,
  onChangeLanguage,
  onToggleTheme,
  onAuthenticated,
}: LoginPageProps) {
  const [mode, setMode] = useState<AuthScreenMode>('login');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [showPassword, setShowPassword] = useState(false);

  const [resetToken, setResetToken] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');

  const [isSubmitting, setIsSubmitting] = useState(false);
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
  const [modalState, setModalState] = useState<ModalState | null>(null);

  const emailRef = useRef<HTMLInputElement | null>(null);
  const passwordRef = useRef<HTMLInputElement | null>(null);
  const tokenRef = useRef<HTMLInputElement | null>(null);
  const newPasswordRef = useRef<HTMLInputElement | null>(null);

  const t = UI_STRINGS[lang];
  const copy = AUTH_COPY[lang];

  const clearError = (field: string) => {
    setFieldErrors((prev) => {
      if (!prev[field]) return prev;
      const next = { ...prev };
      delete next[field];
      return next;
    });
  };

  const handleCloseModal = () => {
    const current = modalState;
    setModalState(null);
    if (current?.variant === 'error') {
      if (fieldErrors.email) emailRef.current?.focus();
      else if (fieldErrors.password) passwordRef.current?.focus();
      else if (fieldErrors.token) tokenRef.current?.focus();
      else if (fieldErrors.newPassword) newPasswordRef.current?.focus();
      else emailRef.current?.focus();
    }
  };

  const handleLoginSubmit = async (e: FormEvent) => {
    e.preventDefault();
    const errors: Record<string, string> = {};
    if (!email.trim()) errors.email = copy.requiredEmailError;
    if (!password) errors.password = copy.requiredPasswordError;

    if (Object.keys(errors).length > 0) {
      setFieldErrors(errors);
      setModalState({
        variant: 'error',
        title: copy.validationTitle,
        message: copy.validationMessage,
      });
      return;
    }

    setIsSubmitting(true);
    try {
      const authenticatedUser = await loginWithLocalIdentity(email, password, lang);
      setPassword('');
      setFieldErrors({});
      onAuthenticated(authenticatedUser);
    } catch (err) {
      const apiErr = err instanceof ApiProblemError ? err : null;
      setModalState({
        variant: 'error',
        title: apiErr?.title ?? copy.validationTitle,
        message: apiErr?.message ?? copy.validationMessage,
        traceId: apiErr?.traceId,
      });
    } finally {
      setIsSubmitting(false);
    }
  };

  const handleCompleteResetSubmit = async (e: FormEvent) => {
    e.preventDefault();
    const errors: Record<string, string> = {};
    if (!email.trim()) errors.email = copy.requiredEmailError;
    if (!resetToken.trim()) errors.token = copy.requiredTokenError;
    if (newPassword.length < 15) errors.newPassword = copy.passwordLengthError;
    if (newPassword !== confirmPassword) errors.confirmPassword = copy.passwordMismatchError;

    if (Object.keys(errors).length > 0) {
      setFieldErrors(errors);
      setModalState({
        variant: 'error',
        title: copy.validationTitle,
        message: copy.validationMessage,
      });
      return;
    }

    setIsSubmitting(true);
    try {
      await completePasswordReset(email, resetToken, newPassword, lang);
      setResetToken('');
      setNewPassword('');
      setConfirmPassword('');
      setFieldErrors({});
      setMode('login');
      setModalState({
        variant: 'success',
        title: copy.resetSuccessTitle,
        message: copy.resetSuccessMessage,
      });
    } catch (err) {
      const apiErr = err instanceof ApiProblemError ? err : null;
      const serverFieldErrors: Record<string, string> = {};
      if (apiErr?.details && typeof apiErr.details.newPassword !== 'undefined') {
        const msgs = apiErr.details.newPassword;
        serverFieldErrors.newPassword = Array.isArray(msgs) ? msgs[0] : String(msgs);
      }
      if (Object.keys(serverFieldErrors).length > 0) {
        setFieldErrors(serverFieldErrors);
      }

      setModalState({
        variant: 'error',
        title: apiErr?.title ?? copy.validationTitle,
        message: apiErr?.message ?? copy.validationMessage,
        traceId: apiErr?.traceId,
      });
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <div className="auth-page-shell">
      <header className="auth-topbar">
        <div className="brand-header" style={{ borderBottom: 'none', marginBottom: 0, padding: 0 }}>
          <div className="brand-mark" aria-hidden="true">
            <ReferenceIcon name="network" size={22} />
          </div>
          <div>
            <div className="brand-title heading-font">{t.brandTitle}</div>
            <div className="brand-subtitle">{t.brandSubtitle}</div>
          </div>
        </div>

        <div className="topbar-actions">
          <div className="lang-switch-group" role="group" aria-label={t.langToggleLabel}>
            <button
              type="button"
              className={`lang-switch-btn ${lang === 'ar' ? 'active' : ''}`}
              onClick={() => onChangeLanguage('ar')}
              aria-pressed={lang === 'ar'}
            >
              AR
            </button>
            <button
              type="button"
              className={`lang-switch-btn ${lang === 'en' ? 'active' : ''}`}
              onClick={() => onChangeLanguage('en')}
              aria-pressed={lang === 'en'}
            >
              EN
            </button>
          </div>

          <button
            type="button"
            className="theme-toggle-btn"
            aria-label={t.themeToggleLabel}
            onClick={onToggleTheme}
          >
            <span aria-hidden="true">{theme === 'light' ? '🌙' : '☀️'}</span>
            <span>{theme === 'light' ? t.themeDark : t.themeLight}</span>
          </button>
        </div>
      </header>

      <main className="auth-card-container">
        <section className="auth-card" aria-labelledby="ui24-auth-heading">
          <div className="login-logo" aria-hidden="true">
            <ReferenceIcon name="network" size={29} />
          </div>
          <h1 id="ui24-auth-heading" className="auth-heading heading-font" style={{ textAlign: 'center' }}>
            {copy.pageTitle}
          </h1>
          <p className="auth-subtitle" style={{ textAlign: 'center' }}>
            {copy.pageSubtitle}
          </p>

          <div className="auth-tabs" role="tablist" aria-label={copy.pageTitle}>
            <button
              type="button"
              role="tab"
              aria-selected={mode === 'login'}
              className={`auth-tab-btn ${mode === 'login' ? 'active' : ''}`}
              onClick={() => {
                setMode('login');
                setFieldErrors({});
              }}
            >
              {copy.tabLogin}
            </button>
            <button
              type="button"
              role="tab"
              aria-selected={mode === 'activate-reset'}
              className={`auth-tab-btn ${mode === 'activate-reset' ? 'active' : ''}`}
              onClick={() => {
                setMode('activate-reset');
                setFieldErrors({});
              }}
            >
              {copy.tabActivateReset}
            </button>
          </div>

          {mode === 'login' ? (
            <form className="auth-form-stack" onSubmit={handleLoginSubmit} noValidate>
              <FormField id="login-email" label={copy.emailLabel} required error={fieldErrors.email}>
                <TextInput
                  ref={emailRef}
                  id="login-email"
                  type="email"
                  autoComplete="email"
                  ltrCode
                  value={email}
                  hasError={Boolean(fieldErrors.email)}
                  placeholder={copy.emailPlaceholder}
                  onChange={(e) => {
                    setEmail(e.target.value);
                    clearError('email');
                  }}
                />
              </FormField>

              <FormField
                id="login-password"
                label={copy.passwordLabel}
                required
                error={fieldErrors.password}
              >
                <div className="auth-password-row">
                  <TextInput
                    ref={passwordRef}
                    id="login-password"
                    type={showPassword ? 'text' : 'password'}
                    autoComplete="current-password"
                    ltrCode
                    value={password}
                    hasError={Boolean(fieldErrors.password)}
                    placeholder={copy.passwordPlaceholder}
                    onChange={(e) => {
                      setPassword(e.target.value);
                      clearError('password');
                    }}
                  />
                  <Button
                    variant="outline"
                    type="button"
                    onClick={() => setShowPassword((prev) => !prev)}
                  >
                    {showPassword ? copy.hidePassword : copy.showPassword}
                  </Button>
                </div>
              </FormField>

              <Button variant="primary" type="submit" disabled={isSubmitting}>
                {isSubmitting ? copy.signingInBtn : copy.signInBtn}
              </Button>
            </form>
          ) : (
            <form className="auth-form-stack" onSubmit={handleCompleteResetSubmit} noValidate>
              <FormField id="reset-email" label={copy.emailLabel} required error={fieldErrors.email}>
                <TextInput
                  ref={emailRef}
                  id="reset-email"
                  type="email"
                  autoComplete="email"
                  ltrCode
                  value={email}
                  hasError={Boolean(fieldErrors.email)}
                  placeholder={copy.emailPlaceholder}
                  onChange={(e) => {
                    setEmail(e.target.value);
                    clearError('email');
                  }}
                />
              </FormField>

              <FormField
                id="reset-token"
                label={copy.tokenLabel}
                required
                error={fieldErrors.token}
              >
                <TextInput
                  ref={tokenRef}
                  id="reset-token"
                  type="text"
                  autoComplete="one-time-code"
                  ltrCode
                  value={resetToken}
                  hasError={Boolean(fieldErrors.token)}
                  placeholder={copy.tokenPlaceholder}
                  onChange={(e) => {
                    setResetToken(e.target.value);
                    clearError('token');
                  }}
                />
              </FormField>

              <FormField
                id="reset-new-password"
                label={copy.newPasswordLabel}
                required
                hint={copy.newPasswordHint}
                error={fieldErrors.newPassword}
              >
                <TextInput
                  ref={newPasswordRef}
                  id="reset-new-password"
                  type="password"
                  autoComplete="new-password"
                  ltrCode
                  value={newPassword}
                  hasError={Boolean(fieldErrors.newPassword)}
                  onChange={(e) => {
                    setNewPassword(e.target.value);
                    clearError('newPassword');
                  }}
                />
              </FormField>

              <FormField
                id="reset-confirm-password"
                label={copy.confirmPasswordLabel}
                required
                error={fieldErrors.confirmPassword}
              >
                <TextInput
                  id="reset-confirm-password"
                  type="password"
                  autoComplete="new-password"
                  ltrCode
                  value={confirmPassword}
                  hasError={Boolean(fieldErrors.confirmPassword)}
                  onChange={(e) => {
                    setConfirmPassword(e.target.value);
                    clearError('confirmPassword');
                  }}
                />
              </FormField>

              <Button variant="primary" type="submit" disabled={isSubmitting}>
                {isSubmitting ? copy.completingResetBtn : copy.completeResetBtn}
              </Button>
            </form>
          )}

          <div className="auth-notice-box" style={{ marginTop: 18 }}>
            {copy.deferredNotice}
          </div>
        </section>
      </main>

      <footer className="auth-footer-note">{copy.footerText}</footer>

      <CentralModalDialog
        isOpen={modalState !== null}
        variant={modalState?.variant ?? 'info'}
        title={modalState?.title ?? ''}
        message={
          modalState
            ? modalState.traceId
              ? `${modalState.message} (${modalState.traceId})`
              : modalState.message
            : ''
        }
        confirmLabel={t.modalConfirmBtn}
        closeAriaLabel={t.modalCloseAria}
        onConfirm={handleCloseModal}
      />
    </div>
  );
}
