/**
 * Create a personal API token using Playwright's APIRequestContext.
 *
 * Usage:
 *   ANDO_BASE_URL=https://ando.dualconsult.net \
 *   ANDO_EMAIL=you@example.com \
 *   ANDO_PASSWORD=... \
 *   ANDO_TOKEN_NAME="CI automation" \
 *   node tools/create-api-token.js
 *
 * Output:
 *   Prints the token value once (store it somewhere safe).
 */

const { request } = require('@playwright/test');

function requiredEnv(name) {
  const v = process.env[name];
  if (!v) {
    throw new Error(`Missing required env var: ${name}`);
  }
  return v;
}

async function main() {
  const baseURL = process.env.ANDO_BASE_URL || 'http://localhost:17110';
  const email = requiredEnv('ANDO_EMAIL');
  const password = requiredEnv('ANDO_PASSWORD');
  const tokenName = process.env.ANDO_TOKEN_NAME || `automation-${new Date().toISOString()}`;

  const api = await request.newContext({
    baseURL,
    ignoreHTTPSErrors: true,
  });

  const loginRes = await api.post('/api/auth/login', {
    data: {
      email,
      password,
      rememberMe: true,
    },
  });

  if (!loginRes.ok()) {
    const body = await loginRes.text().catch(() => '');
    throw new Error(`Login failed: HTTP ${loginRes.status()} ${body}`);
  }

  const loginBody = await loginRes.json();
  if (!loginBody?.success) {
    throw new Error(`Login failed: ${loginBody?.error || 'unknown error'}`);
  }

  const createRes = await api.post('/api/auth/tokens', {
    data: { name: tokenName },
  });

  if (!createRes.ok()) {
    const body = await createRes.text().catch(() => '');
    throw new Error(`Token creation failed: HTTP ${createRes.status()} ${body}`);
  }

  const created = await createRes.json();
  if (!created?.success || !created?.value) {
    throw new Error(`Token creation failed: ${created?.error || 'unknown error'}`);
  }

  // Validate it can authenticate (example call).
  const probe = await api.get('/api/projects', {
    headers: { Authorization: `Bearer ${created.value}` },
  });
  if (probe.status() === 401) {
    throw new Error('Token probe failed (401). Token auth wiring is broken.');
  }

  // Print only the token value; metadata can be fetched later via /api/auth/tokens.
  process.stdout.write(`${created.value}\n`);

  await api.dispose();
}

main().catch((err) => {
  // Keep output minimal and avoid printing any secrets.
  console.error(err.message || String(err));
  process.exit(1);
});
