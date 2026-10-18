import { render, screen, within, fireEvent } from '@testing-library/react';
import { expect, it, vi } from 'vitest';
import SearchReplaceReview from './SearchReplaceReview';
import type { Preview } from './batchApi';

const preview: Preview = {
  planId: 'p1', jobId: 'j1', status: 'Completed', progress: 100, errorCode: null,
  scope: { workspaceId: 'w1', type: 1, documentIds: [], folderPath: 'root', includeSubfolders: true, excludedBranches: ['excluded'], collectionId: null, includedSubfolders: ['child'], subfolderCount: 1, failures: [] },
  filesScanned: 2, filesAffected: 1, occurrenceCount: 4,
  breakdown: { body: 1, table: 1, header: 1, footer: 1 },
  files: [{ documentId: 'd1', fileName: 'one.docx', selected: true, lockState: 'Unknown', failureCode: null,
    occurrences: (['Body', 'Table', 'Header', 'Footer'] as const).map((origin, i) => ({ id: `o${i}`, origin, context: `context-${origin}`, selected: true })) },
    { documentId: 'd2', fileName: 'bad.docx', selected: false, lockState: 'Unknown', failureCode: 'InvalidPackage', occurrences: [] }],
};

function renderReview(overrides: Partial<Parameters<typeof SearchReplaceReview>[0]> = {}) {
  const onEdit = vi.fn();
  render(<SearchReplaceReview preview={preview} find="needle" replacement="new-text" editable scopeLabel="Folder" workspaceName="Office" collectionLabel="None" documentName={id => id} onEdit={onEdit} {...overrides} />);
  return onEdit;
}

it('Header/table labels are visible', () => {
  renderReview();
  const file = screen.getByRole('heading', { name: 'one.docx' }).parentElement!;
  expect(within(file).getAllByRole('heading').map(h => h.textContent)).toEqual(['one.docx', 'Body', 'Table', 'Header', 'Footer']);
});
it('shows the context snippet and old to new preview for each occurrence', () => {
  renderReview();
  const row = screen.getByLabelText('Header occurrence o2').closest('label')!;
  expect(row).toHaveTextContent('context-Header');
  expect(row).toHaveTextContent('needle → new-text');
});
it('Scope tree matches resolver', () => {
  renderReview();
  const tree = screen.getByRole('tree', { name: 'Recursive folder scope' });
  const items = within(tree).getAllByRole('treeitem');
  expect(items.map(item => item.textContent)).toEqual(['root', 'child', 'excluded (excluded)']);
  expect(items.map(item => item.getAttribute('aria-level'))).toEqual(['1', '2', '2']);
});
it('renders no recursive scope tree outside folder scope', () => {
  renderReview({ preview: { ...preview, scope: { ...preview.scope, type: 2, folderPath: null, includeSubfolders: true, excludedBranches: [], includedSubfolders: [], subfolderCount: 0 } } });
  expect(screen.queryByRole('tree')).not.toBeInTheDocument();
  expect(screen.getByText('Include subfolders: Not applicable')).toBeInTheDocument();
  expect(screen.getByText('Excluded branches: Not applicable')).toBeInTheDocument();
});
it('supports select/deselect of file or occurrence', () => {
  const onEdit = renderReview();
  fireEvent.click(screen.getByLabelText('Select one.docx'));
  expect(onEdit).toHaveBeenCalledWith('d1');
  fireEvent.click(screen.getByLabelText('Header occurrence o2'));
  expect(onEdit).toHaveBeenCalledWith('d1', 'o2');
});
it('keeps a failed file non-editable while other files stay reviewable', () => {
  renderReview();
  expect(screen.getByLabelText('Select bad.docx')).toBeDisabled();
  expect(screen.getByLabelText('Select one.docx')).toBeEnabled();
  expect(screen.getByText('File not applied')).toBeInTheDocument();
});
it('disables every review control when the preview is not editable', () => {
  renderReview({ editable: false });
  const boxes = screen.getAllByRole('checkbox');
  expect(boxes.length).toBeGreaterThan(1);
  expect(boxes.every(box => (box as HTMLInputElement).disabled)).toBe(true);
});
it('renders exactly the files and occurrences the preview returned without expanding scope', () => {
  renderReview();
  expect(screen.getAllByRole('checkbox').map(box => box.getAttribute('aria-label'))).toEqual([
    'Select one.docx', 'Body occurrence o0', 'Table occurrence o1', 'Header occurrence o2', 'Footer occurrence o3', 'Select bad.docx',
  ]);
});
