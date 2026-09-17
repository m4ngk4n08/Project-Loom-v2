import { CommonModule } from "@angular/common";
import { Component, inject, computed } from "@angular/core";
import { Router } from "@angular/router";
import { DashboardStateService } from "../../core/services/dashboard-state.service";
import { StatTileComponent } from "../../shared/stat-tile/stat-tile.component";
import { CpuMetricsComponent } from "../cpu-metrics/cpu-metrics.component";
import { MemoryMetricsComponent } from "../memory-metrics/memory-metrics.component";
import { ThreadMetricsComponent } from "../thread-metrics/thread-metrics.component";
import { TimelineBrushComponent } from "../timeline-brush/timeline-brush.component";
import { TopOffendersComponent } from "../top-offenders/top-offenders.component";
import { memoryUsageRatio } from "./memory-usage";


@Component({
    selector: 'app-dashboard',
    standalone: true,
    imports: [
        CommonModule,
        StatTileComponent,
        CpuMetricsComponent,
        MemoryMetricsComponent,
        ThreadMetricsComponent,
        TimelineBrushComponent,
        TopOffendersComponent
    ],
    templateUrl: './dashboard.component.html',
    styleUrls: ['./dashboard.component.scss']
})
export class DashboardComponent {
    stateService = inject(DashboardStateService);
    private router = inject(Router);

    // Target process identity shown in the header badge.
    sessionInfo = this.stateService.sessionInfo.asReadonly();

    // Computed values for stat tiles
    cpuUsage = computed(() => {
        const data = this.stateService.cpuData();
        return data ? data.cpuUsagePercent.toFixed(1) : '-';
    });

    memoryUsage = computed(() => {
        const data = this.stateService.memoryData();
        if (!data) return '-';
        const ratio = memoryUsageRatio(data.usedMemoryMb, data.totalMemoryMb);
        return ratio === null ? '-' : (ratio * 100).toFixed(1);
    });

    memoryTrend = computed(() => {
        const data = this.stateService.memoryData();
        if (!data) return 'neutral';
        const ratio = memoryUsageRatio(data.usedMemoryMb, data.totalMemoryMb);
        return ratio !== null && ratio > 0.8 ? 'up' : 'neutral';
    });

    threadCount = computed(() => {
        const data = this.stateService.threadData();
        return data ? data.totalThreads.toString() : '-';
    });

    blockedThreads = computed(() => {
        const data = this.stateService.threadData();
        return data ? data.blockedThreads.toString() : '-';
    });

    onPointClick(event: { timestamp: Date; method: string }): void {
        this.router.navigate(['/metrics'], {
            queryParams: {
                metric: event.method,
                at: event.timestamp.toISOString()
            }
        });
    }
}