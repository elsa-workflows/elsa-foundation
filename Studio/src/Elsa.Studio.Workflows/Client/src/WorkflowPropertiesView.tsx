import { formatDate } from "./workflowFormatting";
import type { WorkflowDefinitionDetails, WorkflowDraft } from "./workflowTypes";

// Field-name fallbacks for the various casings the backend may use.
const collectionNameKeys = ["name", "Name"];
const collectionTypeKeys = ["typeName", "TypeName", "type", "Type"];
const collectionValueKeys = ["value", "Value", "defaultValue", "DefaultValue"];

function isPlainRecord(value: unknown): value is Record<string, unknown> {
  return !!value && typeof value === "object" && !Array.isArray(value);
}

function readCollectionField(record: Record<string, unknown>, keys: string[]) {
  for (const key of keys) {
    if (key in record && record[key] != null) return String(record[key]);
  }
  return "";
}

function CollectionList({ title, emptyLabel, items }: {
  title: string;
  emptyLabel: string;
  items: unknown[];
}) {
  return (
    <section className="wf-properties-section">
      <h3>{title}</h3>
      {items.length === 0 ? (
        <p className="wf-muted">{emptyLabel}</p>
      ) : (
        <table className="wf-properties-table">
          <thead>
            <tr>
              <th>Name</th>
              <th>Type</th>
              <th>Default</th>
            </tr>
          </thead>
          <tbody>
            {items.map((item, index) => {
              const record = isPlainRecord(item) ? item : null;
              if (!record) {
                return <tr key={index}><td colSpan={3}><code>{JSON.stringify(item)}</code></td></tr>;
              }
              const name = readCollectionField(record, collectionNameKeys);
              const type = readCollectionField(record, collectionTypeKeys);
              const value = readCollectionField(record, collectionValueKeys);
              return (
                <tr key={index}>
                  <td>{name || <span className="wf-muted">—</span>}</td>
                  <td>{type || <span className="wf-muted">—</span>}</td>
                  <td>{value || <span className="wf-muted">—</span>}</td>
                </tr>
              );
            })}
          </tbody>
        </table>
      )}
    </section>
  );
}

export function WorkflowPropertiesView({ details, draft }: {
  details: WorkflowDefinitionDetails | null;
  draft: WorkflowDraft;
}) {
  const variables = (draft.state.variables ?? []) as unknown[];
  const inputs = (draft.state.inputs ?? []) as unknown[];
  const outputs = (draft.state.outputs ?? []) as unknown[];
  const versions = details?.versions ?? [];
  const description = details?.definition.description?.trim();

  return (
    <div className="wf-properties-view">
      <section className="wf-properties-section">
        <h3>Information</h3>
        <dl className="wf-properties-info">
          <dt>Name</dt>
          <dd>{details?.definition.name ?? "—"}</dd>
          <dt>Description</dt>
          <dd>{description ? description : <span className="wf-muted">No description</span>}</dd>
          <dt>Definition ID</dt>
          <dd><code>{draft.definitionId}</code></dd>
        </dl>
        <p className="wf-muted wf-properties-hint">Workflow metadata is read-only here for now. Editing variables, inputs and outputs is coming soon.</p>
      </section>

      <CollectionList title="Variables" emptyLabel="No variables defined." items={variables} />
      <CollectionList title="Inputs" emptyLabel="No inputs defined." items={inputs} />
      <CollectionList title="Outputs" emptyLabel="No outputs defined." items={outputs} />

      <section className="wf-properties-section">
        <h3>Versions</h3>
        {versions.length === 0 ? (
          <p className="wf-muted">No published versions yet.</p>
        ) : (
          <ul className="wf-properties-versions">
            {versions.map(version => (
              <li key={version.id}>
                <span className="wf-properties-version-tag">v{version.version}</span>
                <time>{formatDate(version.createdAt)}</time>
              </li>
            ))}
          </ul>
        )}
      </section>
    </div>
  );
}
