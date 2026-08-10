export interface AcpRequest<T = any> {
    jsonrpc: '2.0';
    id: number | string;
    method: string;
    params?: T;
}

export interface AcpNotification<T = any> {
    jsonrpc: '2.0';
    method: string;
    params?: T;
}

export interface AcpResponse<T = any> {
    jsonrpc: '2.0';
    id: number | string;
    result?: T;
    error?: {
        code: number;
        message: string;
        data?: any;
    };
}

export type AcpMessage = AcpRequest | AcpNotification | AcpResponse;

export interface AcpToolCall {
    id: string;
    type: 'function';
    function: {
        name: string;
        arguments: string;
    };
}

export interface AcpPermissionRequest {
    id: string;
    action: string;
    resource: string;
    risk: 'low' | 'medium' | 'high';
    explanation?: string;
}
