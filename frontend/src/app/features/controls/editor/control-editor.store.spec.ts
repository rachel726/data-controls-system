import { TestBed } from '@angular/core/testing';
import { Observable, Subject, of, throwError } from 'rxjs';
import { ApiError } from '../../../core/api/api-error';
import { ControlsApi } from '../../../core/api/controls-api.service';
import { ConditionNode } from '../../../core/models/control-definition';
import { Control, SaveControlRequest, ValidateResponse } from '../../../core/models/control.models';
import { ControlDefinition } from '../../../core/models/control-definition';
import { ControlEditorStore, LIVE_CHECK_DEBOUNCE_MS } from './control-editor.store';
import { TEST_LIMITS, loadTestCatalog, provideTestCatalogApi } from '../../../../testing/test-catalog';

const age: ConditionNode = { type: 'condition', field: 'age', operator: 'lt', operand: { kind: 'literal', value: 18 } };

const SAVED: Control = {
  id: 7, name: 'קטינים', description: null, definitionDescription: 'גיל קטן מ-18',
  definition: { schemaVersion: 1, rootEntity: 'employee', filter: { type: 'group', logic: 'and', children: [age] } },
  createdAt: '2026-11-12T10:00:00Z', updatedAt: '2026-11-12T10:00:00Z', lastRun: null,
};

describe('ControlEditorStore', () => {
  let store: ControlEditorStore;
  let created: SaveControlRequest[];
  let updated: { id: number; request: SaveControlRequest }[];
  let saveResult: () => Observable<Control>;
  let getResult: () => Observable<Control>;
  let checked: ControlDefinition[];
  let checkResult: () => Observable<ValidateResponse>;

  beforeEach(async () => {
    vi.useFakeTimers();
    created = [];
    updated = [];
    checked = [];
    saveResult = () => of(SAVED);
    getResult = () => of(SAVED);
    checkResult = () => of({ isValid: true, errors: [], description: 'גיל קטן מ-18' });
    TestBed.configureTestingModule({
      providers: [
        ControlEditorStore,
        provideTestCatalogApi,
        {
          provide: ControlsApi,
          useValue: {
            get: () => getResult(),
            create: (request: SaveControlRequest) => {
              created.push(request);
              return saveResult();
            },
            update: (id: number, request: SaveControlRequest) => {
              updated.push({ id, request });
              return saveResult();
            },
            validate: (definition: ControlDefinition) => {
              checked.push(definition);
              return checkResult();
            },
          },
        },
      ],
    });
    await loadTestCatalog();   // the limits come from the server, with the catalog
    store = TestBed.inject(ControlEditorStore);
    store.startNew();
  });

  afterEach(() => vi.useRealTimers());

  /** Lets the debounced live check run. */
  function pause(): void {
    vi.advanceTimersByTime(LIVE_CHECK_DEBOUNCE_MS);
  }

  function draftMinors(): void {
    store.setName('  קטינים  ');
    store.setRootEntity('employee');
    store.apply({ kind: 'add', groupPath: [], node: age });
    pause();
  }

  it('tracks unsaved changes and whether the control can be saved', () => {
    expect(store.dirty()).toBe(false);
    expect(store.canSave()).toBe(false);

    store.setName('קטינים');
    store.setRootEntity('employee');
    expect(store.dirty()).toBe(true);
    expect(store.canSave()).toBe(false);   // no condition yet

    store.apply({ kind: 'add', groupPath: [], node: age });
    expect(store.conditionCount()).toBe(1);
    expect(store.canSave()).toBe(false);   // until the server has checked it
    pause();
    expect(store.canSave()).toBe(true);
  });

  it('allows a name up to the length the server accepts', () => {
    draftMinors();

    store.setName('א'.repeat(TEST_LIMITS.maxNameLength));
    expect(store.canSave()).toBe(true);
    store.setName('א'.repeat(TEST_LIMITS.maxNameLength + 1));
    expect(store.canSave()).toBe(false);
  });

  it('creates a new control with a trimmed name and no empty description', async () => {
    draftMinors();

    const saved = await store.save();

    expect(saved?.id).toBe(7);
    expect(created).toEqual([{ name: 'קטינים', description: null, definition: store.definition()! }]);
    expect(store.isNew()).toBe(false);
    expect(store.dirty()).toBe(false);
  });

  it('updates an existing control', async () => {
    await store.load(7);
    pause();
    store.setDescription('תיאור חדש');

    await store.save();

    expect(updated).toEqual([{ id: 7, request: { name: 'קטינים', description: 'תיאור חדש', definition: SAVED.definition } }]);
    expect(created).toEqual([]);
  });

  it('loads a control into a clean (not dirty) state, and reports load failures', async () => {
    await store.load(7);
    expect(store.name()).toBe('קטינים');
    expect(store.rootEntity()).toBe('employee');
    expect(store.conditionCount()).toBe(1);
    expect(store.dirty()).toBe(false);

    getResult = () => throwError(() => new ApiError(404, 'לא נמצא.', 'הבקרה המבוקשת לא נמצאה.'));
    await store.load(99);
    expect(store.loadError()).toBe('הבקרה המבוקשת לא נמצאה.');
  });

  it('keeps definition errors from the server and maps them to the offending node only', async () => {
    draftMinors();
    store.apply({ kind: 'add', groupPath: [], node: { type: 'group', logic: 'or', children: [age] } });
    pause();
    saveResult = () => throwError(() => new ApiError(400, 'הגדרת הבקרה אינה תקינה.', 'x', {}, [
      { path: 'filter.children[0].operand.value', code: 'InvalidValue', message: 'ערך לא תקין', isIncomplete: false },
      { path: 'filter.children[1].children[0].field', code: 'UnknownField', message: 'שדה לא קיים', isIncomplete: false },
    ]));

    expect(await store.save()).toBeNull();

    expect(store.errorsAt([0]).map((e) => e.code)).toEqual(['InvalidValue']);
    expect(store.errorsAt([1]).map((e) => e.code)).toEqual([]);              // the child's error is not the group's
    expect(store.errorsAt([1, 0]).map((e) => e.code)).toEqual(['UnknownField']);
    expect(store.dirty()).toBe(true);
  });

  it('shows a duplicate name on the name field and clears it when the name changes', async () => {
    draftMinors();
    saveResult = () => throwError(() => new ApiError(409, 'שם הבקרה כבר קיים.', "כבר קיימת בקרה בשם 'קטינים'."));

    await store.save();
    expect(store.fieldErrors()['name']).toEqual(["כבר קיימת בקרה בשם 'קטינים'."]);

    store.setName('קטינים 2');
    expect(store.fieldErrors()['name']).toBeUndefined();
  });

  it('shows an unexpected server error as one form-level message', async () => {
    draftMinors();
    saveResult = () => throwError(() => new ApiError(500, 'אירעה שגיאה בלתי צפויה.', 'הפעולה לא הושלמה.'));

    await store.save();

    expect(store.fieldErrors()['form']).toEqual(['הפעולה לא הושלמה.']);
  });

  it('changing what to check starts an empty condition tree; choosing the same keeps it', () => {
    draftMinors();

    store.setRootEntity('employee');
    expect(store.conditionCount()).toBe(1);

    store.setRootEntity('department');
    expect(store.conditionCount()).toBe(0);
    expect(store.rootEntity()).toBe('department');
  });

  it('checks the definition live after a pause in editing, and only the latest edit counts', () => {
    draftMinors();
    expect(store.check()).toBe('valid');
    expect(store.meaning()).toBe('גיל קטן מ-18');
    const before = checked.length;

    store.apply({ kind: 'update', path: [0], node: { ...age, operator: 'gt' } });
    store.apply({ kind: 'update', path: [0], node: { ...age, operator: 'lte' } });
    expect(store.check()).toBe('checking');
    pause();

    expect(checked.length).toBe(before + 1);                      // one request for the burst of edits
    expect(checked.at(-1)?.filter.children[0]).toMatchObject({ operator: 'lte' });
  });

  it('shows what is wrong and blocks saving while the definition is invalid', () => {
    draftMinors();
    checkResult = () => of({ isValid: false, description: null, errors: [
      { path: 'filter.children[0].operand', code: 'MissingValue', message: 'חסר ערך להשוואה.', isIncomplete: false },
    ] });

    store.apply({ kind: 'update', path: [0], node: { type: 'condition', field: 'age', operator: 'lt' } });
    pause();

    expect(store.check()).toBe('invalid');
    expect(store.meaning()).toBeNull();
    expect(store.errorsAt([0]).map((e) => e.message)).toEqual(['חסר ערך להשוואה.']);
    expect(store.canSave()).toBe(false);
  });

  it('a slow answer about an older definition never overwrites the newer one', () => {
    draftMinors();
    const slow = new Subject<ValidateResponse>();
    checkResult = () => slow;
    store.apply({ kind: 'update', path: [0], node: { ...age, operator: 'gt' } });
    pause();                                                      // request for "gt" is in flight

    checkResult = () => of({ isValid: true, errors: [], description: 'גיל קטן או שווה ל-18' });
    store.apply({ kind: 'update', path: [0], node: { ...age, operator: 'lte' } });
    pause();
    slow.next({ isValid: true, errors: [], description: 'גיל גדול מ-18' });

    expect(store.meaning()).toBe('גיל קטן או שווה ל-18');
  });

  it('an empty tree is not checked; an unreachable server does not block saving', () => {
    store.setName('קטינים');
    store.setRootEntity('employee');
    pause();
    expect(checked).toEqual([]);
    expect(store.check()).toBe('idle');

    checkResult = () => throwError(() => new Error('network'));
    store.apply({ kind: 'add', groupPath: [], node: age });
    pause();

    expect(store.check()).toBe('unavailable');
    expect(store.canSave()).toBe(true);                           // the server validates again on save
  });

  it('a loaded control shows its saved meaning at once', async () => {
    await store.load(7);
    expect(store.meaning()).toBe('גיל קטן מ-18');
  });

  it('refuses to add a condition before choosing what to check', () => {
    expect(() => store.apply({ kind: 'add', groupPath: [], node: age })).toThrow();
  });
});
