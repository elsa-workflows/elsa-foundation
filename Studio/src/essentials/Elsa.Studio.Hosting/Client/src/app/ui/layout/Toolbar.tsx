import React from "react";

export function StudioToolbar({ children }: { children: React.ReactNode }) {
  return <div className="studio-toolbar">{children}</div>;
}

export function StudioToolbarGroup({ children }: { children: React.ReactNode }) {
  return <div className="studio-toolbar-group">{children}</div>;
}
