import PushButton from '../shell/PushButton';

export interface BatchExportProgressProps {
  completed: number;
  total: number;
  currentFileName?: string | null;
  succeeded: number;
  skipped: number;
  failed: number;
  onCancel?: () => void;
}

export function BatchExportProgress({
  completed,
  total,
  currentFileName,
  succeeded,
  skipped,
  failed,
  onCancel,
}: BatchExportProgressProps) {
  const safeTotal = Math.max(total, 0);
  const safeCompleted = Math.min(Math.max(completed, 0), Math.max(safeTotal, 1));
  const percentage = safeTotal <= 0 ? 100 : Math.round((safeCompleted / safeTotal) * 100);
  const isDone = safeTotal > 0 && safeCompleted >= safeTotal;

  return (
    <section
      aria-label="Batch PDF export progress"
      role="region"
      className="mt-2 rounded-lg border border-mac-hairline bg-mac-content p-3"
    >
      <div
        aria-valuemax={safeTotal}
        aria-valuemin={0}
        aria-valuenow={safeCompleted}
        role="progressbar"
        className="flex items-center gap-2"
      >
        <div className="h-1.5 flex-1 overflow-hidden rounded-full bg-mac-chip">
          <div
            className="h-full rounded-full bg-mac-accent transition-[width]"
            style={{ width: `${percentage}%` }}
          />
        </div>
        <span className="text-[11px] tabular-nums text-mac-text-2">{percentage}%</span>
      </div>
      <p className="mt-2 text-xs text-mac-text">
        {safeCompleted} of {safeTotal} files
      </p>
      {currentFileName ? (
        <p aria-live="polite" className="truncate text-[11px] text-mac-text-2">
          {currentFileName}
        </p>
      ) : null}
      <p className="mt-1 text-[11px] text-mac-text-2">
        {succeeded} PDFs generated / {skipped} skipped / {failed} failed
      </p>
      <div className="mt-3 flex gap-2">
        {onCancel && !isDone ? (
          <PushButton onClick={onCancel} size="sm">
            Cancel
          </PushButton>
        ) : null}
      </div>
    </section>
  );
}
