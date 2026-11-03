/** Native EventSource cannot suppress same-origin cookies. Use the SDK response transport for anonymous SSE. */
export function connectAnonymousLogStream(
  request: (init: RequestInit) => Promise<Response>,
  onOpen: () => void,
  onEvent: (event: MessageEvent<string>) => void,
  onError: () => void
) {
  const controller = new AbortController();
  let retry: ReturnType<typeof setTimeout> | undefined;
  let lastEventId = "";

  async function connect() {
    if (controller.signal.aborted) return;
    try {
      const headers = new Headers({ Accept: "text/event-stream" });
      if (lastEventId) headers.set("Last-Event-ID", lastEventId);
      const response = await request({ credentials: "omit", cache: "no-store", headers, signal: controller.signal });
      if (controller.signal.aborted) {await response.body?.cancel();return;}
      if (!response.ok || !response.body) throw new Error("Structured log stream is unavailable.");
      onOpen();
      const reader = response.body.getReader();
      const cancel = () => {void reader.cancel().catch(() => undefined);};
      controller.signal.addEventListener("abort", cancel, { once: true });
      try {
        const decoder = new TextDecoder();
        let buffer = "", name = "message", pendingId = lastEventId, data: string[] = [];
        while (!controller.signal.aborted) {
          const chunk = await reader.read();
          if (chunk.done) break;
          buffer += decoder.decode(chunk.value, { stream: true });
          let end: number;
          while ((end = buffer.indexOf("\n")) >= 0) {
            const line = buffer.slice(0, end).replace(/\r$/, "");
            buffer = buffer.slice(end + 1);
            if (!line) {
              lastEventId = pendingId;
              if (data.length && !controller.signal.aborted) onEvent(new MessageEvent(name, { data: data.join("\n"), lastEventId }));
              name = "message";data = [];
            } else {
              const separator = line.indexOf(":");
              const field = separator < 0 ? line : line.slice(0, separator);
              const value = separator < 0 ? "" : line.slice(separator + 1).replace(/^ /, "");
              if (field === "event") name = value;
              else if (field === "data") data.push(value);
              else if (field === "id" && !value.includes("\0")) pendingId = value;
            }
          }
        }
      } finally {
        controller.signal.removeEventListener("abort", cancel);
        await reader.cancel().catch(() => undefined);
        reader.releaseLock();
      }
    } catch { /* Disconnection is reported below and retried while the view remains mounted. */ }
    if (!controller.signal.aborted) {
      onError();
      if (!controller.signal.aborted) retry = setTimeout(() => {void connect();}, 1000);
    }
  }

  void connect();
  return () => {controller.abort();clearTimeout(retry);};
}
