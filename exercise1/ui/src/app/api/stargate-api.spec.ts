import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import {
  CreatePersonOutcome,
  AstronautDuty,
  CreatePersonResult,
  GetAstronautDutiesByNameResult,
  GetDutiesOutcome,
  GetPeopleResult,
  PersonAstronaut,
  PersonFilters,
  UpdatePersonOutcome,
  UpdatePersonResult,
  ValidationProblemDetails,
} from './models';
import { StargateApi } from './stargate-api';

const mira: PersonAstronaut = {
  personId: 1,
  name: 'Mira Okafor',
  currentRank: '',
  currentDutyTitle: '',
  careerStartDate: null,
  careerEndDate: null,
};

const peopleBody = (people: PersonAstronaut[]): GetPeopleResult => ({
  success: true,
  message: 'Successful',
  responseCode: 200,
  people,
});

const problem = (field: string): ValidationProblemDetails => ({
  type: 'https://tools.ietf.org/html/rfc9110#section-15.5.1',
  title: 'One or more validation errors occurred.',
  status: 400,
  errors: { [field]: ['invalid'] },
  traceId: 'trace-1',
});

describe('StargateApi', () => {
  let api: StargateApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    api = TestBed.inject(StargateApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  describe('listPeople', () => {
    it('U1 requests exactly /api/Person without filters', () => {
      api.listPeople().subscribe();
      const first = http.expectOne('/api/Person');
      expect(first.request.method).toBe('GET');
      first.flush(peopleBody([]));

      api.listPeople({}).subscribe();
      http.expectOne('/api/Person').flush(peopleBody([]));
    });

    it('U2 sends a single filter', () => {
      api.listPeople({ name: 'ra' }).subscribe();
      http.expectOne('/api/Person?name=ra').flush(peopleBody([]));
    });

    it('U3 sends all three filters under the contract key names', () => {
      api.listPeople({ name: 'ra', rank: 'MAJ', dutyTitle: 'Mission Specialist' }).subscribe();
      const req = http.expectOne((r) => r.url === '/api/Person');
      expect(req.request.params.get('name')).toBe('ra');
      expect(req.request.params.get('rank')).toBe('MAJ');
      expect(req.request.params.get('dutyTitle')).toBe('Mission Specialist');
      expect(req.request.params.keys().length).toBe(3);
      req.flush(peopleBody([]));
    });

    it('U4 omits empty, whitespace-only and undefined filters', () => {
      api.listPeople({ name: '', rank: '   ', dutyTitle: undefined }).subscribe();
      http.expectOne('/api/Person').flush(peopleBody([]));
    });

    it('U5 trims filter values', () => {
      api.listPeople({ rank: ' MAJ ' }).subscribe();
      http.expectOne('/api/Person?rank=MAJ').flush(peopleBody([]));
    });

    it('U6 encodes reserved characters', () => {
      api.listPeople({ name: "O'Brien & Co+1#" }).subscribe();
      const req = http.expectOne((r) => r.url === '/api/Person');
      expect(req.request.urlWithParams).toBe("/api/Person?name=O'Brien%20%26%20Co%2B1%23");
      req.flush(peopleBody([]));
    });

    it('U7 emits an empty list when nobody matches', () => {
      let result: GetPeopleResult | undefined;
      api.listPeople({ name: 'zzz' }).subscribe((r) => (result = r));
      http.expectOne('/api/Person?name=zzz').flush(peopleBody([]));
      expect(result?.people).toEqual([]);
    });

    it('U15 propagates a 400 as HttpErrorResponse', () => {
      let error: unknown;
      api.listPeople({ name: '%' }).subscribe({ error: (e: unknown) => (error = e) });
      http
        .expectOne('/api/Person?name=%25')
        .flush(problem('name'), { status: 400, statusText: 'Bad Request' });
      expect(error).toBeInstanceOf(HttpErrorResponse);
      expect((error as HttpErrorResponse).status).toBe(400);
    });

    it('UI-1 looks a person up by exact name and emits the body unchanged', () => {
      let result: GetPeopleResult | undefined;
      const body = peopleBody([mira]);
      api.listPeople({ name: 'Elena Vasquez' }).subscribe((r) => (result = r));
      const req = http.expectOne((r) => r.url === '/api/Person');
      expect(req.request.method).toBe('GET');
      expect(req.request.urlWithParams).toBe('/api/Person?name=Elena%20Vasquez');
      req.flush(body);
      expect(result).toEqual(body);
    });

    it('UI-2 emits an empty people list for an unknown name without an error', () => {
      let result: GetPeopleResult | undefined;
      let failed = false;
      api.listPeople({ name: 'Nobody' }).subscribe({
        next: (r) => (result = r),
        error: () => (failed = true),
      });
      http
        .expectOne('/api/Person?name=Nobody')
        .flush({ people: [], success: true, message: 'Successful', responseCode: 200 });
      expect(result?.people).toEqual([]);
      expect(failed).toBe(false);
    });

    it('UI-4 PersonFilters has exactly the keys name, rank and dutyTitle', () => {
      const keys: Array<keyof PersonFilters> = ['name', 'rank', 'dutyTitle'];
      const all: Required<PersonFilters> = { name: 'a', rank: 'b', dutyTitle: 'c' };
      api.listPeople(all).subscribe();
      const req = http.expectOne((r) => r.url === '/api/Person');
      expect([...req.request.params.keys()].sort()).toEqual([...keys].sort());
      req.flush(peopleBody([]));
    });
  });

  it('UI-3 StargateApi no longer has getPerson', () => {
    expect('getPerson' in api).toBe(false);
    expect((api as unknown as Record<string, unknown>)['getPerson']).toBeUndefined();
  });

  it('U14 getPeople requests exactly /api/Person', () => {
    let result: GetPeopleResult | undefined;
    api.getPeople().subscribe((r) => (result = r));
    http.expectOne('/api/Person').flush(peopleBody([mira]));
    expect(result?.people).toEqual([mira]);
  });

  describe('updatePerson', () => {
    const envelope = (status: number, message: string): UpdatePersonResult => ({
      success: false,
      message,
      responseCode: status,
      person: null,
    });

    function run(): {
      outcomes: UpdatePersonOutcome[];
      errors: unknown[];
      completed: () => boolean;
    } {
      const outcomes: UpdatePersonOutcome[] = [];
      const errors: unknown[] = [];
      let done = false;
      api.updatePerson(7, 'Ada').subscribe({
        next: (o) => outcomes.push(o),
        error: (e: unknown) => errors.push(e),
        complete: () => (done = true),
      });
      return { outcomes, errors, completed: () => done };
    }

    it('U1 sends exactly one PUT /api/Person/7 with the unmodified name only', () => {
      api.updatePerson(7, '  Ada ').subscribe();
      const req = http.expectOne('/api/Person/7');
      expect(req.request.method).toBe('PUT');
      expect(req.request.body).toEqual({ name: '  Ada ' });
      expect(Object.keys(req.request.body as object)).toEqual(['name']);
      req.flush({ ...envelope(200, 'Successful'), success: true, person: mira });
    });

    it('encodes the id in the path', () => {
      api.updatePerson('7/../x' as unknown as number, 'Ada').subscribe();
      http.expectOne('/api/Person/7%2F..%2Fx').flush(envelope(404, 'Person not found'), {
        status: 404,
        statusText: 'Not Found',
      });
    });

    it('U2 emits updated with the person once and completes', () => {
      const r = run();
      http
        .expectOne('/api/Person/7')
        .flush({ ...envelope(200, 'Successful'), success: true, person: mira });
      expect(r.outcomes).toEqual([{ status: 'updated', person: mira }]);
      expect(r.completed()).toBe(true);
      expect(r.errors).toEqual([]);
    });

    it('U3 emits notFound for 404', () => {
      const r = run();
      http
        .expectOne('/api/Person/7')
        .flush(envelope(404, 'Person not found'), { status: 404, statusText: 'Not Found' });
      expect(r.outcomes).toEqual([{ status: 'notFound' }]);
      expect(r.completed()).toBe(true);
    });

    it('U4 emits conflict for 409', () => {
      const r = run();
      http
        .expectOne('/api/Person/7')
        .flush(envelope(409, 'A person with this name already exists'), {
          status: 409,
          statusText: 'Conflict',
        });
      expect(r.outcomes).toEqual([{ status: 'conflict' }]);
      expect(r.completed()).toBe(true);
    });

    it('U5 emits invalid with the body errors for 400', () => {
      const r = run();
      http
        .expectOne('/api/Person/7')
        .flush(problem('Name'), { status: 400, statusText: 'Bad Request' });
      expect(r.outcomes).toEqual([{ status: 'invalid', errors: { Name: ['invalid'] } }]);
      expect(r.completed()).toBe(true);
    });

    it('U6 emits invalid with empty errors for a null, string or errors-less 400 body', () => {
      for (const body of [null, 'oops', {}, { errors: null }]) {
        const r = run();
        http.expectOne('/api/Person/7').flush(body, { status: 400, statusText: 'Bad Request' });
        expect(r.outcomes).toEqual([{ status: 'invalid', errors: {} }]);
      }
    });

    it('U7 propagates a 500 and a network error as HttpErrorResponse', () => {
      const server = run();
      http.expectOne('/api/Person/7').flush('boom', { status: 500, statusText: 'Server Error' });
      expect(server.outcomes).toEqual([]);
      expect(server.errors[0]).toBeInstanceOf(HttpErrorResponse);
      expect((server.errors[0] as HttpErrorResponse).status).toBe(500);

      const network = run();
      http.expectOne('/api/Person/7').error(new ProgressEvent('error'));
      expect(network.errors[0]).toBeInstanceOf(HttpErrorResponse);
      expect((network.errors[0] as HttpErrorResponse).status).toBe(0);
    });

    it('propagates 415 unchanged', () => {
      const r = run();
      http
        .expectOne('/api/Person/7')
        .flush('', { status: 415, statusText: 'Unsupported Media Type' });
      expect((r.errors[0] as HttpErrorResponse).status).toBe(415);
    });

    it('U8 errors with Malformed update response for a 200 without a person', () => {
      const r = run();
      http.expectOne('/api/Person/7').flush({ ...envelope(200, 'Successful'), success: true });
      expect(r.outcomes).toEqual([]);
      expect(r.errors[0]).toBeInstanceOf(Error);
      expect((r.errors[0] as Error).message).toBe('Malformed update response');
    });
  });

  describe('createPerson', () => {
    const created = (id: number | null): CreatePersonResult => ({
      success: true,
      message: 'Successful',
      responseCode: 201,
      id,
    });

    function run(name = 'Ada'): {
      outcomes: CreatePersonOutcome[];
      errors: unknown[];
      completed: () => boolean;
    } {
      const outcomes: CreatePersonOutcome[] = [];
      const errors: unknown[] = [];
      let done = false;
      api.createPerson(name).subscribe({
        next: (o) => outcomes.push(o),
        error: (e: unknown) => errors.push(e),
        complete: () => (done = true),
      });
      return { outcomes, errors, completed: () => done };
    }

    it('U1 sends exactly one POST /api/Person with the unmodified name only', () => {
      api.createPerson(' Ada ').subscribe();
      const req = http.expectOne('/api/Person');
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual({ name: ' Ada ' });
      expect(Object.keys(req.request.body as object)).toEqual(['name']);
      req.flush(created(7), { status: 201, statusText: 'Created' });
    });

    it('U2 still sends a POST for an empty name', () => {
      api.createPerson('').subscribe();
      const req = http.expectOne('/api/Person');
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual({ name: '' });
      req.flush(created(7), { status: 201, statusText: 'Created' });
    });

    it('U3 emits created with the id once and completes', () => {
      const r = run();
      http.expectOne('/api/Person').flush(created(7), { status: 201, statusText: 'Created' });
      expect(r.outcomes).toEqual([{ status: 'created', id: 7 }]);
      expect(r.completed()).toBe(true);
      expect(r.errors).toEqual([]);
    });

    it('U4 errors with Malformed create response for an invalid id', () => {
      for (const id of [null, undefined, 0, -1, 1.5, '7']) {
        const r = run();
        const body =
          id === undefined
            ? { success: true, message: 'Successful', responseCode: 201 }
            : created(id as number | null);
        http.expectOne('/api/Person').flush(body, { status: 201, statusText: 'Created' });
        expect(r.outcomes).toEqual([]);
        expect(r.errors[0]).toBeInstanceOf(Error);
        expect(r.errors[0]).not.toBeInstanceOf(HttpErrorResponse);
        expect((r.errors[0] as Error).message).toBe('Malformed create response');
      }
    });

    it('U4 errors with Malformed create response for a 201 with a null body', () => {
      const r = run();
      http.expectOne('/api/Person').flush(null, { status: 201, statusText: 'Created' });
      expect((r.errors[0] as Error).message).toBe('Malformed create response');
    });

    it('U5 emits conflict for 409', () => {
      const r = run();
      http.expectOne('/api/Person').flush(
        {
          success: false,
          message: 'A person with this name already exists',
          responseCode: 409,
          id: null,
        },
        { status: 409, statusText: 'Conflict' },
      );
      expect(r.outcomes).toEqual([{ status: 'conflict' }]);
      expect(r.completed()).toBe(true);
    });

    it('U6 emits invalid with the body errors for 400', () => {
      const r = run();
      http
        .expectOne('/api/Person')
        .flush(problem('Name'), { status: 400, statusText: 'Bad Request' });
      expect(r.outcomes).toEqual([{ status: 'invalid', errors: { Name: ['invalid'] } }]);
      expect(r.completed()).toBe(true);
    });

    it('U7 emits invalid with empty errors for a null, string or errors-less 400 body', () => {
      for (const body of [null, 'oops', {}, { errors: null }]) {
        const r = run();
        http.expectOne('/api/Person').flush(body, { status: 400, statusText: 'Bad Request' });
        expect(r.outcomes).toEqual([{ status: 'invalid', errors: {} }]);
        expect(r.completed()).toBe(true);
      }
    });

    it('U8 propagates 415, 413, 500 and a network error as HttpErrorResponse', () => {
      const cases: [number, string][] = [
        [415, 'Unsupported Media Type'],
        [413, 'Payload Too Large'],
        [500, 'Server Error'],
      ];
      for (const [status, statusText] of cases) {
        const r = run();
        http.expectOne('/api/Person').flush('x', { status, statusText });
        expect(r.outcomes).toEqual([]);
        expect(r.errors[0]).toBeInstanceOf(HttpErrorResponse);
        expect((r.errors[0] as HttpErrorResponse).status).toBe(status);
      }
      const network = run();
      http.expectOne('/api/Person').error(new ProgressEvent('error'));
      expect(network.errors[0]).toBeInstanceOf(HttpErrorResponse);
      expect((network.errors[0] as HttpErrorResponse).status).toBe(0);
    });
  });

  describe('getDuties', () => {
    const duty = (id: number, start: string, end: string | null): AstronautDuty => ({
      id,
      personId: 1,
      rank: 'Captain',
      dutyTitle: 'Pilot',
      dutyStartDate: start,
      dutyEndDate: end,
    });
    const duties = [
      duty(2, '2024-01-01T00:00:00', null),
      duty(1, '2020-01-01T00:00:00', '2023-12-31T00:00:00'),
    ];
    const body = (
      person: PersonAstronaut | null,
      astronautDuties: AstronautDuty[],
      code = 200,
    ): GetAstronautDutiesByNameResult => ({
      success: code === 200,
      message: code === 200 ? 'Successful' : 'Not found',
      responseCode: code,
      person,
      astronautDuties,
    });

    function run(name = 'Elena Vasquez'): {
      outcomes: GetDutiesOutcome[];
      errors: unknown[];
      completed: () => boolean;
    } {
      const outcomes: GetDutiesOutcome[] = [];
      const errors: unknown[] = [];
      let done = false;
      api.getDuties(name).subscribe({
        next: (o) => outcomes.push(o),
        error: (e: unknown) => errors.push(e),
        complete: () => (done = true),
      });
      return { outcomes, errors, completed: () => done };
    }

    const expectUrl = (urlWithParams: string) =>
      http.expectOne((r) => r.urlWithParams === urlWithParams);

    it('D1 sends one GET /api/AstronautDuty with only the name query parameter', () => {
      api.getDuties('Elena Vasquez').subscribe();
      const req = expectUrl('/api/AstronautDuty?name=Elena%20Vasquez');
      expect(req.request.method).toBe('GET');
      expect(req.request.url).toBe('/api/AstronautDuty');
      expect(req.request.params.keys()).toEqual(['name']);
      req.flush(body(mira, []));
    });

    it('D2 encodes apostrophes, spaces and reserved characters in the query string', () => {
      api.getDuties("Aoife O'Brien Jr.").subscribe();
      const first = expectUrl("/api/AstronautDuty?name=Aoife%20O'Brien%20Jr.");
      expect(first.request.url).toBe('/api/AstronautDuty');
      first.flush(body(mira, []));

      api.getDuties('a&b+c#d').subscribe();
      const second = expectUrl('/api/AstronautDuty?name=a%26b%2Bc%23d');
      expect(second.request.url).toBe('/api/AstronautDuty');
      second.flush(body(mira, []));
    });

    it('D3 emits found with the duties in server order and completes', () => {
      const r = run();
      expectUrl('/api/AstronautDuty?name=Elena%20Vasquez').flush(body(mira, duties));
      expect(r.outcomes).toEqual([{ status: 'found', person: mira, astronautDuties: duties }]);
      expect(r.errors).toEqual([]);
      expect(r.completed()).toBe(true);
    });

    it('D4 emits found with an empty list', () => {
      const r = run();
      expectUrl('/api/AstronautDuty?name=Elena%20Vasquez').flush(body(mira, []));
      expect(r.outcomes).toEqual([{ status: 'found', person: mira, astronautDuties: [] }]);
      expect(r.completed()).toBe(true);
    });

    it('D5 emits notFound for 404 without an error', () => {
      const r = run();
      expectUrl('/api/AstronautDuty?name=Elena%20Vasquez').flush(body(null, [], 404), {
        status: 404,
        statusText: 'Not Found',
      });
      expect(r.outcomes).toEqual([{ status: 'notFound' }]);
      expect(r.errors).toEqual([]);
      expect(r.completed()).toBe(true);
    });

    it('D6 emits invalid with the body errors for 400, or {} when absent', () => {
      const withErrors = run();
      expectUrl('/api/AstronautDuty?name=Elena%20Vasquez').flush(problem('name'), {
        status: 400,
        statusText: 'Bad Request',
      });
      expect(withErrors.outcomes).toEqual([{ status: 'invalid', errors: { name: ['invalid'] } }]);
      expect(withErrors.completed()).toBe(true);

      const without = run();
      expectUrl('/api/AstronautDuty?name=Elena%20Vasquez').flush(
        { title: 'Bad' },
        { status: 400, statusText: 'Bad Request' },
      );
      expect(without.outcomes).toEqual([{ status: 'invalid', errors: {} }]);
      expect(without.errors).toEqual([]);
    });

    it('D7 propagates 500, 401, 403 and a network error as HttpErrorResponse, never notFound', () => {
      for (const status of [500, 401, 403]) {
        const r = run();
        expectUrl('/api/AstronautDuty?name=Elena%20Vasquez').flush('x', {
          status,
          statusText: 'Error',
        });
        expect(r.outcomes).toEqual([]);
        expect(r.errors[0]).toBeInstanceOf(HttpErrorResponse);
        expect((r.errors[0] as HttpErrorResponse).status).toBe(status);
      }
      const network = run();
      expectUrl('/api/AstronautDuty?name=Elena%20Vasquez').error(new ProgressEvent('error'));
      expect(network.outcomes).toEqual([]);
      expect(network.errors[0]).toBeInstanceOf(HttpErrorResponse);
      expect((network.errors[0] as HttpErrorResponse).status).toBe(0);
    });

    it('D8 errors with Malformed duties response for a 200 without person or duties array', () => {
      const bodies: (object | null)[] = [
        body(null, duties),
        { success: true, message: 'Successful', responseCode: 200, person: mira },
        {
          success: true,
          message: 'Successful',
          responseCode: 200,
          person: mira,
          astronautDuties: null,
        },
        null,
      ];
      for (const b of bodies) {
        const r = run();
        expectUrl('/api/AstronautDuty?name=Elena%20Vasquez').flush(b);
        expect(r.outcomes).toEqual([]);
        expect(r.errors.length).toBe(1);
        expect((r.errors[0] as Error).message).toBe('Malformed duties response');
      }
    });

    it('D9 sends the name unmodified, including whitespace and the empty string', () => {
      api.getDuties('  Elena Vasquez ').subscribe();
      expectUrl('/api/AstronautDuty?name=%20%20Elena%20Vasquez%20').flush(body(mira, []));

      api.getDuties('').subscribe();
      const req = expectUrl('/api/AstronautDuty?name=');
      expect(req.request.method).toBe('GET');
      req.flush(body(mira, []));
    });
  });
});
