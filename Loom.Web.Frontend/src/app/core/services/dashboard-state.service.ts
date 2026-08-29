import { Injectable, inject, signal, effect } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { WebSocketService } from './websocket.service';
import { AuthService } from '../auth/auth.service';

interface CpuMetricResponse {
  cpuUsagePercent: number;
  hotpaths: any[];
  timestamp: Date;
}

interface MemoryMetricResponse {
  totalMemoryMb: number;
  usedMemoryMb: number;
  gcStats: {
    gen0Collections: number;
    gen1Collections: number;
    gen2Collections: number;
    totalGcTimeMs: number;
  };
  topAllocations: any[];
  timestamp: Date;
}

interface ThreadMetricResponse {
  totalThreads: number;
  activeThreads: number;
  blockedThreads: number;
  blockages: any[];
  timestamp: Date;
}

interface MetricMessage {
  $type: string;
  data: any;
}

interface SessionInfo {
  targetProcessId: number;
  targetProcessName: string;
  startedAtUtc: string;
  uptimeSeconds: number;
  metricCount: number;
}

@Injectable({
  providedIn: 'root'
})
export class DashboardStateService {
  private wsService = inject(WebSocketService);
  private readonly auth = inject(AuthService);
  private readonly http = inject(HttpClient);

  cpuData = signal<CpuMetricResponse | null>(null);
  memoryData = signal<MemoryMetricResponse | null>(null);
  threadData = signal<ThreadMetricResponse | null>(null);
  isConnected = signal<boolean>(false);
  sessionInfo = signal<SessionInfo | null>(null);

  constructor() {
    // Keyed to the token, not to construction: logout closes the socket (the observable's
    // teardown does it), and a later login opens a fresh one carrying the new credential.
    // The server authorizes a socket only at handshake, so a socket outliving its token
    // would keep streaming to a page whose session has ended.
    effect((onCleanup) => {
      if (!this.auth.token()) {
        this.isConnected.set(false);
        return;
      }

      const sub = this.wsService.connect('/ws/metrics').subscribe({
        next: (msg) => this.handleMessage(msg),
        error: () => this.isConnected.set(false),
        complete: () => this.isConnected.set(false)
      });

      onCleanup(() => sub.unsubscribe());
    });

    this.loadSessionInfo();
  }

  private async loadSessionInfo(): Promise<void> {
    try {
      this.sessionInfo.set(await firstValueFrom(this.http.get<SessionInfo>('/api/session')));
    } catch (error) {
      console.error('Failed to load session info:', error);
    }
  }

  private handleMessage(message: MetricMessage): void {
    this.isConnected.set(true);

    switch (message.$type) {
      case 'cpu':
        this.cpuData.set(message.data as CpuMetricResponse);
        break;
      case 'memory':
        this.memoryData.set(message.data as MemoryMetricResponse);
        break;
      case 'thread':
        this.threadData.set(message.data as ThreadMetricResponse);
        break;
    }
  }
}
