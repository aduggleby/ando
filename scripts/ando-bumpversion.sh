#!/bin/bash
# =============================================================================
# ando-bumpversion.sh
#
# Bumps the version in both src/Ando/Ando.csproj and src/Ando.Server/Ando.Server.csproj
#
# Usage:
#   ./ando-bumpversion.sh           # Bump patch version (1.0.0 -> 1.0.1)
#   ./ando-bumpversion.sh minor     # Bump minor version (1.0.5 -> 1.1.0)
#   ./ando-bumpversion.sh major     # Bump major version (1.5.3 -> 2.0.0)
#
# =============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Clean up Syncthing conflict files first
"$SCRIPT_DIR/clean.sh"
REPO_ROOT="$SCRIPT_DIR/.."
CLI_CSPROJ="$REPO_ROOT/src/Ando/Ando.csproj"
SERVER_CSPROJ="$REPO_ROOT/src/Ando.Server/Ando.Server.csproj"

# Check if csproj files exist
if [[ ! -f "$CLI_CSPROJ" ]]; then
    echo "Error: $CLI_CSPROJ not found"
    exit 1
fi

if [[ ! -f "$SERVER_CSPROJ" ]]; then
    echo "Error: $SERVER_CSPROJ not found"
    exit 1
fi

# Get bump type from argument (default: patch)
BUMP_TYPE="${1:-patch}"

# Show help
if [[ "$BUMP_TYPE" == "-h" || "$BUMP_TYPE" == "--help" ]]; then
    echo "Usage: $0 [patch|minor|major]"
    echo ""
    echo "Bumps the version in both Ando CLI and Ando.Server projects"
    echo ""
    echo "Arguments:"
    echo "  patch   Bump patch version (1.0.0 -> 1.0.1) [default]"
    echo "  minor   Bump minor version (1.0.5 -> 1.1.0)"
    echo "  major   Bump major version (1.5.3 -> 2.0.0)"
    exit 0
fi

# Validate bump type
if [[ "$BUMP_TYPE" != "patch" && "$BUMP_TYPE" != "minor" && "$BUMP_TYPE" != "major" ]]; then
    echo "Error: Invalid bump type '$BUMP_TYPE'"
    echo "Usage: $0 [patch|minor|major]"
    exit 1
fi

# =============================================================================
# Check GitHub authentication before proceeding
# =============================================================================

# Verify gh CLI is authenticated (required for HTTPS push)
if ! gh auth status &>/dev/null; then
    echo "Error: GitHub CLI is not authenticated."
    echo ""
    echo "This script uses 'git push' which requires authentication for HTTPS remotes."
    echo "Please run 'gh auth login' and ensure git is configured to use gh as credential helper."
    echo ""
    echo "To set up gh as git credential helper:"
    echo "  gh auth login"
    echo "  gh auth setup-git"
    exit 1
fi

# =============================================================================
# Check for clean git state before proceeding
# =============================================================================

cd "$REPO_ROOT"

# Check for uncommitted changes (staged or unstaged) or untracked files
HAS_UNCOMMITTED=false
HAS_UNTRACKED=false

if ! git diff --quiet || ! git diff --cached --quiet; then
    HAS_UNCOMMITTED=true
fi

UNTRACKED=$(git ls-files --others --exclude-standard)
if [[ -n "$UNTRACKED" ]]; then
    HAS_UNTRACKED=true
fi

if [[ "$HAS_UNCOMMITTED" == true || "$HAS_UNTRACKED" == true ]]; then
    echo "Warning: You have uncommitted changes or untracked files."
    echo ""
    git status --short
    echo ""
    read -p "Auto-commit with Claude-generated message? (y/N): " AUTO_COMMIT

    case "$AUTO_COMMIT" in
        [yY]|[yY][eE][sS])
            echo ""
            echo "Generating commit message with Claude..."
            echo ""

            # Create temp file for prompt (handles special characters better)
            PROMPT_FILE=$(mktemp)
            trap "rm -f '$PROMPT_FILE'" EXIT

            # Write prompt to temp file
            cat > "$PROMPT_FILE" << 'PROMPT_HEADER'
Generate a concise git commit message for the following changes. Follow conventional commit format.

Rules:
- First line: type(scope): brief description (max 72 chars)
- Types: feat, fix, docs, style, refactor, test, chore, build
- Optional body: blank line then detailed explanation if needed
- Focus on WHAT changed and WHY, not HOW
- Be specific but concise

Git status:
PROMPT_HEADER

            git status --short >> "$PROMPT_FILE"

            echo "" >> "$PROMPT_FILE"
            echo "Staged changes diff:" >> "$PROMPT_FILE"
            git diff --cached >> "$PROMPT_FILE" 2>/dev/null || true

            echo "" >> "$PROMPT_FILE"
            echo "Unstaged changes diff:" >> "$PROMPT_FILE"
            git diff >> "$PROMPT_FILE" 2>/dev/null || true

            echo "" >> "$PROMPT_FILE"
            echo "Output ONLY the commit message, nothing else. No markdown formatting, no explanations." >> "$PROMPT_FILE"

            # Generate commit message using Claude (read prompt from file)
            GENERATED_MESSAGE=$(cat "$PROMPT_FILE" | claude -p - --allowedTools '' 2>&1)
            CLAUDE_EXIT_CODE=$?

            rm -f "$PROMPT_FILE"

            if [[ $CLAUDE_EXIT_CODE -ne 0 ]] || [[ -z "$GENERATED_MESSAGE" ]]; then
                echo "Error: Failed to generate commit message with Claude."
                echo "Claude output: $GENERATED_MESSAGE"
                exit 1
            fi

            echo "Generated commit message:"
            echo "----------------------------------------"
            echo "$GENERATED_MESSAGE"
            echo "----------------------------------------"
            echo ""
            read -p "Use this commit message? (Y/n): " USE_MESSAGE

            case "$USE_MESSAGE" in
                [nN]|[nN][oO])
                    echo "Aborted. Please commit manually."
                    exit 1
                    ;;
                *)
                    # Stage all changes and commit
                    git add -A
                    git commit -m "$GENERATED_MESSAGE"
                    echo ""
                    echo "Changes committed successfully."
                    echo ""
                    ;;
            esac
            ;;
        *)
            echo ""
            echo "Aborted. Please commit or stash your changes before bumping the version."
            exit 1
            ;;
    esac
else
    echo "Git state: clean (all changes committed)"
fi

echo ""

# =============================================================================
# Build verification - ensure everything compiles before bumping version
# =============================================================================

echo "Verifying build..."
echo ""

if ! dotnet build "$REPO_ROOT/src/Ando/Ando.csproj" --nologo -v q; then
    echo ""
    echo "Error: Build failed. Please fix build errors before bumping the version."
    exit 1
fi

if ! dotnet build "$REPO_ROOT/src/Ando.Server/Ando.Server.csproj" --nologo -v q; then
    echo ""
    echo "Error: Server build failed. Please fix build errors before bumping the version."
    exit 1
fi

echo "Build verification passed"
echo ""

# Extract current version from CLI csproj (source of truth)
CURRENT_VERSION=$(grep -oP '<Version>\K[0-9]+\.[0-9]+\.[0-9]+(?=</Version>)' "$CLI_CSPROJ")

if [[ -z "$CURRENT_VERSION" ]]; then
    echo "Error: Could not find <Version>x.y.z</Version> in $CLI_CSPROJ"
    exit 1
fi

# Parse version components
IFS='.' read -r MAJOR MINOR PATCH <<< "$CURRENT_VERSION"

# Bump version based on type
case "$BUMP_TYPE" in
    major)
        MAJOR=$((MAJOR + 1))
        MINOR=0
        PATCH=0
        ;;
    minor)
        MINOR=$((MINOR + 1))
        PATCH=0
        ;;
    patch)
        PATCH=$((PATCH + 1))
        ;;
esac

NEW_VERSION="$MAJOR.$MINOR.$PATCH"

# Update both csproj files
sed -i "s|<Version>$CURRENT_VERSION</Version>|<Version>$NEW_VERSION</Version>|" "$CLI_CSPROJ"
sed -i "s|<Version>[0-9]\+\.[0-9]\+\.[0-9]\+</Version>|<Version>$NEW_VERSION</Version>|" "$SERVER_CSPROJ"

echo "Version bumped: $CURRENT_VERSION -> $NEW_VERSION"
echo "  - src/Ando/Ando.csproj"
echo "  - src/Ando.Server/Ando.Server.csproj"

# =============================================================================
# Run Claude to verify documentation and add changelog entry
# =============================================================================

echo ""
echo "Running Claude to verify documentation and update changelog..."
echo ""

CLAUDE_PROMPT=$(cat <<'PROMPT_EOF'
You have just bumped the ANDO version. Your task is to verify all documentation is up to date and add a changelog entry for this release.

## Version Information
- Previous version: $CURRENT_VERSION
- New version: $NEW_VERSION
- Bump type: $BUMP_TYPE

## Tasks

### 1. Verify Operations Documentation

Compare the actual operations in the source code against the documentation:

**Source of truth (C# files):**
- `src/Ando/Operations/*Operations.cs` - All operation classes
- `src/Ando/Scripting/ScriptGlobals.cs` - Global variables exposed to build scripts

**Documentation to verify:**
- `website/src/data/operations.js` - Operations data (name, desc, examples for each operation)
- `website/src/data/providers.js` - Provider navigation links
- `website/public/llms.txt` - LLM-friendly documentation

For each operations file, check that:
1. All public methods are documented in operations.js
2. Method signatures and descriptions are accurate
3. Examples are correct and up to date

### 2. Verify Provider Documentation

Check that all providers have corresponding documentation:
- Each provider in operations.js should have an entry in providers.js
- Each provider should have a content file in `website/src/content/providers/{provider}.md`
- The llms.txt file should list all providers

### 3. Update Changelog

Add a new entry to `website/src/content/pages/changelog.md` for version $NEW_VERSION:

Format:
```markdown
## $NEW_VERSION

**$(date +%Y-%m-%d)**

[Summary of changes - look at git diff or recent commits to understand what changed]

[Group changes by category: New Features, Improvements, Bug Fixes, Breaking Changes, etc.]
```

If the changelog already has an entry for $NEW_VERSION, update it rather than creating a duplicate.

### 4. Update Index Page Version Badge

In `website/src/pages/index.astro`, find the version badge and update it to show the new version:

Look for a line like:
```astro
        v0.9.x
```

Update it to:
```astro
        v$NEW_VERSION
```

### 5. Build Verification

After making changes, run:
```bash
cd website && npm run build
```

This ensures the documentation site builds correctly.

## Important Notes

- Only update documentation to match actual code - don't add documentation for features that don't exist
- Keep the same formatting and style as existing documentation
- For the changelog, focus on user-facing changes
- If you're unsure about a change, leave a comment or note for manual review
- Run prettier after editing website files: `cd website && npm run format`

## Output

Provide a summary of:
1. Any discrepancies found between code and documentation
2. Changes made to documentation
3. The changelog entry you added
4. Any issues that need manual attention
PROMPT_EOF
)

# Substitute variables into the prompt
CLAUDE_PROMPT="${CLAUDE_PROMPT//\$CURRENT_VERSION/$CURRENT_VERSION}"
CLAUDE_PROMPT="${CLAUDE_PROMPT//\$NEW_VERSION/$NEW_VERSION}"
CLAUDE_PROMPT="${CLAUDE_PROMPT//\$BUMP_TYPE/$BUMP_TYPE}"
CLAUDE_PROMPT="${CLAUDE_PROMPT//\$(date +%Y-%m-%d)/$(date +%Y-%m-%d)}"

# Save prompt to temp file for reference
PROMPT_FILE=$(mktemp)
echo "$CLAUDE_PROMPT" > "$PROMPT_FILE"

echo "=========================================="
echo "Claude will now verify documentation and update changelog."
echo "Prompt saved to: $PROMPT_FILE"
echo ""
echo "This runs with --dangerously-skip-permissions to avoid prompts."
echo "Claude is working... (output will appear when complete)"
echo "=========================================="
echo ""

# Run Claude with -p (non-interactive, auto-exits) and skip permission prompts
cd "$REPO_ROOT"
claude -p "$CLAUDE_PROMPT" --dangerously-skip-permissions

# Clean up temp file
rm -f "$PROMPT_FILE"

# =============================================================================
# Commit and push the version bump
# =============================================================================

echo ""
echo "Committing version bump..."
echo ""

cd "$REPO_ROOT"

# Stage all changes made by this script and Claude
git add -A

# Check if there are changes to commit
if git diff --cached --quiet; then
    echo "Warning: No changes to commit. This is unexpected."
    exit 1
fi

# Create commit with version bump message
git commit -m "Bump version to $NEW_VERSION

- Updated version in src/Ando/Ando.csproj
- Updated version in src/Ando.Server/Ando.Server.csproj
- Updated changelog and documentation"

echo ""
echo "Pushing to remote..."
echo ""

# Push to remote
git push

echo ""
echo "=========================================="
echo "Version bump complete: $CURRENT_VERSION -> $NEW_VERSION"
echo "Changes committed and pushed to remote."
echo "=========================================="

# =============================================================================
# Ask user if they want to publish
# =============================================================================

echo ""
read -p "Do you want to publish this version? This will run ando-push.sh (y/N): " PUBLISH_RESPONSE

case "$PUBLISH_RESPONSE" in
    [yY]|[yY][eE][sS])
        echo ""
        echo "Running ando-push.sh to publish..."
        echo ""
        "$SCRIPT_DIR/ando-push.sh"
        ;;
    *)
        echo ""
        echo "Skipping publish. Run './scripts/ando-push.sh' manually when ready."
        ;;
esac
