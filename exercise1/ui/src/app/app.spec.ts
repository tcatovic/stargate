import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { GetPeopleResult } from './api/models';
import { App } from './app';

describe('App', () => {
  let http: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('lists people returned by the API', async () => {
    const fixture = TestBed.createComponent(App);
    const body: GetPeopleResult = {
      success: true,
      message: 'Successful',
      responseCode: 200,
      people: [
        {
          personId: 1,
          name: 'John Doe',
          currentRank: '1LT',
          currentDutyTitle: 'Commander',
          careerStartDate: '2024-01-01T00:00:00',
          careerEndDate: null,
        },
      ],
    };

    http.expectOne('/api/Person').flush(body);
    await fixture.whenStable();

    const rows = (fixture.nativeElement as HTMLElement).querySelectorAll('tbody tr');
    expect(rows.length).toBe(1);
    expect(rows[0].textContent).toContain('John Doe');
  });

  it('shows an error with retry when the API is unreachable', async () => {
    const fixture = TestBed.createComponent(App);

    http.expectOne('/api/Person').flush('boom', { status: 500, statusText: 'Server Error' });
    await fixture.whenStable();

    expect((fixture.nativeElement as HTMLElement).querySelector('[role=alert]')).toBeTruthy();
  });
});
