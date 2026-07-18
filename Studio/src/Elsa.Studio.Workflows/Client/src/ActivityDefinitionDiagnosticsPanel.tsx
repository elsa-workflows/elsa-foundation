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

export function ActivityDefinitionDiagnosticsPanel({
  validation,
  failure,
  focusAnnouncement,
  canReturn,
  onFocus,
  onReturn
}: {
  validation?: ActivityDraftValidationView | null;
  failure?: ActivityDraftValidationFailure | null;
  focusAnnouncement?: string | null;
  canReturn: boolean;
  onFocus(diagnostic: StudioActivityDiagnostic, trigger: HTMLButtonElement): Promise<StudioActivityDiagnosticFocusResult>;
  onReturn(): void;
}) {
  if (!validation && !failure) return null;

  const diagnostics = validation?.diagnostics ?? [];
  const counts = countSeverities(diagnostics);

  return <section className="ad-diagnostics-panel" aria-labelledby="activity-diagnostics-title">
    <header className="ad-diagnostics-header">
      <div>
        <span className="ad-kicker">Exact saved revision</span>
        <h2 id="activity-diagnostics-title">Draft diagnostics</h2>
        <p>{validation
          ? validation.isValid
            ? `Revision ${validation.revision} passed validation. The result is server-authoritative for this exact saved revision.`
            : `Revision ${validation.revision} was rejected by draft validation. Correct the errors before publication or Test Run.`
          : validationFailureMessage(failure!)}</p>
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
    {diagnostics.length ? <ActivityDiagnosticList diagnostics={diagnostics} onFocus={onFocus} /> : validation ? <p className="ad-diagnostics-empty">No diagnostics were returned for this saved revision.</p> : null}
    <div className="ad-diagnostic-context" role="status" aria-live="polite">
      <span>{focusAnnouncement ?? "Select a diagnostic to move to a supported contract control or provider-owned implementation location."}</span>
      {canReturn ? <button type="button" onClick={onReturn}><Undo2 size={15} aria-hidden /> Return to diagnostic</button> : null}
    </div>
    <p className="ad-runtime-distinction"><strong>Runtime is separate.</strong> A later Test Run can be rejected during dispatch even when this draft is valid; Runtime rejection is reported by the Test Run experience, not as a draft-validation transport failure.</p>
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

function countSeverities(diagnostics: StudioActivityDiagnostic[]) {
  return diagnostics.reduce((counts, diagnostic) => {
    counts[safeSeverity(diagnostic.severity)] += 1;
    return counts;
  }, { error: 0, warning: 0, info: 0 });
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
