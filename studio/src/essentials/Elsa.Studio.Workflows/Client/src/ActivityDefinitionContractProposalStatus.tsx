import { AlertTriangle, RefreshCw, Sparkles } from "lucide-react";

export type ActivityContractProposalPhase =
  "waiting" | "debouncing" | "loading" | "ready" | "unavailable" | "stale" | "failed" | "applying";

export function ActivityDefinitionContractProposalStatus({
  phase,
  failure
}: {
  phase: ActivityContractProposalPhase;
  failure: string | null;
}) {
  if (phase === "ready" || phase === "applying") return null;
  if (phase === "waiting" || phase === "debouncing") return <div className="ad-inline-status" role="status" aria-live="polite"><Sparkles size={17} aria-hidden /> <span>Suggestions update after your changes are saved.</span></div>;
  if (phase === "loading") return <div className="ad-inline-status" role="status" aria-live="polite" aria-busy="true"><RefreshCw size={17} aria-hidden /> <span>Checking for suggestions… You can keep editing.</span></div>;
  return <div className="ad-inline-error" role="alert"><AlertTriangle size={17} aria-hidden /> <span>{failure}</span></div>;
}
