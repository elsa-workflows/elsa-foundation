import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import { useNoticeTimer } from "../feedback/useNoticeTimer";

let cleanup: (() => void) | null = null;

afterEach(() => {
  cleanup?.();
  cleanup = null;
  vi.useRealTimers();
});

function Probe({ durationMs, resetKey, onElapse }: { durationMs: number | null; resetKey?: unknown; onElapse(): void }) {
  const handlers = useNoticeTimer({ durationMs, onElapse, resetKey });
  return <div data-testid="probe" tabIndex={-1} {...handlers} />;
}

function mount(node: React.ReactElement) {
  const host = document.createElement("div");
  document.body.appendChild(host);
  const root = createRoot(host);
  flushSync(() => root.render(node));
  cleanup = () => {
    flushSync(() => root.unmount());
    host.remove();
  };
  return {
    probe: host.querySelector<HTMLElement>("[data-testid=probe]")!,
    rerender: (next: React.ReactElement) => flushSync(() => root.render(next))
  };
}

function advance(ms: number) {
  flushSync(() => vi.advanceTimersByTime(ms));
}

describe("useNoticeTimer", () => {
  it("elapses after the duration", () => {
    vi.useFakeTimers();
    const onElapse = vi.fn();
    mount(<Probe durationMs={1_000} onElapse={onElapse} />);

    advance(999);
    expect(onElapse).not.toHaveBeenCalled();
    advance(1);
    expect(onElapse).toHaveBeenCalledTimes(1);
  });

  it("never elapses for a null duration, even after hover and focus", () => {
    vi.useFakeTimers();
    const onElapse = vi.fn();
    const { probe } = mount(<Probe durationMs={null} onElapse={onElapse} />);

    advance(60_000);
    flushSync(() => {
      probe.dispatchEvent(new MouseEvent("mouseover", { bubbles: true }));
      probe.dispatchEvent(new MouseEvent("mouseout", { bubbles: true, relatedTarget: document.body }));
    });
    advance(60_000);
    expect(onElapse).not.toHaveBeenCalled();
  });

  it("pauses on hover and resumes with the remaining time", () => {
    vi.useFakeTimers();
    const onElapse = vi.fn();
    const { probe } = mount(<Probe durationMs={8_000} onElapse={onElapse} />);

    advance(5_000);
    flushSync(() => {
      probe.dispatchEvent(new MouseEvent("mouseover", { bubbles: true }));
    });
    advance(10_000);
    expect(onElapse).not.toHaveBeenCalled();

    flushSync(() => {
      probe.dispatchEvent(new MouseEvent("mouseout", { bubbles: true, relatedTarget: document.body }));
    });
    advance(2_999);
    expect(onElapse).not.toHaveBeenCalled();
    advance(1);
    expect(onElapse).toHaveBeenCalledTimes(1);
  });

  it("stays paused while focus is inside and resumes when it leaves", () => {
    vi.useFakeTimers();
    const onElapse = vi.fn();
    const { probe } = mount(<Probe durationMs={1_000} onElapse={onElapse} />);

    flushSync(() => probe.focus());
    advance(5_000);
    expect(onElapse).not.toHaveBeenCalled();

    flushSync(() => probe.blur());
    advance(1_000);
    expect(onElapse).toHaveBeenCalledTimes(1);
  });

  it("restarts the full duration when resetKey changes", () => {
    vi.useFakeTimers();
    const onElapse = vi.fn();
    const { rerender } = mount(<Probe durationMs={1_000} resetKey="a" onElapse={onElapse} />);

    advance(900);
    rerender(<Probe durationMs={1_000} resetKey="b" onElapse={onElapse} />);
    advance(900);
    expect(onElapse).not.toHaveBeenCalled();
    advance(100);
    expect(onElapse).toHaveBeenCalledTimes(1);
  });
});
