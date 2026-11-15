import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { EntityDto, LimitsDto, OperatorDto } from '../models/catalog.models';

@Injectable({ providedIn: 'root' })
export class CatalogApi {
  private readonly http = inject(HttpClient);

  entities(): Observable<EntityDto[]> {
    return this.http.get<EntityDto[]>('/api/catalog/entities');
  }

  operators(): Observable<OperatorDto[]> {
    return this.http.get<OperatorDto[]>('/api/catalog/operators');
  }

  limits(): Observable<LimitsDto> {
    return this.http.get<LimitsDto>('/api/catalog/limits');
  }
}
