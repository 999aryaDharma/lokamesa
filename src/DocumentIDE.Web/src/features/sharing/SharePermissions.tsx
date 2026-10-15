export interface ShareDownloadPermissions {
  allowOriginalDownload: boolean;
  allowPdfDownload: boolean;
}

export interface SharePermissionsProps {
  /** Owner-chosen policy. Both flags default to OFF when omitted. */
  permissions?: Partial<ShareDownloadPermissions>;
  onChange?: (permissions: ShareDownloadPermissions) => void;
}

/**
 * SHR-07 — owner-side download permission toggles.
 *
 * Both permissions default OFF. This UI only records owner intent: enforcement
 * happens server-side in ShareHost (OFF routes return 403 and no artifact
 * bytes are prepared), so hiding viewer buttons is never the boundary.
 */
export function SharePermissions({ permissions, onChange }: SharePermissionsProps) {
  const allowOriginalDownload = permissions?.allowOriginalDownload ?? false;
  const allowPdfDownload = permissions?.allowPdfDownload ?? false;

  const emit = (next: ShareDownloadPermissions) => {
    onChange?.(next);
  };

  return (
    <section aria-label="Share download permissions" role="region">
      <label>
        <input
          checked={allowOriginalDownload}
          onChange={(event) =>
            emit({ allowOriginalDownload: event.target.checked, allowPdfDownload })
          }
          type="checkbox"
        />
        Allow original download
      </label>
      <label>
        <input
          checked={allowPdfDownload}
          onChange={(event) =>
            emit({ allowOriginalDownload, allowPdfDownload: event.target.checked })
          }
          type="checkbox"
        />
        Allow PDF download
      </label>
      <p>Downloads are off by default. Viewers only receive files the owner enables here.</p>
    </section>
  );
}

export interface ShareDownloadButtonsProps {
  /** Frozen share policy from the manifest. */
  permissions: ShareDownloadPermissions;
  originalDownloadUrl?: string | null;
  pdfDownloadUrl?: string | null;
}

/**
 * SHR-07 — viewer-side download buttons. Buttons for disabled downloads are
 * omitted entirely (never rendered disabled). Direct-URL access is still
 * denied server-side by ShareHost.
 */
export function ShareDownloadButtons({
  permissions,
  originalDownloadUrl,
  pdfDownloadUrl,
}: ShareDownloadButtonsProps) {
  const showOriginal = permissions.allowOriginalDownload && !!originalDownloadUrl;
  const showPdf = permissions.allowPdfDownload && !!pdfDownloadUrl;

  if (!showOriginal && !showPdf) {
    return null;
  }

  return (
    <nav aria-label="Shared document downloads">
      {showOriginal ? <a href={originalDownloadUrl ?? ''}>Download original</a> : null}
      {showPdf ? <a href={pdfDownloadUrl ?? ''}>Download PDF</a> : null}
    </nav>
  );
}
