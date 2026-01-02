import { defineConfig } from "astro/config";

export default defineConfig({
  output: "static",
  vite: {
    server: {
      host: true,
      allowedHosts: true
    },
    preview: {
      host: true,
      allowedHosts: true
    }
  }
});
