import { useEffect, useRef, useState } from "react";

export interface CopyableIdentifierProps {
  label: string;
  value: string;
  className?: string;
}

export function CopyableIdentifier({ label, value, className }: CopyableIdentifierProps) {
  const valueRef = useRef<HTMLSpanElement | null>(null);
  const [displayValue, setDisplayValue] = useState(value);
  const [copyState, setCopyState] = useState<"idle" | "copied" | "failed">("idle");

  useEffect(() => {
    const element = valueRef.current;
    if (!element) return;

    const update = () => {
      const width = element.clientWidth;
      if (width <= 0) {
        setDisplayValue(value);
        return;
      }
      setDisplayValue(middleTruncate(value, width, element));
    };
    update();
    const observer = typeof ResizeObserver === "undefined" ? null : new ResizeObserver(update);
    observer?.observe(element);
    return () => observer?.disconnect();
  }, [value]);

  useEffect(() => {
    if (copyState === "idle") return;
    const timeout = window.setTimeout(() => setCopyState("idle"), 1_600);
    return () => window.clearTimeout(timeout);
  }, [copyState]);

  const copy = async () => {
    try {
      await copyExactText(value);
      setCopyState("copied");
    } catch {
      setCopyState("failed");
    }
  };

  const announcement = copyState === "copied"
    ? `${label} copied`
    : copyState === "failed"
      ? `Could not copy ${label}`
      : "";

  return (
    <div className={["studio-copyable-identifier", className].filter(Boolean).join(" ")}>
      <span className="studio-copyable-identifier__label">{label}</span>
      <span
        ref={valueRef}
        className="studio-copyable-identifier__value"
        title={value}
        aria-label={`${label}: ${value}`}
      >
        {displayValue}
      </span>
      <button
        type="button"
        className="studio-copyable-identifier__button"
        aria-label={`Copy ${label}`}
        title={`Copy ${label}`}
        onClick={() => void copy()}
      >
        {copyState === "copied" ? <CheckIcon /> : <CopyIcon />}
      </button>
      <span className="studio-copyable-identifier__announcement" role="status" aria-live="polite">
        {announcement}
      </span>
    </div>
  );
}

function middleTruncate(value: string, availableWidth: number, element: HTMLElement) {
  const context = document.createElement("canvas").getContext("2d");
  if (!context) return value;
  const style = getComputedStyle(element);
  context.font = style.font || `${style.fontSize} ${style.fontFamily}`;
  if (context.measureText(value).width <= availableWidth) return value;

  const ellipsis = "…";
  let low = 2;
  let high = Math.max(2, value.length - 1);
  let best = ellipsis;
  while (low <= high) {
    const visible = Math.floor((low + high) / 2);
    const leading = Math.ceil(visible / 2);
    const candidate = `${value.slice(0, leading)}${ellipsis}${value.slice(-(visible - leading))}`;
    if (context.measureText(candidate).width <= availableWidth) {
      best = candidate;
      low = visible + 1;
    } else {
      high = visible - 1;
    }
  }
  return best;
}

async function copyExactText(value: string) {
  if (navigator.clipboard?.writeText) {
    try {
      await navigator.clipboard.writeText(value);
      return;
    } catch {
      // Continue with the selection fallback for restricted clipboard contexts.
    }
  }

  const textArea = document.createElement("textarea");
  textArea.value = value;
  textArea.setAttribute("readonly", "");
  textArea.style.position = "fixed";
  textArea.style.opacity = "0";
  document.body.appendChild(textArea);
  textArea.select();
  const copied = document.execCommand("copy");
  textArea.remove();
  if (!copied) throw new Error("Clipboard copy failed.");
}

function CopyIcon() {
  return (
    <svg aria-hidden="true" viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" strokeWidth="2">
      <rect x="9" y="9" width="11" height="11" rx="2" />
      <path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1" />
    </svg>
  );
}

function CheckIcon() {
  return (
    <svg aria-hidden="true" viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" strokeWidth="2">
      <path d="m5 12 4 4L19 6" />
    </svg>
  );
}
