import React, { useEffect, useLayoutEffect, useMemo, useRef, useState } from "react";
import type { StudioNotificationApi } from "@elsa-workflows/studio-sdk";
import { Check, Copy } from "lucide-react";

export function CopySecretNameButton({ name, notifications, scope }: { name: string; notifications?: StudioNotificationApi; scope?: object }) {
  const [result, setResult] = useState<"copied" | "failed" | null>(null);
  const copyScope = useMemo(() => ({ name, scope, notifications, active: false, generation: 0 }), [name, scope, notifications]);
  const currentScope = useRef(copyScope);
  currentScope.current = copyScope;
  useLayoutEffect(() => {
    copyScope.active = true;
    setResult(null);
    return () => { copyScope.active = false; copyScope.generation++; };
  }, [copyScope]);

  useEffect(() => {
    if (!result) return;
    const timeout = window.setTimeout(() => setResult(null), 2000);
    return () => window.clearTimeout(timeout);
  }, [result]);

  async function copy() {
    const generation = copyScope.generation;
    const isCurrent = () => currentScope.current === copyScope && copyScope.active && copyScope.generation === generation;
    try {
      if (!navigator.clipboard?.writeText) throw new Error("Clipboard unavailable");
      await navigator.clipboard.writeText(name);
      if (!isCurrent()) return;
      setResult("copied");
      notifications?.notify({ level: "success", title: "Secret name copied." });
    } catch {
      if (isCurrent()) setResult("failed");
    }
  }

  return <span className="secrets-copy-control">
    <button type="button" className="secrets-copy-button" aria-label={`Copy technical name ${name}`} title="Copy technical name" onClick={() => void copy()}>
      {result === "copied" ? <Check size={13} aria-hidden /> : <Copy size={13} aria-hidden />}
    </button>
    <span className={result === "failed" ? "secrets-copy-feedback is-error" : "secrets-copy-feedback"} role="status">{result === "failed" ? "Copy failed" : ""}</span>
  </span>;
}
