import React, { useState } from "react";
import { createRoot } from "react-dom/client";
import { FormDesigner } from "../../../extensions/UserTasks/studio/Elsa.Studio.UserTasks/Client/src/FormDesigner";
import type { TaskField } from "../../../extensions/UserTasks/studio/Elsa.Studio.UserTasks/Client/src/taskApi";
import "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/styles.css";
import "../../../extensions/UserTasks/studio/Elsa.Studio.UserTasks/Client/src/styles.css";
import "./formDesignerFixture.css";

function Fixture() {
  const [fields, setFields] = useState<TaskField[]>([]);
  return <main className="browser-form-designer" data-field-order={fields.map(field => field.key).join(",")}>
    <h1>Reusable task form designer</h1>
    <FormDesigner fields={fields} onFieldsChange={setFields} />
  </main>;
}

createRoot(document.getElementById("root")!).render(<Fixture />);