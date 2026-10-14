import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, map, of, throwError } from 'rxjs';

import {
  GetAstronautDutiesByNameResult,
  GetPeopleResult,
  GetPersonByNameResult,
  PersonAstronaut,
  PersonFilters,
} from './models';

const FILTER_KEYS = ['name', 'rank', 'dutyTitle'] as const;

/** Typed client for the Stargate API. Requests go through the `/api` dev proxy (see proxy.conf.json). */
@Injectable({ providedIn: 'root' })
export class StargateApi {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api';

  getPeople(): Observable<GetPeopleResult> {
    return this.listPeople();
  }

  listPeople(filters: PersonFilters = {}): Observable<GetPeopleResult> {
    let params = new HttpParams();
    for (const key of FILTER_KEYS) {
      const value = filters[key]?.trim();
      if (value) {
        params = params.set(key, value);
      }
    }
    return this.http.get<GetPeopleResult>(`${this.baseUrl}/Person`, { params });
  }

  /** Emits null for an unknown person (HTTP 404) or a blank name (no request is made). Other errors propagate. */
  getPerson(name: string): Observable<PersonAstronaut | null> {
    const trimmed = name.trim();
    if (!trimmed) {
      return of(null);
    }
    return this.http
      .get<GetPersonByNameResult>(`${this.baseUrl}/Person/${encodeURIComponent(trimmed)}`)
      .pipe(
        map((result) => result.person),
        catchError((error: unknown) =>
          error instanceof HttpErrorResponse && error.status === 404 ? of(null) : throwError(() => error),
        ),
      );
  }

  getDuties(name: string): Observable<GetAstronautDutiesByNameResult> {
    return this.http.get<GetAstronautDutiesByNameResult>(
      `${this.baseUrl}/AstronautDuty/${encodeURIComponent(name)}`,
    );
  }
}
