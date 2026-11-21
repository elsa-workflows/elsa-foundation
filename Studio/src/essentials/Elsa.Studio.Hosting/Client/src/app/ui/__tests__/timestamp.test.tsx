import React from "react";
import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it } from "vitest";
import { StudioTimestamp } from "../data-grid/StudioTimestamp";

describe("StudioTimestamp", () => {
  it("renders a localized date and time with a full timestamp tooltip", () => {
    const value = "2026-11-20T22:59:15.000Z";
    const date = new Date(value);
    const markup = renderToStaticMarkup(<StudioTimestamp value={value} />);

    expect(markup).toContain(`dateTime="${value}"`);
    expect(markup).toContain(`title="${new Intl.DateTimeFormat(undefined, { dateStyle: "full", timeStyle: "long" }).format(date)}"`);
    expect(markup).toContain(`<span class="studio-timestamp-date">${new Intl.DateTimeFormat(undefined, { dateStyle: "medium" }).format(date)}</span>`);
    expect(markup).toContain(`<span class="studio-timestamp-time">${new Intl.DateTimeFormat(undefined, { timeStyle: "short" }).format(date)}</span>`);
  });

  it("uses the caller fallback for missing or invalid values", () => {
    expect(renderToStaticMarkup(<StudioTimestamp fallback="Unavailable" />)).toBe("Unavailable");
    expect(renderToStaticMarkup(<StudioTimestamp value="not-a-date" fallback="Unavailable" />)).toBe("Unavailable");
  });

  it("preserves an invalid raw value when no fallback is supplied", () => {
    expect(renderToStaticMarkup(<StudioTimestamp value="not-a-date" />)).toBe("not-a-date");
  });
});