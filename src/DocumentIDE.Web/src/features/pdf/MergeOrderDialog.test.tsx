import { render, screen, fireEvent } from '@testing-library/react';
import { expect, it, vi } from 'vitest';
import { MergeOrderDialog } from './MergeOrderDialog';

const files = [
  { id: 'b', name: 'BA Site B.docx' },
  { id: 'a', name: 'Cover.docx' },
  { id: 'c', name: 'Lampiran.docx' },
];

it('renders the explicit order verbatim and never sorts', () => {
  render(
    <MergeOrderDialog
      files={files}
      order={['c', 'a', 'b']}
      onOrderChange={() => {}}
      onConfirm={() => {}}
    />,
  );

  const items = screen.getAllByTestId(/merge-order-item-/);
  expect(items.map((item) => item.getAttribute('data-testid'))).toEqual([
    'merge-order-item-c',
    'merge-order-item-a',
    'merge-order-item-b',
  ]);
  expect(screen.getByText('3 of 3 documents in merge order')).toBeInTheDocument();
});

it('move buttons reorder explicitly and confirm passes the current order', () => {
  const onOrderChange = vi.fn();
  const onConfirm = vi.fn();
  const { rerender } = render(
    <MergeOrderDialog
      files={files}
      order={['a', 'b', 'c']}
      onOrderChange={onOrderChange}
      onConfirm={onConfirm}
    />,
  );

  fireEvent.click(screen.getByRole('button', { name: 'Move BA Site B.docx down' }));
  expect(onOrderChange).toHaveBeenCalledWith(['a', 'c', 'b']);

  rerender(
    <MergeOrderDialog
      files={files}
      order={['a', 'c', 'b']}
      onOrderChange={onOrderChange}
      onConfirm={onConfirm}
    />,
  );
  fireEvent.click(screen.getByRole('button', { name: 'Merge in this order' }));
  expect(onConfirm).toHaveBeenCalledWith(['a', 'c', 'b']);
});
