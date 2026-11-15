import { TestBed } from '@angular/core/testing';
import { Observable, Subject, of, throwError } from 'rxjs';
import { AiApi } from '../../core/api/ai-api.service';
import { ApiError } from '../../core/api/api-error';
import { ControlsApi } from '../../core/api/controls-api.service';
import { AiSuggestionResult } from '../../core/models/ai.models';
import { Control, SaveControlRequest } from '../../core/models/control.models';
import { VALID_SUGGESTION as VALID } from '../../../testing/ai-fixtures';
import { AiAssistantStore } from './ai-assistant.store';
import { TEST_LIMITS, loadTestCatalog, provideTestCatalogApi } from '../../../testing/test-catalog';
import { AiExample } from '../../core/models/ai.models';


describe('AiAssistantStore', () => {
  let examples: () => Observable<AiExample[]>;
  let store: AiAssistantStore;
  let asked: string[];
  let answer: (text: string) => Observable<AiSuggestionResult>;
  let created: SaveControlRequest[];
  let createResult: () => Observable<Control>;

  beforeEach(async () => {
    examples = () => of([{ label: 'קטינים', text: 'עובדים מתחת לגיל 18' }]);
    asked = [];
    answer = () => of(VALID);
    created = [];
    createResult = () => of({ id: 9, name: 'קטינים עם שכר גבוה' } as Control);
    TestBed.configureTestingModule({
      providers: [
        AiAssistantStore,
        provideTestCatalogApi,
        {
          provide: AiApi,
          useValue: {
            examples: () => examples(),
            suggestControl: (text: string) => {
              asked.push(text);
              return answer(text);
            },
          },
        },
        {
          provide: ControlsApi,
          useValue: {
            create: (request: SaveControlRequest) => {
              created.push(request);
              return createResult();
            },
          },
        },
      ],
    });
    await loadTestCatalog();   // the limits come from the server, with the catalog
    store = TestBed.inject(AiAssistantStore);
  });

  it("loads the AI's examples; none while the AI is unreachable", async () => {
    await store.loadExamples();
    expect(store.examples()).toEqual([{ label: 'קטינים', text: 'עובדים מתחת לגיל 18' }]);

    examples = () => throwError(() => new ApiError(503, 'שירות ה-AI אינו זמין.', 'שירות ה-AI אינו זמין כרגע.'));
    await store.loadExamples();
    expect(store.examples()).toEqual([]);
  });

  it('asks with the trimmed text and offers the suggestion for approval', async () => {
    store.setText('  עובדים מתחת לגיל 18  ');

    await store.suggest();

    expect(asked).toEqual(['עובדים מתחת לגיל 18']);
    expect(store.result()).toEqual(VALID);
    expect(store.name()).toBe('קטינים עם שכר גבוה');
    expect(store.description()).toBe('עובדים מתחת לגיל 18');
    expect(store.canApprove()).toBe(true);
  });

  it('accepts a request and a name up to the lengths the server accepts', async () => {
    store.setText('א'.repeat(TEST_LIMITS.maxAiTextLength));
    expect(store.canSuggest()).toBe(true);
    store.setText('א'.repeat(TEST_LIMITS.maxAiTextLength + 1));
    expect(store.canSuggest()).toBe(false);

    store.setText('עובדים מתחת לגיל 18');
    await store.suggest();
    store.setName('א'.repeat(TEST_LIMITS.maxNameLength + 1));
    expect(store.canApprove()).toBe(false);
  });

  it('does not ask with an empty text', async () => {
    store.setText('   ');

    await store.suggest();

    expect(asked).toEqual([]);
    expect(store.canSuggest()).toBe(false);
  });

  it('only the latest request may update the screen', async () => {
    const slow = new Subject<AiSuggestionResult>();
    answer = (text) => (text === 'ישן' ? slow : of({ ...VALID, name: 'חדש' }));

    store.setText('ישן');
    const older = store.suggest();
    store.setText('חדש');
    await store.suggest();
    slow.next({ ...VALID, name: 'ישן' });
    slow.complete();
    await older;

    expect(store.name()).toBe('חדש');
    expect(store.suggesting()).toBe(false);
  });

  it('an invalid suggestion cannot be approved', async () => {
    answer = () => of({ ...VALID, isValid: false, definitionDescription: null,
      validationErrors: [{ path: 'filter.children[0].field', code: 'UnknownField', message: "לעובד אין שדה בשם 'bonus'.", isIncomplete: false }] });
    store.setText('דמה שגיאה');

    await store.suggest();

    expect(store.canApprove()).toBe(false);
    expect(await store.approve()).toBeNull();
    expect(created).toEqual([]);
  });

  it('approval saves the suggestion as a regular control, with the name the user may have changed', async () => {
    store.setText('קטינים');
    await store.suggest();
    store.setName('  קטינים - שם שלי  ');
    store.setDescription('');

    const saved = await store.approve();

    expect(saved?.id).toBe(9);
    expect(created).toEqual([{ name: 'קטינים - שם שלי', description: null, definition: VALID.definition }]);
  });

  it('a taken name is shown on the name field and cleared when the name changes', async () => {
    createResult = () => throwError(() => new ApiError(409, 'שם הבקרה כבר קיים.', "כבר קיימת בקרה בשם 'קטינים עם שכר גבוה'."));
    store.setText('קטינים');
    await store.suggest();

    expect(await store.approve()).toBeNull();
    expect(store.nameError()).toBe("כבר קיימת בקרה בשם 'קטינים עם שכר גבוה'.");

    store.setName('שם אחר');
    expect(store.nameError()).toBeNull();
  });

  it('keeps the Hebrew reason when the AI is unavailable', async () => {
    answer = () => throwError(() => new ApiError(503, 'שירות ה-AI אינו זמין.', 'שירות ה-AI אינו זמין כרגע.'));
    store.setText('קטינים');

    await store.suggest();

    expect(store.error()).toBe('שירות ה-AI אינו זמין כרגע.');
    expect(store.result()).toBeNull();
  });

  it('gives the editor the suggestion as an unsaved draft', async () => {
    store.setText('קטינים');
    await store.suggest();

    expect(store.draft()).toEqual({ name: 'קטינים עם שכר גבוה', description: 'עובדים מתחת לגיל 18', definition: VALID.definition });
  });
});
