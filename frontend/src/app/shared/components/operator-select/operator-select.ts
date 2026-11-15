import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { ConditionOperator, DataType } from '../../../core/models/catalog.models';
import { CatalogStore } from '../../../core/services/catalog.store';

/** The operators that make sense for a field's data type, worded for that type ("לפני" for dates). Rules come from the server. */
@Component({
  selector: 'app-operator-select',
  imports: [MatFormFieldModule, MatSelectModule],
  template: `
    <mat-form-field appearance="outline" subscriptSizing="dynamic" class="operator-select">
      <mat-label>תנאי</mat-label>
      <mat-select [value]="value()" (selectionChange)="valueChange.emit($event.value)" [disabled]="!dataType()">
        @for (option of options(); track option.key) {
          <mat-option [value]="option.key">{{ option.label }}</mat-option>
        }
      </mat-select>
    </mat-form-field>
  `,
  styles: `
    .operator-select {
      width: 100%;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class OperatorSelect {
  private readonly catalog = inject(CatalogStore);

  /** Data type of the chosen field; null disables the select until a field is chosen. */
  readonly dataType = input<DataType | null>(null);
  readonly value = input<ConditionOperator | null>(null);
  readonly valueChange = output<ConditionOperator>();

  protected readonly options = computed(() => {
    const type = this.dataType();
    return type
      ? this.catalog.operatorsFor(type).map((o) => ({ key: o.key, label: this.catalog.operatorLabel(o, type) }))
      : [];
  });
}
