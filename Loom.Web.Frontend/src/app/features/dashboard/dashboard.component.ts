import { CommonModule } from "@angular/common";
import { Component, DestroyRef, inject, OnInit, signal } from "@angular/core";
import { WebSocketService } from "../../core/services/websocket.service";
import { takeUntilDestroyed } from "@angular/core/rxjs-interop";
import { CpuMetricsComponent } from "../cpu-metrics/cpu-metrics.component";
import { MemoryMetricsComponent } from "../memory-metrics/memory-metrics.component";
import { ThreadMetricsComponent } from "../thread-metrics/thread-metrics.component";


@Component({
    selector: 'app-dashboard',
    standalone: true, 
    imports: [CommonModule, CpuMetricsComponent, MemoryMetricsComponent, ThreadMetricsComponent],
    templateUrl: './dashboard.component.html',
    styleUrls: ['./dashboard.component.scss']
})

export class DashboardComponent implements OnInit{
    private wsService = inject(WebSocketService);
    private destroyRef = inject(DestroyRef);

    // Signals for reactive state
    cpuData = signal<any>(null);
    memoryData = signal<any>(null);
    threadData = signal<any>(null);
    isConnected = signal<boolean>(false);
    connectionError = signal<boolean>(false);

    ngOnInit(): void {
        this.connectWebSocket();
    }

    private connectWebSocket(): void {
        this.wsService.connect('/ws/metrics')
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: (message) => {
                    this.isConnected.set(true);
                    this.connectionError.set(false);
                    this.handleMetricUpdate(message);
                },
                error: (error) => {
                    console.error('WebSocket error:', error);
                    this.isConnected.set(false);
                    this.connectionError.set(true);
                },
                complete: () => {
                    console.log('WebSocket connection closed');
                    this.isConnected.set(false);
                }
            });
    }

    private handleMetricUpdate(message: any): void {
        // Handle discriminated union based on $type field
        switch(message.$type || message.type) {
            case 'cpu':
                this.cpuData.set(message.data);
                break;
            case 'memory':
                this.memoryData.set(message.data);
                break;
            case 'thread':
                this.threadData.set(message.data);
                break;
            default:
                console.warn('Unknown metric type:', message);
        }
    }

    reconnect(): void {
        this.connectWebSocket();
    }
}