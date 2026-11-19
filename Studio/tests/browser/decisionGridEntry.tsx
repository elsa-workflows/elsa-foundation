import React from "react";
import { createRoot } from "react-dom/client";
import { getTheme, themeTokenNames } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/themes/presets";
import "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/ui/tokens.css";
import "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/ui/components.css";
import "./fixture.css";
import { DecisionGridFixture, DecisionPageFixture } from "./decisionGridFixture";

const query = new URLSearchParams(window.location.search);
const dark = query.get("theme") === "dark";
document.documentElement.dataset.theme = "meridian";
document.documentElement.dataset.themeLayout = "classic";
document.documentElement.dataset.themeMode = dark ? "dark" : "light";
const palette = getTheme("meridian")![dark ? "dark" : "light"];
for (const token of themeTokenNames) {
  document.documentElement.style.setProperty(`--${token.replace(/([A-Z])/g, "-$1").toLowerCase()}`, palette[token]);
}

const root = createRoot(document.getElementById("root")!);
root.render(query.get("page") === "full" ? <DecisionPageFixture /> : <DecisionGridFixture />);