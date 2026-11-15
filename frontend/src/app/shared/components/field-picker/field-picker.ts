import { ChangeDetectionStrategy, Component, computed, effect, inject, input, output, signal } from '@angular/core';
import { MatAutocompleteModule, MatAutocompleteSelectedEvent } from '@angular/material/autocomplete';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatTooltipModule } from '@angular/material/tooltip';
import { DataType } from '../../../core/models/catalog.models';
import { CatalogStore, FieldOption } from '../../../core/services/catalog.store';
import { inputValue } from '../../../core/utils/dom';

interface FieldGroup {
  readonly label: string;
  readonly options: readonly FieldOption[];
}

const TYPE_ICONS: Record<DataType, string> = {
  text: 'text_fields',
  integer: 'tag',
  decimal: 'tag',
  date: 'calendar_today',
  boolean: 'toggle_on',
  enum: 'list',
};

/**
 * Pick a field in business terms. Lists the entity's own fields and fields reached through relations,
 * grouped by the record that owns them ("שדות של עובד", "שדות של מחלקה של מנהל ישיר"), with the catalog description as a tooltip and search in Hebrew.
 * Everything comes from the catalog; `filter` lets the caller restrict the choice (e.g. compatible types).
 */
@Component({
  selector: 'app-field-picker',
  imports: [MatAutocompleteModule, MatFormFieldModule, MatIconModule, MatInputModule, MatTooltipModule],
  template: `
    <mat-form-field appearance="outline" subscriptSizing="dynamic" class="field-picker">
      <mat-label>{{ label() }}</mat-label>
      @if (showTypeIcon() && selected(); as option) {
        <mat-icon matPrefix class="type-icon" aria-hidden="true">{{ icon(option) }}</mat-icon>
      }
      <input
        matInput
        type="text"
        [value]="query()"
        [matTooltip]="selected()?.label ?? ''"
        (input)="search($event)"
        (blur)="restore()"
        [matAutocomplete]="auto"
        [attr.aria-invalid]="invalid() || null"
        placeholder="חיפוש שדה"
      />
      <mat-autocomplete #auto="matAutocomplete" [displayWith]="labelOf" (optionSelected)="choose($event)" [hideSingleSelectionIndicator]="true">
        @for (group of groups(); track group.label) {
          <mat-optgroup [label]="group.label">
            @for (option of group.options; track option.path) {
              <mat-option [value]="option.path" [matTooltip]="option.field.description ?? ''" matTooltipPosition="before">
                <mat-icon class="type-icon" aria-hidden="true">{{ icon(option) }}</mat-icon>
                {{ option.field.displayName }}
              </mat-option>
            }
          </mat-optgroup>
        } @empty {
          <mat-option disabled>לא נמצאו שדות מתאימים</mat-option>
        }
      </mat-autocomplete>
      @if (invalid()) {
        <mat-hint class="field-error" role="alert">השדה שנבחר אינו קיים עוד - בחרו שדה אחר</mat-hint>
      } @else if (showDescription() && selected()?.field?.description; as description) {
        <mat-hint>{{ description }}</mat-hint>
      }
    </mat-form-field>
  `,
  styles: `
    .field-picker {
      width: 100%;
    }
    .type-icon {
      color: var(--mat-sys-on-surface-variant);
      font-size: 20px;
      width: 20px;
      height: 20px;
    }
    .field-error {
      color: var(--mat-sys-error);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FieldPicker {
  private readonly catalog = inject(CatalogStore);

  /** The entity the paths are relative to (the control's root, or a related entity inside "related records"). */
  readonly entityKey = input.required<string>();
  /** Selected field path, e.g. "department.isActive"; null when nothing is chosen yet. */
  readonly value = input<string | null>(null);
  readonly label = input('שדה');
  /** The chosen field's meaning under the box. A compact row shows it elsewhere (a wrong field is always reported). */
  readonly showDescription = input(true);
  /** The chosen field's type icon. Off where the type is already known (a compared field has the row's type). */
  readonly showTypeIcon = input(true);
  /** Optional restriction, e.g. only fields whose type is comparable. */
  readonly filter = input<((option: FieldOption) => boolean) | null>(null);

  readonly valueChange = output<string>();

  protected readonly query = signal('');

  private readonly options = computed(() => {
    const filter = this.filter();
    const all = this.catalog.fieldOptions(this.entityKey());
    return filter ? all.filter(filter) : all;
  });

  protected readonly selected = computed(() => {
    const value = this.value();
    return value ? this.catalog.fieldOption(this.entityKey(), value) : undefined;
  });

  /** A stored path that the catalog no longer has (the control must be fixed). */
  protected readonly invalid = computed(() => this.value() !== null && this.catalog.loaded() && !this.selected());

  protected readonly groups = computed<FieldGroup[]>(() => {
    const term = this.query().trim();
    const showAll = term === '' || term === this.selected()?.label;
    const groups = new Map<string, FieldOption[]>();

    for (const option of this.options()) {
      if (!showAll && !option.label.includes(term)) {
        continue;
      }
      const group = `שדות של ${option.owner}`;
      groups.set(group, [...(groups.get(group) ?? []), option]);
    }

    return [...groups].map(([label, options]) => ({ label, options }));
  });

  constructor() {
    // Show the chosen field's business label whenever the value changes from outside.
    effect(() => this.query.set(this.selected()?.label ?? this.value() ?? ''));
  }

  protected icon(option: FieldOption): string {
    return TYPE_ICONS[option.field.dataType];
  }

  protected search(event: Event): void {
    this.query.set(inputValue(event));
  }

  /** What the box shows for a chosen option: its business name, never the path itself ("department.isActive"). */
  protected readonly labelOf = (path: string | null): string =>
    path ? (this.catalog.fieldOption(this.entityKey(), path)?.label ?? path) : '';

  protected choose(event: MatAutocompleteSelectedEvent): void {
    const path = event.option.value as string;
    this.query.set(this.labelOf(path));
    this.valueChange.emit(path);
  }

  /** Leaving the field without choosing keeps the previous choice. */
  protected restore(): void {
    this.query.set(this.selected()?.label ?? '');
  }
}
