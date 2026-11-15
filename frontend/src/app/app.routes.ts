import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'controls' },
  {
    path: 'controls',
    title: 'הבקרות שלי',
    loadComponent: () =>
      import('./features/controls/list/controls-list.page').then((m) => m.ControlsListPage),
  },
  {
    path: 'controls/new',
    title: 'בקרה חדשה',
    loadComponent: () =>
      import('./features/controls/editor/control-editor.page').then((m) => m.ControlEditorPage),
  },
  {
    path: 'controls/:id/edit',
    title: 'עריכת בקרה',
    loadComponent: () =>
      import('./features/controls/editor/control-editor.page').then((m) => m.ControlEditorPage),
  },
  {
    path: 'controls/:id/results',
    title: 'תוצאות בקרה',
    loadComponent: () =>
      import('./features/controls/results/control-results.page').then((m) => m.ControlResultsPage),
  },
  {
    path: 'ai',
    title: 'בקרה בשפה חופשית',
    loadComponent: () => import('./features/ai/ai-assistant.page').then((m) => m.AiAssistantPage),
  },
  { path: '**', redirectTo: 'controls' },
];
