import type { AuthSession, StudioEndpointContext } from "@elsa-workflows/studio-sdk";

export const dashboardActorScope = (identity: Pick<AuthSession, "subject" | "tenantId">) => JSON.stringify([identity.tenantId ?? "default", identity.subject ?? "anonymous"]);

type Work = { signal?: AbortSignal; start(): void; cancel(): void };
class NativeRequestScheduler {
  private active = 0;
  private readonly queue: Work[] = [];
  constructor(private readonly idle: () => void = () => undefined) {}

  run<T>(signal: AbortSignal | undefined, request: () => Promise<T>): Promise<T> {
    if (signal?.aborted) return Promise.reject(new DOMException("Request cancelled.", "AbortError"));
    if (this.queue.length >= 128) return Promise.reject(new Error("Too many dashboard requests are waiting. Cancel a refresh and retry."));
    return new Promise<T>((resolve, reject) => {
      const abort = () => { const index = this.queue.indexOf(work); if (index >= 0) { this.queue.splice(index, 1); reject(new DOMException("Request cancelled.", "AbortError")); this.pump(); } };
      const work: Work = {
        signal,
        cancel: () => { signal?.removeEventListener("abort", abort); reject(new DOMException("Request cancelled.", "AbortError")); },
        start: () => {
          signal?.removeEventListener("abort", abort);
          this.active++;
          // Keep the slot until the actual transport settles, including a transport ignoring abort.
          Promise.resolve().then(() => { signal?.throwIfAborted(); return request(); }).then(resolve, reject).finally(() => { this.active--; this.pump(); });
        }
      };
      signal?.addEventListener("abort", abort, { once: true }); this.queue.push(work); this.pump();
    });
  }

  private pump() {
    while (this.active < 2 && this.queue.length) {
      const work = this.queue.shift()!;
      if (work.signal?.aborted) work.cancel(); else work.start();
    }
    if (this.active === 0 && this.queue.length === 0) this.idle();
  }
}

// Only pending work is shared. No results or actor catalog survive an idle queue.
const activeScopes = new WeakMap<StudioEndpointContext, Map<string, NativeRequestScheduler>>();
export function scheduleNativeRequest<T>(context: StudioEndpointContext, actorScope: string | undefined, signal: AbortSignal | undefined, request: () => Promise<T>): Promise<T> {
  if (actorScope === undefined) return request();
  let scopes = activeScopes.get(context);
  if (!scopes) { scopes = new Map(); activeScopes.set(context, scopes); }
  let scheduler = scopes.get(actorScope);
  if (!scheduler) { scheduler = new NativeRequestScheduler(() => scopes!.delete(actorScope)); scopes.set(actorScope, scheduler); }
  return scheduler.run(signal, request);
}
