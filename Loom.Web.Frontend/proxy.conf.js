// Same env var Loom.Dashboard reads for --port/LOOM_DASHBOARD_PORT (see
// Loom.Dashboard/Program.cs) so `ng serve` can point at a non-default dashboard
// instance without editing this file. Defaults to 5209 to match the dashboard's
// own default.
const target = `http://localhost:${process.env['LOOM_DASHBOARD_PORT'] || 5209}`;

module.exports = {
  '/api': {
    target,
    secure: false,
    changeOrigin: true,
    logLevel: 'debug'
  },
  '/ws': {
    target,
    secure: false,
    ws: true,
    changeOrigin: true,
    logLevel: 'debug'
  },
  '/prometheus': {
    target,
    secure: false,
    changeOrigin: true
  }
};
