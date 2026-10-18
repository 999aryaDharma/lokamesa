import { describe, expect, it } from 'vitest';
import type { SourceControlChangeEntry } from './sourceControlModels';
import {
  docxDiffTargetFor,
  toDocxDiffViewState,
  type DocxDiffApiResponse,
} from './docxDiffApi';

function entry(overrides: Partial<SourceControlChangeEntry> = {}): SourceControlChangeEntry {
  return {
    path: 'docs/Report.docx',
    originalPath: null,
    state: 'Modified',
    indexChange: 'Unmodified',
    workingTreeChange: 'Modified',
    conflictKind: 'None',
    statusCharacters: ' M',
    absolutePath: 'C:\\docs\\docs\\Report.docx',
    documentId: 'docx-1',
    ...overrides,
  };
}

/**
 * Real-shaped payload of `POST /api/source-control/docx-diff` (SCM-19):
 * PascalCase enum names, a report with exact before/after content and nullable
 * indexes, mirroring `SourceControlController.ToDocxDiffResponse`.
 */
function availablePayload(): DocxDiffApiResponse {
  return {
    status: 'Available',
    isSuccess: true,
    errorCode: null,
    message: null,
    report: {
      target: 'Unstaged',
      path: 'docs/Report.docx',
      worktreeRoot: 'C:\\docs',
      leftLabel: 'index',
      rightLabel: 'working tree',
      leftExists: true,
      rightExists: true,
      changeCount: 3,
      hasChanges: true,
      changes: [
        {
          kind: 'ParagraphTextChanged',
          origin: 'Body',
          partName: 'word/document.xml',
          summary: 'Paragraph text changed: "Either party may terminate" -> "Either party may terminate"',
          before: 'Either party may terminate this agreement with 30 days written notice.',
          after: 'Either party may terminate this agreement with 60 days written notice.',
          property: null,
          oldParagraphIndex: 12,
          newParagraphIndex: 12,
          runIndex: null,
          tableIndex: null,
          rowIndex: null,
          cellIndex: null,
        },
        {
          kind: 'TableCellChanged',
          origin: 'Table',
          partName: 'word/document.xml',
          summary: 'Table cell changed: "Alpha" -> "Beta"',
          before: 'Alpha',
          after: 'Beta',
          property: null,
          oldParagraphIndex: null,
          newParagraphIndex: null,
          runIndex: null,
          tableIndex: 1,
          rowIndex: 2,
          cellIndex: 3,
        },
        {
          kind: 'FormattingChanged',
          origin: 'Body',
          partName: 'word/document.xml',
          summary: 'Formatting changed for "Audit": Bold off -> on',
          before: 'off',
          after: 'on',
          property: 'Bold',
          oldParagraphIndex: 22,
          newParagraphIndex: 22,
          runIndex: 0,
          tableIndex: null,
          rowIndex: null,
          cellIndex: null,
        },
      ],
      binarySummary: null,
    },
    failure: null,
  };
}

describe('docxDiffTargetFor', () => {
  it('compares HEAD with the index for a staged-only change', () => {
    expect(docxDiffTargetFor(entry({ indexChange: 'Modified', workingTreeChange: 'Unmodified' }))).toBe(
      'Staged',
    );
  });

  it('compares the index with the working tree for an unstaged-only change', () => {
    expect(docxDiffTargetFor(entry())).toBe('Unstaged');
  });

  it('compares HEAD with the working tree for an untracked document', () => {
    expect(
      docxDiffTargetFor(
        entry({
          state: 'Untracked',
          indexChange: 'Unmodified',
          workingTreeChange: 'Unmodified',
          statusCharacters: '??',
        }),
      ),
    ).toBe('WorkingTree');
  });

  it('uses the staged pair for both-staged-and-unstaged and conflicted entries', () => {
    expect(
      docxDiffTargetFor(entry({ indexChange: 'Modified', workingTreeChange: 'Modified' })),
    ).toBe('Staged');
    expect(
      docxDiffTargetFor(
        entry({
          state: 'Conflicted',
          indexChange: 'Unmerged',
          workingTreeChange: 'Unmerged',
          conflictKind: 'BothModified',
          statusCharacters: 'UU',
        }),
      ),
    ).toBe('Staged');
  });
});

describe('toDocxDiffViewState', () => {
  it('maps a real document-aware report onto the DocxDiffPanel view model', () => {
    const state = toDocxDiffViewState(availablePayload());

    expect(state.status).toBe('available');
    expect(state.failureMessage).toBeNull();
    expect(state.diff).not.toBeNull();
    expect(state.diff?.target).toBe('unstaged');
    expect(state.diff?.leftLabel).toBe('index');
    expect(state.diff?.rightLabel).toBe('working tree');
    expect(state.diff?.changes).toHaveLength(3);
    expect(state.diff?.changes[0]).toEqual({
      kind: 'paragraph-text-changed',
      summary: 'Paragraph text changed: "Either party may terminate" -> "Either party may terminate"',
      before: 'Either party may terminate this agreement with 30 days written notice.',
      after: 'Either party may terminate this agreement with 60 days written notice.',
      property: null,
      oldParagraphIndex: 12,
      newParagraphIndex: 12,
      runIndex: null,
      tableIndex: null,
      rowIndex: null,
      cellIndex: null,
    });
    expect(state.diff?.changes[1].kind).toBe('table-cell-changed');
    expect(state.diff?.changes[1].tableIndex).toBe(1);
    expect(state.diff?.changes[2].kind).toBe('formatting-changed');
    expect(state.diff?.changes[2].property).toBe('Bold');
    expect(state.diff?.binarySummary).toBeNull();
  });

  it('maps a binary fallback to its typed state with the size/SHA summary, never to rows', () => {
    const state = toDocxDiffViewState({
      status: 'BinaryFallback',
      isSuccess: false,
      errorCode: 'BinaryFallback',
      message:
        'The materialized version is not a valid DOCX package: The package is not a zip archive. (Code: DOCX_INVALID).',
      report: {
        target: 'Staged',
        path: 'docs/Report.docx',
        worktreeRoot: 'C:\\docs',
        leftLabel: 'HEAD',
        rightLabel: 'index',
        leftExists: true,
        rightExists: true,
        changeCount: 0,
        hasChanges: false,
        changes: [],
        binarySummary: {
          leftExists: true,
          rightExists: true,
          leftSizeBytes: 120,
          rightSizeBytes: 340,
          leftSha256: 'a'.repeat(64),
          rightSha256: 'b'.repeat(64),
          contentIdentical: false,
        },
      },
      failure: {
        status: 'Failed',
        errorKind: 'Unknown',
        errorMessage: 'The materialized version is not a valid DOCX package.',
        exitCode: null,
      },
    });

    expect(state.status).toBe('binary-fallback');
    expect(state.diff?.target).toBe('staged');
    expect(state.diff?.changes).toEqual([]);
    expect(state.diff?.binarySummary).toEqual({
      leftExists: true,
      rightExists: true,
      leftSizeBytes: 120,
      rightSizeBytes: 340,
      leftSha256: 'a'.repeat(64),
      rightSha256: 'b'.repeat(64),
      contentIdentical: false,
    });
    expect(state.failureMessage).toContain('not a valid DOCX package');
  });

  it.each([
    ['NoRepository', 'no-repository'],
    ['GitUnavailable', 'git-unavailable'],
    ['PathRejected', 'path-rejected'],
    ['Failed', 'failed'],
  ] as const)('maps the typed %s status without inventing a report', (status, expected) => {
    const state = toDocxDiffViewState({
      status,
      isSuccess: false,
      errorCode: status,
      message: `Typed ${status} outcome.`,
      report: null,
      failure: null,
    });

    expect(state.status).toBe(expected);
    expect(state.diff).toBeNull();
    expect(state.failureMessage).toBe(`Typed ${status} outcome.`);
  });

  it('treats an unknown status or change class as a typed failure, never as fabricated rows', () => {
    const unknownStatus = toDocxDiffViewState({
      status: 'SomethingNew',
      isSuccess: false,
      errorCode: null,
      message: null,
      report: null,
      failure: null,
    });
    expect(unknownStatus.status).toBe('failed');
    expect(unknownStatus.diff).toBeNull();
    expect(unknownStatus.failureMessage).toMatch(/unknown/i);

    const payload = availablePayload();
    payload.report!.changes[0].kind = 'WordReordered';
    const unknownKind = toDocxDiffViewState(payload);
    expect(unknownKind.status).toBe('failed');
    expect(unknownKind.diff).toBeNull();
    expect(unknownKind.failureMessage).toContain('WordReordered');
  });

  it('keeps an available report with zero changes as an available empty report', () => {
    const payload = availablePayload();
    payload.report!.changes = [];
    payload.report!.changeCount = 0;
    payload.report!.hasChanges = false;

    const state = toDocxDiffViewState(payload);
    expect(state.status).toBe('available');
    expect(state.diff?.changes).toEqual([]);
    expect(state.failureMessage).toBeNull();
  });

  it('reports a missing report body as a typed failure instead of an empty diff', () => {
    const state = toDocxDiffViewState({
      status: 'Available',
      isSuccess: true,
      errorCode: null,
      message: null,
      report: null,
      failure: null,
    });

    expect(state.status).toBe('failed');
    expect(state.diff).toBeNull();
    expect(state.failureMessage).toMatch(/no document report/i);
  });
});
