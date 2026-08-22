import * as echarts from 'echarts';

// Shared ECharts group id - instances with this .group linked via echarts.connect()
// get synced tooltips/axisPointer crosshairs when hovering any one of them.
export const DASHBOARD_CHART_GROUP = 'loom-dashboard-crosshair';

let connected = false;

/** echarts.connect(groupId) links every currently-registered chart sharing that
 *  group id; calling it once after the group is established is enough, so this
 *  guards against each chart component redundantly re-calling it on init. */
export function connectDashboardChartGroup(): void {
  if (connected) return;
  connected = true;
  echarts.connect(DASHBOARD_CHART_GROUP);
}
