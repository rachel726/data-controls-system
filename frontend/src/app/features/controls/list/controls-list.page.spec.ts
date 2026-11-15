import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { Observable, of, throwError } from 'rxjs';
import { ControlsApi } from '../../../core/api/controls-api.service';
import { ControlListItem } from '../../../core/models/control.models';
import { NotifyService } from '../../../core/services/notify.service';
import { ConfirmService } from '../../../shared/components/confirm-dialog/confirm-dialog';
import { ControlsListPage } from './controls-list.page';

const CONTROLS: ControlListItem[] = [
  {
    id: 1, name: 'עובדים מתחת לגיל 18 עם שכר גבוה', description: 'חשד לטעות', rootEntityKey: 'employee', rootEntityName: 'עובדים',
    updatedAt: '2026-11-12T10:00:00Z', lastRun: { runAt: '2026-11-12T10:05:00Z', status: 'succeeded', violationCount: 3, error: null },
  },
  {
    id: 2, name: 'מחלקות פעילות ללא עובדים פעילים', description: null, rootEntityKey: 'department', rootEntityName: 'מחלקות',
    updatedAt: '2026-11-12T10:00:00Z', lastRun: null,
  },
  {
    id: 3, name: 'פרויקטים', description: null, rootEntityKey: 'project', rootEntityName: 'פרויקטים',
    updatedAt: '2026-11-12T10:00:00Z', lastRun: { runAt: '2026-11-12T10:06:00Z', status: 'failed', violationCount: null, error: 'הרצת הבקרה ארכה זמן רב מדי.' },
  },
];

describe('ControlsListPage', () => {
  let fixture: ComponentFixture<ControlsListPage>;
  let listCalls: string[];
  let listResult: () => Observable<ControlListItem[]>;
  let deleted: number[];
  let confirmAnswer: boolean;
  let successes: string[];

  beforeEach(() => {
    vi.useFakeTimers();
    listCalls = [];
    listResult = () => of(CONTROLS);
    deleted = [];
    confirmAnswer = true;
    successes = [];

    TestBed.configureTestingModule({
      imports: [ControlsListPage],
      providers: [
        provideRouter([]),
        {
          provide: ControlsApi,
          useValue: {
            list: (search?: string) => {
              listCalls.push(search ?? '');
              return listResult();
            },
            delete: (id: number) => {
              deleted.push(id);
              return of(undefined);
            },
          },
        },
        { provide: ConfirmService, useValue: { ask: () => Promise.resolve(confirmAnswer) } },
        { provide: NotifyService, useValue: { success: (m: string) => successes.push(m), error: () => undefined } },
      ],
    });
  });

  afterEach(() => vi.useRealTimers());

  async function render(): Promise<HTMLElement> {
    fixture = TestBed.createComponent(ControlsListPage);
    fixture.detectChanges();
    await vi.advanceTimersByTimeAsync(400);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  const cards = (el: HTMLElement) => Array.from(el.querySelectorAll('li.card'));

  it('lists the controls with the outcome of their last run', async () => {
    const el = await render();

    expect(cards(el).length).toBe(3);
    expect(el.querySelector('h1')?.textContent).toBe('הבקרות שלי');
    expect(el.querySelector('.subtitle')?.textContent).toBe('3 בקרות');
    expect(cards(el)[0].querySelector('.badge.violations')?.textContent).toContain('3 חריגות');
    expect(cards(el)[1].querySelector('.badge.never')?.textContent).toContain('טרם הורצה');
    expect(cards(el)[2].querySelector('.badge.failed')?.textContent).toContain('הריצה האחרונה נכשלה');
    expect(cards(el)[0].querySelector('.entity')?.textContent).toBe('עובדים');
  });

  it('searches on the server after the user stops typing', async () => {
    const el = await render();
    const input = el.querySelector('input') as HTMLInputElement;

    for (const text of ['כ', 'כה', 'כהן']) {
      input.value = text;
      input.dispatchEvent(new Event('input'));
      await vi.advanceTimersByTimeAsync(100);
    }
    await vi.advanceTimersByTimeAsync(400);

    expect(listCalls).toEqual(['', 'כהן']);   // one request for the final text, none per keystroke
  });

  it('deletes a control only after confirmation', async () => {
    const el = await render();

    (cards(el)[1].querySelector('button[aria-label="מחיקה"]') as HTMLButtonElement).click();
    await vi.advanceTimersByTimeAsync(0);
    fixture.detectChanges();

    expect(deleted).toEqual([2]);
    expect(cards(el).length).toBe(2);
    expect(successes[0]).toContain('נמחקה');
  });

  it('keeps the control when the user cancels', async () => {
    confirmAnswer = false;
    const el = await render();

    (cards(el)[0].querySelector('button[aria-label="מחיקה"]') as HTMLButtonElement).click();
    await vi.advanceTimersByTimeAsync(0);
    fixture.detectChanges();

    expect(deleted).toEqual([]);
    expect(cards(el).length).toBe(3);
  });

  it('opens the results screen to run a control', async () => {
    const el = await render();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    (cards(el)[0].querySelector('.actions button') as HTMLButtonElement).click();

    expect(navigate).toHaveBeenCalledWith(['/controls', 1, 'results'], { queryParams: { run: true } });
  });

  it('a search without matches says so in place of the cards, and the header keeps its line', async () => {
    const el = await render();
    expect(el.querySelector('.subtitle')?.textContent).toBe('3 בקרות');

    listResult = () => of([]);
    const input = el.querySelector('.search input') as HTMLInputElement;
    input.value = 'zzz';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    await vi.advanceTimersByTimeAsync(400);
    fixture.detectChanges();

    expect(el.querySelector('.results app-empty-state h2')?.textContent).toBe('לא נמצאו בקרות');
    expect(el.querySelector('.cards')).toBeNull();
    expect(el.querySelector('.subtitle')?.textContent).toBe('0 בקרות');
  });

  it('counts one control in proper Hebrew', async () => {
    listResult = () => of([CONTROLS[0]]);
    const el = await render();

    expect(el.querySelector('.subtitle')?.textContent).toBe('בקרה אחת');
  });

  it('invites the user to create the first control when there are none', async () => {
    listResult = () => of([]);
    const el = await render();

    expect(el.querySelector('app-empty-state h2')?.textContent).toBe('עדיין אין בקרות');
    expect(el.querySelector('app-empty-state a[href="/controls/new"]')).not.toBeNull();
  });

  it('shows a retry option when the list cannot be loaded', async () => {
    listResult = () => throwError(() => new Error('down'));
    const el = await render();

    expect(el.querySelector('app-empty-state h2')?.textContent).toBe('לא ניתן לטעון את הבקרות');

    listResult = () => of(CONTROLS);
    (el.querySelector('app-empty-state button') as HTMLButtonElement).click();
    await vi.advanceTimersByTimeAsync(400);
    fixture.detectChanges();

    expect(cards(el).length).toBe(3);
    expect(el.querySelector('app-empty-state')).toBeNull();
  });
});
