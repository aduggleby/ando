// Provider metadata for navigation and linking (sorted alphabetically)
export const providers = [
  { id: "Ando", href: "/providers/ando", label: "Ando" },
  { id: "AppService", href: "/providers/appservice", label: "App Service" },
  { id: "Azure", href: "/providers/azure", label: "Azure" },
  { id: "Bicep", href: "/providers/bicep", label: "Bicep" },
  { id: "Cloudflare", href: "/providers/cloudflare", label: "Cloudflare" },
  { id: "Dotnet", href: "/providers/dotnet", label: "Dotnet" },
  { id: "DotnetSdk", href: "/providers/dotnetsdk", label: "DotnetSdk" },
  { id: "Ef", href: "/providers/ef", label: "EF Core" },
  { id: "Functions", href: "/providers/functions", label: "Functions" },
  { id: "Node", href: "/providers/node", label: "Node" },
  { id: "Npm", href: "/providers/npm", label: "Npm" },
  { id: "Nuget", href: "/providers/nuget", label: "NuGet" },
];

// Get provider by ID (group name from operations)
export function getProviderById(id) {
  return providers.find((p) => p.id === id);
}

// Get provider links as a Record for lookup
export function getProviderLinks() {
  return Object.fromEntries(providers.map((p) => [p.id, { href: p.href, label: p.label }]));
}
