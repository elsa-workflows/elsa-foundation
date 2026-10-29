import React, { useState } from "react";
import { createRoot } from "react-dom/client";
import "@xyflow/react/dist/style.css";
import { ShellFrame, getStudioNavigation } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/App";
import { ThemeProvider } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/components/ThemeProvider";
import { ThemeSwitcher } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/components/ThemeSwitcher";
import { AppearancePage } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/settings/AppearancePage";
import { ActivityDuplicateEditorFixture } from "./activityDuplicateEditorFixture";
import { WorkflowDefinitionBrowseFixture } from "./workflowDefinitionBrowseFixture";
import { WorkflowPublishChoiceFixture } from "./workflowPublishChoiceFixture";
import { DialogHost } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/ui/dialog/DialogHost";
import { dialogs } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/dialogs";
import "../../src/essentials/Elsa.Studio.Workflows/Client/src/workspaceAppearance.css";
import "./workspaceAppearanceFixture.css";

const navigation = getStudioNavigation([
  { id: "home", label: "Home", path: "/dashboard", placement: "main", order: 10 },
  { id: "workflows", label: "Workflows", path: "/workflows/definitions", placement: "main", order: 20 },
  { id: "workflows-runs", label: "Runs", path: "/workflows/instances", placement: "main", order: 30 },
  { id: "executables", label: "Executables", path: "/workflows/executables", placement: "more" }
], { includeThemeBuilder: false });

function Fixture() {
  const [view, setView] = useState(new URLSearchParams(location.search).get("view") ?? "editor");
  const path = view === "appearance" ? "/settings/appearance" : "/workflows/definitions";
  return <ThemeProvider>
    <ShellFrame navigation={navigation} panels={[]} path={path} title={view === "appearance" ? "Appearance" : "Workflows"}
      backendBaseUrl="https://backend.example/" themeAction={<ThemeSwitcher />}
      onNavigate={next => setView(next.startsWith("/settings") ? "appearance" : "list")}>
      {view === "appearance" ? <AppearancePage onNavigate={() => {}} /> : view === "list" ? <WorkflowDefinitionBrowseFixture /> : view === "publication" ? <WorkflowPublishChoiceFixture /> : view === "dialogs" ? <>
        <button onClick={() => void dialogs.confirm({ title: "Delete workflow", message: "Delete this workflow?", tone: "danger" })}>Delete workflow</button>
        <button onClick={() => void dialogs.confirm({ title: "Publish workflow", message: "Publish this workflow?" })}>Publish workflow</button>
        <DialogHost />
      </> : <ActivityDuplicateEditorFixture />}
    </ShellFrame>
  </ThemeProvider>;
}

createRoot(document.getElementById("root")!).render(<Fixture />);
