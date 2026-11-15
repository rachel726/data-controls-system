import { HttpClient, HttpContext, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { NotifyService } from '../services/notify.service';
import { ApiError, SILENT_ERRORS } from './api-error';
import { errorInterceptor } from './error.interceptor';

describe('errorInterceptor', () => {
  let http: HttpClient;
  let backend: HttpTestingController;
  const shown: string[] = [];

  beforeEach(() => {
    shown.length = 0;
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([errorInterceptor])),
        provideHttpClientTesting(),
        { provide: NotifyService, useValue: { error: (m: string) => shown.push(m), success: () => undefined } },
      ],
    });
    http = TestBed.inject(HttpClient);
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => backend.verify());

  it('shows the server Hebrew message and rethrows a normalized ApiError', async () => {
    const result = firstValueFrom(http.get('/api/controls/1')).catch((e: unknown) => e);
    backend.expectOne('/api/controls/1').flush(
      { title: 'לא נמצא.', detail: 'הבקרה המבוקשת (מספר 1) לא נמצאה.', status: 404, traceId: 't-1' },
      { status: 404, statusText: 'Not Found' },
    );
    const error = (await result) as ApiError;

    expect(error).toBeInstanceOf(ApiError);
    expect(error.status).toBe(404);
    expect(error.message).toBe('הבקרה המבוקשת (מספר 1) לא נמצאה.');
    expect(error.traceId).toBe('t-1');
    expect(shown).toEqual(['הבקרה המבוקשת (מספר 1) לא נמצאה.']);
  });

  it('keeps field and definition errors for the screen to show inline, silently', async () => {
    const result = firstValueFrom(http.post('/api/controls', {}, { context: new HttpContext().set(SILENT_ERRORS, true) })).catch((e: unknown) => e);
    backend.expectOne('/api/controls').flush(
      {
        title: 'הגדרת הבקרה אינה תקינה.', detail: 'הגדרת הבקרה אינה תקינה.', status: 400,
        errors: { name: ['יש להזין שם לבקרה.'] },
        definitionErrors: [{ path: 'filter.children[0].field', code: 'UnknownField', message: 'לעובד אין שדה בשם x', isIncomplete: false }],
      },
      { status: 400, statusText: 'Bad Request' },
    );
    const error = (await result) as ApiError;

    expect(shown).toEqual([]);
    expect(error.fieldErrors['name']).toEqual(['יש להזין שם לבקרה.']);
    expect(error.definitionErrors[0].path).toBe('filter.children[0].field');
  });

  it('explains a missing server connection in Hebrew', async () => {
    const result = firstValueFrom(http.get('/api/controls')).catch((e: unknown) => e);
    backend.expectOne('/api/controls').error(new ProgressEvent('error'), { status: 0 });
    const error = (await result) as ApiError;

    expect(error.status).toBe(0);
    expect(error.message).toContain('לא ניתן להתחבר לשרת');
    expect(shown.length).toBe(1);
  });

  it('falls back to a generic Hebrew message when the body is not ProblemDetails', async () => {
    const result = firstValueFrom(http.get('/api/controls')).catch((e: unknown) => e);
    backend.expectOne('/api/controls').flush('<html>gateway</html>', { status: 502, statusText: 'Bad Gateway' });
    const error = (await result) as ApiError;

    expect(error.message).toBe('הפעולה נכשלה. נסו שוב.');
  });

  it('does not touch successful responses', async () => {
    const result = firstValueFrom(http.get<{ ok: boolean }>('/api/controls'));
    backend.expectOne('/api/controls').flush({ ok: true });

    expect(await result).toEqual({ ok: true });
    expect(shown).toEqual([]);
  });
});
