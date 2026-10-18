/**
 * UI-01 primitive — segmented control.
 *
 * The macOS series uses one segmented control idiom in three places: the
 * toolbar's `Canvas | List | Graph` view switch, the search inspector's
 * `exact / fuzzy / semantic` mode switch, and the Markdown editor's
 * `Write | Preview | Split` switch.
 *
 * Semantics are preserved exactly as the hand-rolled controls had them
 * (`role="radiogroup"` + `role="radio"` + `aria-checked`), so this is a pure
 * presentation swap; keyboard/AT behaviour is unchanged.
 */
export interface SegmentedOption<T extends string> {
  value: T;
  label: string;
  testId?: string;
}

export interface SegmentedControlProps<T extends string> {
  ariaLabel: string;
  value: T;
  options: ReadonlyArray<SegmentedOption<T>>;
  onChange: (value: T) => void;
  disabled?: boolean;
  size?: 'sm' | 'md';
  className?: string;
}

export function SegmentedControl<T extends string>({
  ariaLabel,
  value,
  options,
  onChange,
  disabled = false,
  size = 'md',
  className = '',
}: SegmentedControlProps<T>) {
  return (
    <div
      role="radiogroup"
      aria-label={ariaLabel}
      className={[
        'inline-flex items-center rounded-lg bg-mac-chip p-0.5 select-none',
        size === 'sm' ? 'text-[11px]' : 'text-xs',
        className,
      ]
        .filter(Boolean)
        .join(' ')}
    >
      {options.map((option) => {
        const active = option.value === value;
        return (
          <button
            key={option.value}
            type="button"
            role="radio"
            aria-checked={active}
            disabled={disabled}
            onClick={() => onChange(option.value)}
            data-testid={option.testId}
            className={[
              'rounded-md font-medium transition-colors cursor-pointer',
              size === 'sm' ? 'px-2 py-0.5' : 'px-2.5 py-1',
              active
                ? 'bg-mac-content text-mac-text shadow-xs'
                : 'text-mac-text-2 hover:text-mac-text',
            ].join(' ')}
          >
            {option.label}
          </button>
        );
      })}
    </div>
  );
}

export default SegmentedControl;
