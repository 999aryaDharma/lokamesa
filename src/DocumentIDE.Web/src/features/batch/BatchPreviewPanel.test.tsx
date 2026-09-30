import { render, screen, fireEvent } from '@testing-library/react';
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
let requests: { url: string; init?: RequestInit }[];
beforeEach(() => {
  requests = []; getPlan = completed; saveStatus = 200;
  Object.assign(window, { __DOCUMENTIDE_API_URL__: 'http://localhost:5000', __DOCUMENTIDE_SESSION_TOKEN__: 'secret' });
  vi.stubGlobal('fetch', vi.fn(async (url: string, init?: RequestInit) => {
    requests.push({ url, init });
    const data = url.endsWith('/workspaces') ? [{ id: 'w1', name: 'Office' }] : url.endsWith('/preview') ? { planId: 'p1', jobId: 'j1' } : getPlan;
    const status = url.endsWith('/selection') ? saveStatus : url.endsWith('/cancel') ? 204 : 200;
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
it('requests explicit authenticated scope and renders counts, origins, scope and failures without apply', async () => {
  await start();
  expect(await screen.findByText('2 files scanned; 1 files affected; 4 occurrences')).toBeInTheDocument();
  for (const origin of ['Body', 'Table', 'Header', 'Footer']) expect(screen.getByText(`${origin}: 1`)).toBeInTheDocument();
  expect(screen.getByText('child')).toBeInTheDocument();
  expect(screen.getByText(/InvalidPackage/)).toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Apply' })).toBeDisabled();
  expect(screen.getByText('Preview and review only. Applying changes is not available yet.')).toBeInTheDocument();
  const request = requests.find(r => r.url.endsWith('/preview'))!;
  expect(JSON.parse(request.init!.body as string).scope).toMatchObject({ workspaceId: 'w1', type: 1, includeSubfolders: true, folderPath: 'root' });
  expect(request.init!.headers).toMatchObject({ 'X-DocumentIDE-Session-Token': 'secret' });
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
  expect(await screen.findByText('Workspace ID: w1')).toBeInTheDocument();
  expect(screen.getByText('Document IDs: trusted-id')).toBeInTheDocument();
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
