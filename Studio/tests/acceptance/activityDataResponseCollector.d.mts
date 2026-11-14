type ContextResponse = {
  request(): { url(): string; method(): string; postDataJSON(): unknown; failure(): { errorText: string } | null };
  finished(): Promise<unknown>;
  body(): Promise<{ toString(): string }>;
};

export function createActivityDataResponseCollector<TRequest, TResponse>(
  contextUrl: string,
  contexts: { request: TRequest; response: TResponse }[],
  transportControls: { kind: "cancelled" | "cancelled-context-response"; request: unknown; failure: string }[]
): { observe(response: ContextResponse): void; settle(): Promise<void> };
