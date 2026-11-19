/**
 * E2E Runner Networking
 *
 * How the Playwright process reaches the E2E server container:
 * - On a developer machine: the published port, http://localhost:17110.
 * - Inside a container that shares the host's Docker daemon (the ANDO build
 *   container with --dind): directly over Docker networking. The runner attaches
 *   itself to the compose network and uses the server's container name, because
 *   host firewalls (ufw on workstations, nftables default-drop on the prod1 VMs)
 *   block container-to-host traffic, so host.docker.internal and published ports
 *   are not reliable there.
 *
 * The runner's hostname is its container id, so `docker network connect` can
 * address it directly. Disconnect before `compose down`, or the network cannot be
 * removed while the runner is still attached.
 */

import { execSync } from 'child_process';
import * as fs from 'fs';
import * as os from 'os';

/** Container name of the E2E server (both compose variants use it). */
export const SERVER_CONTAINER = 'ando-e2e-server';

/** Port the server publishes on the host (developer machines). */
export const SERVER_HOST_PORT = 17110;

/** Port the server listens on inside its container. */
export const SERVER_CONTAINER_PORT = 8080;

/**
 * Detect if we're running inside a Docker container.
 * The /.dockerenv file exists in Docker containers.
 */
export function isInsideContainer(): boolean {
  return fs.existsSync('/.dockerenv');
}

/**
 * Base URL of the E2E server for this process (no trailing slash).
 */
export function getServerBaseUrl(): string {
  return isInsideContainer()
    ? `http://${SERVER_CONTAINER}:${SERVER_CONTAINER_PORT}`
    : `http://localhost:${SERVER_HOST_PORT}`;
}

/** Name of the compose network the server container is attached to. */
function serverNetwork(): string {
  return execSync(
    `docker inspect -f '{{range $k,$v := .NetworkSettings.Networks}}{{$k}}{{end}}' ${SERVER_CONTAINER}`,
    { encoding: 'utf-8' }
  ).trim();
}

/**
 * Inside a container: attach this runner to the server's compose network so the
 * server is reachable by name. No-op on a developer machine. Idempotent.
 */
export function connectRunnerToServerNetwork(): void {
  if (!isInsideContainer()) return;
  const network = serverNetwork();
  try {
    execSync(`docker network connect ${network} ${os.hostname()}`, { stdio: 'pipe' });
    console.log(`Runner container attached to ${network} (reaching ${SERVER_CONTAINER} directly).`);
  } catch (error: unknown) {
    const message = String((error as { stderr?: Buffer }).stderr ?? (error as Error).message);
    if (!/already exists|already connected/i.test(message)) throw error;
  }
}

/**
 * Inside a container: detach this runner from the server's compose network (before
 * `compose down`). Never fails: the server or network may already be gone.
 */
export function disconnectRunnerFromServerNetwork(): void {
  if (!isInsideContainer()) return;
  try {
    execSync(`docker network disconnect ${serverNetwork()} ${os.hostname()}`, { stdio: 'pipe' });
  } catch {
    // Not connected, or the server container no longer exists.
  }
}
