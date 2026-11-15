import { LiteralValues } from './literal-values';

describe('LiteralValues', () => {
  it('turns typed text into numbers for numeric fields and keeps text as is', () => {
    expect(LiteralValues.fromText('15000', 'decimal')).toBe(15000);
    expect(LiteralValues.fromText('18', 'integer')).toBe(18);
    expect(LiteralValues.fromText('-2.5', 'decimal')).toBe(-2.5);
    expect(LiteralValues.fromText('כהן', 'text')).toBe('כהן');
  });

  it('treats an empty box as "no value yet" and rejects non-numbers for numeric fields', () => {
    expect(LiteralValues.fromText('', 'text')).toBeNull();
    expect(LiteralValues.fromText('   ', 'integer')).toBeNull();
    expect(LiteralValues.fromText('abc', 'decimal')).toBeNull();
  });

  it('writes calendar dates as yyyy-MM-dd from local date parts (no time-zone shift)', () => {
    expect(LiteralValues.fromDate(new Date(2020, 2, 15))).toBe('2020-03-15');
    expect(LiteralValues.fromDate(new Date(2020, 0, 1, 0, 0, 0))).toBe('2020-01-01');      // local midnight - toISOString would give 2019-12-31 in UTC+2
    expect(LiteralValues.fromDate(new Date(2020, 11, 31, 23, 59))).toBe('2020-12-31');
    expect(LiteralValues.fromDate(null)).toBeNull();
    expect(LiteralValues.fromDate(new Date('invalid'))).toBeNull();
  });

  it('reads yyyy-MM-dd back as the same local calendar day', () => {
    const date = LiteralValues.toDate('2020-03-15')!;
    expect([date.getFullYear(), date.getMonth(), date.getDate()]).toEqual([2020, 2, 15]);
    expect(LiteralValues.fromDate(date)).toBe('2020-03-15');                                 // round trip
    expect(LiteralValues.toDate('15/03/2020')).toBeNull();
    expect(LiteralValues.toDate(18)).toBeNull();
  });

  it('allows comparing a field with another of the same type or both numeric (same rule as the server)', () => {
    expect(LiteralValues.areComparable('date', 'date')).toBe(true);
    expect(LiteralValues.areComparable('integer', 'decimal')).toBe(true);
    expect(LiteralValues.areComparable('decimal', 'date')).toBe(false);
    expect(LiteralValues.areComparable('text', 'enum')).toBe(false);
  });
});
