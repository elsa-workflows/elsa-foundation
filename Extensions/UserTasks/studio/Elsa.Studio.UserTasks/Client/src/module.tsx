import React from "react";
import { ClipboardList, FileText } from "lucide-react";
import { useStudioRouteLocation } from "@elsa-workflows/studio-sdk";
import type { ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import { TasksPage } from "./TasksPage";
import { TaskDetailPage } from "./TaskDetailPage";
import { FormsPage } from "./FormsPage";
import { BasicTaskForm } from "./TaskForm";
import { ActionsEditor, AssignmentEditor, FormEditor } from "./PropertyEditors";
import { basicProvider, taskCapability } from "./taskApi";
import "./styles.css";
function DetailRoute({ api, navigate }: { api: ElsaStudioModuleApi; navigate(path: string): void }) {
  const location = useStudioRouteLocation();
  let id: string;
  try { id = decodeURIComponent(location.pathname.split("/").at(-1) ?? ""); } catch { return <p role="alert">Invalid task address.</p>; }
  return <TaskDetailPage key={id} api={api} id={id} navigate={navigate} />;
}
export function register(api: ElsaStudioModuleApi) {
  api.userTaskForms?.add({ id: "user-tasks.basic", provider: basicProvider, component: BasicTaskForm });
  api.featureAreas.add({ id: "user-tasks", title: "User Tasks", navGroup: "Workspace", order: 30, description: "Assigned work, group queues and reusable forms.", ownedPaths: ["/user-tasks"], required: false, defaultEnabled: true,
    nav: { title: "User Tasks", icon: ClipboardList, path: "/user-tasks", placement: "operate", order: 30, items: [
      { title: "Inbox", icon: ClipboardList, path: "/user-tasks", placement: "operate", order: 40, requiresPermission: ["user-tasks:read", "user-tasks:manage"] },
      { title: "Forms", icon: FileText, path: "/user-tasks/forms", placement: "operate", order: 41, requiresPermission: ["user-task-forms:read", "user-task-forms:manage"] }
    ], requiresBackend: taskCapability, requiresPermission: ["user-tasks:read", "user-tasks:manage", "user-task-forms:read", "user-task-forms:manage"] },
    routes: [
      { id: "user-tasks.forms", path: "/user-tasks/forms", label: "Task forms", requiresBackend: taskCapability, component: () => <FormsPage api={api} /> },
      { id: "user-tasks.detail", path: "/user-tasks/:id", label: "Task", requiresBackend: taskCapability, workspace: { resourceKey: location => { try { return `user-task:${decodeURIComponent(location.pathname.split("/").at(-1) ?? "")}`; } catch { return null; } } }, component: ({ navigate }) => <DetailRoute api={api} navigate={navigate} /> },
      { id: "user-tasks", path: "/user-tasks", label: "User Tasks", requiresBackend: taskCapability, component: ({ navigate }) => <TasksPage api={api} navigate={navigate} /> }
    ] });
  api.propertyEditors.add({ id: "user-tasks.actions", requiresBackend: taskCapability, order: 50, supportsValidationReporting: true, supports: (descriptor, context) => descriptor.uiHint === "user-task-actions" && context.scope === "collection", component: ActionsEditor });
  for (const group of [false, true]) api.propertyEditors.add({ id: `user-tasks.${group ? "group" : "user"}`, requiresBackend: taskCapability, order: 50, supportsValidationReporting: true, readsActivityContext: true,
    supports: descriptor => descriptor.uiHint === (group ? "user-task-group" : "user-task-user"), component: props => <AssignmentEditor {...props} api={api} group={group} /> });
  api.propertyEditors.add({ id: "user-tasks.form", requiresBackend: taskCapability, order: 50, supportsValidationReporting: true, readsActivityContext: true, supports: (descriptor, context) => ["user-task-form", "user-task-form-version"].includes(descriptor.uiHint ?? "") || (descriptor.name === "FormVersion" && Object.hasOwn(context.inputValues ?? {}, "OptionalFormActions") && Object.hasOwn(context.inputValues ?? {}, "FormId")), component: props => <FormEditor {...props} api={api} /> });
}
