import * as vscode from 'vscode';
import { spawn, ChildProcess } from 'child_process';
import { LogService } from './LogService';
import { EventEmitter } from 'events';

export class NanoAgentProcessManager extends EventEmitter {
    private process: ChildProcess | null = null;
    private logService: LogService;
    private isRestarting = false;

    constructor() {
        super();
        this.logService = LogService.getInstance();
    }

    public async start(): Promise<void> {
        if (this.process) {
            this.logService.warn('NanoAgent process is already running.');
            return;
        }

        const config = vscode.workspace.getConfiguration('nanoagent');
        const command = config.get<string>('command', 'nanoai');
        const args = config.get<string[]>('args', ['--acp']);
        
        let cwd = config.get<string>('workingDirectory');
        if (!cwd && vscode.workspace.workspaceFolders && vscode.workspace.workspaceFolders.length > 0) {
            cwd = vscode.workspace.workspaceFolders[0].uri.fsPath;
        }

        this.logService.info(`Starting NanoAgent process: ${command} ${args.join(' ')}`, { cwd });
        this.emit('status', 'starting');

        try {
            this.process = spawn(command, args, {
                cwd: cwd || undefined,
                env: process.env,
                shell: process.platform === 'win32' // Use shell on Windows for better path resolution
            });

            this.process.on('error', (err) => {
                this.logService.error('Failed to start NanoAgent process. Ensure it is installed and in your PATH.', err);
                vscode.window.showErrorMessage(`Failed to start NanoAgent: ${err.message}. Is nanoai installed?`);
                this.process = null;
                this.emit('status', 'error');
            });

            this.process.on('exit', (code, signal) => {
                this.logService.info(`NanoAgent process exited with code ${code} and signal ${signal}`);
                this.process = null;
                if (!this.isRestarting) {
                    this.emit('status', 'stopped');
                    if (code !== 0 && code !== null) {
                        vscode.window.showErrorMessage(`NanoAgent process exited unexpectedly with code ${code}. Check logs for details.`);
                    }
                }
            });

            if (this.process.stdout) {
                this.process.stdout.on('data', (data) => {
                    this.emit('stdout', data);
                });
            }

            if (this.process.stderr) {
                this.process.stderr.on('data', (data) => {
                    this.logService.warn(`NanoAgent stderr: ${data.toString().trim()}`);
                    this.emit('stderr', data);
                });
            }

            this.logService.info(`NanoAgent process started successfully (PID: ${this.process.pid}).`);
            this.emit('status', 'running');
        } catch (error) {
            this.logService.error('Exception while starting NanoAgent process', error);
            this.emit('status', 'error');
        }
    }

    public async stop(): Promise<void> {
        if (!this.process) {
            return;
        }
        
        this.logService.info('Stopping NanoAgent process...');
        this.process.kill('SIGTERM');
        
        // Wait for it to exit
        await new Promise<void>((resolve) => {
            if (!this.process) {
                resolve();
            } else {
                this.process.once('exit', () => resolve());
                setTimeout(() => {
                    if (this.process) {
                        this.logService.warn('Process did not exit after SIGTERM, sending SIGKILL');
                        this.process.kill('SIGKILL');
                    }
                    resolve();
                }, 3000);
            }
        });
        
        this.process = null;
        this.logService.info('NanoAgent process stopped.');
        this.emit('status', 'stopped');
    }

    public async restart(): Promise<void> {
        this.logService.info('Restarting NanoAgent process...');
        this.isRestarting = true;
        await this.stop();
        await this.start();
        this.isRestarting = false;
    }

    public getProcess(): ChildProcess | null {
        return this.process;
    }
}
