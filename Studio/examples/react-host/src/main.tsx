import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import App from "./App";

// Public styles first; the consuming host can then override semantic tokens.
import "@elsa-workflows/studio-ui/styles.css";
import "@elsa-workflows/studio-code-editor/style.css";
import "./theme.css";
import "@xyflow/react/dist/style.css";
import "@elsa-workflows/studio-workflows/style.css";
import "./app.css";

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <App />
  </StrictMode>
);
