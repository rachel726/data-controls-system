import { TextFieldModule } from '@angular/cdk/text-field';
import { Location } from '@angular/common';
import { ChangeDetectionStrategy, Component, OnInit, computed, inject, input } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectChange, MatSelectModule } from '@angular/material/select';
import { Router } from '@angular/router';
import { CatalogStore } from '../../../core/services/catalog.store';
import { NotifyService } from '../../../core/services/notify.service';
import { ConfirmService } from '../../../shared/components/confirm-dialog/confirm-dialog';
import { ControlDraft } from '../../../core/models/control.models';
import { NodePath } from '../../../core/models/definition-tree';
import { ConditionGroup } from '../../../shared/components/condition-builder/condition-group';
import { DefinitionDescription } from '../../../shared/components/definition-description/definition-description';
import { LoadingBar } from '../../../shared/components/loading-bar/loading-bar';
import { EmptyState } from '../../../shared/components/empty-state/empty-state';
import { PageHeader } from '../../../shared/components/page-header/page-header';
import { ControlEditorStore } from './control-editor.store';
import { inputValue } from '../../../core/utils/dom';

/** Create or edit a control: name, description, what to check, and its conditions. */
@Component({
  selector: 'app-control-editor-page',
  imports: [
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    LoadingBar,
    MatSelectModule,
    TextFieldModule,
    PageHeader,
    EmptyState,
    ConditionGroup,
    DefinitionDescription,
  ],
  providers: [ControlEditorStore],
  templateUrl: './control-editor.page.html',
  styleUrl: './control-editor.page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ControlEditorPage implements OnInit {
  /** Route parameter (bound via withComponentInputBinding); undefined when creating. */
  readonly id = input<string>();

  protected readonly inputValue = inputValue;
  protected readonly store = inject(ControlEditorStore);
  protected readonly catalog = inject(CatalogStore);
  private readonly router = inject(Router);
  private readonly location = inject(Location);
  private readonly confirm = inject(ConfirmService);
  private readonly notify = inject(NotifyService);

  /** From the server, with the catalog. */
  protected readonly limits = this.catalog.limits;
  /** What the meaning panel says while there is no sentence. The errors themselves are shown on their conditions. */
  protected readonly meaningNote = computed(() => {
    if (this.store.definitionErrors().length > 0) {
      return 'יש לתקן את ההערות שמסומנות בתנאים.';
    }
    switch (this.store.check()) {
      case 'incomplete':
        return 'השלימו את התנאים (שדה, תנאי וערך) כדי לראות כאן את משמעות הבקרה.';
      case 'unavailable':
        return 'לא ניתן לבדוק את הבקרה כרגע - היא תיבדק בשמירה.';
      default:
        return 'הוסיפו תנאי כדי לראות כאן את משמעות הבקרה.';
    }
  });
  /** Server errors next to the condition they are about. */
  protected readonly errorsFor = (path: NodePath) => this.store.errorsAt(path);

  /** From the address, so a control that failed to load is still "עריכת בקרה". */
  protected readonly title = computed(() => (this.id() ? 'עריכת בקרה' : 'בקרה חדשה'));
  /** Always a line (never null): a line that appears on the first change would push the whole page down. */
  protected readonly status = computed(() => {
    if (this.store.loadError()) {
      return null;   // nothing is being edited
    }
    if (this.store.dirty()) {
      return 'יש שינויים שלא נשמרו';
    }
    return this.id() ? 'כל השינויים נשמרו' : 'הגדירו שם, מה בודקים ומתי רשומה חריגה';
  });

  protected readonly rootEntityName = computed(() => {
    const key = this.store.rootEntity();
    return key ? (this.catalog.entity(key)?.displayNamePlural ?? key) : null;
  });

  ngOnInit(): void {
    void this.catalog.load();
    const id = Number(this.id());
    if (this.id() && Number.isInteger(id)) {
      void this.store.load(id);
    } else {
      this.store.startNew(this.draftFromNavigation());
    }
  }

  /** A new control may arrive pre-filled, e.g. an AI suggestion opened with "עריכה בעורך" (navigation state). */
  private draftFromNavigation(): ControlDraft | undefined {
    const draft = (this.location.getState() as { draft?: ControlDraft } | null)?.draft;
    return draft?.definition?.rootEntity && draft.definition.filter ? draft : undefined;
  }

  protected async changeRootEntity(event: MatSelectChange<string>): Promise<void> {
    const current = this.store.rootEntity();
    if (current && this.store.conditionCount() > 0) {
      const confirmed = await this.confirm.ask({
        title: 'החלפת סוג הנתונים',
        message: 'התנאים שהוגדרו מתאימים לסוג הנתונים הנוכחי ויימחקו. להמשיך?',
        confirmLabel: 'החלפה',
        danger: true,
      });
      if (!confirmed) {
        event.source.value = current; // keep showing the previous choice
        return;
      }
    }
    this.store.setRootEntity(event.value);
  }

  protected async save(): Promise<void> {
    const saved = await this.store.save();
    if (saved) {
      this.notify.success(`הבקרה "${saved.name}" נשמרה.`);
      void this.router.navigate(['/controls']);
    }
  }

  protected async cancel(): Promise<void> {
    if (this.store.dirty()) {
      const confirmed = await this.confirm.ask({
        title: 'ביטול השינויים',
        message: 'השינויים שלא נשמרו יאבדו. לצאת מהעורך?',
        confirmLabel: 'יציאה בלי לשמור',
        cancelLabel: 'המשך עריכה',
        danger: true,
      });
      if (!confirmed) {
        return;
      }
    }
    void this.router.navigate(['/controls']);
  }
}
