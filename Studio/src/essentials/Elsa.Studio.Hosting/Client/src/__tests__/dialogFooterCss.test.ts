// @vitest-environment node

import { describe, expect, it } from "vitest";
import { read } from "./themeTestUtils";

describe("StudioDialogFooter and StudioButton styles", () => {
  it("import CSS from the component modules so designer entries do not depend on page.css", () => {
    expect(read("../app/ui/layout/DialogFooter.tsx")).toContain('import "./DialogFooter.css"');
    expect(read("../app/ui/forms/Button.tsx")).toContain('import "./Button.css"');
    expect(read("../app/ui/layout/page.css")).toContain('@import "./DialogFooter.css"');
    expect(read("../app/ui/components.css")).toContain('@import "./forms/Button.css"');
    expect(read("../app/ui/layout/DialogFooter.css")).toContain(".studio-dialog-footer-actions");
    expect(read("../app/ui/forms/Button.css")).toContain('.studio-button[data-variant="primary"]');
  });
});
