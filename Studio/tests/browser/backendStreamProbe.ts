import { randomUUID } from "node:crypto";
import type { IncomingMessage, ServerResponse } from "node:http";

// A real HTTP cookie/stream probe, not a workflow backend. It exercises browser credential modes.
export function backendStreamProbe(request: IncomingMessage, response: ServerResponse, next: () => void) {
  const url = new URL(request.url ?? "/", "http://fixture");
  if (!url.pathname.startsWith("/_backend-transport-probe/")) return next();
  const cookieSeen = request.headers.cookie?.includes("backend-transport-probe=ambient") ?? false;
  if (url.pathname.endsWith("/cookie-control")) return json(response, { cookieSeen });
  if (url.pathname.includes("/hub")) return hub(request, response, url, cookieSeen);
  if (url.pathname.endsWith("/stream")) {
    response.writeHead(200, { "Content-Type": "text/event-stream", "Cache-Control": "no-cache" });
    const message = cookieSeen ? "Cookie leaked" : "Cookie-free";
    if (!url.pathname.includes("/agent/")) {
      const resume = request.headers["last-event-id"];
      response.write(`id: ${resume ? "probe:2" : "probe:1"}\nevent: entry\ndata: ${JSON.stringify({sequence:resume ? 2 : 1,timestamp:new Date().toISOString(),level:"Information",category:"Probe",message:resume === "probe:1" ? `${message} Replay` : `${message} Logs`})}\n\n`);
      if (!resume) {response.end();return;}
    } else {
      response.write(`data: ${JSON.stringify({type:"message-delta",messageId:"probe",content:`${message} Agent`})}\n\n`);
    }
    const heartbeat = setInterval(() => response.write(": heartbeat\n\n"), 15000);
    response.on("close", () => clearInterval(heartbeat));
    return;
  }
  return json(response, []);
}

interface Client { first: boolean; cookieSeen: boolean; queue: string[]; poll?: ServerResponse }
const clients = new Map<string, Client>();
function json(response: ServerResponse, value: unknown) {response.setHeader("Content-Type", "application/json");response.end(JSON.stringify(value));}
function deliver(client: Client) {
  if (!client.poll || !client.queue.length) return;
  const response = client.poll;client.poll = undefined;
  response.end(client.queue.splice(0).join(""));
}
function hub(request: IncomingMessage, response: ServerResponse, url: URL, cookieSeen: boolean) {
  if (url.pathname.endsWith("/negotiate")) {
    const id = randomUUID();clients.set(id, {first:true,cookieSeen,queue:[]});
    return json(response, {negotiateVersion:1,connectionId:id,connectionToken:id,availableTransports:[{transport:"LongPolling",transferFormats:["Text"]}]});
  }
  const id = url.searchParams.get("id") ?? "", client = clients.get(id);
  if (!client) {response.writeHead(404);response.end();return;}
  client.cookieSeen ||= cookieSeen;
  if (request.method === "DELETE") {client.poll?.end();clients.delete(id);response.end();return;}
  if (request.method === "GET") {
    response.setHeader("Content-Type", "application/octet-stream");
    if (client.first) {client.first = false;response.end();return;}
    client.poll = response;response.on("close", () => {
      if (client.poll !== response) return;
      client.poll = undefined;
      if (!response.writableEnded) clients.delete(id);
    });
    deliver(client);return;
  }
  let body = "";
  request.on("data", chunk => {body += String(chunk);});
  request.on("end", () => {
    for (const frame of body.split("\u001e").filter(Boolean)) {
      const message = JSON.parse(frame);
      if (message.protocol) client.queue.push("{}\u001e");
      if (message.type === 4) client.queue.push(JSON.stringify({type:2,invocationId:message.invocationId,item:{line:{sequence:1,timestamp:new Date().toISOString(),stream:"stdout",text:`${client.cookieSeen ? "Cookie leaked" : "Cookie-free"} Console`}}})+"\u001e");
    }
    response.end();deliver(client);
  });
}
