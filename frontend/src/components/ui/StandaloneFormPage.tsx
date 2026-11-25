import { FormEvent, ReactNode } from 'react';
import { ReferenceIcon } from './AppLayoutShell';
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
 * Reconstructed directly from viewEditor() in UX_reference_only(2).html:
 * <div class="editor-page">
 *   <div class="editor-toolbar"><button class="editor-back">...</button></div>
 *   <section class="editor-card">
 *     <div class="editor-card-top"><b>...</b><span class="tag">...</span></div>
 *     <div class="editor-card-content">...</div>
 *     <div class="editor-card-actions">...</div>
 *   </section>
 * </div>
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

  const cleanTagLabel = modeBadgeLabel
    .replace(/·?\s*UI-\d+[A-Z]?/g, '')
    .replace(/·?\s*[A-Z]\d{2}/g, '')
    .trim();

  return (
    <section className="standalone-editor-page editor-page" aria-labelledby="standalone-form-heading">
      <div className="editor-top-bar editor-toolbar">
        <button type="button" className="editor-back" onClick={onBack}>
          <ReferenceIcon name="right" size={15} />
          <span>{backButtonLabel}</span>
        </button>
        <span className="sr-only">
          <StatusBadge variant="info">{modeBadgeLabel}</StatusBadge>
        </span>
      </div>

      <form className="surface-card editor-form-card editor-card" onSubmit={handleFormSubmit} noValidate>
        <div className="editor-card-header editor-card-top">
          <div>
            <h2 id="standalone-form-heading" className="card-title heading-font">
              <b>{formTitle}</b>
            </h2>
            {formSubtitle && <p className="card-subtitle">{formSubtitle}</p>}
          </div>
          {cleanTagLabel && <span className="tag">{cleanTagLabel}</span>}
        </div>

        <div className="editor-card-body editor-card-content">{children}</div>

        <div className="editor-card-footer editor-card-actions">
          <Button type="button" variant="outline" onClick={onCancel}>
            {cancelButtonLabel}
          </Button>
          <Button type="submit" variant="primary" isLoading={isSubmitting}>
            {submitButtonLabel}
          </Button>
        </div>
      </form>
    </section>
  );
}

