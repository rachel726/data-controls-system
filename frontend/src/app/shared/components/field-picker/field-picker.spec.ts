import { HarnessLoader } from '@angular/cdk/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatAutocompleteHarness } from '@angular/material/autocomplete/testing';
import { FieldOption } from '../../../core/services/catalog.store';
import { loadTestCatalog, provideTestCatalogApi } from '../../../../testing/test-catalog';
import { FieldPicker } from './field-picker';

@Component({
  imports: [FieldPicker],
  template: `<app-field-picker [entityKey]="entity()" [value]="value()" [filter]="filter()" (valueChange)="chosen.push($event)" />`,
})
class Host {
  readonly entity = signal('employee');
  readonly value = signal<string | null>(null);
  readonly filter = signal<((o: FieldOption) => boolean) | null>(null);
  readonly chosen: string[] = [];
}

describe('FieldPicker', () => {
  let fixture: ComponentFixture<Host>;
  let autocomplete: MatAutocompleteHarness;

  beforeEach(async () => {
    TestBed.configureTestingModule({ imports: [Host], providers: [provideTestCatalogApi] });
    await loadTestCatalog();
    fixture = TestBed.createComponent(Host);
    const loader: HarnessLoader = TestbedHarnessEnvironment.loader(fixture);
    autocomplete = await loader.getHarness(MatAutocompleteHarness);
  });

  async function groupLabels(): Promise<string[]> {
    await autocomplete.focus();
    return Promise.all((await autocomplete.getOptionGroups()).map((g) => g.getLabelText()));
  }

  async function optionTexts(): Promise<string[]> {
    return Promise.all((await autocomplete.getOptions()).map((o) => o.getText()));
  }

  it('groups the fields by how they are reached, in business terms', async () => {
    expect(await groupLabels()).toEqual(['שדות של עובד', 'שדות של מחלקה', 'שדות של מנהל ישיר'])   // one relation (the server's default);
    expect(await optionTexts()).toContain('תאריך תחילת העסקה');
  });

  it('searches in Hebrew across fields and relation paths', async () => {
    await autocomplete.enterText('פעיל');

    const texts = await optionTexts();
    expect(texts.length).toBeGreaterThan(0);
    expect(texts.every((t) => t.includes('פעיל'))).toBe(true);
    expect(await groupLabels()).toContain('שדות של מחלקה');
  });

  it('emits the chosen field path and then shows its business label', async () => {
    await autocomplete.enterText('מחלקה פעילה');
    await autocomplete.selectOption({ text: /מחלקה פעילה/ });

    expect(fixture.componentInstance.chosen).toEqual(['department.isActive']);
    expect(await autocomplete.getValue()).toBe('מחלקה פעילה');   // the name says whose it is - and never the path
  });

  it('shows the label of a field chosen earlier', async () => {
    fixture.componentInstance.value.set('manager.salary');
    fixture.detectChanges();

    expect(await autocomplete.getValue()).toBe('שכר חודשי של מנהל ישיר');
  });

  it('lets the caller restrict the choice (e.g. dates only)', async () => {
    fixture.componentInstance.filter.set((o) => o.field.dataType === 'date');
    fixture.detectChanges();

    expect(await groupLabels()).toEqual(['שדות של עובד', 'שדות של מנהל ישיר']);   // "מחלקה" has no dates
    expect(new Set(await optionTexts())).toEqual(new Set(['תאריך תחילת העסקה', 'תאריך סיום העסקה']));
  });

  it('works on any entity - fields come from the catalog', async () => {
    fixture.componentInstance.entity.set('project');
    fixture.detectChanges();

    expect(await groupLabels()).toEqual(['שדות של פרויקט', 'שדות של מחלקה אחראית']);
  });

  it('flags a stored field the catalog no longer has', async () => {
    fixture.componentInstance.value.set('shoeSize');
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).querySelector('.field-error')?.textContent).toContain('אינו קיים עוד');
  });
});
