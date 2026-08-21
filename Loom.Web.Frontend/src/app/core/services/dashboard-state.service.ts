import { Injectable, inject, signal, DestroyRef } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { WebSocketService } from './websocket.service';

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
  private destroyRef = inject(DestroyRef);

  cpuData = signal<CpuMetricResponse | null>(null);
  memoryData = signal<MemoryMetricResponse | null>(null);
  threadData = signal<ThreadMetricResponse | null>(null);
  isConnected = signal<boolean>(false);
  sessionInfo = signal<SessionInfo | null>(null);

  constructor() {
    this.wsService.connect('/ws/metrics')
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (msg) => this.handleMessage(msg),
        error: () => this.isConnected.set(false),
        complete: () => this.isConnected.set(false)
      });

    this.loadSessionInfo();
  }

  private async loadSessionInfo(): Promise<void> {
    try {
      const response = await fetch(`${location.origin}/api/session`);
      if (response.ok) {
        this.sessionInfo.set(await response.json() as SessionInfo);
      }
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
