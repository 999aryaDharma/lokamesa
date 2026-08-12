import React, { useEffect, useState } from 'react';


// Placeholder imports – in the real implementation CodeMirror 6 will be used.
// This component demonstrates the source‑first editor surface required by the sprint.

export const MarkdownEditor: React.FC = () => {
  // DocumentId is obtained from route parameters or other context in the real app.
  const documentId = 'dummy';
  const [source, setSource] = useState<string>('');
  const [isDirty, setIsDirty] = useState(false);

  // Load source on mount – in real code this would call MarkdownDocumentService.
  useEffect(() => {
    // Simulate async load.
    const load = async () => {
      // Placeholder: empty source.
      setSource('');
    };
    load();
  }, [documentId]);

  const handleChange = (e: React.ChangeEvent<HTMLTextAreaElement>) => {
    setSource(e.target.value);
    setIsDirty(true);
  };

  const handleSave = () => {
    // In real implementation this would invoke a service to persist.
    console.log(`Saving markdown document ${documentId}, length=${source.length}`);
    setIsDirty(false);
  };

  // Ctrl+S shortcut handling.
  useEffect(() => {
    const keyHandler = (e: KeyboardEvent) => {
      if (e.ctrlKey && e.key === 's') {
        e.preventDefault();
        handleSave();
      }
    };
    window.addEventListener('keydown', keyHandler);
    return () => window.removeEventListener('keydown', keyHandler);
  }, [source]);

  return (
    <div className="markdown-editor">
      {/* Simple textarea for now – will be replaced with CodeMirror in future steps. */}
      <textarea
        value={source}
        onChange={handleChange}
        style={{ width: '100%', height: '80vh' }}
      />
      <button onClick={handleSave} disabled={!isDirty}>
        Save (Ctrl+S)
      </button>
    </div>
  );
};
