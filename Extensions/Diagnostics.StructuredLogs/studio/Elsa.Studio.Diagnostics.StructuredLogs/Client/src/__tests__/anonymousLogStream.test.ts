import { afterEach, describe, expect, it, vi } from "vitest";
import { connectAnonymousLogStream } from "../anonymousLogStream";

afterEach(() => vi.useRealTimers());

describe("anonymous structured log stream", () => {
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
