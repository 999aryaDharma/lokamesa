// src/DocumentIDE.Web/src/features/graph/DocumentGraph.tsx

import React from "react";
import { useGraphStore } from "./graphStore";
// Canvas store import removed - Graph view should not affect canvas


/** Simple Graph view component placeholder */
export const DocumentGraph: React.FC = () => {
  const { positions, setPosition } = useGraphStore.getState();

  // Example interaction: on click of a node div, move it (dummy)
  const handleClick = (id: string) => {
    const pos = positions.get(id) ?? { x: 0, y: 0 };
    // Update graph node position only; no canvas side effect
    setPosition(id, { x: pos.x + 10, y: pos.y + 10 });
  };


  return (
    <div data-testid="graph-view">
      {Array.from(positions.entries()).map(([id, pos]) => (
        <div
          key={id}
          data-testid={`graph-node-${id}`}
          style={{ position: "absolute", left: pos.x, top: pos.y }}
          onClick={() => handleClick(id)}
        >
          Node {id}
        </div>
      ))}
    </div>
  );
};

export default DocumentGraph;
