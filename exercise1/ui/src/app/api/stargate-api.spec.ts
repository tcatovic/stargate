import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import {
  GetPeopleResult,
  GetPersonByNameResult,
  PersonAstronaut,
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
  });

  describe('getPerson', () => {
    it('U8 returns the person on 200', () => {
      let person: PersonAstronaut | null | undefined;
      api.getPerson('Mira Okafor').subscribe((p) => (person = p));
      const body: GetPersonByNameResult = {
        success: true,
        message: 'Successful',
        responseCode: 200,
        person: mira,
      };
      http.expectOne('/api/Person/Mira%20Okafor').flush(body);
      expect(person).toEqual(mira);
    });

    it('U9 maps 404 to null without an error', () => {
      let person: PersonAstronaut | null | undefined;
      let completed = false;
      let failed = false;
      api.getPerson('Nobody').subscribe({
        next: (p) => (person = p),
        error: () => (failed = true),
        complete: () => (completed = true),
      });
      const body: GetPersonByNameResult = {
        success: false,
        message: 'Person not found',
        responseCode: 404,
        person: null,
      };
      http.expectOne('/api/Person/Nobody').flush(body, { status: 404, statusText: 'Not Found' });
      expect(person).toBeNull();
      expect(completed).toBe(true);
      expect(failed).toBe(false);
    });

    it('U10 propagates a 400 with its problem details', () => {
      let error: unknown;
      api.getPerson("x' OR '1'='1").subscribe({ error: (e: unknown) => (error = e) });
      http
        .expectOne("/api/Person/x'%20OR%20'1'%3D'1")
        .flush(problem('name'), { status: 400, statusText: 'Bad Request' });
      expect(error).toBeInstanceOf(HttpErrorResponse);
      const err = error as HttpErrorResponse;
      expect(err.status).toBe(400);
      expect((err.error as ValidationProblemDetails).errors['name']).toBeDefined();
    });

    it('U11 propagates a 500', () => {
      let error: unknown;
      api.getPerson('Mira Okafor').subscribe({ error: (e: unknown) => (error = e) });
      http
        .expectOne('/api/Person/Mira%20Okafor')
        .flush('boom', { status: 500, statusText: 'Server Error' });
      expect(error).toBeInstanceOf(HttpErrorResponse);
      expect((error as HttpErrorResponse).status).toBe(500);
    });

    it('propagates a network failure (status 0)', () => {
      let error: unknown;
      api.getPerson('Mira Okafor').subscribe({ error: (e: unknown) => (error = e) });
      http.expectOne('/api/Person/Mira%20Okafor').error(new ProgressEvent('error'));
      expect(error).toBeInstanceOf(HttpErrorResponse);
      expect((error as HttpErrorResponse).status).toBe(0);
    });

    it('U12 encodes reserved characters in the path', () => {
      api.getPerson('a/b?c#d').subscribe();
      http
        .expectOne('/api/Person/a%2Fb%3Fc%23d')
        .flush({}, { status: 404, statusText: 'Not Found' });
    });

    it('trims the name before requesting', () => {
      api.getPerson('  Mira Okafor  ').subscribe();
      http
        .expectOne('/api/Person/Mira%20Okafor')
        .flush({}, { status: 404, statusText: 'Not Found' });
    });

    it('U13 emits null without a request for a blank name', () => {
      const seen: (PersonAstronaut | null)[] = [];
      let completions = 0;
      for (const name of ['   ', '']) {
        api.getPerson(name).subscribe({ next: (p) => seen.push(p), complete: () => completions++ });
      }
      expect(seen).toEqual([null, null]);
      expect(completions).toBe(2);
      http.expectNone(() => true);
    });
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
});
