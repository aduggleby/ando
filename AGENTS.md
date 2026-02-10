# Repository Guidelines

## Project Structure

- `src/Ando/`: Ando CLI (.NET tool) and core libraries.
- `src/Ando.Server/`: ASP.NET Core server (EF Core + SQL Server), React client in `src/Ando.Server/ClientApp/`.
- `tests/`:
  - `tests/Ando.Tests/`: unit tests for CLI/core.
  - `tests/Ando.Server.Tests/`: server tests (see `tests/Ando.Server.Tests/TESTING.md`).
  - `tests/Ando.Server.E2E/`: Playwright E2E tests (Node).
- `scripts/`: automation and deployment helpers (e.g. server installer).
- `website/`: docs site.

## Build, Test, and Development Commands

```bash
dotnet build Ando.sln
dotnet test
docker compose up -d         # local dev SQL Server + Ando.Server
docker compose logs -f
```

E2E (example):

```bash
docker compose -f tests/docker-compose.test.yml up -d --build
cd tests/Ando.Server.E2E && npm ci && npm test
```

## Coding Style & Naming

- C#: follow existing conventions; nullable reference types are enabled. Use `async`/`await` end-to-end for IO.
- Web client: TypeScript/React; keep lint clean (`src/Ando.Server/ClientApp/eslint.config.js`).
- Website: Prettier is used (`website/package.json`).

## Testing Guidelines

- Prefer adding/adjusting unit tests alongside behavior changes.
- Name tests descriptively; keep E2E flows stable and deterministic (no time-dependent assertions).

## Commit & Pull Request Guidelines

- Commit messages generally follow `type(scope): summary` (e.g. `fix(server): ...`, `refactor(email): ...`) and version bumps are explicit (`Bump version to X.Y.Z`).
- PRs should describe the behavior change, include repro steps, and note any config/env var changes.

## Agent-Specific Rules (Do Not Skip)

- **Never push by yourself.** Do not run `git push`, `docker push`, `docker buildx --push`, `dotnet nuget push`, or create/upload releases.
- If publishing is needed: ask the user to push, then **wait**. After the user confirms, you may pull/restart the image on the Ando server.

## Ando Server Access (Production)

Current server (LAN): `192.168.1.150`

SSH key: `~/.sshkeys/id_ad_dualconsult_com`

The repo has a documented production deployment (rootless Docker under user `ando`) with a compose file at `/opt/ando/docker-compose.yml`. See `CLAUDE.md` for canonical deployment notes and paths (some older docs reference a previous public IP).

Common management commands (run over SSH on the server):

```bash
# Example SSH (adjust user if needed)
ssh -i ~/.sshkeys/id_ad_dualconsult_com -o IdentitiesOnly=yes <user>@192.168.1.150

# Rootless Docker socket for the ando user (UID 1000 on the server)
export XDG_RUNTIME_DIR=/run/user/1000
export DOCKER_HOST=unix:///run/user/1000/docker.sock

# Status / logs
sudo -u ando docker compose -f /opt/ando/docker-compose.yml ps
sudo -u ando docker compose -f /opt/ando/docker-compose.yml logs -f ando-server

# Update to latest pushed image (registry-based)
sudo -u ando docker compose -f /opt/ando/docker-compose.yml pull ando-server
sudo -u ando docker compose -f /opt/ando/docker-compose.yml up -d ando-server
```
