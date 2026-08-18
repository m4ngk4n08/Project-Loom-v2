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
      <app-top-bar />
      <div class="layout-main">
        <app-sidebar />
        <main class="content" id="main-content" tabindex="-1">
          <router-outlet />
        </main>
      </div>
      <app-status-bar />
    </div>
  `,
  styles: [`
    .layout {
      display: flex;
      flex-direction: column;
      height: 100vh;
      background: var(--bg-primary);
    }

    .layout-main {
      display: flex;
      flex: 1;
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
