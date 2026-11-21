import React from "react";

const timestampDateFormatter = new Intl.DateTimeFormat(undefined, { dateStyle: "medium" });
const timestampTimeFormatter = new Intl.DateTimeFormat(undefined, { timeStyle: "short" });
const timestampTitleFormatter = new Intl.DateTimeFormat(undefined, { dateStyle: "full", timeStyle: "long" });

export function StudioTimestamp({ value, fallback = null, invalidFallback }: {
  value?: string | null;
  fallback?: React.ReactNode;
  invalidFallback?: React.ReactNode;
}) {
  if (!value) return <>{fallback}</>;

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return <>{invalidFallback ?? fallback ?? value}</>;

  return (
    <time className="studio-timestamp" dateTime={value} title={timestampTitleFormatter.format(date)}>
      <span className="studio-timestamp-date">{timestampDateFormatter.format(date)}</span>
      <span className="studio-timestamp-time">{timestampTimeFormatter.format(date)}</span>
    </time>
  );
}