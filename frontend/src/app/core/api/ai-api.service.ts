import { HttpClient, HttpContext } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { AiExample, AiSuggestionResult } from '../models/ai.models';
import { SILENT_ERRORS } from './api-error';

/** REST client for /api/ai. Errors arrive as ApiError and are shown by the AI page itself. */
@Injectable({ providedIn: 'root' })
export class AiApi {
  private readonly http = inject(HttpClient);

  /** The AI service's own examples - the UI holds no request text of its own. */
  examples(): Observable<AiExample[]> {
    return this.http.get<AiExample[]>('/api/ai/examples', { context: new HttpContext().set(SILENT_ERRORS, true) });
  }

  suggestControl(text: string): Observable<AiSuggestionResult> {
    return this.http.post<AiSuggestionResult>('/api/ai/suggest-control', { text }, {
      context: new HttpContext().set(SILENT_ERRORS, true),
    });
  }
}
