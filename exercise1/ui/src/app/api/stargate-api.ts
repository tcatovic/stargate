import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, map, of, throwError } from 'rxjs';

import {
  CreatePersonOutcome,
  CreatePersonRequest,
  CreatePersonResult,
  GetAstronautDutiesByNameResult,
  GetPeopleResult,
  GetPersonByNameResult,
  PersonAstronaut,
  PersonFilters,
  UpdatePersonOutcome,
  UpdatePersonRequest,
  UpdatePersonResult,
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
          error instanceof HttpErrorResponse && error.status === 404
            ? of(null)
            : throwError(() => error),
        ),
      );
  }

  getDuties(name: string): Observable<GetAstronautDutiesByNameResult> {
    return this.http.get<GetAstronautDutiesByNameResult>(
      `${this.baseUrl}/AstronautDuty/${encodeURIComponent(name)}`,
    );
  }

  /**
   * Renames a person. The name is sent unmodified (the server trims). 404, 409 and 400 are emitted as
   * outcomes; a 200 without a person errors; every other failure propagates as the HttpErrorResponse.
   */
  updatePerson(personId: number, name: string): Observable<UpdatePersonOutcome> {
    const body: UpdatePersonRequest = { name };
    return this.http
      .put<UpdatePersonResult>(
        `${this.baseUrl}/Person/${encodeURIComponent(String(personId))}`,
        body,
      )
      .pipe(
        map((result): UpdatePersonOutcome => {
          if (!result.person) {
            throw new Error('Malformed update response');
          }
          return { status: 'updated', person: result.person };
        }),
        catchError((error: unknown) => {
          if (error instanceof HttpErrorResponse) {
            switch (error.status) {
              case 404:
                return of<UpdatePersonOutcome>({ status: 'notFound' });
              case 409:
                return of<UpdatePersonOutcome>({ status: 'conflict' });
              case 400:
                return of<UpdatePersonOutcome>({
                  status: 'invalid',
                  errors: extractErrors(error.error),
                });
            }
          }
          return throwError(() => error);
        }),
      );
  }

  /**
   * Creates a person. POSTs `{ name }` unmodified (no trim, no client-side validation; an empty name is still sent).
   * 201 with an integer id >= 1 emits created; 409 and 400 are emitted as outcomes; a 201 without a valid id errors
   * with 'Malformed create response'; every other failure propagates as the HttpErrorResponse.
   */
  createPerson(name: string): Observable<CreatePersonOutcome> {
    const body: CreatePersonRequest = { name };
    return this.http.post<CreatePersonResult>(`${this.baseUrl}/Person`, body).pipe(
      map((result): CreatePersonOutcome => {
        const id: unknown = result?.id;
        if (typeof id !== 'number' || !Number.isInteger(id) || id < 1) {
          throw new Error('Malformed create response');
        }
        return { status: 'created', id };
      }),
      catchError((error: unknown) => {
        if (error instanceof HttpErrorResponse) {
          switch (error.status) {
            case 409:
              return of<CreatePersonOutcome>({ status: 'conflict' });
            case 400:
              return of<CreatePersonOutcome>({
                status: 'invalid',
                errors: extractErrors(error.error),
              });
          }
        }
        return throwError(() => error);
      }),
    );
  }
}

function extractErrors(body: unknown): Record<string, string[]> {
  if (typeof body === 'object' && body !== null && 'errors' in body) {
    const errors = (body as { errors: unknown }).errors;
    if (typeof errors === 'object' && errors !== null) {
      return errors as Record<string, string[]>;
    }
  }
  return {};
}
