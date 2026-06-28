import React from "react";
import { Check, CircleDot, Loader2, Wrench, X } from "lucide-react";
import type { AgentPlanStep } from "../agent/agentTypes";
import type { WeaverTimelineEntry } from "./useWeaverSession";

export function WeaverTimeline({ entries, plan }: { entries: WeaverTimelineEntry[]; plan: AgentPlanStep[] }) {
  if (entries.length === 0 && plan.length === 0) {
    return null;
  }

  return (
    <section className="weaver-timeline" aria-label="Agent activity">
      {plan.length > 0 ? (
        <ol className="weaver-plan">
          {plan.map(step => (
            <li key={step.id} data-status={step.status}>
              <CircleDot size={12} /> {step.title}
            </li>
          ))}
        </ol>
      ) : null}
      {entries.map(entry => (
        <div key={entry.id} className="weaver-timeline-entry" data-kind={entry.kind} data-status={entry.kind === "tool" || entry.kind === "step" ? entry.status : undefined}>
          {renderEntry(entry)}
        </div>
      ))}
    </section>
  );
}

function renderEntry(entry: WeaverTimelineEntry) {
  if (entry.kind === "progress") {
    return <span className="weaver-timeline-progress"><Loader2 size={12} className="weaver-spin" /> {entry.label}</span>;
  }

  if (entry.kind === "step") {
    return (
      <span className="weaver-timeline-step">
        {entry.status === "completed" ? <Check size={12} /> : <Loader2 size={12} className="weaver-spin" />}
        Step {entry.stepIndex + 1}
      </span>
    );
  }

  return (
    <span className="weaver-timeline-tool">
      {toolIcon(entry.status, entry.succeeded)}
      <code>{entry.toolName}</code>
      {entry.summary ? <span className="weaver-timeline-tool-summary">{entry.summary}</span> : null}
    </span>
  );
}

function toolIcon(status: "requested" | "running" | "completed", succeeded?: boolean) {
  if (status === "completed") return succeeded === false ? <X size={12} /> : <Check size={12} />;
  if (status === "running") return <Loader2 size={12} className="weaver-spin" />;
  return <Wrench size={12} />;
}
