import { readConversionPlan, shortenFingerprint } from "./conversionSettings";

/**
 * Read-only summary of a pinned `ValueConversionPlan` from executable inspection: resolved
 * mode/profile with its pinned version, the source representation → target contract, and the plan
 * fingerprint. Renders nothing when the binding carries no plan (sensitive bindings and legacy
 * executables published before conversion plans existed).
 */
export function ConversionPlanCaption({ plan }: { plan: unknown }) {
  const summary = readConversionPlan(plan);
  if (!summary) return null;

  const profileLabel = summary.profile ? `${summary.profile.id}@${summary.profile.version}` : null;
  const flow = [summary.sourceRepresentation, summary.targetContract].filter(Boolean).join(" → ");

  return (
    <span className="wf-conversion-plan">
      <span className="wf-chip wf-conversion-plan-mode">{summary.mode}</span>
      {profileLabel ? <span className="wf-conversion-plan-profile">{profileLabel}</span> : null}
      {flow ? <span className="wf-conversion-plan-flow">{flow}</span> : null}
      {summary.fingerprint ? (
        <code className="wf-conversion-plan-fingerprint" title={summary.fingerprint}>
          {shortenFingerprint(summary.fingerprint)}
        </code>
      ) : null}
    </span>
  );
}
