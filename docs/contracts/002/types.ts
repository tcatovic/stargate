/**
 * Contract for task 002: exact types the UI must use. `exercise1/ui/src/app/api/models.ts` must declare
 * UpdatePersonRequest, UpdatePersonResult and UpdatePersonOutcome with identical names, fields, types and
 * nullability. Existing interfaces (BaseResponse, PersonAstronaut, ValidationProblemDetails, ...) are unchanged and
 * are repeated here only for reference. StargateApiContract documents the new method; it is not copied into models.ts.
 */
import { Observable } from 'rxjs';

/** Unchanged. `responseCode` always equals the HTTP status. */
export interface BaseResponse {
  success: boolean;
  message: string;
  responseCode: number;
}

/** Unchanged. */
export interface PersonAstronaut {
  personId: number;
  name: string;
  currentRank: string;
  currentDutyTitle: string;
  careerStartDate: string | null;
  careerEndDate: string | null;
}

/** Unchanged shape. Doc comment in models.ts becomes: "Body of every HTTP 400 from GET /Person, GET /Person/{name} and PUT /Person/{personId}." */
export interface ValidationProblemDetails {
  type: string;
  title: string;
  status: number;
  /** Keys identify the failing input; clients must not branch on them. Messages are for display only. */
  errors: Record<string, string[]>;
  traceId: string;
}

/** NEW. Body of PUT /Person/{personId}. The name is sent exactly as given; the server trims and NFC-normalises. */
export interface UpdatePersonRequest {
  name: string;
}

/** NEW. Body of PUT /Person/{personId} for 200, 404 and 409. `person` is non-null on 200, null on 404 and 409. */
export interface UpdatePersonResult extends BaseResponse {
  person: PersonAstronaut | null;
}

/** NEW. What StargateApi.updatePerson emits. Exactly one value, then completes. */
export type UpdatePersonOutcome =
  | { status: 'updated'; person: PersonAstronaut }
  | { status: 'notFound' }
  | { status: 'conflict' }
  | { status: 'invalid'; errors: Record<string, string[]> };

/** Method added to StargateApi (exercise1/ui/src/app/api/stargate-api.ts). */
export interface StargateApiContract {
  /**
   * PUT `/api/Person/${personId}` with JSON body `{ name }` (UpdatePersonRequest), name passed through unmodified.
   * No client-side validation, no request is skipped.
   * - 200 with non-null `person` -> `{ status: 'updated', person }`.
   * - 200 with `person === null`  -> the observable errors with `new Error('Malformed update response')`.
   * - 404 -> `{ status: 'notFound' }`.
   * - 409 -> `{ status: 'conflict' }`.
   * - 400 -> `{ status: 'invalid', errors }`, where `errors` is the body's `errors` object when the body is an object
   *   whose `errors` is a non-null object, otherwise `{}`.
   * - Any other status (0, 415, 500, ...) -> the HttpErrorResponse propagates unchanged.
   */
  updatePerson(personId: number, name: string): Observable<UpdatePersonOutcome>;
}
