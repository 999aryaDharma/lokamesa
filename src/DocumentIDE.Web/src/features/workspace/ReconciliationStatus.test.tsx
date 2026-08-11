import { render, screen } from '@testing-library/react';
import { describe, it, expect } from 'vitest';
import ReconciliationStatus from './ReconciliationStatus';

describe('ReconciliationStatus Component', () => {
  it('renders status when reconciling', () => {
    render(
      <ReconciliationStatus
        isReconciling={true}
        scannedCount={5}
        totalCount={12}
        changedCount={2}
        missingCount={1}
      />
    );

    expect(screen.getByText(/reconciling workspace/i)).toBeInTheDocument();
    expect(screen.getByText(/5\/12 files checked/i)).toBeInTheDocument();
  });

  it('renders up to date status when reconciliation is complete and quiet', () => {
    render(
      <ReconciliationStatus
        isReconciling={false}
        scannedCount={10}
        totalCount={10}
        changedCount={0}
        missingCount={0}
      />
    );

    expect(screen.getByText(/workspace up to date/i)).toBeInTheDocument();
  });

  it('renders changed files summary when reconciliation finishes with updates', () => {
    render(
      <ReconciliationStatus
        isReconciling={false}
        scannedCount={10}
        totalCount={10}
        changedCount={3}
        missingCount={1}
      />
    );

    expect(screen.getByText(/reconciled: 3 changed, 1 missing/i)).toBeInTheDocument();
  });
});
