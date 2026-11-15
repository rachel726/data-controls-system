import { ChangeDetectionStrategy, Component, OnInit, computed, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Router, RouterLink } from '@angular/router';
import { DefinitionTree, NodePath } from '../../core/models/definition-tree';
import { CatalogStore } from '../../core/services/catalog.store';
import { NotifyService } from '../../core/services/notify.service';
import { ConditionGroup } from '../../shared/components/condition-builder/condition-group';
import { DefinitionDescription } from '../../shared/components/definition-description/definition-description';
import { LoadingBar } from '../../shared/components/loading-bar/loading-bar';
import { EmptyState } from '../../shared/components/empty-state/empty-state';
import { PageHeader } from '../../shared/components/page-header/page-header';
import { AiAssistantStore } from './ai-assistant.store';
import { inputValue } from '../../core/utils/dom';

/**
 * Stage B: describe a control in free text → the AI's suggestion, shown for approval: how it understood the
 * request, its assumptions, the control's meaning and conditions (read-only) → edit it in the editor, or approve
 * and save (and run). Nothing is saved or run before the user approves.
 */
@Component({
  selector: 'app-ai-assistant-page',
  imports: [
    RouterLink,
    MatButtonModule,
    MatChipsModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    LoadingBar,
    MatTooltipModule,
    PageHeader,
    EmptyState,
    DefinitionDescription,
    ConditionGroup,
  ],
  providers: [AiAssistantStore],
  templateUrl: './ai-assistant.page.html',
  styleUrl: './ai-assistant.page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AiAssistantPage implements OnInit {
  protected readonly inputValue = inputValue;
  protected readonly store = inject(AiAssistantStore);
  protected readonly catalog = inject(CatalogStore);
  private readonly router = inject(Router);
  private readonly notify = inject(NotifyService);

  /** From the server, with the catalog. */
  protected readonly limits = this.catalog.limits;

  protected readonly rootEntityName = computed(() => {
    const key = this.store.result()?.definition?.rootEntity;
    return key ? (this.catalog.entity(key)?.displayNamePlural ?? key) : null;
  });

  /** The validation errors next to the condition they are about. */
  protected readonly errorsFor = (path: NodePath) => DefinitionTree.errorsAbout(this.store.result()?.validationErrors ?? [], path);

  ngOnInit(): void {
    void this.catalog.load();
    void this.store.loadExamples();
  }

  protected useExample(text: string): void {
    this.store.setText(text);
    void this.store.suggest();
  }

  /** Ctrl+Enter sends, like in chat boxes. */
  protected onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter' && (event.ctrlKey || event.metaKey)) {
      event.preventDefault();
      void this.store.suggest();
    }
  }

  protected editInEditor(): void {
    const draft = this.store.draft();
    if (draft) {
      void this.router.navigate(['/controls/new'], { state: { draft } });
    }
  }

  protected async approve(run: boolean): Promise<void> {
    const saved = await this.store.approve();
    if (!saved) {
      return;
    }
    this.notify.success(`הבקרה "${saved.name}" נשמרה.`);
    await this.router.navigate(run ? ['/controls', saved.id, 'results'] : ['/controls'], run ? { queryParams: { run: true } } : {});
  }
}
