/// <reference types="vite/client" />

declare module "*.css?url&no-inline" {
  const url: string;
  export default url;
}
