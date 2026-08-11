import React, { useEffect, useState } from 'react';

export interface RebindCandidateDto {
  targetPath: string;
  filename: string;
  sizeBytes: number;
  modifiedAt: string;
  contentFingerprint: string;
  matchScore: number;
  exactFingerprintMatch: boolean;
  sameFilenameMatch: boolean;
  matchReason: string;
  isAmbiguous: boolean;
}

export interface RebindCandidateListDto {
  documentId: string;
  lastKnownPath: string;
  isAmbiguous: boolean;
  candidates: RebindCandidateDto[];
}

export interface LocateMovedFileDialogProps {
  isOpen: boolean;
  documentId: string;
  filename: string;
  lastKnownPath: string;
  onClose: () => void;
  onRebound?: (newPath: string) => void;
}

export const LocateMovedFileDialog: React.FC<LocateMovedFileDialogProps> = ({
  isOpen,
  documentId,
  filename,
  lastKnownPath,
  onClose,
  onRebound,
}) => {
  const [isLoading, setIsLoading] = useState<boolean>(false);
  const [candidates, setCandidates] = useState<RebindCandidateDto[]>([]);
  const [isAmbiguous, setIsAmbiguous] = useState<boolean>(false);
  const [selectedTargetPath, setSelectedTargetPath] = useState<string | null>(null);
  const [customPath, setCustomPath] = useState<string>('');
  const [isSubmitting, setIsSubmitting] = useState<boolean>(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  useEffect(() => {
    if (!isOpen || !documentId) return;

    const fetchCandidates = async () => {
      setIsLoading(true);
      setErrorMessage(null);
      try {
        const response = await fetch(`/api/documents/${documentId}/rebind-candidates`);
        if (!response.ok) {
          throw new Error(`Failed to fetch candidates (Status: ${response.status})`);
        }
        const data: RebindCandidateListDto = await response.json();
        setCandidates(data.candidates || []);
        setIsAmbiguous(data.isAmbiguous || false);
        if (data.candidates && data.candidates.length > 0) {
          setSelectedTargetPath(data.candidates[0].targetPath);
        } else {
          setSelectedTargetPath(null);
        }
      } catch (err: any) {
        setErrorMessage(err.message || 'Error loading candidate files.');
      } finally {
        setIsLoading(false);
      }
    };

    fetchCandidates();
  }, [isOpen, documentId]);

  if (!isOpen) return null;

  const activePath = selectedTargetPath === 'custom' ? customPath.trim() : selectedTargetPath;
  const canConfirm = Boolean(activePath && activePath.length > 0) && !isSubmitting;

  const handleConfirm = async () => {
    if (!activePath) return;

    setIsSubmitting(true);
    setErrorMessage(null);

    try {
      const response = await fetch(`/api/documents/${documentId}/rebind`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({
          targetPath: activePath,
          forceRebind: true,
        }),
      });

      if (!response.ok) {
        const errData = await response.json().catch(() => ({}));
        throw new Error(errData.error || 'Failed to rebind document path.');
      }

      if (onRebound) {
        onRebound(activePath);
      }
      onClose();
    } catch (err: any) {
      setErrorMessage(err.message || 'An error occurred while confirming rebind.');
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <div
      role="dialog"
      aria-labelledby="locate-dialog-title"
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 p-4"
    >
      <div className="w-full max-w-2xl rounded-lg bg-white p-6 shadow-xl dark:bg-slate-800 dark:text-white">
        <h2 id="locate-dialog-title" className="text-xl font-bold text-slate-900 dark:text-white">
          Locate Moved File
        </h2>

        <div className="mt-3 space-y-1 text-sm text-slate-600 dark:text-slate-300">
          <p>
            <span className="font-semibold">Document:</span> {filename}
          </p>
          <p className="break-all">
            <span className="font-semibold">Last Known Path:</span> {lastKnownPath}
          </p>
        </div>

        {errorMessage && (
          <div className="mt-4 rounded border border-red-300 bg-red-50 p-3 text-sm text-red-700 dark:border-red-800 dark:bg-red-950 dark:text-red-300">
            {errorMessage}
          </div>
        )}

        {(isAmbiguous || candidates.length > 1) && (
          <div className="mt-4 flex items-start gap-2 rounded border border-amber-300 bg-amber-50 p-3 text-sm text-amber-800 dark:border-amber-800 dark:bg-amber-950 dark:text-amber-200">
            <span className="text-base">⚠️</span>
            <div>
              <p className="font-semibold">Ambiguous File Match</p>
              <p>
                Multiple candidate files match this document's content or metadata. Please verify and select the correct file to rebind.
              </p>
            </div>
          </div>
        )}

        <div className="mt-4 max-h-60 overflow-y-auto space-y-2">
          {isLoading ? (
            <div className="py-8 text-center text-sm text-slate-500">Searching for candidate files...</div>
          ) : candidates.length === 0 ? (
            <div className="py-4 text-sm text-slate-500">No candidate files automatically detected.</div>
          ) : (
            candidates.map((cand) => (
              <label
                key={cand.targetPath}
                className={`flex cursor-pointer items-start gap-3 rounded border p-3 transition-colors ${
                  selectedTargetPath === cand.targetPath
                    ? 'border-blue-500 bg-blue-50/50 dark:bg-blue-950/30'
                    : 'border-slate-200 dark:border-slate-700 hover:bg-slate-50 dark:hover:bg-slate-750'
                }`}
              >
                <input
                  type="radio"
                  name="candidate-selection"
                  value={cand.targetPath}
                  checked={selectedTargetPath === cand.targetPath}
                  onChange={() => setSelectedTargetPath(cand.targetPath)}
                  className="mt-1"
                />
                <div className="flex-1 text-xs">
                  <div className="flex items-center justify-between">
                    <span className="font-semibold text-sm text-slate-800 dark:text-slate-100">{cand.filename}</span>
                    <span className="rounded bg-blue-100 px-2 py-0.5 font-medium text-blue-800 dark:bg-blue-900 dark:text-blue-200">
                      Score: {cand.matchScore}%
                    </span>
                  </div>
                  <p className="mt-1 break-all text-slate-600 dark:text-slate-300">{cand.targetPath}</p>
                  <p className="mt-1 text-slate-500 italic">{cand.matchReason}</p>
                </div>
              </label>
            ))
          )}

          <div className="mt-3 rounded border border-slate-200 p-3 dark:border-slate-700">
            <label className="flex items-center gap-2 text-sm font-medium">
              <input
                type="radio"
                name="candidate-selection"
                value="custom"
                checked={selectedTargetPath === 'custom'}
                onChange={() => setSelectedTargetPath('custom')}
              />
              Select Custom File Path
            </label>
            {selectedTargetPath === 'custom' && (
              <input
                type="text"
                value={customPath}
                onChange={(e) => setCustomPath(e.target.value)}
                placeholder="Enter full target file path..."
                className="mt-2 w-full rounded border border-slate-300 p-2 text-sm dark:border-slate-600 dark:bg-slate-900 dark:text-white"
              />
            )}
          </div>
        </div>

        <div className="mt-6 flex justify-end gap-3">
          <button
            type="button"
            onClick={onClose}
            disabled={isSubmitting}
            className="rounded border border-slate-300 px-4 py-2 text-sm font-medium text-slate-700 hover:bg-slate-100 dark:border-slate-600 dark:text-slate-200 dark:hover:bg-slate-700"
          >
            Cancel
          </button>
          <button
            type="button"
            onClick={handleConfirm}
            disabled={!canConfirm}
            className="rounded bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50"
          >
            {isSubmitting ? 'Rebinding...' : 'Confirm Rebind'}
          </button>
        </div>
      </div>
    </div>
  );
};

export default LocateMovedFileDialog;
