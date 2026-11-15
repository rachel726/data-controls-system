import { TestBed } from '@angular/core/testing';
import { CatalogStore, FieldOption } from '../../../core/services/catalog.store';
import { loadTestCatalog, provideTestCatalogApi } from '../../../../testing/test-catalog';
import { conditionPhrase, operandText } from './operand-text';

describe('operandText', () => {
  let catalog: CatalogStore;
  const option = (entity: string, path: string): FieldOption => catalog.fieldOption(entity, path)!;
  const label = (path: string) => catalog.fieldOption('employee', path)?.label ?? path;

  beforeEach(async () => {
    TestBed.configureTestingModule({ providers: [provideTestCatalogApi] });
    catalog = await loadTestCatalog();
  });

  it('words values in business terms', () => {
    expect(operandText(option('employee', 'salary'), 'value', { kind: 'literal', value: 22000 }, label)).toBe('22,000 ₪');
    expect(operandText(option('employee', 'isActive'), 'value', { kind: 'literal', value: false }, label)).toBe('לא');
    expect(operandText(option('employee', 'hireDate'), 'value', { kind: 'literal', value: '2021-03-05' }, label)).toBe('05/03/2021');
    expect(operandText(option('employee', 'fullName'), 'value', { kind: 'literal', value: 'כהן' }, label)).toBe('"כהן"');
    expect(operandText(option('project', 'status'), 'list', { kind: 'literal', value: ['Active', 'Planned'] }, label)).toBe('פעיל, מתוכנן');
  });

  it('tells a range from a two-item list by the operator, and marks a missing end', () => {
    expect(operandText(option('employee', 'age'), 'range', { kind: 'literal', value: [18, 30] }, label)).toBe('18 ל-30');
    expect(operandText(option('employee', 'age'), 'list', { kind: 'literal', value: [18, 30] }, label)).toBe('18, 30');
    expect(operandText(option('employee', 'age'), 'range', { kind: 'literal', value: [18, null] }, label)).toBe('18 ל-…');
  });

  it('names a compared field, and is empty without a value', () => {
    expect(operandText(option('employee', 'salary'), 'value', { kind: 'field', field: 'manager.salary' }, label)).toBe('שכר חודשי של מנהל ישיר');
    expect(operandText(option('employee', 'salary'), 'value', null, label)).toBe('');
  });

  it('words operator and value together like the server: prefixes, lists, yes/no, ranges, no value', () => {
    expect(conditionPhrase('קטן מ', 'value', false, '18')).toBe('קטן מ-18');
    expect(conditionPhrase('גדול מ', 'value', false, 'שכר חודשי')).toBe('גדול משכר חודשי');
    expect(conditionPhrase('לפני', 'value', false, '01/01/2020')).toBe('לפני 01/01/2020');
    expect(conditionPhrase('שווה ל', 'value', true, 'כן')).toBe(': כן');
    expect(conditionPhrase('אחד מ', 'list', false, 'פעיל, מתוכנן')).toBe('אחד מ: פעיל, מתוכנן');
    expect(conditionPhrase('בין', 'range', false, '5,000 ₪ ל-9,000 ₪')).toBe('בין 5,000 ₪ ל-9,000 ₪');
    expect(conditionPhrase('ריק', 'none', false, '')).toBe('ריק');
    expect(conditionPhrase('שווה ל', 'value', false, '"רחל"')).toBe('שווה ל"רחל"');   // a quoted text joins the prefix
    expect(conditionPhrase('ריק', 'none', false, '', true)).toBe(': ריק');              // after a yes/no field
  });
});
