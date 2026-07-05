import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import App from "./App";

/*
 * STYLE IMPORT ORDER (matters — later files reference tokens defined earlier):
 *
 *  1. theme.css              — LAYER-1 primitive tokens (--background, --primary, ...).
 *                              The consuming host owns these; see the file header.
 *  2. studio-ui tokens.css   — the semantic --studio-* token layer, aliased onto layer 1.
 *  3. @xyflow/react style     — the workflow canvas (React Flow) base styles.
 *  4. studio-workflows CSS    — the workflows module's own component styles.
 *  5. studio-components.css   — component visual rules for the primitives + code editor
 *                              (host-owned in Elsa.Studio.Web; supplied here — see file header).
 *  6. app.css                 — this example's own shell layout.
 *
 * IN-REPO vs PUBLISHED RESOLUTION — READ THIS:
 *   The workspace packages' on-disk `exports` maps only expose "." (the JS/TS entry), so the
 *   published CSS SUBPATHS do NOT resolve against the in-repo source. Externally you would write:
 *
 *       import "@elsa-workflows/studio-ui/tokens.css";
 *       import "@elsa-workflows/studio-workflows/style.css";
 *
 *   In-repo we instead reach the same source files by workspace-relative path (below). Everything
 *   else (the component/JS imports) is identical to what an external consumer writes.
 */
import "./theme.css";
// External consumers: import "@elsa-workflows/studio-ui/tokens.css";
import "../../../src/Elsa.Studio.Web/Client/src/app/ui/tokens.css";
import "@xyflow/react/dist/style.css";
// External consumers: import "@elsa-workflows/studio-workflows/style.css";
import "../../../src/Elsa.Studio.Workflows/Client/src/styles.css";
import "./studio-components.css";
import "./app.css";

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <App />
  </StrictMode>
);
