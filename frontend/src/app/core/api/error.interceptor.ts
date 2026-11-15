import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { NotifyService } from '../services/notify.service';
import { ApiError, SILENT_ERRORS } from './api-error';

/**
 * One place for API errors: every failure becomes an ApiError (Hebrew title/message from the server's
 * ProblemDetails) and, unless the request is marked SILENT_ERRORS, is shown to the user.
 */
export const errorInterceptor: HttpInterceptorFn = (request, next) => {
  const notify = inject(NotifyService);

  return next(request).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse)) {
        return throwError(() => error);
      }

      const apiError = ApiError.from(error);
      if (!request.context.get(SILENT_ERRORS)) {
        notify.error(apiError.message);
      }

      return throwError(() => apiError);
    }),
  );
};
