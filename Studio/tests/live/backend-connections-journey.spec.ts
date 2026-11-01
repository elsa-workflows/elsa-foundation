import { expect, test, type Page } from "@playwright/test";
import { readFileSync, writeFileSync } from "node:fs";
import { studioPassword, studioUser } from "./support/journey";

// Two actual source-built Workbenches, with independent stores/identity and full shell URLs.
// The second backend deliberately omits authoring APIs. No page.route API mocks are used.
test.describe("backend connections against composed hosts",()=>{
  test.skip(process.env.ELSA_BACKEND_CONNECTIONS_LIVE !== "1","Requires the owned two-backend qualification stack.");
  test.describe.configure({mode:"serial"});
  const primary="authoring", secondary="runtime";

  async function login(page:Page) {
    await page.goto("/");
    await page.getByLabel("Username",{exact:true}).fill(studioUser);
    await page.getByLabel("Password",{exact:true}).fill(studioPassword);
    await page.getByRole("button",{name:"Sign in",exact:true}).click();
    await page.waitForLoadState("networkidle");
    await expect(page.getByRole("heading",{name:"Home",exact:true,level:2})).toBeVisible();
  }

  test("personal CRUD persists through reload and failed connection retains the active backend",async({page})=>{
    await login(page);
    await page.goto(`/settings/backends?backend=${primary}`);
    await page.getByRole("button",{name:"Add backend",exact:true}).click();
    await page.getByLabel("Name",{exact:true}).fill("Unreachable qualification backend");
    await page.getByLabel("Backend API URL",{exact:true}).fill("http://127.0.0.1:56648/full-shell/");
    await page.getByRole("button",{name:"Create backend"}).click();
    await expect(page.getByRole("heading",{name:"Unreachable qualification backend",exact:true})).toBeVisible();
    await page.reload();
    await expect(page.getByRole("heading",{name:"Unreachable qualification backend",exact:true})).toBeVisible();
    await page.getByRole("button",{name:"Edit",exact:true}).click();
    await page.getByLabel("Name",{exact:true}).fill("Renamed qualification backend");
    await page.getByRole("button",{name:"Save changes"}).click();
    await page.getByRole("button",{name:"Connect",exact:true}).click();
    await expect(page.getByRole("button",{name:/Select backend\. Current: Authoring/})).toBeVisible();
    await expect(page).toHaveURL(/backend=authoring/);
    await expect(page.getByRole("status").filter({hasText:/fetch|reach|network|failed/i})).toBeVisible();
    page.once("dialog",dialog=>dialog.accept());
    await page.getByRole("button",{name:"Remove backend",exact:true}).click();
    await expect(page.getByRole("heading",{name:"Backends",exact:true,level:2})).toBeVisible();
    await expect(page.getByRole("button",{name:"Renamed qualification backend",exact:true})).toHaveCount(0);
  });

  test("feature settings Apply stays on its reviewed host, then an authenticated switch adapts Studio",async({page})=>{
    const primaryFile=process.env.ELSA_BACKEND_PRIMARY_SHELLS!;
    const secondaryFile=process.env.ELSA_BACKEND_SECONDARY_SHELLS!;
    expect(Boolean(primaryFile && secondaryFile)).toBe(true);
    const secondaryBefore=readFileSync(secondaryFile,"utf8");
    const settings=JSON.parse(readFileSync(primaryFile,"utf8")).CShells.Shells.default.Features.DiagnosticsStructuredLogs;
    const capacity=settings.BufferCapacity === 2028 ? 2029 : 2028;
    const primaryBearers=new Set<string>(),secondaryBearers=new Set<string>();
    let capabilityReads=0;
    page.on("request",request=>{
      const url=request.url(), bearer=request.headers().authorization;
      if (url.includes(":56641/default/capabilities")) capabilityReads++;
      if (!bearer || url.includes("/_elsa/identity/")) return;
      if (url.includes(":56641/default/")) primaryBearers.add(bearer);
      if (url.includes(":56642/default/")) secondaryBearers.add(bearer);
    });
    await login(page);
    await page.goto(`/features?target=server&backend=${primary}`);
    await page.locator(".feature-management-feature-button").filter({has:page.locator("code").filter({hasText:/^DiagnosticsStructuredLogs$/})}).click();
    await page.getByRole("button",{name:"Edit settings",exact:true}).click();
    await page.getByRole("dialog").getByLabel("Buffer capacity",{exact:false}).fill(String(capacity));
    await page.getByRole("button",{name:"Save draft",exact:true}).click();
    await page.getByRole("button",{name:/Select backend/}).click();
    await page.locator(".backend-switcher-popover").getByRole("button",{name:/Runtime.*Connect/}).click();
    await expect(page).toHaveURL(/backend=authoring/);
    await expect(page.getByText(/Finish or discard the current draft first/)).toBeVisible();
    await page.getByRole("button",{name:"Review changes",exact:true}).click();
    const review=page.getByRole("dialog",{name:"Review changes"});
    await expect(review).toContainText("Buffer capacity");
    const readsBefore=capabilityReads;
    const applied=page.waitForResponse(response=>response.url().includes(":56641/default/modularity/features/apply") && response.request().method()==="POST");
    await review.getByRole("button",{name:"Apply",exact:true}).click();
    expect((await applied).status()).toBe(200);
    await expect.poll(()=>JSON.parse(readFileSync(primaryFile,"utf8")).CShells.Shells.default.Features.DiagnosticsStructuredLogs.BufferCapacity).toBe(capacity);
    expect(readFileSync(secondaryFile,"utf8")===secondaryBefore).toBe(true);
    await expect.poll(()=>capabilityReads).toBeGreaterThan(readsBefore);
    await expect(page.getByText(/Applied \d+ descriptor/)).toBeVisible();
    await page.getByRole("button",{name:/Select backend/}).click();
    await page.locator(".backend-switcher-popover").getByRole("button",{name:/Runtime.*Connect/}).click();
    await page.getByRole("button",{name:"Sign in to Runtime",exact:true}).click();
    await page.getByLabel("Username",{exact:true}).fill(studioUser);
    await page.getByLabel("Password",{exact:true}).fill(studioPassword);
    await page.getByRole("button",{name:"Sign in",exact:true}).click();
    await page.waitForLoadState("networkidle");
    await expect(page).toHaveURL(new RegExp(`^${process.env.ELSA_STUDIO_URL}/\\?backend=${secondary}$`));
    await expect(page.getByRole("heading",{name:"Home",exact:true,level:2})).toBeVisible();
    await expect(page.getByRole("link",{name:"Workflows",exact:true})).toHaveCount(0);
    await expect(page.getByRole("link",{name:"Runs",exact:true})).toBeVisible();
    await expect(page.getByRole("link",{name:"Runs",exact:true})).toHaveAttribute("href",/backend=runtime/);
    expect(secondaryBearers.size>0).toBe(true);
    expect([...secondaryBearers].every(bearer=>!primaryBearers.has(bearer))).toBe(true);
    await page.goBack();
    await expect(page.getByRole("button",{name:/Select backend\. Current: Authoring/})).toBeVisible();
    await page.goForward();
    await expect(page.getByRole("button",{name:/Select backend\. Current: Runtime/})).toBeVisible();
    await page.goto(`/workflows/executables?backend=${secondary}`);
    await expect(page.getByRole("heading",{name:"No workflow executables",exact:true})).toBeVisible();
    await expect(page.getByText("This backend has no executables in the selected scope.",{exact:true})).toBeVisible();
    await expect(page.getByRole("button",{name:"Open workflow definitions",exact:true})).toHaveCount(0);
    await page.goto(`/workflows/definitions?definition=shared-resource-id&backend=${secondary}`);
    await expect(page.getByRole("status").filter({hasText:"This backend does not support elsa.api.workflow-design."})).toBeVisible();
    await page.reload();
    await expect(page.getByRole("button",{name:/Select backend\. Current: Runtime/})).toBeVisible();
    await expect(page.getByRole("status").filter({hasText:"This backend does not support elsa.api.workflow-design."})).toBeVisible();
  });
  test("Studio Apply disables and re-enables backend management across a safe Home document",async({page})=>{
    const studioFile=process.env.ELSA_BACKEND_STUDIO_SHELLS;
    test.skip(!studioFile,"Requires the owned Studio shell configuration.");
    const original=readFileSync(studioFile!,"utf8");
    const imported:string[]=[];
    page.on("request",request=>{if(request.url().includes("/_content/Elsa.Studio.Backends/"))imported.push(request.url());});
    try {
      await login(page);
      await page.goto(`/settings/backends?backend=${primary}`);
      await page.getByRole("button",{name:"Add backend",exact:true}).click();
      await page.getByLabel("Name",{exact:true}).fill("Retained optional profile");
      await page.getByLabel("Backend API URL",{exact:true}).fill("http://127.0.0.1:56648/retained/");
      await page.getByRole("button",{name:"Create backend",exact:true}).click();
      await expect(page.getByRole("heading",{name:"Retained optional profile",exact:true})).toBeVisible();
      await page.getByRole("button",{name:/Select backend/}).click();
      await page.locator(".backend-switcher-popover").getByRole("button",{name:/Runtime.*Connect/}).click();
      await page.getByRole("button",{name:"Sign in to Runtime",exact:true}).click();
      await page.getByLabel("Username",{exact:true}).fill(studioUser);
      await page.getByLabel("Password",{exact:true}).fill(studioPassword);
      await page.getByRole("button",{name:"Sign in",exact:true}).click();
      await expect(page.getByRole("button",{name:/Select backend\. Current: Runtime/})).toBeVisible();
      await page.goto(`/features?target=studio&backend=${secondary}`);
      await page.getByRole("button",{name:"Disable Backend connections",exact:true}).click();
      await page.getByRole("button",{name:"Review changes",exact:true}).click();
      const review=page.getByRole("dialog",{name:"Review changes"});
      await expect(review).toContainText("Backend connections");
      imported.length=0;
      await review.getByRole("button",{name:"Apply",exact:true}).click();
      await expect(page).toHaveURL(process.env.ELSA_STUDIO_URL+"/");
      await expect(page.getByRole("heading",{name:"Home",exact:true,level:2})).toBeVisible();
      await expect(page.getByRole("button",{name:/Select backend/})).toHaveCount(0);
      await expect(page.getByRole("link",{name:"Backends",exact:true})).toHaveCount(0);
      await expect(page.getByRole("link",{name:"Workflows",exact:true})).toBeVisible();
      expect(imported).toEqual([]);
      expect((await page.request.get("/_elsa/studio/backends/profiles")).status()).toBe(404);
      const resources:string[]=[];
      const track=(request:import("@playwright/test").Request)=>{if(/:5664[12]\/default\/.*(workflows|capabilities)/.test(request.url()))resources.push(request.url());};
      page.on("request",track);
      await page.goto(`/workflows/definitions?definition=foreign-resource&backend=${secondary}`);
      await expect(page.getByRole("status").filter({hasText:"not available in this Studio"})).toBeVisible();
      expect(resources).toEqual([]);
      page.off("request",track);
      await page.getByRole("button",{name:"Return Home",exact:true}).click();
      await expect(page.getByRole("heading",{name:"Home",exact:true,level:2})).toBeVisible();
      await page.goto("/features?target=studio");
      await page.getByRole("button",{name:"Enable Backend connections",exact:true}).click();
      await page.getByRole("button",{name:"Review changes",exact:true}).click();
      await page.getByRole("dialog",{name:"Review changes"}).getByRole("button",{name:"Apply",exact:true}).click();
      await expect(page.getByRole("button",{name:/Select backend\. Current: Runtime/})).toBeVisible();
      await expect(page.getByRole("heading",{name:"Home",exact:true,level:2})).toBeVisible();
      await page.goto(`/settings/backends?backend=${secondary}`);
      await expect(page.getByRole("button",{name:"Retained optional profile",exact:true})).toBeVisible();
    } finally {
      const current=JSON.parse(readFileSync(studioFile!,"utf8"));
      const features=current.CShells.Shells.Default.Features;
      if(!features.BackendsStudio || features.BackendsStudio===false)writeFileSync(studioFile!,original);
    }
  });

});
