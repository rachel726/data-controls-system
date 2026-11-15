import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { of } from 'rxjs';
import { DefinitionError } from '../../core/models/control.models';
import { ConfirmService } from './confirm-dialog/confirm-dialog';
import { DefinitionDescription } from './definition-description/definition-description';
import { EmptyState } from './empty-state/empty-state';
import { PageHeader } from './page-header/page-header';

@Component({
  imports: [PageHeader, EmptyState],
  template: `
    <app-page-header title="הבקרות שלי" subtitle="7 בקרות"><button class="act">בקרה חדשה</button></app-page-header>
    <app-empty-state icon="fact_check" title="אין עדיין בקרות" message="צרו בקרה ראשונה"><button class="cta">צור</button></app-empty-state>
  `,
})
class HostComponent {}

@Component({
  imports: [DefinitionDescription],
  template: `<app-definition-description [sentence]="sentence()" [errors]="errors()" [loading]="loading()" />`,
})
class DescriptionHost {
  readonly sentence = signal<string | null>(null);
  readonly errors = signal<readonly DefinitionError[]>([]);
  readonly loading = signal(false);
}

describe('shared components', () => {
  it('page header and empty state render text and projected actions', async () => {
    const fixture = TestBed.createComponent(HostComponent);
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;

    expect(el.querySelector('h1')?.textContent).toBe('הבקרות שלי');
    expect(el.querySelector('.subtitle')?.textContent).toBe('7 בקרות');
    expect(el.querySelector('app-page-header .act')).not.toBeNull();
    expect(el.querySelector('app-empty-state h2')?.textContent).toBe('אין עדיין בקרות');
    expect(el.querySelector('app-empty-state [role="status"]')).not.toBeNull();
    expect(el.querySelector('app-empty-state .cta')).not.toBeNull();
  });

  describe('definition description', () => {
    async function render(setup: (host: DescriptionHost) => void): Promise<HTMLElement> {
      const fixture = TestBed.createComponent(DescriptionHost);
      setup(fixture.componentInstance);
      await fixture.whenStable();
      return fixture.nativeElement as HTMLElement;
    }

    it('shows the sentence under the business heading', async () => {
      const el = await render((h) => h.sentence.set('גיל קטן מ-18'));

      expect(el.querySelector('h3')?.textContent).toBe('רשומה תיחשב חריגה כאשר:');
      expect(el.querySelector('.sentence')?.textContent).toBe('גיל קטן מ-18');
      expect(el.querySelector('.invalid')).toBeNull();
    });

    it('shows errors instead of the sentence, marked invalid', async () => {
      const el = await render((h) => {
        h.sentence.set('ישן');
        h.errors.set([{ path: 'filter.children[0].field', code: 'UnknownField', message: 'לעובד אין שדה בשם x', isIncomplete: false }]);
      });

      expect(el.querySelector('.sentence')).toBeNull();
      expect(el.querySelector('.errors li')?.textContent).toContain('לעובד אין שדה בשם x');
      expect(el.querySelector('.invalid')).not.toBeNull();
    });

    it('shows a placeholder when empty and a progress bar while checking', async () => {
      expect((await render(() => undefined)).querySelector('.placeholder')).not.toBeNull();

      const loading = await render((h) => h.loading.set(true));
      expect(loading.querySelector('mat-progress-bar')).not.toBeNull();
      expect(loading.querySelector('.placeholder')).toBeNull();
    });
  });

  describe('confirm service', () => {
    function withDialogResult(result: unknown): ConfirmService {
      TestBed.configureTestingModule({ providers: [{ provide: MatDialog, useValue: { open: () => ({ afterClosed: () => of(result) }) } }] });
      return TestBed.inject(ConfirmService);
    }

    it('resolves true only when the user confirmed', async () => {
      expect(await withDialogResult(true).ask({ title: 'מחיקה', message: 'למחוק?' })).toBe(true);
    });

    it.each([false, undefined, null])('treats %s (cancel, escape, backdrop) as "no"', async (result) => {
      expect(await withDialogResult(result).ask({ title: 'מחיקה', message: 'למחוק?' })).toBe(false);
    });
  });
});
