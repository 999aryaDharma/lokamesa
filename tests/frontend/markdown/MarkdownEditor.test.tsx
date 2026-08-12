import React from 'react';
import { render, screen } from '@testing-library/react';
import '@testing-library/jest-dom';
import { BrowserRouter } from 'react-router-dom';
import { MarkdownEditor } from '../../src/DocumentIDE.Web/src/features/markdown/MarkdownEditor';

describe('MarkdownEditor', () => {
  test('renders without crashing', () => {
    render(
      <BrowserRouter>
        <MarkdownEditor />
      </BrowserRouter>
    );
    const textarea = screen.getByRole('textbox');
    expect(textarea).toBeInTheDocument();
  });
});
