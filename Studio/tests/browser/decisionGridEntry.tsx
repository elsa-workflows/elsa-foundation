import React from "react";
import { createRoot } from "react-dom/client";
import { getTheme, themeTokenNames } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/themes/presets";
import "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/ui/tokens.css";
import "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/ui/components.css";
import "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/ui/disclosure/disclosure.css";
import "./fixture.css";
import { DecisionGridFixture, DecisionPageFixture } from "./decisionGridFixture";

const query = new URLSearchParams(window.location.search);
const dark = query.get("theme") === "dark";
document.documentElement.dataset.theme = "meridian";
document.documentElement.dataset.themeLayout = "classic";
document.documentElement.dataset.themeMode = dark ? "dark" : "light";
document.body.style.color = "var(--studio-text)";
const palette = getTheme("meridian")![dark ? "dark" : "light"];
for (const token of themeTokenNames) {
  document.documentElement.style.setProperty(`--${token.replace(/([A-Z])/g, "-$1").toLowerCase()}`, palette[token]);
}

const root = createRoot(document.getElementById("root")!);
root.render(["full", "collection"].includes(query.get("page") ?? "") ? <DecisionPageFixture /> : <DecisionGridFixture />);