import React, { useState } from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { AddOption, isAddOptionVisible } from "../AddOption";
import { AdvancedSection, isAdvancedSectionForced } from "../AdvancedSection";
import { ExpressionToggle, isExpressionToggleVisible } from "../ExpressionToggle";
import { OverflowMenu, orderOverflowMenuItems } from "../OverflowMenu";
import { PeerViewSwitch } from "../PeerViewSwitch";
import { RevealToggle } from "../RevealToggle";
import { TechnicalDetails } from "../TechnicalDetails";
import { shouldForceReveal } from "../types";
import { disclosureStorageKey, isDisclosureBoolean, readDisclosureState, writeDisclosureState } from "../useDisclosureState";

let cleanup: (() => void) | null = null;

beforeEach(() => {
  window.localStorage.clear();
});

afterEach(() => {
  cleanup?.();
  cleanup = null;
  vi.restoreAllMocks();
  window.localStorage.clear();
});

function mount(node: React.ReactElement): HTMLElement {
  const host = document.createElement("div");
  document.body.appendChild(host);
  const root = createRoot(host);
  flushSync(() => root.render(node));
  cleanup = () => {
    flushSync(() => root.unmount());
    host.remove();
  };
  return host;
}

describe("never-hide invariant", () => {
  it("treats a non-default, bound or invalid value as forced", () => {
    expect(shouldForceReveal({ isNonDefault: true })).toBe(true);
    expect(shouldForceReveal({ isBound: true })).toBe(true);
    expect(shouldForceReveal({ hasError: true })).toBe(true);
    expect(shouldForceReveal({ forceOpen: true })).toBe(true);
    expect(shouldForceReveal({})).toBe(false);
  });

  it("keeps an optional field visible when it is bound or non-default even if not added", () => {
    expect(isAddOptionVisible({ id: "timeout", label: "Timeout", isNonDefault: true }, new Set(), false)).toBe(true);
    expect(isAddOptionVisible({ id: "headers", label: "Headers", isBound: true }, new Set(), false)).toBe(true);
    expect(isAddOptionVisible({ id: "proxy", label: "Proxy" }, new Set(), false)).toBe(false);
    expect(isAddOptionVisible({ id: "proxy", label: "Proxy" }, new Set(["proxy"]), false)).toBe(true);
  });

  it("opens Advanced when it holds a set value or an error", () => {
    expect(isAdvancedSectionForced({ setCount: 2 })).toBe(true);
    expect(isAdvancedSectionForced({ hasError: true })).toBe(true);
    expect(isAdvancedSectionForced({ setCount: 0 })).toBe(false);
  });

  it("keeps ExpressionToggle visible in Expression or when the value is non-default", () => {
    expect(isExpressionToggleVisible({ value: "expression" })).toBe(true);
    expect(isExpressionToggleVisible({ value: "fixed", isNonDefault: true })).toBe(true);
    expect(isExpressionToggleVisible({ value: "fixed" })).toBe(false);
  });
});

describe("useDisclosureState storage", () => {
  it("namespaces keys under elsa-studio.ui.* and swallows storage errors", () => {
    expect(disclosureStorageKey("designer.view")).toBe("elsa-studio.ui.designer.view");
    expect(disclosureStorageKey("elsa-studio.ui.run.technical")).toBe("elsa-studio.ui.run.technical");
    writeDisclosureState("run.technical", true);
    expect(readDisclosureState("run.technical", false, isDisclosureBoolean)).toBe(true);
    expect(readDisclosureState("missing", false, isDisclosureBoolean)).toBe(false);

    const throwing = {
      getItem() { throw new Error("denied"); },
      setItem() { throw new Error("denied"); },
      removeItem() {},
      clear() {},
      key() { return null; },
      length: 0
    } as Storage;
    expect(readDisclosureState("run.technical", false, isDisclosureBoolean, throwing)).toBe(false);
    expect(() => writeDisclosureState("run.technical", true, throwing)).not.toThrow();
  });

  it("rejects invalid stored values", () => {
    window.localStorage.setItem("elsa-studio.ui.broken", "not-json");
    expect(readDisclosureState("broken", false, isDisclosureBoolean)).toBe(false);
    window.localStorage.setItem("elsa-studio.ui.broken", JSON.stringify("yes"));
    expect(readDisclosureState("broken", false, isDisclosureBoolean)).toBe(false);
  });
});

describe("AddOption", () => {
  it("shows the empty label, adds a field, and remove resets it", () => {
    const added: string[] = [];
    const removed: string[] = [];
    const host = mount(
      <AddOption
        options={[{ id: "timeout", label: "Timeout" }, { id: "proxy", label: "Proxy" }]}
        addedIds={[]}
        defaultPickerOpen
        onAdd={id => added.push(id)}
        onRemove={id => removed.push(id)}
      />
    );

    expect(host.textContent).toContain("No options set");
    expect(host.querySelector("[aria-expanded='true']")).not.toBeNull();
    const timeout = Array.from(document.body.querySelectorAll<HTMLButtonElement>("[role='option']"))
      .find(button => button.textContent?.includes("Timeout"));
    flushSync(() => timeout!.dispatchEvent(new MouseEvent("click", { bubbles: true })));
    expect(added).toEqual(["timeout"]);
  });

  it("never hides a non-default option and remove announces a reset", () => {
    const removed: string[] = [];
    const host = mount(
      <AddOption
        options={[{ id: "timeout", label: "Timeout", isNonDefault: true }]}
        addedIds={[]}
        onRemove={id => removed.push(id)}
        renderField={item => <span>{item.label} field</span>}
      />
    );

    expect(host.textContent).toContain("Timeout field");
    flushSync(() => {
      host.querySelector<HTMLButtonElement>(".studio-add-option-remove")!
        .dispatchEvent(new MouseEvent("click", { bubbles: true }));
    });
    expect(removed).toEqual(["timeout"]);
  });

  it("searches when more than eight options remain", () => {
    const options = Array.from({ length: 9 }, (_, index) => ({ id: `opt-${index}`, label: `Option ${index}` }));
    mount(<AddOption options={options} defaultPickerOpen />);
    expect(document.body.querySelector("[role='searchbox']")).not.toBeNull();
  });
});

describe("RevealToggle", () => {
  it("reveals the group when on and greys it out with a note when forced off", () => {
    function Harness() {
      const [checked, setChecked] = useState(false);
      return (
        <RevealToggle label="Send headers" checked={checked} onCheckedChange={setChecked} forceOpen>
          <span>Header fields</span>
        </RevealToggle>
      );
    }

    const host = mount(<Harness />);
    const toggle = host.querySelector<HTMLButtonElement>("[role='switch']")!;
    expect(toggle.getAttribute("aria-checked")).toBe("false");
    expect(toggle.getAttribute("aria-expanded")).toBe("true");
    expect(host.textContent).toContain("will be ignored");
    expect(host.textContent).toContain("Header fields");

    flushSync(() => toggle.dispatchEvent(new MouseEvent("click", { bubbles: true })));
    expect(toggle.getAttribute("aria-checked")).toBe("true");
    expect(host.textContent).not.toContain("will be ignored");
  });

  it("hides the group when off and not forced", () => {
    const host = mount(
      <RevealToggle label="Send query" checked={false} onCheckedChange={() => undefined}>
        <span>Query fields</span>
      </RevealToggle>
    );
    expect(host.textContent).not.toContain("Query fields");
    expect(host.querySelector("[role='switch']")?.getAttribute("aria-expanded")).toBe("false");
  });
});

describe("AdvancedSection", () => {
  it("stays open when it holds a non-default value and exposes aria-expanded", () => {
    const host = mount(
      <AdvancedSection label="Advanced" summary="retries, proxy" setCount={2}>
        <span>Retry policy</span>
      </AdvancedSection>
    );
    const trigger = host.querySelector<HTMLButtonElement>(".studio-disclosure-fold-trigger")!;
    expect(trigger.getAttribute("aria-expanded")).toBe("true");
    expect(host.textContent).toContain("2 set");
    expect(host.textContent).toContain("Retry policy");
    flushSync(() => trigger.dispatchEvent(new MouseEvent("click", { bubbles: true })));
    expect(trigger.getAttribute("aria-expanded")).toBe("true");
  });

  it("opens itself when it holds a validation error", () => {
    const host = mount(
      <AdvancedSection label="Advanced" hasError>
        <span>Broken retry</span>
      </AdvancedSection>
    );
    expect(host.querySelector(".studio-disclosure-fold-trigger")?.getAttribute("aria-expanded")).toBe("true");
    expect(host.textContent).toContain("Broken retry");
  });
});

describe("TechnicalDetails", () => {
  it("starts collapsed and toggles with aria-expanded / aria-controls", () => {
    const host = mount(
      <TechnicalDetails label="Technical details" summary="journal, variables">
        <span>Call stack</span>
      </TechnicalDetails>
    );
    const trigger = host.querySelector<HTMLButtonElement>(".studio-disclosure-fold-trigger")!;
    const bodyId = trigger.getAttribute("aria-controls");
    expect(trigger.getAttribute("aria-expanded")).toBe("false");
    expect(host.querySelector(`[id="${bodyId}"]`)?.hasAttribute("hidden")).toBe(true);

    flushSync(() => trigger.dispatchEvent(new MouseEvent("click", { bubbles: true })));
    expect(trigger.getAttribute("aria-expanded")).toBe("true");
    expect(host.textContent).toContain("Call stack");
  });

  it("stays open when forceOpen is set", () => {
    const host = mount(
      <TechnicalDetails forceOpen>
        <span>Incidents</span>
      </TechnicalDetails>
    );
    const trigger = host.querySelector<HTMLButtonElement>(".studio-disclosure-fold-trigger")!;
    expect(trigger.getAttribute("aria-expanded")).toBe("true");
    flushSync(() => trigger.dispatchEvent(new MouseEvent("click", { bubbles: true })));
    expect(trigger.getAttribute("aria-expanded")).toBe("true");
  });

  it("remembers its open state under persistKey", () => {
    const first = mount(
      <TechnicalDetails persistKey="run.technical">
        <span>Journal</span>
      </TechnicalDetails>
    );
    flushSync(() => {
      first.querySelector<HTMLButtonElement>(".studio-disclosure-fold-trigger")!
        .dispatchEvent(new MouseEvent("click", { bubbles: true }));
    });
    cleanup?.();
    cleanup = null;

    const second = mount(
      <TechnicalDetails persistKey="run.technical">
        <span>Journal</span>
      </TechnicalDetails>
    );
    expect(second.querySelector(".studio-disclosure-fold-trigger")?.getAttribute("aria-expanded")).toBe("true");
  });
});

describe("OverflowMenu", () => {
  it("places destructive items last and separated", () => {
    expect(orderOverflowMenuItems([
      { id: "delete", label: "Delete", destructive: true, onSelect: () => undefined },
      { id: "retry", label: "Retry", onSelect: () => undefined }
    ])).toEqual([
      expect.objectContaining({ id: "retry" }),
      { type: "separator" },
      expect.objectContaining({ id: "delete" })
    ]);
  });

  it("opens from the trigger, supports keyboard movement, and restores focus", () => {
    const selected: string[] = [];
    const host = mount(
      <OverflowMenu
        items={[
          { id: "retry", label: "Retry", onSelect: () => selected.push("retry") },
          { id: "cancel", label: "Cancel", destructive: true, onSelect: () => selected.push("cancel") }
        ]}
      />
    );
    const trigger = host.querySelector<HTMLButtonElement>(".studio-overflow-menu-trigger")!;
    expect(trigger.getAttribute("aria-expanded")).toBe("false");
    flushSync(() => trigger.dispatchEvent(new MouseEvent("click", { bubbles: true })));
    expect(trigger.getAttribute("aria-expanded")).toBe("true");

    const menu = document.body.querySelector<HTMLElement>("[role='menu']")!;
    expect(menu.querySelector("[data-destructive='true']")?.textContent).toBe("Cancel");
    flushSync(() => menu.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true })));
    expect(trigger.getAttribute("aria-expanded")).toBe("false");
  });
});

describe("ExpressionToggle", () => {
  it("exposes a tablist and keeps Expression selected visible", () => {
    const host = mount(
      <ExpressionToggle label="URL" value="expression" onChange={() => undefined}>
        <input defaultValue="${input.orderId}" />
      </ExpressionToggle>
    );
    const tabs = host.querySelectorAll<HTMLButtonElement>("[role='tab']");
    expect(host.querySelector("[role='tablist']")).not.toBeNull();
    expect(tabs[1].getAttribute("aria-selected")).toBe("true");
    expect(host.querySelector(".studio-expression-toggle")?.getAttribute("data-visible")).toBe("true");
  });
});

describe("PeerViewSwitch", () => {
  it("remembers the last view and implements tablist keyboard movement", () => {
    function Harness() {
      const [view, setView] = useState("designer");
      return (
        <PeerViewSwitch
          persistKey="designer.view"
          views={[{ id: "designer", label: "Designer" }, { id: "code", label: "Code" }]}
          value={view}
          onChange={setView}
        />
      );
    }

    const host = mount(<Harness />);
    const tabs = () => host.querySelectorAll<HTMLButtonElement>("[role='tab']");
    expect(tabs()[0].getAttribute("aria-selected")).toBe("true");
    flushSync(() => tabs()[1].dispatchEvent(new MouseEvent("click", { bubbles: true })));
    expect(tabs()[1].getAttribute("aria-selected")).toBe("true");
    expect(window.localStorage.getItem("elsa-studio.ui.designer.view")).toBe(JSON.stringify("code"));
  });
});
