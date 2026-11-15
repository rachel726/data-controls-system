import { HarnessLoader } from '@angular/cdk/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatButtonToggleHarness } from '@angular/material/button-toggle/testing';
import { MatSelectHarness } from '@angular/material/select/testing';
import { OperatorDto } from '../../../core/models/catalog.models';
import { Operand } from '../../../core/models/control-definition';
import { CatalogStore, FieldOption } from '../../../core/services/catalog.store';
import { TEST_LIMITS, TEST_OPERATORS, loadTestCatalog, provideTestCatalogApi } from '../../../../testing/test-catalog';
import { OperatorSelect } from '../operator-select/operator-select';
import { ValueInput } from './value-input';

@Component({
  imports: [ValueInput],
  template: `<app-value-input [entityKey]="entity()" [field]="field()" [operator]="operator()" [operand]="operand()"
                              (operandChange)="emitted.push($event); operand.set($event)" />`,
})
class Host {
  readonly entity = signal('employee');
  readonly field = signal<FieldOption>(null!);
  readonly operator = signal<OperatorDto>(null!);
  readonly operand = signal<Operand | null>(null);
  readonly emitted: (Operand | null)[] = [];
}

const op = (key: string) => TEST_OPERATORS.find((o) => o.key === key)!;

describe('ValueInput', () => {
  let catalog: CatalogStore;
  let fixture: ComponentFixture<Host>;
  let loader: HarnessLoader;
  let el: HTMLElement;

  beforeEach(async () => {
    TestBed.configureTestingModule({ imports: [Host], providers: [provideTestCatalogApi] });
    catalog = await loadTestCatalog();
  });

  function render(entity: string, path: string, operator: string, operand: Operand | null = null): Host {
    fixture = TestBed.createComponent(Host);
    const host = fixture.componentInstance;
    host.entity.set(entity);
    host.field.set(catalog.fieldOption(entity, path)!);
    host.operator.set(op(operator));
    host.operand.set(operand);
    fixture.detectChanges();
    loader = TestbedHarnessEnvironment.loader(fixture);
    el = fixture.nativeElement as HTMLElement;
    return host;
  }

  function type(input: HTMLInputElement, text: string): void {
    input.value = text;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  it('shows nothing for operators without a value ("ריק")', () => {
    render('employee', 'email', 'isEmpty');

    expect(el.querySelector('.value-input')).toBeNull();
  });

  it("stops a text value at the server's limit; numbers are not limited by length", () => {
    render('employee', 'fullName', 'eq');
    expect(el.querySelector('input')?.getAttribute('maxlength')).toBe(String(TEST_LIMITS.maxTextValueLength));

    render('employee', 'salary', 'gt');
    expect(el.querySelector('input')?.getAttribute('maxlength')).toBeNull();
  });

  it('takes a number with its unit and emits a numeric literal', () => {
    const host = render('employee', 'salary', 'gt');

    expect(el.querySelector('[matTextSuffix]')?.textContent).toBe('₪');
    type(el.querySelector('input') as HTMLInputElement, '15000');
    type(el.querySelector('input') as HTMLInputElement, '');

    expect(host.emitted).toEqual([{ kind: 'literal', value: 15000 }, null]);
  });

  it('offers yes / no for yes-no fields', async () => {
    const host = render('employee', 'isActive', 'eq');

    await (await loader.getHarness(MatButtonToggleHarness.with({ text: 'כן' }))).check();

    expect(host.emitted).toEqual([{ kind: 'literal', value: true }]);
  });

  it('offers the allowed values by their business label', async () => {
    const host = render('project', 'status', 'eq');
    const select = await loader.getHarness(MatSelectHarness);

    await select.open();
    expect(await Promise.all((await select.getOptions()).map((o) => o.getText()))).toEqual(['מתוכנן', 'פעיל', 'הסתיים', 'בוטל']);
    await select.clickOptions({ text: 'בוטל' });

    expect(host.emitted).toEqual([{ kind: 'literal', value: 'Cancelled' }]);
  });

  it('switches to comparing with another field and offers only comparable fields', async () => {
    // Regression: choosing "שדה אחר" clears the value, which used to flip the mode straight back to "ערך".
    const host = render('employee', 'terminationDate', 'lt', { kind: 'literal', value: '2020-01-01' });

    await (await loader.getHarness(MatButtonToggleHarness.with({ text: 'שדה אחר' }))).check();
    fixture.detectChanges();

    expect(host.emitted).toEqual([null]);
    expect(el.querySelector('app-field-picker')).not.toBeNull();
    expect(el.querySelector('input[type="number"]')).toBeNull();
  });

  it('shows the chosen field when the operand is a field', () => {
    render('employee', 'terminationDate', 'lt', { kind: 'field', field: 'hireDate' });

    expect((el.querySelector('app-field-picker input') as HTMLInputElement).value).toBe('תאריך תחילת העסקה');
  });

  it('keeps a half-filled range and completes it', () => {
    const host = render('employee', 'salary', 'between');
    const [from, to] = Array.from(el.querySelectorAll('input')) as HTMLInputElement[];

    type(from, '5000');
    type(to, '9000');

    expect(host.emitted).toEqual([{ kind: 'literal', value: [5000, null] }, { kind: 'literal', value: [5000, 9000] }]);
  });

  it('collects a list of values as chips', () => {
    const host = render('employee', 'email', 'in');
    const input = el.querySelector('input') as HTMLInputElement;

    input.value = 'a@b.co';
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', keyCode: 13 }));
    fixture.detectChanges();

    expect(host.emitted).toEqual([{ kind: 'literal', value: ['a@b.co'] }]);
    expect(el.querySelector('mat-chip-row')?.textContent).toContain('a@b.co');
  });

  it('does not offer "another field" where the server would not accept it', () => {
    render('employee', 'fullName', 'contains');

    expect(el.querySelector('mat-button-toggle-group')).toBeNull();
  });

  it('shows an existing date value', () => {
    render('employee', 'hireDate', 'gt', { kind: 'literal', value: '2020-03-15' });

    expect((el.querySelector('input') as HTMLInputElement).value).toContain('2020');
  });
});

@Component({
  imports: [OperatorSelect],
  template: `<app-operator-select [dataType]="type()" [value]="value()" (valueChange)="value.set($event)" />`,
})
class SelectHost {
  readonly type = signal<'date' | 'boolean' | null>('date');
  readonly value = signal<OperatorDto['key'] | null>(null);
}

describe('OperatorSelect', () => {
  async function optionsFor(type: 'date' | 'boolean' | null): Promise<string[]> {
    TestBed.configureTestingModule({ imports: [SelectHost], providers: [provideTestCatalogApi] });
    await loadTestCatalog();
    const fixture = TestBed.createComponent(SelectHost);
    fixture.componentInstance.type.set(type);
    const select = await TestbedHarnessEnvironment.loader(fixture).getHarness(MatSelectHarness);
    if (await select.isDisabled()) {
      return [];
    }
    await select.open();
    return Promise.all((await select.getOptions()).map((o) => o.getText()));
  }

  it('words comparisons for dates as before / after', async () => {
    const options = await optionsFor('date');

    expect(options).toContain('לפני');
    expect(options).toContain('בתאריך או אחרי');
    expect(options).not.toContain('קטן מ');
    expect(options).not.toContain('מכיל');
  });

  it('offers only the operators that make sense for the type', async () => {
    expect(await optionsFor('boolean')).toEqual(['שווה ל', 'שונה מ', 'ריק', 'לא ריק']);
  });

  it('is disabled until a field is chosen', async () => {
    expect(await optionsFor(null)).toEqual([]);
  });
});
