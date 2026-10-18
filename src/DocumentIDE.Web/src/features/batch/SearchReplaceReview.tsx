import type { Preview } from './batchApi';

export interface SearchReplaceReviewProps {
  preview: Preview;
  find: string;
  replacement: string;
  editable: boolean;
  scopeLabel: string;
  workspaceName: string;
  collectionLabel: string;
  documentName: (documentId: string) => string;
  onEdit: (documentId: string, occurrenceId?: string) => void;
}

const uniqueOrigins = (occurrences: Preview['files'][number]['occurrences']) =>
  occurrences.map(o => o.origin).filter((origin, index, all) => all.indexOf(origin) === index);

export default function SearchReplaceReview({ preview, find, replacement, editable, scopeLabel, workspaceName, collectionLabel, documentName, onEdit }: SearchReplaceReviewProps) {
  const scope = preview.scope;
  return <div>
    <p>{preview.filesScanned} files scanned; {preview.filesAffected} files affected; {preview.occurrenceCount} occurrences</p>
    {Object.entries(preview.breakdown).map(([origin, count]) => <p key={origin}>{origin[0].toUpperCase() + origin.slice(1)}: {count}</p>)}
    <h3>Resolved scope: {scopeLabel}</h3>
    <p>Workspace: {workspaceName}</p>
    <p>Documents: {scope.documentIds.map(documentName).join(', ') || 'None explicitly requested'}</p>
    <p>Collection: {collectionLabel}</p>
    <p>Include subfolders: {scope.type === 1 ? (scope.includeSubfolders ? 'On' : 'Off') : 'Not applicable'}</p>
    <p>Root folder: {scope.folderPath || 'Not a folder scope'}</p>
    <p>{scope.subfolderCount} included subfolders</p>
    {scope.type === 1 && <ul role="tree" aria-label="Recursive folder scope">
      <li role="treeitem" aria-level={1}>{scope.folderPath || 'No root folder'}</li>
      {scope.includedSubfolders.map(path => <li role="treeitem" aria-level={2} key={path}>{path}</li>)}
      {scope.excludedBranches.map(branch => <li role="treeitem" aria-level={2} key={`${branch}-excluded`}>{branch} (excluded)</li>)}
    </ul>}
    <p>Excluded branches: {scope.type === 1 ? (scope.excludedBranches.join(', ') || 'None') : 'Not applicable'}</p>
    {scope.failures.map((failure, i) => <p key={i}>Scope failure: {failure.failureCode} {failure.documentId}</p>)}
    {preview.files.map(file => <div key={file.documentId}>
      <h3>{file.fileName}</h3>
      <label><input type="checkbox" aria-label={`Select ${file.fileName}`} disabled={!editable || !!file.failureCode} checked={file.selected} onChange={() => onEdit(file.documentId)} />Select file</label>
      <p>Lock: {file.lockState}; Failure: {file.failureCode || 'None'}</p>
      {!file.selected && <p>File not applied</p>}
      {uniqueOrigins(file.occurrences).map(origin => <div key={origin}>
        <h4>{origin}</h4>
        {file.occurrences.filter(o => o.origin === origin).map(o => <label className="block" key={o.id}>
          <input type="checkbox" aria-label={`${o.origin} occurrence ${o.id}`} disabled={!editable || !!file.failureCode} checked={o.selected} onChange={() => onEdit(file.documentId, o.id)} />
          {o.origin}: {o.context}{' '}
          <span>{find} → {replacement || '(empty)'}</span>{' '}
          {!o.selected || !file.selected ? <span>Not applied</span> : null}
        </label>)}
      </div>)}
    </div>)}
  </div>;
}
