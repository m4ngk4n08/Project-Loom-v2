export const environment = {
  production: false,
  apiUrl: '',
  // Empty means "derive from the page origin" - see wsBase() in
  // core/services/websocket.service.ts. Set this only to point a dev session at a
  // dashboard on another host or port.
  wsUrl: ''
};
