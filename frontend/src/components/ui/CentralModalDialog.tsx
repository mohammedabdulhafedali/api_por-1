import { useEffect, useRef } from 'react';
import { Button } from './FormControls';

export type CentralModalVariant = 'info' | 'success' | 'warning' | 'error' | 'confirm';

export interface CentralModalDialogProps {
  isOpen: boolean;
  variant: CentralModalVariant;
  title: string;
  message: string;
  confirmLabel: string;
  cancelLabel?: string;
  closeAriaLabel?: string;
  traceId?: string;
  onConfirm: () => void;
  onCancel?: () => void;
}

/**
 * Unified central Modal dialog for system feedback (success, error, warning, info) and confirmations (U03).
 * Per PRD-06 & 02_UX_UI_v1.2.md: System messages use a central Modal (never bottom Toasts),
 * restores focus when closed, and Create/Edit forms remain standalone pages (never Modals).
 */
export function CentralModalDialog({
  isOpen,
  variant,
  title,
  message,
  confirmLabel,
  cancelLabel,
  closeAriaLabel,
  traceId,
  onConfirm,
  onCancel,
}: CentralModalDialogProps) {
  const previousFocusRef = useRef<HTMLElement | null>(null);
  const wasOpenRef = useRef<boolean>(false);

  useEffect(() => {
    if (isOpen && !wasOpenRef.current) {
      previousFocusRef.current = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    } else if (!isOpen && wasOpenRef.current) {
      const target = previousFocusRef.current;
      previousFocusRef.current = null;
      if (target && document.contains(target) && document.activeElement === document.body) {
        target.focus();
      }
    }
    wasOpenRef.current = isOpen;
  }, [isOpen]);

  useEffect(() => {
    if (!isOpen) return;
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        if (onCancel) {
          onCancel();
        } else {
          onConfirm();
        }
      }
    };
    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [isOpen, onCancel, onConfirm]);

  if (!isOpen) {
    return null;
  }

  const iconSymbol =
    variant === 'success'
      ? '✓'
      : variant === 'warning' || variant === 'confirm'
        ? '!'
        : variant === 'error'
          ? '✕'
          : 'i';

  const iconClass = variant === 'confirm' ? 'warning' : variant;

  return (
    <div
      className="modal-backdrop"
      role="dialog"
      aria-modal="true"
      aria-labelledby="central-modal-title"
      aria-describedby="central-modal-message"
    >
      <div className="modal-dialog">
        <div className="modal-header">
          <div className={`modal-status-icon ${iconClass}`} aria-hidden="true">
            {iconSymbol}
          </div>
          <h3 id="central-modal-title" className="modal-title heading-font">
            {title}
          </h3>
        </div>
        <p id="central-modal-message" className="modal-body">
          {message}
        </p>
        {traceId && (
          <div style={{ marginTop: 8, marginBottom: 4 }}>
            <code className="ltr-code" style={{ fontSize: 11.5 }}>
              traceId: {traceId}
            </code>
          </div>
        )}
        <div className="modal-actions">
          {cancelLabel && onCancel && (
            <Button variant="outline" onClick={onCancel}>
              {cancelLabel}
            </Button>
          )}
          <Button
            variant={variant === 'error' ? 'danger' : 'primary'}
            onClick={onConfirm}
            aria-label={closeAriaLabel || confirmLabel}
            autoFocus
          >
            {confirmLabel}
          </Button>
        </div>
      </div>
    </div>
  );
}
