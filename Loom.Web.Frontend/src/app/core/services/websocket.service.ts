import { Injectable } from "@angular/core";
import { Observable } from "rxjs";
import { environment } from "../../../environments/environment";


@Injectable({
    providedIn: "root"
})
export class WebSocketService {
    private readonly maxReconnectAttempts = 10;

    connect(endpoint: string): Observable<any> {
     return new Observable(observer => {
      const url = `${environment.wsUrl}${endpoint}`;
      let socket: WebSocket | null = null;
      let reconnectAttempts = 0;
      let closedByCaller = false;

      const open = () => {
        try {
          socket = new WebSocket(url);

          socket.onopen = () => {
            console.log(`WebSocket connected to ${url}`);
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

            if (!closedByCaller && !event.wasClean && reconnectAttempts < this.maxReconnectAttempts) {
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
