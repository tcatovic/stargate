/**
 * Contract for task 006 (revision 2): exact types the UI must use. `exercise1/ui/src/app/api/models.ts` must declare
 * CreatePersonRequest, CreatePersonResult and CreatePersonOutcome with identical names, fields, types and nullability.
 * Existing interfaces (BaseResponse, ValidationProblemDetails, ...) are unchanged and repeated only for reference.
 * StargateApiContract documents the new method; it is not copied into models.ts.
 * The UI never reads the Location header and does no client-side name validation.
 *
 * Revision 2: no type changes. Comment wording only. Names are ASCII-only and trimmed of ASCII whitespace by the
 * server, which does no Unicode normalisation. Two existing comments must be corrected in the same commit:
 *   - models.ts UpdatePersonRequest: "The name is sent exactly as given; the server trims surrounding whitespace
 *     (names are ASCII-only)."
 *   - stargate-api.ts updatePerson: "(the server trims and normalises)" becomes "(the server trims)".
 */
import { Observable } from 'rxjs';

/** Unchanged. `responseCode` always equals the HTTP status. */
export interface BaseResponse {
  success: boolean;
  message: string;
  responseCode: number;
}

/**
 * Unchanged shape. Doc comment in models.ts becomes:
 * "Body of every HTTP 400 from GET /Person, GET /Person/{name}, PUT /Person/{personId} and POST /Person."
 */
export interface ValidationProblemDetails {
  type: string;
  title: string;
  status: number;
  /** Keys identify the failing input; clients must not branch on them. Messages are for display only. */
  errors: Record<string, string[]>;
  traceId: string;
}

/** NEW. Body of POST /Person. The name is sent exactly as given; the server trims surrounding whitespace (names are ASCII-only). */
export interface CreatePersonRequest {
  name: string;
}

/**
 * NEW. Body of POST /Person for 201 and 409. `id` is an integer >= 1 on 201 and null on 409.
 * There is no `name` property. Property order on the wire is not significant.
 */
export interface CreatePersonResult extends BaseResponse {
  id: number | null;
}

/** NEW. What StargateApi.createPerson emits. Exactly one value, then completes. */
export type CreatePersonOutcome =
  | { status: 'created'; id: number }
  | { status: 'conflict' }
  | { status: 'invalid'; errors: Record<string, string[]> };

/** Method added to StargateApi (exercise1/ui/src/app/api/stargate-api.ts). */
export interface StargateApiContract {
  /**
   * POST `/api/Person` with JSON body `{ name }` (CreatePersonRequest), name passed through unmodified (no trim).
   * Exactly one request; no client-side validation; no request is skipped (an empty name is still sent).
   * - Success response (201) whose body `id` is an integer >= 1 -> `{ status: 'created', id }`.
   * - Success response whose `id` is null, missing, not an integer, or < 1 -> the observable errors with
   *   `new Error('Malformed create response')`.
   * - 409 -> `{ status: 'conflict' }`.
   * - 400 -> `{ status: 'invalid', errors }`, where `errors` is the body's `errors` object when the body is an object
   *   whose `errors` is a non-null object, otherwise `{}` (reuse the existing `extractErrors`).
   * - Any other status (0, 413, 415, 500, ...) -> the HttpErrorResponse propagates unchanged. Key on the status only.
   */
  createPerson(name: string): Observable<CreatePersonOutcome>;
}
