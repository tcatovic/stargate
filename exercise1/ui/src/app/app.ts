import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';

import { PersonAstronaut } from './api/models';
import { StargateApi } from './api/stargate-api';

type LoadState = 'loading' | 'ready' | 'error';

@Component({
  selector: 'app-root',
  imports: [DatePipe],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  private readonly api = inject(StargateApi);

  protected readonly state = signal<LoadState>('loading');
  protected readonly people = signal<PersonAstronaut[]>([]);

  constructor() {
    this.load();
  }

  protected load(): void {
    this.state.set('loading');
    this.api.getPeople().subscribe({
      next: (result) => {
        this.people.set(result.people);
        this.state.set('ready');
      },
      error: () => this.state.set('error'),
    });
  }
}
