import React from 'react';
import { render, screen } from '@testing-library/react';

import { MarkdownEditor } from './MarkdownEditor';

describe('MarkdownEditor Component', () => {
  test('renders textarea', () => {
    render(
      <MarkdownEditor />
    );
    const textarea = screen.getByRole('textbox');
    expect(textarea).toBeInTheDocument();
  });
});
