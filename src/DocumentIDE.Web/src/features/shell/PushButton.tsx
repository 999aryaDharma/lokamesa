import React from 'react';

/**
 * UI-01 primitive — push button (secondary / primary / ghost / danger).
 *
 * The macOS screens use two obvious button weights: a white bordered
 * *secondary* push button and a filled accent *primary* one. This is the single
 * implementation reused by every sheet footer, inspector action and commit bar.
 *
 * It is deliberately a thin `button type="button"` wrapper: it carries no
 * behaviour beyond the caller's `onClick`/`disabled`, so swapping a raw
 * `<button>` for this primitive can never change what a click does.
 */
export type PushButtonVariant = 'primary' | 'secondary' | 'ghost' | 'danger';
export type PushButtonSize = 'sm' | 'md';

export interface PushButtonProps
  extends Omit<React.ButtonHTMLAttributes<HTMLButtonElement>, 'className'> {
  variant?: PushButtonVariant;
  size?: PushButtonSize;
  className?: string;
}

const VARIANT_CLASSES: Record<PushButtonVariant, string> = {
  primary:
    'bg-mac-accent text-white border border-mac-accent hover:brightness-105',
  secondary:
    'bg-mac-content text-mac-text border border-mac-hairline-strong hover:bg-mac-toolbar',
  ghost:
    'bg-transparent text-mac-text-2 border border-transparent hover:bg-mac-chip',
  danger:
    'bg-mac-content text-mac-danger border border-mac-hairline-strong hover:bg-mac-toolbar',
};

const SIZE_CLASSES: Record<PushButtonSize, string> = {
  sm: 'px-2 py-0.5 text-[11px]',
  md: 'px-2.5 py-1 text-xs',
};

export const PushButton: React.FC<PushButtonProps> = ({
  variant = 'secondary',
  size = 'md',
  className = '',
  type = 'button',
  ...rest
}) => (
  <button
    type={type}
    className={[
      'inline-flex items-center justify-center gap-1 rounded-md font-medium',
      'transition-colors cursor-pointer disabled:opacity-40 disabled:cursor-not-allowed',
      VARIANT_CLASSES[variant],
      SIZE_CLASSES[size],
      className,
    ]
      .filter(Boolean)
      .join(' ')}
    {...rest}
  />
);

export default PushButton;
