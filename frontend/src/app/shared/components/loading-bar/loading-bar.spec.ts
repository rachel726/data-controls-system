import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { LoadingBar } from './loading-bar';

@Component({
  imports: [LoadingBar],
  template: `<app-loading-bar [active]="active()" label="טוען תוצאות" />`,
})
class Host {
  readonly active = signal(false);
}

describe('LoadingBar', () => {
  it('always keeps its slot, so the page does not move when loading starts or ends', () => {
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    const bar = (fixture.nativeElement as HTMLElement).querySelector('app-loading-bar')!;

    expect(bar.querySelector('mat-progress-bar')).toBeNull();
    expect(bar.getAttribute('aria-busy')).toBe('false');

    fixture.componentInstance.active.set(true);
    fixture.detectChanges();

    expect(bar.querySelector('mat-progress-bar')?.getAttribute('aria-label')).toBe('טוען תוצאות');
    expect(bar.getAttribute('aria-busy')).toBe('true');
    expect(getComputedStyle(bar).height).toBe('4px');
  });
});
