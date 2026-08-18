import { CommonModule } from "@angular/common";
import { Component, inject, computed } from "@angular/core";
import { DashboardStateService } from "../../core/services/dashboard-state.service";
import { StatTileComponent } from "../../shared/stat-tile/stat-tile.component";
import { CpuMetricsComponent } from "../cpu-metrics/cpu-metrics.component";
import { MemoryMetricsComponent } from "../memory-metrics/memory-metrics.component";
import { ThreadMetricsComponent } from "../thread-metrics/thread-metrics.component";


@Component({
    selector: 'app-dashboard',
    standalone: true,
    imports: [
        CommonModule,
        StatTileComponent,
        CpuMetricsComponent,
        MemoryMetricsComponent,
        ThreadMetricsComponent
    ],
    templateUrl: './dashboard.component.html',
    styleUrls: ['./dashboard.component.scss']
})
export class DashboardComponent {
    stateService = inject(DashboardStateService);

    // Computed values for stat tiles
    cpuUsage = computed(() => {
        const data = this.stateService.cpuData();
        return data ? data.cpuUsagePercent.toFixed(1) : '-';
    });

    memoryUsage = computed(() => {
        const data = this.stateService.memoryData();
        return data ? ((data.usedMemoryMb / data.totalMemoryMb) * 100).toFixed(1) : '-';
    });

    threadCount = computed(() => {
        const data = this.stateService.threadData();
        return data ? data.totalThreads.toString() : '-';
    });

    blockedThreads = computed(() => {
        const data = this.stateService.threadData();
        return data ? data.blockedThreads.toString() : '-';
    });
}