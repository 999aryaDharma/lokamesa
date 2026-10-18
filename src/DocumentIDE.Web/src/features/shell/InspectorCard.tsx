import React from 'react';

/**
 * UI-01 primitives — right-inspector card and its key/value rows.
 *
 * The inspector dock on every reference screen is built from the same pieces:
 * a small uppercase section title, then `key ......... value` rows, then
 * "reference cards" (a glyph, a name, a trailing type chip). `InspectorCard`
 * wraps the section; `InspectorKeyValue`/`InspectorField` are the rows.
 *
 * Purely presentational — no state, no handlers, so a restyle cannot alter
 * behaviour.
 */
export type InspectorTone = 'default' | 'success' | 'warning' | 'danger';

const TONE_CLASSES: Record<InspectorTone, string> = {
  default: 'text-mac-text',
  success: 'text-mac-success',
  warning: 'text-mac-warning',
  danger: 'text-mac-danger',
};

export interface InspectorCardProps {
  title?: string;
  /** Optional trailing header control (e.g. the `⋯` inspector menu). */
  action?: React.ReactNode;
  children?: React.ReactNode;
  className?: string;
  testId?: string;
}

export const InspectorCard: React.FC<InspectorCardProps> = ({
  title,
  action,
  children,
  className = '',
  testId,
}) => (
  <section
    aria-label={title}
    data-testid={testId}
    className={['space-y-1.5', className].filter(Boolean).join(' ')}
  >
    {(title || action) && (
      <header className="flex items-center justify-between">
        {title && (
          <h3 className="text-[10px] font-semibold uppercase tracking-[0.06em] text-mac-text-3">
            {title}
          </h3>
        )}
        {action}
      </header>
    )}
    {children}
  </section>
);

export interface InspectorFieldProps {
  label: string;
  value: React.ReactNode;
  tone?: InspectorTone;
  /** Render the row as a bordered reference card rather than a bare row. */
  card?: boolean;
}

export const InspectorField: React.FC<InspectorFieldProps> = ({
  label,
  value,
  tone = 'default',
  card = false,
}) => (
  <div
    className={[
      'flex items-center justify-between gap-2 text-xs',
      card ? 'rounded-md border border-mac-hairline bg-mac-content px-2 py-1.5' : 'py-0.5',
    ].join(' ')}
  >
    <span className="truncate text-mac-text-2">{label}</span>
    <span className={['shrink-0 font-medium truncate', TONE_CLASSES[tone]].join(' ')}>{value}</span>
  </div>
);

/** Alias kept for the key/value framing used across the design brief. */
export const InspectorKeyValue = InspectorField;

export default InspectorCard;
