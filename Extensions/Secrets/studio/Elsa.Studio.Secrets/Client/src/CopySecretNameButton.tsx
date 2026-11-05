import React, { useEffect, useState } from "react";
import type { StudioNotificationApi } from "@elsa-workflows/studio-sdk";
import { Check, Copy } from "lucide-react";

export function CopySecretNameButton({ name, notifications }: { name: string; notifications?: StudioNotificationApi }) {
  const [result, setResult] = useState<"copied" | "failed" | null>(null);

  useEffect(() => {
    if (!result) return;
    const timeout = window.setTimeout(() => setResult(null), 2000);
    return () => window.clearTimeout(timeout);
  }, [result]);

  async function copy() {
    try {
      if (!navigator.clipboard?.writeText) throw new Error("Clipboard unavailable");
      await navigator.clipboard.writeText(name);
      setResult("copied");
      notifications?.notify({ level: "success", title: "Secret name copied." });
    } catch {
      setResult("failed");
    }
  }

  return <span className="secrets-copy-control">
    <button type="button" className="secrets-copy-button" aria-label={`Copy technical name ${name}`} title="Copy technical name" onClick={() => void copy()}>
      {result === "copied" ? <Check size={13} aria-hidden /> : <Copy size={13} aria-hidden />}
    </button>
    <span className={result === "failed" ? "secrets-copy-feedback is-error" : "secrets-copy-feedback"} role="status">{result === "failed" ? "Copy failed" : ""}</span>
  </span>;
}
