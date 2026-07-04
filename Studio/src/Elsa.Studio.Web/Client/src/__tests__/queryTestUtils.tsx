import React from "react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";

// Wraps a node in a fresh QueryClient for tests that exercise `useQuery`/`useMutation` shell surfaces.
// Retries and refetch-on-focus are disabled so error paths surface immediately and tests don't fire
// background refetches; a per-render client keeps cache state isolated between tests.
export function createTestQueryClient() {
  return new QueryClient({
    defaultOptions: {
      queries: { retry: false, refetchOnWindowFocus: false, gcTime: 0 },
      mutations: { retry: false }
    }
  });
}

export function withQueryClient(node: React.ReactNode, client: QueryClient = createTestQueryClient()) {
  return <QueryClientProvider client={client}>{node}</QueryClientProvider>;
}
