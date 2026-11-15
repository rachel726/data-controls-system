import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { CatalogApi } from '../app/core/api/catalog-api.service';
import { DataType, EntityDto, FieldDto, FieldPathDto, LimitsDto, OperatorDto } from '../app/core/models/catalog.models';
import { CatalogStore } from '../app/core/services/catalog.store';

/**
 * Test copy of the business catalog (mirrors database/scripts/04_seed_catalog.sql, reduced to what UI tests need)
 * and of the operator registry served by the API.
 */
function field(key: string, displayName: string, dataType: DataType, extra: Partial<FieldDto> = {}): FieldDto {
  return { key, displayName, dataType, description: null, isRequiredByBusiness: false, allowedValues: [], unit: null, ...extra };
}

const EMPLOYEE_FIELDS: FieldDto[] = [
  field('fullName', 'שם מלא', 'text'),
  field('isActive', 'עובד פעיל', 'boolean', { description: 'האם העובד מועסק כיום בארגון.' }),
  field('age', 'גיל', 'integer', { description: 'גיל העובד בשנים מלאות.' }),
  field('email', 'דואר אלקטרוני', 'text'),
  field('hireDate', 'תאריך תחילת העסקה', 'date'),
  field('terminationDate', 'תאריך סיום העסקה', 'date'),
  field('salary', 'שכר חודשי', 'decimal', { unit: '₪' }),
];
const DEPARTMENT_FIELDS: FieldDto[] = [field('name', 'שם המחלקה', 'text'), field('isActive', 'מחלקה פעילה', 'boolean')];
const PROJECT_FIELDS: FieldDto[] = [
  field('name', 'שם הפרויקט', 'text'),
  field('status', 'סטטוס', 'enum', {
    allowedValues: [
      { value: 'Planned', label: 'מתוכנן' }, { value: 'Active', label: 'פעיל' },
      { value: 'Completed', label: 'הסתיים' }, { value: 'Cancelled', label: 'בוטל' },
    ],
  }),
  field('startDate', 'תאריך התחלה', 'date'),
  field('endDate', 'תאריך סיום', 'date'),
];

/** Field-list entries as the server words them (one relation): `suffix` names the owner where a name repeats. */
function paths(owner: string, entityKey: string, prefix: string, fields: readonly FieldDto[], suffix = ''): FieldPathDto[] {
  return fields.map((f) => ({ path: prefix + f.key, label: f.displayName + suffix, owner, entityKey, fieldKey: f.key }));
}

export const TEST_ENTITIES: EntityDto[] = [
  {
    key: 'employee', displayName: 'עובד', displayNamePlural: 'עובדים', description: 'עובדי הארגון',
    fields: EMPLOYEE_FIELDS,
    relations: [
      { key: 'department', displayName: 'מחלקה', description: null, targetEntityKey: 'department', cardinality: 'manyToOne' },
      { key: 'manager', displayName: 'מנהל ישיר', description: null, targetEntityKey: 'employee', cardinality: 'manyToOne' },
      { key: 'directReports', displayName: 'עובדים כפופים', description: null, targetEntityKey: 'employee', cardinality: 'oneToMany' },
    ],
    fieldPaths: [
      ...paths('עובד', 'employee', '', EMPLOYEE_FIELDS),
      ...paths('מחלקה', 'department', 'department.', DEPARTMENT_FIELDS),
      // the employee has the same fields - the manager's carry their owner
      ...paths('מנהל ישיר', 'employee', 'manager.', EMPLOYEE_FIELDS, ' של מנהל ישיר'),
    ],
  },
  {
    key: 'department', displayName: 'מחלקה', displayNamePlural: 'מחלקות', description: null,
    fields: DEPARTMENT_FIELDS,
    relations: [
      { key: 'employees', displayName: 'עובדים', description: null, targetEntityKey: 'employee', cardinality: 'oneToMany' },
      { key: 'projects', displayName: 'פרויקטים', description: null, targetEntityKey: 'project', cardinality: 'oneToMany' },
    ],
    fieldPaths: paths('מחלקה', 'department', '', DEPARTMENT_FIELDS),
  },
  {
    key: 'project', displayName: 'פרויקט', displayNamePlural: 'פרויקטים', description: null,
    fields: PROJECT_FIELDS,
    relations: [{ key: 'department', displayName: 'מחלקה אחראית', description: null, targetEntityKey: 'department', cardinality: 'manyToOne' }],
    fieldPaths: [...paths('פרויקט', 'project', '', PROJECT_FIELDS), ...paths('מחלקה אחראית', 'department', 'department.', DEPARTMENT_FIELDS)],
  },
];

const ALL: DataType[] = ['text', 'integer', 'decimal', 'date', 'boolean', 'enum'];
const ORDERED: DataType[] = ['integer', 'decimal', 'date'];

export const TEST_OPERATORS: OperatorDto[] = [
  { key: 'eq', label: 'שווה ל', dateLabel: null, allowedTypes: ALL, shape: 'value', allowsFieldOperand: true },
  { key: 'neq', label: 'שונה מ', dateLabel: null, allowedTypes: ALL, shape: 'value', allowsFieldOperand: true },
  { key: 'gt', label: 'גדול מ', dateLabel: 'אחרי', allowedTypes: ORDERED, shape: 'value', allowsFieldOperand: true },
  { key: 'gte', label: 'גדול או שווה ל', dateLabel: 'בתאריך או אחרי', allowedTypes: ORDERED, shape: 'value', allowsFieldOperand: true },
  { key: 'lt', label: 'קטן מ', dateLabel: 'לפני', allowedTypes: ORDERED, shape: 'value', allowsFieldOperand: true },
  { key: 'lte', label: 'קטן או שווה ל', dateLabel: 'בתאריך או לפני', allowedTypes: ORDERED, shape: 'value', allowsFieldOperand: true },
  { key: 'between', label: 'בין', dateLabel: null, allowedTypes: ORDERED, shape: 'range', allowsFieldOperand: false },
  { key: 'in', label: 'אחד מ', dateLabel: null, allowedTypes: ['text', 'integer', 'enum'], shape: 'list', allowsFieldOperand: false },
  { key: 'contains', label: 'מכיל', dateLabel: null, allowedTypes: ['text'], shape: 'value', allowsFieldOperand: false },
  { key: 'isEmpty', label: 'ריק', dateLabel: null, allowedTypes: ALL, shape: 'none', allowsFieldOperand: false },
  { key: 'isNotEmpty', label: 'לא ריק', dateLabel: null, allowedTypes: ALL, shape: 'none', allowsFieldOperand: false },
];

/** The server's default limits (ValidationLimits, ControlService, AiSuggestionService). */
export const TEST_LIMITS: LimitsDto = {
  maxConditions: 20,
  maxDepth: 3,
  maxRelationHops: 1,
  maxTextValueLength: 200,
  maxSearchLength: 100,
  maxNameLength: 200,
  maxDescriptionLength: 1000,
  maxAiTextLength: 1000,
};

/** Provider that makes the real CatalogStore load the test catalog (no HTTP). */
export const provideTestCatalogApi = {
  provide: CatalogApi,
  useValue: { entities: () => of(TEST_ENTITIES), operators: () => of(TEST_OPERATORS), limits: () => of(TEST_LIMITS) },
};

/** Loads the real CatalogStore with the test catalog; call inside a TestBed with `provideTestCatalogApi`. */
export async function loadTestCatalog(): Promise<CatalogStore> {
  const store = TestBed.inject(CatalogStore);
  await store.load();
  return store;
}
