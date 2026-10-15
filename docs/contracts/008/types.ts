/**
 * Contract for task 008 (revision 1): exact UI types.
 * `exercise1/ui/src/app/api/models.ts` must declare PersonFilters with identical name, members, types and optionality.
 * BaseResponse, PersonAstronaut, GetPeopleResult and ValidationProblemDetails keep their current shapes; only the
 * doc comments below change. StargateApiContract documents the client surface affected by this task; it is not
 * copied into models.ts.
 *
 * REMOVED (must not exist anywhere under exercise1/ui/src):
 *   - interface GetPersonByNameResult
 *   - StargateApi.getPerson(name: string): Observable<PersonAstronaut | null>
 */
import { Observable } from 'rxjs';

/** Unchanged. `responseCode` always equals the HTTP status. */
export interface BaseResponse {
  success: boolean;
  message: string;
  responseCode: number;
}

/** Unchanged. currentRank/currentDutyTitle are '' (never null) for a person with no assignment. */
export interface PersonAstronaut {
  personId: number;
  name: string;
  currentRank: string;
  currentDutyTitle: string;
  careerStartDate: string | null;
  careerEndDate: string | null;
}

/** Unchanged. Body of GET /Person for 200 (people may be empty, e.g. an unknown name). */
export interface GetPeopleResult extends BaseResponse {
  people: PersonAstronaut[];
}

/**
 * Shape UNCHANGED. Doc comment in models.ts becomes exactly:
 * "Optional filters for GET /Person. Mirrors the API's GetPeople query object field for field (query keys name, rank,
 * dutyTitle). Each is a case-insensitive exact match; name is the person's full name. Values are trimmed; empty
 * values are not sent."
 */
export interface PersonFilters {
  name?: string;
  rank?: string;
  dutyTitle?: string;
}

/**
 * Shape UNCHANGED. Doc comment in models.ts becomes exactly:
 * "Body of every HTTP 400 from GET /Person, PUT /Person/{personId}, POST /Person, GET /AstronautDuty and
 * POST /AstronautDuty."
 * (followed by the existing comment on `errors`, unchanged).
 */
export interface ValidationProblemDetails {
  type: string;
  title: string;
  status: number;
  /** Keys identify the failing input; clients must not branch on them. Messages are for display only. */
  errors: Record<string, string[]>;
  traceId: string;
}

/**
 * Client surface affected by this task (documentation, not copied into models.ts).
 * listPeople behaviour is unchanged: for each key in the fixed order name, rank, dutyTitle, the value is trimmed with
 * String.prototype.trim and sent via HttpParams only if non-empty; GET /api/Person with no params when none remain.
 * 200 emits the body unchanged; every error (including 400) propagates as HttpErrorResponse (no mapping).
 * getPeople() is listPeople() with no filters.
 */
export interface StargateApiContract {
  getPeople(): Observable<GetPeopleResult>;
  listPeople(filters?: PersonFilters): Observable<GetPeopleResult>;
  // getPerson: REMOVED. Callers use listPeople({ name }) and treat an empty `people` as "not found".
}
