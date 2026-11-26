import React, { useEffect, useId, useRef, useState } from "react";
import { OverflowMenu, StudioButton, StudioPageHeader, StudioSidePanelActions, StudioTabPanel, StudioTabs, type OverflowMenuItem } from "@elsa-workflows/studio-ui";
import type { QueryProblem } from "./types";

type QuestionWorkspaceProps = {
  title: string;
  status: string;
  backAction?: React.ReactNode;
  saveAction?: React.ReactNode;
  menuItems?: OverflowMenuItem[];
  previewDisabled: boolean;
  previewBusy: boolean;
  onPreview(): void;
  build: React.ReactNode;
  preview: React.ReactNode;
  properties: React.ReactNode;
  problems?: QueryProblem[];
  validationAttempt?: number;
};

export function QuestionWorkspace({ title, status, backAction, saveAction, menuItems = [], previewDisabled, previewBusy, onPreview, build, preview, properties, problems = [], validationAttempt = 0 }: QuestionWorkspaceProps) {
  const [activeView, setActiveView] = useState("build");
  const [propertiesOpen, setPropertiesOpen] = useState(false);
  const [propertiesMaximized, setPropertiesMaximized] = useState(false);
  const baseId = useId();
  const propertiesId = `${baseId}-properties`;
  const propertiesRef = useRef<HTMLDivElement>(null);
  const hasValidationErrors = validationAttempt > 0 && problems.length > 0;
  const firstProblemPath = problems[0]?.path;
  const presentationProblem = firstProblemPath === "visualization" || Boolean(firstProblemPath?.startsWith("visualization."));

  useEffect(() => {
    if (!hasValidationErrors) return;
    setActiveView("build");
    if (presentationProblem) setPropertiesOpen(true);
  }, [hasValidationErrors, presentationProblem, validationAttempt]);

  useEffect(() => {
    if (!presentationProblem || !propertiesOpen || !validationAttempt) return;
    const path = firstProblemPath;
    const sections = [...(propertiesRef.current?.querySelectorAll<HTMLElement>("[data-query-path]") ?? [])];
    const target = sections.filter(element => {
      const candidate = element.dataset.queryPath ?? "";
      return path === candidate || Boolean(path?.startsWith(`${candidate}.`)) || Boolean(path?.startsWith(`${candidate}[`)) || Boolean(candidate.startsWith(`${path}.`));
    }).sort((a, b) => (b.dataset.queryPath?.length ?? 0) - (a.dataset.queryPath?.length ?? 0))[0];
    const control = target?.matches("input, select, textarea, button") ? target : target?.querySelector<HTMLElement>("input:not(:disabled), select:not(:disabled), textarea:not(:disabled), button:not(:disabled)");
    control?.focus();
  }, [presentationProblem, propertiesOpen, validationAttempt, firstProblemPath]);

  const tabs = [
    { id: "build", label: "Build", panelIndex: 0 },
    { id: "preview", label: "Preview", disabled: previewDisabled, panelIndex: 1 }
  ];
  const activeTab = activeView === "preview" && !previewDisabled ? "preview" : "build";

  return <div className="named-question-workspace" data-properties-open={propertiesOpen ? "true" : "false"} data-properties-maximized={propertiesMaximized ? "true" : "false"}>
    <StudioPageHeader headingLevel={2} title={title} description={status} actions={<>
      {backAction}
      <StudioButton variant="primary" disabled={previewDisabled || previewBusy} onClick={() => { setActiveView("preview"); onPreview(); }}>{previewBusy ? "Previewing…" : "Preview"}</StudioButton>
      {saveAction}
      {menuItems.length ? <OverflowMenu items={menuItems} ariaLabel="More question actions" /> : null}
    </>} />
    <div className="named-question-view-toolbar">
      <StudioTabs tabs={tabs} activeTab={activeTab} onSelect={setActiveView} ariaLabel="Question workspace views" baseId={baseId} />
      <StudioButton aria-controls={propertiesId} aria-expanded={propertiesOpen} onClick={() => { setPropertiesOpen(value => !value); setPropertiesMaximized(false); }}>Properties</StudioButton>
    </div>
    <div className="named-question-workspace-body">
      <main className="named-question-main" aria-label={activeTab === "build" ? "Build question" : "Question preview"}>
        <StudioTabPanel baseId={baseId} index={0} hidden={activeTab !== "build"} className="named-question-tab-panel">{build}</StudioTabPanel>
        <StudioTabPanel baseId={baseId} index={1} hidden={activeTab !== "preview"} className="named-question-tab-panel">{preview}</StudioTabPanel>
      </main>
      {propertiesOpen ? <aside id={propertiesId} className={`named-question-properties-panel${propertiesMaximized ? " is-maximized" : ""}`} aria-label="Question properties" data-studio-side-panel="inspector">
        <header><h3>Properties</h3><StudioSidePanelActions side="inspector" panelLabel="Properties" collapsed={false} maximized={propertiesMaximized} onToggleCollapsed={() => { setPropertiesOpen(false); setPropertiesMaximized(false); }} onToggleMaximized={() => setPropertiesMaximized(value => !value)} /></header>
        <div ref={propertiesRef} className="named-question-properties-content">{properties}</div>
      </aside> : null}
    </div>
  </div>;
}