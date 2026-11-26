import { FormEvent, ReactNode } from 'react';
import { ReferenceIcon } from './AppLayoutShell';
import { Button } from './FormControls';

export interface BackIconButtonProps {
  label: string;
  onClick: () => void;
}

/**
 * Compact icon-only back button used consistently across all secondary views and standalone forms.
 */
export function BackIconButton({ label, onClick }: BackIconButtonProps) {
  return (
    <button
      type="button"
      className="editor-back"
      onClick={onClick}
      aria-label={label}
      title={label}
    >
      <ReferenceIcon name="right" size={16} />
    </button>
  );
}

export interface RowActionButtonProps {
  icon: string;
  label: string;
  onClick: () => void;
  variant?: 'default' | 'danger' | 'warning' | 'success';
  disabled?: boolean;
}

/**
 * Compact icon-only action button for table rows with accessible label and tooltip.
 */
export function RowActionButton({
  icon,
  label,
  onClick,
  variant = 'default',
  disabled = false,
}: RowActionButtonProps) {
  return (
    <button
      type="button"
      className={`action-icon-btn action-icon-btn--${variant}`}
      onClick={onClick}
      aria-label={label}
      title={label}
      disabled={disabled}
    >
      <ReferenceIcon name={icon} size={15} />
    </button>
  );
}

export interface StandaloneFormPageProps {
  modeBadgeLabel?: string;
  formTitle: string;
  formSubtitle?: string;
  backButtonLabel: string;
  submitButtonLabel: string;
  cancelButtonLabel: string;
  isSubmitting?: boolean;
  submitDisabled?: boolean;
  onBack: () => void;
  onCancel: () => void;
  onSubmit: () => void;
  children: ReactNode;
}

/**
 * Reusable compact full-page layout for Create and Edit forms.
 */
export function StandaloneFormPage({
  formTitle,
  backButtonLabel,
  submitButtonLabel,
  cancelButtonLabel,
  isSubmitting = false,
  submitDisabled = false,
  onBack,
  onCancel,
  onSubmit,
  children,
}: StandaloneFormPageProps) {
  const handleFormSubmit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    onSubmit();
  };

  return (
    <section className="standalone-editor-page editor-page" aria-labelledby="standalone-form-heading">
      <form className="surface-card editor-form-card editor-card" onSubmit={handleFormSubmit} noValidate>
        <div className="editor-card-header editor-card-top">
          <div className="editor-header-main">
            <BackIconButton label={backButtonLabel} onClick={onBack} />
            <h2 id="standalone-form-heading" className="card-title heading-font">
              <b>{formTitle}</b>
            </h2>
          </div>
        </div>

        <div className="editor-card-body editor-card-content">{children}</div>

        <div className="editor-card-footer editor-card-actions">
          <Button type="button" variant="outline" onClick={onCancel}>
            {cancelButtonLabel}
          </Button>
          <Button type="submit" variant="primary" isLoading={isSubmitting} disabled={submitDisabled}>
            {submitButtonLabel}
          </Button>
        </div>
      </form>
    </section>
  );
}

