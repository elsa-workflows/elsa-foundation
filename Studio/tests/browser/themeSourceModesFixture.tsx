import React from "react";
import { createRoot } from "react-dom/client";
import { createHttpClient, type ElsaStudioModuleApi, type StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { ThemeProvider, useTheme } from "../../src/apps/Elsa.Studio.Web/Client/src/app/components/ThemeProvider";
import { ThemeSwitcher } from "../../src/apps/Elsa.Studio.Web/Client/src/app/components/ThemeSwitcher";
import { ThemeBuilderPage } from "../../src/apps/Elsa.Studio.Web/Client/src/app/modules/ThemeBuilderPage";
import { AppearancePage } from "../../src/apps/Elsa.Studio.Web/Client/src/app/settings/AppearancePage";

const context: StudioEndpointContext = { baseUrl: location.origin, http: createHttpClient(location.origin) };
const view = new URLSearchParams(location.search).get("view");
const api = { host: context, dialogs: { confirm: async () => true } } as unknown as ElsaStudioModuleApi;

function ThemeControls() {
  const { currentTheme, availableThemes, store } = useTheme();
  return <main>
    <output data-testid="source-mode">{store.themesMode}</output>
    <output data-testid="current-theme">{currentTheme.id}</output>
    <output data-testid="selectable-ids">{JSON.stringify(availableThemes.map(theme => theme.id))}</output>
    <ThemeSwitcher />
    {view === "appearance" ? <AppearancePage onNavigate={() => {}} /> : null}
    {view === "builder" ? <ThemeBuilderPage api={api} /> : null}
  </main>;
}

createRoot(document.getElementById("root")!).render(<ThemeProvider storeContext={context}><ThemeControls /></ThemeProvider>);
