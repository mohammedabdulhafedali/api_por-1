import { FormEvent, ReactNode } from 'react';
import { Button } from './FormControls';
import { StatusBadge } from './StateViews';

export interface StandaloneFormPageProps {
  modeBadgeLabel: string;
  formTitle: string;
  formSubtitle: string;
  backButtonLabel: string;
  submitButtonLabel: string;
  cancelButtonLabel: string;
  isSubmitting?: boolean;
  onBack: () => void;
  onCancel: () => void;
  onSubmit: () => void;
  children: ReactNode;
}

/**
 * Reusable standalone full-page layout for Create and Edit forms.
 * Enforces PRD-06 & 02_UX_UI_v1.2.md rule: "Create and Edit are standalone pages inside the application, NOT Modals."
 */
export function StandaloneFormPage({
  modeBadgeLabel,
  formTitle,
  formSubtitle,
  backButtonLabel,
  submitButtonLabel,
  cancelButtonLabel,
  isSubmitting = false,
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
    <section className="standalone-editor-page" aria-labelledby="standalone-form-heading">
      <div className="editor-top-bar">
        <Button variant="outline" size="sm" onClick={onBack}>
          {backButtonLabel}
        </Button>
        <StatusBadge variant="info">{modeBadgeLabel}</StatusBadge>
      </div>

      <form className="surface-card editor-form-card" onSubmit={handleFormSubmit} noValidate>
        <div className="editor-card-header">
          <div>
            <h2 id="standalone-form-heading" className="card-title heading-font">
              {formTitle}
            </h2>
            <p className="card-subtitle">{formSubtitle}</p>
          </div>
        </div>

        <div className="editor-card-body">{children}</div>

        <div className="editor-card-footer">
          <Button type="submit" variant="primary" isLoading={isSubmitting}>
            {submitButtonLabel}
          </Button>
          <Button type="button" variant="outline" onClick={onCancel}>
            {cancelButtonLabel}
          </Button>
        </div>
      </form>
    </section>
  );
}

