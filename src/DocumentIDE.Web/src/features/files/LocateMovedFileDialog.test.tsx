import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import LocateMovedFileDialog from './LocateMovedFileDialog';

describe('LocateMovedFileDialog Component', () => {
  const defaultProps = {
    isOpen: true,
    documentId: 'doc-123',
    filename: 'test_document.md',
    lastKnownPath: 'C:\\Users\\User\\Documents\\test_document.md',
    onClose: vi.fn(),
    onRebound: vi.fn(),
  };

  beforeEach(() => {
    vi.restoreAllMocks();
    delete (window as any).__DOCUMENTIDE_API_URL__;
    delete (window as any).__DOCUMENTIDE_SESSION_TOKEN__;
  });

  it('renders nothing when isOpen is false', () => {
    const { container } = render(<LocateMovedFileDialog {...defaultProps} isOpen={false} />);
    expect(container.firstChild).toBeNull();
  });

  it('renders document details and fetches candidates on mount', async () => {
    const mockCandidates = [
      {
        targetPath: 'C:\\Users\\User\\Desktop\\test_document.md',
        filename: 'test_document.md',
        sizeBytes: 150,
        modifiedAt: '2026-08-10T12:00:00Z',
        contentFingerprint: 'hash123',
        matchScore: 100,
        exactFingerprintMatch: true,
        sameFilenameMatch: true,
        matchReason: 'Exact content fingerprint and filename match',
        isAmbiguous: false,
      },
    ];

    vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce({
      ok: true,
      json: async () => ({
        documentId: 'doc-123',
        lastKnownPath: defaultProps.lastKnownPath,
        isAmbiguous: false,
        candidates: mockCandidates,
      }),
    } as Response);

    render(<LocateMovedFileDialog {...defaultProps} />);

    expect(screen.getByText(/locate moved file/i)).toBeInTheDocument();
    expect(screen.getByText('test_document.md')).toBeInTheDocument();

    await waitFor(() => {
      expect(screen.getByText('C:\\Users\\User\\Desktop\\test_document.md')).toBeInTheDocument();
      expect(screen.getByText(/score: 100%/i)).toBeInTheDocument();
    });
  });

  it('displays ambiguity warning banner when isAmbiguous is true', async () => {
    const mockCandidates = [
      {
        targetPath: 'C:\\Dir1\\test_document.md',
        filename: 'test_document.md',
        sizeBytes: 150,
        modifiedAt: '2026-08-10T12:00:00Z',
        contentFingerprint: 'hash123',
        matchScore: 100,
        exactFingerprintMatch: true,
        sameFilenameMatch: true,
        matchReason: 'Exact content fingerprint match',
        isAmbiguous: true,
      },
      {
        targetPath: 'C:\\Dir2\\test_document.md',
        filename: 'test_document.md',
        sizeBytes: 150,
        modifiedAt: '2026-08-10T12:00:00Z',
        contentFingerprint: 'hash123',
        matchScore: 100,
        exactFingerprintMatch: true,
        sameFilenameMatch: true,
        matchReason: 'Exact content fingerprint match',
        isAmbiguous: true,
      },
    ];

    vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce({
      ok: true,
      json: async () => ({
        documentId: 'doc-123',
        lastKnownPath: defaultProps.lastKnownPath,
        isAmbiguous: true,
        candidates: mockCandidates,
      }),
    } as Response);

    render(<LocateMovedFileDialog {...defaultProps} />);

    await waitFor(() => {
      expect(screen.getByText(/ambiguous file match/i)).toBeInTheDocument();
    });
  });

  it('submits rebind request and triggers callbacks on confirm click', async () => {
    const mockCandidates = [
      {
        targetPath: 'C:\\Users\\User\\Desktop\\test_document.md',
        filename: 'test_document.md',
        sizeBytes: 150,
        modifiedAt: '2026-08-10T12:00:00Z',
        contentFingerprint: 'hash123',
        matchScore: 100,
        exactFingerprintMatch: true,
        sameFilenameMatch: true,
        matchReason: 'Exact match',
        isAmbiguous: false,
      },
    ];

    let postPayload: any = null;

    vi.spyOn(globalThis, 'fetch').mockImplementation(async (url, init) => {
      if (typeof url === 'string' && url.includes('/rebind-candidates')) {
        return {
          ok: true,
          json: async () => ({
            documentId: 'doc-123',
            lastKnownPath: defaultProps.lastKnownPath,
            isAmbiguous: false,
            candidates: mockCandidates,
          }),
        } as Response;
      }
      if (typeof url === 'string' && url.includes('/rebind')) {
        postPayload = JSON.parse(init?.body as string);
        return {
          ok: true,
          json: async () => ({
            documentId: 'doc-123',
            previousPath: defaultProps.lastKnownPath,
            newPath: 'C:\\Users\\User\\Desktop\\test_document.md',
            availabilityStatus: 'available',
            success: true,
          }),
        } as Response;
      }
      return { ok: false } as Response;
    });

    render(<LocateMovedFileDialog {...defaultProps} />);

    const confirmButton = await screen.findByRole('button', { name: /confirm rebind/i });
    expect(confirmButton).not.toBeDisabled();

    fireEvent.click(confirmButton);

    await waitFor(() => {
      expect(postPayload).toEqual({
        targetPath: 'C:\\Users\\User\\Desktop\\test_document.md',
        forceRebind: true,
      });
      expect(defaultProps.onRebound).toHaveBeenCalledWith('C:\\Users\\User\\Desktop\\test_document.md');
      expect(defaultProps.onClose).toHaveBeenCalled();
    });
  });

  it('reaches the loopback API with the host session token (never a relative unauthenticated fetch)', async () => {
    (window as any).__DOCUMENTIDE_API_URL__ = 'http://127.0.0.1:5311';
    (window as any).__DOCUMENTIDE_SESSION_TOKEN__ = 'session-token-abc';

    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValue({
      ok: true,
      json: async () => ({
        documentId: 'doc-123',
        lastKnownPath: defaultProps.lastKnownPath,
        isAmbiguous: false,
        candidates: [],
      }),
    } as Response);

    render(<LocateMovedFileDialog {...defaultProps} />);

    await waitFor(() => expect(fetchMock).toHaveBeenCalled());

    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe('http://127.0.0.1:5311/api/documents/doc-123/rebind-candidates');
    expect(((init?.headers ?? {}) as Record<string, string>)['X-DocumentIDE-Session-Token']).toBe(
      'session-token-abc',
    );
  });

  it('sends the session token on the rebind mutation too', async () => {
    (window as any).__DOCUMENTIDE_API_URL__ = 'http://127.0.0.1:5311';
    (window as any).__DOCUMENTIDE_SESSION_TOKEN__ = 'session-token-abc';

    const fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation(async (url) => {
      if (String(url).endsWith('/rebind-candidates')) {
        return {
          ok: true,
          json: async () => ({
            documentId: 'doc-123',
            lastKnownPath: defaultProps.lastKnownPath,
            isAmbiguous: false,
            candidates: [
              {
                targetPath: 'C:\\Users\\User\\Desktop\\test_document.md',
                filename: 'test_document.md',
                sizeBytes: 150,
                modifiedAt: '2026-08-10T12:00:00Z',
                contentFingerprint: 'hash123',
                matchScore: 100,
                exactFingerprintMatch: true,
                sameFilenameMatch: true,
                matchReason: 'Exact match',
                isAmbiguous: false,
              },
            ],
          }),
        } as Response;
      }
      return {
        ok: true,
        json: async () => ({
          documentId: 'doc-123',
          previousPath: defaultProps.lastKnownPath,
          newPath: 'C:\\Users\\User\\Desktop\\test_document.md',
          availabilityStatus: 'available',
          success: true,
        }),
      } as Response;
    });

    render(<LocateMovedFileDialog {...defaultProps} />);
    fireEvent.click(await screen.findByRole('button', { name: /confirm rebind/i }));

    await waitFor(() =>
      expect(fetchMock.mock.calls.some(([url]) => String(url).endsWith('/rebind'))).toBe(true),
    );
    const rebindCall = fetchMock.mock.calls.find(([url]) => String(url).endsWith('/rebind'))!;
    expect(String(rebindCall[0])).toBe('http://127.0.0.1:5311/api/documents/doc-123/rebind');
    expect(
      ((rebindCall[1]?.headers ?? {}) as Record<string, string>)[
        'X-DocumentIDE-Session-Token'
      ],
    ).toBe('session-token-abc');
  });

  it('calls onClose when cancel button is clicked', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce({
      ok: true,
      json: async () => ({
        documentId: 'doc-123',
        lastKnownPath: defaultProps.lastKnownPath,
        isAmbiguous: false,
        candidates: [],
      }),
    } as Response);

    render(<LocateMovedFileDialog {...defaultProps} />);

    const cancelButton = await screen.findByRole('button', { name: /cancel/i });
    await waitFor(() => {
      expect(screen.getByText(/no candidate files automatically detected/i)).toBeInTheDocument();
    });

    fireEvent.click(cancelButton);

    expect(defaultProps.onClose).toHaveBeenCalled();
  });

  it('allows custom file path selection and rebind submission', async () => {
    let postPayload: any = null;

    vi.spyOn(globalThis, 'fetch').mockImplementation(async (url, init) => {
      if (typeof url === 'string' && url.includes('/rebind-candidates')) {
        return {
          ok: true,
          json: async () => ({
            documentId: 'doc-123',
            lastKnownPath: defaultProps.lastKnownPath,
            isAmbiguous: false,
            candidates: [],
          }),
        } as Response;
      }
      if (typeof url === 'string' && url.includes('/rebind')) {
        postPayload = JSON.parse(init?.body as string);
        return {
          ok: true,
          json: async () => ({
            documentId: 'doc-123',
            previousPath: defaultProps.lastKnownPath,
            newPath: 'C:\\CustomPath\\custom_file.md',
            availabilityStatus: 'available',
            success: true,
          }),
        } as Response;
      }
      return { ok: false } as Response;
    });

    render(<LocateMovedFileDialog {...defaultProps} />);

    await waitFor(() => {
      expect(screen.getByText(/no candidate files automatically detected/i)).toBeInTheDocument();
    });

    const customRadio = screen.getByLabelText(/select custom file path/i);
    fireEvent.click(customRadio);

    const customInput = screen.getByPlaceholderText(/enter full target file path/i);
    fireEvent.change(customInput, { target: { value: 'C:\\CustomPath\\custom_file.md' } });

    const confirmButton = screen.getByRole('button', { name: /confirm rebind/i });
    expect(confirmButton).not.toBeDisabled();

    fireEvent.click(confirmButton);

    await waitFor(() => {
      expect(postPayload).toEqual({
        targetPath: 'C:\\CustomPath\\custom_file.md',
        forceRebind: true,
      });
      expect(defaultProps.onRebound).toHaveBeenCalledWith('C:\\CustomPath\\custom_file.md');
      expect(defaultProps.onClose).toHaveBeenCalled();
    });
  });
});
