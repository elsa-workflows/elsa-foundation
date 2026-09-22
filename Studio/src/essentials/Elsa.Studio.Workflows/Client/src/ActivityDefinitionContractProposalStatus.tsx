import { AlertTriangle, RefreshCw, Sparkles } from "lucide-react";

export type ActivityContractProposalPhase =
  "waiting" | "debouncing" | "loading" | "ready" | "unavailable" | "stale" | "failed" | "applying";

export function ActivityDefinitionContractProposalStatus({
  phase,
  revision,
  failure
}: {
  phase: ActivityContractProposalPhase;
  revision: number;
  failure: string | null;
}) {
  if (phase === "ready" || phase === "applying") return null;
  if (phase === "waiting") return <div className="ad-inline-status" role="status" aria-live="polite"><Sparkles size={17} aria-hidden /> <span>Waiting for autosave. Proposals will recompute after the exact implementation and contract revision is saved.</span></div>;
  if (phase === "debouncing") return <div className="ad-inline-status" role="status" aria-live="polite"><Sparkles size={17} aria-hidden /> <span>Waiting briefly for editing to settle before reviewing revision {revision}.</span></div>;
  if (phase === "loading") return <div className="ad-inline-status" role="status" aria-live="polite" aria-busy="true"><RefreshCw size={17} aria-hidden /> <span>Generating provider proposals for exact saved revision {revision}… Editing and autosave remain available.</span></div>;
  return <div className="ad-inline-error" role="alert"><AlertTriangle size={17} aria-hidden /> <span>{failure}</span></div>;
}
