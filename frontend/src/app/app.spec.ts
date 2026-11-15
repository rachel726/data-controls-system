import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { App } from './app';
import { routes } from './app.routes';

describe('App shell', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter(routes)],
    }).compileComponents();
  });

  it('shows the Hebrew title and the three navigation items', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;

    expect(el.querySelector('.title')?.textContent).toContain('בקרות נתונים');
    const labels = Array.from(el.querySelectorAll('mat-nav-list [matListItemTitle]')).map((s) =>
      s.textContent?.trim(),
    );
    expect(labels).toEqual(['הבקרות שלי', 'בקרה חדשה', 'בקרה בשפה חופשית']);
  });
});
