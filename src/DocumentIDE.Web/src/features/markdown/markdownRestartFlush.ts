/**
 * DEV-UPD — save-before-restart bridge.
 *
 * The desktop host injects `window.__DOCUMENTIDE_PREPARE_RESTART__`, calls it
 * before the auto-updater stops the process, and aborts the restart when the
 * result is `{ ok: false }`. Every dirty editor registers a handler here; the
 * handler saves through the same safe-write API as Ctrl+S, so a restart never
 * trades unsaved work for a newer binary.
 */

export interface RestartFlushResult {
  ok: boolean;
  reason?: string;
}

export type RestartFlushHandler = () => Promise<RestartFlushResult>;

export interface RestartFlushWindow extends Window {
  __DOCUMENTIDE_PREPARE_RESTART__?: () => Promise<RestartFlushResult>;
}

const handlers = new Set<RestartFlushHandler>();

/** Registers one handler; the returned function unregisters it. */
export function registerRestartFlush(handler: RestartFlushHandler): () => void {
  handlers.add(handler);
  return () => {
    handlers.delete(handler);
  };
}

/**
 * Runs every registered handler (even after a failure, so one locked document
 * does not hide another) and returns the first failure, or `{ ok: true }`.
 */
export async function flushForRestart(): Promise<RestartFlushResult> {
  let firstFailure: RestartFlushResult | undefined;

  for (const handler of Array.from(handlers)) {
    try {
      const result = await handler();
      if (!result.ok && !firstFailure) firstFailure = result;
    } catch (error: unknown) {
      if (!firstFailure) {
        firstFailure = {
          ok: false,
          reason: error instanceof Error ? error.message : String(error),
        };
      }
    }
  }

  return firstFailure ?? { ok: true };
}

/** Installs the bridge the desktop host polls during a restart. */
export function installRestartFlushBridge(target: Window = window): void {
  (target as RestartFlushWindow).__DOCUMENTIDE_PREPARE_RESTART__ = flushForRestart;
}
