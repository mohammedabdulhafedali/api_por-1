import { FormEvent, useRef, useState } from 'react';
import {
  ApiProblemError,
  AuthenticatedUser,
  loginWithLocalIdentity,
} from '../../api/authApi';
import { AppLanguage, AppTheme, UI_STRINGS } from '../../constants/uiBaseline';
import { CentralModalDialog, ReferenceIcon } from '../ui';

interface ModalState {
  variant: 'success' | 'error';
  title: string;
  message: string;
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
    pageTitle: 'تسجيل الدخول',
    usernameLabel: 'اسم المستخدم أو البريد الإلكتروني',
    usernamePlaceholder: '',
    passwordLabel: 'كلمة المرور',
    passwordPlaceholder: '',
    showPasswordAria: 'إظهار كلمة المرور',
    hidePasswordAria: 'إخفاء كلمة المرور',
    signInBtn: 'تسجيل الدخول',
    signingInBtn: 'جارٍ التحقق...',
    requiredUsernameError: 'يرجى إدخال اسم المستخدم أو البريد الإلكتروني.',
    requiredPasswordError: 'يرجى إدخال كلمة المرور.',
    validationTitle: 'تعذر تسجيل الدخول',
    validationMessage: 'يرجى إدخال اسم المستخدم وكلمة المرور.',
    serviceUnavailableTitle: 'تعذر تسجيل الدخول حاليًا',
    serviceUnavailableMessage:
      'الخدمة غير متاحة مؤقتًا أو تعذر الاتصال. يرجى المحاولة مرة أخرى بعد قليل.',
  },
  en: {
    pageTitle: 'Sign In',
    usernameLabel: 'Username or Email',
    usernamePlaceholder: '',
    passwordLabel: 'Password',
    passwordPlaceholder: '',
    showPasswordAria: 'Show password',
    hidePasswordAria: 'Hide password',
    signInBtn: 'Sign In',
    signingInBtn: 'Verifying...',
    requiredUsernameError: 'Username or email is required.',
    requiredPasswordError: 'Password is required.',
    validationTitle: 'Sign In Error',
    validationMessage: 'Please enter your username and password.',
    serviceUnavailableTitle: 'Unable to Sign In Right Now',
    serviceUnavailableMessage:
      'The service is temporarily unavailable or could not be reached. Please try again shortly.',
  },
} as const;

export function LoginPage({
  lang,
  theme,
  onChangeLanguage,
  onToggleTheme,
  onAuthenticated,
}: LoginPageProps) {
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [showPassword, setShowPassword] = useState(false);

  const [isSubmitting, setIsSubmitting] = useState(false);
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
  const [modalState, setModalState] = useState<ModalState | null>(null);

  const usernameRef = useRef<HTMLInputElement | null>(null);
  const passwordRef = useRef<HTMLInputElement | null>(null);

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
      if (fieldErrors.username) usernameRef.current?.focus();
      else if (fieldErrors.password) passwordRef.current?.focus();
      else usernameRef.current?.focus();
    }
  };

  const handleLoginSubmit = async (e: FormEvent) => {
    e.preventDefault();
    const errors: Record<string, string> = {};
    if (!username.trim()) errors.username = copy.requiredUsernameError;
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
      const authenticatedUser = await loginWithLocalIdentity(username, password, lang);
      setPassword('');
      setFieldErrors({});
      onAuthenticated(authenticatedUser);
    } catch (err) {
      const apiErr = err instanceof ApiProblemError ? err : null;
      const isServerOrNetworkFailure = !apiErr || apiErr.status >= 500;
      setModalState({
        variant: 'error',
        title: isServerOrNetworkFailure
          ? copy.serviceUnavailableTitle
          : apiErr.title || copy.validationTitle,
        message: isServerOrNetworkFailure
          ? copy.serviceUnavailableMessage
          : apiErr.message || copy.validationMessage,
      });
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <div className="auth-page-shell">
      <header className="auth-floating-controls">
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
            className="theme-toggle-btn icon-btn theme-switch"
            aria-label={t.themeToggleLabel}
            title={theme === 'light' ? t.themeDark : t.themeLight}
            onClick={onToggleTheme}
          >
            <ReferenceIcon name={theme === 'light' ? 'moon' : 'sun'} size={20} />
            <span className="sr-only">{theme === 'light' ? t.themeDark : t.themeLight}</span>
          </button>
        </div>
      </header>

      <main className="auth-card-container">
        <section className="auth-card auth-card-emerald" aria-labelledby="ui24-auth-heading">
          <h1 id="ui24-auth-heading" className="sr-only">
            {copy.pageTitle}
          </h1>

          <div className="login-logo login-logo-emerald" aria-hidden="true">
            <ReferenceIcon name="lock" size={26} />
          </div>

          <form className="auth-form-stack" onSubmit={handleLoginSubmit} noValidate>
            <div className="form-field">
              <label htmlFor="login-username" className="auth-field-label">
                {copy.usernameLabel}
              </label>
              <div className="auth-input-icon-box">
                <span className="auth-field-lead-icon" aria-hidden="true">
                  <ReferenceIcon name="user" size={17} />
                </span>
                <input
                  ref={usernameRef}
                  id="login-username"
                  type="text"
                  autoComplete="username"
                  className={`auth-pill-input ${fieldErrors.username ? 'input-error' : ''}`}
                  value={username}
                  placeholder={copy.usernamePlaceholder}
                  onChange={(e) => {
                    setUsername(e.target.value);
                    clearError('username');
                  }}
                />
              </div>
              {fieldErrors.username && (
                <div className="field-error" role="alert">
                  {fieldErrors.username}
                </div>
              )}
            </div>

            <div className="form-field">
              <label htmlFor="login-password" className="auth-field-label">
                {copy.passwordLabel}
              </label>
              <div className="auth-input-icon-box">
                <span className="auth-field-lead-icon" aria-hidden="true">
                  <ReferenceIcon name="lock" size={17} />
                </span>
                <input
                  ref={passwordRef}
                  id="login-password"
                  type={showPassword ? 'text' : 'password'}
                  autoComplete="current-password"
                  className={`auth-pill-input auth-pill-input-password ${fieldErrors.password ? 'input-error' : ''}`}
                  value={password}
                  placeholder={copy.passwordPlaceholder}
                  onChange={(e) => {
                    setPassword(e.target.value);
                    clearError('password');
                  }}
                />
                <button
                  type="button"
                  className="auth-field-trail-btn"
                  aria-label={showPassword ? copy.hidePasswordAria : copy.showPasswordAria}
                  title={showPassword ? copy.hidePasswordAria : copy.showPasswordAria}
                  onClick={() => setShowPassword((prev) => !prev)}
                >
                  <ReferenceIcon name={showPassword ? 'eye-off' : 'eye'} size={18} />
                </button>
              </div>
              {fieldErrors.password && (
                <div className="field-error" role="alert">
                  {fieldErrors.password}
                </div>
              )}
            </div>

            <button
              type="submit"
              className="auth-submit-emerald-btn"
              disabled={isSubmitting}
            >
              {isSubmitting ? copy.signingInBtn : copy.signInBtn}
            </button>
          </form>
        </section>
      </main>

      <CentralModalDialog
        isOpen={modalState !== null}
        variant={modalState?.variant ?? 'info'}
        title={modalState?.title ?? ''}
        message={modalState?.message ?? ''}
        confirmLabel={t.modalConfirmBtn}
        closeAriaLabel={t.modalCloseAria}
        onConfirm={handleCloseModal}
      />
    </div>
  );
}
