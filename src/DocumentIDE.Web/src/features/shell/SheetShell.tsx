import React from 'react';

/**
 * UI-01 primitive — sheet/scrim shell.
 *
 * The reference series renders every modal piece of work (Batch operations,
 * create worktree, the six dialogs on screen 12) as a centred sheet over a
 * scrim: header with title + `✕`, scrolling body, footer with secondary and
 * primary actions.
 *
 * This primitive owns ONLY that chrome. It never renders or manages a portal,
 * focus trap or Escape handler on its own unless asked (`onClose` adds the `✕`
 * and a click-on-scrim close); callers keep their existing behaviour.
 */
export interface SheetShellProps {
  title: string;
  onClose?: () => void;
  children?: React.ReactNode;
  /** Footer actions (secondary/primary push buttons). */
  footer?: React.ReactNode;
  /** Optional status line under the header (e.g. "Preview ready · 12 files"). */
  status?: React.ReactNode;
  widthClass?: string;
  className?: string;
  testId?: string;
  /**
   * Render over the full-screen scrim (default — the reference's modal idiom).
   *
   * Set `false` for a surface that is **not modal today** (the merge-order sheet
   * lives inline inside the mounted PDF panel): the same sheet *panel* chrome is
   * reused, but without the scrim and without `aria-modal`, because turning an
   * inline surface into a modal one is a behaviour change.
   */
  scrim?: boolean;
}

export const SheetShell: React.FC<SheetShellProps> = ({
  title,
  onClose,
  children,
  footer,
  status,
  widthClass = 'max-w-[720px]',
  className = '',
  testId,
  scrim = true,
}) => {
  const panel = (
    <div
      role="dialog"
      aria-modal={scrim ? 'true' : undefined}
      aria-label={title}
      data-testid={testId}
      className={[
        'flex max-h-full w-full flex-col overflow-hidden rounded-xl border border-mac-hairline-strong',
        'bg-mac-content shadow-2xl',
        widthClass,
        className,
      ]
        .filter(Boolean)
        .join(' ')}
    >
      <header className="flex items-center justify-between border-b border-mac-hairline px-4 py-2.5">
        <h2 className="text-[13px] font-semibold text-mac-text">{title}</h2>
        {onClose && (
          <button
            type="button"
            aria-label={`Close ${title}`}
            onClick={onClose}
            className="text-mac-text-3 hover:text-mac-text cursor-pointer"
          >
            ✕
          </button>
        )}
      </header>

      {status && (
        <div className="border-b border-mac-hairline px-4 py-2 text-xs text-mac-text-2">{status}</div>
      )}

      <div className="flex-1 overflow-y-auto px-4 py-3 text-xs text-mac-text">{children}</div>

      {footer && (
        <footer className="flex items-center gap-2 border-t border-mac-hairline px-4 py-2.5">
          {footer}
        </footer>
      )}
    </div>
  );

  if (!scrim) return panel;

  return (
    <div
      data-testid="sheet-scrim"
      className="absolute inset-0 z-40 flex items-center justify-center bg-black/20 p-6"
      onClick={onClose ? (event) => event.target === event.currentTarget && onClose() : undefined}
    >
      {panel}
    </div>
  );
};

export default SheetShell;
