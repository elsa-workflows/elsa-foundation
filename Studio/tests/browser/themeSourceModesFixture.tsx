import React from "react";
import { createRoot } from "react-dom/client";
import { createHttpClient, createNotificationController, type ElsaStudioModuleApi, type StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { ThemeProvider, useTheme } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/components/ThemeProvider";
import { ThemeSwitcher } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/components/ThemeSwitcher";
import { ThemeBuilderPage } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/modules/ThemeBuilderPage";
import { ToastHost } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/ui/feedback/ToastHost";
import "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/ui/components.css";
import { AppearancePage } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/settings/AppearancePage";

const context: StudioEndpointContext = { baseUrl: location.origin, http: createHttpClient(location.origin) };
const view = new URLSearchParams(location.search).get("view");
const notifications = createNotificationController();
const api = { host: context, notifications: notifications.api, dialogs: { confirm: async () => true } } as unknown as ElsaStudioModuleApi;

function ThemeControls() {
  const { currentTheme, availableThemes, store } = useTheme();
  return <main>
    <ToastHost controller={notifications} navigate={() => {}} />
    <output data-testid="source-mode">{store.themesMode}</output>
    <output data-testid="current-theme">{currentTheme.id}</output>
    <output data-testid="selectable-ids">{JSON.stringify(availableThemes.map(theme => theme.id))}</output>
    <ThemeSwitcher />
    {view === "appearance" ? <AppearancePage onNavigate={() => {}} /> : null}
    {view === "builder" ? <ThemeBuilderPage api={api} /> : null}
  </main>;
}

createRoot(document.getElementById("root")!).render(<ThemeProvider storeContext={context}><ThemeControls /></ThemeProvider>);
