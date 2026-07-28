import { AlertCircle, AlertTriangle, CheckCircle2, Info, LocateFixed, Undo2 } from "lucide-react";
import type {
  StudioActivityDiagnostic,
  StudioActivityDiagnosticFocusResult
} from "@elsa-workflows/studio-sdk";
import type { ActivityDraftValidationView } from "./activityDefinitionTypes";

type DiagnosticTone = "error" | "warning" | "info";

export type ActivityDraftValidationFailure =
  | "rejected"
  | "transport"
  | "unavailable"
  | "forbidden"
  | "not-found";

export interface ActivityDefinitionLocalDiagnostic {
  area: "JSON";
  severity: DiagnosticTone;
  message: string;
}

export function ActivityDefinitionDiagnosticsPanel({
  validation,
  failure,
  localDiagnostics = [],
  focusAnnouncement,
  canReturn,
  onFocus,
  onReturn
}: {
  validation?: ActivityDraftValidationView | null;
  failure?: ActivityDraftValidationFailure | null;
  localDiagnostics?: ActivityDefinitionLocalDiagnostic[];
  focusAnnouncement?: string | null;
  canReturn: boolean;
  onFocus(diagnostic: StudioActivityDiagnostic, trigger: HTMLButtonElement): Promise<StudioActivityDiagnosticFocusResult>;
  onReturn(): void;
}) {
  if (!validation && !failure && localDiagnostics.length === 0) return null;

  const diagnostics = validation?.diagnostics ?? [];
  const counts = countSeverities(diagnostics, localDiagnostics);
  const groups = groupDiagnostics(diagnostics);
  const defaultOpen = Boolean(failure || !validation?.isValid || localDiagnostics.some(item => item.severity === "error"));

  return <section className="ad-diagnostics-panel" aria-labelledby="activity-diagnostics-title">
    <details open={defaultOpen}>
      <summary>
        <span>Draft diagnostics</span>
        <span>{counts.error} errors · {counts.warning} warnings · {counts.info} info</span>
      </summary>
    <div className="ad-diagnostics-content">
      <header className="ad-diagnostics-header">
      <div>
        <span className="ad-kicker">{validation || failure ? "Exact saved revision" : "Local authoring buffer"}</span>
        <h2 id="activity-diagnostics-title">Draft diagnostics</h2>
        <p>{validation
          ? validation.isValid
            ? `Revision ${validation.revision} passed validation. The result is server-authoritative for this exact saved revision.`
            : `Revision ${validation.revision} was rejected by draft validation. Correct the errors before publication or Test Run.`
          : failure
            ? validationFailureMessage(failure)
            : "Local authoring diagnostics must be resolved or reset before revision-sensitive actions can continue."}</p>
      </div>
      {validation ? <span className={`ad-validation-result is-${validation.isValid ? "valid" : "invalid"}`}>
        {validation.isValid ? <CheckCircle2 size={16} aria-hidden /> : <AlertCircle size={16} aria-hidden />}
        {validation.isValid ? "Valid draft" : "Draft rejected"}
      </span> : null}
      </header>
    <div className="ad-diagnostic-counts" aria-label="Diagnostic severity counts">
      <SeverityCount severity="error" count={counts.error} />
      <SeverityCount severity="warning" count={counts.warning} />
      <SeverityCount severity="info" count={counts.info} />
    </div>
    {groups.map(group => <section key={group.area} className="ad-diagnostic-group" aria-labelledby={`activity-diagnostics-${group.id}`}>
      <h3 id={`activity-diagnostics-${group.id}`}>{group.area}</h3>
      <ActivityDiagnosticList diagnostics={group.diagnostics} onFocus={onFocus} label={`${group.area} diagnostics`} />
    </section>)}
    {localDiagnostics.length ? <section className="ad-diagnostic-group" aria-labelledby="activity-diagnostics-json">
      <h3 id="activity-diagnostics-json">JSON</h3>
      <ul className="ad-diagnostic-list" aria-label="JSON diagnostics">
        {localDiagnostics.map((diagnostic, index) => <li key={`${diagnostic.severity}:${index}`} className={`ad-diagnostic-item is-${diagnostic.severity}`}>
          <div className="ad-diagnostic-item-heading"><span className="ad-diagnostic-severity">{severityIcon(diagnostic.severity)} {severityLabel(diagnostic.severity)}</span><code className="ad-diagnostic-code">activity.authoring.json</code></div>
          <p>{diagnostic.message}</p>
        </li>)}
      </ul>
    </section> : null}
    {!diagnostics.length && !localDiagnostics.length && validation ? <p className="ad-diagnostics-empty">No diagnostics were returned for this saved revision.</p> : null}
    <div className="ad-diagnostic-context" role="status" aria-live="polite">
      <span>{focusAnnouncement ?? "Select a diagnostic to move to a supported contract control or provider-owned implementation location."}</span>
      {canReturn ? <button type="button" onClick={onReturn}><Undo2 size={15} aria-hidden /> Return to diagnostic</button> : null}
    </div>
    <p className="ad-runtime-distinction"><strong>Runtime is separate.</strong> A later Test Run can be rejected during dispatch even when this draft is valid; Runtime rejection is reported by the Test Run experience, not as a draft-validation transport failure.</p>
    </div>
    </details>
  </section>;
}

export function ActivityDiagnosticList({
  diagnostics,
  onFocus,
  label = "Structured diagnostics"
}: {
  diagnostics: StudioActivityDiagnostic[];
  onFocus(diagnostic: StudioActivityDiagnostic, trigger: HTMLButtonElement): Promise<StudioActivityDiagnosticFocusResult>;
  label?: string;
}) {
  return <ol className="ad-diagnostic-list" aria-label={label}>
    {diagnostics.map((diagnostic, index) => <DiagnosticItem
      key={`${safeDiagnosticCode(diagnostic.code)}:${index}`}
      diagnostic={diagnostic}
      onFocus={onFocus}
    />)}
  </ol>;
}

function DiagnosticItem({
  diagnostic,
  onFocus
}: {
  diagnostic: StudioActivityDiagnostic;
  onFocus(diagnostic: StudioActivityDiagnostic, trigger: HTMLButtonElement): Promise<StudioActivityDiagnosticFocusResult>;
}) {
  const code = safeDiagnosticCode(diagnostic.code);
  const severity = safeSeverity(diagnostic.severity);
  const context = diagnosticContext(diagnostic);

  return <li className={`ad-diagnostic-item is-${severity}`}>
    <div className="ad-diagnostic-item-heading">
      <span className="ad-diagnostic-severity">{severityIcon(severity)} {severityLabel(severity)}</span>
      <code className="ad-diagnostic-code">{code}</code>
    </div>
    <p>{diagnostic.message}</p>
    {diagnostic.remediation ? <p className="ad-diagnostic-guidance"><strong>Guidance:</strong> {diagnostic.remediation}</p> : null}
    <span className="ad-diagnostic-subject">{context}</span>
    <button
      type="button"
      aria-label={`Focus ${code}`}
      onClick={event => void onFocus(diagnostic, event.currentTarget)}
    >
      <LocateFixed size={15} aria-hidden /> Focus location
    </button>
  </li>;
}

function SeverityCount({ severity, count }: { severity: DiagnosticTone; count: number }) {
  return <span className={`is-${severity}`}>{severityIcon(severity)} <strong>{count}</strong> {severity}{count === 1 ? "" : "s"}</span>;
}

function countSeverities(
  diagnostics: StudioActivityDiagnostic[],
  localDiagnostics: ActivityDefinitionLocalDiagnostic[] = []
) {
  const counts = diagnostics.reduce((result, diagnostic) => {
    result[safeSeverity(diagnostic.severity)] += 1;
    return result;
  }, { error: 0, warning: 0, info: 0 });
  return localDiagnostics.reduce((result, diagnostic) => {
    result[diagnostic.severity] += 1;
    return result;
  }, counts);
}

function groupDiagnostics(diagnostics: StudioActivityDiagnostic[]) {
  const groups = new Map<string, StudioActivityDiagnostic[]>();
  for (const diagnostic of diagnostics) {
    const area = diagnosticArea(diagnostic);
    groups.set(area, [...(groups.get(area) ?? []), diagnostic]);
  }
  return [...groups].map(([area, items]) => ({
    area,
    id: area.toLocaleLowerCase().replaceAll(/[^a-z0-9]+/g, "-"),
    diagnostics: items
  }));
}

function diagnosticArea(diagnostic: StudioActivityDiagnostic) {
  const pointer = diagnostic.location?.jsonPointer ?? "";
  if (pointer.startsWith("/contract")) return "Public contract";
  if (pointer.startsWith("/outputMappings") || pointer.startsWith("/outcomeMappings")) return "Boundary mappings";
  if (pointer.startsWith("/rootActivity") || pointer.startsWith("/variables")) return "Graph";
  if (diagnostic.location?.providerKey) return "Provider";
  return "General";
}

function safeSeverity(severity: string): DiagnosticTone {
  if (severity === "Error") return "error";
  if (severity === "Warning") return "warning";
  return "info";
}

function severityIcon(severity: DiagnosticTone) {
  if (severity === "error") return <AlertCircle size={15} aria-hidden />;
  if (severity === "warning") return <AlertTriangle size={15} aria-hidden />;
  return <Info size={15} aria-hidden />;
}

function severityLabel(severity: DiagnosticTone) {
  return severity.charAt(0).toUpperCase() + severity.slice(1);
}

export function safeDiagnosticCode(code: string) {
  return /^[a-z0-9][a-z0-9._-]{0,127}$/.test(code) ? code : "activity.validation.issue";
}

function diagnosticContext(diagnostic: StudioActivityDiagnostic) {
  const revision = typeof diagnostic.subject.revision === "number" ? ` · revision ${diagnostic.subject.revision}` : "";
  const dependencyCount = diagnostic.location?.dependencyPath?.length ?? 0;
  const originCount = diagnostic.location?.nodeOrigin?.length ?? 0;
  const dependency = dependencyCount ? ` · dependency path with ${dependencyCount} authorized step${dependencyCount === 1 ? "" : "s"}` : "";
  const origin = originCount ? ` · ${originCount} origin segment${originCount === 1 ? "" : "s"}` : "";
  return `${diagnostic.subject.kind}${revision}${dependency}${origin}`;
}

function validationFailureMessage(failure: ActivityDraftValidationFailure) {
  if (failure === "rejected") return "The backend rejected validation for the exact saved revision. Reload the draft before retrying.";
  if (failure === "forbidden") return "Draft validation is not authorized in this context. No protected diagnostic details were disclosed.";
  if (failure === "not-found") return "The exact authorized draft could not be confirmed for validation. No resource identity was disclosed.";
  if (failure === "unavailable") return "Draft validation is unavailable because the advertised capability could not be confirmed.";
  return "Draft validation could not reach the server. The saved draft was not classified as invalid.";
}
