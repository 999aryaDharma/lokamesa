import { render, screen, fireEvent } from '@testing-library/react';
import { describe, test, expect, vi } from 'vitest';
import { EditingInWordBadge } from './EditingInWordBadge';

describe('EditingInWordBadge', () => {
  test('renders nothing when the document is not being edited', () => {
    const { container } = render(<EditingInWordBadge isEditing={false} />);
    expect(container).toBeEmptyDOMElement();
    expect(screen.queryByTestId('editing-in-word-badge')).toBeNull();
  });

  test('open Word doc shows editing state with confidence', () => {
    render(
      <EditingInWordBadge
        isEditing={true}
        confidence="high"
        summary="Likely editing in Word (2 of 3 signals positive)."
      />,
    );

    const badge = screen.getByTestId('editing-in-word-badge');
    expect(badge).toHaveTextContent('Editing in Word');
    expect(screen.getByTestId('editing-in-word-confidence')).toHaveTextContent('likely');
    expect(badge).toHaveAttribute(
      'title',
      'Likely editing in Word (2 of 3 signals positive).',
    );
  });

  test('single-signal verdict is labeled as possible, not certain', () => {
    render(<EditingInWordBadge isEditing={true} confidence="low" />);
    expect(screen.getByTestId('editing-in-word-confidence')).toHaveTextContent('possibly');
  });

  test('offers Open Word action when a handler is provided', () => {
    const onOpenInWord = vi.fn();
    render(<EditingInWordBadge isEditing={true} onOpenInWord={onOpenInWord} />);

    fireEvent.click(screen.getByTestId('editing-in-word-open'));
    expect(onOpenInWord).toHaveBeenCalledTimes(1);
  });
});
