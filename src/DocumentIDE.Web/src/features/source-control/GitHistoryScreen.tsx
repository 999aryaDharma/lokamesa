import InspectorCard, { InspectorField } from '../shell/InspectorCard';
import GitHistoryPanel from './GitHistoryPanel';

/**
 * UI-03 reachability — Git history surface.
 *
 * The sidebar's `History` row is a real navigation; it must not be an inert
 * label. This screen mounts the shipped SCM-08 `GitHistoryPanel` against the
 * real `GET /api/source-control/history` route. It is explicitly labelled as
 * *Git* history: the Document IDE operations/undo screen (design 10) is part B
 * of UI-03 and is not claimed here.
 */

export interface GitHistoryScreenProps {
  path: string | null;
  inspectorOpen: boolean;
  onAttachFolder: () => void;
}

export default function GitHistoryScreen({
  path,
  inspectorOpen,
  onAttachFolder,
}: GitHistoryScreenProps) {
  if (!path) {
    return (
      <section
        aria-label="History"
        data-testid="git-history-screen"
        className="flex h-full min-h-0 w-full flex-col items-center justify-center gap-3 bg-mac-content p-6 text-center"
      >
        <h1 className="text-sm font-semibold text-mac-text">History</h1>
        <p data-testid="git-history-no-path" className="max-w-md text-xs text-mac-text-2">
          Attach a folder source first. Git history is read from the repository that contains an
          attached workspace folder.
        </p>
        <button
          type="button"
          onClick={onAttachFolder}
          className="rounded-lg bg-mac-accent px-3 py-1.5 text-xs font-medium text-white hover:bg-mac-accent/90"
        >
          Attach folder source
        </button>
      </section>
    );
  }

  return (
    <section
      aria-label="History"
      data-testid="git-history-screen"
      className="flex h-full min-h-0 w-full bg-mac-content"
    >
      <div className="flex min-w-0 flex-1 flex-col">
        <header className="flex h-11 shrink-0 items-center justify-between gap-3 border-b border-mac-hairline px-3">
          <div className="flex min-w-0 items-baseline gap-2">
            <h1 className="text-sm font-semibold text-mac-text">History</h1>
            <span className="truncate text-[11px] text-mac-text-2">
              Git commit history · read-only
            </span>
          </div>
        </header>
        <div className="min-h-0 flex-1 overflow-y-auto">
          <GitHistoryPanel path={path} />
        </div>
      </div>

      {inspectorOpen && (
        <aside
          aria-label="History inspector"
          className="flex h-full w-[280px] shrink-0 flex-col gap-4 overflow-y-auto border-l border-mac-hairline bg-mac-inspector px-3 py-3"
        >
          <header className="flex items-center justify-between">
            <h2 className="text-[11px] font-semibold text-mac-text">Source</h2>
          </header>
          <InspectorCard title="Git history" testId="history-inspector-source">
            <InspectorField label="Repository" value={<span className="font-mono">{path}</span>} />
            <InspectorField label="Authority" value="Git" />
            <InspectorField label="Writes" value="None" />
            <p className="pt-1 text-[11px] text-mac-text-3">
              This list is Git's commit history. It is separate from Document IDE History
              (operations and undo), which is not part of this view.
            </p>
          </InspectorCard>
        </aside>
      )}
    </section>
  );
}
