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

/** Body of every HTTP 400 from GET /Person and GET /Person/{name}. */
export interface ValidationProblemDetails {
  type: string;
  title: string;
  status: number;
  /** Keys are parameter names: 'name' | 'rank' | 'dutyTitle'. Messages are for display only. */
  errors: Record<string, string[]>;
  traceId: string;
}
