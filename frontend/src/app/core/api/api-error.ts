import { HttpContextToken, HttpErrorResponse } from '@angular/common/http';
import { DefinitionError } from '../models/control.models';

/**
 * Mark a request whose errors the calling screen shows itself (e.g. next to a form field),
 * so the global interceptor does not also pop a snackbar.
 */
export const SILENT_ERRORS = new HttpContextToken<boolean>(() => false);

/** Every failed API call is normalized to this shape (from RFC 7807 ProblemDetails). */
export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly title: string,
    message: string,
    readonly fieldErrors: Readonly<Record<string, readonly string[]>> = {},
    readonly definitionErrors: readonly DefinitionError[] = [],
    readonly traceId: string | null = null,
  ) {
    super(message);
    this.name = 'ApiError';
  }

  static from(response: HttpErrorResponse): ApiError {
    if (response.status === 0) {
      return new ApiError(0, 'אין חיבור לשרת', 'לא ניתן להתחבר לשרת. בדקו שהשרת פועל ונסו שוב.');
    }

    const problem = isProblem(response.error) ? response.error : null;
    const title = problem?.title ?? 'אירעה שגיאה';
    const message = problem?.detail ?? problem?.title ?? 'הפעולה נכשלה. נסו שוב.';
    return new ApiError(
      response.status,
      title,
      message,
      problem?.errors ?? {},
      problem?.definitionErrors ?? [],
      problem?.traceId ?? null,
    );
  }
}

interface ProblemDetails {
  readonly title?: string;
  readonly detail?: string;
  readonly status?: number;
  readonly traceId?: string;
  readonly errors?: Record<string, readonly string[]>;
  readonly definitionErrors?: readonly DefinitionError[];
}

function isProblem(value: unknown): value is ProblemDetails {
  return typeof value === 'object' && value !== null && ('title' in value || 'detail' in value);
}
