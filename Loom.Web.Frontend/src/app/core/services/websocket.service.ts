import { Injectable } from "@angular/core";
import { Observable } from "rxjs";
import { environment } from "../../../environments/environment";


@Injectable({
    providedIn: "root"
})
export class WebSocketService {
    private socket: WebSocket | null = null;
    private reconnectAttempts = 0;
    private readonly maxReconnectAttempts = 10;

    connect(endpoint: string): Observable<any> {
     return new Observable(observer => {
      const url = `${environment.wsUrl}${endpoint}`;

      try {
        this.socket = new WebSocket(url);

        this.socket.onopen = () => {
          console.log(`WebSocket connected to ${url}`);
          this.reconnectAttempts = 0;
        };

        this.socket.onmessage = (event) => {
          try {
            const data = JSON.parse(event.data);
            observer.next(data);
          } catch (error) {
            console.error('Failed to parse WebSocket message:', error);
          }
        };

        this.socket.onerror = (error) => {
          console.error('WebSocket error:', error);
          observer.error(error);
        };

        this.socket.onclose = (event) => {
          console.log(`WebSocket closed: ${event.code}`);

          if (!event.wasClean && this.reconnectAttempts < this.maxReconnectAttempts) {
            this.reconnectAttempts++;
            const delay = Math.min(1000 * Math.pow(2, this.reconnectAttempts), 30000);

            setTimeout(() => {
              this.connect(endpoint).subscribe(observer);
            }, delay);
          } else {
            observer.complete();
          }
        };
      } catch (error) {
        observer.error(error);
      }

      return () => this.disconnect();
    });
    }

    send(message: any): void {
        if(this.socket && this.socket.readyState === WebSocket.OPEN){
            this.socket.send(JSON.stringify(message));
        }
    }

    disconnect(): void {
        if (this.socket) {
            this.socket.close(1000, "Client disconnecting");
            this.socket = null;
        }
    }
}