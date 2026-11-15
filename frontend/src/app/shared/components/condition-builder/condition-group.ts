import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { FilterNode, GroupNode, LogicalOperator } from '../../../core/models/control-definition';
import { DefinitionError } from '../../../core/models/control.models';
import { NodePath, TreeEdit, allowedAdditions } from '../../../core/models/definition-tree';
import { CatalogStore } from '../../../core/services/catalog.store';
import { ConditionRow } from './condition-row';
import { QUANTIFIER_LABELS, RelatedRow, ofEntity } from './related-row';

const EMPTY_GROUP: GroupNode = { type: 'group', logic: 'and', children: [] };
const NO_ERRORS = (): readonly DefinitionError[] => [];

/**
 * A group of conditions joined by "וגם" (all) or "או" (any), recursively: a child is a condition,
 * a nested group, or a "related records" condition with its own group on the related entity.
 * Stateless - every change is emitted as a TreeEdit for the owner to apply. Read-only mode shows the
 * same structure as text (e.g. an AI suggestion before approval). What may be added follows the server's limits.
 */
@Component({
  selector: 'app-condition-group',
  imports: [MatButtonModule, MatButtonToggleModule, MatIconModule, MatTooltipModule, ConditionRow, RelatedRow],
  templateUrl: './condition-group.html',
  styleUrl: './condition-builder.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ConditionGroup {
  private readonly catalog = inject(CatalogStore);

  readonly group = input.required<GroupNode>();
  /** Entity the group's conditions are about. */
  readonly entityKey = input.required<string>();
  readonly path = input<NodePath>([]);
  /** 1 for the root group. */
  readonly depth = input(1);
  /** True for the conditions of a "related records" node. */
  readonly insideRelated = input(false);
  readonly readonly = input(false);
  /** Server errors about the node at a path (not its children). */
  readonly errorsFor = input<(path: NodePath) => readonly DefinitionError[]>(NO_ERRORS);
  /** False once the definition has the maximum number of conditions. */
  readonly canAddCondition = input(true);
  /** A nested group can be removed; the root group and a related node's group cannot. */
  readonly removable = input(false);
  readonly emptyHint = input('עדיין אין תנאים. הוסיפו תנאי ראשון.');

  readonly edit = output<TreeEdit>();

  protected readonly emptyGroup = EMPTY_GROUP;
  protected readonly children = computed(() => this.group().children);
  protected readonly childPaths = computed(() => this.children().map((_, i): NodePath => [...this.path(), i]));
  protected readonly wherePaths = computed(() => this.childPaths().map((p): NodePath => [...p, 'where']));
  protected readonly errors = computed(() => this.errorsFor()(this.path()));

  protected readonly relations = computed(() => this.catalog.relatedRelations(this.entityKey()));
  /** "למשל: האם יש עובדים / פרויקטים של המחלקה" - from the catalog. */
  protected readonly relatedExample = computed(
    () => `למשל: האם יש ${this.relations().map((r) => r.displayName).join(' / ')} ${ofEntity(this.catalog.entity(this.entityKey())?.displayName)}`,
  );
  protected readonly can = computed(() => {
    const allowed = allowedAdditions(this.depth(), this.insideRelated(), this.catalog.limits()?.maxDepth ?? 0);
    const condition = this.canAddCondition();
    return { condition, group: condition && allowed.group, related: condition && allowed.related && this.relations().length > 0 };
  });

  protected readonly joiner = computed(() => (this.group().logic === 'and' ? 'וגם' : 'או'));

  /** "ללא תנאים נוספים - נבדק רק אם יש עובדים." */
  protected relatedHint(node: FilterNode): string {
    if (node.type !== 'related') {
      return '';
    }
    const name = this.relations().find((r) => r.key === node.relation)?.displayName ?? node.relation;
    return `ללא תנאים נוספים - נבדק רק אם ${QUANTIFIER_LABELS[node.quantifier]} ${name}.`;
  }

  /** The entity a "related records" node leads to, e.g. "employee" for a department's "עובדים". */
  protected targetOf(node: FilterNode): string | null {
    return node.type === 'related' ? (this.relations().find((r) => r.key === node.relation)?.targetEntityKey ?? null) : null;
  }

  protected setLogic(logic: LogicalOperator): void {
    this.edit.emit({ kind: 'logic', groupPath: this.path(), logic });
  }

  protected addCondition(): void {
    this.add({ type: 'condition', field: '', operator: 'eq' });
  }

  /** A nested group exists to mix "and" with "or", so it starts with the other logic and one empty condition. */
  protected addGroup(): void {
    this.add({
      type: 'group',
      logic: this.group().logic === 'and' ? 'or' : 'and',
      children: [{ type: 'condition', field: '', operator: 'eq' }],
    });
  }

  protected addRelated(): void {
    this.add({ type: 'related', relation: this.relations()[0].key, quantifier: 'any' });
  }

  protected remove(): void {
    this.edit.emit({ kind: 'remove', path: this.path() });
  }

  private add(node: FilterNode): void {
    this.edit.emit({ kind: 'add', groupPath: this.path(), node });
  }
}
