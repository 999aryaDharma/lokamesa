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
    <section aria-label="Batch PDF export progress" role="region">
      <div
        aria-valuemax={safeTotal}
        aria-valuemin={0}
        aria-valuenow={safeCompleted}
        role="progressbar"
      >
        {percentage}%
      </div>
      <p>
        {safeCompleted} of {safeTotal} files
      </p>
      {currentFileName ? <p aria-live="polite">{currentFileName}</p> : null}
      <p>
        {succeeded} PDFs generated / {skipped} skipped / {failed} failed
      </p>
      {onCancel && !isDone ? (
        <button onClick={onCancel} type="button">
          Cancel
        </button>
      ) : null}
    </section>
  );
}
