import { AbortError, HttpClient, HttpResponse, TimeoutError, type HttpRequest } from "@microsoft/signalr";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";

/** SignalR's withCredentials:false still sends same-origin cookies. Delegate polling to the anonymous SDK client. */
export class AnonymousConsoleHttpClient extends HttpClient {
  constructor(private readonly context: StudioEndpointContext) {super();}
  async send(request: HttpRequest): Promise<HttpResponse> {
    if (request.abortSignal?.aborted) throw new AbortError();
    if (!request.url || !this.context.http.requestResponse) throw new Error("Anonymous console streaming requires a streaming-capable backend transport.");
    const controller = new AbortController();
    const abort = () => controller.abort();
    if (request.abortSignal) request.abortSignal.onabort = abort;
    let timedOut = false;
    const timeout = request.timeout ? setTimeout(() => {timedOut = true;controller.abort();}, request.timeout) : undefined;
    try {
      const headers = new Headers(request.headers);
      if (request.content) headers.set("Content-Type", typeof request.content === "string" ? "text/plain;charset=UTF-8" : "application/octet-stream");
      const response = await this.context.http.requestResponse(request.url, {
        method: request.method ?? "GET", headers, body: request.content || undefined,
        credentials: "omit", cache: "no-store", signal: controller.signal,
        ...(request.timeout ? {timeoutMs:request.timeout} : {})
      });
      const content = request.responseType === "arraybuffer" ? await response.arrayBuffer() : await response.text();
      return new HttpResponse(response.status, response.statusText, content);
    } catch (error) {
      if (timedOut) throw new TimeoutError();
      if (controller.signal.aborted || error instanceof Error && error.name === "AbortError") throw new AbortError();
      throw error;
    } finally {
      clearTimeout(timeout);
      if (request.abortSignal?.onabort === abort) request.abortSignal.onabort = null;
    }
  }
}
