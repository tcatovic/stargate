/**
 * Contract for task 001: exact types the UI must use. `exercise1/ui/src/app/api/models.ts` must declare the
 * interfaces below with identical names, fields, types and nullability (StargateApiContract documents the
 * StargateApi method signatures; it is not copied into models.ts).
 */
import { Observable } from 'rxjs';

/** Unchanged. `responseCode` always equals the HTTP status. */
export interface BaseResponse {
  success: boolean;
  message: string;
  responseCode: number;
}

/** Unchanged. Unassigned people have currentRank === '' and currentDutyTitle === '' (never null) and null dates. */
export interface PersonAstronaut {
  personId: number;
  name: string;
  currentRank: string;
  currentDutyTitle: string;
  careerStartDate: string | null;
  careerEndDate: string | null;
}

/** Unchanged. */
export interface AstronautDuty {
  id: number;
  personId: number;
  rank: string;
  dutyTitle: string;
  dutyStartDate: string;
  dutyEndDate: string | null;
}

/** Unchanged. 200 only; `people` may be empty. Order unspecified. */
export interface GetPeopleResult extends BaseResponse {
  people: PersonAstronaut[];
}

/** Unchanged. 200 body: person non-null. 404 body: success false, message 'Person not found', responseCode 404, person null. */
export interface GetPersonByNameResult extends BaseResponse {
  person: PersonAstronaut | null;
}

/** Unchanged (endpoint out of scope). */
export interface GetAstronautDutiesByNameResult extends BaseResponse {
  person: PersonAstronaut | null;
  astronautDuties: AstronautDuty[];
}

/**
 * NEW. Optional filters for GET /Person. Each value is trimmed; a missing, empty or whitespace-only value is not sent.
 * Server bounds: at most 100 characters of letters, digits, space, hyphen, apostrophe, period (else HTTP 400).
 */
export interface PersonFilters {
  name?: string;
  rank?: string;
  dutyTitle?: string;
}

/** NEW. Body of every HTTP 400 from GET /Person and GET /Person/{name} (available as HttpErrorResponse.error). */
export interface ValidationProblemDetails {
  type: string;
  title: string;
  status: number;
  /** Keys are parameter names: 'name' | 'rank' | 'dutyTitle'. Messages are for display only. */
  errors: Record<string, string[]>;
  traceId: string;
}

/** Signatures `StargateApi` must expose after this task. */
export interface StargateApiContract {
  /** Unchanged behaviour: GET /api/Person with no query string. Implemented as listPeople(). */
  getPeople(): Observable<GetPeopleResult>;

  /**
   * NEW. GET /api/Person with HttpParams built from `filters`: keys exactly 'name', 'rank', 'dutyTitle';
   * values trimmed; keys with missing/empty/whitespace-only values omitted (no filters => URL is exactly '/api/Person').
   * Default HttpUrlEncodingCodec. Emits the 200 body; errors with HttpErrorResponse on any non-2xx (400 body is
   * ValidationProblemDetails).
   */
  listPeople(filters?: PersonFilters): Observable<GetPeopleResult>;

  /**
   * CHANGED (was Observable<GetPersonByNameResult>). Trims `name`; if the result is empty, emits null and completes
   * WITHOUT an HTTP request. Otherwise GET /api/Person/${encodeURIComponent(trimmedName)}.
   * 200 => emits body.person; 404 => emits null and completes (no error); any other failure (400, 500, status 0)
   * => errors with the HttpErrorResponse unchanged.
   */
  getPerson(name: string): Observable<PersonAstronaut | null>;

  /** Unchanged (out of scope). Note: the API now answers 404 for an unknown name here; no component uses it. */
  getDuties(name: string): Observable<GetAstronautDutiesByNameResult>;
}
