import { useState } from "react";
import { createRoot } from "react-dom/client";
import { StudioHttpError, type ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import { ThemeProvider } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/components/ThemeProvider";
import { writeAppearancePreferences } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/themes/appearance";
import { TasksPage } from "../../../extensions/UserTasks/studio/Elsa.Studio.UserTasks/Client/src/TasksPage";
import { TaskDetailPage } from "../../../extensions/UserTasks/studio/Elsa.Studio.UserTasks/Client/src/TaskDetailPage";
import { FormsPage } from "../../../extensions/UserTasks/studio/Elsa.Studio.UserTasks/Client/src/FormsPage";
import { BasicTaskForm } from "../../../extensions/UserTasks/studio/Elsa.Studio.UserTasks/Client/src/TaskForm";
import { basicProvider, type FormDefinition, type FormVersion, type TaskForm } from "../../../extensions/UserTasks/studio/Elsa.Studio.UserTasks/Client/src/taskApi";
import "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/styles.css";
import "../../../extensions/UserTasks/studio/Elsa.Studio.UserTasks/Client/src/styles.css";

const params = new URLSearchParams(location.search);
const requests: unknown[] = [];
Object.assign(window, { taskRequests: requests });
writeAppearancePreferences({ themeId: "classic", mode: params.get("theme") === "dark" ? "dark" : "light", workspaceStyle: "linen" });
const task = { id: "purchase", title: "Review purchase request with a long descriptive title", description: "Review the request and record your decision.", assigneeId: "alice", status: "Open", revision: 1,
  actions: ["Approve", "Reject"], optionalFormActions: [], prefill: {}, history: [{ operation: "created", actorId: "workflow", at: "2026-11-04T12:00:00Z" }],
  form: { id: "purchase", version: "1", name: "Purchase response", provider: "elsa.basic-forms/1", fields: [{ key: "Note", label: "Note", kind: "multiline", required: true, readOnly: false }] } };
const completedTask = { ...task, status: "Completed", decision: { response: { action: "Approve", data: { Note: "Saved response" } }, actorId: "alice" } };
const publishedForm = (id: string, name: string, version: string, label: string): FormVersion => ({ form: { ...task.form, id, name, version, fields: [{ ...task.form.fields[0], label }] }, tenantId: "tenant", createdAt: `2026-11-0${version}T12:00:00Z`, createdBy: "alice" });
const listForm = publishedForm("purchase-form-with-a-long-identifier", "Purchase response", "1", "Decision note");
const historyId = "purchase-form-history";
const history = new Map<string, FormVersion[]>([[historyId, [publishedForm(historyId, "Purchase response", "2", "Decision note"), publishedForm(historyId, "Purchase response", "1", "Earlier note")]]]);
const legacyFormId = "newsletter-signup";
const legacyVersions = [publishedForm(legacyFormId, "Newsletter", "2", "Email address"), publishedForm("travel-form", "Travel response", "1", "Travel details"), publishedForm(legacyFormId, "Newsletter", "1", "Earlier note")];
const definition = (version: FormVersion, revision = 0): FormDefinition => ({ tenantId: "tenant", id: version.form.id, latestPublished: version, draft: null, revision });
const definitions: FormDefinition[] = params.has("form-error")
  ? Array.from({ length: 26 }, (_, index) => definition(publishedForm(`form-${String(index + 1).padStart(2, "0")}`, `Form ${index + 1}`, "1", "Note")))
  : params.has("form-history") ? [definition(history.get(historyId)![0])]
    : params.has("populated-forms") ? [definition(listForm)] : [];
const publishedVersions = definitions.flatMap(item => item.latestPublished ? [item.latestPublished] : []);
const legacyBackend = params.get("backend") === "legacy";
const api = {
  dialogs: { confirm: async ({ message }: { message: string }) => window.confirm(message) },
  backendSession: { capabilities: { capabilities: [{ id: "elsa.api.user-tasks", contractVersion: "1", links: [
    { rel: "identity", href: "runtime/user-task-identity" }, { rel: "tasks", href: "runtime/user-tasks" }, { rel: "task", href: "runtime/user-tasks/{id}" },
    { rel: "forms", href: "runtime/user-task-forms" },
    ...(!legacyBackend ? [
      { rel: "form-definitions", href: "runtime/user-task-forms/definitions" },
      { rel: "form-definition", href: "runtime/user-task-forms/{id}/definition" }, { rel: "form-history", href: "runtime/user-task-forms/{id}/versions" },
      { rel: "save-form-draft", href: "runtime/user-task-forms/{id}/draft" }, { rel: "discard-form-draft", href: "runtime/user-task-forms/{id}/discard" },
      { rel: "publish-form", href: "runtime/user-task-forms/{id}/publish" }
    ] : []),
    { rel: "complete", href: "runtime/user-tasks/{id}/complete" }, { rel: "reassign", href: "runtime/user-tasks/{id}/reassign" }
  ] }] } },
  backend: { http: {
    getJson: async (path: string) => {
      if (path.includes("identity")) return { userId: "alice", canRead: !params.has("denied"), canAct: true, canManage: params.has("manage"), canReadForms: !params.has("denied"), canManageForms: !params.has("denied") && !params.has("read-only") };
      if (params.has("error")) throw new Error("The server is unavailable. Please try again.");
      const url = new URL(path, location.origin);
      if (path.includes("/runtime/user-task-forms/definitions")) {
        const after = url.searchParams.get("after");
        const offset = after ? Math.max(0, definitions.findIndex(item => item.id === after) + 1) : 0;
        const items = definitions.filter(item => item.latestPublished || item.draft).slice(offset, offset + 25);
        return { items, next: offset + items.length < definitions.length ? items.at(-1)?.id : null };
      }
      const historyMatch = path.match(/\/runtime\/user-task-forms\/([^/?]+)\/versions/);
      if (historyMatch) {
        const id = decodeURIComponent(historyMatch[1]); const versions = history.get(id) ?? [];
        if (url.searchParams.get("after") === "form-history-page") {
          if (params.has("form-history-error")) throw new StudioHttpError(503, "Older form versions are unavailable");
          return { items: versions.slice(1), next: null };
        }
        return { items: versions.slice(0, 1), next: versions.length > 1 ? "form-history-page" : null };
      }
      const definitionMatch = path.match(/\/runtime\/user-task-forms\/([^/?]+)\/definition/);
      if (definitionMatch) return definitions.find(item => item.id === decodeURIComponent(definitionMatch[1]));
      if (path.includes("/runtime/user-task-forms?")) {
        if (legacyBackend && params.has("legacy-forms")) {
          if (url.searchParams.get("after") === "legacy-page-2") {
            if (params.has("form-history-error")) throw new StudioHttpError(503, "Older form versions are unavailable");
            return { items: [legacyVersions[2]], next: null };
          }
          return { items: legacyVersions.slice(0, 2), next: "legacy-page-2" };
        }
        return { items: publishedVersions, next: null };
      }
      if (path.includes("?")) {
        if (params.has("cursor-error") && new URL(path, location.origin).searchParams.has("after")) throw new StudioHttpError(410, "The inbox cursor expired");
        const search = url.searchParams.get("search")?.toLowerCase() ?? "";
        const items = params.has("empty") ? [] : params.has("two") ? [task, { ...task, id: "travel", title: "Approve travel request" }] : [task];
        return { items: items.filter(item => item.title.toLowerCase().includes(search)) };
      }
      return path.endsWith("/travel") ? { ...task, id: "travel", title: "Approve travel request" } : params.has("completed") ? completedTask : task;
    }, postJson: async (path: string, data: unknown) => {
      if (path.includes("/runtime/user-tasks/")) requests.push(data);
      if (path.endsWith("/publish") && params.has("form-error")) throw new StudioHttpError(409, "This form changed elsewhere");
      if (path.includes("/runtime/user-tasks/") && params.has("ambiguous")) throw new StudioHttpError(0, "Connection lost");
      if (path.endsWith("/reassign")) {
        if (params.has("reassign-error")) throw new StudioHttpError(503, "Reassignment failed");
        const input = data as { revision: number; assigneeId: string | null; groupId: string | null };
        return { ...task, revision: input.revision + 1, assigneeId: input.assigneeId ?? undefined, groupId: input.groupId ?? undefined,
          history: [...task.history, { operation: "reassigned", actorId: "alice", at: "2026-11-21T01:00:00Z" }] };
      }
      const id = decodeURIComponent(path.split("/").at(-2)!);
      const current = definitions.find(item => item.id === id);
      if (path.endsWith("/draft")) {
        const input = data as { revision: number; form: TaskForm };
        const draft = { tenantId: "tenant", id, form: input.form, revision: input.revision + 1, updatedAt: "2026-11-20T12:00:00Z", updatedBy: "alice" };
        const updated: FormDefinition = { tenantId: "tenant", id, latestPublished: current?.latestPublished ?? null, draft, revision: draft.revision };
        if (current) definitions[definitions.indexOf(current)] = updated; else definitions.push(updated);
        return updated;
      }
      if (path.endsWith("/discard")) {
        const input = data as { revision: number };
        const updated: FormDefinition = { tenantId: "tenant", id, latestPublished: current?.latestPublished ?? null, draft: null, revision: input.revision + 1 };
        if (current) definitions[definitions.indexOf(current)] = updated;
        return updated;
      }
      if (path.endsWith("/publish")) {
        const input = data as { revision: number; form: TaskForm };
        const nextVersion = String(Number(current?.latestPublished?.form.version ?? 0) + 1);
        const publication = publishedForm(id, input.form.name, nextVersion, input.form.fields[0]?.label ?? "Note");
        publication.form = { ...input.form, version: nextVersion };
        publishedVersions.push(publication); const versions = history.get(id) ?? [];
        history.set(id, [publication, ...versions]);
        const updated: FormDefinition = { tenantId: "tenant", id, latestPublished: publication, draft: null, revision: input.revision + 1 };
        if (current) definitions[definitions.indexOf(current)] = updated; else definitions.push(updated);
        return publication;
      }
      return {};
    }
  } }, userTaskForms: { list: () => [{ id: "basic", provider: basicProvider, component: BasicTaskForm }] }
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
    window.history.replaceState(window.history.state, "", `${fixturePath}?${query}`);
    setTaskId(id);
  };
  return params.has("forms") ? <FormsPage api={api} /> : taskId ? <TaskDetailPage api={api} id={taskId} navigate={navigate} /> : <TasksPage api={api} navigate={navigate} />;
}
createRoot(document.getElementById("root")!).render(<ThemeProvider><main style={{ padding: 24, minWidth: 0 }}><Fixture /></main></ThemeProvider>);
