/** Native EventSource cannot suppress same-origin cookies. Use the SDK response transport for anonymous SSE. */
export function connectAnonymousLogStream(
  request: (init: RequestInit) => Promise<Response>,
  onOpen: () => void,
  onEvent: (event: MessageEvent<string>) => void,
  onError: () => void
) {
  const controller = new AbortController();
  let retry: ReturnType<typeof setTimeout> | undefined;

  async function connect() {
    if (controller.signal.aborted) return;
    try {
      const response = await request({ credentials: "omit", cache: "no-store", headers: { Accept: "text/event-stream" }, signal: controller.signal });
      if (controller.signal.aborted) {await response.body?.cancel();return;}
      if (!response.ok || !response.body) throw new Error("Structured log stream is unavailable.");
      onOpen();
      const reader = response.body.getReader();
      const cancel = () => {void reader.cancel().catch(() => undefined);};
      controller.signal.addEventListener("abort", cancel, { once: true });
      try {
        const decoder = new TextDecoder();
        let buffer = "", name = "message", data: string[] = [];
        while (!controller.signal.aborted) {
          const chunk = await reader.read();
          if (chunk.done) break;
          buffer += decoder.decode(chunk.value, { stream: true });
          let end: number;
          while ((end = buffer.indexOf("\n")) >= 0) {
            const line = buffer.slice(0, end).replace(/\r$/, "");
            buffer = buffer.slice(end + 1);
            if (!line) {
              if (data.length && !controller.signal.aborted) onEvent(new MessageEvent(name, { data: data.join("\n") }));
              name = "message";data = [];
            } else if (line.startsWith("event:")) name = line.slice(6).replace(/^ /, "");
            else if (line.startsWith("data:")) data.push(line.slice(5).replace(/^ /, ""));
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
