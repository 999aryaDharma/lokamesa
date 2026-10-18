import { useState } from 'react';
import SheetShell from '../shell/SheetShell';
import PushButton from '../shell/PushButton';

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
    // `scrim={false}`: this sheet is rendered inline inside the already-mounted
    // PDF panel, so it must stay non-modal (a scrim would change behaviour).
    <SheetShell
      title="Merge document order"
      scrim={false}
      widthClass="max-w-[420px]"
      className="mt-2"
      footer={
        <div className="flex w-full justify-end gap-2">
          <PushButton variant="primary" onClick={() => onConfirm([...order])}>
            Merge in this order
          </PushButton>
          {onCancel ? <PushButton onClick={onCancel}>Cancel</PushButton> : null}
        </div>
      }
    >
      <p className="text-[11px] text-mac-text-2">
        {visible.length} of {files.length} documents in merge order
      </p>
      <ol className="mt-2 space-y-1">
        {visible.map((file, visibleIndex) => (
          <li
            key={file.id}
            aria-label={`${visibleIndex + 1}. ${file.name}`}
            data-testid={`merge-order-item-${file.id}`}
            className={[
              'flex items-center gap-2 rounded-md border border-mac-hairline bg-mac-content px-2 py-1 text-xs',
              draggedId === file.id ? 'opacity-60' : '',
            ]
              .filter(Boolean)
              .join(' ')}
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
            <span className="flex-1 truncate text-mac-text">
              {visibleIndex + 1}. {file.name}
            </span>
            <PushButton
              aria-label={`Move ${file.name} up`}
              disabled={visibleIndex === 0}
              onClick={() => {
                const current = indexOf(file.id);
                move(current, current - 1);
              }}
              size="sm"
            >
              Up
            </PushButton>
            <PushButton
              aria-label={`Move ${file.name} down`}
              disabled={visibleIndex === visible.length - 1}
              onClick={() => {
                const current = indexOf(file.id);
                move(current, current + 1);
              }}
              size="sm"
            >
              Down
            </PushButton>
          </li>
        ))}
      </ol>
    </SheetShell>
  );
}
