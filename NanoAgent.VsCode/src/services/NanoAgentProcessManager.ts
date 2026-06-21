import * as vscode from 'vscode';
import { spawn, spawnSync, ChildProcess } from 'child_process';
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
    private startPromise: Promise<void> | null = null;
    private installPromise: Promise<string | null> | null = null;
    private status: NanoAgentProcessStatus = 'stopped';
    private static readonly UPDATE_CHECK_KEY = 'nanoagent.lastUpdateCheck';
    private static readonly UPDATE_CHECK_INTERVAL_MS = 24 * 60 * 60 * 1000;

    constructor(private readonly globalState?: vscode.Memento) {
        super();
        this.logService = LogService.getInstance();
    }

    public async start(): Promise<void> {
        if (this.process) {
            this.logService.warn('NanoAgent process is already running.');
            return;
        }

        if (this.startPromise) {
            this.logService.debug('NanoAgent start is already in progress; waiting for the existing attempt.');
            await this.startPromise;
            return;
        }

        this.startPromise = this.startCore();
        try {
            await this.startPromise;
        } finally {
            this.startPromise = null;
        }
    }

    private async startCore(): Promise<void> {
        const config = vscode.workspace.getConfiguration('nanoagent');
        const configured = config.get<string>('command', 'nanoai');
        let command: string | null = this.resolveCommand(configured);
        if (configured === 'nanoai' && !this.isAvailable(command)) {
            command = await this.ensureInstalled();
            if (!command) {
                this.setStatus('error');
                return; // user declined install or it failed; error already surfaced.
            }
        }
        const configuredArgs = config.get<string[]>('args', ['--acp']);
        const args = this.ensureSurfaceArg(configuredArgs, 'vscode');
        
        let cwd = config.get<string>('workingDirectory');
        if (!cwd && vscode.workspace.workspaceFolders && vscode.workspace.workspaceFolders.length > 0) {
            cwd = vscode.workspace.workspaceFolders[0].uri.fsPath;
        }

        this.logService.info(`Starting NanoAgent process: ${command} ${args.join(' ')}`, { cwd });
        this.setStatus('starting');

        try {
            this.process = spawn(this.prepareCommandForSpawn(command), args, {
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

            if (configured === 'nanoai') {
                void this.checkForUpdate(command); // fire-and-forget; never delays startup.
            }
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

        const names = this.getNanoAgentCommandNames();
        const dirs = this.getGlobalCliSearchDirs();

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

    // True if resolveCommand found an absolute binary, or bare "nanoai" is on PATH.
    private isAvailable(command: string): boolean {
        if (command !== 'nanoai') {
            return true; // resolveCommand returned an absolute path it verified exists.
        }
        const probe = process.platform === 'win32' ? 'where' : 'which';
        return spawnSync(probe, ['nanoai'], { shell: false }).status === 0;
    }

    // nanoai not found anywhere. Ask consent, then `npm install -g nanoai-cli`.
    // Returns the resolved command on success, or null if declined/failed.
    private async ensureInstalled(): Promise<string | null> {
        const existing = this.resolveCommand('nanoai');
        if (this.isAvailable(existing)) {
            return existing;
        }

        if (this.installPromise) {
            this.logService.debug('nanoai installation is already in progress; waiting for the existing attempt.');
            return this.installPromise;
        }

        this.installPromise = this.ensureInstalledCore();
        try {
            return await this.installPromise;
        } finally {
            this.installPromise = null;
        }
    }

    private async ensureInstalledCore(): Promise<string | null> {
        const choice = await vscode.window.showWarningMessage(
            'NanoAgent CLI (nanoai) was not found. Install it globally via npm?',
            { modal: true, detail: 'Runs: npm install -g nanoai-cli' },
            'Install'
        );
        if (choice !== 'Install') {
            this.logService.info('User declined nanoai install.');
            return null;
        }

        const ok = await vscode.window.withProgress(
            { location: vscode.ProgressLocation.Notification, title: 'Installing NanoAgent CLI…' },
            () => new Promise<boolean>((resolve) => {
                const npm = process.platform === 'win32' ? 'npm.cmd' : 'npm';
                const proc = spawn(npm, ['install', '-g', 'nanoai-cli'], { shell: process.platform === 'win32' });
                proc.stdout?.on('data', (d) => this.logService.info(`npm: ${d.toString().trim()}`));
                proc.stderr?.on('data', (d) => this.logService.warn(`npm: ${d.toString().trim()}`));
                proc.on('error', (err) => {
                    this.logService.error('npm install failed to start. Is Node.js/npm installed?', err);
                    resolve(false);
                });
                proc.on('exit', (code) => resolve(code === 0));
            })
        );

        if (!ok) {
            vscode.window.showErrorMessage('Failed to install nanoai. Run "npm install -g nanoai-cli" manually. See logs for details.');
            return null;
        }

        const resolved = this.resolveCommand('nanoai');
        if (!this.isAvailable(resolved)) {
            vscode.window.showErrorMessage('nanoai installed but not found on PATH. You may need to restart VS Code.');
            return null;
        }
        this.logService.info('nanoai installed successfully.');
        return resolved;
    }

    private getNanoAgentCommandNames(): string[] {
        return process.platform === 'win32'
            ? ['nanoai.cmd', 'nanoai.exe', 'nanoai']
            : ['nanoai'];
    }

    private getGlobalCliSearchDirs(): string[] {
        const home = os.homedir();
        const isWin = process.platform === 'win32';
        const dirs = new Set<string>();
        const npmPrefix = this.getNpmGlobalPrefix();

        const addDir = (dir: string | undefined | null) => {
            if (typeof dir === 'string' && dir.trim()) {
                dirs.add(dir.trim());
            }
        };

        addDir(process.env.PNPM_HOME);
        addDir(process.env.BUN_INSTALL ? path.join(process.env.BUN_INSTALL, 'bin') : undefined);
        addDir(isWin && process.env.APPDATA ? path.join(process.env.APPDATA, 'npm') : undefined);
        addDir(isWin && process.env.LOCALAPPDATA ? path.join(process.env.LOCALAPPDATA, 'pnpm') : undefined);
        addDir(path.join(home, '.bun', 'bin'));
        addDir(path.join(home, 'Library', 'pnpm'));
        addDir(path.join(home, '.local', 'share', 'pnpm'));
        addDir(path.join(home, '.npm-global', 'bin'));
        addDir('/usr/local/bin');
        addDir('/opt/homebrew/bin');

        if (npmPrefix) {
            addDir(isWin ? npmPrefix : path.join(npmPrefix, 'bin'));
        }

        return Array.from(dirs);
    }

    private getNpmGlobalPrefix(): string | null {
        const npm = process.platform === 'win32' ? 'npm.cmd' : 'npm';
        try {
            const result = spawnSync(npm, ['prefix', '-g'], {
                shell: process.platform === 'win32'
            });
            if (result.status !== 0) {
                this.logService.debug('Failed to read npm global prefix', result.stderr?.toString().trim());
                return null;
            }

            const prefix = result.stdout?.toString().trim();
            return prefix || null;
        } catch (error) {
            this.logService.debug('Unable to probe npm global prefix', error);
            return null;
        }
    }

    private prepareCommandForSpawn(command: string): string {
        if (process.platform !== 'win32') {
            return command;
        }

        // Quoting absolute Windows paths keeps cmd.exe from splitting at spaces.
        return command.includes(path.sep) && !command.startsWith('"')
            ? `"${command}"`
            : command;
    }

    // Background check: compare installed nanoai against npm latest, offer update on consent.
    private async checkForUpdate(command: string): Promise<void> {
        const last = this.globalState?.get<number>(NanoAgentProcessManager.UPDATE_CHECK_KEY, 0) ?? 0;
        if (Date.now() - last < NanoAgentProcessManager.UPDATE_CHECK_INTERVAL_MS) {
            return; // checked within the last 24h; skip the npm-view network call.
        }
        void this.globalState?.update(NanoAgentProcessManager.UPDATE_CHECK_KEY, Date.now());

        try {
            const current = this.parseVersion(spawnSync(this.prepareCommandForSpawn(command), ['--version'], { shell: process.platform === 'win32' }).stdout?.toString());
            const latest = this.parseVersion(spawnSync(process.platform === 'win32' ? 'npm.cmd' : 'npm', ['view', 'nanoai-cli', 'version'], { shell: process.platform === 'win32' }).stdout?.toString());
            if (!current || !latest || this.compareVersions(latest, current) <= 0) {
                return; // up to date, or couldn't determine — stay quiet.
            }

            this.logService.info(`nanoai update available: ${current} -> ${latest}`);
            const choice = await vscode.window.showInformationMessage(
                `A new NanoAgent CLI is available (${current} → ${latest}).`,
                'Update'
            );
            if (choice !== 'Update') {
                return;
            }

            const ok = await vscode.window.withProgress(
                { location: vscode.ProgressLocation.Notification, title: 'Updating NanoAgent CLI…' },
                () => new Promise<boolean>((resolve) => {
                    const npm = process.platform === 'win32' ? 'npm.cmd' : 'npm';
                    const proc = spawn(npm, ['install', '-g', 'nanoai-cli@latest'], { shell: process.platform === 'win32' });
                    proc.stdout?.on('data', (d) => this.logService.info(`npm: ${d.toString().trim()}`));
                    proc.stderr?.on('data', (d) => this.logService.warn(`npm: ${d.toString().trim()}`));
                    proc.on('error', (err) => { this.logService.error('npm update failed to start.', err); resolve(false); });
                    proc.on('exit', (code) => resolve(code === 0));
                })
            );

            if (!ok) {
                vscode.window.showErrorMessage('Failed to update nanoai. See logs for details.');
                return;
            }
            this.logService.info('nanoai updated; restarting to use the new version.');
            await this.restart();
        } catch (err) {
            this.logService.debug('Update check skipped', err); // never block the agent on this.
        }
    }

    private parseVersion(text: string | undefined): string | null {
        const m = text?.match(/(\d+)\.(\d+)\.(\d+)/);
        return m ? m[0] : null;
    }

    private compareVersions(a: string, b: string): number {
        const pa = a.split('.').map(Number), pb = b.split('.').map(Number);
        for (let i = 0; i < 3; i++) {
            if (pa[i] !== pb[i]) { return pa[i] - pb[i]; }
        }
        return 0;
    }

    private ensureSurfaceArg(args: string[], surface: string): string[] {
        const hasSurfaceArg = args.some((arg, index) =>
            arg === '--surface' || arg.startsWith('--surface=') || (index > 0 && args[index - 1] === '--surface'));

        return hasSurfaceArg
            ? [...args]
            : [...args, '--surface', surface];
    }
}
