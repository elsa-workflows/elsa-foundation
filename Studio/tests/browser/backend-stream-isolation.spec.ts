import { expect, test } from "@playwright/test";

test("anonymous backend HTTP, SSE and SignalR omit an existing same-origin cookie", async ({page,context,baseURL}) => {
  await context.addCookies([{name:"backend-transport-probe",value:"ambient",url:baseURL!}]);
  await page.goto("/backendStreams.html");
  await page.getByRole("group",{name:"Console host"}).getByRole("button",{name:"Server",exact:true}).click();
  await expect(page.getByText("Cookie-free Agent",{exact:true})).toBeVisible();
  await expect(page.getByText("Cookie-free Logs",{exact:true})).toBeVisible();
  await expect(page.getByText("Cookie-free Console",{exact:true})).toBeVisible();
  await expect(page.getByText("Cookie-free Replay",{exact:true})).toBeVisible();
  // Prove the cookie is present and would be sent by an ordinary same-origin request.
  expect(await page.evaluate(async () => (await fetch("/_backend-transport-probe/cookie-control")).json())).toEqual({cookieSeen:true});
  await expect(page.getByText(/Cookie leaked/)).toHaveCount(0);
});
