import React, { useId } from "react";
import { StudioTabs, StudioTabPanel } from "@elsa-workflows/studio-ui";

export type SecretEditorSection = "secret" | "details" | "storage";

export function SecretEditorTabs({ active, onSelect, detailsCount = 0, storageCount = 0, storageNeedsAttention = false, children }: {
  active: SecretEditorSection;
  onSelect(section: SecretEditorSection): void;
  detailsCount?: number;
  storageCount?: number;
  storageNeedsAttention?: boolean;
  children: Record<SecretEditorSection, React.ReactNode>;
}) {
  const baseId = useId();
  const tabs = [
    { id: "secret", label: "Secret" },
    { id: "details", label: `Details${detailsCount ? ` (${detailsCount})` : ""}` },
    { id: "storage", label: `Storage & lifetime${storageCount ? ` (${storageCount})` : ""}${storageNeedsAttention ? " — needs attention" : ""}` }
  ];
  return <>
    <StudioTabs baseId={baseId} tabs={tabs} activeTab={active} onSelect={section => onSelect(section as SecretEditorSection)} ariaLabel="Secret sections" />
    {tabs.map((tab, index) => <StudioTabPanel key={tab.id} baseId={baseId} index={index} hidden={active !== tab.id} className="secrets-editor-panel">
      {children[tab.id as SecretEditorSection]}
    </StudioTabPanel>)}
  </>;
}

export interface SecretEditorState { dirty: boolean; busy: boolean }
