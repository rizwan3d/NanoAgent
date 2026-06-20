import * as vscode from 'vscode';
import { spawn, ChildProcess } from 'child_process';
import * as os from 'os';
import * as path from 'path';
import * as fs from 'fs';
import { LogService } from './LogService';
import { EventEmitter } from 'events';

export type NanoAgentProcessStatus = 'stopped' | 'starting' | 'running' | 'error';

export class NanoAgentProcessManager extends EventEmitter {
    private process: ChildProcess | null = null;
    private logService: LogService;
    private isRestarting = false;
    private status: NanoAgentProcessStatus = 'stopped';

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
        const command = this.resolveCommand(config.get<string>('command', 'nanoai'));
        const configuredArgs = config.get<string[]>('args', ['--acp']);
        const args = this.ensureSurfaceArg(configuredArgs, 'vscode');
        
        let cwd = config.get<string>('workingDirectory');
        if (!cwd && vscode.workspace.workspaceFolders && vscode.workspace.workspaceFolders.length > 0) {
            cwd = vscode.workspace.workspaceFolders[0].uri.fsPath;
        }

        this.logService.info(`Starting NanoAgent process: ${command} ${args.join(' ')}`, { cwd });
        this.setStatus('starting');

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
                this.setStatus('error');
            });

            this.process.on('exit', (code, signal) => {
                this.logService.info(`NanoAgent process exited with code ${code} and signal ${signal}`);
                this.process = null;
                if (!this.isRestarting) {
                    this.setStatus('stopped');
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
            this.setStatus('running');
        } catch (error) {
            this.logService.error('Exception while starting NanoAgent process', error);
            this.setStatus('error');
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
        this.setStatus('stopped');
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

    public getStatus(): NanoAgentProcessStatus {
        return this.status;
    }

    private setStatus(status: NanoAgentProcessStatus) {
        this.status = status;
        this.emit('status', status);
    }

    // GUI-launched VS Code doesn't inherit the shell PATH, so a global `nanoai`
    // installed via npm/pnpm/bun is invisible. Scan their global bin dirs and
    // return an absolute path when the default command isn't already a path.
    private resolveCommand(command: string): string {
        if (command !== 'nanoai' || command.includes('/') || command.includes(path.sep)) {
            return command; // user gave an explicit path/command — trust it.
        }

        const home = os.homedir();
        const isWin = process.platform === 'win32';
        const names = isWin ? ['nanoai.cmd', 'nanoai.exe', 'nanoai'] : ['nanoai'];

        const dirs = [
            process.env.PNPM_HOME,
            process.env.BUN_INSTALL && path.join(process.env.BUN_INSTALL, 'bin'),
            isWin && process.env.APPDATA && path.join(process.env.APPDATA, 'npm'),
            isWin && process.env.LOCALAPPDATA && path.join(process.env.LOCALAPPDATA, 'pnpm'),
            path.join(home, '.bun', 'bin'),                 // bun default
            path.join(home, 'Library', 'pnpm'),             // pnpm macOS
            path.join(home, '.local', 'share', 'pnpm'),     // pnpm Linux
            path.join(home, '.npm-global', 'bin'),          // common npm prefix
            '/usr/local/bin',
            '/opt/homebrew/bin',
        ].filter(Boolean) as string[];

        // debug() is off by default (logLevel=info) so this trace is dev-only.
        this.logService.debug('Resolving nanoai across npm/pnpm/bun bin dirs', { dirs, names });
        for (const dir of dirs) {
            for (const name of names) {
                const candidate = path.join(dir, name);
                const found = fs.existsSync(candidate);
                this.logService.debug(`  check ${candidate} -> ${found ? 'FOUND' : 'miss'}`);
                if (found) {
                    this.logService.info(`Resolved NanoAgent binary: ${candidate}`);
                    return candidate;
                }
            }
        }
        this.logService.debug('No binary in known dirs; falling back to PATH lookup of "nanoai"');
        return command; // fall back to PATH; spawn 'error' handler already reports a clear failure.
    }

    private ensureSurfaceArg(args: string[], surface: string): string[] {
        const hasSurfaceArg = args.some((arg, index) =>
            arg === '--surface' || arg.startsWith('--surface=') || (index > 0 && args[index - 1] === '--surface'));

        return hasSurfaceArg
            ? [...args]
            : [...args, '--surface', surface];
    }
}
