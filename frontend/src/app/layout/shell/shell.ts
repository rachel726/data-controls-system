import { BreakpointObserver } from '@angular/cdk/layout';
import { ChangeDetectionStrategy, Component, inject, viewChild } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatListModule } from '@angular/material/list';
import { MatSidenav, MatSidenavModule } from '@angular/material/sidenav';
import { MatToolbarModule } from '@angular/material/toolbar';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { map } from 'rxjs';

const MOBILE_QUERY = '(max-width: 959.98px)';

interface NavItem {
  readonly label: string;
  readonly icon: string;
  readonly link: string;
  readonly exact: boolean;
}

/** Application frame: toolbar + navigation (side on desktop, overlay drawer on mobile). */
@Component({
  selector: 'app-shell',
  imports: [
    RouterOutlet,
    RouterLink,
    RouterLinkActive,
    MatToolbarModule,
    MatSidenavModule,
    MatListModule,
    MatIconModule,
    MatButtonModule,
  ],
  templateUrl: './shell.html',
  styleUrl: './shell.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Shell {
  protected readonly navItems: readonly NavItem[] = [
    { label: 'הבקרות שלי', icon: 'fact_check', link: '/controls', exact: true },
    { label: 'בקרה חדשה', icon: 'add_circle', link: '/controls/new', exact: true },
    { label: 'בקרה בשפה חופשית', icon: 'auto_awesome', link: '/ai', exact: true },
  ];

  private readonly breakpoints = inject(BreakpointObserver);

  /** Starts from a synchronous check, so a phone never renders the desktop layout first. */
  protected readonly isMobile = toSignal(
    this.breakpoints.observe(MOBILE_QUERY).pipe(map((state) => state.matches)),
    { initialValue: this.breakpoints.isMatched(MOBILE_QUERY) },
  );

  private readonly sidenav = viewChild.required(MatSidenav);

  protected closeIfMobile(): void {
    if (this.isMobile()) {
      void this.sidenav().close();
    }
  }
}
