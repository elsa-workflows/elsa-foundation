import React from "react";
import { formatStudioDate, formatStudioDateParts, useStudioTimeZone } from "../../../sdk/timeZone";

/** Locale-aware inline timestamp for plain-text content such as confirmation messages. */
export function formatStudioDateTime(value: string | null, fallback: string): string {
  const date = value ? new Date(value) : null;
  return date && Number.isFinite(date.getTime()) ? formatStudioDate(date) : fallback;
}

export function StudioTimestamp({ value, fallback = null, invalidFallback, format = "stacked" }: {
  value?: string | null;
  /** All layouts show seconds and a UTC offset; the default stacks the date above the time. */
  format?: "stacked" | "time" | "date-time";
  fallback?: React.ReactNode;
  invalidFallback?: React.ReactNode;
}) {
  const preference = useStudioTimeZone();
  if (!value) return <>{fallback}</>;

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return <>{invalidFallback ?? fallback ?? value}</>;

  const parts = formatStudioDateParts(date, preference);
  if (format !== "stacked") {
    return <time dateTime={value} title={parts.dateTime}>{format === "time" ? parts.time : parts.dateTime}</time>;
  }

  return (
    <time className="studio-timestamp" dateTime={value} title={parts.dateTime}>
      <span className="studio-timestamp-date">{parts.date}</span>
      <span className="studio-timestamp-time">{parts.time}</span>
    </time>
  );
}
