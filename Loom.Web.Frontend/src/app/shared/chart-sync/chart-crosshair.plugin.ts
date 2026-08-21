import type { ChartConfiguration } from 'chart.js';
import { Chart } from 'chart.js';
import { DashboardTimelineService } from '../../core/services/dashboard-timeline.service';

/** Chart.js v4.5 constructor is `(item, userConfig)` — plugins must be passed in
 *  `config.plugins`, not as a third ctor arg. Typed helper that wires the shared
 *  crosshair plugin into the chart config. */
export function createChartWithCrosshair(
  canvas: HTMLCanvasElement,
  config: ChartConfiguration,
  service: DashboardTimelineService,
  getOffset: () => number = () => service.windowStart()
): Chart {
  const plugin = createCrosshairPlugin(service, getOffset);
  const plugins = config.plugins ?? [];
  config.plugins = [...plugins, plugin];
  return new Chart(canvas, config);
}

/** Chart.js inline plugin: draws a vertical crosshair line at the shared timeline
 *  crosshair index. All dashboard charts register it so hovering one chart
 *  highlights the same instant on every chart. `getOffset` maps the global tick
 *  index to the chart's local dataset index (windowed charts offset by
 *  windowStart, overview chart offset by 0). */
export function createCrosshairPlugin(
  service: DashboardTimelineService,
  getOffset: () => number = () => service.windowStart()
): any {
  return {
    id: 'loomCrosshairSync',
    afterDraw(chart: Chart) {
      const crosshair = service.crosshairIndex();
      if (crosshair === null) return;

      const meta = chart.getDatasetMeta(0);
      if (!meta || meta.data.length === 0) return;

      const localIndex = crosshair - getOffset();
      if (localIndex < 0 || localIndex >= meta.data.length) return;

      const point = meta.data[localIndex];
      const ctx = chart.ctx;
      const area = chart.chartArea;
      if (!area) return;

      ctx.save();
      ctx.strokeStyle = 'rgba(20, 184, 166, 0.6)';
      ctx.lineWidth = 1;
      ctx.setLineDash([4, 4]);
      ctx.beginPath();
      ctx.moveTo(point.x, area.top);
      ctx.lineTo(point.x, area.bottom);
      ctx.stroke();
      ctx.restore();
    }
  };
}

/** Standard onHover handler that syncs the crosshair to the nearest data point.
 *  `getOffset` must match the plugin offset used by the same chart. */
export function syncCrosshairOnHover(
  service: DashboardTimelineService,
  getOffset: () => number = () => service.windowStart()
) {
  return (event: any, elements: any[]) => {
    if (elements && elements.length > 0) {
      service.setCrosshair(getOffset() + elements[0].index);
    } else {
      service.setCrosshair(null);
    }
  };
}