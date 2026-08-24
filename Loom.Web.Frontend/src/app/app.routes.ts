import { Routes } from '@angular/router';
import { LayoutComponent } from './layout/layout.component';

export const routes: Routes = [
  {
    path: '',
    redirectTo: '/dashboard',
    pathMatch: 'full'
  },
  {
    path: '',
    component: LayoutComponent,
    children: [
      {
        path: 'dashboard',
        loadComponent: () => import('./features/dashboard/dashboard.component')
          .then(m => m.DashboardComponent)
      },
      {
        path: 'metrics',
        loadComponent: () => import('./features/metrics-explorer/metrics-explorer.component')
          .then(m => m.MetricsExplorerComponent)
      },
      {
        path: 'rankings',
        loadComponent: () => import('./features/rankings/rankings.component')
          .then(m => m.RankingsComponent)
      },
      {
        path: 'query',
        loadComponent: () => import('./features/query-builder/query-builder.component')
          .then(m => m.QueryBuilderComponent)
      },
      {
        path: 'logs',
        loadComponent: () => import('./features/logs/logs.component')
          .then(m => m.LogsComponent)
      },
      {
        path: 'alerts',
        loadComponent: () => import('./features/alerts/alerts.component')
          .then(m => m.AlertsComponent)
      },
      {
        path: 'exporters',
        loadComponent: () => import('./features/exporters/exporters.component')
          .then(m => m.ExportersComponent)
      }
    ]
  },
  {
    path: '**',
    redirectTo: '/dashboard'
  }
];