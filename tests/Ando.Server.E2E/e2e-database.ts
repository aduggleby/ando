/**
 * E2E Database Provider Selection
 *
 * Resolves which database variant the E2E environment runs against, from the
 * E2E_DB_PROVIDER environment variable:
 * - `sqlserver` (default): tests/docker-compose.test.yml, service `sqlserver`
 * - `postgres`: tests/docker-compose.test.postgres.yml, service `postgres`
 *
 * Shared by global setup, global teardown, and specs that drive docker compose
 * directly, so every consumer targets the same compose file.
 */

import * as path from 'path';

export type E2EDbProvider = 'sqlserver' | 'postgres';

export interface E2EDatabase {
  /** Normalized provider key from E2E_DB_PROVIDER. */
  provider: E2EDbProvider;
  /** Absolute path of the compose file for this variant. */
  composeFile: string;
  /** Compose service name of the database container. */
  databaseService: string;
  /** Value the server must report in the `database` field of GET /health. */
  expectedHealthDatabase: 'SqlServer' | 'PostgreSql';
}

/**
 * Returns the E2E database variant selected by E2E_DB_PROVIDER.
 * Throws for unknown values so a typo never silently runs the default variant.
 */
export function getE2EDatabase(): E2EDatabase {
  const raw = (process.env.E2E_DB_PROVIDER ?? 'sqlserver').trim().toLowerCase();

  switch (raw) {
    case '':
    case 'sqlserver':
      return {
        provider: 'sqlserver',
        composeFile: path.resolve(__dirname, '../docker-compose.test.yml'),
        databaseService: 'sqlserver',
        expectedHealthDatabase: 'SqlServer',
      };
    case 'postgres':
      return {
        provider: 'postgres',
        composeFile: path.resolve(__dirname, '../docker-compose.test.postgres.yml'),
        databaseService: 'postgres',
        expectedHealthDatabase: 'PostgreSql',
      };
    default:
      throw new Error(
        `Unknown E2E_DB_PROVIDER "${process.env.E2E_DB_PROVIDER}". Expected "sqlserver" (default) or "postgres".`
      );
  }
}
