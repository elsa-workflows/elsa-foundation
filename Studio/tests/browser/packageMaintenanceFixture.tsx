import React from "react";
import { createRoot } from "react-dom/client";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { PackageFeedsPage } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/modules/PackageFeedsPage";
import { ThemeProvider } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/components/ThemeProvider";
import { createEndpointContext, type ElsaStudioModuleApi } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/sdk";
import "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/styles.css";

const context = createEndpointContext(location.origin);
const api = {
  host: { ...context, hostVersion: "1.0.0", sdkVersion: "1.0.0" }, backend: context,
  dialogs: { confirm: async (options: { message: string }) => window.confirm(options.message) }
} as ElsaStudioModuleApi;
const client = new QueryClient({ defaultOptions: { queries: { retry: false, refetchOnWindowFocus: false } } });
createRoot(document.getElementById("root")!).render(
  <ThemeProvider><QueryClientProvider client={client}><main><PackageFeedsPage api={api} /></main></QueryClientProvider></ThemeProvider>
);
