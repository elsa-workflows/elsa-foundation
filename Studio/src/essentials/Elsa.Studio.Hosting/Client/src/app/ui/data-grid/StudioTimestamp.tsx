import React from "react";

const timestampDateFormatter = new Intl.DateTimeFormat(undefined, { dateStyle: "medium" });
const timestampTimeFormatter = new Intl.DateTimeFormat(undefined, { timeStyle: "short" });
const preciseTimeFormatter = new Intl.DateTimeFormat(undefined, { timeStyle: "medium" });
const preciseDateTimeFormatter = new Intl.DateTimeFormat(undefined, { dateStyle: "medium", timeStyle: "medium" });
const timestampTitleFormatter = new Intl.DateTimeFormat(undefined, { dateStyle: "full", timeStyle: "long" });

/** Locale-aware inline timestamp for plain-text content such as confirmation messages. */
export function formatStudioDateTime(value: string | null, fallback: string): string {
  const date = value ? new Date(value) : null;
  return date && Number.isFinite(date.getTime()) ? preciseDateTimeFormatter.format(date) : fallback;
}

export function StudioTimestamp({ value, fallback = null, invalidFallback, format = "stacked" }: {
  value?: string | null;
  /** Inline diagnostic readings retain seconds. The default stacks a date above short time. */
  format?: "stacked" | "time" | "date-time";
  fallback?: React.ReactNode;
  invalidFallback?: React.ReactNode;
}) {
  if (!value) return <>{fallback}</>;

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return <>{invalidFallback ?? fallback ?? value}</>;

  if (format !== "stacked") {
    const formatter = format === "time" ? preciseTimeFormatter : preciseDateTimeFormatter;
    return <time dateTime={value} title={timestampTitleFormatter.format(date)}>{formatter.format(date)}</time>;
  }

  return (
    <time className="studio-timestamp" dateTime={value} title={timestampTitleFormatter.format(date)}>
      <span className="studio-timestamp-date">{timestampDateFormatter.format(date)}</span>
      <span className="studio-timestamp-time">{timestampTimeFormatter.format(date)}</span>
    </time>
  );
}
