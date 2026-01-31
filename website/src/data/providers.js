// Provider metadata for navigation and linking (Ando first, then alphabetically)
export const providers = [
  { id: "Ando", href: "/providers/ando", label: "Ando" },
  { id: "AppService", href: "/providers/appservice", label: "Azure App Service" },
  { id: "Azure", href: "/providers/azure", label: "Azure CLI" },
  { id: "Bicep", href: "/providers/bicep", label: "Azure Bicep" },
  { id: "Functions", href: "/providers/functions", label: "Azure Functions" },
  { id: "Cloudflare", href: "/providers/cloudflare", label: "Cloudflare" },
  { id: "Docker", href: "/providers/docker", label: "Docker" },
  { id: "Docfx", href: "/providers/docfx", label: "DocFX" },
  { id: "Dotnet", href: "/providers/dotnet", label: "Dotnet" },
  { id: "Ef", href: "/providers/ef", label: "Entity Framework Core" },
  { id: "Git", href: "/providers/git", label: "Git" },
  { id: "GitHub", href: "/providers/github", label: "GitHub" },
  { id: "Node", href: "/providers/node", label: "Node" },
  { id: "Npm", href: "/providers/npm", label: "Npm" },
  { id: "Nuget", href: "/providers/nuget", label: "NuGet" },
  { id: "Playwright", href: "/providers/playwright", label: "Playwright" },
];

// Get provider by ID (group name from operations)
export function getProviderById(id) {
  return providers.find((p) => p.id === id);
}

// Get provider links as a Record for lookup
export function getProviderLinks() {
  return Object.fromEntries(providers.map((p) => [p.id, { href: p.href, label: p.label }]));
}
