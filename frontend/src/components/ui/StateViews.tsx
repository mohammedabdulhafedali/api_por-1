import { ReactNode } from 'react';
import { Button } from './FormControls';

export type BadgeVariant = 'neutral' | 'success' | 'warning' | 'danger' | 'info';

export interface StatusBadgeProps {
  variant?: BadgeVariant;
  dot?: boolean;
  children: ReactNode;
}

export function StatusBadge({ variant = 'neutral', dot = false, children }: StatusBadgeProps) {
  return (
    <span className={`status-tag tag-${variant} ${dot ? 'tag-dot' : ''}`.trim()}>
      {children}
    </span>
  );
}

export interface LoadingStateProps {
  title: string;
  description?: string;
}

export function LoadingState({ title, description }: LoadingStateProps) {
  return (
    <div className="state-box state-loading" role="status" aria-live="polite">
      <div className="state-spinner" aria-hidden="true" />
      <div className="state-title heading-font">{title}</div>
      {description && <p className="state-desc">{description}</p>}
    </div>
  );
}

export interface EmptyStateProps {
  title: string;
  description?: string;
  actionLabel?: string;
  onAction?: () => void;
}

export function EmptyState({ title, description, actionLabel, onAction }: EmptyStateProps) {
  return (
    <div className="state-box state-empty">
      <div className="state-icon" aria-hidden="true">
        ∅
      </div>
      <div className="state-title heading-font">{title}</div>
      {description && <p className="state-desc">{description}</p>}
      {actionLabel && onAction && (
        <div className="state-action">
          <Button variant="primary" size="sm" onClick={onAction}>
            {actionLabel}
          </Button>
        </div>
      )}
    </div>
  );
}

export interface ErrorStateProps {
  title: string;
  message: string;
  traceId?: string;
  retryLabel?: string;
  onRetry?: () => void;
}

export function ErrorState({ title, message, traceId, retryLabel, onRetry }: ErrorStateProps) {
  return (
    <div className="state-box state-error" role="alert">
      <div className="state-icon error" aria-hidden="true">
        !
      </div>
      <div className="state-title heading-font">{title}</div>
      <p className="state-desc">{message}</p>
      {traceId && (
        <div className="state-trace">
          <span>Trace ID: </span>
          <code className="ltr-code">{traceId}</code>
        </div>
      )}
      {retryLabel && onRetry && (
        <div className="state-action">
          <Button variant="outline" size="sm" onClick={onRetry}>
            {retryLabel}
          </Button>
        </div>
      )}
    </div>
  );
}

