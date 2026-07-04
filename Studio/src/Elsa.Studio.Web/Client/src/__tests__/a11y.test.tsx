import React, { useState } from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { ShellFrame } from "../app/App";
import { ThemeProvider } from "../app/components/ThemeProvider";
import { StudioTabs, type StudioTabItem } from "../app/ui/layout/Tabs";
import { StudioAlert } from "../app/ui/feedback/FeedbackStates";
import { StudioDataGrid } from "../app/ui/data-grid/DataGrid";

let container: HTMLDivElement;
let root: Root;

beforeEach(() => {
  container = document.createElement("div");
  document.body.appendChild(container);
  root = createRoot(container);
});

afterEach(() => {
  flushSync(() => root.unmount());
  container.remove();
});

function render(node: React.ReactNode) {
  flushSync(() => root.render(node));
}

function fireKey(element: Element, key: string) {
  flushSync(() => {
    element.dispatchEvent(new KeyboardEvent("keydown", { key, bubbles: true }));
  });
}

function tabs(): HTMLButtonElement[] {
  return Array.from(container.querySelectorAll<HTMLButtonElement>('[role="tab"]'));
}

describe("StudioTabs keyboard navigation", () => {
  const items: StudioTabItem[] = [
    { id: "one", label: "One" },
    { id: "two", label: "Two" },
    { id: "three", label: "Three" }
  ];

  function ControlledTabs() {
    const [active, setActive] = useState("one");
    return <StudioTabs tabs={items} activeTab={active} onSelect={setActive} ariaLabel="Sections" />;
  }

  it("applies roving tabindex so only the active tab is in the tab order", () => {
    render(<ControlledTabs />);
    expect(tabs().map(tab => tab.tabIndex)).toEqual([0, -1, -1]);
    expect(tabs()[0].getAttribute("aria-selected")).toBe("true");
  });

  it("links each tab to its panel via aria-controls / id", () => {
    render(<ControlledTabs />);
    const [first] = tabs();
    const panelId = first.getAttribute("aria-controls");
    expect(panelId).toBeTruthy();
    expect(first.id).toBeTruthy();
    expect(first.id).not.toBe(panelId);
  });

  it("selects the next tab with ArrowRight and wraps at the end", () => {
    render(<ControlledTabs />);
    const list = container.querySelector('[role="tablist"]')!;

    fireKey(list, "ArrowRight");
    expect(tabs().map(tab => tab.getAttribute("aria-selected"))).toEqual(["false", "true", "false"]);
    expect(tabs().map(tab => tab.tabIndex)).toEqual([-1, 0, -1]);

    fireKey(list, "ArrowRight");
    fireKey(list, "ArrowRight");
    // Wrapped back to the first tab.
    expect(tabs()[0].getAttribute("aria-selected")).toBe("true");
  });

  it("selects the previous tab with ArrowLeft and wraps at the start", () => {
    render(<ControlledTabs />);
    const list = container.querySelector('[role="tablist"]')!;

    fireKey(list, "ArrowLeft");
    expect(tabs()[2].getAttribute("aria-selected")).toBe("true");
  });

  it("jumps to the first and last tab with Home and End", () => {
    render(<ControlledTabs />);
    const list = container.querySelector('[role="tablist"]')!;

    fireKey(list, "End");
    expect(tabs()[2].getAttribute("aria-selected")).toBe("true");

    fireKey(list, "Home");
    expect(tabs()[0].getAttribute("aria-selected")).toBe("true");
  });

  it("moves DOM focus onto the newly selected tab", () => {
    render(<ControlledTabs />);
    const list = container.querySelector('[role="tablist"]')!;
    tabs()[0].focus();

    fireKey(list, "ArrowRight");
    expect(document.activeElement).toBe(tabs()[1]);
  });
});

describe("StudioAlert roles", () => {
  it("uses role=alert for urgent tones", () => {
    render(
      <>
        <StudioAlert tone="danger">boom</StudioAlert>
        <StudioAlert tone="warning">careful</StudioAlert>
      </>
    );
    const alerts = container.querySelectorAll('[role="alert"]');
    expect(alerts).toHaveLength(2);
    expect(container.querySelector('.studio-alert[data-tone="danger"]')?.getAttribute("role")).toBe("alert");
    expect(container.querySelector('.studio-alert[data-tone="warning"]')?.getAttribute("role")).toBe("alert");
  });

  it("uses role=status for informational tones", () => {
    render(
      <>
        <StudioAlert tone="info">fyi</StudioAlert>
        <StudioAlert tone="success">done</StudioAlert>
        <StudioAlert>default</StudioAlert>
      </>
    );
    expect(container.querySelectorAll('[role="status"]')).toHaveLength(3);
    expect(container.querySelector('.studio-alert[data-tone="info"]')?.getAttribute("role")).toBe("status");
    expect(container.querySelector('.studio-alert[data-tone="success"]')?.getAttribute("role")).toBe("status");
  });
});

describe("StudioDataGrid semantics", () => {
  const columns = [
    { id: "name", header: "Name", render: (item: { id: string; name: string }) => item.name }
  ];
  const items = [
    { id: "a", name: "Alpha" },
    { id: "b", name: "Beta" }
  ];

  it("uses valid table roles with rows that are not buttons", () => {
    render(<StudioDataGrid columns={columns} items={items} getKey={item => item.id} onSelect={() => {}} selectedKey="a" />);

    expect(container.querySelector('[role="table"]')).not.toBeNull();
    const rows = container.querySelectorAll('.studio-data-grid-row[role="row"]');
    expect(rows).toHaveLength(2);
    rows.forEach(row => expect(row.tagName).not.toBe("BUTTON"));
    expect(container.querySelectorAll('[role="cell"]')).toHaveLength(2);
    // Roving tabindex anchors on the selected row.
    expect((rows[0] as HTMLElement).tabIndex).toBe(0);
    expect((rows[1] as HTMLElement).tabIndex).toBe(-1);
    expect(rows[0].getAttribute("aria-selected")).toBe("true");
  });

  it("activates a row on Enter and Space", () => {
    const selected: string[] = [];
    render(<StudioDataGrid columns={columns} items={items} getKey={item => item.id} onSelect={item => selected.push(item.id)} />);
    const rows = container.querySelectorAll('.studio-data-grid-row[role="row"]');

    fireKey(rows[1], "Enter");
    fireKey(rows[0], " ");
    expect(selected).toEqual(["b", "a"]);
  });

  it("renders the emptyState slot when there are no rows", () => {
    render(<StudioDataGrid columns={columns} items={[]} getKey={item => item.id} emptyState={<span>Nothing here</span>} />);
    expect(container.textContent).toContain("Nothing here");
    expect(container.querySelectorAll('.studio-data-grid-row[role="row"]')).toHaveLength(0);
  });
});

describe("sidebar search filtering", () => {
  const navigation = [
    { id: "dashboard", label: "Dashboard", path: "/dashboard" },
    { id: "extension-builder", label: "Extension Builder", path: "/extension-builder" },
    { id: "modules", label: "Modules", path: "/modules" },
    { id: "package-feeds", label: "Package feeds", path: "/package-feeds" }
  ];

  function renderShell() {
    render(
      <ThemeProvider>
        <ShellFrame
          navigation={navigation}
          panels={[]}
          path="/dashboard"
          title="Dashboard"
          backendBaseUrl="https://backend.example/"
          onNavigate={() => {}}
        >
          <div>content</div>
        </ShellFrame>
      </ThemeProvider>
    );
  }

  function searchInput() {
    return container.querySelector<HTMLInputElement>('input[aria-label="Search modules"]')!;
  }

  function navLinkLabels() {
    return Array.from(container.querySelectorAll(".nav-section a")).map(link => link.textContent?.trim());
  }

  function type(value: string) {
    const input = searchInput();
    flushSync(() => {
      const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!;
      setter.call(input, value);
      input.dispatchEvent(new Event("input", { bubbles: true }));
    });
  }

  it("marks the active nav link with aria-current=page", () => {
    renderShell();
    const active = container.querySelector('.nav-section a[aria-current="page"]');
    expect(active?.textContent).toContain("Dashboard");
    // Exactly one current link.
    expect(container.querySelectorAll('.nav-section a[aria-current="page"]')).toHaveLength(1);
  });

  it("filters nav items by case-insensitive label substring", () => {
    renderShell();
    type("mod");
    const labels = navLinkLabels();
    expect(labels.some(label => label?.includes("Modules"))).toBe(true);
    expect(labels.some(label => label?.includes("Dashboard"))).toBe(false);
    expect(labels.some(label => label?.includes("Extension Builder"))).toBe(false);
  });

  it("shows an empty-result state when nothing matches", () => {
    renderShell();
    type("zzzznomatch");
    expect(container.querySelector(".sidebar-search-empty")?.textContent).toContain("No modules match");
    expect(container.querySelectorAll(".nav-section").length).toBe(0);
  });

  it("clears the query when Escape is pressed", () => {
    renderShell();
    type("mod");
    expect(navLinkLabels().some(label => label?.includes("Dashboard"))).toBe(false);

    fireKey(searchInput(), "Escape");
    expect(searchInput().value).toBe("");
    expect(navLinkLabels().some(label => label?.includes("Dashboard"))).toBe(true);
  });
});
