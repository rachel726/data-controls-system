import { COMMA, ENTER } from '@angular/cdk/keycodes';
import { ChangeDetectionStrategy, Component, computed, inject, input, output, signal } from '@angular/core';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatChipInputEvent, MatChipsModule } from '@angular/material/chips';
import { MAT_DATE_LOCALE, provideNativeDateAdapter } from '@angular/material/core';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { OperatorDto } from '../../../core/models/catalog.models';
import { LiteralScalar, Operand } from '../../../core/models/control-definition';
import { CatalogStore, FieldOption } from '../../../core/services/catalog.store';
import { FieldPicker } from '../field-picker/field-picker';
import { LiteralValues } from './literal-values';
import { inputValue } from '../../../core/utils/dom';

type Mode = 'literal' | 'field';

/**
 * The right-hand side of a condition, shaped by the operator and the field's data type:
 * nothing ("ריק"), one value, a from–to range, a list - or another field to compare with.
 * Emits the operand in the definition contract, or null while no value is entered.
 */
@Component({
  selector: 'app-value-input',
  imports: [
    MatButtonToggleModule,
    MatChipsModule,
    MatDatepickerModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatSelectModule,
    FieldPicker,
  ],
  // The date adapter lives with the only component that needs it, so it loads lazily with the editor.
  providers: [provideNativeDateAdapter(), { provide: MAT_DATE_LOCALE, useValue: 'he-IL' }],
  templateUrl: './value-input.html',
  styleUrl: './value-input.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ValueInput {
  private readonly catalog = inject(CatalogStore);
  /** The condition's field (type, unit, allowed values). */
  readonly field = input.required<FieldOption>();
  readonly operator = input.required<OperatorDto>();
  readonly operand = input<Operand | null | undefined>(null);
  /** Entity the condition's paths are relative to (for choosing another field). */
  readonly entityKey = input.required<string>();
  /** False hides "ערך / שדה אחר" (a yes/no field is answered with yes or no). */
  readonly allowFieldOperand = input(true);

  readonly operandChange = output<Operand | null>();

  protected readonly separatorKeys = [ENTER, COMMA] as const;
  protected readonly rangeIndexes = [0, 1] as const;

  /** The server's limit of a text value - the box stops there instead of the server rejecting it. */
  protected readonly maxTextLength = computed(() => this.catalog.limits()?.maxTextValueLength ?? null);
  protected readonly type = computed(() => this.field().field.dataType);
  protected readonly shape = computed(() => this.operator().shape);
  protected readonly canCompareToField = computed(
    () => this.allowFieldOperand() && this.operator().allowsFieldOperand && this.shape() === 'value',
  );

  /**
   * The user's choice between a value and another field. Kept locally because choosing "another field" clears the
   * operand (nothing is picked yet) - deriving the mode from the operand alone would flip straight back to "value".
   */
  private readonly prefersField = signal(false);
  protected readonly mode = computed<Mode>(() =>
    this.canCompareToField() && (this.operand()?.kind === 'field' || this.prefersField()) ? 'field' : 'literal',
  );

  protected readonly literal = computed(() => {
    const operand = this.operand();
    return operand?.kind === 'literal' ? operand.value : null;
  });
  protected readonly scalar = computed(() => {
    const value = this.literal();
    return value === null || Array.isArray(value) ? null : (value as LiteralScalar);
  });
  protected readonly items = computed<readonly (LiteralScalar | null)[]>(() => {
    const value = this.literal();
    return Array.isArray(value) ? value : [];
  });
  protected readonly comparedField = computed(() => {
    const operand = this.operand();
    return operand?.kind === 'field' ? operand.field : null;
  });

  /** Another field may be chosen only if it is comparable and is not the condition's own field. */
  protected readonly comparableFields = computed(() => {
    const own = this.field();
    return (option: FieldOption) => option.path !== own.path && LiteralValues.areComparable(own.field.dataType, option.field.dataType);
  });

  protected setMode(mode: Mode): void {
    if (mode === this.mode()) {
      return;
    }
    this.prefersField.set(mode === 'field');
    this.operandChange.emit(null); // a value and a field are not interchangeable - start the new one empty
  }

  protected setText(event: Event): void {
    this.emitScalar(LiteralValues.fromText(inputValue(event), this.type()));
  }

  protected setDate(date: Date | null): void {
    this.emitScalar(LiteralValues.fromDate(date));
  }

  protected setScalar(value: LiteralScalar | null): void {
    this.emitScalar(value);
  }

  protected setRangeText(index: 0 | 1, event: Event): void {
    this.setRange(index, LiteralValues.fromText(inputValue(event), this.type()));
  }

  protected setRangeDate(index: 0 | 1, date: Date | null): void {
    this.setRange(index, LiteralValues.fromDate(date));
  }

  protected addItem(event: MatChipInputEvent): void {
    const value = LiteralValues.fromText(event.value, this.type());
    event.chipInput.clear();
    if (value !== null && !this.items().includes(value)) {
      this.emitList([...this.items(), value]);
    }
  }

  protected removeItem(item: LiteralScalar | null): void {
    this.emitList(this.items().filter((i) => i !== item));
  }

  protected setList(values: LiteralScalar[]): void {
    this.emitList(values);
  }

  protected setComparedField(path: string): void {
    this.operandChange.emit({ kind: 'field', field: path });
  }

  protected toDate(value: unknown): Date | null {
    return LiteralValues.toDate(value);
  }

  protected labelOf(value: LiteralScalar | null): string {
    return this.field().field.allowedValues.find((a) => a.value === value)?.label ?? String(value ?? '');
  }

  private setRange(index: 0 | 1, value: LiteralScalar | null): void {
    const range: (LiteralScalar | null)[] = [this.items()[0] ?? null, this.items()[1] ?? null];
    range[index] = value;
    // Keep a half-filled range on screen; the server reports the missing value.
    this.operandChange.emit(range[0] === null && range[1] === null ? null : { kind: 'literal', value: range });
  }

  private emitScalar(value: LiteralScalar | null): void {
    this.operandChange.emit(value === null ? null : { kind: 'literal', value });
  }

  private emitList(values: readonly (LiteralScalar | null)[]): void {
    this.operandChange.emit(values.length === 0 ? null : { kind: 'literal', value: values });
  }
}
