import { Component, signal } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { CommonModule } from '@angular/common';

interface NavItem {
  path: string;
  label: string;
  ariaLabel: string;
  icon: string;
}

interface NavGroup {
  label: string;
  items: NavItem[];
}

@Component({
  selector: 'app-sidebar',
  standalone: true,
  imports: [CommonModule, RouterLink, RouterLinkActive],
  templateUrl: './sidebar.component.html',
  styleUrls: ['./sidebar.component.scss']
})
export class SidebarComponent {
  collapsed = signal(false);

  navGroups: NavGroup[] = [
    {
      label: 'Overview',
      items: [
        {
          path: '/dashboard',
          label: 'Dashboard',
          ariaLabel: 'Navigate to Dashboard',
          icon: 'M3 13h8V3H3v10zm0 8h8v-6H3v6zm10 0h8V11h-8v10zm0-18v6h8V3h-8z'
        }
      ]
    },
    {
      label: 'Analyze',
      items: [
        {
          path: '/metrics',
          label: 'Metrics Explorer',
          ariaLabel: 'Navigate to Metrics Explorer',
          icon: 'M2 20h20M4 16l4-5 4 3 5-7 3 4'
        },
        {
          path: '/rankings',
          label: 'Rankings',
          ariaLabel: 'Navigate to Rankings',
          icon: 'M3 20h18M5 20V10M10 20V5M15 20v-8M20 20V3'
        },
        {
          path: '/query',
          label: 'Query Builder',
          ariaLabel: 'Navigate to Query Builder',
          icon: 'M21 21l-4.35-4.35M11 19a8 8 0 100-16 8 8 0 000 16z'
        },
        {
          path: '/logs',
          label: 'Logs',
          ariaLabel: 'Navigate to Logs',
          icon: 'M4 4h16v4H4zM4 10h16v4H4zM4 16h10v4H4z'
        },
        {
          path: '/log-search',
          label: 'Log Search',
          ariaLabel: 'Navigate to Log Search',
          icon: 'M11 19a8 8 0 100-16 8 8 0 000 16zm10 2l-4.35-4.35'
        }
      ]
    },
    {
      label: 'Monitor',
      items: [
        {
          path: '/alerts',
          label: 'Alerts',
          ariaLabel: 'Navigate to Alerts',
          icon: 'M12 2l8 4v6c0 5-3.5 8-8 10-4.5-2-8-5-8-10V6l8-4z'
        },
        {
          path: '/exporters',
          label: 'Exporters',
          ariaLabel: 'Navigate to Exporters',
          icon: 'M12 3v12M7 10l5 5 5-5M4 21h16'
        }
      ]
    }
  ];

  toggleCollapsed(): void {
    this.collapsed.update(v => !v);
  }
}