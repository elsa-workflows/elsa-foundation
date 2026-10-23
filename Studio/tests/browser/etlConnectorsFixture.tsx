import { useState } from "react";
import { createRoot } from "react-dom/client";
import { createEndpointContext } from "@elsa-workflows/studio-sdk";
import { DialogHost } from "../../src/apps/Elsa.Studio.Web/Client/src/app/ui/dialog/DialogHost";
import { dialogs } from "../../src/apps/Elsa.Studio.Web/Client/src/app/dialogs";
import { ThemeProvider } from "../../src/apps/Elsa.Studio.Web/Client/src/app/components/ThemeProvider";
import { EtlDefinitionsPage } from "../../../Extensions/Etl/studio/Elsa.Studio.Etl/Client/src/EtlDefinitionsPage";
import { EtlPublishedSelectionEditor } from "../../../Extensions/Etl/studio/Elsa.Studio.Etl/Client/src/EtlPublishedSelectionEditor";
import "../../src/apps/Elsa.Studio.Web/Client/src/app/styles.css";
import "../../../Extensions/Etl/studio/Elsa.Studio.Etl/Client/src/styles.css";

const endpoint = createEndpointContext(`${window.location.origin}/fixture-etl`);

function Fixture() {
  const [selection, setSelection] = useState<unknown>("");
  return <ThemeProvider><main>
    <EtlDefinitionsPage context={endpoint} dialogs={dialogs} profile="connector" />
    <section className="etl-shell" aria-labelledby="native-pipeline-selection">
      <h2 id="native-pipeline-selection">Workflow pipeline selection</h2>
      <EtlPublishedSelectionEditor endpointContext={endpoint}
        descriptor={{ name: "Pipeline", typeName: "System.String", uiHint: "etl-published-selection" }}
        context={{ activity: {}, expressionDescriptors: [] }} value={selection} onChange={setSelection} />
      <output aria-label="Saved pipeline selection">{typeof selection === "string" ? selection : ""}</output>
    </section>
  </main><DialogHost /></ThemeProvider>;
}
createRoot(document.getElementById("root")!).render(<Fixture />);
