export interface BaseResponse {
  success: boolean;
  message: string;
  responseCode: number;
}

export interface PersonAstronaut {
  personId: number;
  name: string;
  currentRank: string;
  currentDutyTitle: string;
  careerStartDate: string | null;
  careerEndDate: string | null;
}

/**
 * One item of GET /AstronautDuty?name=. Exactly these six properties. Dates are 'YYYY-MM-DDT00:00:00' (no offset);
 * dutyEndDate is null for the current duty.
 */
export interface AstronautDuty {
  id: number;
  personId: number;
  rank: string;
  dutyTitle: string;
  dutyStartDate: string;
  dutyEndDate: string | null;
}

export interface GetPeopleResult extends BaseResponse {
  people: PersonAstronaut[];
}

export interface GetPersonByNameResult extends BaseResponse {
  person: PersonAstronaut | null;
}

/**
 * Body of GET /AstronautDuty?name= for 200 and 404. On 200 `person` is non-null and `astronautDuties` is ordered
 * newest start first (possibly empty). On 404 `person` is null and `astronautDuties` is [].
 */
export interface GetAstronautDutiesByNameResult extends BaseResponse {
  person: PersonAstronaut | null;
  astronautDuties: AstronautDuty[];
}

/** Optional filters for GET /Person. Values are trimmed; empty values are not sent. */
export interface PersonFilters {
  name?: string;
  rank?: string;
  dutyTitle?: string;
}

/**
 * Body of every HTTP 400 from GET /Person, GET /Person/{name}, PUT /Person/{personId}, POST /Person,
 * GET /AstronautDuty and POST /AstronautDuty.
 */
export interface ValidationProblemDetails {
  type: string;
  title: string;
  status: number;
  /** Keys identify the failing input; clients must not branch on them. Messages are for display only. */
  errors: Record<string, string[]>;
  traceId: string;
}

/** Body of PUT /Person/{personId}. The name is sent exactly as given; the server trims surrounding whitespace (names are ASCII-only). */
export interface UpdatePersonRequest {
  name: string;
}

/** Body of PUT /Person/{personId} for 200, 404 and 409. `person` is non-null on 200, null on 404 and 409. */
export interface UpdatePersonResult extends BaseResponse {
  person: PersonAstronaut | null;
}

/** What StargateApi.updatePerson emits. Exactly one value, then completes. */
export type UpdatePersonOutcome =
  | { status: 'updated'; person: PersonAstronaut }
  | { status: 'notFound' }
  | { status: 'conflict' }
  | { status: 'invalid'; errors: Record<string, string[]> };

/** Body of POST /Person. The name is sent exactly as given; the server trims surrounding whitespace (names are ASCII-only). */
export interface CreatePersonRequest {
  name: string;
}

/**
 * Body of POST /Person for 201 and 409. `id` is an integer >= 1 on 201 and null on 409.
 * There is no `name` property. Property order on the wire is not significant.
 */
export interface CreatePersonResult extends BaseResponse {
  id: number | null;
}

/** What StargateApi.createPerson emits. Exactly one value, then completes. */
export type CreatePersonOutcome =
  | { status: 'created'; id: number }
  | { status: 'conflict' }
  | { status: 'invalid'; errors: Record<string, string[]> };

/** What StargateApi.getDuties emits. Exactly one value, then completes. */
export type GetDutiesOutcome =
  | { status: 'found'; person: PersonAstronaut; astronautDuties: AstronautDuty[] }
  | { status: 'notFound' }
  | { status: 'invalid'; errors: Record<string, string[]> };
