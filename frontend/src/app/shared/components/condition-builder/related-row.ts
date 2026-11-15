import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatSelectModule } from '@angular/material/select';
import { MatTooltipModule } from '@angular/material/tooltip';
import { RelatedNode, RelatedQuantifier } from '../../../core/models/control-definition';
import { DefinitionError } from '../../../core/models/control.models';
import { NodePath, TreeEdit } from '../../../core/models/definition-tree';
import { CatalogStore } from '../../../core/services/catalog.store';

export const QUANTIFIER_LABELS: Record<RelatedQuantifier, string> = { any: 'יש', none: 'אין' };

/**
 * The head of a "related records" condition: "יש / אין" + a one-to-many relation of the entity ("עובדים").
 * The optional conditions on those records are a nested group, rendered under it by the parent group.
 */
@Component({
  selector: 'app-related-row',
  imports: [MatButtonModule, MatFormFieldModule, MatIconModule, MatSelectModule, MatTooltipModule],
  template: `
    <div class="row related-head" [class.invalid]="errors().length > 0">
      <mat-icon class="related-icon" aria-hidden="true">account_tree</mat-icon>
      @if (readonly()) {
        <p class="text phrase">{{ quantifierLabels[node().quantifier] }} <span class="field">{{ relation()?.displayName ?? node().relation }}</span></p>
      } @else {
        <mat-form-field appearance="outline" subscriptSizing="dynamic" class="quantifier">
          <mat-label>יש או אין</mat-label>
          <mat-select [value]="node().quantifier" (selectionChange)="setQuantifier($event.value)">
            <mat-option value="any">{{ quantifierLabels.any }}</mat-option>
            <mat-option value="none">{{ quantifierLabels.none }}</mat-option>
          </mat-select>
        </mat-form-field>
        <mat-form-field appearance="outline" subscriptSizing="dynamic" class="relation">
          <mat-label>{{ ofEntity() }}</mat-label>
          <mat-select [value]="node().relation || null" (selectionChange)="setRelation($event.value)">
            @for (relation of relations(); track relation.key) {
              <mat-option [value]="relation.key">{{ relation.displayName }}</mat-option>
            }
          </mat-select>
          @if (relation()?.description; as description) {
            <mat-hint>{{ description }}</mat-hint>
          }
        </mat-form-field>
        <button matIconButton type="button" class="remove" (click)="edit.emit({ kind: 'remove', path: path() })" aria-label="הסרת התנאי" matTooltip="הסרת התנאי">
          <mat-icon>delete</mat-icon>
        </button>
      }
    </div>
    @for (error of errors(); track error.code + error.path) {
      <p class="error" role="alert"><mat-icon aria-hidden="true">error</mat-icon>{{ error.message }}</p>
    }
  `,
  styleUrl: './condition-builder.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RelatedRow {
  private readonly catalog = inject(CatalogStore);

  /** The entity whose related records are checked ("מחלקה" → its "עובדים"). */
  readonly entityKey = input.required<string>();
  readonly node = input.required<RelatedNode>();
  readonly path = input.required<NodePath>();
  readonly readonly = input(false);
  readonly errors = input<readonly DefinitionError[]>([]);

  readonly edit = output<TreeEdit>();

  protected readonly quantifierLabels = QUANTIFIER_LABELS;
  protected readonly relations = computed(() => this.catalog.relatedRelations(this.entityKey()));
  protected readonly relation = computed(() => this.relations().find((r) => r.key === this.node().relation));
  /** "של המחלקה" - whose records are checked, from the catalog. */
  protected readonly ofEntity = computed(() => ofEntity(this.catalog.entity(this.entityKey())?.displayName));

  protected setQuantifier(quantifier: RelatedQuantifier): void {
    this.edit.emit({ kind: 'update', path: this.path(), node: { ...this.node(), quantifier } });
  }

  /** Other records have other fields - the conditions on them start over. */
  protected setRelation(relation: string): void {
    if (relation !== this.node().relation) {
      this.edit.emit({ kind: 'update', path: this.path(), node: { type: 'related', relation, quantifier: this.node().quantifier } });
    }
  }
}

/** "של המחלקה", "של העובד" - the definite form of the entity's catalog name. */
export function ofEntity(displayName: string | undefined): string {
  return displayName ? `של ה${displayName}` : '';
}
