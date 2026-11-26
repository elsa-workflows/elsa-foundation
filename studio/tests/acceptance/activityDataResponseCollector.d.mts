type ContextRequest = { url(): string; method(): string; postDataJSON(): unknown; failure(): { errorText: string } | null };
type ContextResponse = {
  request(): ContextRequest;
  finished(): Promise<unknown>;
  body(): Promise<{ toString(): string }>;
};

export function createActivityDataResponseCollector<TRequest, TResponse>(
  contextUrl: string,
  contexts: { request: TRequest; response: TResponse }[],
  transportControls: { kind: "cancelled" | "cancelled-context-response"; request: unknown; failure: string }[]
): { observe(response: ContextResponse): void; requestFailed(request: ContextRequest): void; drain(): Promise<void>; settle(): Promise<void> };
