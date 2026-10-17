/**
 * SHR-11 — Owner share panel (gap G5 container).
 *
 * Mounts the presentational `ShareDialog`/`ShareManager` on top of the real
 * privileged share-lifecycle API (`shareApi.ts`) so the owner can actually
 * create a share and manage its lifecycle (stop / revoke / regenerate link,
 * add / remove explicit members). This is the missing product path REL-03
 * recorded as G5 ("callbacks unwired; no create/stop/revoke endpoints").
 *
 * Safety:
 *   - Nothing is published until the owner clicks Create share (the dialog
 *     holds the intent; this container only forwards it on the explicit click).
 *   - Membership stays explicit: adding a member requires the manager's
 *     submit action; the backend never auto-expands the set (SHR-05).
 *   - Only the public share URL is copied/displayed; the share token is never
 *     rendered (SHR-11).
 */
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import {
  addShareMember as defaultAddShareMember,
  createShare as defaultCreateShare,
  listShares as defaultListShares,
  regenerateShare as defaultRegenerateShare,
  removeShareMember as defaultRemoveShareMember,
  revokeShare as defaultRevokeShare,
  stopShare as defaultStopShare,
} from './shareApi';
import { ShareDialog, type ShareDialogDocument } from './ShareDialog';
import { ShareManager, type ShareManagementState } from './ShareManager';

export interface SharePanelApi {
  listShares: typeof defaultListShares;
  createShare: typeof defaultCreateShare;
  stopShare: typeof defaultStopShare;
  revokeShare: typeof defaultRevokeShare;
  regenerateShare: typeof defaultRegenerateShare;
  addShareMember: typeof defaultAddShareMember;
  removeShareMember: typeof defaultRemoveShareMember;
}

const defaultApi: SharePanelApi = {
  listShares: defaultListShares,
  createShare: defaultCreateShare,
  stopShare: defaultStopShare,
  revokeShare: defaultRevokeShare,
  regenerateShare: defaultRegenerateShare,
  addShareMember: defaultAddShareMember,
  removeShareMember: defaultRemoveShareMember,
};

export interface SharePanelProps {
  /** Documents selectable as explicit share members. */
  availableDocuments: ShareDialogDocument[];
  /** Pre-checked members for a new share (owner's current selection). */
  initialSelectedIds?: string[];
  onClose?: () => void;
  onCopyLink?: (shareUrl: string) => void;
  /** Injectable lifecycle API for the shell/tests; defaults to `shareApi`. */
  api?: Partial<SharePanelApi>;
}

function errorMessage(error: unknown, fallback: string): string {
  return error instanceof Error ? error.message : fallback;
}

export function SharePanel({
  availableDocuments,
  initialSelectedIds,
  onClose,
  onCopyLink,
  api,
}: SharePanelProps) {
  const deps = useMemo<SharePanelApi>(() => ({ ...defaultApi, ...api }), [api]);
  const [shares, setShares] = useState<ShareManagementState[]>([]);
  const [selectedShareId, setSelectedShareId] = useState<string | null>(null);
  const [isCreating, setIsCreating] = useState(false);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const mounted = useRef(true);

  useEffect(() => {
    mounted.current = true;
    deps
      .listShares()
      .then((loaded) => {
        if (!mounted.current) return;
        setShares(loaded);
        if (loaded.length > 0) {
          setSelectedShareId(loaded[0].shareId);
        } else {
          setIsCreating(true);
        }
      })
      .catch((err: unknown) => {
        if (!mounted.current) return;
        setError(errorMessage(err, 'Failed to load shares.'));
      })
      .finally(() => {
        if (mounted.current) setIsLoading(false);
      });
    return () => {
      mounted.current = false;
    };
    // Load once on mount; `deps` is rebuilt each render but the API is stable.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const activeShare = shares.find((share) => share.shareId === selectedShareId) ?? null;

  const replaceShare = useCallback((next: ShareManagementState) => {
    setShares((current) => {
      const exists = current.some((share) => share.shareId === next.shareId);
      return exists
        ? current.map((share) => (share.shareId === next.shareId ? next : share))
        : [...current, next];
    });
  }, []);

  const handleCreate = useCallback(
    async (payload: Parameters<typeof defaultCreateShare>[0]) => {
      setError(null);
      try {
        const created = await deps.createShare(payload);
        if (!mounted.current) return;
        replaceShare(created);
        setSelectedShareId(created.shareId);
        setIsCreating(false);
      } catch (err: unknown) {
        if (mounted.current) setError(errorMessage(err, 'Failed to create share.'));
      }
    },
    [deps, replaceShare],
  );

  const runLifecycle = useCallback(
    (action: (shareId: string) => Promise<ShareManagementState>, fallback: string) => {
      if (!activeShare) return;
      setError(null);
      action(activeShare.shareId)
        .then((next) => {
          if (mounted.current) replaceShare(next);
        })
        .catch((err: unknown) => {
          if (mounted.current) setError(errorMessage(err, fallback));
        });
    },
    [activeShare, replaceShare],
  );

  const handleCopyLink = useCallback(
    (shareUrl: string) => {
      onCopyLink?.(shareUrl);
      if (!onCopyLink && typeof navigator !== 'undefined' && navigator.clipboard) {
        void navigator.clipboard.writeText(shareUrl).catch(() => {
          /* clipboard may be unavailable; the URL is still visible */
        });
      }
    },
    [onCopyLink],
  );

  return (
    <section
      data-testid="share-panel"
      aria-label="Share management"
      className="w-96 max-w-full bg-white dark:bg-slate-900 border-r border-slate-200 dark:border-slate-800 flex flex-col h-full overflow-y-auto"
    >
      <header className="flex items-center justify-between px-3 py-2 border-b border-slate-200">
        <h2 className="text-sm font-semibold text-slate-800">Shares</h2>
        <div className="flex items-center gap-2">
          {!isCreating && (
            <button
              type="button"
              onClick={() => {
                setError(null);
                setIsCreating(true);
              }}
              className="text-xs px-2 py-0.5 rounded border border-slate-300 hover:bg-slate-100"
            >
              New share
            </button>
          )}
          {onClose && (
            <button
              type="button"
              aria-label="Close shares"
              onClick={onClose}
              className="text-slate-400 hover:text-slate-600 text-lg leading-none"
            >
              &times;
            </button>
          )}
        </div>
      </header>

      {error && (
        <p role="alert" className="px-3 py-2 text-xs text-red-700 bg-red-50 border-b border-red-200">
          {error}
        </p>
      )}

      <div className="p-3">
        {isLoading ? (
          <p className="text-xs text-slate-400">Loading shares…</p>
        ) : isCreating ? (
          <ShareDialog
            availableDocuments={availableDocuments}
            initialSelectedIds={initialSelectedIds}
            onCreate={(payload) => void handleCreate(payload)}
            onCancel={() => {
              setIsCreating(false);
              setError(null);
            }}
          />
        ) : activeShare ? (
          <ShareManager
            share={activeShare}
            onStop={() => runLifecycle(deps.stopShare, 'Failed to stop share.')}
            onRevoke={() => runLifecycle(deps.revokeShare, 'Failed to revoke share.')}
            onRegenerate={() => runLifecycle(deps.regenerateShare, 'Failed to regenerate share link.')}
            onCopyLink={handleCopyLink}
            onAddMember={(documentId) =>
              runLifecycle(
                (shareId) => deps.addShareMember(shareId, documentId),
                'Failed to add share member.',
              )
            }
            onRemoveMember={(documentId) =>
              runLifecycle(
                (shareId) => deps.removeShareMember(shareId, documentId),
                'Failed to remove share member.',
              )
            }
          />
        ) : (
          <p className="text-xs text-slate-400">No shares yet.</p>
        )}
      </div>
    </section>
  );
}

export default SharePanel;
