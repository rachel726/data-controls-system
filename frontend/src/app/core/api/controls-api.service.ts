import { HttpClient, HttpContext, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ControlDefinition } from '../models/control-definition';
import { Control, ControlListItem, ResultsPage, ResultsRequest, RunResult, SaveControlRequest, ValidateResponse } from '../models/control.models';
import { SILENT_ERRORS } from './api-error';

/** REST client for /api/controls. Errors arrive as ApiError (see errorInterceptor). */
@Injectable({ providedIn: 'root' })
export class ControlsApi {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/controls';

  list(search?: string): Observable<ControlListItem[]> {
    const params = search?.trim() ? new HttpParams().set('search', search.trim()) : undefined;
    return this.http.get<ControlListItem[]>(this.base, { params });
  }

  /** The editor and results pages show a load failure in place, so it is not also shown as a message. */
  get(id: number): Observable<Control> {
    return this.http.get<Control>(`${this.base}/${id}`, { context: silent() });
  }

  /** Save errors (invalid name / definition) are shown inline by the editor, so they are silent here. */
  create(request: SaveControlRequest): Observable<Control> {
    return this.http.post<Control>(this.base, request, { context: silent() });
  }

  update(id: number, request: SaveControlRequest): Observable<Control> {
    return this.http.put<Control>(`${this.base}/${id}`, request, { context: silent() });
  }

  delete(id: number): Observable<void> {
    return this.http.delete<void>(`${this.base}/${id}`);
  }

  /** Live check while editing - errors are part of the response, transport problems stay silent. */
  validate(definition: ControlDefinition): Observable<ValidateResponse> {
    return this.http.post<ValidateResponse>(`${this.base}/validate`, definition, { context: silent() });
  }

  /** Run and results errors are shown on the results page itself. */
  run(id: number): Observable<RunResult> {
    return this.http.post<RunResult>(`${this.base}/${id}/run`, null, { context: silent() });
  }

  results(id: number, request: ResultsRequest): Observable<ResultsPage> {
    return this.http.post<ResultsPage>(`${this.base}/${id}/results`, request, { context: silent() });
  }
}

function silent(): HttpContext {
  return new HttpContext().set(SILENT_ERRORS, true);
}
