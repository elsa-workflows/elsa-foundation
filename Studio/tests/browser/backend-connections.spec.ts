import { expect, test } from "@playwright/test";

test.beforeEach(async ({page})=>{
  await page.route("**/authoring/capabilities",route=>route.fulfill({json:{capabilities:[{id:"elsa.api.workflow-design",contractVersion:"1",links:[]}]}}));
  await page.route("**/runtime/capabilities",route=>route.fulfill({json:{capabilities:[{id:"elsa.api.runtime",contractVersion:"1",links:[]}]}}));
  await page.goto("/backendConnections.html");
  await expect(page.getByRole("heading",{name:"Backends",exact:true})).toBeVisible();
});

test("keeps list, details and edit separate; persists personal metadata on reload",async({page})=>{
  await page.getByRole("button",{name:"Add backend",exact:true}).click();
  await expect(page.getByRole("heading",{name:"Add backend"})).toBeVisible();
  let breadcrumbs=page.getByRole("navigation",{name:"Breadcrumb",exact:true});
  await expect(breadcrumbs.getByRole("link",{name:"Backends",exact:true})).toHaveAttribute("href",/^\/settings\/backends(?:\?|$)/);
  await expect(breadcrumbs.locator('[aria-current="page"]')).toHaveText("Add backend");
  await expect(page.getByRole("table")).toHaveCount(0);
  await page.getByLabel("Name",{exact:true}).fill("My personal backend");
  await page.getByLabel("Backend API URL",{exact:true}).fill("https://personal.example/shell/");
  await page.getByRole("button",{name:"Create backend"}).click();
  await expect(page.getByRole("heading",{name:"My personal backend"})).toBeVisible();
  breadcrumbs=page.getByRole("navigation",{name:"Breadcrumb",exact:true});
  await expect(breadcrumbs.getByRole("link",{name:"Backends",exact:true})).toBeVisible();
  await expect(breadcrumbs.locator('[aria-current="page"]')).toHaveText("My personal backend");
  await expect(page.getByRole("tab",{name:"Connection",exact:true})).toHaveAttribute("aria-selected","true");
  await page.getByRole("button",{name:"Edit",exact:true}).click();
  breadcrumbs=page.getByRole("navigation",{name:"Breadcrumb",exact:true});
  await expect(breadcrumbs.getByRole("link",{name:"Backends",exact:true})).toBeVisible();
  await expect(breadcrumbs.getByRole("link",{name:"My personal backend",exact:true})).toBeVisible();
  await expect(breadcrumbs.locator('[aria-current="page"]')).toHaveText("Edit");
  await page.getByLabel("Name",{exact:true}).fill("Unsaved backend name");
  const backendsLink=breadcrumbs.getByRole("link",{name:"Backends",exact:true});
  page.once("dialog",dialog=>dialog.dismiss());
  await backendsLink.click();
  await expect(page.getByRole("heading",{name:"Edit backend",exact:true})).toBeVisible();
  page.once("dialog",dialog=>dialog.accept());
  await backendsLink.click();
  await expect(page.getByRole("heading",{name:"Backends",exact:true})).toBeVisible();
  await page.getByRole("button",{name:/^My personal backend/}).click();
  await page.getByRole("button",{name:"Edit",exact:true}).click();
  await page.getByLabel("Name",{exact:true}).fill("Renamed backend");
  await page.getByRole("button",{name:"Save changes"}).click();
  await expect(page.getByRole("heading",{name:"Renamed backend"})).toBeVisible();
  await page.reload();
  await expect(page.getByRole("button",{name:"Renamed backend"})).toBeVisible();
  await page.getByRole("button",{name:"Renamed backend"}).click();
  page.once("dialog",dialog=>dialog.accept());
  await page.getByRole("button",{name:"Remove backend"}).click();
  await expect(page.getByRole("heading",{name:"Backends",exact:true})).toBeVisible();
  await expect(page.getByRole("button",{name:"Renamed backend"})).toHaveCount(0);
});

test("missing backend profiles keep a breadcrumb back to the collection",async({page})=>{
  await page.goto("/backendConnections.html?path=%2Fsettings%2Fbackends%2Fmissing-profile");
  const breadcrumbs=page.getByRole("navigation",{name:"Breadcrumb",exact:true});
  await expect(breadcrumbs.getByRole("link",{name:"Backends",exact:true})).toHaveAttribute("href",/^\/settings\/backends(?:\?|$)/);
  await expect(breadcrumbs.locator('[aria-current="page"]')).toHaveText("Unavailable profile");
  await expect(page.getByText("This backend profile is no longer available.", {exact:true})).toBeVisible();
});

test("configuration ownership, capability tabs and keyboard switching",async({page})=>{
  await page.getByRole("button",{name:/^Authoring ·/}).last().click();
  await expect(page.getByRole("button",{name:"Edit",exact:true})).toHaveCount(0);
  const tab=page.getByRole("tab",{name:"Connection",exact:true});
  await tab.focus();await page.keyboard.press("ArrowRight");
  await expect(page.getByRole("tab",{name:"Capabilities",exact:true})).toHaveAttribute("aria-selected","true");
  await expect(page.getByText("elsa.api.workflow-design",{exact:true})).toBeVisible();
  await expect(page.getByText("Backend API URL",{exact:true})).toBeHidden();
  await page.getByRole("button",{name:/Select backend/}).click();
  await page.getByRole("button",{name:/Runtime.*Connect/}).click();
  await expect(page.locator("html")).toHaveAttribute("data-committed-backend-url","/?backend=runtime");
});

for (const mode of ["light","dark"] as const) for (const width of [320,390,768,1440]) test(`long profile names fit at ${width}px in ${mode}`,async({page})=>{
  await page.emulateMedia({colorScheme:mode});
  await page.setViewportSize({width,height:900});
  await expect.poll(()=>page.evaluate(()=>document.documentElement.scrollWidth<=window.innerWidth)).toBe(true);
  await page.getByRole("button",{name:/^Authoring ·/}).last().click();
  await expect.poll(()=>page.evaluate(()=>document.documentElement.scrollWidth<=window.innerWidth)).toBe(true);
  await page.getByRole("tab",{name:"Capabilities",exact:true}).click();
  await expect.poll(()=>page.evaluate(()=>document.documentElement.scrollWidth<=window.innerWidth)).toBe(true);
});

test("failed active connection test removes previously advertised capabilities",async({page})=>{
  await page.getByRole("button",{name:/^Authoring ·/}).last().click();
  await page.getByRole("tab",{name:"Capabilities",exact:true}).click();
  await expect(page.getByText("elsa.api.workflow-design",{exact:true})).toBeVisible();
  await page.route("**/authoring/capabilities",route=>route.fulfill({status:403,json:{message:"Access denied"}}));
  await page.getByRole("button",{name:"Test connection",exact:true}).click();
  await expect(page.getByText("elsa.api.workflow-design",{exact:true})).toHaveCount(0);
  await page.getByRole("tab",{name:"Connection",exact:true}).click();
  await expect(page.getByText("Unavailable",{exact:true})).toBeVisible();
});
