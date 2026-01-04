# ANDO Todo List

## High Priority

- [ ] **Document Azure & Bicep operations on website**
  - Website currently documents: Dotnet, Npm, Ef, Cloudflare
  - Missing: Azure (EnsureLoggedIn, LoginWithServicePrincipal, LoginWithManagedIdentity, SetSubscription, CreateResourceGroup, DeleteResourceGroup)
  - Missing: Bicep (DeployToResourceGroup, DeployToSubscription, DeployToManagementGroup)
  - Update `website/src/pages/index.astro` operations array

- [ ] **Commit remaining changes**
  - Uncommitted modifications to:
    - `src/Ando/Operations/AzureOperations.cs`
    - `src/Ando/Operations/DotnetOperations.cs`
    - `src/Ando/Workflow/WorkflowRunner.cs`
    - `tests/Ando.Tests/Unit/Operations/AzureOperationsTests.cs`
    - `tests/Ando.Tests/E2E/ExampleProjectTests.cs`
    - `tests/Ando.Tests/Integration/AzureIntegrationTests.cs`
    - `tests/Ando.Tests/Integration/DockerManagerTests.cs`
  - Untracked files to review:
    - `docker-compose.yml`
    - `tests/Ando.Server.E2E/` (Playwright E2E tests)

- [ ] **Add file header comments to files missing them**
  - Run: `find src tests -name "*.cs" -not -path "*/obj/*" -not -path "*/bin/*" | xargs grep -L "^// =\+$"`
  - Add standard header format per CLAUDE.md documentation standards

- [ ] **Expand test coverage**
  - Current: ~55-65%
  - Target: 80%+
  - Focus on edge cases and error handling
  - Add property-based tests with FsCheck

- [ ] **Website improvements**
  - Add search functionality
  - Add dark mode toggle
  - Add copy-to-clipboard for code blocks

## Completed

- [x] Add Cloudflare Pages operations
- [x] Document Cloudflare operations on website
- [x] Create website build.ando example
- [x] Write DEPLOYMENT.md server deployment guide
