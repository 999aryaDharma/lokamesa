import { render, screen, fireEvent } from '@testing-library/react';
import { describe, it, expect, vi } from 'vitest';
import React from 'react';
import { ContextPackDialog } from './ContextPackDialog';

describe('ContextPackDialog', () => {
  const sampleSources = [
    { id: 'doc1', title: 'Architecture.md', content: '# Architecture\nMain details here.' },
    { id: 'doc2', title: 'API.md', content: '# API Spec\nEndpoints detailed here.' }
  ];

  it('renders correctly when open', () => {
    render(
      <ContextPackDialog
        isOpen={true}
        onClose={vi.fn()}
        sources={sampleSources}
      />
    );

    expect(screen.getByText('Context Pack Export & Token Estimate')).toBeInTheDocument();
    expect(screen.getByText('Architecture.md')).toBeInTheDocument();
    expect(screen.getByText('API.md')).toBeInTheDocument();
  });

  it('displays estimated tokens labeled by model selection', () => {
    render(
      <ContextPackDialog
        isOpen={true}
        onClose={vi.fn()}
        sources={sampleSources}
      />
    );

    const modelSelect = screen.getByLabelText('Tokenizer Model') as HTMLSelectElement;
    expect(modelSelect.value).toBe('generic');
    expect(screen.getByTestId('model-label')).toHaveTextContent(/Generic Tokenizer/i);

    fireEvent.change(modelSelect, { target: { value: 'gpt4' } });
    expect(screen.getByTestId('model-label')).toHaveTextContent(/GPT-4/i);
  });

  it('shows contribution breakdown for each source item', () => {
    render(
      <ContextPackDialog
        isOpen={true}
        onClose={vi.fn()}
        sources={sampleSources}
      />
    );

    expect(screen.getByTestId('source-row-doc1')).toBeInTheDocument();
    expect(screen.getByTestId('source-row-doc2')).toBeInTheDocument();
  });

  it('warns when target token budget is exceeded', () => {
    render(
      <ContextPackDialog
        isOpen={true}
        onClose={vi.fn()}
        sources={sampleSources}
        tokenBudget={5} // Set a tiny budget to trigger warning
      />
    );

    expect(screen.getByTestId('budget-warning-banner')).toBeInTheDocument();
    expect(screen.getByTestId('budget-warning-banner')).toHaveTextContent(/exceeds target budget/i);
  });

  it('copies combined Markdown to clipboard', async () => {
    const handleCopy = vi.fn();
    Object.assign(navigator, {
      clipboard: {
        writeText: vi.fn().mockResolvedValue(undefined)
      }
    });

    render(
      <ContextPackDialog
        isOpen={true}
        onClose={vi.fn()}
        sources={sampleSources}
        onCopy={handleCopy}
      />
    );

    const copyBtn = screen.getByRole('button', { name: /copy context/i });
    await React.act(async () => {
      fireEvent.click(copyBtn);
    });

    expect(handleCopy).toHaveBeenCalledWith(expect.stringContaining('# Architecture'));
    expect(handleCopy).toHaveBeenCalledWith(expect.stringContaining('# API Spec'));
  });

  it('triggers export to file', async () => {
    const handleExport = vi.fn();
    render(
      <ContextPackDialog
        isOpen={true}
        onClose={vi.fn()}
        sources={sampleSources}
        onExportToFile={handleExport}
      />
    );

    const exportBtn = screen.getByRole('button', { name: /export context\.md/i });
    await React.act(async () => {
      fireEvent.click(exportBtn);
    });

    expect(handleExport).toHaveBeenCalledWith(
      expect.stringContaining('# Architecture'),
      'context.md'
    );
  });
});
