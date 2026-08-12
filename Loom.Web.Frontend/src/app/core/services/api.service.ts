import { Injectable } from "@angular/core";
import { environment } from "../../../environments/environment";
import { HttpClient } from "@angular/common/http";
import { Observable } from "rxjs";


export interface CpuHotPath {
    methodName: string;
    cpuPercent: number;
    invocationCount: number;
    averageTimeMs: number;
}

export interface CpuMetricResponse{
    cpuUsagePercent: number;
    hotpaths: CpuHotPath[];
    timestamp: string;
}

export interface MemoryAllocation {
    typeName: string;
    sizeBytes: number;
    count: number;
}

export interface GarbageCollectionState{
    gen0Collections: number;
    gen1Collections: number;
    gen2Collections: number;
    totalPauseTimeMs: number;
}

export interface MemoryMetricResponse {
    totalMemoryMb: number;
    allocations: MemoryAllocation[];
    gcStats: GarbageCollectionState;
    timestamp: string;
}

export interface ThreadBlockage{
    threadId: number;
    blockedOnResource: string;
    durationMs: number;
}

export interface ThreadMetricResponse{
    totalThreads: number;
    runningThreads: number;
    blockedThreads: number;
    blockages: ThreadBlockage[];
    timestamp: string;
}

@Injectable({
    providedIn: 'root'
})
export class ApiService {
    private readonly apiUrl = environment.apiUrl;

    constructor(private http: HttpClient){}

    getCpuMetrics(): Observable<CpuMetricResponse> {
        return this.http.get<CpuMetricResponse>(`${this.apiUrl}/api/metrics/cpu`);
    }

    getMemoryMetrics(): Observable<MemoryMetricResponse> {
        return this.http.get<MemoryMetricResponse>(`${this.apiUrl}/api/metrics/memory`);
    }

    getThreadMetrics(): Observable<ThreadMetricResponse>{
        return this.http.get<ThreadMetricResponse>(`${this.apiUrl}/api/metrics/threads`);
    }

    getHealth(): Observable<any> {
        return this.http.get(`${this.apiUrl}/health`);
    }
}