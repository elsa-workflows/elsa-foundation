import { CopyableIdentifier } from "@elsa-workflows/studio-ui";
import { createRoot } from "react-dom/client";
import type { ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import { WeatherPage, register } from "../../../Extensions/Samples.WeatherForecast/studio/Elsa.Studio.Samples.WeatherForecast/Client/src/module";
// Deliberately no host stylesheet: exercise the public primitive rules with a consumer palette.
import "../../src/essentials/Elsa.Studio.Hosting/StudioUi/src/styles.css";
import "../../examples/react-host/src/theme.css";

register({
  navigation: { add() {} },
  routes: { add() {} },
  host: { http: { getJson: async (path: string) => {
    const response = await fetch(path);
    if (!response.ok) throw new Error("Forecast request failed.");
    return response.json();
  } } }
} as unknown as ElsaStudioModuleApi);

createRoot(document.getElementById("root")!).render(<main style={{ padding: 12, minWidth: 0 }}><WeatherPage /><section aria-label="Public identifier" style={{ maxWidth: 300 }}><CopyableIdentifier label="Example ID" value="module-example-0123456789abcdef0123456789abcdef" /></section></main>);
