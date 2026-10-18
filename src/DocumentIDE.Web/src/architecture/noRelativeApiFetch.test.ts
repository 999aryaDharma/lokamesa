import { describe, expect, it } from 'vitest';

/**
 * The packaged desktop app serves the SPA from the WebView virtual host
 * `https://appassets.html2sav`, so a relative `/api/...` fetch never reaches
 * the loopback API; the browser reports it as "Failed to fetch". It only works
 * behind the Vite dev-server proxy, which is why this class of bug passed every
 * unit test and still broke the shipped build (FS-01 attach folder and search).
 *
 * Every API call must go through `window.__DOCUMENTIDE_API_URL__`.
 */
const sources = import.meta.glob('../**/*.{ts,tsx}', {
  query: '?raw',
  import: 'default',
  eager: true,
}) as Record<string, string>;

const RELATIVE_API_FETCH = /fetch\(\s*(?:`|'|")\/api/;

describe('shipped API calls', () => {
  it('never fetches a relative /api path', () => {
    const files = Object.entries(sources).filter(([path]) => !/\.test\./.test(path));

    // Guard against a vacuous pass if the glob ever stops resolving.
    expect(files.length).toBeGreaterThan(50);

    const offenders = files.flatMap(([path, text]) =>
      text.split(/\r?\n/).flatMap((line, index) =>
        RELATIVE_API_FETCH.test(line) ? [`${path}:${index + 1}: ${line.trim()}`] : [],
      ),
    );

    expect(offenders).toEqual([]);
  });
});
