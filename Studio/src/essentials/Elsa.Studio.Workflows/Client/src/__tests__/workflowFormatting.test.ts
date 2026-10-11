import { describe, expect, it } from "vitest";
import { formatActivityDefinitionDate, humanizeTypeName, resolveActivityDefinitionTitle, truncate } from "../workflowFormatting";

describe("workflow formatting helpers", () => {
  it("humanizes the last CLR/type-key segment", () => {
    expect(humanizeTypeName("Elsa.Activities.Http.Activities.SendHttpRequest")).toBe("Send Http Request");
    expect(humanizeTypeName("SendHTTPRequest")).toBe("Send HTTP Request");
    expect(humanizeTypeName("write_line")).toBe("write line");
    expect(humanizeTypeName("")).toBe("");
    expect(humanizeTypeName(null)).toBe("");
  });

  it("prefers a distinct backend display name and never double-applies humanization", () => {
    expect(resolveActivityDefinitionTitle("Send invoice reminder", "Contoso.SendInvoiceReminder")).toBe("Send invoice reminder");
  });

  it("humanizes the type key when the display name is missing or identical to it", () => {
    const clr = "Elsa.Activities.Http.Activities.SendHttpRequest";
    expect(resolveActivityDefinitionTitle(clr, clr)).toBe("Send Http Request");
    expect(resolveActivityDefinitionTitle(null, clr)).toBe("Send Http Request");
    expect(resolveActivityDefinitionTitle("", "WriteLine")).toBe("Write Line");
  });

  it("falls back to a stable label when nothing is available", () => {
    expect(resolveActivityDefinitionTitle(null, null)).toBe("Unnamed activity");
  });

  it("renders an em-dash for missing, unparseable, or MinValue dates", () => {
    expect(formatActivityDefinitionDate(null)).toBe("—");
    expect(formatActivityDefinitionDate("")).toBe("—");
    expect(formatActivityDefinitionDate("not-a-date")).toBe("—");
    expect(formatActivityDefinitionDate("0001-01-01T00:00:00")).toBe("—");
    expect(formatActivityDefinitionDate("0001-01-01T00:00:00.0000000Z")).toBe("—");
  });

  it("renders a locale date for a real timestamp", () => {
    const rendered = formatActivityDefinitionDate("2026-07-17T10:00:00Z");
    expect(rendered).not.toBe("—");
    expect(rendered).toBe(new Date("2026-07-17T10:00:00Z").toLocaleDateString());
  });

  it("truncates only when over the limit, with a single ellipsis", () => {
    expect(truncate("short")).toBe("short");
    expect(truncate("  padded  ")).toBe("padded");
    expect(truncate("0123456789", 5)).toBe("0123…");
    expect(truncate("0123 6789", 6)).toBe("0123…");
    expect(truncate("exactly-ten", 11)).toBe("exactly-ten");
  });
});
