import { render, screen, fireEvent } from '@testing-library/react';
import { expect, it, vi } from 'vitest';
import { BatchExportProgress } from './BatchExportProgress';

it('shows progress, current filename, and per-file summary', () => {
  render(
    <BatchExportProgress
      completed={1}
      total={3}
      currentFileName="site-a.docx"
      succeeded={1}
      skipped={0}
      failed={0}
    />,
  );

  const bar = screen.getByRole('progressbar');
  expect(bar).toHaveAttribute('aria-valuenow', '1');
  expect(bar).toHaveAttribute('aria-valuemax', '3');
  expect(screen.getByText('site-a.docx')).toBeInTheDocument();
  expect(screen.getByText('1 PDFs generated / 0 skipped / 0 failed')).toBeInTheDocument();
});

it('invokes cancel and hides the button once complete', () => {
  const onCancel = vi.fn();
  const { rerender } = render(
    <BatchExportProgress
      completed={0}
      total={2}
      currentFileName="cover.docx"
      succeeded={0}
      skipped={0}
      failed={0}
      onCancel={onCancel}
    />,
  );

  fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
  expect(onCancel).toHaveBeenCalledTimes(1);

  rerender(
    <BatchExportProgress
      completed={2}
      total={2}
      succeeded={1}
      skipped={1}
      failed={0}
    />,
  );
  expect(screen.queryByRole('button', { name: 'Cancel' })).not.toBeInTheDocument();
});
