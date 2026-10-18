export interface ShareMemberView {
  documentId: string;
  displayName: string;
}

export type ShareModeChoice = 'Snapshot' | 'Live';

export type ShareLifecycleStatus = 'active' | 'stopped' | 'tunnel-error';

/**
 * Owner-side view of a share (SHR-11).
 * Mirrors the backend contract: `Share.Members` is the explicit member set
 * (SHR-05 — live content updates never expand it) and download flags default
 * OFF (SHR-07). `revoked` models spec §17/§18 stop/revoke: a revoked or
 * stopped share rejects its old link.
 */
export interface ShareManagementState {
  shareId: string;
  mode: ShareModeChoice;
  status: ShareLifecycleStatus;
  shareToken: string;
  shareUrl: string;
  revoked: boolean;
  allowOriginalDownload: boolean;
  allowPdfDownload: boolean;
  members: ShareMemberView[];
  tunnelError?: string | null;
}

export type ShareLinkAccessReason =
  | 'ok'
  | 'revoked'
  | 'stopped'
  | 'tunnel-error'
  | 'token-mismatch';

export interface ShareLinkAccessDecision {
  allowed: boolean;
  reason: ShareLinkAccessReason;
}

/**
 * Owner-side mirror of the ShareHost access decision for a presented token.
 * Backend contract: `ShareTokenService.VerifyTokenForShare` returns false
 * for unknown shares, empty tokens, and fixed-time hash mismatches; spec §17
 * requires stopped shares to reject their token and §18 forbids reactivating
 * an old token after regenerate (modelled here as `revoked: true` with a
 * rotated `shareToken`). `tunnel-error` denies because the origin cannot
 * serve (spec §2/§27: share unavailable is the accepted behavior).
 */
export function resolveShareLinkAccess(
  share: ShareManagementState,
  presentedToken: string,
): ShareLinkAccessDecision {
  if (share.revoked) {
    return { allowed: false, reason: 'revoked' };
  }
  if (share.status === 'stopped') {
    return { allowed: false, reason: 'stopped' };
  }
  if (share.status === 'tunnel-error') {
    return { allowed: false, reason: 'tunnel-error' };
  }
  if (!presentedToken || presentedToken !== share.shareToken) {
    return { allowed: false, reason: 'token-mismatch' };
  }
  return { allowed: true, reason: 'ok' };
}

/**
 * Maps the backend `TransportStatusState` (IShareTransport, SHR-09) to the
 * owner-visible lifecycle status: Stopped -> stopped, Starting/Running ->
 * active, Failed -> tunnel-error.
 */
export function mapTransportStateToShareStatus(
  transportState: 'Stopped' | 'Starting' | 'Running' | 'Failed',
): ShareLifecycleStatus {
  switch (transportState) {
    case 'Running':
    case 'Starting':
      return 'active';
    case 'Failed':
      return 'tunnel-error';
    case 'Stopped':
    default:
      return 'stopped';
  }
}

export interface ShareManagerProps {
  share: ShareManagementState;
  onAddMember?: (documentId: string) => void;
  onRemoveMember?: (documentId: string) => void;
  onCopyLink?: (shareUrl: string) => void;
  /** Spec §17: stop serving (the share becomes `stopped` and rejects its link). */
  onStop?: () => void;
  onRevoke?: () => void;
  onRegenerate?: () => void;
}

export function ShareManager({
  share,
  onAddMember,
  onRemoveMember,
  onCopyLink,
  onStop,
  onRevoke,
  onRegenerate,
}: ShareManagerProps) {
  const statusLabel =
    share.status === 'active'
      ? 'Active'
      : share.status === 'stopped'
        ? 'Stopped'
        : 'Tunnel error';
  return (
    <section aria-label="Share manager" role="region">
      <p aria-label="Share status">{statusLabel}</p>
      {share.status === 'tunnel-error' && share.tunnelError ? (
        <p role="alert">{share.tunnelError}</p>
      ) : null}
      <ul aria-label="Share members">
        {share.members.map((member) => (
          <li key={member.documentId}>
            {member.displayName}
            <button
              aria-label={`Remove ${member.displayName}`}
              onClick={() => onRemoveMember?.(member.documentId)}
              type="button"
            >
              Remove
            </button>
          </li>
        ))}
      </ul>
      <AddMemberForm onAddMember={onAddMember} />
      <p>{share.shareUrl}</p>
      <button onClick={() => onCopyLink?.(share.shareUrl)} type="button">
        Copy link
      </button>
      <button onClick={() => onStop?.()} type="button">
        Stop
      </button>
      <button onClick={() => onRevoke?.()} type="button">
        Revoke
      </button>
      <button onClick={() => onRegenerate?.()} type="button">
        Regenerate link
      </button>
    </section>
  );
}

function AddMemberForm({ onAddMember }: { onAddMember?: (documentId: string) => void }) {
  return (
    <form
      onSubmit={(event) => {
        event.preventDefault();
        const data = new FormData(event.currentTarget);
        const value = (data.get('documentId') ?? '').toString().trim();
        if (value) {
          onAddMember?.(value);
        }
        event.currentTarget.reset();
      }}
    >
      <label htmlFor="share-manager-add-member">Add member</label>
      <input id="share-manager-add-member" name="documentId" type="text" />
      <button type="submit">Add member</button>
    </form>
  );
}
