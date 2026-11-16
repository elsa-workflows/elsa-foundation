import { useState } from "react";
import { createRoot } from "react-dom/client";
import { StudioHttpError, type ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import { ThemeProvider } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/components/ThemeProvider";
import { writeAppearancePreferences } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/themes/appearance";
import { TasksPage } from "../../../Extensions/UserTasks/studio/Elsa.Studio.UserTasks/Client/src/TasksPage";
import { TaskDetailPage } from "../../../Extensions/UserTasks/studio/Elsa.Studio.UserTasks/Client/src/TaskDetailPage";
import { FormsPage } from "../../../Extensions/UserTasks/studio/Elsa.Studio.UserTasks/Client/src/FormsPage";
import { BasicTaskForm } from "../../../Extensions/UserTasks/studio/Elsa.Studio.UserTasks/Client/src/TaskForm";
import "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/styles.css";
import "../../../Extensions/UserTasks/studio/Elsa.Studio.UserTasks/Client/src/styles.css";

const params = new URLSearchParams(location.search);
const requests: unknown[] = [];
Object.assign(window, { taskRequests: requests });
writeAppearancePreferences({ themeId: "classic", mode: params.get("theme") === "dark" ? "dark" : "light", workspaceStyle: "linen" });
const task = { id: "purchase", title: "Review purchase request with a long descriptive title", description: "Review the request and record your decision.", assigneeId: "alice", status: "Open", revision: 1,
  actions: ["Approve", "Reject"], optionalFormActions: [], prefill: {}, history: [{ operation: "created", actorId: "workflow", at: "2026-11-04T12:00:00Z" }],
  form: { id: "purchase", version: "1", name: "Purchase response", provider: "elsa.basic-forms/1", fields: [{ key: "Note", label: "Note", kind: "multiline", required: true, readOnly: false }] } };
const api = {
  dialogs: { confirm: async ({ message }: { message: string }) => window.confirm(message) },
  backendSession: { capabilities: { capabilities: [{ id: "elsa.api.user-tasks", contractVersion: "1", links: [
    { rel: "identity", href: "runtime/user-task-identity" }, { rel: "tasks", href: "runtime/user-tasks" }, { rel: "task", href: "runtime/user-tasks/{id}" }, { rel: "forms", href: "runtime/user-task-forms" }, { rel: "complete", href: "runtime/user-tasks/{id}/complete" }
  ] }] } },
  backend: { http: {
    getJson: async (path: string) => {
      if (path.includes("identity")) return { userId: "alice", canRead: !params.has("denied"), canAct: true, canManage: false, canReadForms: !params.has("denied"), canManageForms: !params.has("denied") };
      if (params.has("error")) throw new Error("The server is unavailable. Please try again.");
      if (path.includes("forms")) return { items: params.has("populated-forms") ? [{ form: { ...task.form, id: "purchase-form-with-a-long-identifier" } }] : [], next: params.has("form-error") ? "form-cursor" : null };
      if (path.includes("?")) {
        if (params.has("cursor-error") && new URL(path, location.origin).searchParams.has("after")) throw new StudioHttpError(410, "The inbox cursor expired");
        const search = new URL(path, location.origin).searchParams.get("search")?.toLowerCase() ?? "";
        const items = params.has("empty") ? [] : params.has("two") ? [task, { ...task, id: "travel", title: "Approve travel request" }] : [task];
        return { items: items.filter(item => item.title.toLowerCase().includes(search)) };
      }
      return path.endsWith("/travel") ? { ...task, id: "travel", title: "Approve travel request" } : task;
    }, postJson: async (_path: string, data: unknown) => {
      requests.push(data);
      if (params.has("form-error")) throw new StudioHttpError(409, "This version already exists");
      if (params.has("ambiguous")) throw new StudioHttpError(0, "Connection lost");
      return {};
    }
  } }, userTaskForms: { list: () => [{ provider: "elsa.basic-forms/1", component: BasicTaskForm }] }
} as unknown as ElsaStudioModuleApi;
// This is isolated UI evidence. The fixture adapter supplies routing; the composed suite tests the real Studio router.
const fixturePath = location.pathname;
function Fixture() {
  const [taskId, setTaskId] = useState(params.get("task"));
  const navigate = (path: string) => {
    const destination = new URL(path, location.origin);
    const matched = destination.pathname.match(/\/user-tasks\/([^/]+)$/);
    const id = matched ? decodeURIComponent(matched[1]) : null;
    const query = new URLSearchParams(params);
    if (id) query.set("task", id); else query.delete("task");
    for (const key of ["view", "search", "after", "focusTask", "backend"]) {
      if (destination.searchParams.has(key)) query.set(key, destination.searchParams.get(key)!); else query.delete(key);
    }
    history.replaceState(history.state, "", `${fixturePath}?${query}`);
    setTaskId(id);
  };
  return params.has("forms") ? <FormsPage api={api} /> : taskId ? <TaskDetailPage api={api} id={taskId} navigate={navigate} /> : <TasksPage api={api} navigate={navigate} />;
}
createRoot(document.getElementById("root")!).render(<ThemeProvider><main style={{ padding: 24, minWidth: 0 }}><Fixture /></main></ThemeProvider>);
