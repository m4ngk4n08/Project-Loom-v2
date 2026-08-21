import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { SidebarComponent } from './sidebar/sidebar.component';
import { TopBarComponent } from './top-bar/top-bar.component';
import { StatusBarComponent } from './status-bar/status-bar.component';

@Component({
  selector: 'app-layout',
  standalone: true,
  imports: [RouterOutlet, SidebarComponent, TopBarComponent, StatusBarComponent],
  template: `
    <div class="layout" role="application">
      <app-sidebar />
      <div class="layout-body">
        <app-top-bar />
        <main class="content" id="main-content" tabindex="-1">
          <router-outlet />
        </main>
        <app-status-bar />
      </div>
    </div>
  `,
  styles: [`
    .layout {
      display: flex;
      height: 100vh;
      background: var(--bg-primary);
    }

    .layout-body {
      display: flex;
      flex-direction: column;
      flex: 1;
      min-width: 0;
      overflow: hidden;
    }

    .content {
      flex: 1;
      overflow-y: auto;
      padding: 1.5rem;
    }

    .content:focus {
      outline: none;
    }

    @media (max-width: 768px) {
      .content {
        padding: 1rem;
      }
    }
  `]
})
export class LayoutComponent {}
