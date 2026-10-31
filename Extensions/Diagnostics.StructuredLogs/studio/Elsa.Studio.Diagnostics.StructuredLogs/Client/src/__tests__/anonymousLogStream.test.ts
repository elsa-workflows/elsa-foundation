import { afterEach, describe, expect, it, vi } from "vitest";
import { connectAnonymousLogStream } from "../anonymousLogStream";

afterEach(() => vi.useRealTimers());

describe("anonymous structured log stream", () => {
  it("resumes after the last complete event and delivers a record committed while disconnected", async () => {
    vi.useFakeTimers();
    const request = vi.fn(async (_init: RequestInit) => new Response(new ReadableStream<Uint8Array>({start(controller) {
      const first = request.mock.calls.length === 1;
      controller.enqueue(new TextEncoder().encode(first ? "id: cursor-1\nevent: entry\ndata: first\n\n" : "id: cursor-2\nevent: entry\ndata: committed-during-disconnect\n\n"));
      controller.close();
    }})));
    const received = vi.fn();
    const close = connectAnonymousLogStream(request, vi.fn(), received, vi.fn());
    try {
      await vi.advanceTimersByTimeAsync(0);
      await vi.advanceTimersByTimeAsync(1000);
      expect(new Headers(request.mock.calls[1][0].headers).get("Last-Event-ID")).toBe("cursor-1");
      expect(received.mock.calls[1][0]).toMatchObject({data:"committed-during-disconnect",lastEventId:"cursor-2"});
    } finally {close();}
  });

  it("discards an incomplete cursor and honors an explicit empty event id", async () => {
    vi.useFakeTimers();
    const request = vi.fn(async (_init: RequestInit) => new Response(new ReadableStream<Uint8Array>({start(controller) {
      const attempt = request.mock.calls.length;
      controller.enqueue(new TextEncoder().encode(attempt === 1 ? "id: complete\ndata: first\n\nid: incomplete\ndata: unfinished" : "id:\ndata: reset\n\n"));
      controller.close();
    }})));
    const close = connectAnonymousLogStream(request, vi.fn(), vi.fn(), vi.fn());
    try {
      await vi.advanceTimersByTimeAsync(0);await vi.advanceTimersByTimeAsync(1000);await vi.advanceTimersByTimeAsync(1000);
      expect(new Headers(request.mock.calls[1][0].headers).get("Last-Event-ID")).toBe("complete");
      expect(new Headers(request.mock.calls[2][0].headers).has("Last-Event-ID")).toBe(false);
    } finally {close();}
  });

  it("decodes named events across chunks, reconnects after EOF and stops retries on close", async () => {
    vi.useFakeTimers();
    const request = vi.fn(async (_init: RequestInit) => new Response(new ReadableStream<Uint8Array>({start(controller) {
      for (const part of [": heartbeat\r\neve", "nt: dropped\r\ndata: {\"count\":\r\n", "data: 2}\r\n\r\n"])
        controller.enqueue(new TextEncoder().encode(part));
      controller.close();
    }})));
    const opened = vi.fn(), received = vi.fn(), failed = vi.fn();
    const close = connectAnonymousLogStream(request, opened, received, failed);
    try {
      await vi.advanceTimersByTimeAsync(0);
      expect(opened).toHaveBeenCalledOnce();expect(failed).toHaveBeenCalledOnce();
      expect(received.mock.calls[0][0]).toMatchObject({type: "dropped", data: "{\"count\":\n2}"});
      await vi.advanceTimersByTimeAsync(1000);
      expect(request).toHaveBeenCalledTimes(2);
      for (const [init] of request.mock.calls) expect(init.credentials).toBe("omit");
    } finally {close();}
    await vi.advanceTimersByTimeAsync(5000);
    expect(request).toHaveBeenCalledTimes(2);
  });

  it("cancels a late response body after the view has closed", async () => {
    let finish!: (response: Response) => void;
    const request = vi.fn((_init: RequestInit) => new Promise<Response>(resolve => {finish = resolve;}));
    const cancelled = vi.fn(), opened = vi.fn(), failed = vi.fn();
    const close = connectAnonymousLogStream(request, opened, vi.fn(), failed);
    close();
    finish(new Response(new ReadableStream({cancel: cancelled})));
    await vi.waitFor(() => expect(cancelled).toHaveBeenCalledOnce());
    expect(opened).not.toHaveBeenCalled();expect(failed).not.toHaveBeenCalled();
  });
});
