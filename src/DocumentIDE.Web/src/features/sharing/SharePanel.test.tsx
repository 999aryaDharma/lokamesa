import { render, screen, fireEvent } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { SharePanel } from './SharePanel';
import type { ShareManagementState } from './ShareManager';

function activeShare(overrides: Partial<ShareManagementState> = {}): ShareManagementState {
  return {
    shareId: 'share-1',
    mode: 'Snapshot',
    status: 'active',
    shareToken: 'token-abc',
    shareUrl: 'https://example.ngrok.io/share/token-abc',
    revoked: false,
    allowOriginalDownload: false,
    allowPdfDownload: false,
    members: [{ documentId: 'doc-a', displayName: 'Alpha.md' }],
    ...overrides,
  };
}

function makeApi(overrides: Record<string, unknown> = {}) {
  return {
    listShares: vi.fn().mockResolvedValue([]),
    createShare: vi.fn().mockResolvedValue(activeShare()),
    stopShare: vi.fn().mockResolvedValue(activeShare({ status: 'stopped' })),
    revokeShare: vi.fn().mockResolvedValue(activeShare({ revoked: true, status: 'stopped' })),
    regenerateShare: vi.fn().mockResolvedValue(activeShare({ shareToken: 'token-new' })),
    addShareMember: vi.fn().mockResolvedValue(activeShare()),
    removeShareMember: vi.fn().mockResolvedValue(activeShare()),
    ...overrides,
  };
}

const availableDocuments = [
  { id: 'doc-a', displayName: 'Alpha.md' },
  { id: 'doc-b', displayName: 'Beta.docx' },
];

describe('SharePanel (SHR-11 owner surface, gap G5)', () => {
  it('loads existing shares and shows the manager with the share URL', async () => {
    const api = makeApi({ listShares: vi.fn().mockResolvedValue([activeShare()]) });
    render(<SharePanel availableDocuments={availableDocuments} api={api as never} />);

    expect(await screen.findByText('https://example.ngrok.io/share/token-abc')).toBeInTheDocument();
    expect(api.listShares).toHaveBeenCalledTimes(1);
  });

  it('opens the create dialog when there are no shares and creates only on explicit action', async () => {
    const api = makeApi();
    render(<SharePanel availableDocuments={availableDocuments} api={api as never} />);

    // Wait for the initial load, then the empty-state create dialog appears.
    const createButton = await screen.findByRole('button', { name: /create share/i });
    fireEvent.click(screen.getByLabelText('Alpha.md'));
    expect(api.createShare).not.toHaveBeenCalled();

    fireEvent.click(createButton);

    expect(api.createShare).toHaveBeenCalledTimes(1);
    expect(api.createShare).toHaveBeenCalledWith({
      mode: 'Snapshot',
      memberDocumentIds: ['doc-a'],
      allowOriginalDownload: false,
      allowPdfDownload: false,
    });
  });

  it('wires stop, revoke, regenerate, and member management to the lifecycle API', async () => {
    const api = makeApi({ listShares: vi.fn().mockResolvedValue([activeShare()]) });
    render(<SharePanel availableDocuments={availableDocuments} api={api as never} />);

    await screen.findByText('https://example.ngrok.io/share/token-abc');

    fireEvent.click(screen.getByRole('button', { name: /^stop$/i }));
    expect(api.stopShare).toHaveBeenCalledWith('share-1');

    fireEvent.click(screen.getByRole('button', { name: /^revoke$/i }));
    expect(api.revokeShare).toHaveBeenCalledWith('share-1');

    fireEvent.click(screen.getByRole('button', { name: /regenerate/i }));
    expect(api.regenerateShare).toHaveBeenCalledWith('share-1');

    fireEvent.change(screen.getByLabelText(/add member/i), { target: { value: 'doc-b' } });
    fireEvent.click(screen.getByRole('button', { name: /add member/i }));
    expect(api.addShareMember).toHaveBeenCalledWith('share-1', 'doc-b');

    fireEvent.click(screen.getByRole('button', { name: /remove alpha/i }));
    expect(api.removeShareMember).toHaveBeenCalledWith('share-1', 'doc-a');
  });

  it('surfaces a load failure as an alert instead of crashing', async () => {
    const api = makeApi({ listShares: vi.fn().mockRejectedValue(new Error('API offline')) });
    render(<SharePanel availableDocuments={availableDocuments} api={api as never} />);

    expect(await screen.findByRole('alert')).toHaveTextContent('API offline');
  });
});
