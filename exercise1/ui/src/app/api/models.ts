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

/** Body of every HTTP 400 from GET /Person, GET /Person/{name} and PUT /Person/{personId}. */
export interface ValidationProblemDetails {
  type: string;
  title: string;
  status: number;
  /** Keys identify the failing input; clients must not branch on them. Messages are for display only. */
  errors: Record<string, string[]>;
  traceId: string;
}

/** Body of PUT /Person/{personId}. The name is sent exactly as given; the server trims and NFC-normalises. */
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
