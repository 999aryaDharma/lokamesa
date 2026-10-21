import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  flushForRestart,
  installRestartFlushBridge,
  registerRestartFlush,
  type RestartFlushResult,
} from './markdownRestartFlush';

type BridgeWindow = Window & {
  __DOCUMENTIDE_PREPARE_RESTART__: () => Promise<RestartFlushResult>;
};

describe('markdown restart flush', () => {
  const cleanups: Array<() => void> = [];

  afterEach(() => {
    while (cleanups.length > 0) cleanups.pop()!();
    vi.restoreAllMocks();
  });

  it('returns ok when no editor is dirty', async () => {
    await expect(flushForRestart()).resolves.toEqual({ ok: true });
  });

  it('runs every registered handler and reports the first failure', async () => {
    const order: string[] = [];
    cleanups.push(
      registerRestartFlush(async () => {
        order.push('first');
        return { ok: false, reason: 'locked' };
      }),
    );
    cleanups.push(
      registerRestartFlush(async () => {
        order.push('second');
        return { ok: true };
      }),
    );

    await expect(flushForRestart()).resolves.toEqual({ ok: false, reason: 'locked' });
    expect(order).toEqual(['first', 'second']);
  });

  it('turns a thrown handler into a failure instead of rejecting', async () => {
    cleanups.push(
      registerRestartFlush(async () => {
        throw new Error('network down');
      }),
    );

    await expect(flushForRestart()).resolves.toEqual({ ok: false, reason: 'network down' });
  });

  it('installs the window bridge', async () => {
    const target = {} as BridgeWindow;
    installRestartFlushBridge(target);
    await expect(target.__DOCUMENTIDE_PREPARE_RESTART__()).resolves.toEqual({ ok: true });
  });
});
