import { HarnessLoader } from '@angular/cdk/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatAutocompleteHarness } from '@angular/material/autocomplete/testing';
import { MatButtonToggleHarness } from '@angular/material/button-toggle/testing';
import { MatSelectHarness } from '@angular/material/select/testing';
import { ConditionNode, GroupNode } from '../../../core/models/control-definition';
import { DefinitionError } from '../../../core/models/control.models';
import { DefinitionTree, NodePath, TreeEdit } from '../../../core/models/definition-tree';
import { loadTestCatalog, provideTestCatalogApi } from '../../../../testing/test-catalog';
import { ConditionGroup } from './condition-group';

@Component({
  imports: [ConditionGroup],
  template: `<app-condition-group [group]="group()" [entityKey]="entity()" [readonly]="readonly()" [errorsFor]="errorsFor()"
                                  [canAddCondition]="canAdd()" (edit)="apply($event)" />`,
})
class Host {
  readonly entity = signal('employee');
  readonly group = signal<GroupNode>({ type: 'group', logic: 'and', children: [] });
  readonly readonly = signal(false);
  readonly canAdd = signal(true);
  readonly errorsFor = signal<(path: NodePath) => readonly DefinitionError[]>(() => []);
  readonly edits: TreeEdit[] = [];

  /** Owns the tree like the editor store does. */
  apply(edit: TreeEdit): void {
    this.edits.push(edit);
    this.group.update((root) => DefinitionTree.apply(root, edit));
  }
}

const AGE_OVER_18: ConditionNode = { type: 'condition', field: 'age', operator: 'gt', operand: { kind: 'literal', value: 18 } };

describe('Condition builder', () => {
  let fixture: ComponentFixture<Host>;
  let host: Host;
  let el: HTMLElement;
  let loader: HarnessLoader;

  beforeEach(async () => {
    TestBed.configureTestingModule({ imports: [Host], providers: [provideTestCatalogApi] });
    await loadTestCatalog();
  });

  function render(entity: string, group?: GroupNode): void {
    fixture = TestBed.createComponent(Host);
    host = fixture.componentInstance;
    host.entity.set(entity);
    if (group) {
      host.group.set(group);
    }
    fixture.detectChanges();
    el = fixture.nativeElement as HTMLElement;
    loader = TestbedHarnessEnvironment.loader(fixture);
  }

  /** The "add" buttons of the n-th group on screen (0 = root), by their text. */
  function addButtons(groupIndex = 0): string[] {
    const footer = el.querySelectorAll('section.group')[groupIndex].querySelector(':scope > footer.add');
    return Array.from(footer?.querySelectorAll('button') ?? []).map((b) => b.textContent!.replace(/^\w+/, '').trim());
  }

  function click(text: string, groupIndex = 0): void {
    const footer = el.querySelectorAll('section.group')[groupIndex].querySelector(':scope > footer.add')!;
    const button = Array.from(footer.querySelectorAll('button')).find((b) => b.textContent!.replace(/^\w+/, '').trim() === text)!;
    button.click();
    fixture.detectChanges();
  }

  async function selectIn(container: string): Promise<MatSelectHarness> {
    return (await loader.getChildLoader(container)).getHarness(MatSelectHarness);
  }

  const joiners = () => Array.from(el.querySelectorAll('.joiner')).map((j) => j.textContent?.trim());

  it('starts empty, adds a condition and a group that mixes "and" with "or"', () => {
    render('employee');
    expect(el.querySelector('.empty')?.textContent).toContain('עדיין אין תנאים');

    click('תנאי');
    click('קבוצת תנאים');

    expect(host.group().children).toEqual([
      { type: 'condition', field: '', operator: 'eq' },
      { type: 'group', logic: 'or', children: [{ type: 'condition', field: '', operator: 'eq' }] },
    ]);
    expect(el.querySelectorAll('app-condition-row').length).toBe(2);
    expect(joiners()).toEqual(['וגם']);
  });

  it('joins the conditions by "all" or "any"', async () => {
    render('employee', { type: 'group', logic: 'and', children: [AGE_OVER_18, AGE_OVER_18] });

    await (await loader.getHarness(MatButtonToggleHarness.with({ text: 'אחד מהתנאים' }))).check();

    expect(host.edits).toEqual([{ kind: 'logic', groupPath: [], logic: 'or' }]);
    expect(joiners()).toEqual(['או']);
  });

  it('builds a "related records" condition with conditions on the related entity', async () => {
    render('department');

    click('תנאי יש / אין');
    expect(host.group().children).toEqual([{ type: 'related', relation: 'employees', quantifier: 'any' }]);
    expect(el.querySelector('.related .empty')?.textContent).toContain('נבדק רק אם יש עובדים');
    expect(Array.from(el.querySelectorAll('.related-head mat-label')).map((l) => l.textContent!.trim())).toEqual(['יש או אין', 'של המחלקה']);

    click('תנאי', 1); // the related records' own group
    expect(host.edits.at(-1)).toMatchObject({ kind: 'add', groupPath: [0, 'where'] });
    expect(host.group().children[0]).toMatchObject({ where: { children: [{ type: 'condition' }] } });

    await (await selectIn('.quantifier')).clickOptions({ text: 'אין' });
    expect(host.group().children[0]).toMatchObject({ quantifier: 'none', where: { children: [{ type: 'condition' }] } });

    // other records have other fields - their conditions start over
    await (await selectIn('.relation')).clickOptions({ text: 'פרויקטים' });
    expect(host.group().children).toEqual([{ type: 'related', relation: 'projects', quantifier: 'none' }]);
  });

  it('offers only what the server allows at each nesting level', () => {
    render('employee', {
      type: 'group', logic: 'and', children: [
        { type: 'group', logic: 'or', children: [{ type: 'group', logic: 'and', children: [AGE_OVER_18] }] },
        { type: 'related', relation: 'directReports', quantifier: 'none', where: { type: 'group', logic: 'and', children: [] } },
      ],
    });

    // section order: root, nested (2), nested in nested (3), related records' group (3)
    expect(addButtons(0)).toEqual(['תנאי', 'קבוצת תנאים', 'תנאי יש / אין']);
    expect(addButtons(1)).toEqual(['תנאי', 'קבוצת תנאים']);
    expect(addButtons(2)).toEqual(['תנאי']);
    expect(addButtons(3)).toEqual(['תנאי']);
  });

  it('stops adding at the maximum number of conditions', () => {
    render('employee', { type: 'group', logic: 'and', children: [AGE_OVER_18] });
    host.canAdd.set(false);
    fixture.detectChanges();

    expect(addButtons()).toEqual(['תנאי']);
    expect(el.querySelector<HTMLButtonElement>('footer.add button')!.disabled).toBe(true);
  });

  it('removes a condition and a nested group', () => {
    render('employee', {
      type: 'group', logic: 'and', children: [AGE_OVER_18, { type: 'group', logic: 'or', children: [AGE_OVER_18] }],
    });

    el.querySelector<HTMLButtonElement>('[aria-label="הסרת התנאי"]')!.click();
    fixture.detectChanges();
    expect(host.group().children).toEqual([{ type: 'group', logic: 'or', children: [AGE_OVER_18] }]);

    el.querySelector<HTMLButtonElement>('.remove-group')!.click();
    fixture.detectChanges();
    expect(host.group().children).toEqual([]);
  });

  it('keeps a value only while the operator still takes it', async () => {
    render('employee', { type: 'group', logic: 'and', children: [AGE_OVER_18] });
    await (await selectIn('app-operator-select')).clickOptions({ text: 'קטן מ' });
    expect(host.group().children[0]).toEqual({ ...AGE_OVER_18, operator: 'lt' });

    await (await selectIn('app-operator-select')).clickOptions({ text: 'בין' });
    expect(host.group().children[0]).toEqual({ type: 'condition', field: 'age', operator: 'between' });
  });

  it('a yes/no field is answered with yes or no only - no operator, no other field', async () => {
    render('employee', { type: 'group', logic: 'and', children: [AGE_OVER_18] });
    expect(el.querySelector('app-operator-select')).not.toBeNull();
    expect(el.textContent).toContain('שדה אחר');

    const field = await loader.getHarness(MatAutocompleteHarness);
    await field.clear();
    await field.enterText('עובד פעיל');
    await field.selectOption({ text: /עובד פעיל/ });

    expect(host.group().children[0]).toEqual({ type: 'condition', field: 'isActive', operator: 'eq' });
    expect(el.querySelector('app-operator-select')).toBeNull();
    expect(el.textContent).not.toContain('שדה אחר');
    expect(el.querySelector('.row.yes-no')).not.toBeNull();
  });

  it('a saved yes/no condition worded otherwise keeps its full row', () => {
    render('employee', { type: 'group', logic: 'and', children: [{ type: 'condition', field: 'isActive', operator: 'isEmpty' }] });

    expect(el.querySelector('app-operator-select')).not.toBeNull();
    expect(el.querySelector('.row.yes-no')).toBeNull();
  });

  it('a new field keeps the operator if it applies, and starts without a value', async () => {
    render('employee', { type: 'group', logic: 'and', children: [AGE_OVER_18] });
    const field = await loader.getHarness(MatAutocompleteHarness);

    await field.clear();
    await field.enterText('שכר');
    await field.selectOption({ text: /שכר חודשי/ });
    expect(host.group().children[0]).toEqual({ type: 'condition', field: 'salary', operator: 'gt' });

    await field.clear();
    await field.enterText('שם מלא');
    await field.selectOption({ text: /שם מלא/ });
    expect(host.group().children[0]).toEqual({ type: 'condition', field: 'fullName', operator: 'eq' }); // "גדול מ" is not for text
  });

  it('read-only shows the same structure as text, with nothing to edit', () => {
    render('employee', {
      type: 'group', logic: 'and', children: [
        AGE_OVER_18,
        { type: 'condition', field: 'salary', operator: 'between', operand: { kind: 'literal', value: [5000, 9000] } },
        { type: 'related', relation: 'directReports', quantifier: 'none',
          where: { type: 'group', logic: 'and', children: [{ type: 'condition', field: 'isActive', operator: 'eq', operand: { kind: 'literal', value: true } }] } },
      ],
    });
    host.readonly.set(true);
    fixture.detectChanges();

    // worded like the server's sentences
    const lines = Array.from(el.querySelectorAll('p.text')).map((p) => p.textContent?.replace(/\s+/g, ' ').trim());
    expect(lines).toEqual(['גיל גדול מ-18', 'שכר חודשי בין 5,000 ₪ ל-9,000 ₪', 'אין עובדים כפופים', 'עובד פעיל: כן']);
    expect(el.querySelector('.logic-text')?.textContent).toBe('כל התנאים מתקיימים');
    expect(el.querySelectorAll('button, input, mat-select').length).toBe(0);
  });

  it("shows the field's meaning on one line under the row, not squeezed under the field box", () => {
    render('employee', { type: 'group', logic: 'and', children: [AGE_OVER_18, { type: 'condition', field: 'nosuch', operator: 'eq' }] });
    const [known, missing] = Array.from(el.querySelectorAll('app-condition-row'));

    expect(known.querySelector('.field-hint')?.textContent).toBe('גיל העובד בשנים מלאות.');
    expect(known.querySelector('app-field-picker mat-hint')).toBeNull();
    // a field the catalog no longer has is still reported on the field itself
    expect(missing.querySelector('app-field-picker mat-hint')?.textContent).toContain('השדה שנבחר אינו קיים עוד');
  });

  it("shows the server's errors next to the condition they are about", () => {
    render('employee', { type: 'group', logic: 'and', children: [AGE_OVER_18, { type: 'condition', field: '', operator: 'eq' }] });
    host.errorsFor.set((path) =>
      path.length === 1 && path[0] === 1 ? [{ path: 'filter.children[1].field', code: 'emptyPath', message: 'לא נבחר שדה.', isIncomplete: false }] : [],
    );
    fixture.detectChanges();

    const rows = el.querySelectorAll('app-condition-row');
    expect(rows[0].querySelector('.error')).toBeNull();
    expect(rows[1].querySelector('.error')?.textContent).toContain('לא נבחר שדה.');
  });
});
