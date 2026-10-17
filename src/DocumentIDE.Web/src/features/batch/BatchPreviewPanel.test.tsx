import { render, screen, fireEvent, within } from '@testing-library/react';
import { beforeEach, expect, it, vi } from 'vitest';
import BatchPreviewPanel from './BatchPreviewPanel';

const completed = {
  planId: 'p1', jobId: 'j1', status: 'Completed', progress: 100, errorCode: null,
  scope: { workspaceId: 'w1', type: 1, documentIds: [], folderPath: 'root', includeSubfolders: true, excludedBranches: ['excluded'], collectionId: null, includedSubfolders: ['child'], subfolderCount: 1, failures: [] },
  filesScanned: 2, filesAffected: 1, occurrenceCount: 4,
  breakdown: { body: 1, table: 1, header: 1, footer: 1 },
  files: [{ documentId: 'd1', fileName: 'one.docx', selected: true, lockState: 'Unknown', failureCode: null,
    occurrences: ['Body', 'Table', 'Header', 'Footer'].map((origin, i) => ({ id: `o${i}`, origin, context: `context-${origin}`, selected: true })) },
    { documentId: 'd2', fileName: 'bad.docx', selected: false, lockState: 'Unknown', failureCode: 'InvalidPackage', occurrences: [] }],
};
let getPlan: unknown;
let saveStatus: number;
let applyStatus: number;
let requests: { url: string; init?: RequestInit }[];
const applied = { planId: 'p1', snapshotId: 's1', status: 'Completed', files: [{ documentId: 'd1', filePath: 'one.docx', outcome: 'Success', reasonCode: null, detail: null }] };
beforeEach(() => {
  requests = []; getPlan = completed; saveStatus = 200; applyStatus = 200;
  Object.assign(window, { __DOCUMENTIDE_API_URL__: 'http://localhost:5000', __DOCUMENTIDE_SESSION_TOKEN__: 'secret' });
  vi.stubGlobal('fetch', vi.fn(async (url: string, init?: RequestInit) => {
    requests.push({ url, init });
    const data = url.endsWith('/workspaces') ? [{ id: 'w1', name: 'Office' }] : url.endsWith('/scope-options') ? { documents: [{ id: 'trusted-id', fileName: 'Trusted.md' }], collections: [] } : url.endsWith('/preview') ? { planId: 'p1', jobId: 'j1' } : url.endsWith('/apply') ? applied : getPlan;
    const status = url.endsWith('/selection') ? saveStatus : url.endsWith('/apply') ? applyStatus : url.endsWith('/cancel') ? 204 : 200;
    return { ok: status < 400, status, json: async () => data } as Response;
  }));
});
async function start() {
  render(<BatchPreviewPanel />);
  await screen.findByRole('option', { name: 'Office' });
  fireEvent.change(screen.getByLabelText('Workspace'), { target: { value: 'w1' } });
  fireEvent.change(screen.getByLabelText('Scope'), { target: { value: '1' } });
  fireEvent.change(screen.getByLabelText('Root folder'), { target: { value: 'root' } });
  fireEvent.change(screen.getByLabelText('Exact find'), { target: { value: 'needle' } });
  fireEvent.click(screen.getByRole('button', { name: 'Preview' }));
}
it('requests explicit authenticated scope and renders counts, origins, scope and failures', async () => {
  await start();
  expect(await screen.findByText('2 files scanned; 1 files affected; 4 occurrences')).toBeInTheDocument();
  for (const origin of ['Body', 'Table', 'Header', 'Footer']) expect(screen.getByText(`${origin}: 1`)).toBeInTheDocument();
  expect(screen.getByText('child')).toBeInTheDocument();
  expect(screen.getByText(/InvalidPackage/)).toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Apply' })).toBeEnabled();
  const request = requests.find(r => r.url.endsWith('/preview'))!;
  expect(JSON.parse(request.init!.body as string).scope).toMatchObject({ workspaceId: 'w1', type: 1, includeSubfolders: true, folderPath: 'root' });
  expect(request.init!.headers).toMatchObject({ 'X-DocumentIDE-Session-Token': 'secret' });
});
it('applies the saved reviewed plan through the API and reports the typed outcome once', async () => {
  await start(); await screen.findByText('Completed: 100%');
  fireEvent.click(screen.getByRole('button', { name: 'Apply' }));
  expect(await screen.findByText('Apply Completed: 1 applied / 0 skipped / 0 failed')).toBeInTheDocument();
  const request = requests.find(r => r.url.endsWith('/p1/apply'))!;
  expect(request.init!.method).toBe('POST');
  expect(request.init!.headers).toMatchObject({ 'X-DocumentIDE-Session-Token': 'secret' });
  expect(screen.getByRole('button', { name: 'Apply' })).toBeDisabled();
});
it('requires re-preview when apply is rejected as stale and withholds apply until saved', async () => {
  await start(); await screen.findByText('Completed: 100%');
  fireEvent.click(screen.getByLabelText('Select one.docx'));
  expect(screen.getByRole('button', { name: 'Apply' })).toBeDisabled();
  fireEvent.click(screen.getByRole('button', { name: 'Save selection' }));
  await screen.findByText('Selection saved.');
  expect(screen.getByRole('button', { name: 'Apply' })).toBeEnabled();
  applyStatus = 409;
  fireEvent.click(screen.getByRole('button', { name: 'Apply' }));
  expect(await screen.findByRole('alert')).toHaveTextContent('Plan is stale or expired. Re-preview required.');
  expect(screen.getByRole('button', { name: 'Apply' })).toBeDisabled();
});
it('keeps edits local until Save selection and requires re-preview on stale save', async () => {
  await start(); await screen.findByText('Completed: 100%');
  fireEvent.click(screen.getByLabelText('Body occurrence o0'));
  fireEvent.click(screen.getByLabelText('Select one.docx'));
  expect(requests.filter(r => r.init?.method === 'PUT')).toHaveLength(0);
  fireEvent.click(screen.getByRole('button', { name: 'Save selection' }));
  await screen.findByText('Selection saved.');
  expect(JSON.parse(requests.find(r => r.init?.method === 'PUT')!.init!.body as string)).toMatchObject({ files: [{ documentId: 'd1', selected: false, selectedOccurrenceIds: ['o1', 'o2', 'o3'] }, { documentId: 'd2', selected: false, selectedOccurrenceIds: [] }] });
  saveStatus = 409;
  fireEvent.click(screen.getByLabelText('Body occurrence o0'));
  fireEvent.click(screen.getByRole('button', { name: 'Save selection' }));
  expect(await screen.findByRole('alert')).toHaveTextContent(/Re-preview required/);
});
it('Unchecked occurrence is not applied: marks it excluded, omits it from the saved plan using only preview-returned IDs, and prompts re-preview when the save goes stale', async () => {
  await start(); await screen.findByText('Completed: 100%');
  fireEvent.click(screen.getByLabelText('Body occurrence o0'));
  const file = screen.getByText('one.docx').closest('div')!;
  expect(within(file).getByText('Not applied')).toBeInTheDocument();
  expect(within(file).getAllByText('Not applied')).toHaveLength(1);
  fireEvent.click(screen.getByRole('button', { name: 'Save selection' }));
  await screen.findByText('Selection saved.');
  const payload: { files: { documentId: string; selectedOccurrenceIds: string[] }[] } = JSON.parse(requests.find(r => r.init?.method === 'PUT')!.init!.body as string);
  expect(payload.files).toMatchObject([
    { documentId: 'd1', selected: true, selectedOccurrenceIds: ['o1', 'o2', 'o3'] },
    { documentId: 'd2', selected: false, selectedOccurrenceIds: [] },
  ]);
  const returnedIds = completed.files.flatMap(f => f.occurrences.map(o => o.id));
  const sentIds = payload.files.flatMap(f => f.selectedOccurrenceIds);
  expect(payload.files.map(f => f.documentId)).toEqual(completed.files.map(f => f.documentId));
  expect(sentIds.every(id => returnedIds.includes(id))).toBe(true);
  saveStatus = 409;
  fireEvent.click(screen.getByLabelText('Table occurrence o1'));
  fireEvent.click(screen.getByRole('button', { name: 'Save selection' }));
  expect(await screen.findByRole('alert')).toHaveTextContent(/Re-preview required/);
  expect(screen.getByLabelText('Table occurrence o1')).toBeDisabled();
  expect(screen.getByRole('button', { name: 'Save selection' })).toBeDisabled();
});
it('shows pending progress, disables editing and cancels through the API', async () => {
  getPlan = { ...completed, status: 'Running', progress: 40 };
  await start(); await screen.findByText('Running: 40%');
  expect(screen.getByRole('button', { name: 'Apply' })).toBeDisabled();
  expect(screen.getByRole('button', { name: 'Save selection' })).toBeDisabled();
  fireEvent.click(screen.getByRole('button', { name: 'Cancel preview' }));
  await screen.findByText('Cancelled: 40%');
  expect(requests.some(r => r.url.endsWith('/p1/cancel') && r.init?.method === 'POST')).toBe(true);
});
it('renders failed and expired terminal outcomes', async () => {
  getPlan = { ...completed, status: 'Failed', errorCode: 'ScanFailed' };
  await start(); expect(await screen.findByText('ScanFailed')).toBeInTheDocument();
});
it('shows frozen workspace and membership rather than edited request fields', async () => {
  getPlan = { ...completed, scope: { ...completed.scope, type: 0, documentIds: ['trusted-id'] } };
  await start();
  expect(await screen.findByText('Workspace: Office')).toBeInTheDocument();
  expect(screen.getByText('Documents: Trusted.md')).toBeInTheDocument();
});
it('polls to completion and stops polling at terminal status', async () => {
  getPlan = { ...completed, status: 'Running', progress: 40 };
  await start(); await screen.findByText('Running: 40%');
  getPlan = completed;
  await screen.findByText('Completed: 100%');
  const polls = requests.filter(r => r.url.endsWith('/p1')).length;
  await new Promise(resolve => setTimeout(resolve, 800));
  expect(requests.filter(r => r.url.endsWith('/p1'))).toHaveLength(polls);
});
it('labels resolved folder enumeration and scope failures accurately', async () => {
  getPlan = { ...completed, scope: { ...completed.scope, failures: [{ documentId: 'unsafe', failureCode: 'UnsafePath' }] } };
  await start();
  expect(await screen.findByText('1 included subfolders')).toBeInTheDocument();
  expect(screen.getByText('Scope failure: UnsafePath unsafe')).toBeInTheDocument();
});
it('aborts active reads and cancels the running job on unmount without further polling', async () => {
  getPlan = { ...completed, status: 'Running', progress: 40 };
  const view = render(<BatchPreviewPanel />);
  await screen.findByRole('option', { name: 'Office' });
  fireEvent.change(screen.getByLabelText('Workspace'), { target: { value: 'w1' } });
  fireEvent.change(screen.getByLabelText('Scope'), { target: { value: '2' } });
  fireEvent.change(screen.getByLabelText('Exact find'), { target: { value: 'needle' } });
  fireEvent.click(screen.getByRole('button', { name: 'Preview' }));
  await screen.findByText('Running: 40%');
  const read = requests.find(r => r.url.endsWith('/p1'))!;
  view.unmount();
  expect(read.init?.signal?.aborted).toBe(true);
  expect(requests.some(r => r.url.endsWith('/cancel'))).toBe(true);
  const count = requests.length;
  await new Promise(resolve => setTimeout(resolve, 800));
  expect(requests).toHaveLength(count);
});
it('ignores a late response from a replaced plan', async () => {
  let resolveOld!: (response: Response) => void;
  const original = globalThis.fetch;
  let held = false;
  vi.stubGlobal('fetch', vi.fn((url: string, init?: RequestInit) => {
    if (url.endsWith('/p1') && !held) { held = true; return new Promise<Response>(resolve => { resolveOld = resolve; }); }
    return original(url, init);
  }));
  await start();
  await new Promise(resolve => setTimeout(resolve, 0));
  getPlan = { ...completed, filesScanned: 7 };
  fireEvent.submit(screen.getByRole('button', { name: 'Preview' }).closest('form')!);
  await screen.findByText('7 files scanned; 1 files affected; 4 occurrences');
  resolveOld({ ok: true, status: 200, json: async () => completed } as Response);
  await new Promise(resolve => setTimeout(resolve, 0));
  expect(screen.queryByText('2 files scanned; 1 files affected; 4 occurrences')).not.toBeInTheDocument();
});
it('requires re-preview after expiry and does not permit reviewed edits', async () => {
  getPlan = { ...completed, status: 'Expired' };
  await start();
  expect(await screen.findByRole('alert')).toHaveTextContent('Plan expired. Re-preview required.');
  expect(screen.getByLabelText('Select one.docx')).toBeDisabled();
});
it('reports request errors without showing arbitrary server error text', async () => {
  const original = globalThis.fetch;
  vi.stubGlobal('fetch', vi.fn((url: string, init?: RequestInit) => url.endsWith('/preview') ? Promise.resolve({ ok: false, status: 500, json: async () => ({ error: 'secret internal path' }) } as Response) : original(url, init)));
  await start();
  expect(await screen.findByRole('alert')).toHaveTextContent('Batch request failed (500).');
  expect(screen.queryByText('secret internal path')).not.toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Preview' })).toBeEnabled();
});
it('offers trusted document and collection names and resets membership on workspace change', async () => {
  const original = globalThis.fetch;
  vi.stubGlobal('fetch', vi.fn((url: string, init?: RequestInit) => url.endsWith('/scope-options') ? Promise.resolve({ ok: true, status: 200, json: async () => ({ documents: [{ id: 'trusted', fileName: 'Selected.md' }], collections: [{ id: 'collection', name: 'Final documents' }] }) } as Response) : original(url, init)));
  render(<BatchPreviewPanel />);
  await screen.findByRole('option', { name: 'Office' });
  fireEvent.change(screen.getByLabelText('Workspace'), { target: { value: 'w1' } });
  fireEvent.change(screen.getByLabelText('Scope'), { target: { value: '0' } });
  fireEvent.click(await screen.findByLabelText('Selected.md'));
  fireEvent.change(screen.getByLabelText('Exact find'), { target: { value: 'needle' } });
  fireEvent.click(screen.getByRole('button', { name: 'Preview' }));
  await screen.findByText('Completed: 100%');
  expect(JSON.parse(requests.find(r => r.url.endsWith('/preview'))!.init!.body as string).scope.documentIds).toEqual(['trusted']);
  fireEvent.change(screen.getByLabelText('Scope'), { target: { value: '3' } });
  fireEvent.change(screen.getByLabelText('Collection'), { target: { value: 'collection' } });
  fireEvent.change(screen.getByLabelText('Workspace'), { target: { value: '' } });
  expect(screen.getByLabelText('Collection')).toHaveValue('');
});
it.each(['close', 'restart'])('keeps failed-poll remote activity cancellable on %s', async action => {
  getPlan = { ...completed, status: 'Running', progress: 40 };
  const original = globalThis.fetch;
  let reads = 0;
  vi.stubGlobal('fetch', vi.fn((url: string, init?: RequestInit) => {
    if (url.endsWith('/p1') && ++reads > 1) return Promise.resolve({ ok: false, status: 503 } as Response);
    return original(url, init);
  }));
  await start(); await screen.findByText('Running: 40%');
  await screen.findByText('Batch request failed (503).');
  expect(screen.getByRole('button', { name: 'Retry preview status' })).toBeEnabled();
  if (action === 'close') {
    const { cleanup } = await import('@testing-library/react'); cleanup();
  } else {
    fireEvent.click(screen.getByRole('button', { name: 'Preview' }));
    await new Promise(resolve => setTimeout(resolve, 0));
  }
  expect(requests.some(r => r.url.endsWith('/p1/cancel'))).toBe(true);
  if (action === 'restart') {
    const cancelIndex = requests.findIndex(r => r.url.endsWith('/p1/cancel'));
    const previewIndices = requests.flatMap((r, i) => r.url.endsWith('/preview') ? [i] : []);
    expect(previewIndices).toHaveLength(2);
    expect(cancelIndex).toBeLessThan(previewIndices[1]);
  }
});
it('recovers a failed poll through retry without replacing the plan', async () => {
  getPlan = { ...completed, status: 'Running', progress: 40 };
  const original = globalThis.fetch;
  let reads = 0;
  vi.stubGlobal('fetch', vi.fn((url: string, init?: RequestInit) => url.endsWith('/p1') && ++reads === 2 ? Promise.resolve({ ok: false, status: 503 } as Response) : original(url, init)));
  await start(); await screen.findByText('Batch request failed (503).');
  getPlan = completed;
  fireEvent.click(screen.getByRole('button', { name: 'Retry preview status' }));
  await screen.findByText('Completed: 100%');
  expect(requests.filter(r => r.url.endsWith('/preview'))).toHaveLength(1);
});
it('blocks replacement when predecessor cancellation fails', async () => {
  getPlan = { ...completed, status: 'Running', progress: 40 };
  const original = globalThis.fetch;
  let reads = 0;
  vi.stubGlobal('fetch', vi.fn((url: string, init?: RequestInit) => (url.endsWith('/p1') && ++reads > 1) || url.endsWith('/cancel') ? Promise.resolve({ ok: false, status: 503 } as Response) : original(url, init)));
  await start(); await screen.findByText('Batch request failed (503).');
  fireEvent.click(screen.getByRole('button', { name: 'Preview' }));
  await new Promise(resolve => setTimeout(resolve, 0));
  expect(requests.filter(r => r.url.endsWith('/preview'))).toHaveLength(1);
  expect(screen.getByRole('button', { name: 'Cancel preview' })).toBeEnabled();
});
it('sends neutral exclusion fields and marks them not applicable when switching Folder to Workspace', async () => {
  getPlan = { ...completed, scope: { ...completed.scope, type: 2, folderPath: null, excludedBranches: [], includeSubfolders: true, includedSubfolders: [], subfolderCount: 0 } };
  render(<BatchPreviewPanel />);
  await screen.findByRole('option', { name: 'Office' });
  fireEvent.change(screen.getByLabelText('Workspace'), { target: { value: 'w1' } });
  fireEvent.change(screen.getByLabelText('Scope'), { target: { value: '1' } });
  fireEvent.change(screen.getByLabelText('Root folder'), { target: { value: 'root' } });
  fireEvent.change(screen.getByLabelText('Excluded branches (one per line)'), { target: { value: 'Archive' } });
  fireEvent.change(screen.getByLabelText('Exact find'), { target: { value: 'needle' } });
  fireEvent.change(screen.getByLabelText('Scope'), { target: { value: '2' } });
  expect(screen.queryByLabelText('Excluded branches (one per line)')).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole('button', { name: 'Preview' }));
  await screen.findByText('Workspace: Office');
  const request = requests.find(r => r.url.endsWith('/preview'))!;
  expect(JSON.parse(request.init!.body as string).scope).toMatchObject({ type: 2, includeSubfolders: true, excludedBranches: [] });
  expect(screen.getByText('Include subfolders: Not applicable')).toBeInTheDocument();
  expect(screen.getByText('Excluded branches: Not applicable')).toBeInTheDocument();
});
it('cancels the returned plan exactly once when initial creation resolves after close', async () => {
  const original = globalThis.fetch;
  let finishCreate!: (response: Response) => void;
  vi.stubGlobal('fetch', vi.fn((url: string, init?: RequestInit) => {
    if (url.endsWith('/preview')) return new Promise<Response>(resolve => { finishCreate = resolve; });
    return original(url, init);
  }));
  const view = render(<BatchPreviewPanel />);
  await screen.findByRole('option', { name: 'Office' });
  fireEvent.change(screen.getByLabelText('Workspace'), { target: { value: 'w1' } });
  fireEvent.change(screen.getByLabelText('Scope'), { target: { value: '2' } });
  fireEvent.change(screen.getByLabelText('Exact find'), { target: { value: 'needle' } });
  fireEvent.click(screen.getByRole('button', { name: 'Preview' }));
  await new Promise(resolve => setTimeout(resolve, 0));
  view.unmount();
  finishCreate({ ok: true, status: 202, json: async () => ({ planId: 'p9', jobId: 'j9' }) } as Response);
  await new Promise(resolve => setTimeout(resolve, 0));
  expect(requests.filter(r => r.url.endsWith('/p9/cancel') && r.init?.method === 'POST')).toHaveLength(1);
  expect(requests.filter(r => r.url.endsWith('/p9'))).toHaveLength(0);
  const count = requests.length;
  await new Promise(resolve => setTimeout(resolve, 800));
  expect(requests).toHaveLength(count);
});
it('does not start a replacement after closing during predecessor cancellation', async () => {
  getPlan = { ...completed, status: 'Running', progress: 40 };
  const original = globalThis.fetch;
  let reads = 0;
  let finishCancel!: (response: Response) => void;
  vi.stubGlobal('fetch', vi.fn((url: string, init?: RequestInit) => {
    if (url.endsWith('/p1') && ++reads > 1) return Promise.resolve({ ok: false, status: 503 } as Response);
    if (url.endsWith('/cancel') && !finishCancel) return new Promise<Response>(resolve => { finishCancel = resolve; });
    return original(url, init);
  }));
  await start(); await screen.findByText('Batch request failed (503).');
  fireEvent.click(screen.getByRole('button', { name: 'Preview' }));
  const { cleanup } = await import('@testing-library/react'); cleanup();
  finishCancel({ ok: true, status: 204 } as Response);
  await new Promise(resolve => setTimeout(resolve, 0));
  expect(requests.filter(r => r.url.endsWith('/preview'))).toHaveLength(1);
});
