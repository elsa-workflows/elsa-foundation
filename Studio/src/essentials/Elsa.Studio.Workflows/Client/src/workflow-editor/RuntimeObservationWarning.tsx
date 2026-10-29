import { AlertTriangle } from "lucide-react";

export function RuntimeObservationWarning({ messages, className = "" }: { messages?: string[]; className?: string }) {
  if (!messages?.length) return null;
  const label = `${messages.length} ${messages.length === 1 ? "warning" : "warnings"}`;
  return <span className={`wf-observation-warning ${className}`} title={messages.join("\n")} role="img" aria-label={`${label}: ${messages.join(" ")}`}>
    <AlertTriangle size={12} aria-hidden="true" />{label}
  </span>;
}
