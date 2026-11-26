import { readdir, readFile } from "node:fs/promises";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const moduleDirectory = resolve(dirname(fileURLToPath(import.meta.url)), "../../extensions/Agent/studio/Elsa.Studio.Agent/wwwroot/studio/modules/agent");
const files = (await readdir(moduleDirectory)).filter(file => file.endsWith(".js"));
const chunks = await Promise.all(files.map(async file => ({ file, source: await readFile(resolve(moduleDirectory, file), "utf8") })));
const sharedImport = chunks.some(({ source }) => /from\s*["']@tanstack\/react-query["']/.test(source));
const privateRuntime = chunks.find(({ source }) => source.includes("No QueryClient set, use QueryClientProvider to set one"));

if (!sharedImport) {
  throw new Error("The built Agent module does not import the host-provided @tanstack/react-query package.");
}

if (privateRuntime) {
  throw new Error(`The built Agent module embeds a private React Query runtime in ${privateRuntime.file}.`);
}

console.log("Agent React Query bundle boundary is valid.");