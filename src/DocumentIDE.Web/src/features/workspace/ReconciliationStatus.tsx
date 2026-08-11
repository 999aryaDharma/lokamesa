import React from 'react';

export interface ReconciliationStatusProps {
  isReconciling: boolean;
  scannedCount: number;
  totalCount: number;
  changedCount: number;
  missingCount: number;
}

export const ReconciliationStatus: React.FC<ReconciliationStatusProps> = ({
  isReconciling,
  scannedCount,
  totalCount,
  changedCount,
  missingCount,
}) => {
  if (isReconciling) {
    return (
      <div className="fixed bottom-4 right-4 p-3 bg-slate-900 text-white rounded-lg shadow-lg text-xs flex items-center gap-3 z-50">
        <span className="animate-spin rounded-full h-3 w-3 border-2 border-blue-400 border-t-transparent" />
        <span>Reconciling workspace...</span>
        <span className="text-slate-400">({scannedCount}/{totalCount} files checked)</span>
      </div>
    );
  }

  if (changedCount > 0 || missingCount > 0) {
    return (
      <div className="fixed bottom-4 right-4 p-3 bg-slate-900 text-white rounded-lg shadow-lg text-xs flex items-center gap-2 z-50">
        <span className="h-2 w-2 rounded-full bg-amber-400" />
        <span>Reconciled: {changedCount} changed, {missingCount} missing</span>
      </div>
    );
  }

  return (
    <div className="fixed bottom-4 right-4 p-3 bg-slate-900 text-white rounded-lg shadow-lg text-xs flex items-center gap-2 z-50">
      <span className="h-2 w-2 rounded-full bg-emerald-400" />
      <span>Workspace up to date</span>
    </div>
  );
};

export default ReconciliationStatus;
