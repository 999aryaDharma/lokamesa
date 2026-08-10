/**
 * App root — wires DocumentCanvas as the main application surface.
 * CAN-01: Canvas Foundation.
 */
import DocumentCanvas from './features/canvas/DocumentCanvas';

export default function App() {
  return (
    <div className="w-screen h-screen overflow-hidden bg-slate-50">
      <DocumentCanvas />
    </div>
  );
}
