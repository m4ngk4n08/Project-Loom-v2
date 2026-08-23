// Contract tests: hit the real Loom.Web.Api and assert responses match our TS DTOs
// (QueryResponse, AlertRule, ExporterStatus, MetricSummary, Cpu/Memory/ThreadMetricResponse).
// Catches silent drift when a C# property is renamed/removed but the mirrored TS
// interface isn't updated.
//
// Requires a running backend. Start it first:
//   dotnet run --project Loom.Web.Api
// Override the target with LOOM_API_BASE_URL if it's not on the default port.
import { beforeAll, describe, expect, it } from 'vitest';

// Read via globalThis rather than `process.env` directly: this package has no
// @types/node dependency (only vitest + jsdom), and this avoids adding one.
const nodeProcess = (globalThis as { process?: { env?: Record<string, string | undefined> } }).process;
const BASE_URL = nodeProcess?.env?.['LOOM_API_BASE_URL'] ?? 'http://localhost:5209';

async function getJson(path: string): Promise<unknown> {
  const res = await fetch(`${BASE_URL}${path}`);
  expect(res.ok, `${path} returned ${res.status}`).toBe(true);
  return res.json();
}

function assertKeys(obj: Record<string, unknown>, spec: Record<string, string>): void {
  for (const [key, type] of Object.entries(spec)) {
    expect(obj, `missing "${key}"`).toHaveProperty(key);
    if (obj[key] !== null) {
      expect(typeof obj[key], `"${key}" should be ${type}`).toBe(type);
    }
  }
}

beforeAll(async () => {
  try {
    const res = await fetch(`${BASE_URL}/api/health`);
    if (!res.ok) throw new Error(`status ${res.status}`);
  } catch (err) {
    throw new Error(
      `Loom.Web.Api is not reachable at ${BASE_URL} (${(err as Error).message}). ` +
        `Start it with "dotnet run --project Loom.Web.Api" before running contract tests.`
    );
  }

  // The metric store is empty on a cold-start instance with no target process attached
  // (verified: /api/exporters/metrics/names and /metrics/summary return [] otherwise) -
  // seed one metric so the "always non-empty" tests below have something to assert against.
  const seed = await fetch(`${BASE_URL}/api/metrics/ingest`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ metrics: [{ name: 'contract.test.seed', type: 'Gauge', value: 1 }] }),
  });
  expect(seed.ok, `seed POST /api/metrics/ingest returned ${seed.status}`).toBe(true);
});

// --- Endpoints whose backing store is legitimately empty in a default dev run.
// Runtime-skip (not a silent pass) via the test context when the array is empty,
// so the run report shows "skipped" instead of a green check that verified nothing.
describe('GET /api/exporters/status -> ExporterStatus[]', () => {
  it('matches the ExporterStatus TS interface', async (ctx) => {
    const body = (await getJson('/api/exporters/status')) as Record<string, unknown>[];
    if (body.length === 0) return ctx.skip('/api/exporters/status returned no exporters to assert against');
    for (const entry of body) {
      assertKeys(entry, { name: 'string', isHealthy: 'boolean', totalExports: 'number', totalFailures: 'number' });
    }
  });
});

describe('GET /api/alerts -> AlertRule[]', () => {
  it('matches the AlertRule TS interface (required fields)', async (ctx) => {
    const body = (await getJson('/api/alerts')) as Record<string, unknown>[];
    if (body.length === 0) return ctx.skip('/api/alerts returned no rules to assert against');
    for (const entry of body) {
      assertKeys(entry, { name: 'string', metricName: 'string', window: 'string' });
    }
  });
});

// --- Endpoints guaranteed non-empty once the API is running: assert that explicitly
// so an empty array fails loudly instead of the loop below silently checking nothing.
describe('GET /api/exporters/metrics/names -> string[]', () => {
  it('matches string[]', async () => {
    const body = (await getJson('/api/exporters/metrics/names')) as unknown[];
    expect(body.length).toBeGreaterThan(0);
    for (const name of body) expect(typeof name).toBe('string');
  });
});

describe('GET /api/exporters/metrics/summary -> MetricSummary[]', () => {
  it('matches the MetricSummary TS interface', async () => {
    const body = (await getJson('/api/exporters/metrics/summary')) as Record<string, unknown>[];
    expect(body.length).toBeGreaterThan(0);
    for (const entry of body) {
      assertKeys(entry, {
        name: 'string', type: 'string', unit: 'string', sampleCount: 'number',
        latestValue: 'number', average: 'number', min: 'number', max: 'number', p95: 'number',
        firstTimestampUtc: 'string', lastTimestampUtc: 'string',
      });
    }
  });
});

describe('POST /api/query -> QueryResponse', () => {
  it('matches the QueryResponse TS interface, including a genuinely non-empty row set', async () => {
    const metricName = `contract.test.${Date.now()}`;
    const ingest = await fetch(`${BASE_URL}/api/metrics/ingest`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ metrics: [{ name: metricName, type: 'Gauge', value: 42 }] }),
    });
    expect(ingest.ok, `POST /api/metrics/ingest returned ${ingest.status}`).toBe(true);

    const res = await fetch(`${BASE_URL}/api/query`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ query: `SELECT * FROM telemetry WHERE method = '${metricName}' LIMIT 5` }),
    });
    expect(res.ok, `POST /api/query returned ${res.status}`).toBe(true);
    const body = (await res.json()) as Record<string, unknown>;

    assertKeys(body, { columns: 'object', rows: 'object', executionTimeMs: 'number' });
    expect(Array.isArray(body['columns'])).toBe(true);
    const rows = body['rows'] as Record<string, unknown>[];
    expect(Array.isArray(rows)).toBe(true);
    expect(rows.length, 'no rows returned for the freshly-ingested metric').toBeGreaterThan(0);

    const variantKeys = ['text', 'number', 'timestamp'];
    for (const row of rows) {
      expect(Array.isArray(row['values'])).toBe(true);
      for (const value of row['values'] as Record<string, unknown>[]) {
        // QueryValue is a flat record (text/number/timestamp). WhenWritingNull omits
        // unset fields, so a NULL cell arrives as `{}`. At most one field may be set,
        // and no other key should ever appear.
        const keys = Object.keys(value);
        const extraneous = keys.filter((k) => !variantKeys.includes(k));
        expect(extraneous, `QueryValue has unexpected key(s): ${extraneous.join(', ')}`).toEqual([]);
        expect(keys.length, `QueryValue should have at most one populated field, got ${JSON.stringify(value)}`).toBeLessThanOrEqual(1);
      }
    }
  });
});

// --- The dashboard's live CPU/memory/thread panels are driven by the /ws/metrics
// WebSocket (see DashboardStateService), not these REST endpoints directly. Both are
// backed by the same IMetricsService/DTOs, so these are a faithful proxy for the
// WS payload shape without needing a WebSocket client in the test harness.
describe('GET /api/metrics/cpu -> CpuMetricResponse', () => {
  it('matches the CpuMetricResponse TS interface (dashboard-state.service.ts)', async () => {
    const body = (await getJson('/api/metrics/cpu')) as Record<string, unknown>;
    assertKeys(body, { cpuUsagePercent: 'number', hotpaths: 'object', timestamp: 'string' });
    expect(Array.isArray(body['hotpaths'])).toBe(true);
  });
});

describe('GET /api/metrics/memory -> MemoryMetricResponse', () => {
  it('matches the MemoryMetricResponse TS interface (dashboard-state.service.ts)', async () => {
    const body = (await getJson('/api/metrics/memory')) as Record<string, unknown>;
    assertKeys(body, {
      totalMemoryMb: 'number', usedMemoryMb: 'number', gcStats: 'object',
      topAllocations: 'object', timestamp: 'string',
    });
    expect(Array.isArray(body['topAllocations'])).toBe(true);
    assertKeys(body['gcStats'] as Record<string, unknown>, {
      gen0Collections: 'number', gen1Collections: 'number', gen2Collections: 'number', totalGcTimeMs: 'number',
    });
  });
});

describe('GET /api/metrics/thread -> ThreadMetricResponse', () => {
  it('matches the ThreadMetricResponse TS interface (dashboard-state.service.ts)', async () => {
    const body = (await getJson('/api/metrics/thread')) as Record<string, unknown>;
    assertKeys(body, {
      totalThreads: 'number', activeThreads: 'number', blockedThreads: 'number',
      blockages: 'object', timestamp: 'string',
    });
    expect(Array.isArray(body['blockages'])).toBe(true);
  });
});
