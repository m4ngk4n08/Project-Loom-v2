import { toUtcIso, isSearchableQuery, scoreBarWidth, shortTraceId, matchesTraceFilter, groupByTemplate, levelRank, parseArguments, rowKey, DisplayRow, meetsMinLevel, isInteractiveEventTarget, canExplain, explainErrorMessage, hasNoPlaceholders } from './logs.component';
import { LogEntry } from '../../core/services/logs.service';

describe('toUtcIso', () => {
  it('converts a local datetime-local string to a UTC ISO string ending in Z', () => {
    const result = toUtcIso('2026-08-24T14:30');
    expect(result?.endsWith('Z')).toBe(true);
  });

  it('returns undefined for an empty string', () => {
    expect(toUtcIso('')).toBeUndefined();
  });

  it('preserves the instant named in the local timezone', () => {
    const result = toUtcIso('2026-08-24T14:30');
    const expected = new Date(2026, 7, 24, 14, 30).toISOString();
    expect(result).toBe(expected);
  });

  it('returns undefined for an unparseable value instead of throwing', () => {
    expect(() => toUtcIso('garbage')).not.toThrow();
    expect(toUtcIso('garbage')).toBeUndefined();
  });
});

describe('isSearchableQuery', () => {
  it('rejects a whitespace-only query', () => {
    expect(isSearchableQuery('   ')).toBe(false);
  });

  it('rejects an empty query', () => {
    expect(isSearchableQuery('')).toBe(false);
  });

  it('accepts a valid query', () => {
    expect(isSearchableQuery('database timeout')).toBe(true);
  });

  it('accepts a query with surrounding whitespace', () => {
    expect(isSearchableQuery('  timeout  ')).toBe(true);
  });
});

describe('scoreBarWidth', () => {
  it('returns 100 for the top score relative to itself', () => {
    expect(scoreBarWidth(5.2, 5.2)).toBe(100);
  });

  it('returns a proportional width below the top score', () => {
    expect(scoreBarWidth(2.6, 5.2)).toBe(50);
  });

  it('does not produce NaN or Infinity when the score is 0', () => {
    const width = scoreBarWidth(0, 5.2);
    expect(Number.isNaN(width)).toBe(false);
    expect(Number.isFinite(width)).toBe(true);
    expect(width).toBe(0);
  });

  it('does not produce NaN or Infinity when the top score is 0', () => {
    const width = scoreBarWidth(0, 0);
    expect(Number.isNaN(width)).toBe(false);
    expect(Number.isFinite(width)).toBe(true);
    expect(width).toBe(0);
  });

  it('clamps to 100 even if a score somehow exceeds the top score', () => {
    expect(scoreBarWidth(10, 5)).toBe(100);
  });
});

describe('shortTraceId', () => {
  it('returns undefined for undefined', () => {
    expect(shortTraceId(undefined)).toBeUndefined();
  });

  it('returns undefined for an empty string', () => {
    expect(shortTraceId('')).toBeUndefined();
  });

  it('returns the first 8 characters of a 32-hex trace id', () => {
    expect(shortTraceId('4bf92f3577b34da6a3ce929d0e0e4736')).toBe('4bf92f35');
  });

  it('returns a value shorter than 8 chars unchanged', () => {
    expect(shortTraceId('abc')).toBe('abc');
  });
});

describe('matchesTraceFilter', () => {
  it('matches everything when the filter is empty, even an undefined id', () => {
    expect(matchesTraceFilter(undefined, '')).toBe(true);
  });

  it('matches everything when the filter is empty, even a populated id', () => {
    expect(matchesTraceFilter('4bf92f3577b34da6a3ce929d0e0e4736', '')).toBe(true);
  });

  it('matches when the id equals the filter', () => {
    expect(matchesTraceFilter('4bf92f3577b34da6a3ce929d0e0e4736', '4bf92f3577b34da6a3ce929d0e0e4736')).toBe(true);
  });

  it('does not match when the id differs from the filter', () => {
    expect(matchesTraceFilter('4bf92f3577b34da6a3ce929d0e0e4736', 'deadbeefdeadbeefdeadbeefdeadbeef')).toBe(false);
  });

  it('does not match a non-empty filter against an undefined id', () => {
    expect(matchesTraceFilter(undefined, '4bf92f3577b34da6a3ce929d0e0e4736')).toBe(false);
  });
});

describe('levelRank', () => {
  it('ranks Critical strictly above Information', () => {
    expect(levelRank('Critical')).toBeGreaterThan(levelRank('Information'));
  });

  it('ranks an unrecognised level below every known level', () => {
    expect(levelRank('Bogus')).toBeLessThan(levelRank('Trace'));
  });
});

describe('groupByTemplate', () => {
  const entry = (over: Partial<LogEntry>): LogEntry => ({
    message: 'm', category: 'c', level: 'Information',
    timestampUtc: '2026-01-01T00:00:00Z', eventId: 0, ...over,
  });

  it('returns empty groups and zero ungroupedCount for empty input', () => {
    expect(groupByTemplate([])).toEqual({ groups: [], ungroupedCount: 0 });
  });

  it('counts every entry as ungrouped when none carry a template', () => {
    const entries = [entry({}), entry({}), entry({})];
    const result = groupByTemplate(entries);
    expect(result.groups).toEqual([]);
    expect(result.ungroupedCount).toBe(entries.length);
  });

  it('treats an empty-string template as ungrouped, not a group keyed on ""', () => {
    const result = groupByTemplate([entry({ template: '' })]);
    expect(result.groups).toEqual([]);
    expect(result.ungroupedCount).toBe(1);
  });

  it('groups two entries sharing a template into one group with count 2', () => {
    const result = groupByTemplate([
      entry({ template: 'User {UserId} logged in' }),
      entry({ template: 'User {UserId} logged in' }),
    ]);
    expect(result.groups.length).toBe(1);
    expect(result.groups[0].count).toBe(2);
  });

  it('produces two groups for two different templates', () => {
    const result = groupByTemplate([
      entry({ template: 'A {X}' }),
      entry({ template: 'B {Y}' }),
    ]);
    expect(result.groups.length).toBe(2);
  });

  it('sorts groups by count descending', () => {
    const result = groupByTemplate([
      entry({ template: 'A' }),
      entry({ template: 'B' }),
      entry({ template: 'B' }),
      entry({ template: 'B' }),
    ]);
    expect(result.groups.map(g => g.template)).toEqual(['B', 'A']);
  });

  it('breaks a tie on count by latestTimestampUtc descending', () => {
    const result = groupByTemplate([
      entry({ template: 'A', timestampUtc: '2026-01-01T00:00:00Z' }),
      entry({ template: 'B', timestampUtc: '2026-01-02T00:00:00Z' }),
    ]);
    expect(result.groups.map(g => g.template)).toEqual(['B', 'A']);
  });

  it('excludes untemplated entries from groups and counts exactly those as ungrouped', () => {
    const result = groupByTemplate([
      entry({ template: 'A' }),
      entry({}),
      entry({ template: 'A' }),
      entry({}),
      entry({}),
    ]);
    expect(result.groups.length).toBe(1);
    expect(result.groups[0].count).toBe(2);
    expect(result.ungroupedCount).toBe(3);
  });

  it("uses the highest severity present in the group, not the first or last entry", () => {
    const result = groupByTemplate([
      entry({ template: 'A', level: 'Warning' }),
      entry({ template: 'A', level: 'Critical' }),
      entry({ template: 'A', level: 'Information' }),
    ]);
    expect(result.groups[0].level).toBe('Critical');
  });

  it("sets category to the shared value when all entries agree, and 'multiple' when they do not", () => {
    const shared = groupByTemplate([
      entry({ template: 'A', category: 'Http' }),
      entry({ template: 'A', category: 'Http' }),
    ]);
    expect(shared.groups[0].category).toBe('Http');

    const mixed = groupByTemplate([
      entry({ template: 'A', category: 'Http' }),
      entry({ template: 'A', category: 'Db' }),
    ]);
    expect(mixed.groups[0].category).toBe('multiple');
  });
});

describe('parseArguments', () => {
  it('returns [] for undefined', () => {
    expect(parseArguments(undefined)).toEqual([]);
  });

  it('returns [] for an empty string', () => {
    expect(parseArguments('')).toEqual([]);
  });

  it('returns [] and does not throw for malformed JSON', () => {
    expect(() => parseArguments('{not json')).not.toThrow();
    expect(parseArguments('{not json')).toEqual([]);
  });

  it("returns [] for '{}'", () => {
    expect(parseArguments('{}')).toEqual([]);
  });

  it('returns one LogArgument per property with the correct names and values', () => {
    const result = parseArguments('{"UserId":"41","Ms":"900"}');
    expect(result).toEqual([
      { name: 'UserId', value: '41' },
      { name: 'Ms', value: '900' },
    ]);
  });

  it('preserves property order', () => {
    const result = parseArguments('{"z":"1","a":"2","m":"3"}');
    expect(result.map(a => a.name)).toEqual(['z', 'a', 'm']);
  });

  it('returns [] for a JSON array, null, or a bare string', () => {
    expect(parseArguments('[1,2]')).toEqual([]);
    expect(parseArguments('null')).toEqual([]);
    expect(parseArguments('"text"')).toEqual([]);
  });

  it('stringifies non-string values', () => {
    const result = parseArguments('{"n":42,"o":{"a":1}}');
    expect(result).toEqual([
      { name: 'n', value: '42' },
      { name: 'o', value: '{"a":1}' },
    ]);
  });
});

describe('rowKey', () => {
  const row = (over: Partial<DisplayRow>): DisplayRow => ({
    timestampUtc: '2026-01-01T00:00:00Z', level: 'Information',
    category: 'c', message: 'm', eventId: 0, score: null, ...over,
  });

  it('differs when only timestampUtc differs', () => {
    expect(rowKey(row({ timestampUtc: '2026-01-01T00:00:00Z' })))
      .not.toBe(rowKey(row({ timestampUtc: '2026-01-01T00:00:01Z' })));
  });

  it('differs when only message differs', () => {
    expect(rowKey(row({ message: 'm1' }))).not.toBe(rowKey(row({ message: 'm2' })));
  });

  it('differs when only level differs', () => {
    expect(rowKey(row({ level: 'Warning' }))).not.toBe(rowKey(row({ level: 'Error' })));
  });

  it('is identical for two structurally identical rows (documented collision)', () => {
    expect(rowKey(row({}))).toBe(rowKey(row({})));
  });
});

describe('meetsMinLevel', () => {
  it('an empty minLevel passes any level', () => {
    expect(meetsMinLevel('Trace', '')).toBe(true);
  });

  it('an equal level passes', () => {
    expect(meetsMinLevel('Warning', 'Warning')).toBe(true);
  });

  it('a higher level passes', () => {
    expect(meetsMinLevel('Error', 'Warning')).toBe(true);
  });

  it('a lower level fails', () => {
    expect(meetsMinLevel('Debug', 'Warning')).toBe(false);
  });

  it('an unrecognised entry level always passes, since a filter cannot classify it', () => {
    expect(meetsMinLevel('Verbose', 'Error')).toBe(true);
  });

  it('Trace against Trace passes (lowest-level boundary)', () => {
    expect(meetsMinLevel('Trace', 'Trace')).toBe(true);
  });
});

describe('isInteractiveEventTarget', () => {
  it('is false when the row itself is the target, even with role="button"', () => {
    const row = document.createElement('div');
    row.setAttribute('role', 'button');
    expect(isInteractiveEventTarget(row, row)).toBe(false);
  });

  it('is false for a plain span child of the row', () => {
    const row = document.createElement('div');
    const span = document.createElement('span');
    row.appendChild(span);
    expect(isInteractiveEventTarget(span, row)).toBe(false);
  });

  it('is true for a button child of the row', () => {
    const row = document.createElement('div');
    const button = document.createElement('button');
    row.appendChild(button);
    expect(isInteractiveEventTarget(button, row)).toBe(true);
  });

  it('is true for a span nested inside a button inside the row', () => {
    const row = document.createElement('div');
    const button = document.createElement('button');
    const span = document.createElement('span');
    button.appendChild(span);
    row.appendChild(button);
    expect(isInteractiveEventTarget(span, row)).toBe(true);
  });

  it('is true for an anchor child of the row', () => {
    const row = document.createElement('div');
    const anchor = document.createElement('a');
    row.appendChild(anchor);
    expect(isInteractiveEventTarget(anchor, row)).toBe(true);
  });

  it('is false when the target or currentTarget is null', () => {
    const row = document.createElement('div');
    expect(isInteractiveEventTarget(null, row)).toBe(false);
    expect(isInteractiveEventTarget(row, null)).toBe(false);
  });

  it('is false for a button that is an ANCESTOR of currentTarget - Element.closest() would get this wrong', () => {
    const button = document.createElement('button');
    const row = document.createElement('div');
    const span = document.createElement('span');
    button.appendChild(row);
    row.appendChild(span);
    expect(isInteractiveEventTarget(span, row)).toBe(false);
  });
});

describe('canExplain', () => {
  const row = (over: Partial<DisplayRow>): DisplayRow => ({
    timestampUtc: '2026-01-01T00:00:00Z', level: 'Information',
    category: 'c', message: 'm', eventId: 0, score: null, ...over,
  });

  it('returns true for a row with a populated template', () => {
    expect(canExplain(row({ template: 'processing {UserId}' }))).toBe(true);
  });

  it('returns false for a row with template undefined', () => {
    expect(canExplain(row({ template: undefined }))).toBe(false);
  });

  it("returns false for a row with template ''", () => {
    expect(canExplain(row({ template: '' }))).toBe(false);
  });
});

describe('hasNoPlaceholders', () => {
  it('returns false for a template with two placeholders', () => {
    expect(hasNoPlaceholders('Payment processed: {Amount} via {Method}')).toBe(false);
  });

  it('returns false for a template with a formatted placeholder', () => {
    expect(hasNoPlaceholders('Order {OrderId:D8} shipped')).toBe(false);
  });

  it('returns false for a template with a destructured placeholder', () => {
    expect(hasNoPlaceholders('User {@User} signed in')).toBe(false);
  });

  it('returns true for a template with no placeholders', () => {
    expect(hasNoPlaceholders('Failed to authenticate angelo@example.com')).toBe(true);
  });

  it('returns true for a plain message', () => {
    expect(hasNoPlaceholders('Cache warm')).toBe(true);
  });

  it('returns true when only escaped braces are present', () => {
    expect(hasNoPlaceholders('Progress at 50%% done {{literal}}')).toBe(true);
  });

  it('returns false when a real placeholder accompanies escaped braces', () => {
    expect(hasNoPlaceholders('Mixed {{escaped}} and {Real}')).toBe(false);
  });

  it('returns true for empty {} braces - not a named hole', () => {
    expect(hasNoPlaceholders('Empty {} braces')).toBe(true);
  });

  it("returns false for ''", () => {
    expect(hasNoPlaceholders('')).toBe(false);
  });

  it('returns false for undefined', () => {
    expect(hasNoPlaceholders(undefined)).toBe(false);
  });
});

describe('explainErrorMessage', () => {
  it('mentions LOOM_LLM_API_KEY for a 404', () => {
    expect(explainErrorMessage(404)).toContain('LOOM_LLM_API_KEY');
  });

  it('an unconfigured feature is not worded as a failure for a 404', () => {
    const message = explainErrorMessage(404).toLowerCase();
    expect(message).not.toContain('failed');
    expect(message).not.toContain('error');
  });

  it('mentions "template" for a 400', () => {
    expect(explainErrorMessage(400)).toContain('template');
  });

  it('returns the generic message for a 500', () => {
    expect(explainErrorMessage(500)).toBe('Could not reach the model. Check the connection and try again.');
  });

  it('returns the same generic message as 500 for a 0 status and does not throw', () => {
    expect(() => explainErrorMessage(0)).not.toThrow();
    expect(explainErrorMessage(0)).toBe(explainErrorMessage(500));
  });
});
