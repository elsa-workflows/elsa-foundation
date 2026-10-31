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
  await expect(page.getByRole("table")).toHaveCount(0);
  await page.getByLabel("Name",{exact:true}).fill("My personal backend");
  await page.getByLabel("Backend API URL",{exact:true}).fill("https://personal.example/shell/");
  await page.getByRole("button",{name:"Create backend"}).click();
  await expect(page.getByRole("heading",{name:"My personal backend"})).toBeVisible();
  await expect(page.getByRole("tab",{name:"Connection",exact:true})).toHaveAttribute("aria-selected","true");
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

for (const width of [320,390,768,1440]) test(`long profile names fit at ${width}px`,async({page})=>{
  await page.setViewportSize({width,height:900});
  await expect.poll(()=>page.evaluate(()=>document.documentElement.scrollWidth<=window.innerWidth)).toBe(true);
  await page.getByRole("button",{name:/^Authoring ·/}).last().click();
  await expect.poll(()=>page.evaluate(()=>document.documentElement.scrollWidth<=window.innerWidth)).toBe(true);
  await page.getByRole("tab",{name:"Capabilities",exact:true}).click();
  await expect.poll(()=>page.evaluate(()=>document.documentElement.scrollWidth<=window.innerWidth)).toBe(true);
});
