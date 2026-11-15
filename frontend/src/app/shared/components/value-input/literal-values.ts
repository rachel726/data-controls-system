import { DataType } from '../../../core/models/catalog.models';
import { LiteralScalar } from '../../../core/models/control-definition';

/**
 * Converting what the user typed or picked into the literal values of the definition contract
 * (numbers as numbers, dates as "yyyy-MM-dd", booleans as true/false). Pure functions, unit-tested.
 */
export const LiteralValues = {
  /** Text box → literal; empty means "no value yet" (null). */
  fromText(raw: string, type: DataType): LiteralScalar | null {
    if (raw.trim() === '') {
      return null;
    }
    if (type === 'integer' || type === 'decimal') {
      const number = Number(raw);
      return Number.isFinite(number) ? number : null;
    }
    return raw;
  },

  /** Calendar date → "yyyy-MM-dd" from LOCAL date parts (toISOString would shift a day in UTC+ time zones). */
  fromDate(date: Date | null): string | null {
    if (!date || Number.isNaN(date.getTime())) {
      return null;
    }
    const pad = (n: number) => String(n).padStart(2, '0');
    return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
  },

  /** "yyyy-MM-dd" → local calendar date (null if not a valid ISO date). */
  toDate(value: unknown): Date | null {
    if (typeof value !== 'string') {
      return null;
    }
    const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value);
    return match ? new Date(Number(match[1]), Number(match[2]) - 1, Number(match[3])) : null;
  },

  /**
   * Whether two fields can be compared ("field operand"): same type, or both numeric.
   * Mirrors the server validator's rule - here only to offer sensible choices; the server still decides.
   */
  areComparable(a: DataType, b: DataType): boolean {
    const numeric = (t: DataType) => t === 'integer' || t === 'decimal';
    return a === b || (numeric(a) && numeric(b));
  },
};
