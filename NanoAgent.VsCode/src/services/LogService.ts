import * as vscode from 'vscode';

export class LogService {
    private static instance: LogService;
    private outputChannel: vscode.OutputChannel;
    private logLevel: 'debug' | 'info' | 'warn' | 'error' = 'info';

    private constructor() {
        this.outputChannel = vscode.window.createOutputChannel('NanoAgent');
        this.updateLogLevel();
        
        vscode.workspace.onDidChangeConfiguration(e => {
            if (e.affectsConfiguration('nanoagent.logLevel')) {
                this.updateLogLevel();
            }
        });
    }

    public static getInstance(): LogService {
        if (!LogService.instance) {
            LogService.instance = new LogService();
        }
        return LogService.instance;
    }

    private updateLogLevel() {
        const config = vscode.workspace.getConfiguration('nanoagent');
        this.logLevel = config.get<'debug' | 'info' | 'warn' | 'error'>('logLevel', 'info');
    }

    private log(level: 'debug' | 'info' | 'warn' | 'error', message: string, data?: any) {
        const levels = ['debug', 'info', 'warn', 'error'];
        if (levels.indexOf(level) >= levels.indexOf(this.logLevel)) {
            const timestamp = new Date().toISOString();
            const dataStr = data ? `\n${JSON.stringify(data, null, 2)}` : '';
            this.outputChannel.appendLine(`[${timestamp}] [${level.toUpperCase()}] ${message}${dataStr}`);
        }
    }

    public debug(message: string, data?: any) {
        this.log('debug', message, data);
    }

    public info(message: string, data?: any) {
        this.log('info', message, data);
    }

    public warn(message: string, data?: any) {
        this.log('warn', message, data);
    }

    public error(message: string, error?: any) {
        let errData = error;
        if (error instanceof Error) {
            errData = { message: error.message, stack: error.stack };
        }
        this.log('error', message, errData);
    }

    public show() {
        this.outputChannel.show(true);
    }

    public dispose() {
        this.outputChannel.dispose();
    }
}
