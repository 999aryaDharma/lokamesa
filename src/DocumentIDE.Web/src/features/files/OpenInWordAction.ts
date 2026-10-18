export type WordLaunchStatus =
  | 'Success'
  | 'WordNotInstalled'
  | 'DocumentNotFound'
  | 'InvalidFileType'
  | 'ComError';

export interface WordLaunchResult {
  success: boolean;
  status: WordLaunchStatus;
  errorMessage: string;
  documentId?: string;
  filePath?: string;
}

export async function openInWord(documentId: string): Promise<WordLaunchResult> {
  if (!documentId) {
    return {
      success: false,
      status: 'DocumentNotFound',
      errorMessage: 'Document ID is required.',
    };
  }

  const baseUrl = (window as any).__DOCUMENTIDE_API_URL__ || '';
  const token = (window as any).__DOCUMENTIDE_SESSION_TOKEN__ || '';

  try {
    const response = await fetch(`${baseUrl}/api/word/open`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'X-DocumentIDE-Session-Token': token,
      },
      body: JSON.stringify({ documentId }),
    });

    if (response.status === 503) {
      let errorMessage = 'Microsoft Word is required for this operation.';
      try {
        const body = await response.json();
        if (body && body.errorMessage) {
          errorMessage = body.errorMessage;
        }
      } catch {
        // Fallback message if body parsing fails
      }

      return {
        success: false,
        status: 'WordNotInstalled',
        errorMessage,
        documentId,
      };
    }

    const data: WordLaunchResult = await response.json();
    return data;
  } catch (err) {
    return {
      success: false,
      status: 'ComError',
      errorMessage: err instanceof Error ? err.message : 'Network error while attempting to open Microsoft Word.',
      documentId,
    };
  }
}
