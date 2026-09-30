import { useEffect, useRef, useState } from 'react';
import { batchRequest, type Preview } from './batchApi';

const scopeNames = ['Selection', 'Folder', 'Workspace', 'Collection'];
const lines = (value: string) => value.split(/\r?\n/).map(v => v.trim()).filter(Boolean);
export default function BatchPreviewPanel() {
  const [workspaces, setWorkspaces] = useState<{ id: string; name: string }[]>([]);
  const [workspaceId, setWorkspaceId] = useState('');
  const [type, setType] = useState('');
  const [documentIds, setDocumentIds] = useState('');
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
  const active = useRef<{ controller: AbortController; planId?: string; timer?: ReturnType<typeof setTimeout>; running: boolean } | null>(null);
  const editable = preview?.status === 'Completed' && !busy && !stale;
  useEffect(() => () => {
    const job = active.current;
    if (!job) return;
    job.controller.abort(); clearTimeout(job.timer);
    if (job.running && job.planId) void batchRequest(`/${encodeURIComponent(job.planId)}/cancel`, { method: 'POST' }).catch(() => {});
  }, []);
  useEffect(() => {
    const controller = new AbortController();
    batchRequest<{ id: string; name: string }[]>('/workspaces', { signal: controller.signal }).then(setWorkspaces).catch(() => { if (!controller.signal.aborted) setError('Unable to load workspaces.'); });
    return () => controller.abort();
  }, []);
  async function start() {
    if (active.current) { active.current.controller.abort(); clearTimeout(active.current.timer); }
    const job = { controller: new AbortController(), running: true, planId: undefined as string | undefined, timer: undefined as ReturnType<typeof setTimeout> | undefined };
    active.current = job;
    setError(''); setPreview(null); setBusy(true); setDirty(false); setSaved(false); setStale(false);
    try {
      const result = await batchRequest<{ planId: string }>('/preview', { method: 'POST', signal: job.controller.signal, body: JSON.stringify({ scope: { workspaceId, type: Number(type), documentIds: type === '0' ? lines(documentIds) : [], folderPath: type === '1' ? folderPath : null, collectionId: type === '3' ? collectionId : null, includeSubfolders, excludedBranches: lines(excludedBranches) }, find, replacement }) });
      if (job.controller.signal.aborted) return;
      job.planId = result.planId;
      const poll = async () => {
        try {
          const data = await batchRequest<Preview>(`/${encodeURIComponent(result.planId)}`, { signal: job.controller.signal });
          if (job.controller.signal.aborted) return;
          job.running = data.status === 'Pending' || data.status === 'Running';
          setPreview(data); setBusy(false);
          if (job.running) job.timer = setTimeout(() => void poll(), 750);
        } catch (e) { if (!job.controller.signal.aborted) { job.running = false; setError((e as Error).message); setBusy(false); } }
      };
      await poll();
    } catch (e) { if (!job.controller.signal.aborted) { job.running = false; setError((e as Error).message); setBusy(false); } }
  }
  async function cancel() {
    const job = active.current;
    if (!job?.planId) return;
    job.controller.abort(); clearTimeout(job.timer); setBusy(true);
    try {
      await batchRequest(`/${encodeURIComponent(job.planId)}/cancel`, { method: 'POST' });
      if (active.current !== job) return;
      job.running = false;
      setPreview(p => p && { ...p, status: 'Cancelled' });
    } catch (e) { if (active.current === job) setError((e as Error).message); }
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
  function edit(documentId: string, occurrenceId?: string) {
    setSaved(false); setDirty(true);
    setPreview(p => p && { ...p, files: p.files.map(f => f.documentId !== documentId ? f : occurrenceId ? { ...f, occurrences: f.occurrences.map(o => o.id === occurrenceId ? { ...o, selected: !o.selected } : o) } : { ...f, selected: !f.selected }) });
  }
  return <section aria-label="Batch operations" className="w-full max-w-xl shrink-0 overflow-auto border-l border-slate-200 bg-white p-4" onKeyDown={e => e.stopPropagation()}>
    <h2>Batch operations</h2>
    <form onSubmit={e => { e.preventDefault(); void start(); }} className="grid gap-3">
      <label>Workspace<select required value={workspaceId} onChange={e => setWorkspaceId(e.target.value)}><option value="">Choose workspace</option>{workspaces.map(w => <option key={w.id} value={w.id}>{w.name}</option>)}</select></label>
      <label>Scope<select required value={type} onChange={e => setType(e.target.value)}><option value="">Choose scope</option>{scopeNames.map((name, i) => <option key={name} value={i}>{name}</option>)}</select></label>
      {type === '0' && <label>Document IDs (one per line)<textarea required value={documentIds} onChange={e => setDocumentIds(e.target.value)} /></label>}
      {type === '1' && <label>Root folder<input required value={folderPath} onChange={e => setFolderPath(e.target.value)} /></label>}
      {type === '3' && <label>Collection ID<input required value={collectionId} onChange={e => setCollectionId(e.target.value)} /></label>}
      <label><input type="checkbox" checked={includeSubfolders} onChange={e => setIncludeSubfolders(e.target.checked)} />Include subfolders</label>
      <label>Excluded branches (one per line)<textarea value={excludedBranches} onChange={e => setExcludedBranches(e.target.value)} /></label>
      <label>Exact find<textarea required value={find} onChange={e => setFind(e.target.value)} /></label>
      <label>Replacement<textarea value={replacement} onChange={e => setReplacement(e.target.value)} /></label>
      <button disabled={busy || !!active.current?.running || !workspaceId || type === '' || !find} type="submit">Preview</button>
    </form>
    {error && <p role="alert">{error}</p>}
    {preview && <div>
      <p role="status">{preview.status}: {preview.progress}%</p><progress aria-label="Preview progress" value={preview.progress} max={100} />{preview.errorCode && <p role="alert">{preview.errorCode}</p>}{preview.status === 'Expired' && <p role="alert">Plan expired. Re-preview required.</p>}{(preview.status === 'Running' || preview.status === 'Pending') && <button disabled={busy} onClick={() => void cancel()}>Cancel preview</button>}
      <p>{preview.filesScanned} files scanned; {preview.filesAffected} files affected; {preview.occurrenceCount} occurrences</p>
      {Object.entries(preview.breakdown).map(([origin, count]) => <p key={origin}>{origin[0].toUpperCase() + origin.slice(1)}: {count}</p>)}
      <h3>Resolved scope: {scopeNames[preview.scope.type]}</h3>
      <p>Workspace ID: {preview.scope.workspaceId}</p>
      <p>Document IDs: {preview.scope.documentIds.join(', ') || 'None explicitly requested'}</p>
      <p>Collection ID: {preview.scope.collectionId || 'None'}</p>
      <p>Include subfolders: {preview.scope.includeSubfolders ? 'On' : 'Off'}</p>
      <p>Root folder: {preview.scope.folderPath || 'Not a folder scope'}</p>
      <p>{preview.scope.subfolderCount} included subfolders</p>
      <ul>{preview.scope.includedSubfolders.map(path => <li key={path}>{path}</li>)}</ul>
      <p>Excluded branches: {preview.scope.excludedBranches.join(', ') || 'None'}</p>
      {preview.scope.failures.map((failure, i) => <p key={i}>Scope failure: {failure.failureCode} {failure.documentId}</p>)}
      {preview.files.map(file => <div key={file.documentId}><h3>{file.fileName}</h3><label><input type="checkbox" aria-label={`Select ${file.fileName}`} disabled={!editable || !!file.failureCode} checked={file.selected} onChange={() => edit(file.documentId)} />Select file</label><p>Lock: {file.lockState}; Failure: {file.failureCode || 'None'}</p>{file.occurrences.map(o => <label className="block" key={o.id}><input type="checkbox" aria-label={`${o.origin} occurrence ${o.id}`} disabled={!editable || !!file.failureCode} checked={o.selected} onChange={() => edit(file.documentId, o.id)} />{o.origin}: {o.context}</label>)}</div>)}
    </div>}
    <button disabled={!editable || !dirty} onClick={() => void save()}>Save selection</button>{saved && <p role="status">Selection saved.</p>}
    <button disabled title="Applying changes is not available yet">Apply</button><p>Preview and review only. Applying changes is not available yet.</p>
  </section>;
}


