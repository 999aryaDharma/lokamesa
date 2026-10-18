import React, { useState, useMemo } from 'react';
import SheetShell from '../shell/SheetShell';
import PushButton from '../shell/PushButton';

export interface ContextSourceItem {
  id: string;
  title: string;
  content: string;
  isDependency?: boolean;
  addedFrom?: string;
}

export interface ContextPackDialogProps {
  isOpen: boolean;
  onClose: () => void;
  sources: ContextSourceItem[];
  tokenBudget?: number;
  onExportToFile?: (combinedMarkdown: string, filename: string) => Promise<void> | void;
  onCopy?: (combinedMarkdown: string) => void;
}

export type TokenizerModelType = 'generic' | 'gpt4' | 'claude35' | 'llama3';

/**
 * UI-01 — context-pack sheet (screen 12).
 *
 * Presentation only: the overlay was hand-rolled with inline styles and is now
 * the shared `SheetShell` scrim sheet with `mac-*` tokens. Every testid,
 * accessible name, label and callback is unchanged.
 */
export const ContextPackDialog: React.FC<ContextPackDialogProps> = ({
  isOpen,
  onClose,
  sources,
  tokenBudget: initialBudget,
  onExportToFile,
  onCopy
}) => {
  const [model, setModel] = useState<TokenizerModelType>('generic');
  const [targetBudget, setTargetBudget] = useState<number | ''>(initialBudget ?? '');
  const [copied, setCopied] = useState(false);
  const [exported, setExported] = useState(false);

  const charsPerTokenMap: Record<TokenizerModelType, number> = {
    generic: 4.0,
    gpt4: 3.75,
    claude35: 3.8,
    llama3: 3.6
  };

  const modelLabels: Record<TokenizerModelType, string> = {
    generic: 'Generic Tokenizer (4 chars/token)',
    gpt4: 'GPT-4 (cl100k_base approx)',
    claude35: 'Claude 3.5 Sonnet (approx)',
    llama3: 'Llama 3 (approx)'
  };

  const ratio = charsPerTokenMap[model];

  const { itemsWithEstimate, totalTokens, totalChars } = useMemo(() => {
    let totTokens = 0;
    let totChars = 0;

    const items = sources.map((src) => {
      const content = src.content || '';
      const charCount = content.length;
      const estTokens = charCount === 0 ? 0 : Math.max(1, Math.ceil(charCount / ratio));

      totChars += charCount;
      totTokens += estTokens;

      return {
        ...src,
        charCount,
        estTokens
      };
    });

    return {
      itemsWithEstimate: items.map((item) => ({
        ...item,
        percentage: totTokens > 0 ? ((item.estTokens / totTokens) * 100).toFixed(1) : '0.0'
      })),
      totalTokens: totTokens,
      totalChars: totChars
    };
  }, [sources, ratio]);

  const exceedsBudget = typeof targetBudget === 'number' && targetBudget > 0 && totalTokens > targetBudget;
  const budgetExcess = exceedsBudget && typeof targetBudget === 'number' ? totalTokens - targetBudget : 0;

  const combinedMarkdown = useMemo(() => {
    return sources.map((s) => s.content || '').join('\n\n').trim();
  }, [sources]);

  if (!isOpen) return null;

  const handleCopyClick = () => {
    if (navigator?.clipboard?.writeText) {
      navigator.clipboard.writeText(combinedMarkdown).catch(() => {});
    }
    if (onCopy) {
      onCopy(combinedMarkdown);
    }
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  };

  const handleExportClick = async () => {
    if (onExportToFile) {
      await onExportToFile(combinedMarkdown, 'context.md');
    }
    setExported(true);
    setTimeout(() => setExported(false), 2000);
  };

  const fieldLabelClass = 'text-[11px] font-medium text-mac-text-2';
  const inputClass =
    'rounded-md border border-mac-hairline-strong bg-mac-content px-2 py-1 text-xs text-mac-text';
  const thClass = 'border-b border-mac-hairline px-3 py-2 text-left font-medium text-mac-text-2';
  const tdClass = 'px-3 py-2 align-top text-mac-text-2';

  return (
    <div className="fixed inset-0 z-[1000]">
      <SheetShell
        title="Context Pack Export & Token Estimate"
        widthClass="max-w-[640px]"
        onClose={onClose}
        footer={
          <div className="flex w-full justify-end gap-2">
            <PushButton onClick={onClose}>Close</PushButton>
            <PushButton variant="primary" onClick={handleCopyClick}>
              {copied ? 'Copied!' : 'Copy Context'}
            </PushButton>
            <PushButton onClick={handleExportClick}>
              {exported ? 'Exported!' : 'Export context.md'}
            </PushButton>
          </div>
        }
      >
        <div className="grid grid-cols-2 gap-3">
          <div className="flex flex-col gap-1">
            <label htmlFor="tokenizer-model-select" className={fieldLabelClass}>
              Tokenizer Model
            </label>
            <select
              id="tokenizer-model-select"
              value={model}
              onChange={(e) => setModel(e.target.value as TokenizerModelType)}
              className={inputClass}
            >
              <option value="generic">Generic (4 chars/token)</option>
              <option value="gpt4">GPT-4 (cl100k_base approx)</option>
              <option value="claude35">Claude 3.5 Sonnet (approx)</option>
              <option value="llama3">Llama 3 (approx)</option>
            </select>
          </div>

          <div className="flex flex-col gap-1">
            <label htmlFor="token-budget-input" className={fieldLabelClass}>
              Target Token Budget
            </label>
            <input
              id="token-budget-input"
              type="number"
              placeholder="e.g. 8000"
              value={targetBudget}
              onChange={(e) => setTargetBudget(e.target.value === '' ? '' : Number(e.target.value))}
              className={inputClass}
            />
          </div>
        </div>

        <div className="mt-3 flex flex-wrap justify-between gap-x-4 gap-y-1 rounded-lg bg-mac-toolbar px-3 py-2 text-xs text-mac-text-2">
          <div>
            <strong className="text-mac-text">Active Model: </strong>
            <span data-testid="model-label">{modelLabels[model]}</span>
          </div>
          <div>
            <strong className="text-mac-text">Total Characters: </strong>
            <span>{totalChars.toLocaleString()}</span>
          </div>
          <div>
            <strong className="text-mac-text">Total Estimated Tokens: </strong>
            <span className={exceedsBudget ? 'font-bold text-mac-danger' : 'font-bold text-mac-success'}>
              {totalTokens.toLocaleString()}
            </span>
          </div>
        </div>

        {exceedsBudget && (
          <div
            data-testid="budget-warning-banner"
            className="mt-3 rounded-lg border border-mac-warning/40 bg-mac-warning/10 px-3 py-2 text-xs text-mac-warning"
          >
            <strong>Warning: </strong> Total estimated tokens ({totalTokens.toLocaleString()}) exceeds target budget (
            {targetBudget.toLocaleString()}) by {budgetExcess.toLocaleString()} tokens!
          </div>
        )}

        <div className="mt-3 overflow-auto rounded-lg border border-mac-hairline">
          <table className="w-full border-collapse text-xs">
            <thead className="bg-mac-toolbar">
              <tr>
                <th className={thClass}>Source Document</th>
                <th className={thClass}>Type</th>
                <th className={thClass}>Characters</th>
                <th className={thClass}>Est. Tokens</th>
                <th className={thClass}>Share</th>
              </tr>
            </thead>
            <tbody>
              {itemsWithEstimate.map((item) => (
                <tr
                  key={item.id}
                  data-testid={`source-row-${item.id}`}
                  className="border-b border-mac-hairline last:border-b-0"
                >
                  <td className={tdClass}>
                    <strong className="text-mac-text">{item.title}</strong>
                    {item.addedFrom && (
                      <div className="mt-0.5 text-[11px] text-mac-text-3">
                        Added via link from: {item.addedFrom}
                      </div>
                    )}
                  </td>
                  <td className={tdClass}>
                    {item.isDependency ? (
                      <span className="rounded bg-mac-chip px-1.5 py-0.5 text-[10px] text-mac-text-2">
                        Dependency
                      </span>
                    ) : (
                      <span className="rounded bg-mac-accent-weak px-1.5 py-0.5 text-[10px] text-mac-accent">
                        Seed
                      </span>
                    )}
                  </td>
                  <td className={tdClass}>{item.charCount.toLocaleString()}</td>
                  <td className={tdClass}>{item.estTokens.toLocaleString()}</td>
                  <td className={tdClass}>{item.percentage}%</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </SheetShell>
    </div>
  );
};
