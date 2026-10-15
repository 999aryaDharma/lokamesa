import { useState } from 'react';

export interface MergeOrderFile {
  id: string;
  name: string;
}

export interface MergeOrderDialogProps {
  files: MergeOrderFile[];
  /**
   * Explicit user order: file ids in merge sequence. This component renders
   * exactly this order and only changes it through user drag/button actions.
   * It never sorts, infers, or otherwise reorders silently (spec 09 §6-§7).
   */
  order: string[];
  onOrderChange: (nextOrder: string[]) => void;
  onConfirm: (orderedIds: string[]) => void;
  onCancel?: () => void;
}

function resolveVisibleOrder(files: MergeOrderFile[], order: string[]): MergeOrderFile[] {
  const byId = new Map(files.map((file) => [file.id, file]));
  const visible = order
    .map((id) => byId.get(id))
    .filter((file): file is MergeOrderFile => file !== undefined);
  return visible;
}

export function MergeOrderDialog({
  files,
  order,
  onOrderChange,
  onConfirm,
  onCancel,
}: MergeOrderDialogProps) {
  const [draggedId, setDraggedId] = useState<string | null>(null);
  const visible = resolveVisibleOrder(files, order);

  const move = (fromIndex: number, toIndex: number) => {
    if (fromIndex === toIndex) {
      return;
    }
    const next = [...order];
    const [moved] = next.splice(fromIndex, 1);
    next.splice(toIndex, 0, moved);
    onOrderChange(next);
  };

  const indexOf = (id: string) => order.indexOf(id);

  return (
    <section aria-label="Merge document order" role="dialog">
      <p>
        {visible.length} of {files.length} documents in merge order
      </p>
      <ol>
        {visible.map((file, visibleIndex) => (
          <li
            key={file.id}
            aria-label={`${visibleIndex + 1}. ${file.name}`}
            data-testid={`merge-order-item-${file.id}`}
            draggable
            onDragStart={(event) => {
              setDraggedId(file.id);
              event.dataTransfer.effectAllowed = 'move';
            }}
            onDragEnd={() => setDraggedId(null)}
            onDragOver={(event) => {
              event.preventDefault();
              event.dataTransfer.dropEffect = 'move';
            }}
            onDrop={(event) => {
              event.preventDefault();
              if (draggedId === null || draggedId === file.id) {
                return;
              }
              move(indexOf(draggedId), indexOf(file.id));
              setDraggedId(null);
            }}
          >
            <span>
              {visibleIndex + 1}. {file.name}
            </span>
            <button
              aria-label={`Move ${file.name} up`}
              disabled={visibleIndex === 0}
              onClick={() => {
                const current = indexOf(file.id);
                move(current, current - 1);
              }}
              type="button"
            >
              Up
            </button>
            <button
              aria-label={`Move ${file.name} down`}
              disabled={visibleIndex === visible.length - 1}
              onClick={() => {
                const current = indexOf(file.id);
                move(current, current + 1);
              }}
              type="button"
            >
              Down
            </button>
          </li>
        ))}
      </ol>
      <button onClick={() => onConfirm([...order])} type="button">
        Merge in this order
      </button>
      {onCancel ? (
        <button onClick={onCancel} type="button">
          Cancel
        </button>
      ) : null}
    </section>
  );
}
