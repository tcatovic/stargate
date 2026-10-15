/**
 * Contract for task 007 (revision 1): exact types the UI must use.
 * `exercise1/ui/src/app/api/models.ts` must declare GetDutiesOutcome with identical name, members, types and
 * nullability. AstronautDuty and GetAstronautDutiesByNameResult keep their current shapes; only their doc comments
 * change (text below). StargateApiContract documents the changed method; it is not copied into models.ts.
 * No createDuty method and no CreateAstronautDuty* types in the UI (decision 7).
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

/**
 * Unchanged shape. Doc comment in models.ts becomes:
 * "One item of GET /AstronautDuty?name=. Exactly these six properties. Dates are 'YYYY-MM-DDT00:00:00' (no offset);
 * dutyEndDate is null for the current duty."
 */
export interface AstronautDuty {
  id: number;
  personId: number;
  rank: string;
  dutyTitle: string;
  dutyStartDate: string;
  dutyEndDate: string | null;
}

/**
 * Unchanged shape. Doc comment in models.ts becomes:
 * "Body of GET /AstronautDuty?name= for 200 and 404. On 200 `person` is non-null and `astronautDuties` is ordered
 * newest start first (possibly empty). On 404 `person` is null and `astronautDuties` is []."
 */
export interface GetAstronautDutiesByNameResult extends BaseResponse {
  person: PersonAstronaut | null;
  astronautDuties: AstronautDuty[];
}

/**
 * Unchanged shape. Doc comment in models.ts becomes:
 * "Body of every HTTP 400 from GET /Person, GET /Person/{name}, PUT /Person/{personId}, POST /Person,
 * GET /AstronautDuty and POST /AstronautDuty."
 */
export interface ValidationProblemDetails {
  type: string;
  title: string;
  status: number;
  /** Keys identify the failing input; clients must not branch on them. Messages are for display only. */
  errors: Record<string, string[]>;
  traceId: string;
}

/** NEW. What StargateApi.getDuties emits. Exactly one value, then completes. */
export type GetDutiesOutcome =
  | { status: 'found'; person: PersonAstronaut; astronautDuties: AstronautDuty[] }
  | { status: 'notFound' }
  | { status: 'invalid'; errors: Record<string, string[]> };

/** Documentation of the changed client method (not copied into models.ts). */
export interface StargateApiContract {
  /**
   * CHANGED (return type and URL). Sends GET `/api/AstronautDuty` with the single query parameter `name` set through
   * `HttpParams` to the argument UNMODIFIED (no trim, no client-side validation; an empty string is still sent as
   * `?name=`). No path segment after `AstronautDuty`.
   * - 200 whose body has a non-null object `person` and an array `astronautDuties`: emits
   *   `{ status: 'found', person, astronautDuties }` with the array in server order.
   * - 200 without that (person null/missing, or astronautDuties not an array): errors with
   *   `new Error('Malformed duties response')`.
   * - 404: emits `{ status: 'notFound' }` (no error).
   * - 400: emits `{ status: 'invalid', errors }` where `errors` is the body's `errors` object, or `{}` if absent.
   * - Any other failure (500, 0/network, 401, 403, ...): propagates the HttpErrorResponse unchanged.
   */
  getDuties(name: string): Observable<GetDutiesOutcome>;
}
