---
title: Cloudflare
description: Deploy static sites to Cloudflare Pages using the wrangler CLI.
provider: Cloudflare
---

## Authentication

Cloudflare operations require an API token and account ID. Set these as environment variables or enter them when prompted.

### Environment Variables

| Variable | Description |
|----------|-------------|
| `CLOUDFLARE_API_TOKEN` | API token with Cloudflare Pages permissions |
| `CLOUDFLARE_ACCOUNT_ID` | Your Cloudflare account ID (found in dashboard URL) |
| `CLOUDFLARE_PROJECT_NAME` | Optional default project name for Pages operations |

### Interactive Prompting

When running locally without environment variables set, `Cloudflare.EnsureAuthenticated()` will prompt for credentials interactively. The API token input is hidden for security. Credentials are only stored for the current build session and are cleared when the process exits.

### Creating a Cloudflare API Token

1. Go to [Cloudflare Dashboard → Profile → API Tokens](https://dash.cloudflare.com/profile/api-tokens)
2. Click **Create Token**
3. Click **Create Custom Token**
4. Add the required permissions (see table below)
5. Under **Account Resources**, select your account
6. Under **Zone Resources**, select **All zones** or specific zones
7. Click **Continue to summary**, then **Create Token**
8. Copy the token and set it as `CLOUDFLARE_API_TOKEN`

### Required Token Permissions

| Permission | Required For |
|------------|--------------|
| `Account → Cloudflare Pages → Edit` | Deploying to Cloudflare Pages |
| `Zone → Cache Purge → Purge` | Purging cache with `PurgeCache()` |
| `Zone → Zone → Read` | Resolving domain names to Zone IDs (for `PurgeCache("example.com")`) |

### Finding Your Account ID

Your account ID is in the Cloudflare dashboard URL: `https://dash.cloudflare.com/ACCOUNT_ID/...`

Or find it on any zone's overview page in the right sidebar under **API → Account ID**.

## Example

Deploy a static site to Cloudflare Pages and purge the cache.

```csharp
// Create a directory reference
var website = Directory("./website");

// Ensure we're authenticated with Cloudflare
Cloudflare.EnsureAuthenticated();

// Build the site
Npm.Ci(website);
Npm.Run(website, "build");

// Deploy to Cloudflare Pages (deploy the dist folder)
Cloudflare.PagesDeploy(website / "dist", "my-website");

// Purge the cache (accepts domain name or Zone ID)
Cloudflare.PurgeCache("my-website.com");
```

## Options Reference

### Cloudflare.PagesDeploy Options

| Option | Description |
|--------|-------------|
| `WithProjectName(string)` | Cloudflare Pages project name. Must match an existing project in your Cloudflare account. Can also be set via `CLOUDFLARE_PROJECT_NAME` environment variable. |
| `WithBranch(string)` | Git branch name for the deployment. Used by Cloudflare to determine if this is a production or preview deployment. Production branch deploys to the main URL. |
| `WithCommitHash(string)` | Git commit hash to associate with the deployment. Displayed in Cloudflare dashboard for tracking which code version is deployed. |
| `WithCommitMessage(string)` | Git commit message for the deployment. Displayed in Cloudflare dashboard alongside the commit hash. |
