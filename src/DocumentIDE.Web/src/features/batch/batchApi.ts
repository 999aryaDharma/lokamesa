export interface BatchScope {
  workspaceId: string; type: number; documentIds: string[]; folderPath: string | null;
  includeSubfolders: boolean; excludedBranches: string[]; collectionId: string | null;
}
export interface Preview {
  planId: string; jobId: string; status: 'Pending' | 'Running' | 'Completed' | 'Cancelled' | 'Failed' | 'Expired';
  progress: number; errorCode: string | null;
  scope: BatchScope & { includedSubfolders: string[]; subfolderCount: number; failures: { documentId: string; failureCode: string }[] };
  filesScanned: number; filesAffected: number; occurrenceCount: number;
  breakdown: { body: number; table: number; header: number; footer: number };
  files: { documentId: string; fileName: string; selected: boolean; lockState: string; failureCode: string | null;
    occurrences: { id: string; origin: 'Body' | 'Table' | 'Header' | 'Footer'; context: string; selected: boolean }[] }[];
}
export async function batchRequest<T>(path: string, init: RequestInit = {}): Promise<T> {
  const globals = window as Window & { __DOCUMENTIDE_API_URL__?: string; __DOCUMENTIDE_SESSION_TOKEN__?: string };
  const response = await fetch(`${globals.__DOCUMENTIDE_API_URL__ || ''}/api/batch-operations${path}`, {
    ...init, headers: { 'Content-Type': 'application/json', 'X-DocumentIDE-Session-Token': globals.__DOCUMENTIDE_SESSION_TOKEN__ || '' },
  });
  if (!response.ok) throw new Error(response.status === 409 || response.status === 410 ? 'Plan is stale or expired. Re-preview required.' : `Batch request failed (${response.status}).`);
  return response.status === 204 ? undefined as T : response.json();
}
