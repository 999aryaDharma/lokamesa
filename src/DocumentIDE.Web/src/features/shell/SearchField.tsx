import React, { useCallback, useState } from 'react';

/**
 * UI-01 primitive — toolbar/inspector search field.
 *
 * Reproduces the macOS series' search field: a rounded white control with a
 * leading magnifier glyph. `onSubmit` is the submit-on-enter seam (screen 08
 * drives the search panel only when the user presses Enter); `onChange` is the
 * optional live-filter seam used by the list view's name filter.
 *
 * Uncontrolled by default (keeps `defaultValue`), but accepts `value` for the
 * controlled live-filter case.
 */
export interface SearchFieldProps {
  /** Accessible name; doubles as the placeholder when `placeholder` is absent. */
  ariaLabel: string;
  placeholder?: string;
  value?: string;
  defaultValue?: string;
  onChange?: (value: string) => void;
  /** Called with the trimmed query when the user presses Enter. */
  onSubmit?: (query: string) => void;
  disabled?: boolean;
  className?: string;
  testId?: string;
}

export const SearchField: React.FC<SearchFieldProps> = ({
  ariaLabel,
  placeholder,
  value,
  defaultValue,
  onChange,
  onSubmit,
  disabled = false,
  className = '',
  testId,
}) => {
  const isControlled = value !== undefined;
  const [internal, setInternal] = useState(defaultValue ?? '');
  const current = isControlled ? value : internal;

  const handleChange = useCallback(
    (event: React.ChangeEvent<HTMLInputElement>) => {
      const next = event.target.value;
      if (!isControlled) setInternal(next);
      onChange?.(next);
    },
    [isControlled, onChange],
  );

  const handleKeyDown = useCallback(
    (event: React.KeyboardEvent<HTMLInputElement>) => {
      if (event.key === 'Enter') {
        event.preventDefault();
        onSubmit?.(current.trim());
      }
    },
    [current, onSubmit],
  );

  return (
    <div
      className={[
        'flex items-center gap-1.5 rounded-md border border-mac-hairline-strong',
        'bg-mac-content px-2 py-1 text-xs text-mac-text',
        disabled ? 'opacity-50' : '',
        className,
      ]
        .filter(Boolean)
        .join(' ')}
    >
      <span aria-hidden="true" className="text-mac-text-3 text-[11px] leading-none">
        ⌕
      </span>
      <input
        type="search"
        aria-label={ariaLabel}
        placeholder={placeholder ?? ariaLabel}
        value={current}
        onChange={handleChange}
        onKeyDown={handleKeyDown}
        disabled={disabled}
        data-testid={testId}
        className="w-full bg-transparent outline-none placeholder:text-mac-text-3 text-xs"
      />
    </div>
  );
};

export default SearchField;
