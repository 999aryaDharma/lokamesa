/**
 * HIST-04 — typed client for the read-only Document IDE operation history.
 *
 * The Operations tab only ever renders what these routes return:
 *   - `GET /api/history/operations?skip=&limit=`  (bounded page)
 *   - `GET /api/history/storage`                  (real snapshot bytes/capacity)
 *   - `POST /api/batch-operations/{planId}/undo`  (existing reviewed undo, unchanged)
 *
 * An `Unavailable` payload or a failed request throws, so `HistoryScreen` renders
 * its typed unavailable state instead of an empty list. Nothing here invents a
 * row, a count or a meter value.
 */

import type {
  OperationHistoryPayload,
  OperationHistorySource,
} from './HistoryScreen';
import type {
  OperationHistoryEntryView,
  OperationUndoView,
  OperationStatusView,
} from './HistoryPanel';
import type {
  UndoFileResultView,
  UndoOutcome,
  UndoResultView,
  UndoStatus,
} from './UndoConflictDialog';

interface HistoryApiGlobals {
  __DOCUMENTIDE_API_URL__?: string;
  __DOCUMENTIDE_SESSION_TOKEN__?: string;
}

/** Matches the SCM pagination convention: a bounded, newest-first page. */
export const HISTORY_OPERATIONS_PAGE_LIMIT = 50;

function apiUrl(): string {
  return (window as Window & HistoryApiGlobals).__DOCUMENTIDE_API_URL__ || '';
}

function sessionToken(): string {
  return (window as Window & HistoryApiGlobals).__DOCUMENTIDE_SESSION_TOKEN__ || '';
}

async function readJson<T>(response: Response, fallback: string): Promise<T> {
  const data: unknown = await response.json();
  if (!response.ok) {
    const rejection = data as { errorCode?: string; error?: string };
    const detail = rejection.error ?? `${fallback} (${response.status}).`;
    throw new Error(rejection.errorCode ? `${rejection.errorCode}: ${detail}` : detail);
  }
  return data as T;
}

interface OperationHistoryApiPage {
  status: 'Available' | 'Unavailable';
  entries?: OperationHistoryApiEntry[];
  skip?: number;
  limit?: number;
  totalCount?: number;
  hasMore?: boolean;
  nextSkip?: number | null;
  errorCode?: string;
  error?: string;
}

interface OperationHistoryApiEntry {
  operationId: string;
  planId: string;
  operationType: string;
  status: OperationStatusView;
  recordedUtc: string;
  startedUtc?: string;
  completedUtc?: string;
  affectedFileCount?: number;
  snapshotId: string | null;
  files: Array<{
    documentId: string;
    outcome: 'Success' | 'Skipped' | 'Failure';
    reasonCode: string | null;
    detail: string | null;
  }>;
  undo: {
    status: UndoStatus;
    files: Array<{
      documentId: string;
      outcome: UndoOutcome;
      reasonCode: string | null;
      detail: string | null;
    }>;
  } | null;
}

interface HistoryStorageApi {
  status: 'Available' | 'Unavailable';
  usedBytes?: number | null;
  capacityBytes?: number | null;
  errorCode?: string;
  error?: string;
}

interface UndoApiResult {
  operationId: string;
  snapshotId: string | null;
  status: UndoStatus;
  files: UndoFileResultView[];
}

/** Reads one bounded page plus the real storage meter, in parallel. */
export async function loadOperationHistory(): Promise<OperationHistoryPayload> {
  const params = new URLSearchParams({
    skip: '0',
    limit: String(HISTORY_OPERATIONS_PAGE_LIMIT),
  });
  const headers = { 'X-DocumentIDE-Session-Token': sessionToken() };

  const [pageResponse, storageResponse] = await Promise.all([
    fetch(`${apiUrl()}/api/history/operations?${params.toString()}`, { headers }),
    fetch(`${apiUrl()}/api/history/storage`, { headers }),
  ]);

  const page = await readJson<OperationHistoryApiPage>(
    pageResponse,
    'The operation history request failed',
  );
  if (page.status !== 'Available') {
    throw new Error(page.error ?? 'The operation history is unavailable.');
  }

  const storage = await readJson<HistoryStorageApi>(
    storageResponse,
    'The history storage request failed',
  );
  if (storage.status !== 'Available') {
    throw new Error(storage.error ?? 'The history storage usage is unavailable.');
  }
  if (typeof storage.usedBytes !== 'number' || typeof storage.capacityBytes !== 'number') {
    throw new Error('The history storage response did not include usage figures.');
  }

  const entries: OperationHistoryEntryView[] = (page.entries ?? []).map((entry) => {
    const undo: OperationUndoView | null = entry.undo
      ? {
          status: entry.undo.status,
          files: entry.undo.files.map((file) => ({
            documentId: file.documentId,
            outcome: file.outcome,
            reasonCode: file.reasonCode,
            detail: file.detail,
          })),
        }
      : null;

    return {
      operationId: entry.operationId,
      operationType: entry.operationType,
      recordedUtc: entry.recordedUtc,
      status: entry.status,
      snapshotId: entry.snapshotId,
      files: entry.files.map((file) => ({
        documentId: file.documentId,
        outcome: file.outcome,
        reasonCode: file.reasonCode,
        detail: file.detail,
      })),
      undo,
    };
  });

  return {
    entries,
    storageUsage: {
      usedBytes: storage.usedBytes,
      capacityBytes: storage.capacityBytes,
    },
  };
}

/** Calls the existing HIST-02 reviewed undo route; no force/overwrite parameter exists. */
export async function undoOperation(operationId: string): Promise<UndoResultView> {
  const response = await fetch(
    `${apiUrl()}/api/batch-operations/${encodeURIComponent(operationId)}/undo`,
    {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'X-DocumentIDE-Session-Token': sessionToken(),
      },
      body: '{}',
    },
  );

  const result = await readJson<UndoApiResult>(response, 'The undo request failed');
  return {
    operationId: result.operationId,
    snapshotId: result.snapshotId,
    status: result.status,
    files: result.files,
  };
}

/**
 * Stable module-level source. It holds no state and reads the host-injected
 * API URL/token per call, so the shell can pass it without re-triggering loads.
 */
export const operationHistorySource: OperationHistorySource = {
  load: loadOperationHistory,
  undo: undoOperation,
};

export function createOperationHistorySource(): OperationHistorySource {
  return { load: loadOperationHistory, undo: undoOperation };
}
