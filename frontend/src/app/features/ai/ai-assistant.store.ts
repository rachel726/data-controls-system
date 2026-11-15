import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AiApi } from '../../core/api/ai-api.service';
import { ApiError } from '../../core/api/api-error';
import { ControlsApi } from '../../core/api/controls-api.service';
import { AiExample, AiSuggestionResult } from '../../core/models/ai.models';
import { Control, ControlDraft } from '../../core/models/control.models';
import { CatalogStore } from '../../core/services/catalog.store';

/**
 * State of the free-text screen (provided per page): the request, the AI's suggestion for approval, and saving
 * it once approved. Nothing is saved or run before the user approves. Only the latest request may update the
 * screen, so an older slow answer never replaces a newer one.
 */
@Injectable()
export class AiAssistantStore {
  private readonly ai = inject(AiApi);
  private readonly controls = inject(ControlsApi);
  private readonly catalog = inject(CatalogStore);
  private latest = 0;

  private readonly _examples = signal<readonly AiExample[]>([]);
  private readonly _text = signal('');
  private readonly _suggesting = signal(false);
  private readonly _result = signal<AiSuggestionResult | null>(null);
  private readonly _error = signal<string | null>(null);
  private readonly _name = signal('');
  private readonly _description = signal('');
  private readonly _saving = signal(false);
  private readonly _nameError = signal<string | null>(null);
  private readonly _saveError = signal<string | null>(null);

  /** One-click examples, served by the AI. None while it is unreachable - asking then says so. */
  readonly examples = this._examples.asReadonly();
  readonly text = this._text.asReadonly();
  readonly suggesting = this._suggesting.asReadonly();
  readonly result = this._result.asReadonly();
  readonly error = this._error.asReadonly();
  readonly name = this._name.asReadonly();
  readonly description = this._description.asReadonly();
  readonly saving = this._saving.asReadonly();
  readonly nameError = this._nameError.asReadonly();
  readonly saveError = this._saveError.asReadonly();

  readonly canSuggest = computed(
    () => this._text().trim().length > 0 && this._text().length <= (this.catalog.limits()?.maxAiTextLength ?? 0),
  );
  /** Approval is possible only for a valid suggestion with a name. */
  readonly canApprove = computed(() => {
    const result = this._result();
    const name = this._name().trim();
    const maxName = this.catalog.limits()?.maxNameLength ?? 0;
    return !!result?.isValid && !!result.definition && name.length > 0 && name.length <= maxName && !this._saving();
  });

  async loadExamples(): Promise<void> {
    try {
      this._examples.set(await firstValueFrom(this.ai.examples()));
    } catch {
      this._examples.set([]);
    }
  }

  setText(text: string): void {
    this._text.set(text);
  }

  setName(name: string): void {
    this._name.set(name);
    this._nameError.set(null);
  }

  setDescription(description: string): void {
    this._description.set(description);
  }

  async suggest(): Promise<void> {
    if (!this.canSuggest()) {
      return;
    }

    const ticket = ++this.latest;
    this._suggesting.set(true);
    this._error.set(null);
    this._result.set(null);
    this._saveError.set(null);
    this._nameError.set(null);
    try {
      const result = await firstValueFrom(this.ai.suggestControl(this._text().trim()));
      if (ticket === this.latest) {
        this._result.set(result);
        this._name.set(result.name);
        this._description.set(result.description ?? '');
      }
    } catch (error) {
      if (ticket === this.latest) {
        this._error.set(error instanceof ApiError ? error.message : 'לא ניתן לקבל הצעה כרגע. נסו שוב.');
      }
    } finally {
      if (ticket === this.latest) {
        this._suggesting.set(false);
      }
    }
  }

  /** The suggestion as an unsaved draft, for the editor. */
  draft(): ControlDraft | null {
    const definition = this._result()?.definition;
    return definition ? { name: this._name().trim(), description: this._description().trim(), definition } : null;
  }

  /** Saves the approved suggestion as a regular control. Returns it, or null when the server refused (reason kept). */
  async approve(): Promise<Control | null> {
    const draft = this.draft();
    if (!this.canApprove() || !draft) {
      return null;
    }

    this._saving.set(true);
    this._saveError.set(null);
    try {
      return await firstValueFrom(
        this.controls.create({ name: draft.name, description: draft.description || null, definition: draft.definition }),
      );
    } catch (error) {
      if (!(error instanceof ApiError)) {
        throw error;
      }
      // A taken or invalid name belongs on the name field; anything else is one message.
      const nameError = error.status === 409 ? error.message : error.fieldErrors['name']?.[0];
      if (nameError) {
        this._nameError.set(nameError);
      } else {
        this._saveError.set(error.message);
      }
      return null;
    } finally {
      this._saving.set(false);
    }
  }
}
