import * as echarts from 'echarts';

export const LOOM_DARK_THEME_NAME = 'loom-dark';

let themeRegistered = false;

/** Registers the shared ECharts dark theme once per app lifetime. Safe to call
 *  from every chart component - idempotent after the first call. */
export function registerLoomDarkTheme(): void {
  if (themeRegistered) return;
  themeRegistered = true;

  echarts.registerTheme(LOOM_DARK_THEME_NAME, {
    backgroundColor: 'transparent',
    textStyle: { color: '#94a3b8' },
    legend: { textStyle: { color: '#94a3b8' } },
    line: { smooth: true },
    grid: { borderColor: 'rgba(255, 255, 255, 0.04)' },
    categoryAxis: {
      axisLine: { lineStyle: { color: 'rgba(255, 255, 255, 0.1)' } },
      splitLine: { lineStyle: { color: 'rgba(255, 255, 255, 0.04)' } }
    },
    valueAxis: {
      axisLine: { lineStyle: { color: 'rgba(255, 255, 255, 0.1)' } },
      splitLine: { lineStyle: { color: 'rgba(255, 255, 255, 0.04)' } }
    }
  });
}

/** Source of truth for chart color branching: the app's own [data-theme]
 *  attribute (styles.scss defaults to dark; "light" is the sole override),
 *  NOT the OS-level prefers-color-scheme media query - the two can disagree
 *  whenever the app hasn't been explicitly switched to light. */
export function isLightTheme(): boolean {
  return document.documentElement.getAttribute('data-theme') === 'light';
}
