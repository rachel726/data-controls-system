import { BreakpointObserver } from '@angular/cdk/layout';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatPaginatorHarness } from '@angular/material/paginator/testing';
import { MatSortHarness } from '@angular/material/sort/testing';
import { of } from 'rxjs';
import { ColumnFilter, ResultColumn, ResultRow } from '../../../core/models/control.models';
import { DataTable, PageChange, SortChange } from './data-table';

const COLUMNS: ResultColumn[] = [
  { key: 'fullName', displayName: 'שם מלא', dataType: 'text', unit: null, isConditionField: false, allowedValues: [] },
  { key: 'isActive', displayName: 'עובד פעיל', dataType: 'boolean', unit: null, isConditionField: false, allowedValues: [] },
  { key: 'age', displayName: 'גיל', dataType: 'integer', unit: null, isConditionField: true, allowedValues: [] },
  { key: 'department__status', displayName: 'סטטוס', dataType: 'enum', unit: null, isConditionField: false,
    allowedValues: [{ value: 'Active', label: 'פעיל' }, { value: 'Cancelled', label: 'בוטל' }] },
];

const ROWS: ResultRow[] = [
  { recordId: 201, title: 'מיכל חדד', values: { fullName: 'מיכל חדד', isActive: 'כן', age: '16', department__status: 'פעיל' },
    reasons: ['גיל (16) קטן מ-18', 'שכר חודשי (22,000 ₪) גדול מ-15,000 ₪'] },
];

@Component({
  imports: [DataTable],
  template: `<app-data-table [columns]="columns" [rows]="rows()" [total]="41" [page]="1" [pageSize]="25" [filters]="filters()" [loading]="loading()" [maxFilterLength]="100" emptyMessage="אין חריגות שמתאימות לחיפוש."
               (pageChange)="pages.push($event)" (sortChange)="sorts.push($event)" (filtersChange)="filterChanges.push($event); filters.set($event)" />`,
})
class Host {
  readonly columns = COLUMNS;
  readonly rows = signal<ResultRow[]>(ROWS);
  readonly loading = signal(false);
  readonly filters = signal<ColumnFilter[]>([]);
  readonly pages: PageChange[] = [];
  readonly sorts: SortChange[] = [];
  readonly filterChanges: ColumnFilter[][] = [];
}

describe('DataTable', () => {
  let fixture: ComponentFixture<Host>;
  let el: HTMLElement;

  function render(mobile = false): void {
    TestBed.configureTestingModule({
      imports: [Host],
      providers: [{ provide: BreakpointObserver, useValue: { observe: () => of({ matches: mobile }), isMatched: () => mobile } }],
    });
    fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    el = fixture.nativeElement as HTMLElement;
  }

  function change(selector: string, value: string): void {
    const control = el.querySelector(selector) as HTMLInputElement | HTMLSelectElement;
    control.value = value;
    control.dispatchEvent(new Event('change'));
    fixture.detectChanges();
  }

  it('renders the server columns, highlights the checked fields and shows why each record is a violation', () => {
    render();

    const headers = Array.from(el.querySelectorAll('tr:first-child th')).map((th) => th.textContent?.trim());
    expect(headers).toEqual(['שם מלא', 'עובד פעיל', 'גיל', 'סטטוס', 'למה זו חריגה?']);
    expect(el.querySelector('th.condition')?.textContent).toContain('גיל');
    expect(Array.from(el.querySelectorAll('td.reasons .reason')).map((r) => r.textContent)).toEqual(ROWS[0].reasons);
    // each value is isolated, so a phone / e-mail / code keeps its own direction inside the RTL page
    expect(Array.from(el.querySelectorAll('td.mat-mdc-cell:not(.reasons)')).every((td) => td.firstElementChild?.tagName === 'BDI')).toBe(true);
  });

  it('offers a filter that fits each column type and emits it', () => {
    render();

    expect(el.querySelector('[aria-label="סינון שם מלא"]')?.getAttribute('type')).toBe('search');
    expect(el.querySelector('[aria-label="סינון גיל"]')?.getAttribute('type')).toBe('number');
    expect(Array.from(el.querySelectorAll('[aria-label="סינון סטטוס"] option')).map((o) => o.textContent)).toEqual(['הכל', 'פעיל', 'בוטל']);

    change('[aria-label="סינון עובד פעיל"]', 'true');
    change('[aria-label="סינון סטטוס"]', 'Cancelled');
    change('[aria-label="סינון עובד פעיל"]', '');   // back to "all" removes the filter

    expect(fixture.componentInstance.filterChanges).toEqual([
      [{ key: 'isActive', value: 'true' }],
      [{ key: 'isActive', value: 'true' }, { key: 'department__status', value: 'Cancelled' }],
      [{ key: 'department__status', value: 'Cancelled' }],
    ]);
  });

  it('a text filter stops at the given limit; other filters are not limited by length', () => {
    render();

    expect(el.querySelector('[aria-label="סינון שם מלא"]')?.getAttribute('maxlength')).toBe('100');
    expect(el.querySelector('[aria-label="סינון גיל"]')?.getAttribute('maxlength')).toBeNull();
  });

  it('emits sorting and paging for the server to apply', async () => {
    render();
    const loader = TestbedHarnessEnvironment.loader(fixture);

    const [nameHeader] = await (await loader.getHarness(MatSortHarness)).getSortHeaders({ label: 'שם מלא' });
    await nameHeader.click();
    await nameHeader.click();
    await (await loader.getHarness(MatPaginatorHarness)).goToNextPage();

    expect(fixture.componentInstance.sorts).toEqual([{ key: 'fullName', descending: false }, { key: 'fullName', descending: true }]);
    expect(fixture.componentInstance.pages).toEqual([{ page: 2, pageSize: 25 }]);
  });

  it('pages in Hebrew', async () => {
    render();

    const paginator = await TestbedHarnessEnvironment.loader(fixture).getHarness(MatPaginatorHarness);
    expect(await paginator.getRangeLabel()).toBe('1–25 מתוך 41');
  });

  it('shows "no rows" inside the table and dims the rows while loading - the page keeps its shape', () => {
    render();
    fixture.componentInstance.loading.set(true);
    fixture.detectChanges();
    expect(el.querySelector('.table-scroll')?.classList).toContain('busy');
    expect(el.querySelector('.table-scroll')?.getAttribute('aria-busy')).toBe('true');
    expect(el.querySelectorAll('tr.mat-mdc-row').length).toBe(1);   // the current rows stay until new ones arrive

    fixture.componentInstance.rows.set([]);
    fixture.componentInstance.loading.set(false);
    fixture.detectChanges();
    expect(el.querySelector('table tr.no-data')?.textContent).toContain('אין חריגות שמתאימות לחיפוש.');
    expect(el.querySelector('table thead')).not.toBeNull();   // headers and filters stay usable
  });

  it('on phones too, "no rows" takes the place of the cards', () => {
    render(true);
    fixture.componentInstance.rows.set([]);
    fixture.detectChanges();

    expect(el.querySelector('ul.cards li.no-data')?.textContent).toContain('אין חריגות שמתאימות לחיפוש.');
  });

  it('shows cards on phones and tablets, with the checked fields first', () => {
    render(true);

    expect(el.querySelector('table')).toBeNull();
    const card = el.querySelector('li.card')!;
    expect(card.querySelector('h3')?.textContent).toBe('מיכל חדד');
    expect(card.querySelectorAll('.reasons li').length).toBe(2);
    expect(card.querySelector('dl div dt')?.textContent).toBe('גיל');
  });
});
