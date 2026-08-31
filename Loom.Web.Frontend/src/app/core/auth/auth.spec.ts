import { shouldAttachToken } from './auth.interceptor';
import { refreshDelayMs } from './auth.service';
import { wsBase } from '../services/websocket.service';
import { loginErrorMessage } from '../../features/login/login.component';
import { exportFileName } from '../../features/logs/logs.component';

describe('shouldAttachToken', () => {
  it('attaches to relative api and prometheus paths', () => {
    expect(shouldAttachToken('/api/logs')).toBe(true);
    expect(shouldAttachToken('/api/token/refresh')).toBe(true);
    expect(shouldAttachToken('/prometheus')).toBe(true);
  });

  it('does not attach to the login post or non-api paths', () => {
    expect(shouldAttachToken('/api/token')).toBe(false);
    expect(shouldAttachToken('/api/token?x=1')).toBe(false);
    expect(shouldAttachToken('https://evil.example/api/logs')).toBe(false);
    expect(shouldAttachToken('/assets/x.png')).toBe(false);
  });
});

describe('refreshDelayMs', () => {
  it('subtracts the lead time from the token lifetime', () => {
    expect(refreshDelayMs(3600)).toBe(3_000_000);
  });

  it('floors at 30 seconds for a short lifetime', () => {
    expect(refreshDelayMs(60)).toBe(30_000);
    expect(refreshDelayMs(0)).toBe(30_000);
  });
});

describe('wsBase', () => {
  it('uses the configured value when present', () => {
    expect(wsBase('ws://example:1234', 'http:', 'localhost:4200')).toBe('ws://example:1234');
  });

  it('derives ws from http', () => {
    expect(wsBase('', 'http:', 'localhost:5209')).toBe('ws://localhost:5209');
  });

  it('derives wss from https', () => {
    expect(wsBase('', 'https:', 'example.com')).toBe('wss://example.com');
  });
});

describe('loginErrorMessage', () => {
  it('reports invalid credentials for 401', () => {
    expect(loginErrorMessage(401, null)).toBe('Invalid username or password.');
  });

  it('reports the retry delay for 429 with a valid header', () => {
    expect(loginErrorMessage(429, '45')).toBe('Too many attempts. Try again in 45 seconds.');
  });

  it('falls back for 429 with a missing header', () => {
    expect(loginErrorMessage(429, null)).toBe('Too many attempts. Try again shortly.');
  });

  it('falls back for 429 with an unparseable header', () => {
    expect(loginErrorMessage(429, 'abc')).toBe('Too many attempts. Try again shortly.');
  });

  it('reports a generic failure for anything else', () => {
    expect(loginErrorMessage(500, null)).toBe('Could not reach the Loom dashboard.');
  });
});

describe('exportFileName', () => {
  it('extracts a quoted filename', () => {
    expect(exportFileName('csv', 'attachment; filename="loom-logs.csv"')).toBe('loom-logs.csv');
  });

  it('extracts an unquoted filename', () => {
    expect(exportFileName('csv', 'attachment; filename=loom-logs.csv')).toBe('loom-logs.csv');
  });

  it('falls back to loom-logs.json when json has no header', () => {
    expect(exportFileName('json', null)).toBe('loom-logs.json');
  });

  it('maps text format to .txt', () => {
    expect(exportFileName('text', null)).toBe('loom-logs.txt');
  });
});
