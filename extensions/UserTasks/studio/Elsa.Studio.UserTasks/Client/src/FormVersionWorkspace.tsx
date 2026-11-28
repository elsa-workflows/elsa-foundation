import React, { useId } from "react";
import { CopyableIdentifier, StudioTimestamp, StatusChip, StudioTabPanel, StudioTabs } from "@elsa-workflows/studio-ui";
import type { TaskForm } from "./taskApi";

export type FormWorkspaceTab = "form" | "versions";

export function FormWorkspaceTabs({
  primaryLabel,
  activeTab,
  onSelect,
  primary,
  versions,
  primaryClassName
}: {
  primaryLabel: string;
  activeTab: FormWorkspaceTab;
  onSelect(tab: FormWorkspaceTab): void;
  primary: React.ReactNode;
  versions: React.ReactNode;
  primaryClassName?: string;
}) {
  const baseId = useId();
  const tabs = [{ id: "form", label: primaryLabel }, { id: "versions", label: "Versions" }];

  return <div className="user-task-form-workspace">
    <StudioTabs tabs={tabs} activeTab={activeTab} onSelect={tab => onSelect(tab as FormWorkspaceTab)} baseId={baseId} ariaLabel="Form workspace" />
    <StudioTabPanel baseId={baseId} index={0} hidden={activeTab !== "form"} className={primaryClassName ?? "user-task-form-workspace__panel"}>{primary}</StudioTabPanel>
    <StudioTabPanel baseId={baseId} index={1} hidden={activeTab !== "versions"} className="user-task-form-workspace__panel">{versions}</StudioTabPanel>
  </div>;
}

export function PublishedFormSnapshot({ form, createdAt, latestVersion, actions }: {
  form: TaskForm;
  createdAt?: string;
  latestVersion?: string;
  actions?: React.ReactNode;
}) {
  const isLatest = latestVersion === undefined || form.version === latestVersion;
  return <div className="user-task-form-snapshot">
    <div className="user-task-form-snapshot__header">
      <div>
        <div className="user-task-form-snapshot__status">
          <StatusChip tone="neutral">Published version</StatusChip>
          {isLatest ? <StatusChip tone="success">Latest</StatusChip> : null}
        </div>
        <h2>Published version {form.version}</h2>
        <CopyableIdentifier label="Form ID" value={form.id} />
        {form.description ? <p>{form.description}</p> : null}
        {createdAt ? <p><StudioTimestamp value={createdAt} /></p> : null}
      </div>
      {actions ? <div className="user-task-actions">{actions}</div> : null}
    </div>
    <p>This published form is immutable. {isLatest ? "Changes require publishing a new version." : `Version ${latestVersion} is the latest version of this form.`} Open tasks keep this version.</p>
    {form.fields.length ? <dl className="user-task-form-fields">{form.fields.map(field => <div key={field.key}>
      <dt>{field.label}</dt>
      <dd>{field.kind}{field.required ? " · Required" : ""}{field.readOnly ? " · Read only" : ""}</dd>
      <dd><code>{field.key}</code>{field.help ? <p>{field.help}</p> : null}{field.choices ? <p>Choices: {field.choices.join(", ")}</p> : null}
        {field.minimum != null ? <p>Minimum: {field.minimum}</p> : null}{field.maximum != null ? <p>Maximum: {field.maximum}</p> : null}
        {field.maxLength != null ? <p>Maximum characters: {field.maxLength}</p> : null}
      </dd>
    </div>)}</dl> : <p>This form version has no fields.</p>}
  </div>;
}