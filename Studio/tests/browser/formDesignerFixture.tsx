import React, { useCallback, useMemo, useState } from "react";
import { createRoot } from "react-dom/client";
import { FormsPage } from "../../../extensions/UserTasks/studio/Elsa.Studio.UserTasks/Client/src/FormsPage";
import { basicProvider, type FormDefinition, type FormVersion, type TaskActor, type TaskField, type TaskForm } from "../../../extensions/UserTasks/studio/Elsa.Studio.UserTasks/Client/src/taskApi";
import { builtInTaskFieldControls } from "../../../extensions/UserTasks/studio/Elsa.Studio.UserTasks/Client/src/fieldControls";
import { createContributionRegistry, studioSlots, type ElsaStudioModuleApi, type StudioUserTaskFieldControlContribution } from "@elsa-workflows/studio-sdk";
import { StudioField } from "@elsa-workflows/studio-ui";
import { ThemeProvider } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/components/ThemeProvider";
import "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/styles.css";
import "../../../extensions/UserTasks/studio/Elsa.Studio.UserTasks/Client/src/styles.css";
import "./formDesignerFixture.css";

const fixtureSearch = new URLSearchParams(window.location.search);
const existingForm: TaskForm = {
  id: "contact",
  version: "1",
  name: "Contact form",
  provider: basicProvider,
  fields: fixtureSearch.has("nullable-settings")
    ? JSON.parse(`[
      {"key":"accepted","label":"Accept terms","kind":"boolean","required":false,"readOnly":false,"help":null,"choices":null,"mustBeTrue":null,"presentation":{"display":"radio","trueLabel":null,"falseLabel":"No","unansweredLabel":null}},
      {"key":"amount","label":"Amount","kind":"number","required":false,"readOnly":false,"minimum":null,"maximum":10,"maxLength":null,"configuration":null},
      {"key":"rating","label":"Rating","kind":"number","required":false,"readOnly":false,"controlId":"example.rating/1","configuration":{"max":5}}
    ]`) as TaskField[]
    : fixtureSearch.has("missing-control")
    ? [{ key: "rating", label: "Rating", kind: "number", required: true, readOnly: false, controlId: "example.rating/1", configuration: { max: 5 } }]
    : [{ key: "email", label: "Email address", kind: "text", required: true, readOnly: false }]
};
const versions: FormVersion[] = [{ tenantId: "fixture", form: existingForm, createdAt: "2026-11-20T00:00:00Z", createdBy: "fixture-author" }];
const definitions: FormDefinition[] = [{ tenantId: "fixture", id: existingForm.id, latestPublished: versions[0], draft: null, revision: 0 }];
const formRequests: { path: string; body: unknown }[] = [];
Object.assign(window, { formRequests });

function createFixtureApi(onPublish: (form: TaskForm) => void, legacy: boolean) {
  const search = new URLSearchParams(window.location.search);
  const identity: TaskActor = { userId: "fixture-author", canRead: true, canAct: true, canManage: true, canManageForms: !search.has("read-only"), canReadForms: true };
  const links = [
    { rel: "identity", href: "user-tasks/identity" },
    { rel: "forms", href: "user-tasks/forms" },
    { rel: "form-definitions", href: "user-tasks/forms/definitions" },
    { rel: "form-definition", href: "user-tasks/forms/{id}/definition" },
    { rel: "form-history", href: "user-tasks/forms/{id}/versions" },
    { rel: "save-form-draft", href: "user-tasks/forms/{id}/draft" },
    { rel: "discard-form-draft", href: "user-tasks/forms/{id}/discard" },
    { rel: "publish-form", href: "user-tasks/forms/{id}/publish" },
    { rel: "delete-form", href: "user-tasks/forms/{id}/delete" }
  ];
  const capability = { id: "elsa.api.user-tasks", contractVersion: "1", status: "available", links: legacy ? links.filter(({ rel }) => rel === "identity" || rel === "forms") : search.has("delete-unavailable") ? links.filter(({ rel }) => rel !== "delete-form") : links };
  const userTaskFieldControls = createContributionRegistry<StudioUserTaskFieldControlContribution>({ slot: studioSlots.userTaskFieldControls });
  for (const control of builtInTaskFieldControls) userTaskFieldControls.add(control);
  const http = {
    async getJson<T>(path: string): Promise<T> {
      if (path.includes("/identity")) return identity as T;
      if (path.includes("/definitions")) return { items: definitions, next: null } as T;
      const historyMatch = path.match(/\/forms\/([^/]+)\/versions/);
      if (historyMatch) return { items: versions.filter(item => item.form.id === decodeURIComponent(historyMatch[1])), next: null } as T;
      const definitionMatch = path.match(/\/forms\/([^/]+)\/definition/);
      if (definitionMatch) return definitions.find(item => item.id === decodeURIComponent(definitionMatch[1])) as T;
      if (path.includes("/forms?")) return { items: versions, next: null } as T;
      throw new Error(`Unexpected fixture request: ${path}`);
    },
    async postJson<T>(path: string, value: unknown): Promise<T> {
      formRequests.push({ path, body: structuredClone(value) });
      if (path.endsWith("/forms")) {
        const request = structuredClone(value as TaskForm);
        const latestVersion = Math.max(0, ...versions.filter(item => item.form.id === request.id).map(item => Number(item.form.version)));
        const form = { ...request, version: String(latestVersion + 1) };
        const published: FormVersion = { tenantId: "fixture", form, createdAt: "2026-11-20T00:00:00Z", createdBy: identity.userId };
        versions.push(published);
        const definition: FormDefinition = { tenantId: "fixture", id: form.id, latestPublished: published, draft: null, revision: latestVersion + 1 };
        const current = definitions.find(item => item.id === form.id);
        if (current) definitions[definitions.indexOf(current)] = definition; else definitions.push(definition);
        onPublish(published.form);
        return published as T;
      }
      const id = decodeURIComponent(path.split("/").at(-2)!);
      const current = definitions.find(item => item.id === id);
      if (path.endsWith("/draft")) {
        if (search.has("save-fails")) throw new Error("Draft save failed in fixture.");
        if (search.has("save-conflicts")) throw Object.assign(new Error("The form changed. Reload the latest draft."), { status: 409 });
        const delay = Number(search.get("save-delay") ?? 0);
        if (delay > 0) await new Promise(resolve => window.setTimeout(resolve, delay));
        const request = value as { form: TaskForm; revision: number };
        if (current && current.revision !== request.revision) throw Object.assign(new Error("The form changed. Reload the latest draft."), { status: 409 });
        const draft = { tenantId: "fixture", id, form: structuredClone(request.form), revision: request.revision + 1, updatedAt: "2026-11-20T00:00:00Z", updatedBy: identity.userId };
        const definition: FormDefinition = { tenantId: "fixture", id, latestPublished: current?.latestPublished ?? null, draft, revision: draft.revision };
        if (current) definitions[definitions.indexOf(current)] = definition; else definitions.push(definition);
        return definition as T;
      }
      if (path.endsWith("/discard")) {
        const request = value as { revision: number };
        const definition: FormDefinition = { tenantId: "fixture", id, latestPublished: current?.latestPublished ?? null, draft: null, revision: request.revision + 1 };
        if (current) definitions[definitions.indexOf(current)] = definition;
        return definition as T;
      }
      if (path.endsWith("/delete")) {
        if (search.has("delete-fails")) throw new Error("Delete failed in fixture.");
        const request = value as { revision: number };
        if (!current || current.revision !== request.revision) throw Object.assign(new Error("The form changed. Refresh and try again."), { status: 409 });
        definitions.splice(definitions.indexOf(current), 1);
        return { ...current, draft: null, revision: current.revision + 1, deleted: true } as T;
      }
      const request = value as { form: TaskForm; revision: number };
      const latestVersion = Number(current?.latestPublished?.form.version ?? 0);
      const form = { ...structuredClone(request.form), version: String(latestVersion + 1) };
      const published: FormVersion = { tenantId: "fixture", form, createdAt: "2026-11-20T00:00:00Z", createdBy: identity.userId };
      versions.push(published);
      const definition: FormDefinition = { tenantId: "fixture", id, latestPublished: published, draft: null, revision: request.revision + 1 };
      if (current) definitions[definitions.indexOf(current)] = definition; else definitions.push(definition);
      onPublish(published.form);
      return published as T;
    }
  };
  const api = {
    backendSession: { capabilities: { capabilities: [capability] } },
    backend: { http },
    userTaskFieldControls,
    dialogs: { confirm: async ({ message }: { message: string }) => window.confirm(message) }
  } as unknown as ElsaStudioModuleApi;
  if (search.has("custom-control")) registerExampleRatingModule(api);
  return api;
}

const exampleRatingControl: StudioUserTaskFieldControlContribution = {
  id: "example.rating/1",
  label: "Rating",
  valueKind: "number",
  order: 20,
  createField: fields => ({ key: `rating${fields.length + 1}`, label: "Rating", kind: "number", required: false, readOnly: false, configuration: { max: 5 } }),
  component: ({ field, id, disabled, value, error, onChange }) => {
    const configuration = (field as TaskField).configuration ?? {};
    const maximum = typeof configuration.max === "number" ? configuration.max : 5;
    return <input id={id} data-studio-field-control="" type="number" min="1" max={maximum} value={typeof value === "number" ? value : ""} disabled={disabled} aria-invalid={Boolean(error)} onChange={event => onChange(event.target.value ? Number(event.target.value) : undefined)} />;
  },
  propertiesEditor: ({ field, disabled, onChange }) => {
    const typedField = field as TaskField;
    const maximum = typeof typedField.configuration?.max === "number" ? typedField.configuration.max : 5;
    return <StudioField label="Maximum rating"><input type="number" min="1" max="10" value={maximum} disabled={disabled}
      onChange={event => onChange({ ...typedField, configuration: { ...typedField.configuration, max: Number(event.target.value) } })} /></StudioField>;
  },
  validateConfiguration: configuration => typeof configuration === "object" && configuration !== null && !Array.isArray(configuration) &&
    typeof (configuration as Record<string, unknown>).max === "number" && (configuration as Record<string, number>).max >= 1 && (configuration as Record<string, number>).max <= 10
      ? [] : ["Maximum rating must be between 1 and 10."]
};

function registerExampleRatingModule(api: ElsaStudioModuleApi) {
  api.userTaskFieldControls?.add(exampleRatingControl);
}

function Fixture() {
  const [published, setPublished] = useState<TaskForm | null>(null);
  const onPublish = useCallback((form: TaskForm) => setPublished(form), []);
  const legacy = new URLSearchParams(window.location.search).has("legacy");
  const api = useMemo(() => createFixtureApi(onPublish, legacy), [onPublish, legacy]);
  return <ThemeProvider>
    <main className="browser-form-designer">
      <FormsPage api={api} />
      <output data-testid="published-form">{published ? JSON.stringify(published) : ""}</output>
    </main>
  </ThemeProvider>;
}

createRoot(document.getElementById("root")!).render(<Fixture />);