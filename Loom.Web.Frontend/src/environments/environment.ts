export const environment = {
  production: false,
  apiUrl: '',
  // Unlike /api and /prometheus, the WebSocket client (core/services/websocket.service.ts)
  // connects here directly and bypasses the ng serve dev proxy (proxy.conf.js) entirely.
  // This port must be kept in sync by hand with whatever port Loom.Dashboard actually
  // bound (see LOOM_DASHBOARD_PORT / --port in Loom.Dashboard/Program.cs) — if the
  // dashboard falls back off 5209, live metrics break here until this is updated.
  wsUrl: 'ws://localhost:5209'
};
