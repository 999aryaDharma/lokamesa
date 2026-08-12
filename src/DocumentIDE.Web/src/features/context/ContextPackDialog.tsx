import React, { useState, useMemo } from 'react';

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

  return (
    <div className="modal-overlay" style={overlayStyle}>
      <div className="modal-content" style={modalStyle}>
        <div style={headerStyle}>
          <h2>Context Pack Export &amp; Token Estimate</h2>
          <button style={closeBtnStyle} onClick={onClose}>
            &times;
          </button>
        </div>

        <div style={controlsRowStyle}>
          <div style={controlGroupStyle}>
            <label htmlFor="tokenizer-model-select">Tokenizer Model</label>
            <select
              id="tokenizer-model-select"
              value={model}
              onChange={(e) => setModel(e.target.value as TokenizerModelType)}
              style={inputStyle}
            >
              <option value="generic">Generic (4 chars/token)</option>
              <option value="gpt4">GPT-4 (cl100k_base approx)</option>
              <option value="claude35">Claude 3.5 Sonnet (approx)</option>
              <option value="llama3">Llama 3 (approx)</option>
            </select>
          </div>

          <div style={controlGroupStyle}>
            <label htmlFor="token-budget-input">Target Token Budget</label>
            <input
              id="token-budget-input"
              type="number"
              placeholder="e.g. 8000"
              value={targetBudget}
              onChange={(e) => setTargetBudget(e.target.value === '' ? '' : Number(e.target.value))}
              style={inputStyle}
            />
          </div>
        </div>

        <div style={summaryBoxStyle}>
          <div>
            <strong>Active Model: </strong>
            <span data-testid="model-label">{modelLabels[model]}</span>
          </div>
          <div>
            <strong>Total Characters: </strong>
            <span>{totalChars.toLocaleString()}</span>
          </div>
          <div>
            <strong>Total Estimated Tokens: </strong>
            <span style={{ fontWeight: 'bold', color: exceedsBudget ? '#d9534f' : '#2e7d32' }}>
              {totalTokens.toLocaleString()}
            </span>
          </div>
        </div>

        {exceedsBudget && (
          <div data-testid="budget-warning-banner" style={warningBannerStyle}>
            <strong>Warning: </strong> Total estimated tokens ({totalTokens.toLocaleString()}) exceeds target budget (
            {targetBudget.toLocaleString()}) by {budgetExcess.toLocaleString()} tokens!
          </div>
        )}

        <div style={tableContainerStyle}>
          <table style={tableStyle}>
            <thead>
              <tr>
                <th style={thStyle}>Source Document</th>
                <th style={thStyle}>Type</th>
                <th style={thStyle}>Characters</th>
                <th style={thStyle}>Est. Tokens</th>
                <th style={thStyle}>Share</th>
              </tr>
            </thead>
            <tbody>
              {itemsWithEstimate.map((item) => (
                <tr key={item.id} data-testid={`source-row-${item.id}`} style={trStyle}>
                  <td style={tdStyle}>
                    <strong>{item.title}</strong>
                    {item.addedFrom && (
                      <div style={subTextStyle}>Added via link from: {item.addedFrom}</div>
                    )}
                  </td>
                  <td style={tdStyle}>
                    {item.isDependency ? (
                      <span style={tagStyle}>Dependency</span>
                    ) : (
                      <span style={primaryTagStyle}>Seed</span>
                    )}
                  </td>
                  <td style={tdStyle}>{item.charCount.toLocaleString()}</td>
                  <td style={tdStyle}>{item.estTokens.toLocaleString()}</td>
                  <td style={tdStyle}>{item.percentage}%</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>

        <div style={footerStyle}>
          <button style={btnSecondaryStyle} onClick={onClose}>
            Close
          </button>
          <button style={btnPrimaryStyle} onClick={handleCopyClick}>
            {copied ? 'Copied!' : 'Copy Context'}
          </button>
          <button style={btnSuccessStyle} onClick={handleExportClick}>
            {exported ? 'Exported!' : 'Export context.md'}
          </button>
        </div>
      </div>
    </div>
  );
};

const overlayStyle: React.CSSProperties = {
  position: 'fixed',
  top: 0,
  left: 0,
  right: 0,
  bottom: 0,
  backgroundColor: 'rgba(0, 0, 0, 0.5)',
  display: 'flex',
  justifyContent: 'center',
  alignItems: 'center',
  zIndex: 1000
};

const modalStyle: React.CSSProperties = {
  backgroundColor: '#fff',
  borderRadius: '8px',
  width: '640px',
  maxWidth: '90vw',
  maxHeight: '85vh',
  display: 'flex',
  flexDirection: 'column',
  padding: '24px',
  boxShadow: '0 4px 20px rgba(0, 0, 0, 0.15)',
  overflow: 'hidden'
};

const headerStyle: React.CSSProperties = {
  display: 'flex',
  justifyContent: 'space-between',
  alignItems: 'center',
  borderBottom: '1px solid #eee',
  paddingBottom: '12px',
  marginBottom: '16px'
};

const closeBtnStyle: React.CSSProperties = {
  background: 'none',
  border: 'none',
  fontSize: '24px',
  cursor: 'pointer'
};

const controlsRowStyle: React.CSSProperties = {
  display: 'flex',
  gap: '16px',
  marginBottom: '16px'
};

const controlGroupStyle: React.CSSProperties = {
  display: 'flex',
  flexDirection: 'column',
  flex: 1
};

const inputStyle: React.CSSProperties = {
  padding: '8px',
  borderRadius: '4px',
  border: '1px solid #ccc',
  marginTop: '4px'
};

const summaryBoxStyle: React.CSSProperties = {
  display: 'flex',
  justifyContent: 'space-between',
  backgroundColor: '#f8f9fa',
  padding: '12px 16px',
  borderRadius: '6px',
  marginBottom: '16px',
  fontSize: '14px'
};

const warningBannerStyle: React.CSSProperties = {
  backgroundColor: '#fff3cd',
  color: '#856404',
  border: '1px solid #ffeeba',
  padding: '12px',
  borderRadius: '6px',
  marginBottom: '16px',
  fontSize: '14px'
};

const tableContainerStyle: React.CSSProperties = {
  flex: 1,
  overflowY: 'auto',
  marginBottom: '16px',
  border: '1px solid #eee',
  borderRadius: '4px'
};

const tableStyle: React.CSSProperties = {
  width: '100%',
  borderCollapse: 'collapse',
  fontSize: '14px'
};

const thStyle: React.CSSProperties = {
  textAlign: 'left',
  padding: '10px 12px',
  borderBottom: '2px solid #ddd',
  backgroundColor: '#fafafa'
};

const tdStyle: React.CSSProperties = {
  padding: '10px 12px',
  borderBottom: '1px solid #eee'
};

const trStyle: React.CSSProperties = {};

const tagStyle: React.CSSProperties = {
  fontSize: '12px',
  backgroundColor: '#e9ecef',
  color: '#495057',
  padding: '2px 6px',
  borderRadius: '4px'
};

const primaryTagStyle: React.CSSProperties = {
  fontSize: '12px',
  backgroundColor: '#e3f2fd',
  color: '#0d47a1',
  padding: '2px 6px',
  borderRadius: '4px'
};

const subTextStyle: React.CSSProperties = {
  fontSize: '12px',
  color: '#6c757d',
  marginTop: '2px'
};

const footerStyle: React.CSSProperties = {
  display: 'flex',
  justifyContent: 'flex-end',
  gap: '12px',
  borderTop: '1px solid #eee',
  paddingTop: '16px'
};

const btnSecondaryStyle: React.CSSProperties = {
  padding: '8px 16px',
  borderRadius: '4px',
  border: '1px solid #ccc',
  backgroundColor: '#f8f9fa',
  cursor: 'pointer'
};

const btnPrimaryStyle: React.CSSProperties = {
  padding: '8px 16px',
  borderRadius: '4px',
  border: 'none',
  backgroundColor: '#0066cc',
  color: '#fff',
  cursor: 'pointer'
};

const btnSuccessStyle: React.CSSProperties = {
  padding: '8px 16px',
  borderRadius: '4px',
  border: 'none',
  backgroundColor: '#2e7d32',
  color: '#fff',
  cursor: 'pointer'
};
