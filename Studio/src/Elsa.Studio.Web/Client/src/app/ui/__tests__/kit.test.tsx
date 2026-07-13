import React, { useState } from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import { StudioButton } from "../forms/Button";
import { StudioSearchInput } from "../forms/SearchInput";
import { StudioActionNotice } from "../feedback/ActionNotice";
import { StatusPill } from "../feedback/StatusPill";
import { StudioListContainer, StudioListRow } from "../list/ListRow";
import { StudioSparkline, StudioStatTile } from "../stat/StatTile";

let cleanup: (() => void) | null = null;

afterEach(() => {
  cleanup?.();
  cleanup = null;
  vi.useRealTimers();
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

describe("StudioButton", () => {
  it("emits the shared class plus variant/size data-attributes and defaults to type=button", () => {
    const host = mount(<StudioButton variant="primary" size="sm">Save</StudioButton>);
    const button = host.querySelector("button")!;
    expect(button.classList.contains("studio-button")).toBe(true);
    expect(button.getAttribute("data-variant")).toBe("primary");
    expect(button.getAttribute("data-size")).toBe("sm");
    expect(button.getAttribute("type")).toBe("button");
  });

  it("merges a caller className and forwards native props", () => {
    let clicks = 0;
    const host = mount(<StudioButton className="danger" onClick={() => clicks++}>Delete</StudioButton>);
    const button = host.querySelector("button")!;
    expect(button.className).toBe("studio-button danger");
    button.dispatchEvent(new MouseEvent("click", { bubbles: true }));
    expect(clicks).toBe(1);
  });
});

describe("StudioListRow", () => {
  it("renders a plain listitem with title/subtitle and no activation affordance", () => {
    const host = mount(
      <StudioListContainer>
        <StudioListRow title="Workflows" subtitle="Full-stack" />
      </StudioListContainer>
    );
    const list = host.querySelector(".studio-list")!;
    expect(list.getAttribute("role")).toBe("list");
    const row = host.querySelector(".studio-list-row")!;
    expect(row.getAttribute("role")).toBe("listitem");
    expect(row.hasAttribute("tabindex")).toBe(false);
    expect(row.getAttribute("aria-selected")).toBeNull();
    expect(host.querySelector(".studio-list-row-title")?.textContent).toBe("Workflows");
    expect(host.querySelector(".studio-list-row-subtitle")?.textContent).toBe("Full-stack");
  });

  it("becomes selectable when onSelect is provided and activates via click and keyboard", () => {
    let activations = 0;
    const host = mount(
      <StudioListRow title="Dashboard" selected onSelect={() => activations++} trailing={<span>x</span>} />
    );
    const row = host.querySelector(".studio-list-row")!;
    expect(row.classList.contains("selected")).toBe(true);
    expect(row.getAttribute("tabindex")).toBe("0");
    expect(row.getAttribute("aria-selected")).toBe("true");
    expect(host.querySelector(".studio-list-row-trailing")?.textContent).toBe("x");

    row.dispatchEvent(new MouseEvent("click", { bubbles: true }));
    row.dispatchEvent(new KeyboardEvent("keydown", { key: "Enter", bubbles: true }));
    row.dispatchEvent(new KeyboardEvent("keydown", { key: " ", bubbles: true }));
    expect(activations).toBe(3);
  });
});

describe("StudioSearchInput", () => {
  it("renders a searchbox input with a decorative icon", () => {
    const host = mount(<StudioSearchInput placeholder="Filter" />);
    const input = host.querySelector("input")!;
    expect(input.getAttribute("type")).toBe("search");
    expect(input.getAttribute("role")).toBe("searchbox");
    expect(input.getAttribute("placeholder")).toBe("Filter");
    expect(host.querySelector(".studio-search-icon")?.getAttribute("aria-hidden")).toBe("true");
  });
});

describe("StatusPill", () => {
  it("renders an outlined pill carrying the tone", () => {
    const host = mount(<StatusPill tone="warning">Beta</StatusPill>);
    const pill = host.querySelector(".studio-status-pill")!;
    expect(pill.getAttribute("data-tone")).toBe("warning");
    expect(pill.textContent).toBe("Beta");
  });

  it("defaults to the neutral tone", () => {
    const host = mount(<StatusPill>Tag</StatusPill>);
    expect(host.querySelector(".studio-status-pill")?.getAttribute("data-tone")).toBe("neutral");
  });
});

describe("StudioActionNotice", () => {
  it("announces its message politely and provides accessible action and dismiss controls", () => {
    const host = mount(
      <StudioActionNotice message="Entry removed" actionLabel="Undo" onAction={() => undefined} />
    );
    const notice = host.querySelector<HTMLElement>(".studio-action-notice")!;

    expect(notice.getAttribute("role")).toBe("status");
    expect(notice.getAttribute("aria-live")).toBe("polite");
    expect(notice.getAttribute("aria-atomic")).toBe("true");
    expect(notice.textContent).toContain("Entry removed");
    expect(host.querySelector<HTMLButtonElement>(".studio-action-notice-action")?.textContent).toBe("Undo");
    expect(host.querySelector<HTMLButtonElement>(".studio-action-notice-dismiss")?.getAttribute("aria-label"))
      .toBe("Dismiss notification");
  });

  it("runs its optional action and dismisses the notice", () => {
    const onAction = vi.fn();
    const onDismiss = vi.fn();
    const host = mount(
      <StudioActionNotice message="Entry removed" actionLabel="Undo" onAction={onAction} onDismiss={onDismiss} />
    );

    flushSync(() => {
      host.querySelector<HTMLButtonElement>(".studio-action-notice-action")!
        .dispatchEvent(new MouseEvent("click", { bubbles: true }));
    });

    expect(onAction).toHaveBeenCalledOnce();
    expect(onDismiss).toHaveBeenCalledOnce();
    expect(host.querySelector(".studio-action-notice")).toBeNull();
  });

  it("supports explicit dismissal", () => {
    const onDismiss = vi.fn();
    const host = mount(<StudioActionNotice message="JSON copied" onDismiss={onDismiss} />);

    flushSync(() => {
      host.querySelector<HTMLButtonElement>(".studio-action-notice-dismiss")!
        .dispatchEvent(new MouseEvent("click", { bubbles: true }));
    });

    expect(onDismiss).toHaveBeenCalledOnce();
    expect(host.querySelector(".studio-action-notice")).toBeNull();
  });

  it("dismisses automatically after its configured lifetime", () => {
    vi.useFakeTimers();
    const onDismiss = vi.fn();
    const host = mount(<StudioActionNotice durationMs={1_000} message="Entry removed" onDismiss={onDismiss} />);

    flushSync(() => vi.advanceTimersByTime(999));
    expect(host.querySelector(".studio-action-notice")).not.toBeNull();
    flushSync(() => vi.advanceTimersByTime(1));
    expect(onDismiss).toHaveBeenCalledOnce();
    expect(host.querySelector(".studio-action-notice")).toBeNull();
  });

  it("uses an eight-second lifetime by default", () => {
    vi.useFakeTimers();
    const host = mount(<StudioActionNotice message="Entry removed" />);

    flushSync(() => vi.advanceTimersByTime(7_999));
    expect(host.querySelector(".studio-action-notice")).not.toBeNull();
    flushSync(() => vi.advanceTimersByTime(1));
    expect(host.querySelector(".studio-action-notice")).toBeNull();
  });

  it("pauses its remaining lifetime while hovered", () => {
    vi.useFakeTimers();
    const host = mount(<StudioActionNotice durationMs={1_000} message="Entry removed" />);
    const notice = host.querySelector<HTMLElement>(".studio-action-notice")!;

    flushSync(() => vi.advanceTimersByTime(400));
    flushSync(() => notice.dispatchEvent(new MouseEvent("mouseover", { bubbles: true })));
    flushSync(() => vi.advanceTimersByTime(1_000));
    expect(host.querySelector(".studio-action-notice")).not.toBeNull();

    flushSync(() => notice.dispatchEvent(new MouseEvent("mouseout", { bubbles: true, relatedTarget: document.body })));
    flushSync(() => vi.advanceTimersByTime(599));
    expect(host.querySelector(".studio-action-notice")).not.toBeNull();
    flushSync(() => vi.advanceTimersByTime(1));
    expect(host.querySelector(".studio-action-notice")).toBeNull();
  });

  it("pauses its remaining lifetime while focus is inside", () => {
    vi.useFakeTimers();
    const host = mount(
      <StudioActionNotice durationMs={1_000} message="Entry removed" actionLabel="Undo" onAction={() => undefined} />
    );
    const action = host.querySelector<HTMLButtonElement>(".studio-action-notice-action")!;

    flushSync(() => vi.advanceTimersByTime(250));
    flushSync(() => action.focus());
    flushSync(() => vi.advanceTimersByTime(1_000));
    expect(host.querySelector(".studio-action-notice")).not.toBeNull();

    flushSync(() => action.blur());
    flushSync(() => vi.advanceTimersByTime(749));
    expect(host.querySelector(".studio-action-notice")).not.toBeNull();
    flushSync(() => vi.advanceTimersByTime(1));
    expect(host.querySelector(".studio-action-notice")).toBeNull();
  });

  it("replaces its content and restarts its lifetime when the message changes", () => {
    vi.useFakeTimers();

    function ReplacementHarness() {
      const [message, setMessage] = useState("First entry removed");
      return (
        <>
          <button type="button" onClick={() => setMessage("Second entry removed")}>Replace</button>
          <StudioActionNotice durationMs={1_000} message={message} />
        </>
      );
    }

    const host = mount(<ReplacementHarness />);
    flushSync(() => vi.advanceTimersByTime(800));
    flushSync(() => {
      host.querySelector<HTMLButtonElement>("button")!
        .dispatchEvent(new MouseEvent("click", { bubbles: true }));
    });

    expect(host.textContent).not.toContain("First entry removed");
    expect(host.textContent).toContain("Second entry removed");
    flushSync(() => vi.advanceTimersByTime(999));
    expect(host.querySelector(".studio-action-notice")).not.toBeNull();
    flushSync(() => vi.advanceTimersByTime(1));
    expect(host.querySelector(".studio-action-notice")).toBeNull();
  });
});

describe("StudioStatTile", () => {
  it("renders label, value, toned delta and a sparkline when a trend is supplied", () => {
    const host = mount(
      <StudioStatTile label="Executions" value="12.4k" delta="+8%" deltaTone="success" trend={[1, 3, 2, 5]} />
    );
    expect(host.querySelector(".studio-stat-tile-label")?.textContent).toBe("Executions");
    expect(host.querySelector(".studio-stat-tile-value")?.textContent).toBe("12.4k");
    const delta = host.querySelector(".studio-stat-tile-delta")!;
    expect(delta.getAttribute("data-tone")).toBe("success");
    expect(delta.textContent).toBe("+8%");
    expect(host.querySelector(".studio-sparkline path")).not.toBeNull();
  });

  it("omits the delta and sparkline when not provided", () => {
    const host = mount(<StudioStatTile label="Faulted" value="0" />);
    expect(host.querySelector(".studio-stat-tile-delta")).toBeNull();
    expect(host.querySelector(".studio-sparkline")).toBeNull();
  });
});

describe("StudioSparkline", () => {
  it("draws a multi-segment path stroked with the chart token for >= 2 points", () => {
    const host = mount(<StudioSparkline points={[0, 5, 2, 8]} />);
    const path = host.querySelector("path")!;
    expect(path.getAttribute("stroke")).toBe("var(--chart-1)");
    // One move + three line commands.
    expect((path.getAttribute("d")?.match(/L/g) ?? []).length).toBe(3);
  });

  it("falls back to a flat baseline for fewer than two points", () => {
    const host = mount(<StudioSparkline points={[7]} width={100} height={20} />);
    expect(host.querySelector("path")?.getAttribute("d")).toBe("M0 10 L100 10");
  });
});
