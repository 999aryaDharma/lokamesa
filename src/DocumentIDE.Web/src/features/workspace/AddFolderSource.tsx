import React, { useState, useEffect } from 'react';

export interface FolderSource {
  id: string;
  workspaceId: string;
  canonicalPath: string;
  displayName: string;
  availabilityStatus: string;
}

export interface AttachedFolderCard {
  folderSource: FolderSource;
  supportedFileCount: number;
}

interface AddFolderSourceProps {
  workspaceId: string;
  /** UI-03: optional path prefilled by the worktree attachment signal. */
  initialPath?: string;
  onFolderAttached?: (card: AttachedFolderCard) => void;
}

export default function AddFolderSource({
  workspaceId,
  initialPath = '',
  onFolderAttached,
}: AddFolderSourceProps) {
  const [folderPath, setFolderPath] = useState(initialPath);
  const [isLoading, setIsLoading] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [attachedSources, setAttachedSources] = useState<AttachedFolderCard[]>([]);

  // The worktree attachment signal may arrive after this panel mounted.
  useEffect(() => {
    setFolderPath(initialPath);
  }, [initialPath]);

  // Load existing folder sources on mount
  useEffect(() => {
    let isMounted = true;
    async function loadFolderSources() {
      try {
        const response = await fetch(`/api/workspaces/${workspaceId}/folder-sources`);
        if (response.ok) {
          const data: FolderSource[] = await response.json();
          if (isMounted) {
            setAttachedSources(
              data.map((fs) => ({
                folderSource: fs,
                supportedFileCount: 0, // Loaded from list endpoint
              }))
            );
          }
        }
      } catch {
        // API server may not be reachable in isolated unit tests
      }
    }
    loadFolderSources();
    return () => {
      isMounted = false;
    };
  }, [workspaceId]);

  const handleAttach = async (e: React.FormEvent) => {
    e.preventDefault();
    setErrorMessage(null);

    const trimmedPath = folderPath.trim();
    if (!trimmedPath) {
      setErrorMessage('Please enter a folder path');
      return;
    }

    setIsLoading(true);

    try {
      const response = await fetch(`/api/workspaces/${workspaceId}/folder-sources`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({ folderPath: trimmedPath }),
      });

      const data = await response.json();

      if (!response.ok) {
        setErrorMessage(data.error || 'Failed to attach folder source');
        return;
      }

      const card: AttachedFolderCard = {
        folderSource: data.folderSource,
        supportedFileCount: data.supportedFileCount,
      };

      setAttachedSources((prev) => [...prev, card]);
      setFolderPath('');
      if (onFolderAttached) {
        onFolderAttached(card);
      }
    } catch (err) {
      setErrorMessage(err instanceof Error ? err.message : 'An unexpected error occurred');
    } finally {
      setIsLoading(false);
    }
  };

  return (
    <div className="w-full max-w-xl p-4 bg-mac-content rounded-xl border border-mac-hairline shadow-sm space-y-4">
      <form onSubmit={handleAttach} className="flex flex-col gap-2">
        <label htmlFor="folder-path-input" className="text-sm font-semibold text-mac-text">
          Attach Local Folder Source
        </label>
        <div className="flex gap-2">
          <input
            id="folder-path-input"
            type="text"
            value={folderPath}
            onChange={(e) => setFolderPath(e.target.value)}
            placeholder="Enter local folder path (e.g. C:\MyDocuments)"
            disabled={isLoading}
            className="flex-1 px-3 py-2 text-sm border border-mac-hairline-strong rounded-lg focus:outline-none focus:ring-2 focus:ring-mac-accent disabled:opacity-50"
          />
          <button
            type="submit"
            disabled={isLoading}
            className="px-4 py-2 text-sm font-medium text-white bg-mac-accent rounded-lg hover:bg-mac-accent focus:outline-none focus:ring-2 focus:ring-mac-accent disabled:opacity-50 transition-colors"
          >
            {isLoading ? 'Attaching...' : 'Attach Folder'}
          </button>
        </div>
      </form>

      {errorMessage && (
        <div className="p-3 text-sm text-mac-danger bg-mac-danger/10 border border-mac-danger/40 rounded-lg" role="alert">
          {errorMessage}
        </div>
      )}

      {attachedSources.length > 0 && (
        <div className="space-y-2">
          <h4 className="text-xs font-semibold uppercase tracking-wider text-mac-text-3">
            Attached Folder Sources
          </h4>
          <div className="space-y-2">
            {attachedSources.map((card) => (
              <div
                key={card.folderSource.id}
                className="p-3 bg-mac-canvas border border-mac-hairline rounded-lg flex items-center justify-between"
              >
                <div className="flex flex-col min-w-0 pr-2">
                  <span className="text-sm font-medium text-mac-text truncate">
                    {card.folderSource.displayName || card.folderSource.canonicalPath}
                  </span>
                  <span className="text-xs font-mono text-mac-text-3 truncate">
                    {card.folderSource.canonicalPath}
                  </span>
                </div>
                <div className="flex items-center gap-3 shrink-0">
                  <span className="text-xs px-2 py-0.5 rounded bg-mac-accent-weak text-mac-accent font-medium border border-mac-accent/40">
                    {card.supportedFileCount} supported files
                  </span>
                  <span
                    className={`text-xs px-2 py-0.5 rounded font-medium ${
                      card.folderSource.availabilityStatus === 'available'
                        ? 'bg-emerald-50 text-emerald-700 border border-emerald-200'
                        : 'bg-mac-warning/10 text-mac-warning border border-mac-warning/40'
                    }`}
                  >
                    {card.folderSource.availabilityStatus}
                  </span>
                </div>
              </div>
            ))}
          </div>
        </div>
      )}
    </div>
  );
}
