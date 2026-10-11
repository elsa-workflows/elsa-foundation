import React, { useState } from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it } from "vitest";
import { deriveActivityCategorySuggestions, filterActivityCategorySuggestions } from "../activityCategories";
import { ActivityCategoryCombobox } from "../ActivityDefinitionCreateDialog";

const mounted: Array<{ root: ReturnType<typeof createRoot>; container: HTMLDivElement }> = [];

afterEach(() => {
  for (const item of mounted.splice(0)) {
    flushSync(() => item.root.unmount());
    item.container.remove();
  }
});

describe("Activity Category suggestions", () => {
  it("trims and deduplicates visible catalog categories without changing the first display spelling", () => {
    expect(deriveActivityCategorySuggestions([
      { category: " Finance " },
      { category: "finance" },
      { category: "FINANCE" },
      { category: "Operations" },
      { category: "   " },
      {}
    ])).toEqual(["Finance", "Operations"]);
  });

  it("filters suggestions case-insensitively while leaving a new free-form value valid", () => {
    const suggestions = ["Finance", "Operations", "Document processing"];

    expect(filterActivityCategorySuggestions(suggestions, "pro")).toEqual(["Document processing"]);
    expect(filterActivityCategorySuggestions(suggestions, "new category")).toEqual([]);
  });

  it("supports keyboard selection and an arbitrary free-form category", () => {
    const container = document.createElement("div");
    document.body.append(container);
    const root = createRoot(container);
    mounted.push({ root, container });
    flushSync(() => root.render(<CategoryHarness />));

    const input = container.querySelector<HTMLInputElement>("input[role='combobox']")!;
    input.focus();
    key(input, "ArrowDown");
    expect(input.getAttribute("aria-expanded")).toBe("true");
    key(input, "Enter");
    expect(input.value).toBe("Finance");

    fill(input, "A brand new category");
    expect(input.value).toBe("A brand new category");
  });
});

function CategoryHarness() {
  const [value, setValue] = useState("");
  return <ActivityCategoryCombobox value={value} suggestions={["Finance", "Operations"]} onChange={setValue} />;
}

function key(input: HTMLInputElement, value: string) {
  flushSync(() => input.dispatchEvent(new KeyboardEvent("keydown", { key: value, bubbles: true })));
}

function fill(input: HTMLInputElement, value: string) {
  const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")?.set;
  flushSync(() => {
    setter?.call(input, value);
    input.dispatchEvent(new Event("input", { bubbles: true }));
  });
}
