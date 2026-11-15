import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { Observable, of } from 'rxjs';
import { AiApi } from '../../core/api/ai-api.service';
import { ControlsApi } from '../../core/api/controls-api.service';
import { AiSuggestionResult } from '../../core/models/ai.models';
import { Control } from '../../core/models/control.models';
import { NotifyService } from '../../core/services/notify.service';
import { VALID_SUGGESTION as VALID } from '../../../testing/ai-fixtures';
import { loadTestCatalog, provideTestCatalogApi } from '../../../testing/test-catalog';
import { AiAssistantPage } from './ai-assistant.page';
import { AiExample } from '../../core/models/ai.models';


/** As the AI service serves them (GET /api/ai/examples). */
const SERVED_EXAMPLES: AiExample[] = [
  { label: 'קטינים עם שכר גבוה', text: 'עובדים מתחת לגיל 18 עם שכר גבוה' },
  { label: 'דוגמה שנייה', text: 'טקסט שני' },
];

describe('AiAssistantPage', () => {
  let fixture: ComponentFixture<AiAssistantPage>;
  let el: HTMLElement;
  let answer: () => Observable<AiSuggestionResult>;
  let asked: string[];
  let navigations: unknown[][];

  beforeEach(async () => {
    answer = () => of(VALID);
    asked = [];
    navigations = [];
    TestBed.configureTestingModule({
      imports: [AiAssistantPage],
      providers: [
        provideRouter([]),
        provideTestCatalogApi,
        {
          provide: AiApi,
          useValue: {
            examples: () => of(SERVED_EXAMPLES),
            suggestControl: (text: string) => {
              asked.push(text);
              return answer();
            },
          },
        },
        { provide: ControlsApi, useValue: { create: () => of({ id: 9, name: 'קטינים עם שכר גבוה' } as Control) } },
        { provide: NotifyService, useValue: { success: () => undefined, error: () => undefined } },
      ],
    });
    await loadTestCatalog();
    const router = TestBed.inject(Router);
    vi.spyOn(router, 'navigate').mockImplementation((...args: unknown[]) => {
      navigations.push(args);
      return Promise.resolve(true);
    });
    fixture = TestBed.createComponent(AiAssistantPage);
    fixture.detectChanges();
    el = fixture.nativeElement as HTMLElement;
    await fixture.whenStable();   // the examples arrive from the server
    fixture.detectChanges();
  });

  async function settle(): Promise<void> {
    await fixture.whenStable();
    fixture.detectChanges();
  }

  const buttons = () => Array.from(el.querySelectorAll<HTMLButtonElement>('.actions button'));
  const button = (text: string) => buttons().find((b) => b.textContent?.includes(text))!;

  it("shows the AI's own examples; one fills the box and asks", async () => {
    expect(Array.from(el.querySelectorAll('mat-chip')).map((c) => c.textContent?.trim())).toEqual(SERVED_EXAMPLES.map((e) => e.label));

    (el.querySelector('mat-chip') as HTMLElement).click();
    await settle();

    expect(asked).toEqual([SERVED_EXAMPLES[0].text]);
    expect((el.querySelector('textarea') as HTMLTextAreaElement).value).toBe(SERVED_EXAMPLES[0].text);
  });

  it('shows the suggestion for approval: understanding, assumptions, meaning and read-only conditions', async () => {
    (el.querySelector('mat-chip') as HTMLElement).click();
    await settle();

    expect(el.querySelector('.understanding')?.textContent).toContain('חיפשתי עובדים שגילם קטן מ-18.');
    expect(el.querySelector('.understanding li')?.textContent).toBe("'משכורת' = שכר חודשי");
    expect(el.querySelector('app-definition-description .sentence')?.textContent).toBe('גיל קטן מ-18');
    expect(el.querySelector('.conditions h3')?.textContent).toBe('בודקים: עובדים');
    expect(el.querySelectorAll('.conditions p.text').length).toBe(1);
    expect(el.querySelector('.conditions app-condition-row input, .conditions footer')).toBeNull();   // read-only
    expect(buttons().map((b) => b.disabled)).toEqual([false, false, false]);
  });

  it('an invalid suggestion shows what is wrong and can only be opened in the editor', async () => {
    answer = () => of({ ...VALID, isValid: false, definitionDescription: null,
      validationErrors: [{ path: 'filter.children[0].field', code: 'UnknownField', message: "לעובד אין שדה בשם 'bonus'.", isIncomplete: false }] });
    (el.querySelector('mat-chip') as HTMLElement).click();
    await settle();

    expect(el.querySelector('.invalid-note')?.textContent).toContain('ההצעה אינה תקינה');
    expect(el.querySelector('.conditions app-condition-row .error')?.textContent).toContain("לעובד אין שדה בשם 'bonus'.");
    expect(button('עריכה בעורך').disabled).toBe(false);
    expect(button('אישור ושמירה').disabled).toBe(true);
    expect(button('אישור, שמירה והרצה').disabled).toBe(true);
  });

  it('a request that was not understood shows the words it could not place and offers the manual editor', async () => {
    answer = () => of({ ...VALID, understood: false, definition: null, isValid: false, definitionDescription: null,
      explanation: 'לא הצלחתי להבין את הבקשה.', unmappedTerms: ['מזג', 'האוויר'] });
    (el.querySelector('mat-chip') as HTMLElement).click();
    await settle();

    expect(el.querySelector('app-empty-state h2')?.textContent).toBe('לא הצלחתי להבין את הבקשה');
    expect(Array.from(el.querySelectorAll('app-empty-state mat-chip')).map((c) => c.textContent?.trim())).toEqual(['מזג', 'האוויר']);
    expect(el.querySelector('app-empty-state a')?.getAttribute('href')).toBe('/controls/new');
    expect(el.querySelector('.review')).toBeNull();
  });

  it('"עריכה בעורך" opens the editor with the suggestion as an unsaved draft', async () => {
    (el.querySelector('mat-chip') as HTMLElement).click();
    await settle();

    button('עריכה בעורך').click();

    expect(navigations).toEqual([[['/controls/new'], { state: { draft: {
      name: 'קטינים עם שכר גבוה', description: 'עובדים מתחת לגיל 18', definition: VALID.definition } } }]]);
  });

  it('"אישור, שמירה והרצה" saves and runs the new control', async () => {
    (el.querySelector('mat-chip') as HTMLElement).click();
    await settle();

    button('אישור, שמירה והרצה').click();
    await settle();

    expect(navigations).toEqual([[['/controls', 9, 'results'], { queryParams: { run: true } }]]);
  });
});
