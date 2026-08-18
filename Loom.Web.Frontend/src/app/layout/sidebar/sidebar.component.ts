import { Component, signal } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { CommonModule } from '@angular/common';

interface NavItem {
  path: string;
  label: string;
  icon: string;
  ariaLabel: string;
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

  navItems: NavItem[] = [
    {
      path: '/dashboard',
      label: 'Dashboard',
      icon: '📊',
      ariaLabel: 'Navigate to Dashboard'
    },
    {
      path: '/metrics',
      label: 'Metrics Explorer',
      icon: '📈',
      ariaLabel: 'Navigate to Metrics Explorer'
    },
    {
      path: '/query',
      label: 'Query Builder',
      icon: '🔍',
      ariaLabel: 'Navigate to Query Builder'
    },
    {
      path: '/alerts',
      label: 'Alerts',
      icon: '🔔',
      ariaLabel: 'Navigate to Alerts'
    },
    {
      path: '/exporters',
      label: 'Exporters',
      icon: '📤',
      ariaLabel: 'Navigate to Exporters'
    }
  ];

  toggleCollapsed(): void {
    this.collapsed.update(v => !v);
  }
}
