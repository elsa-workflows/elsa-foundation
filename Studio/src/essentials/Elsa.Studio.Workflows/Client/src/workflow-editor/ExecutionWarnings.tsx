import { useId } from "react";
import { AlertTriangle, Info } from "lucide-react";
import { StatusChip } from "@elsa-workflows/studio-ui";
import type { ExecutionWarningView } from "../workflowTypes";
import "./variableLifecycle.css";

// Spec 1228 (S-8): the shared run-time warning channel. A warning changed and refused nothing; it says something the
// author should know, such as a number a script computed with that JavaScript cannot hold exactly. It never carries a
// value (spec 1229 FR-006), so it is shown to anyone who can see the execution.

/** Where a warning came from, in words. */
export function warningSourceLabel(warning: Pick<ExecutionWarningView, "source" | "inputKey">) {
  return warning.source === "input"
    ? warning.inputKey ? `Input "${warning.inputKey}"` : "An input"
    : "The activity";
}

/** The badge text: "1 warning" or "N warnings". */
export function warningBadgeLabel(count: number) {
  return count === 1 ? "1 warning" : `${count} warnings`;
}

/** The warning count as the shared warning status chip, beside the lost-update badge. */
export function ExecutionWarningBadge({ warnings }: { warnings: readonly ExecutionWarningView[] | null | undefined }) {
  const count = warnings?.length ?? 0;
  if (count === 0) return null;
  const informationCount = warnings!.filter(warning => warning.severity === "Information").length;
  const warningCount = count - informationCount;
  const informationOnly = warningCount === 0;
  const badge = [warningCount ? warningBadgeLabel(warningCount) : null,
    informationCount ? `${informationCount} information ${informationCount === 1 ? "record" : "records"}` : null].filter(Boolean).join(" · ");
  return (
    <span className="wf-lost-update-status" data-testid="execution-warning-badge">
      <StatusChip tone={informationOnly ? "neutral" : "warning"}>{informationOnly ? <Info size={12} aria-hidden="true" /> : <AlertTriangle size={12} aria-hidden="true" />} {badge}</StatusChip>
    </span>
  );
}

/** The warnings an execution raised, input expressions first, then the activity. Renders nothing when there are none. */
export function ExecutionWarningsSection({ warnings }: { warnings: readonly ExecutionWarningView[] | null | undefined }) {
  const headingId = useId();
  if (!warnings?.length) return null;
  return (
    <section className="wf-instance-section wf-variable-writes" aria-labelledby={headingId} data-testid="execution-warnings">
      <header className="wf-runtime-evidence-heading">
        <h4 id={headingId}><AlertTriangle size={14} aria-hidden="true" /> Warnings</h4>
        <ExecutionWarningBadge warnings={warnings} />
      </header>
      <div className="wf-variable-write-list" role="list">
        {warnings.map((warning, index) => (
          <article key={`${warning.code}:${warning.source}:${warning.inputKey ?? ""}:${index}`} className="wf-variable-write" role="listitem" data-outcome={warning.severity === "Information" ? "Information" : "Warning"}>
            <header>
              <strong>{warningSourceLabel(warning)}</strong>
              <span className="wf-variable-write-outcome">{warning.severity || "Warning"} · {warning.code}</span>
            </header>
            <p>{warning.message}</p>
            {Object.keys(warning.details ?? {}).length > 0 ? (
              <dl className="wf-variable-write-facts">
                {Object.entries(warning.details).map(([key, value]) => (
                  <div key={key}><dt>{detailLabel(key)}</dt><dd><code>{value}</code></dd></div>
                ))}
              </dl>
            ) : null}
          </article>
        ))}
      </div>
    </section>
  );
}

function detailLabel(key: string) {
  return key === "paths" ? "Paths" : key.charAt(0).toUpperCase() + key.slice(1);
}
