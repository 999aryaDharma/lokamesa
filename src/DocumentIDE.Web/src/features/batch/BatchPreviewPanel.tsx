import { useEffect, useRef, useState } from 'react';
import SheetShell from '../shell/SheetShell';
import { batchRequest, type Preview } from './batchApi';
import SearchReplaceReview from './SearchReplaceReview';

export interface BatchApplyResult {
  planId: string; snapshotId: string | null; status: string;
  files: { documentId: string; filePath: string; outcome: string; reasonCode: string | null; detail: string | null }[];
}

export interface BatchPreviewPanelProps {
  /** Optional: renders the sheet's close affordance (same action as the toolbar toggle). */
  onClose?: () => void;
}

const scopeNames = ['Selection', 'Folder', 'Workspace', 'Collection'];
const lines = (value: string) => value.split(/\r?\n/).map(v => v.trim()).filter(Boolean);
export default function BatchPreviewPanel({ onClose }: BatchPreviewPanelProps = {}) {
  const [workspaces, setWorkspaces] = useState<{ id: string; name: string }[]>([]);
  const [workspaceId, setWorkspaceId] = useState('');
  const [type, setType] = useState('');
  const [documentIds, setDocumentIds] = useState<string[]>([]);
  const [options, setOptions] = useState<{ documents: { id: string; fileName: string }[]; collections: { id: string; name: string }[] }>({ documents: [], collections: [] });
  const [folderPath, setFolderPath] = useState('');
  const [collectionId, setCollectionId] = useState('');
  const [excludedBranches, setExcludedBranches] = useState('');
  const [includeSubfolders, setIncludeSubfolders] = useState(true);
  const [find, setFind] = useState('');
  const [replacement, setReplacement] = useState('');
  const [preview, setPreview] = useState<Preview | null>(null);
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const [dirty, setDirty] = useState(false);
  const [saved, setSaved] = useState(false);
  const [stale, setStale] = useState(false);
  const [pollFailed, setPollFailed] = useState(false);
  const [applyState, setApplyState] = useState<{ planId: string; summary: string } | null>(null);
  const active = useRef<{ controller: AbortController; planId?: string; timer?: ReturnType<typeof setTimeout>; running: boolean } | null>(null);
  const editable = preview?.status === 'Completed' && !busy && !stale;
  // Applying uses the plan that was last saved server-side, so unsaved local
  // edits (dirty) must be saved first; a plan can only be applied once.
  const canApply = editable && !dirty && applyState?.planId !== preview?.planId;
  useEffect(() => () => {
    const job = active.current;
    if (!job) return;
    active.current = null;
    job.controller.abort(); clearTimeout(job.timer);
    if (job.running && job.planId) void batchRequest(`/${encodeURIComponent(job.planId)}/cancel`, { method: 'POST' }).catch(() => {});
  }, []);
  useEffect(() => {
    const controller = new AbortController();
    batchRequest<{ id: string; name: string }[]>('/workspaces', { signal: controller.signal }).then(setWorkspaces).catch(() => { if (!controller.signal.aborted) setError('Unable to load workspaces.'); });
    return () => controller.abort();
  }, []);
  useEffect(() => {
    const controller = new AbortController();
    setDocumentIds([]); setCollectionId(''); setOptions({ documents: [], collections: [] });
    if (workspaceId) void batchRequest<typeof options>(`/workspaces/${encodeURIComponent(workspaceId)}/scope-options`, { signal: controller.signal })
      .then(data => { if (!controller.signal.aborted) setOptions(data); })
      .catch(() => { if (!controller.signal.aborted) setError('Unable to load scope choices.'); });
    return () => controller.abort();
  }, [workspaceId]);
  async function poll(job: NonNullable<typeof active.current>) {
    const controller = job.controller;
    try {
      const data = await batchRequest<Preview>(`/${encodeURIComponent(job.planId!)}`, { signal: controller.signal });
      if (controller.signal.aborted || active.current !== job) return;
      job.running = data.status === 'Pending' || data.status === 'Running';
      setPreview(data); setBusy(false); setPollFailed(false);
      if (job.running) job.timer = setTimeout(() => void poll(job), 750);
    } catch (e) {
      if (!controller.signal.aborted && active.current === job) {
        setError((e as Error).message); setBusy(false); setPollFailed(true);
      }
    }
  }
  function retry() {
    const job = active.current;
    if (!job?.planId) return;
    job.controller.abort(); clearTimeout(job.timer);
    job.controller = new AbortController();
    setError(''); setPollFailed(false); setBusy(true);
    void poll(job);
  }
  async function start() {
    if (active.current?.running && active.current.planId && !await cancel()) return;
    if (active.current) { active.current.controller.abort(); clearTimeout(active.current.timer); }
    const job = { controller: new AbortController(), running: true, planId: undefined as string | undefined, timer: undefined as ReturnType<typeof setTimeout> | undefined };
    active.current = job;
    setError(''); setPreview(null); setBusy(true); setDirty(false); setSaved(false); setStale(false); setPollFailed(false); setApplyState(null);
    try {
      const result = await batchRequest<{ planId: string }>('/preview', { method: 'POST', body: JSON.stringify({ scope: { workspaceId, type: Number(type), documentIds: type === '0' ? documentIds : [], folderPath: type === '1' ? folderPath : null, collectionId: type === '3' ? collectionId : null, includeSubfolders: type === '1' ? includeSubfolders : true, excludedBranches: type === '1' ? lines(excludedBranches) : [] }, find, replacement }) });
      if (active.current !== job) {
        if (result.planId) void batchRequest(`/${encodeURIComponent(result.planId)}/cancel`, { method: 'POST' }).catch(() => {});
        return;
      }
      job.planId = result.planId;
      await poll(job);
    } catch (e) { if (!job.controller.signal.aborted) { job.running = false; setError((e as Error).message); setBusy(false); } }
  }
  async function cancel() {
    const job = active.current;
    if (!job?.planId) return;
    job.controller.abort(); clearTimeout(job.timer); setBusy(true);
    try {
      await batchRequest(`/${encodeURIComponent(job.planId)}/cancel`, { method: 'POST' });
      if (active.current !== job) return false;
      job.running = false; setPollFailed(false); setError('');
      setPreview(p => p && { ...p, status: 'Cancelled' });
      return true;
    } catch (e) { if (active.current === job) { setError((e as Error).message); setPollFailed(true); } return false; }
    finally { if (active.current === job) setBusy(false); }
  }
  async function save() {
    const job = active.current;
    if (!preview || !job || !editable) return;
    setBusy(true); setError(''); setSaved(false);
    try {
      const data = await batchRequest<Preview>(`/${encodeURIComponent(preview.planId)}/selection`, { method: 'PUT', signal: job.controller.signal, body: JSON.stringify({ files: preview.files.map(f => ({ documentId: f.documentId, selected: f.selected, selectedOccurrenceIds: f.occurrences.filter(o => o.selected).map(o => o.id) })) }) });
      if (job.controller.signal.aborted) return;
      setPreview(data); setDirty(false); setSaved(true);
    } catch (e) {
      if (!job.controller.signal.aborted) { const message = (e as Error).message; setError(message); setStale(message.includes('Re-preview')); }
    } finally { if (!job.controller.signal.aborted) setBusy(false); }
  }
  async function apply() {
    const job = active.current;
    if (!preview || !job || !canApply) return;
    setBusy(true); setError('');
    try {
      const result = await batchRequest<BatchApplyResult>(`/${encodeURIComponent(preview.planId)}/apply`, { method: 'POST', signal: job.controller.signal });
      if (job.controller.signal.aborted) return;
      const applied = result.files.filter(f => f.outcome === 'Success').length;
      const skipped = result.files.filter(f => f.outcome === 'Skipped').length;
      const failed = result.files.filter(f => f.outcome === 'Failure').length;
      setApplyState({ planId: preview.planId, summary: `Apply ${result.status}: ${applied} applied / ${skipped} skipped / ${failed} failed` });
    } catch (e) {
      if (!job.controller.signal.aborted) { const message = (e as Error).message; setError(message); setStale(message.includes('Re-preview')); }
    } finally { if (!job.controller.signal.aborted) setBusy(false); }
  }
  function edit(documentId: string, occurrenceId?: string) {
    setSaved(false); setDirty(true);
    setPreview(p => p && { ...p, files: p.files.map(f => f.documentId !== documentId ? f : occurrenceId ? { ...f, occurrences: f.occurrences.map(o => o.id === occurrenceId ? { ...o, selected: !o.selected } : o) } : { ...f, selected: !f.selected }) });
  }
  return <section
    aria-label="Batch operations"
    data-testid="batch-sheet-layer"
    onKeyDown={e => e.stopPropagation()}
    // UI-03 screen 04: the design's centred sheet, deliberately non-modal.
    // No scrim and `pointer-events-none` on the layer mean the canvas stays
    // usable — only the sheet panel itself receives input.
    className="pointer-events-none absolute inset-0 z-30 flex items-center justify-center p-6"
  >
    <SheetShell
      title="Batch operations"
      testId="batch-sheet-panel"
      widthClass="max-w-[720px]"
      className="pointer-events-auto"
      scrim={false}
      onClose={onClose}
      status={preview ? `${preview.filesAffected === 1 ? '1 file' : `${preview.filesAffected} files`} · ${preview.occurrenceCount} occurrences` : undefined}
      footer={<div className="flex w-full items-center gap-2">
        {!preview && active.current?.planId && active.current.running && <button disabled={busy} onClick={() => void cancel()}>Cancel preview</button>}
        {preview && (preview.status === 'Running' || preview.status === 'Pending') && <button disabled={busy} onClick={() => void cancel()}>Cancel preview</button>}
        <div className="ml-auto flex items-center gap-2">
          <button disabled={!editable || !dirty} onClick={() => void save()}>Save selection</button>
          <button disabled={!canApply} onClick={() => void apply()}>Apply</button>
        </div>
      </div>}
    >
      <form onSubmit={e => { e.preventDefault(); void start(); }} className="grid gap-3">
        <label>Workspace<select required value={workspaceId} onChange={e => setWorkspaceId(e.target.value)}><option value="">Choose workspace</option>{workspaces.map(w => <option key={w.id} value={w.id}>{w.name}</option>)}</select></label>
        <label>Scope<select required value={type} onChange={e => setType(e.target.value)}><option value="">Choose scope</option>{scopeNames.map((name, i) => <option key={name} value={i}>{name}</option>)}</select></label>
        {type === '0' && <fieldset><legend>Documents</legend>{options.documents.map(doc => <label className="block" key={doc.id}><input type="checkbox" checked={documentIds.includes(doc.id)} onChange={e => setDocumentIds(ids => e.target.checked ? [...ids, doc.id] : ids.filter(id => id !== doc.id))} />{doc.fileName}</label>)}</fieldset>}
        {type === '1' && <label>Root folder<input required value={folderPath} onChange={e => setFolderPath(e.target.value)} /></label>}
        {type === '3' && <label>Collection<select required value={collectionId} onChange={e => setCollectionId(e.target.value)}><option value="">Choose collection</option>{options.collections.map(collection => <option key={collection.id} value={collection.id}>{collection.name}</option>)}</select></label>}
        {type === '1' && <label><input type="checkbox" checked={includeSubfolders} onChange={e => setIncludeSubfolders(e.target.checked)} />Include subfolders</label>}
        {type === '1' && <label>Excluded branches (one per line)<textarea value={excludedBranches} onChange={e => setExcludedBranches(e.target.value)} /></label>}
        <label>Exact find<textarea required value={find} onChange={e => setFind(e.target.value)} /></label>
        <label>Replacement<textarea value={replacement} onChange={e => setReplacement(e.target.value)} /></label>
        <button disabled={busy || (!!active.current?.running && !pollFailed) || !workspaceId || type === '' || !find || (type === '0' && documentIds.length === 0) || (type === '3' && !collectionId)} type="submit">Preview</button>
      </form>
      {error && <p role="alert">{error}</p>}
      {pollFailed && active.current?.planId && <button disabled={busy} onClick={retry}>Retry preview status</button>}
      {preview && <div>
        <p role="status">{preview.status}: {preview.progress}%</p><progress aria-label="Preview progress" value={preview.progress} max={100} />{preview.errorCode && <p role="alert">{preview.errorCode}</p>}{preview.status === 'Expired' && <p role="alert">Plan expired. Re-preview required.</p>}
        <SearchReplaceReview
          preview={preview}
          find={find}
          replacement={replacement}
          editable={editable}
          scopeLabel={scopeNames[preview.scope.type]}
          workspaceName={workspaces.find(w => w.id === preview.scope.workspaceId)?.name || 'Unavailable workspace'}
          collectionLabel={preview.scope.collectionId ? options.collections.find(c => c.id === preview.scope.collectionId)?.name || 'Unavailable collection' : 'None'}
          documentName={id => preview.files.find(f => f.documentId === id)?.fileName || options.documents.find(d => d.id === id)?.fileName || 'Unavailable document'}
          onEdit={edit}
        />
      </div>}
      {saved && <p role="status">Selection saved.</p>}
      {applyState && <p role="status">{applyState.summary}</p>}
      {!applyState && dirty && <p role="status">Save the reviewed selection before applying.</p>}
    </SheetShell>
  </section>;
}


