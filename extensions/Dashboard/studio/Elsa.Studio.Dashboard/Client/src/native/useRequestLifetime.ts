import { useEffect, useMemo, useRef } from "react";

/** Reject callbacks from a closed surface or a superseded request, including transports ignoring abort. */
export function useRequestLifetime() {
  const state = useRef({ active: true, version: 0, requests: new Map<string, AbortController>() });
  useEffect(() => {
    const current = state.current;
    current.active = true;
    return () => {
      current.active = false;
      current.version++;
      current.requests.forEach(request => request.abort());
      current.requests.clear();
    };
  }, []);
  return useMemo(() => ({
    capture() {
      const version = state.current.version;
      return () => state.current.active && state.current.version === version;
    },
    begin(channel: string) {
      state.current.requests.get(channel)?.abort();
      const controller = new AbortController();
      state.current.requests.set(channel, controller);
      if (!state.current.active) controller.abort();
      return {
        signal: controller.signal,
        current: () => state.current.active && !controller.signal.aborted && state.current.requests.get(channel) === controller,
        finish: () => { if (state.current.requests.get(channel) === controller) state.current.requests.delete(channel); }
      };
    },
    cancel(channel: string) {
      state.current.requests.get(channel)?.abort();
      state.current.requests.delete(channel);
    },
    invalidate() {
      state.current.version++;
      state.current.requests.forEach(request => request.abort());
      state.current.requests.clear();
    }
  }), []);
}
