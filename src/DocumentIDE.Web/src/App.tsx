import { useEffect, useState, useSyncExternalStore } from 'react';
import DocumentCanvas from './features/canvas/DocumentCanvas';
import BatchPreviewPanel from './features/batch/BatchPreviewPanel';
import DocumentGraph from './features/graph/DocumentGraph';
import DocumentList from './features/list/DocumentList';
import ViewSwitcher from './features/workspace/ViewSwitcher';
import { useViewStore } from './features/workspace/viewStore';

function useViewState() {
  return useSyncExternalStore(
    useViewStore.subscribe,
    useViewStore.getState,
    useViewStore.getState,
  );
}

export default function App() {
  const [batchOpen, setBatchOpen] = useState(false);
  const { activeView, setActiveView, loadPersistedView } = useViewState();

  // Restore the last active view on startup (CAN-08 persistence seam).
  useEffect(() => {
    loadPersistedView();
  }, [loadPersistedView]);

  return (
    <div className="w-screen h-screen overflow-hidden bg-slate-50 flex flex-col">
      <nav onKeyDown={e => e.stopPropagation()} aria-label="Tools" className="border-b border-slate-200 bg-white p-2 flex items-center gap-3">
        <ViewSwitcher activeView={activeView} onViewChange={setActiveView} />
        <button aria-expanded={batchOpen} onClick={() => setBatchOpen(open => !open)}>Batch operations</button>
      </nav>
      <div className="flex flex-1 min-h-0">
        <div className="flex-1 min-w-0">
          {activeView === 'canvas' && <DocumentCanvas />}
          {activeView === 'list' && <DocumentList />}
          {activeView === 'graph' && <DocumentGraph />}
        </div>
        {batchOpen && <BatchPreviewPanel />}
      </div>
    </div>
  );
}
