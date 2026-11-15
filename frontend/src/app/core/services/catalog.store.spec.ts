import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { EntityDto, LimitsDto, OperatorDto } from '../models/catalog.models';
import { CatalogStore } from './catalog.store';
import { TEST_LIMITS } from '../../../testing/test-catalog';

const field = (key: string, displayName: string, dataType: EntityDto['fields'][number]['dataType']) => ({
  key, displayName, dataType, description: null, isRequiredByBusiness: false, allowedValues: [], unit: null,
});

const ENTITIES: EntityDto[] = [
  {
    key: 'employee', displayName: 'עובד', displayNamePlural: 'עובדים', description: null,
    fields: [field('age', 'גיל', 'integer'), field('salary', 'שכר חודשי', 'decimal'), field('isActive', 'עובד פעיל', 'boolean')],
    relations: [
      { key: 'department', displayName: 'מחלקה', description: null, targetEntityKey: 'department', cardinality: 'manyToOne' },
      { key: 'manager', displayName: 'מנהל ישיר', description: null, targetEntityKey: 'employee', cardinality: 'manyToOne' },
      { key: 'directReports', displayName: 'עובדים כפופים', description: null, targetEntityKey: 'employee', cardinality: 'oneToMany' },
    ],
    // as the server lists and names them
    fieldPaths: [
      { path: 'age', label: 'גיל', owner: 'עובד', entityKey: 'employee', fieldKey: 'age' },
      { path: 'salary', label: 'שכר חודשי', owner: 'עובד', entityKey: 'employee', fieldKey: 'salary' },
      { path: 'department.name', label: 'שם המחלקה', owner: 'מחלקה', entityKey: 'department', fieldKey: 'name' },
      { path: 'manager.salary', label: 'שכר חודשי של מנהל ישיר', owner: 'מנהל ישיר', entityKey: 'employee', fieldKey: 'salary' },
      { path: 'department.budget', label: 'תקציב', owner: 'מחלקה', entityKey: 'department', fieldKey: 'budget' },   // unknown field
    ],
  },
  {
    key: 'department', displayName: 'מחלקה', displayNamePlural: 'מחלקות', description: null,
    fields: [field('name', 'שם המחלקה', 'text'), field('isActive', 'מחלקה פעילה', 'boolean')],
    relations: [{ key: 'employees', displayName: 'עובדים', description: null, targetEntityKey: 'employee', cardinality: 'oneToMany' }],
    fieldPaths: [{ path: 'name', label: 'שם המחלקה', owner: 'מחלקה', entityKey: 'department', fieldKey: 'name' }],
  },
];

const OPERATORS: OperatorDto[] = [
  { key: 'eq', label: 'שווה ל', dateLabel: null, allowedTypes: ['text', 'integer', 'decimal', 'date', 'boolean', 'enum'], shape: 'value', allowsFieldOperand: true },
  { key: 'lt', label: 'קטן מ', dateLabel: 'לפני', allowedTypes: ['integer', 'decimal', 'date'], shape: 'value', allowsFieldOperand: true },
  { key: 'contains', label: 'מכיל', dateLabel: null, allowedTypes: ['text'], shape: 'value', allowsFieldOperand: false },
];

const LIMITS: LimitsDto = TEST_LIMITS;

describe('CatalogStore', () => {
  let store: CatalogStore;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    store = TestBed.inject(CatalogStore);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function load(limits: LimitsDto = LIMITS): Promise<void> {
    const loading = store.load();
    http.expectOne('/api/catalog/entities').flush(ENTITIES);
    http.expectOne('/api/catalog/operators').flush(OPERATORS);
    http.expectOne('/api/catalog/limits').flush(limits);
    await loading;
  }

  it('loads the catalog once, however many screens ask for it', async () => {
    const first = store.load();
    const second = store.load();
    http.expectOne('/api/catalog/entities').flush(ENTITIES);
    http.expectOne('/api/catalog/operators').flush(OPERATORS);
    http.expectOne('/api/catalog/limits').flush(LIMITS);
    await Promise.all([first, second]);
    await store.load();

    http.expectNone('/api/catalog/entities');
    expect(store.loaded()).toBe(true);
    expect(store.entity('department')?.displayName).toBe('מחלקה');
    expect(store.limits()).toEqual(LIMITS);
  });

  it("offers the server's field list as it is, with each field's details", async () => {
    await load();

    const options = store.fieldOptions('employee');

    expect(options.map((o) => [o.path, o.label, o.owner])).toEqual([
      ['age', 'גיל', 'עובד'],
      ['salary', 'שכר חודשי', 'עובד'],
      ['department.name', 'שם המחלקה', 'מחלקה'],
      ['manager.salary', 'שכר חודשי של מנהל ישיר', 'מנהל ישיר'],
    ]);   // the server's order and names; a field the catalog does not have is left out
    expect(store.fieldOption('employee', 'manager.salary')?.field.dataType).toBe('decimal');
    expect(store.fieldOptions('department').map((o) => o.path)).toEqual(['name']);
  });

  it('lists one-to-many relations for related-records conditions', async () => {
    await load();

    expect(store.relatedRelations('department').map((r) => r.key)).toEqual(['employees']);
    expect(store.relatedRelations('employee').map((r) => r.key)).toEqual(['directReports']);
  });

  it('offers only the operators allowed for a data type, with date wording for dates', async () => {
    await load();

    expect(store.operatorsFor('text').map((o) => o.key)).toEqual(['eq', 'contains']);
    expect(store.operatorsFor('boolean').map((o) => o.key)).toEqual(['eq']);
    const lt = store.operatorsFor('date').find((o) => o.key === 'lt')!;
    expect(store.operatorLabel(lt, 'date')).toBe('לפני');
    expect(store.operatorLabel(lt, 'integer')).toBe('קטן מ');
  });

  it('resolves a stored path and reports unknown ones', async () => {
    await load();

    expect(store.fieldOption('employee', 'department.name')?.field.displayName).toBe('שם המחלקה');
    expect(store.fieldOption('employee', 'shoeSize')).toBeUndefined();
    expect(store.fieldOptions('nosuch')).toEqual([]);
  });

  it('keeps an error and allows a retry when loading fails', async () => {
    const failed = store.load();
    http.expectOne('/api/catalog/entities').flush('down', { status: 500, statusText: 'Server Error' });
    http.expectOne('/api/catalog/operators');
    http.expectOne('/api/catalog/limits');
    await failed;

    expect(store.loaded()).toBe(false);
    expect(store.error()).not.toBeNull();

    await load();
    expect(store.loaded()).toBe(true);
    expect(store.error()).toBeNull();
  });
});
