import { render, screen, fireEvent } from '@testing-library/react';
import { expect, it, vi } from 'vitest';
import { ShareManager, resolveShareLinkAccess, mapTransportStateToShareStatus } from './ShareManager';
import type { ShareManagementState } from './ShareManager';

it('user can see exact members', () => {
  render(
    <ShareManager
      share={{
        shareId: 'share-1',
        mode: 'Live',
        status: 'active',
        shareToken: 'token-abc',
        shareUrl: 'https://example.ngrok.io/share/token-abc',
        revoked: false,
        allowOriginalDownload: false,
        allowPdfDownload: false,
        members: [
          { documentId: 'doc-a', displayName: 'Alpha.md' },
          { documentId: 'doc-b', displayName: 'Beta.docx' },
        ],
      }}
    />,
  );

  expect(screen.getByText('Alpha.md')).toBeInTheDocument();
  expect(screen.getByText('Beta.docx')).toBeInTheDocument();
});

it('shows stopped status when the share is stopped', () => {
  render(
    <ShareManager
      share={{
        shareId: 'share-1',
        mode: 'Live',
        status: 'stopped',
        shareToken: 'token-abc',
        shareUrl: 'https://example.ngrok.io/share/token-abc',
        revoked: true,
        allowOriginalDownload: false,
        allowPdfDownload: false,
        members: [{ documentId: 'doc-a', displayName: 'Alpha.md' }],
      }}
    />,
  );

  expect(screen.getByText(/stopped/i)).toBeInTheDocument();
});

it('shows tunnel-error status with the tunnel detail', () => {
  render(
    <ShareManager
      share={{
        shareId: 'share-1',
        mode: 'Live',
        status: 'tunnel-error',
        shareToken: 'token-abc',
        shareUrl: 'https://example.ngrok.io/share/token-abc',
        revoked: false,
        allowOriginalDownload: false,
        allowPdfDownload: false,
        tunnelError: 'ngrok unavailable',
        members: [{ documentId: 'doc-a', displayName: 'Alpha.md' }],
      }}
    />,
  );

  expect(screen.getByText(/tunnel/i)).toBeInTheDocument();
  expect(screen.getByText(/ngrok unavailable/i)).toBeInTheDocument();
});

function activeShare(): ShareManagementState {
  return {
    shareId: 'share-1',
    mode: 'Live',
    status: 'active',
    shareToken: 'token-abc',
    shareUrl: 'https://example.ngrok.io/share/token-abc',
    revoked: false,
    allowOriginalDownload: false,
    allowPdfDownload: false,
    members: [{ documentId: 'doc-a', displayName: 'Alpha.md' }],
  };
}

it('adding a member requires an explicit action (typing alone adds nothing)', () => {
  const onAddMember = vi.fn();
  render(<ShareManager share={{ ...activeShare() }} onAddMember={onAddMember} />);

  fireEvent.change(screen.getByLabelText(/add member/i), {
    target: { value: 'doc-new' },
  });

  expect(onAddMember).not.toHaveBeenCalled();
  expect(screen.queryByText('doc-new')).not.toBeInTheDocument();

  fireEvent.click(screen.getByRole('button', { name: /add member/i }));

  expect(onAddMember).toHaveBeenCalledTimes(1);
  expect(onAddMember).toHaveBeenCalledWith('doc-new');
});

it('removing a member requires an explicit remove action', () => {
  const onRemoveMember = vi.fn();
  render(
    <ShareManager share={{ ...activeShare() }} onRemoveMember={onRemoveMember} />,
  );

  expect(onRemoveMember).not.toHaveBeenCalled();

  fireEvent.click(screen.getByRole('button', { name: /remove alpha/i }));

  expect(onRemoveMember).toHaveBeenCalledTimes(1);
  expect(onRemoveMember).toHaveBeenCalledWith('doc-a');
});

/**
 * Backend contract (no backend change in this sprint):
 * - `ShareTokenService.VerifyTokenForShare` returns false for an unknown
 *   share, an empty token, or a fixed-time hash mismatch.
 * - Spec §17: a stopped share rejects its token ("old link must no longer
 *   access content"); §18: regenerate must not reactivate the old token.
 * `resolveShareLinkAccess` mirrors that decision table owner-side so the UI
 * can prove a revoked link fails before the viewer ever probes the server.
 */
it('revoked link fails', () => {
  const revoked = { ...activeShare(), revoked: true, status: 'stopped' as const };

  expect(resolveShareLinkAccess(revoked, 'token-abc').allowed).toBe(false);
  expect(resolveShareLinkAccess(revoked, 'token-abc').reason).toBe('revoked');
});

it('stopped share rejects its token and a wrong token never grants access', () => {
  const stopped = { ...activeShare(), status: 'stopped' as const };

  expect(resolveShareLinkAccess(stopped, 'token-abc').allowed).toBe(false);
  expect(resolveShareLinkAccess(stopped, 'token-abc').reason).toBe('stopped');
  expect(resolveShareLinkAccess({ ...activeShare() }, 'wrong-token').allowed).toBe(
    false,
  );
});

it('active share with the current token allows access', () => {
  const decision = resolveShareLinkAccess({ ...activeShare() }, 'token-abc');

  expect(decision).toEqual({ allowed: true, reason: 'ok' });
});

it('maps SHR-09 transport states to owner-visible status', () => {
  expect(mapTransportStateToShareStatus('Running')).toBe('active');
  expect(mapTransportStateToShareStatus('Starting')).toBe('active');
  expect(mapTransportStateToShareStatus('Stopped')).toBe('stopped');
  expect(mapTransportStateToShareStatus('Failed')).toBe('tunnel-error');
});

it('stopping a share requires an explicit stop action', () => {
  const onStop = vi.fn();
  render(<ShareManager share={{ ...activeShare() }} onStop={onStop} />);

  expect(onStop).not.toHaveBeenCalled();

  fireEvent.click(screen.getByRole('button', { name: /^stop$/i }));

  expect(onStop).toHaveBeenCalledTimes(1);
});

it('copy link, revoke, and regenerate require explicit button actions', () => {
  const onCopyLink = vi.fn();
  const onRevoke = vi.fn();
  const onRegenerate = vi.fn();
  render(
    <ShareManager
      share={{ ...activeShare() }}
      onCopyLink={onCopyLink}
      onRevoke={onRevoke}
      onRegenerate={onRegenerate}
    />,
  );

  expect(screen.getByText('https://example.ngrok.io/share/token-abc')).toBeInTheDocument();
  expect(onCopyLink).not.toHaveBeenCalled();
  expect(onRevoke).not.toHaveBeenCalled();
  expect(onRegenerate).not.toHaveBeenCalled();

  fireEvent.click(screen.getByRole('button', { name: /copy link/i }));
  fireEvent.click(screen.getByRole('button', { name: /^revoke$/i }));
  fireEvent.click(screen.getByRole('button', { name: /regenerate/i }));

  expect(onCopyLink).toHaveBeenCalledTimes(1);
  expect(onCopyLink).toHaveBeenCalledWith('https://example.ngrok.io/share/token-abc');
  expect(onRevoke).toHaveBeenCalledTimes(1);
  expect(onRegenerate).toHaveBeenCalledTimes(1);
});
