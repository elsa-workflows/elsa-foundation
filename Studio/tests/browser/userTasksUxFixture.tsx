import { createRoot } from "react-dom/client";
import type { ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import { ThemeProvider } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/components/ThemeProvider";
import { writeAppearancePreferences } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/themes/appearance";
import { TasksPage } from "../../../Extensions/UserTasks/studio/Elsa.Studio.UserTasks/Client/src/TasksPage";
import { FormsPage } from "../../../Extensions/UserTasks/studio/Elsa.Studio.UserTasks/Client/src/FormsPage";
import { BasicTaskForm } from "../../../Extensions/UserTasks/studio/Elsa.Studio.UserTasks/Client/src/TaskForm";
import "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/styles.css";
import "../../../Extensions/UserTasks/studio/Elsa.Studio.UserTasks/Client/src/styles.css";

const params = new URLSearchParams(location.search);
writeAppearancePreferences({ themeId: "classic", mode: params.get("theme") === "dark" ? "dark" : "light", workspaceStyle: "linen" });
const task = { id: "purchase", title: "Review purchase request with a long descriptive title", description: "Review the request and record your decision.", assigneeId: "alice", status: "Open", revision: 1,
  actions: ["Approve", "Reject"], optionalFormActions: [], prefill: {}, history: [{ operation: "created", actorId: "workflow", at: "2026-11-04T12:00:00Z" }],
  form: { id: "purchase", version: "1", name: "Purchase response", provider: "elsa.basic-forms/1", fields: [{ key: "Note", label: "Note", kind: "multiline", required: true, readOnly: false }] } };
const api = {
  dialogs: { confirm: async ({ message }: { message: string }) => window.confirm(message) },
  backendSession: { capabilities: { capabilities: [{ id: "elsa.api.user-tasks", contractVersion: "1", links: [
    { rel: "identity", href: "runtime/user-task-identity" }, { rel: "tasks", href: "runtime/user-tasks" }, { rel: "task", href: "runtime/user-tasks/{id}" }, { rel: "forms", href: "runtime/user-task-forms" }
  ] }] } },
  backend: { http: {
    getJson: async (path: string) => {
      if (path.includes("identity")) return { userId: "alice", canRead: !params.has("denied"), canAct: true, canManage: false, canReadForms: !params.has("denied"), canManageForms: !params.has("denied") };
      if (params.has("error")) throw new Error("The server is unavailable. Please try again.");
      if (path.includes("forms")) return { items: [] };
      if (path.includes("?")) return { items: !params.has("empty") && !new URL(path, location.origin).searchParams.get("search") ? [task] : [] };
      return task;
    }, postJson: async () => ({})
  } }, userTaskForms: { list: () => [{ provider: "elsa.basic-forms/1", component: BasicTaskForm }] }
} as unknown as ElsaStudioModuleApi;
createRoot(document.getElementById("root")!).render(<ThemeProvider><main style={{ padding: 24, minWidth: 0 }}>
  {params.has("forms") ? <FormsPage api={api} /> : <TasksPage api={api} />}
</main></ThemeProvider>);
