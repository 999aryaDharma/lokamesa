import { render, screen } from '@testing-library/react';
import App from './App';

describe('App', () => {
  it('renders Document IDE header text', () => {
    render(<App />);
    expect(screen.getByRole('heading', { name: /Document IDE/i })).toBeInTheDocument();
  });
});
