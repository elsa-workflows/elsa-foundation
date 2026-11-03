import React, { useId } from "react";
import type { EtlColumn, EtlConnectorBindingSummary, EtlDiagnostic, EtlPipelineV2 } from "../etlTypes";
import { EtlConnectorBudgetsEditor } from "./EtlConnectorBudgetsEditor";
import { EtlConnectorLookupEditor } from "./EtlConnectorLookupEditor";
import { EtlConnectorSourceEditor } from "./EtlConnectorSourceEditor";
import { FilterEditor } from "./FilterEditor";
import { MappingEditor } from "./MappingEditor";
import { PipelineBindingSelect } from "./PipelineBindingSelect";

export function EtlConnectorPipelineEditor({
  pipeline,
  onChange,
  bindings,
  readOnly = false,
  diagnostics = [],
  bindingsLoading = false,
  bindingsUnavailable = false
}: {
  pipeline: EtlPipelineV2;
  onChange(pipeline: EtlPipelineV2): void;
  bindings: readonly EtlConnectorBindingSummary[];
  readOnly?: boolean;
  diagnostics?: readonly EtlDiagnostic[];
  bindingsLoading?: boolean;
  bindingsUnavailable?: boolean;
}) {
  const titleId = useId();
  const update = <K extends keyof EtlPipelineV2>(key: K, value: EtlPipelineV2[K]) => onChange({ ...pipeline, [key]: value });
  const sourceSchema = columns(pipeline);
  const lookupSchema: EtlColumn[] = pipeline.lookup?.schema ?? [];

  return (
    <div className="etl-pipeline-editor" aria-labelledby={titleId}>
      <header className="etl-editor-header">
        <div>
          <p className="etl-editor-eyebrow">{pipeline.continuous ? "Continuous connector pipeline" : "Finite connector pipeline"}</p>
          <h2 id={titleId}>Pipeline stages</h2>
          <p>Configure approved inputs, typed transformation, and independent output routes.</p>
        </div>
      </header>

      <ol className="etl-editor-order" aria-label="ETL operation order">
        <li><span>1</span><div><strong>Read source records</strong><small>{pipeline.source.kind === "csv" ? "Validate the declared CSV schema" : pipeline.source.kind === "jsonl" ? "Read one JSON object per line" : "Read bounded HTTP JSON pages"}</small></div></li>
        <li><span>2</span><div><strong>Match optional lookup</strong><small>{pipeline.lookup ? "Join CSV rows by exact typed keys" : "No lookup is configured"}</small></div></li>
        <li><span>3</span><div><strong>Map output fields</strong><small>Use declared source or lookup values</small></div></li>
        <li><span>4</span><div><strong>Apply output filters</strong><small>Equality or inequality over typed fields</small></div></li>
        <li><span>5</span><div><strong>Route both outcomes</strong><small>Choose HTTP, JSONL or CSV independently</small></div></li>
      </ol>

      {bindingsLoading ? <p className="etl-notice" role="status" aria-live="polite">Loading current approved connector aliases. Existing pipeline selections remain available while they load.</p> : null}
      {bindingsUnavailable ? <p className="etl-error" role="alert">Approved connector aliases could not be loaded. Retry loading bindings before choosing a replacement.</p> : null}
      {diagnostics.length > 0 ? (
        <div className="etl-editor-diagnostics" role="status" aria-live="polite">
          <h3>Validation feedback</h3>
          <ul>{diagnostics.map((diagnostic, index) => <li key={`${diagnostic.code}:${diagnostic.path}:${index}`}><code>{diagnostic.code}</code>{diagnostic.path ? <span className="etl-editor-diagnostic-path">{diagnostic.path}</span> : null}<span>{diagnostic.message}</span></li>)}</ul>
        </div>
      ) : null}

      <EtlConnectorSourceEditor
        source={pipeline.source}
        bindings={bindings}
        maxFields={pipeline.limits.maxFields}
        bindingsLoading={bindingsLoading}
        bindingsUnavailable={bindingsUnavailable}
        disabled={readOnly}
        onChange={source => onChange(withoutUnsupportedContinuous({ ...pipeline, source }))}
      />
      <RunModeEditor pipeline={pipeline} disabled={readOnly} onChange={onChange} />
      <EtlConnectorLookupEditor
        lookup={pipeline.lookup}
        sourceSchema={sourceSchema}
        bindings={bindings}
        maxFields={pipeline.limits.maxFields}
        bindingsLoading={bindingsLoading}
        bindingsUnavailable={bindingsUnavailable}
        disabled={readOnly}
        onChange={lookup => update("lookup", lookup)}
      />
      <MappingEditor mapping={pipeline.mapping} sourceSchema={sourceSchema} lookupSchema={lookupSchema} sourceLabel={pipeline.source.kind === "csv" ? "Source CSV" : "Source JSON"} disabled={readOnly} onChange={mapping => update("mapping", mapping)} />
      <FilterEditor filters={pipeline.filters} mapping={pipeline.mapping} sourceSchema={sourceSchema} lookupSchema={lookupSchema} disabled={readOnly} onChange={filters => update("filters", filters)} />

      <section className="etl-editor-section" aria-labelledby={`${titleId}-routes`}>
        <div className="etl-editor-section-heading"><div><h3 id={`${titleId}-routes`}>Output routes</h3><p>Choose separate host-approved destinations for accepted and rejected records. No endpoint or credential is entered here.</p></div></div>
        <div className="etl-editor-binding-grid">
          <DestinationEditor label="Accepted records" role="MainOutput" destination={pipeline.routes.main} bindings={bindings} bindingsLoading={bindingsLoading} bindingsUnavailable={bindingsUnavailable} disabled={readOnly} onChange={main => update("routes", { ...pipeline.routes, main })} />
          <DestinationEditor label="Rejected records" role="ErrorOutput" destination={pipeline.routes.errors} bindings={bindings} bindingsLoading={bindingsLoading} bindingsUnavailable={bindingsUnavailable} disabled={readOnly} onChange={errors => update("routes", { ...pipeline.routes, errors })} />
        </div>
      </section>

      <EtlConnectorBudgetsEditor limits={pipeline.limits} http={pipeline.http} disabled={readOnly} onLimitsChange={limits => update("limits", limits)} onHttpChange={http => update("http", http)} />
    </div>
  );
}

function DestinationEditor({ label, role, destination, bindings, bindingsLoading, bindingsUnavailable, disabled, onChange }: {
  label: string;
  role: "MainOutput" | "ErrorOutput";
  destination: EtlPipelineV2["routes"]["main"];
  bindings: readonly EtlConnectorBindingSummary[];
  bindingsLoading: boolean;
  bindingsUnavailable: boolean;
  disabled: boolean;
  onChange(destination: EtlPipelineV2["routes"]["main"]): void;
}) {
  const id = useId();
  return (
    <div className="etl-editor-route">
      <div className="etl-editor-field">
        <label htmlFor={`${id}-kind`}>{label} destination kind</label>
        <select id={`${id}-kind`} value={destination.kind} disabled={disabled} onChange={event => {
          const kind = event.currentTarget.value as EtlPipelineV2["routes"]["main"]["kind"];
          onChange({ kind, binding: destination.binding });
        }}>
          <option value="http">HTTP</option>
          <option value="jsonl">JSONL file</option>
          <option value="csv">CSV file</option>
        </select>
      </div>
      <PipelineBindingSelect
        label={`${label} binding`}
        role={role}
        // Hosts list output folder approvals under the jsonl kind; a CSV route publishes into the same folders.
        kind={destination.kind === "http" ? "http" : "jsonl"}
        subjectLabel={destination.kind === "csv" ? (role === "MainOutput" ? "Accepted file output" : "Rejected file output") : undefined}
        value={destination.binding}
        bindings={bindings}
        bindingsLoading={bindingsLoading}
        bindingsUnavailable={bindingsUnavailable}
        disabled={disabled}
        onChange={binding => onChange({ ...destination, binding })}
      />
    </div>
  );
}

/** Continuous runs need an HTTP cursor feed: token or next-link pagination. */
function supportsContinuous(pipeline: EtlPipelineV2) {
  return pipeline.source.kind === "httpJson" && pipeline.source.pagination.kind !== "singlePage";
}

function withoutUnsupportedContinuous(pipeline: EtlPipelineV2): EtlPipelineV2 {
  if (!pipeline.continuous || supportsContinuous(pipeline)) return pipeline;
  const { continuous: _, ...finite } = pipeline;
  return finite;
}

function RunModeEditor({ pipeline, disabled, onChange }: {
  pipeline: EtlPipelineV2;
  disabled: boolean;
  onChange(pipeline: EtlPipelineV2): void;
}) {
  const id = useId();
  if (!supportsContinuous(pipeline)) return null;
  const continuous = pipeline.continuous;
  return (
    <section className="etl-editor-section" aria-labelledby={`${id}-heading`}>
      <div className="etl-editor-section-heading"><div>
        <h3 id={`${id}-heading`}>Run mode</h3>
        <p>A continuous run keeps polling each shard's cursor until it is completed or cancelled. The feed must return a continuation on every page that has records.</p>
      </div></div>
      <label className="etl-editor-checkbox" htmlFor={`${id}-continuous`}>
        <input id={`${id}-continuous`} type="checkbox" checked={continuous !== undefined} disabled={disabled} onChange={event => {
          if (event.currentTarget.checked) onChange({ ...pipeline, continuous: { pollIntervalSeconds: 60 } });
          else {
            const { continuous: _, ...finite } = pipeline;
            onChange(finite);
          }
        }} />
        <span>Run continuously</span>
      </label>
      {continuous ? <div className="etl-editor-field">
        <label htmlFor={`${id}-interval`}>Poll interval (seconds)</label>
        <input id={`${id}-interval`} type="number" inputMode="numeric" min={1} max={86400} step={1} value={continuous.pollIntervalSeconds}
          disabled={disabled} aria-describedby={`${id}-interval-help`}
          onChange={event => onChange({ ...pipeline, continuous: { pollIntervalSeconds: Math.trunc(Number(event.currentTarget.value)) || 0 } })} />
        <small id={`${id}-interval-help`}>How long a caught-up shard waits before polling again, from 1 to 86,400 seconds.</small>
      </div> : null}
    </section>
  );
}

function columns(pipeline: EtlPipelineV2): EtlColumn[] {
  return pipeline.source.kind === "csv"
    ? pipeline.source.schema
    : pipeline.source.fields.map(({ name, type, required }) => ({ name, type, required }));
}
