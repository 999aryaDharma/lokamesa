import React from 'react';

/**
 * UI-01 primitive — depth stepper (`−  N  +`).
 *
 * Used by the graph scope bar ("Depth") and by the create-worktree sheet's
 * numeric fields. Purely presentational: the caller owns `value` and receives
 * clamped candidate values through `onChange`.
 */
export interface DepthStepperProps {
  value: number;
  min: number;
  max: number;
  step?: number;
  onChange: (value: number) => void;
  /** Short label rendered before the controls (e.g. "Depth"). */
  label?: string;
  disabled?: boolean;
  className?: string;
}

export const DepthStepper: React.FC<DepthStepperProps> = ({
  value,
  min,
  max,
  step = 1,
  onChange,
  label,
  disabled = false,
  className = '',
}) => {
  const atMin = value - step < min;
  const atMax = value + step > max;

  return (
    <div
      className={[
        'inline-flex items-center gap-1 rounded-md border border-mac-hairline-strong',
        'bg-mac-content px-1.5 py-1 text-xs text-mac-text',
        className,
      ]
        .filter(Boolean)
        .join(' ')}
    >
      {label && <span className="text-mac-text-2 pr-0.5">{label}</span>}
      <button
        type="button"
        aria-label={`Decrease ${label ? label.toLowerCase() : 'depth'}`}
        disabled={disabled || atMin}
        onClick={() => onChange(Math.max(min, value - step))}
        className="w-4 h-4 flex items-center justify-center rounded text-mac-text-2 hover:bg-mac-chip disabled:opacity-40 cursor-pointer"
      >
        −
      </button>
      <span className="min-w-[1rem] text-center font-medium tabular-nums">{value}</span>
      <button
        type="button"
        aria-label={`Increase ${label ? label.toLowerCase() : 'depth'}`}
        disabled={disabled || atMax}
        onClick={() => onChange(Math.min(max, value + step))}
        className="w-4 h-4 flex items-center justify-center rounded text-mac-text-2 hover:bg-mac-chip disabled:opacity-40 cursor-pointer"
      >
        +
      </button>
    </div>
  );
};

export default DepthStepper;
