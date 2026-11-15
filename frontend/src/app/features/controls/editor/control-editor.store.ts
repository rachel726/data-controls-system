import { Injectable, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { EMPTY, Subject, catchError, debounceTime, firstValueFrom, of, switchMap } from 'rxjs';
import { ApiError } from '../../../core/api/api-error';
import { ControlsApi } from '../../../core/api/controls-api.service';
import { ControlDefinition, GroupNode, emptyDefinition } from '../../../core/models/control-definition';
import { Control, DefinitionError, ValidateResponse } from '../../../core/models/control.models';
import { DefinitionTree, NodePath, TreeEdit } from '../../../core/models/definition-tree';
import { CatalogStore } from '../../../core/services/catalog.store';

/** Wait for a pause in editing before asking the server what the control means. */
export const LIVE_CHECK_DEBOUNCE_MS = 400;

/**
 * The live check of the definition: idle (nothing to check), checking, valid, incomplete (a condition still being
 * filled in), invalid (a real mistake), or unavailable (server unreachable).
 */
export type CheckState = 'idle' | 'checking' | 'valid' | 'incomplete' | 'invalid' | 'unavailable';


interface EditorDraft {
  readonly name: string;
  readonly description: string;
  readonly definition: ControlDefinition | null;
}

const EMPTY_DRAFT: EditorDraft = { name: '', description: '', definition: null };

/**
 * State of one editor screen (provided per page): the draft control, dirty tracking against the last
 * saved version, a live server check of the definition (its Hebrew meaning, or errors per node),
 * and save with the server's errors kept for inline display.
 * The condition tree is changed only through immutable DefinitionTree operations.
 */
@Injectable()
export class ControlEditorStore {
  private readonly api = inject(ControlsApi);
  private readonly catalog = inject(CatalogStore);

  private readonly _id = signal<number | null>(null);
  private readonly _draft = signal<EditorDraft>(EMPTY_DRAFT);
  private readonly _saved = signal<string>(snapshot(EMPTY_DRAFT));
  private readonly _loading = signal(false);
  private readonly _saving = signal(false);
  private readonly _fieldErrors = signal<Readonly<Record<string, readonly string[]>>>({});
  private readonly _definitionErrors = signal<readonly DefinitionError[]>([]);
  private readonly _loadError = signal<string | null>(null);
  private readonly _check = signal<CheckState>('idle');
  private readonly _meaning = signal<string | null>(null);
  private readonly checks = new Subject<ControlDefinition | null>();

  readonly id = this._id.asReadonly();
  readonly isNew = computed(() => this._id() === null);
  readonly name = computed(() => this._draft().name);
  readonly description = computed(() => this._draft().description);
  readonly definition = computed(() => this._draft().definition);
  readonly rootEntity = computed(() => this._draft().definition?.rootEntity ?? null);
  readonly filter = computed(() => this._draft().definition?.filter ?? null);
  readonly conditionCount = computed(() => {
    const filter = this.filter();
    return filter ? DefinitionTree.countConditions(filter) : 0;
  });

  readonly loading = this._loading.asReadonly();
  readonly saving = this._saving.asReadonly();
  readonly loadError = this._loadError.asReadonly();
  readonly fieldErrors = this._fieldErrors.asReadonly();
  readonly definitionErrors = this._definitionErrors.asReadonly();
  readonly check = this._check.asReadonly();
  /** The definition's Hebrew meaning, from the server: "גיל קטן מ-18 וגם ...". */
  readonly meaning = this._meaning.asReadonly();

  readonly dirty = computed(() => snapshot(this._draft()) !== this._saved());
  /** Complete, and not known to be invalid. If the server could not be reached, saving is allowed - it validates again. */
  readonly canSave = computed(
    () =>
      !this._saving() &&
      this.name().trim().length > 0 &&
      this.name().length <= (this.catalog.limits()?.maxNameLength ?? 0) &&
      this.conditionCount() > 0 &&
      this._check() !== 'checking' &&
      this._check() !== 'incomplete' &&
      this._check() !== 'invalid',
  );

  constructor() {
    // Latest definition wins: a newer edit cancels the pending or in-flight check of an older one.
    this.checks
      .pipe(
        debounceTime(LIVE_CHECK_DEBOUNCE_MS),
        switchMap((definition) => (definition ? this.api.validate(definition).pipe(catchError(() => of(null))) : EMPTY)),
        takeUntilDestroyed(),
      )
      .subscribe((response) => this.showCheck(response));
  }

  // ---------- lifecycle ----------

  async load(id: number): Promise<void> {
    this._loading.set(true);
    this._loadError.set(null);
    try {
      this.accept(await firstValueFrom(this.api.get(id)));
    } catch (error) {
      this._loadError.set(error instanceof ApiError ? error.message : 'טעינת הבקרה נכשלה.');
    } finally {
      this._loading.set(false);
    }
  }

  /** Starts a new control, optionally pre-filled (e.g. from an AI suggestion). */
  startNew(prefill?: Partial<EditorDraft>): void {
    const draft = { ...EMPTY_DRAFT, ...prefill };
    this._id.set(null);
    this._draft.set(draft);
    this._saved.set(snapshot(EMPTY_DRAFT));
    this.clearErrors();
    this.checkDefinition(draft.definition);
  }

  /** Saves (create or update). Returns the saved control, or null when the server rejected it - errors are kept for display. */
  async save(): Promise<Control | null> {
    const definition = this.definition();
    if (!this.canSave() || !definition) {
      return null;
    }

    this._saving.set(true);
    this.clearErrors();
    const request = { name: this.name().trim(), description: this.description().trim() || null, definition };
    try {
      const id = this._id();
      const saved = await firstValueFrom(id === null ? this.api.create(request) : this.api.update(id, request));
      this.accept(saved);
      return saved;
    } catch (error) {
      if (!(error instanceof ApiError)) {
        throw error;
      }
      this.showServerErrors(error);
      return null;
    } finally {
      this._saving.set(false);
    }
  }

  // ---------- simple fields ----------

  setName(name: string): void {
    this.patch({ name });
    this.dropFieldError('name');
  }

  setDescription(description: string): void {
    this.patch({ description });
    this.dropFieldError('description');
  }

  /** Choosing what to check starts an empty condition tree (fields differ between entities). */
  setRootEntity(entityKey: string): void {
    if (entityKey !== this.rootEntity()) {
      this.patch({ definition: emptyDefinition(entityKey) });
      this._definitionErrors.set([]);
    }
  }

  // ---------- condition tree ----------

  /** Applies one change emitted by the condition builder. */
  apply(edit: TreeEdit): void {
    this.changeFilter((root) => DefinitionTree.apply(root, edit));
  }

  /** Server errors about this node itself (its field, operator, value...) - not about its children - for highlighting the row. */
  errorsAt(path: NodePath): DefinitionError[] {
    return DefinitionTree.errorsAbout(this._definitionErrors(), path);
  }

  // ---------- internals ----------

  private changeFilter(change: (root: GroupNode) => GroupNode): void {
    const definition = this.definition();
    if (!definition) {
      throw new Error('Choose what to check (the root entity) before adding conditions.');
    }
    this.patch({ definition: { ...definition, filter: change(definition.filter) } });
    this._definitionErrors.set([]); // stale once the tree changed; the live check re-validates
  }

  private patch(change: Partial<EditorDraft>): void {
    this._draft.update((draft) => ({ ...draft, ...change }));
    if (change.definition !== undefined) {
      this.checkDefinition(change.definition);
    }
  }

  /** Asks the server (debounced) what the definition means. An empty tree is not checked - there is nothing to say yet. */
  private checkDefinition(definition: ControlDefinition | null): void {
    const hasConditions = definition !== null && DefinitionTree.countConditions(definition.filter) > 0;
    this._check.set(hasConditions ? 'checking' : 'idle');
    if (!hasConditions) {
      this._meaning.set(null);
    }
    this.checks.next(hasConditions ? definition : null);
  }

  /** null = the server could not be reached; saving stays possible (the server validates on save). */
  private showCheck(response: ValidateResponse | null): void {
    if (!response) {
      this._check.set('unavailable');
      this._meaning.set(null);
      return;
    }
    // not filled in yet is not a mistake while editing: nothing red; the control cannot be saved until complete
    const mistakes = response.errors.filter((e) => !e.isIncomplete);
    this._check.set(response.isValid ? 'valid' : mistakes.length > 0 ? 'invalid' : 'incomplete');
    this._meaning.set(response.isValid ? response.description : null);
    this._definitionErrors.set(mistakes);
  }

  private accept(control: Control): void {
    const draft: EditorDraft = { name: control.name, description: control.description ?? '', definition: control.definition };
    this._id.set(control.id);
    this._draft.set(draft);
    this._saved.set(snapshot(draft));
    this.clearErrors();
    this._meaning.set(control.definitionDescription || null); // shown at once; the check confirms it against the current catalog
    this.checkDefinition(draft.definition);
  }

  /** 409 = duplicate name (shown on the name field); 400 = field / definition errors; anything else = one form-level message. */
  private showServerErrors(error: ApiError): void {
    const fieldErrors = error.status === 409 ? { name: [error.message] } : error.fieldErrors;
    const nothingSpecific = Object.keys(fieldErrors).length === 0 && error.definitionErrors.length === 0;

    this._fieldErrors.set(nothingSpecific ? { form: [error.message] } : fieldErrors);
    this._definitionErrors.set(error.definitionErrors);
  }

  private clearErrors(): void {
    this._fieldErrors.set({});
    this._definitionErrors.set([]);
  }

  private dropFieldError(field: string): void {
    if (this._fieldErrors()[field]) {
      const rest = { ...this._fieldErrors() };
      delete rest[field];
      this._fieldErrors.set(rest);
    }
  }
}

function snapshot(draft: EditorDraft): string {
  return JSON.stringify({ name: draft.name.trim(), description: draft.description.trim(), definition: draft.definition });
}
