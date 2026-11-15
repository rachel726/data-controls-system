import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { ConditionOperator, OperatorDto } from '../../../core/models/catalog.models';
import { ConditionNode, Operand } from '../../../core/models/control-definition';
import { DefinitionError } from '../../../core/models/control.models';
import { NodePath, TreeEdit } from '../../../core/models/definition-tree';
import { CatalogStore } from '../../../core/services/catalog.store';
import { FieldPicker } from '../field-picker/field-picker';
import { OperatorSelect } from '../operator-select/operator-select';
import { ValueInput } from '../value-input/value-input';
import { conditionPhrase, operandText } from './operand-text';

/**
 * One condition: field · operator · value, in business terms. A yes/no field is answered with yes or no only
 * ("מחלקה פעילה | כן / לא"): no operator to pick and no comparison with another field.
 * Keeps the condition consistent while editing: a new field keeps the operator only if it still applies,
 * and a value is kept only while the operator still takes the same kind of value.
 */
@Component({
  selector: 'app-condition-row',
  imports: [MatButtonModule, MatIconModule, MatTooltipModule, FieldPicker, OperatorSelect, ValueInput],
  template: `
    <div class="row" [class.invalid]="errors().length > 0" [class.yes-no]="yesNo()">
      @if (readonly()) {
        <!-- one phrase, worded like the server's sentences: "גיל קטן מ-18", "עובד פעיל: כן" -->
        <p class="text phrase"><span class="field">{{ field()?.label ?? node().field }}</span>{{ phrase() }}</p>
      } @else {
        <app-field-picker class="field" [entityKey]="entityKey()" [value]="node().field || null" [showDescription]="false" (valueChange)="setField($event)" />
        @if (!yesNo()) {
          <app-operator-select class="operator" [dataType]="field()?.field?.dataType ?? null" [value]="node().operator" (valueChange)="setOperator($event)" />
        }
        @if (field(); as field) {
          @if (operator(); as operator) {
            <app-value-input
              class="value"
              [entityKey]="entityKey()"
              [field]="field"
              [operator]="operator"
              [operand]="node().operand"
              [allowFieldOperand]="!yesNo()"
              (operandChange)="setOperand($event)"
            />
          }
        }
        <button matIconButton type="button" class="remove" (click)="edit.emit({ kind: 'remove', path: path() })" aria-label="הסרת התנאי" matTooltip="הסרת התנאי">
          <mat-icon>delete</mat-icon>
        </button>
        <!-- the field's meaning, on one line under the whole row instead of squeezed under the narrow field box -->
        @if (field()?.field?.description; as description) {
          <p class="field-hint">{{ description }}</p>
        }
      }
    </div>
    @for (error of errors(); track error.code + error.path) {
      <p class="error" role="alert"><mat-icon aria-hidden="true">error</mat-icon>{{ error.message }}</p>
    }
  `,
  styleUrl: './condition-builder.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ConditionRow {
  private readonly catalog = inject(CatalogStore);

  /** Entity the field paths are relative to (root, or the related entity inside "related records"). */
  readonly entityKey = input.required<string>();
  readonly node = input.required<ConditionNode>();
  readonly path = input.required<NodePath>();
  readonly readonly = input(false);
  /** Server errors about this condition. */
  readonly errors = input<readonly DefinitionError[]>([]);

  readonly edit = output<TreeEdit>();

  protected readonly field = computed(() => {
    const path = this.node().field;
    return path ? this.catalog.fieldOption(this.entityKey(), path) : undefined;
  });

  /** The operator, if it applies to the chosen field. */
  protected readonly operator = computed(() => {
    const type = this.field()?.field.dataType;
    return type ? this.catalog.operatorsFor(type).find((o) => o.key === this.node().operator) : undefined;
  });

  /**
   * A yes/no field answered with yes or no - the way the editor builds one. A saved condition worded otherwise
   * (e.g. "ריק", or compared with another field) keeps its full row, so nothing is hidden or changed silently.
   */
  protected readonly yesNo = computed(
    () => this.field()?.field.dataType === 'boolean' && this.node().operator === YES_NO_OPERATOR && this.node().operand?.kind !== 'field',
  );

  /** Read-only wording. A field the catalog does not know (e.g. invented by the AI) is still shown as sent. */
  protected readonly phrase = computed(() => {
    const field = this.field();
    const node = this.node();
    const operator = this.operator() ?? this.catalog.operators().find((o) => o.key === node.operator);
    const label = operator ? (field ? this.catalog.operatorLabel(operator, field.field.dataType) : operator.label) : node.operator;
    const shape = operator?.shape ?? 'value';

    const value = field
      ? operandText(field, shape, node.operand, (path) => this.catalog.fieldOption(this.entityKey(), path)?.label ?? path)
      : rawOperand(node.operand);
    const isYesNo = field?.field.dataType === 'boolean' && node.operator === 'eq' && node.operand?.kind === 'literal';
    const phrase = conditionPhrase(label, shape, isYesNo, value, field?.field.dataType === 'boolean');
    return phrase.startsWith(':') ? phrase : ` ${phrase}`;
  });

  protected setField(path: string): void {
    const option = this.catalog.fieldOption(this.entityKey(), path);
    if (!option || path === this.node().field) {
      return;
    }
    const operators = this.catalog.operatorsFor(option.field.dataType);
    const operator =
      (option.field.dataType === 'boolean' ? operators.find((o) => o.key === YES_NO_OPERATOR) : undefined) ??
      operators.find((o) => o.key === this.node().operator) ??
      operators[0];
    // A value belongs to the previous field (its type, unit, allowed values) - start empty.
    this.update({ type: 'condition', field: path, operator: operator.key });
  }

  protected setOperator(key: ConditionOperator): void {
    const type = this.field()?.field.dataType;
    const next = type ? this.catalog.operatorsFor(type).find((o) => o.key === key) : undefined;
    if (!next) {
      return;
    }
    const operand = keepsOperand(this.operator(), next, this.node().operand) ? this.node().operand : null;
    this.update(withOperand({ type: 'condition', field: this.node().field, operator: key }, operand));
  }

  protected setOperand(operand: Operand | null): void {
    this.update(withOperand({ type: 'condition', field: this.node().field, operator: this.node().operator }, operand));
  }

  private update(node: ConditionNode): void {
    this.edit.emit({ kind: 'update', path: this.path(), node });
  }
}

/** A yes/no field is answered with "equals yes / no". */
const YES_NO_OPERATOR: ConditionOperator = 'eq';

/** "גדול מ" → "קטן מ" keeps 18; "גדול מ" → "בין" or "ריק" does not; a compared field needs an operator that allows it. */
function keepsOperand(previous: OperatorDto | undefined, next: OperatorDto, operand: Operand | null | undefined): boolean {
  if (!operand || !previous || previous.shape !== next.shape) {
    return false;
  }
  return operand.kind !== 'field' || next.allowsFieldOperand;
}

function rawOperand(operand: Operand | null | undefined): string {
  if (!operand) {
    return '';
  }
  return operand.kind === 'field' ? operand.field : JSON.stringify(operand.value);
}

function withOperand(node: ConditionNode, operand: Operand | null | undefined): ConditionNode {
  return operand ? { ...node, operand } : node;
}
