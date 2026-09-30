import { useState } from 'react';
import DocumentCanvas from './features/canvas/DocumentCanvas';
import BatchPreviewPanel from './features/batch/BatchPreviewPanel';

export default function App() {
  const [batchOpen, setBatchOpen] = useState(false);
  return (
    <div className="w-screen h-screen overflow-hidden bg-slate-50 flex flex-col">
      <nav onKeyDown={e => e.stopPropagation()} aria-label="Tools" className="border-b border-slate-200 bg-white p-2">
        <button aria-expanded={batchOpen} onClick={() => setBatchOpen(open => !open)}>Batch operations</button>
      </nav>
      <div className="flex flex-1 min-h-0">
        <div className="flex-1 min-w-0"><DocumentCanvas /></div>
        {batchOpen && <BatchPreviewPanel />}
      </div>
    </div>
  );
}
