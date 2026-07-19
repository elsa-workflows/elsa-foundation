import { AlertTriangle, CheckCircle2, GitBranch, Layers3 } from "lucide-react";
import type {
  ActivityUpgradeDiagnostic,
  ActivityUpgradePlan,
  ActivityUpgradeStage,
  ActivityUpgradeStep
} from "./activityUpgradeTypes";
import {
  nextReadyActivityUpgradeStage,
  normalizeDiagnosticSeverity,
  normalizePlanStatus,
  normalizeStageStatus,
  normalizeUpgradeAction
} from "./activityUpgradeModel";

export function ActivityUpgradePlanReview({
  plan,
  stale,
  applying,
  onApply
}: {
  plan: ActivityUpgradePlan;
  stale: boolean;
  applying: boolean;
  onApply(stage: ActivityUpgradeStage): void;
}) {
  const nextStage = nextReadyActivityUpgradeStage(plan);
  const planStatus = normalizePlanStatus(plan.status);
  const stages = [...plan.stages].sort((left, right) => left.order - right.order);
  const steps = new Map(plan.steps.map(step => [step.stepId, step]));

  return <section className="au-review" aria-labelledby="activity-upgrade-review-title">
    <header className="au-review-header">
      <div>
        <span className="ad-kicker">Backend-authoritative evidence</span>
        <h2 id="activity-upgrade-review-title">Reviewed upgrade plan</h2>
        <p>Apply accepts only the exact returned Plan ID and one returned atomic stage. Studio does not recalculate dependency order.</p>
      </div>
      <span className={`au-status is-${planStatus.toLowerCase()}`}>{planStatus}</span>
    </header>

    {stale ? <div className="ad-stale-warning" role="alert">
      <AlertTriangle size={18} aria-hidden />
      <div><strong>Reviewed evidence is stale</strong><span>Apply is paused until the authoritative plan can be confirmed again. The last reviewed evidence remains visible.</span></div>
    </div> : null}

    <dl className="au-plan-facts">
      <div><dt>Plan ID</dt><dd><code>{plan.planId}</code></dd></div>
      <div><dt>Created</dt><dd>{formatDateTime(plan.createdAt)}</dd></div>
      <div><dt>Expires</dt><dd>{formatDateTime(plan.expiresAt)}</dd></div>
      <div><dt>Atomicity</dt><dd>One stage at a time</dd></div>
    </dl>

    <section className="au-evidence-section" aria-labelledby="activity-upgrade-replacements">
      <header><Layers3 size={18} aria-hidden /><div><h3 id="activity-upgrade-replacements">Exact replacements</h3><p>No mutable latest reference is accepted.</p></div></header>
      <ul className="au-identity-list">
        {plan.replacements.map(replacement => <li key={`${replacement.from.versionId}:${replacement.to.versionId}`}>
          <span><strong>{replacement.from.version}</strong><code>{replacement.from.versionId}</code></span>
          <span aria-hidden>→</span>
          <span><strong>{replacement.to.version}</strong><code>{replacement.to.versionId}</code></span>
        </li>)}
      </ul>
    </section>

    <section className="au-evidence-section" aria-labelledby="activity-upgrade-closure">
      <header><GitBranch size={18} aria-hidden /><div><h3 id="activity-upgrade-closure">Selected dependency closure</h3><p>Only the backend-authorized closure for the explicitly selected roots is shown.</p></div></header>
      {plan.binding?.selectedClosure.length ? <ul className="au-closure-list">
        {plan.binding.selectedClosure.map((reference, index) => <li key={`${reference.kind}:${reference.draftId ?? reference.versionId ?? reference.definitionId}:${index}`}>
          <strong>{reference.kind}</strong>
          <code>{reference.draftId ?? reference.versionId ?? reference.definitionId}</code>
          <span>{reference.version ? `Version ${reference.version}` : reference.revision != null ? `Revision ${reference.revision}` : "Exact identity"}</span>
        </li>)}
      </ul> : <p className="ad-inline-status">The authoritative plan returned no disclosed closure rows.</p>}
    </section>

    <section className="au-evidence-section" aria-labelledby="activity-upgrade-preconditions">
      <header><AlertTriangle size={18} aria-hidden /><div><h3 id="activity-upgrade-preconditions">Stale-write preconditions</h3><p>Apply rereads these snapshots under the backend mutation boundary.</p></div></header>
      <ul className="au-snapshot-list">
        {plan.expectedSnapshots.map(snapshot => <li key={`${snapshot.kind}:${snapshot.id}`}>
          <strong>{snapshot.kind}</strong>
          <code>{snapshot.id}</code>
          <span>{snapshot.revision != null ? `Expected revision ${snapshot.revision}` : "Immutable snapshot"}{snapshot.headVersionId ? ` · head ${snapshot.headVersionId}` : ""}</span>
        </li>)}
      </ul>
    </section>

    <section className="au-stages" aria-labelledby="activity-upgrade-stages">
      <header><h3 id="activity-upgrade-stages">Persistent bottom-up stages</h3><p>Each stage is independently atomic and idempotent. Publication remains a separate reviewed action.</p></header>
      <ol>
        {stages.map((stage, index) => <StageCard
          key={stage.stageId}
          stage={stage}
          number={index + 1}
          steps={stage.stepIds.map(stepId => steps.get(stepId)).filter((step): step is ActivityUpgradeStep => Boolean(step))}
        />)}
      </ol>
    </section>

    <DiagnosticList diagnostics={plan.diagnostics} />

    <footer className="au-sticky-actions">
      <div>
        <strong>{nextStage ? `Next: bottom-up stage ${stages.indexOf(nextStage) + 1}` : planStatus === "Applied" ? "All reviewed stages applied" : "No stage is currently ready"}</strong>
        <span>{nextStage ? "Only this returned stage will be submitted." : "Resolve publication or blocking diagnostics before another Apply."}</span>
      </div>
      {nextStage ? <button type="button" className="ad-primary-action" onClick={() => onApply(nextStage)} disabled={stale || applying}>
        {applying ? "Applying atomically…" : `Apply stage ${stages.indexOf(nextStage) + 1} atomically`}
      </button> : null}
    </footer>
  </section>;
}

function StageCard({
  stage,
  number,
  steps
}: {
  stage: ActivityUpgradeStage;
  number: number;
  steps: ActivityUpgradeStep[];
}) {
  const status = normalizeStageStatus(stage.status);
  return <li className={`au-stage is-${status.toLowerCase()}`}>
    <header>
      <div><span>Bottom-up stage {number}</span><code>{stage.stageId}</code></div>
      <strong>{status === "Applied" ? <CheckCircle2 size={16} aria-hidden /> : null}{status}</strong>
    </header>
    {stage.dependsOnStageIds.length ? <p>Prerequisite stages: {stage.dependsOnStageIds.join(", ")}</p> : <p>No stage prerequisites.</p>}
    <div className="au-step-list">
      {steps.map(step => <article key={step.stepId}>
        <header><strong>{actionLabel(step.action)}</strong><code>{step.target.draftId ?? step.target.sourceVersionId ?? step.target.definitionId}</code></header>
        <dl>
          <div><dt>Target</dt><dd>{step.target.kind} · {step.target.definitionId}</dd></div>
          <div><dt>Occurrences</dt><dd>{step.replacements.length}</dd></div>
          <div><dt>Expected draft</dt><dd>{step.expectedRevision != null ? `Revision ${step.expectedRevision}` : "New clone"}</dd></div>
          <div><dt>Expected head</dt><dd>{step.expectedDefinitionHeadVersionId ?? "None"}</dd></div>
        </dl>
        {step.resultingDiff ? <div className="au-compatibility">
          <strong>{valueLabel(step.resultingDiff.compatibility, ["Compatible", "Breaking", "Unknown"])} · {valueLabel(step.resultingDiff.requiredBump, ["None", "Patch", "Minor", "Major"])}</strong>
          <span>{step.resultingDiff.behaviorChanged ? "Behavior changes" : "No behavioral change"}{step.resultingDiff.summary ? ` · ${step.resultingDiff.summary.breaking} breaking · ${step.resultingDiff.summary.additive} additive · ${step.resultingDiff.summary.warnings} warnings` : ""}</span>
        </div> : <div className="au-compatibility"><strong>Compatibility evidence unavailable for this target kind</strong><span>The backend still owns stage readiness and stale validation.</span></div>}
        <DiagnosticList diagnostics={step.diagnostics} compact />
      </article>)}
    </div>
  </li>;
}

function DiagnosticList({ diagnostics, compact = false }: { diagnostics: ActivityUpgradeDiagnostic[]; compact?: boolean }) {
  if (!diagnostics.length) return compact ? null : <div className="au-diagnostics is-empty" role="status">No plan-level diagnostics.</div>;
  return <section className={`au-diagnostics${compact ? " is-compact" : ""}`} aria-label={compact ? "Stage diagnostics" : "Plan diagnostics"}>
    <h3>{compact ? "Stage diagnostics" : "Plan diagnostics"}</h3>
    <ul>{diagnostics.map((diagnostic, index) => {
      const severity = normalizeDiagnosticSeverity(diagnostic.severity);
      return <li key={`${diagnostic.code}:${diagnostic.subject.kind}:${diagnostic.subject.id}:${index}`} className={`is-${severity.toLowerCase()}`}>
        <header><strong>{severity}</strong><code>{diagnostic.code}</code></header>
        <p>{diagnostic.message}</p>
        <span>{diagnostic.subject.kind} · {diagnostic.subject.id}</span>
        {diagnostic.remediation ? <span>{diagnostic.remediation}</span> : null}
      </li>;
    })}</ul>
  </section>;
}

function actionLabel(action: string | number) {
  return {
    UpdateDraft: "Update existing draft",
    CloneActivityVersion: "Clone Activity Definition Version",
    CloneWorkflowVersion: "Clone Workflow Version"
  }[normalizeUpgradeAction(action)] ?? normalizeUpgradeAction(action);
}

function valueLabel(value: string | number, numeric: string[]) {
  return typeof value === "string" ? value : numeric[value] ?? `Unknown (${value})`;
}

function formatDateTime(value: string) {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString();
}
