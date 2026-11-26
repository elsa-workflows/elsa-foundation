import { createRoot } from "react-dom/client";
import { applyThemeStyleVariables } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/components/ThemeProvider";
import { getTheme, themeTokenNames } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/themes/presets";
import { DisclosureFixturePage } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/ui/disclosure/DisclosureFixturePage";
import "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/styles.css";

const searchParams = new URLSearchParams(window.location.search);
const mode = searchParams.get("mode") === "dark" ? "dark" : "light";
const theme = getTheme("classic")!;
const palette = mode === "dark" ? theme.dark : theme.light;

document.documentElement.dataset.theme = "classic";
document.documentElement.dataset.themeMode = mode;
document.documentElement.dataset.themeAppearance = mode;
document.documentElement.dataset.themeLayout = "classic";
applyThemeStyleVariables(document.documentElement, theme);
for (const token of themeTokenNames) {
  document.documentElement.style.setProperty(`--${token.replace(/([A-Z])/g, "-$1").toLowerCase()}`, palette[token]);
}

createRoot(document.getElementById("root")!).render(<DisclosureFixturePage title="Disclosure kit" />);
