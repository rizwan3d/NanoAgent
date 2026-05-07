import { EventEmitter } from 'events';
import { AcpMessage, AcpRequest, AcpResponse, AcpNotification } from '../types/acp';
import { LogService } from './LogService';

export class AcpClient extends EventEmitter {
    private buffer = '';
    private requestId = 0;
    private pendingRequests: Map<number | string, { resolve: (res: any) => void; reject: (err: any) => void }> = new Map();
    private logService: LogService;

    constructor() {
        super();
        this.logService = LogService.getInstance();
    }

    public handleData(data: Buffer | string) {
        this.buffer += data.toString();
        
        let newlineIndex;
        while ((newlineIndex = this.buffer.indexOf('\n')) !== -1) {
            const line = this.buffer.slice(0, newlineIndex).trim();
            this.buffer = this.buffer.slice(newlineIndex + 1);
            
            if (line) {
                this.parseMessage(line);
            }
        }
    }

    private parseMessage(line: string) {
        try {
            const message: AcpMessage = JSON.parse(line);
            
            this.logService.debug('Received ACP Message', message);

            if ('id' in message && ('result' in message || 'error' in message)) {
                // It's a response
                const response = message as AcpResponse;
                const pending = this.pendingRequests.get(response.id);
                if (pending) {
                    this.pendingRequests.delete(response.id);
                    if (response.error) {
                        pending.reject(response.error);
                    } else {
                        pending.resolve(response.result);
                    }
                }
            } else if ('id' in message && 'method' in message) {
                // It's a request
                this.emit('request', message as AcpRequest);
            } else if ('method' in message && !('id' in message)) {
                // It's a notification
                this.emit('notification', message as AcpNotification);
            }
        } catch (err) {
            this.logService.warn('Failed to parse ACP message', { line, error: err });
        }
    }

    public sendRequest<T>(method: string, params?: any): Promise<T> {
        return new Promise((resolve, reject) => {
            const id = ++this.requestId;
            const request: AcpRequest = {
                jsonrpc: '2.0',
                id,
                method,
                params
            };

            this.pendingRequests.set(id, { resolve, reject });
            this.sendMessage(request);
        });
    }

    public sendNotification(method: string, params?: any) {
        const notification: AcpNotification = {
            jsonrpc: '2.0',
            method,
            params
        };
        this.sendMessage(notification);
    }

    public sendResponse(id: number | string, result: any) {
        const response: AcpResponse = {
            jsonrpc: '2.0',
            id,
            result
        };
        this.sendMessage(response);
    }

    public sendError(id: number | string, code: number, message: string, data?: any) {
        const response: AcpResponse = {
            jsonrpc: '2.0',
            id,
            error: { code, message, data }
        };
        this.sendMessage(response);
    }

    private sendMessage(message: AcpMessage) {
        this.logService.debug('Sending ACP Message', message);
        this.emit('send', JSON.stringify(message) + '\n');
    }

    public rejectAllPendingRequests() {
        for (const [id, { reject }] of this.pendingRequests) {
            reject(new Error('AcpClient is shutting down'));
            this.pendingRequests.delete(id);
        }
    }
}
