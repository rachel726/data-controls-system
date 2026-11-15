import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom, forkJoin } from 'rxjs';
import { CatalogApi } from '../api/catalog-api.service';
import { DataType, EntityDto, FieldDto, LimitsDto, OperatorDto, RelationDto } from '../models/catalog.models';

/** A field the user can pick (from the server's list), possibly through relations: "שם המחלקה" = path "department.name". */
export interface FieldOption {
  readonly path: string;
  readonly field: FieldDto;
  /** The record that owns the field, as the server words it: "עובד", "מחלקה", "מחלקה של מנהל ישיר". */
  readonly owner: string;
  /** The server's name for it: "שם המחלקה", "שכר חודשי של מנהל ישיר". */
  readonly label: string;
}

/**
 * The business catalog, operators and limits, loaded once and shared by every screen (signals).
 * Derives what the UI offers - fields reachable from an entity, operators for a data type, how much may be added -
 * from the server's rules, so the client never hard-codes them.
 */
@Injectable({ providedIn: 'root' })
export class CatalogStore {
  private readonly api = inject(CatalogApi);
  private loading: Promise<void> | null = null;

  private readonly _entities = signal<readonly EntityDto[]>([]);
  private readonly _operators = signal<readonly OperatorDto[]>([]);
  private readonly _limits = signal<LimitsDto | null>(null);
  private readonly _error = signal<string | null>(null);

  readonly entities = this._entities.asReadonly();
  readonly operators = this._operators.asReadonly();
  /** Null until loaded: nothing limited by them (adding, saving, asking the AI) is offered before. */
  readonly limits = this._limits.asReadonly();
  readonly error = this._error.asReadonly();
  readonly loaded = computed(() => this._entities().length > 0);

  private readonly entityByKey = computed(() => new Map(this._entities().map((e) => [e.key, e])));

  /** Each entity's field list, built once per catalog: the server's paths joined with their field's details. */
  private readonly optionsByEntity = computed(() => {
    const options = new Map<string, { list: FieldOption[]; byPath: Map<string, FieldOption> }>();
    for (const entity of this._entities()) {
      const list = entity.fieldPaths.flatMap((p) => {
        const field = this.entityByKey().get(p.entityKey)?.fields.find((f) => f.key === p.fieldKey);
        return field ? [{ path: p.path, field, owner: p.owner, label: p.label }] : [];
      });
      options.set(entity.key, { list, byPath: new Map(list.map((o) => [o.path, o])) });
    }
    return options;
  });

  /** Loads the catalog once; concurrent and repeated calls share the same request. */
  load(): Promise<void> {
    this.loading ??= firstValueFrom(forkJoin([this.api.entities(), this.api.operators(), this.api.limits()]))
      .then(([entities, operators, limits]) => {
        this._entities.set(entities);
        this._operators.set(operators);
        this._limits.set(limits);
        this._error.set(null);
      })
      .catch((error: unknown) => {
        this.loading = null; // allow a retry
        this._error.set(error instanceof Error ? error.message : 'טעינת מבנה הנתונים נכשלה.');
      });
    return this.loading;
  }

  entity(key: string): EntityDto | undefined {
    return this.entityByKey().get(key);
  }

  /**
   * The fields a control on `entityKey` may use, as the server lists them (its own, then those of related records,
   * up to the server's limit, under the server's names). The client never works out paths, limits or names.
   */
  fieldOptions(entityKey: string): readonly FieldOption[] {
    return this.optionsByEntity().get(entityKey)?.list ?? [];
  }

  /** Resolves a stored path back to its option (undefined if the catalog no longer has it). */
  fieldOption(entityKey: string, path: string): FieldOption | undefined {
    return this.optionsByEntity().get(entityKey)?.byPath.get(path);
  }

  /** One-to-many relations of an entity, for "related records" conditions ("עובדים" of a department). */
  relatedRelations(entityKey: string): RelationDto[] {
    return this.entity(entityKey)?.relations.filter((r) => r.cardinality === 'oneToMany') ?? [];
  }

  operatorsFor(dataType: DataType): OperatorDto[] {
    return this._operators().filter((o) => o.allowedTypes.includes(dataType));
  }

  /** "לפני" for dates, "קטן מ" otherwise - the same wording rule as the server. */
  operatorLabel(operator: OperatorDto, dataType: DataType): string {
    return dataType === 'date' && operator.dateLabel ? operator.dateLabel : operator.label;
  }
}
