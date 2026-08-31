import { Injectable, inject } from "@angular/core";
import { Observable } from "rxjs";
import { environment } from "../../../environments/environment";
import { AuthService } from "../auth/auth.service";

export const LOOM_SUBPROTOCOL = 'loom.v1';
export const LOOM_TOKEN_PREFIX = 'loom.token.';

/** Pure. The WebSocket origin to connect to. An explicit environment.wsUrl wins as an
 *  escape hatch; otherwise derive it from the page's own origin. Deriving is what makes
 *  a non-default dashboard port work: Loom.Dashboard falls back to an OS-assigned free
 *  port when 5209 is taken, and a hardcoded 5209 then points at the wrong process.
 *  Under `ng serve` this resolves to the dev server, whose proxy already forwards /ws
 *  with ws:true (proxy.conf.js). */
export function wsBase(configured: string, pageProtocol: string, pageHost: string): string {
  if (configured) return configured;
  return `${pageProtocol === 'https:' ? 'wss:' : 'ws:'}//${pageHost}`;
}

@Injectable({
    providedIn: "root"
})
export class WebSocketService {
    private readonly auth = inject(AuthService);
    private readonly maxReconnectAttempts = 10;

    connect(endpoint: string): Observable<any> {
     return new Observable(observer => {
      const url = `${wsBase(environment.wsUrl, location.protocol, location.host)}${endpoint}`;
      let socket: WebSocket | null = null;
      let reconnectAttempts = 0;
      let closedByCaller = false;
      let hasOpened = false;

      const open = () => {
        try {
          // Browsers cannot set an Authorization header on a WebSocket handshake, so the
          // token rides the subprotocol list. Never a query string - that writes the
          // credential into Kestrel's access log and any proxy log in front of it.
          // The server accepts with "loom.v1" (AuthenticationMiddleware.cs:9).
          const token = this.auth.token();
          socket = token
            ? new WebSocket(url, [LOOM_SUBPROTOCOL, LOOM_TOKEN_PREFIX + token])
            : new WebSocket(url, [LOOM_SUBPROTOCOL]);

          socket.onopen = () => {
            console.log(`WebSocket connected to ${url}`);
            hasOpened = true;
            reconnectAttempts = 0;
          };

          socket.onmessage = (event) => {
            try {
              const data = JSON.parse(event.data);
              observer.next(data);
            } catch (error) {
              console.error('Failed to parse WebSocket message:', error);
            }
          };

          socket.onerror = (error) => {
            console.error('WebSocket error:', error);
            observer.error(error);
          };

          socket.onclose = (event) => {
            console.log(`WebSocket closed: ${event.code}`);

            if (!closedByCaller && !hasOpened && reconnectAttempts >= 2) {
              observer.complete();
            } else if (!closedByCaller && !event.wasClean && reconnectAttempts < this.maxReconnectAttempts) {
              reconnectAttempts++;
              const delay = Math.min(1000 * Math.pow(2, reconnectAttempts), 30000);

              setTimeout(open, delay);
            } else {
              observer.complete();
            }
          };
        } catch (error) {
          observer.error(error);
        }
      };

      open();

      return () => {
        closedByCaller = true;
        if (socket) {
          socket.close(1000, "Client disconnecting");
          socket = null;
        }
      };
    });
    }
}
