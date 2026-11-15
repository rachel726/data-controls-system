import { Location } from '@angular/common';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { ApiError } from '../../../core/api/api-error';
import { ControlsApi } from '../../../core/api/controls-api.service';
import { Control, DefinitionError, ValidateResponse } from '../../../core/models/control.models';
import { NotifyService } from '../../../core/services/notify.service';
import { ConfirmService } from '../../../shared/components/confirm-dialog/confirm-dialog';
import { provideTestCatalogApi } from '../../../../testing/test-catalog';
import { LIVE_CHECK_DEBOUNCE_MS } from './control-editor.store';
import { ControlEditorPage } from './control-editor.page';

const CONTROL: Control = {
  id: 3, name: 'עובדים פעילים במחלקה לא פעילה', description: 'בדיקה', definitionDescription: 'עובד פעיל וגם מחלקה אינה פעילה',
  definition: {
    schemaVersion: 1, rootEntity: 'employee', filter: { type: 'group', logic: 'and', children: [
      { type: 'condition', field: 'isActive', operator: 'eq', operand: { kind: 'literal', value: true } },
      { type: 'condition', field: 'department.isActive', operator: 'eq', operand: { kind: 'literal', value: false } },
    ] },
  },
  createdAt: '2026-11-12T10:00:00Z', updatedAt: '2026-11-12T10:00:00Z', lastRun: null,
};

describe('ControlEditorPage', () => {
  let fixture: ComponentFixture<ControlEditorPage>;
  let check: ValidateResponse;
  let found: boolean;

  beforeEach(() => {
    check = { isValid: true, errors: [], description: CONTROL.definitionDescription };
    found = true;
    TestBed.configureTestingModule({
      imports: [ControlEditorPage],
      providers: [
        provideRouter([]),
        {
          provide: ControlsApi,
          useValue: {
            get: () => (found ? of(CONTROL) : throwError(() => new ApiError(404, 'לא נמצא.', 'הבקרה המבוקשת (מספר 999) לא נמצאה.'))),
            validate: () => of(check),
          },
        },
        provideTestCatalogApi,
        { provide: ConfirmService, useValue: { ask: () => Promise.resolve(true) } },
        { provide: NotifyService, useValue: { success: () => undefined, error: () => undefined } },
      ],
    });
  });

  async function render(id?: string): Promise<HTMLElement> {
    fixture = TestBed.createComponent(ControlEditorPage);
    if (id) {
      fixture.componentRef.setInput('id', id);
    }
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('starts a new control with saving disabled until it is complete', async () => {
    const el = await render();

    expect(el.querySelector('h1')?.textContent).toBe('בקרה חדשה');
    expect((el.querySelector('input') as HTMLInputElement).value).toBe('');
    const save = Array.from(el.querySelectorAll('app-page-header button')).find((b) => b.textContent?.includes('שמירה')) as HTMLButtonElement;
    expect(save.disabled).toBe(true);
    expect(el.querySelector('.conditions')).toBeNull();   // nothing to condition until "what to check" is chosen
  });

  it('loads an existing control for editing', async () => {
    const el = await render('3');

    expect(el.querySelector('h1')?.textContent).toBe('עריכת בקרה');
    expect((el.querySelector('input') as HTMLInputElement).value).toBe('עובדים פעילים במחלקה לא פעילה');
    expect(el.querySelector('textarea')?.value).toBe('בדיקה');
    expect(el.querySelector('.conditions h2')?.textContent).toContain('אילו עובדים ייחשבו חריגים?');
  });

  it('opens an AI suggestion as a new, unsaved control (draft in the navigation state)', async () => {
    const draft = { name: 'מהצעת AI', description: 'תיאור', definition: CONTROL.definition };
    TestBed.inject(Location).replaceState('/controls/new', '', { draft });

    const el = await render();

    expect(el.querySelector('h1')?.textContent).toBe('בקרה חדשה');
    expect((el.querySelector('input') as HTMLInputElement).value).toBe('מהצעת AI');
    expect(el.querySelectorAll('.conditions app-condition-row').length).toBe(2);
    expect(el.querySelector('.subtitle')?.textContent).toBe('יש שינויים שלא נשמרו');   // not saved until the user saves
  });

  it('a control that cannot be loaded: still "עריכת בקרה", the reason, and nothing to save', async () => {
    found = false;
    const el = await render('999');

    expect(el.querySelector('h1')?.textContent).toBe('עריכת בקרה');
    expect(el.querySelector('app-empty-state')?.textContent).toContain('הבקרה המבוקשת (מספר 999) לא נמצאה.');
    expect(Array.from(el.querySelectorAll('app-page-header button')).some((b) => b.textContent?.includes('שמירה'))).toBe(false);
  });

  it('marks the page as having unsaved changes while typing', async () => {
    const el = await render('3');
    const name = el.querySelector('input') as HTMLInputElement;

    name.value = 'שם חדש';
    name.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    expect(el.querySelector('.subtitle')?.textContent).toBe('יש שינויים שלא נשמרו');
  });

  it('shows the loaded conditions in the builder; changing them is an unsaved change', async () => {
    const el = await render('3');
    expect(el.querySelectorAll('.conditions app-condition-row').length).toBe(2);
    expect(el.querySelector('.subtitle')?.textContent).toBe('כל השינויים נשמרו');   // always a line: no jump on the first change

    el.querySelector<HTMLButtonElement>('[aria-label="הסרת התנאי"]')!.click();
    fixture.detectChanges();

    expect(el.querySelectorAll('.conditions app-condition-row').length).toBe(1);
    expect(el.querySelector('.subtitle')?.textContent).toBe('יש שינויים שלא נשמרו');
  });

  it("shows the control's meaning beside the conditions; a mistake on its condition, an unfinished one without red", async () => {
    vi.useFakeTimers();
    try {
      const el = await render('3');
      expect(el.querySelector('.meaning .sentence')?.textContent).toBe('עובד פעיל וגם מחלקה אינה פעילה');
      const save = () => Array.from(el.querySelectorAll('app-page-header button')).find((b) => b.textContent?.includes('שמירה')) as HTMLButtonElement;
      const edit = (errors: DefinitionError[]) => {
        check = { isValid: false, description: null, errors };
        el.querySelector<HTMLButtonElement>('.add button')!.click();   // any edit triggers the check
        vi.advanceTimersByTime(LIVE_CHECK_DEBOUNCE_MS);
        fixture.detectChanges();
      };

      // a real mistake: on its condition, once; the panel only points at it
      edit([{ path: 'filter.children[0].operand', code: 'InvalidValue', message: 'הערך אינו תקין.', isIncomplete: false }]);
      expect(el.querySelector('app-condition-row .error')?.textContent).toContain('הערך אינו תקין.');
      expect(el.querySelector('app-condition-row .row.invalid')).not.toBeNull();
      expect(el.querySelector('.meaning .placeholder.error')?.textContent).toContain('יש לתקן את ההערות שמסומנות בתנאים');
      expect(el.querySelector('.meaning')?.textContent).not.toContain('הערך אינו תקין.');
      expect(save().disabled).toBe(true);

      // a condition still being filled in: nothing red, a neutral note, no saving yet
      edit([{ path: 'filter.children[2].operand', code: 'OperandMissing', message: 'יש להזין ערך.', isIncomplete: true }]);
      expect(el.querySelector('.invalid, .error')).toBeNull();
      expect(el.querySelector('.meaning .placeholder')?.textContent).toContain('השלימו את התנאים');
      expect(save().disabled).toBe(true);
    } finally {
      vi.useRealTimers();
    }
  });
});
