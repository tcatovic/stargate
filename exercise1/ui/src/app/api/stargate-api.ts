import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { GetAstronautDutiesByNameResult, GetPeopleResult, GetPersonByNameResult } from './models';

/** Typed client for the Stargate API. Requests go through the `/api` dev proxy (see proxy.conf.json). */
@Injectable({ providedIn: 'root' })
export class StargateApi {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api';

  getPeople(): Observable<GetPeopleResult> {
    return this.http.get<GetPeopleResult>(`${this.baseUrl}/Person`);
  }

  getPerson(name: string): Observable<GetPersonByNameResult> {
    return this.http.get<GetPersonByNameResult>(`${this.baseUrl}/Person/${encodeURIComponent(name)}`);
  }

  getDuties(name: string): Observable<GetAstronautDutiesByNameResult> {
    return this.http.get<GetAstronautDutiesByNameResult>(
      `${this.baseUrl}/AstronautDuty/${encodeURIComponent(name)}`,
    );
  }
}
