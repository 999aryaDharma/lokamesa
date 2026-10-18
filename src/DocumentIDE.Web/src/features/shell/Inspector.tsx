import React from 'react';
import type { CanvasObject } from '../canvas/types';
import type { MainView } from '../workspace/viewStore';
import InspectorCard, { InspectorField } from './InspectorCard';
import NodeIcon, { documentIconKind, type NodeIconKind } from './NodeIcon';

/**
 * UI-01 shell — right inspector dock (260px).
 *
 * Built only from data this app actually has: the canvas selection, the
 * workspace object counts, and the graph store's node/edge counts. Nothing here
 * invents metadata, and nothing here mutates state — the dock is a read-only
 * view of the stores the shell already owns.
 *
 * Deliberate scope note (recorded in the UI-01 handoff): the richer
 * `RelationsInspector` (canvas/screen 01) and `DocxInspector` (screen 06) are
 * restyled but **not mounted**. Neither has a data source in this app — there is
 * no relations endpoint and no document-metadata endpoint wired to the web
 * surface — so mounting them would mean fabricating their contents. The
 * inspector therefore shows what is real: selection, workspace counts and graph
 * scope.
 */
export interface InspectorProps {
  activeView: MainView;
  objects: ReadonlyArray<CanvasObject>;
  selection: ReadonlyArray<CanvasObject>;
  /** From `useGraphStore`, when the graph view is active. */
  graphNodeCount?: number;
  graphEdgeCount?: number;
}

function iconKindFor(object: CanvasObject): NodeIconKind {
  return object.type === 'folder' ? 'folder' : documentIconKind(object.title, object.docType);
}

function typeLabel(kind: NodeIconKind): string {
  if (kind === 'folder') return 'Folder';
  if (kind === 'docx') return 'DOCX';
  if (kind === 'md') return 'MD';
  return 'FILE';
}

const EDGE_LEGEND: ReadonlyArray<{ label: string; color: string }> = [
  { label: 'Explicit relation', color: 'var(--color-mac-accent)' },
  { label: 'Inferred (semantic)', color: 'var(--color-mac-success)' },
  { label: 'Missing resource', color: 'var(--color-mac-warning)' },
];

const ObjectReferenceRow: React.FC<{ object: CanvasObject }> = ({ object }) => {
  const kind = iconKindFor(object);
  return (
    <div className="flex items-center gap-2 rounded-md border border-mac-hairline bg-mac-content px-2 py-1.5">
      <NodeIcon kind={kind} size={14} />
      <span className="min-w-0 flex-1 truncate text-xs text-mac-text" title={object.title}>
        {object.title}
      </span>
    </div>
  );
};

export const Inspector: React.FC<InspectorProps> = ({
  activeView,
  objects,
  selection,
  graphNodeCount = 0,
  graphEdgeCount = 0,
}) => {
  const documents = objects.filter((object) => object.type === 'document');
  const missingCount = documents.filter((object) => object.isMissing).length;
  const readyCount = documents.length - missingCount;
  const folderCount = objects.length - documents.length;

  const single = selection.length === 1 ? selection[0] : undefined;

  return (
    <aside
      aria-label="Inspector"
      onKeyDown={(event) => event.stopPropagation()}
      className="flex h-full w-[260px] shrink-0 flex-col gap-4 overflow-y-auto border-l border-mac-hairline bg-mac-inspector px-3 py-3"
    >
      <header className="flex items-center justify-between">
        <h2 className="text-[11px] font-semibold text-mac-text">Inspector</h2>
        <span className="text-[10px] tabular-nums text-mac-text-3">
          {objects.length} items
        </span>
      </header>

      {activeView === 'graph' && (
        <>
          <InspectorCard title="Scope" testId="inspector-scope">
            <InspectorField label="Nodes" value={graphNodeCount} />
            <InspectorField label="Edges" value={graphEdgeCount} />
          </InspectorCard>

          <InspectorCard title="Edge legend" testId="inspector-edge-legend">
            {EDGE_LEGEND.map((entry) => (
              <div key={entry.label} className="flex items-center gap-2 py-0.5 text-xs">
                <span
                  aria-hidden="true"
                  className="h-0.5 w-4 shrink-0 rounded-full"
                  style={{ background: entry.color }}
                />
                <span className="truncate text-mac-text-2">{entry.label}</span>
              </div>
            ))}
          </InspectorCard>
        </>
      )}

      {activeView !== 'graph' && selection.length > 0 && (
        <InspectorCard title="Selection" testId="inspector-selection">
          {selection.map((object) => (
            <ObjectReferenceRow key={object.id} object={object} />
          ))}
          {single ? (
            <>
              <InspectorField label="Type" value={typeLabel(iconKindFor(single))} />
              {single.statusText && (
                <InspectorField
                  label="Status"
                  value={single.statusText}
                  tone={single.isMissing ? 'warning' : 'success'}
                />
              )}
              {single.isMissing && (
                <InspectorField label="Availability" value="Missing" tone="warning" />
              )}
            </>
          ) : (
            <InspectorField label="Selected" value={selection.length} />
          )}
        </InspectorCard>
      )}

      {activeView !== 'graph' && selection.length === 0 && (
        <InspectorCard title="Workspace state" testId="inspector-workspace">
          <InspectorField label="Folders" value={folderCount} />
          <InspectorField label="Documents" value={documents.length} />
          <InspectorField
            label="Ready"
            value={readyCount}
            tone={readyCount > 0 ? 'success' : 'default'}
          />
          <InspectorField
            label="Missing"
            value={missingCount}
            tone={missingCount > 0 ? 'warning' : 'default'}
          />
        </InspectorCard>
      )}
    </aside>
  );
};

export default Inspector;
