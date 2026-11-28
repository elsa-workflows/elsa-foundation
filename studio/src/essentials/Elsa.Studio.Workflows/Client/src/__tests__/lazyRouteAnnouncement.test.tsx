import { describe, expect, it, vi } from "vitest";
import { definition, renderRegisteredRoute, response } from "./routeRenderingHelpers";

// Kept in its own file so it always renders the definitions route first against a fresh module
// graph (vitest isolates each test file's modules by default; see vite.module.base.ts, which sets
// no `isolate`/`pool` override away from that default). `WorkflowManagementPage` (module.tsx) is a
// module-scope `React.lazy`; once any earlier test resolves that lazy payload, the "Loading workflow
// definitions" fallback this test waits for never renders again. See #504.
describe("workflows module", () => {
  it("announces while the workflow definitions route loads on demand", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => response({ items: [definition()] })));
    const { container, unmount } = await renderRegisteredRoute();

    expect(container.querySelector("[role='status']")?.textContent).toContain("Loading workflow definitions");
    await vi.waitFor(() => expect(container.textContent).toContain("Hello World"), { timeout: 10_000 });

    await unmount();
  });
});
