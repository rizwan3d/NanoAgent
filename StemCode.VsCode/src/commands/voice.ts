import { spawn } from 'child_process';
import * as vscode from 'vscode';
import { StemCodeProcessManager } from '../services/StemCodeProcessManager';
import { ChatViewProvider } from '../webviews/ChatViewProvider';

interface VoiceModelOption {
    Id: string;
    Label: string;
    Description: string;
    IsRecommended: boolean;
}

interface VoiceInputDevice {
    Id: string;
    Name: string;
    IsDefault: boolean;
}

export function registerVoiceCommand(
    context: vscode.ExtensionContext,
    processManager: StemCodeProcessManager,
    chatViewProvider: ChatViewProvider
): void {
    context.subscriptions.push(
        vscode.commands.registerCommand('stemcode.voiceDictation', async () => {
            const transcript = await captureVoiceTranscript(processManager);
            if (!transcript) {
                return;
            }

            await vscode.commands.executeCommand('stemcode.openChat');
            if (!chatViewProvider.prefillMessage(transcript)) {
                await vscode.env.clipboard.writeText(transcript);
                vscode.window.showInformationMessage('Voice transcript copied to the clipboard.');
            }
        }),
        vscode.commands.registerCommand('stemcode.voiceSetup', async () => {
            await setupVoice(processManager);
        }),
        vscode.commands.registerCommand('stemcode.voiceUpdate', async () => {
            await runVoiceProgressCommand(
                processManager,
                ['--voice-update'],
                'Updating voice models'
            );
        })
    );
}

async function setupVoice(processManager: StemCodeProcessManager): Promise<void> {
    const modelJson = await runVoiceCommand(processManager, ['--voice-models']);
    if (!modelJson) {
        return;
    }

    let models: VoiceModelOption[];
    try {
        models = JSON.parse(modelJson) as VoiceModelOption[];
    } catch {
        vscode.window.showErrorMessage('Voice setup could not read the available models.');
        return;
    }

    if (!models.length) {
        vscode.window.showInformationMessage('No voice models are available.');
        return;
    }

    const selectedModel = await vscode.window.showQuickPick(
        models.map(model => ({
            label: model.Label,
            description: model.IsRecommended ? 'Recommended' : undefined,
            detail: model.Description,
            model
        })),
        {
            title: 'Voice model',
            placeHolder: 'Choose the local speech model used for dictation'
        }
    );
    if (!selectedModel) {
        return;
    }

    const deviceJson = await runVoiceCommand(processManager, ['--voice-devices']);
    if (!deviceJson) {
        return;
    }

    let devices: VoiceInputDevice[];
    try {
        devices = JSON.parse(deviceJson) as VoiceInputDevice[];
    } catch {
        vscode.window.showErrorMessage('Voice setup could not read microphone devices.');
        return;
    }

    let selectedDevice: VoiceInputDevice | undefined;
    if (devices.length > 1) {
        const devicePick = await vscode.window.showQuickPick(
            devices.map(device => ({
                label: device.Name,
                description: device.IsDefault ? 'System default' : undefined,
                device
            })),
            {
                title: 'Voice microphone',
                placeHolder: 'Choose the microphone used for dictation'
            }
        );
        if (!devicePick) {
            return;
        }
        selectedDevice = devicePick.device;
    } else if (devices.length === 1) {
        selectedDevice = devices[0];
    }

    const args = ['--voice-configure', '--model', selectedModel.model.Id];
    if (selectedDevice?.Id) {
        args.push('--device', selectedDevice.Id);
    }

    const configured = await runVoiceProgressCommand(processManager, args, 'Saving voice setup');
    if (configured !== undefined) {
        vscode.window.showInformationMessage('Voice setup saved.');
    }
}

async function captureVoiceTranscript(processManager: StemCodeProcessManager): Promise<string | undefined> {
    return runVoiceProgressCommand(processManager, ['--voice-dictate'], 'Voice dictation');
}

async function runVoiceCommand(
    processManager: StemCodeProcessManager,
    args: string[]
): Promise<string | undefined> {
    const { command, cwd } = getVoiceProcessContext(processManager);

    return new Promise<string | undefined>((resolve) => {
        const child = spawn(command, args, {
            cwd,
            env: process.env,
            shell: process.platform === 'win32'
        });
        let stdout = '';
        let stderr = '';

        child.stdout?.on('data', data => {
            stdout += data.toString();
        });
        child.stderr?.on('data', data => {
            stderr += data.toString();
        });
        child.on('error', error => {
            vscode.window.showErrorMessage(`Voice command failed: ${error.message}`);
            resolve(undefined);
        });
        child.on('exit', code => {
            if (code !== 0) {
                vscode.window.showErrorMessage(stderr.trim() || 'Voice command failed.');
                resolve(undefined);
                return;
            }
            resolve(stdout.trim());
        });
    });
}

async function runVoiceProgressCommand(
    processManager: StemCodeProcessManager,
    args: string[],
    title: string
): Promise<string | undefined> {
    const { command, cwd } = getVoiceProcessContext(processManager);

    return vscode.window.withProgress(
        {
            location: vscode.ProgressLocation.Notification,
            title,
            cancellable: false
        },
        progress => new Promise<string | undefined>((resolve) => {
            const child = spawn(command, args, {
                cwd,
                env: process.env,
                shell: process.platform === 'win32'
            });
            let stdout = '';
            let stderr = '';

            child.stdout?.on('data', data => {
                stdout += data.toString();
            });
            child.stderr?.on('data', data => {
                const text = data.toString().trim();
                if (text) {
                    stderr = text;
                    const lines = text.split(/\r?\n/);
                    const lastLine = lines[lines.length - 1];
                    if (lastLine) {
                        progress.report({ message: lastLine });
                    }
                }
            });
            child.on('error', error => {
                vscode.window.showErrorMessage(`Voice command failed: ${error.message}`);
                resolve(undefined);
            });
            child.on('exit', code => {
                if (code !== 0) {
                    vscode.window.showErrorMessage(
                        stderr || `Voice command exited with code ${code ?? 'unknown'}.`
                    );
                    resolve(undefined);
                    return;
                }

                resolve(stdout.trim());
            });
        })
    );
}

function getVoiceProcessContext(processManager: StemCodeProcessManager): {
    command: string;
    cwd: string | undefined;
} {
    const config = vscode.workspace.getConfiguration('stemcode');
    const activeProcess = processManager.getProcess();
    const command = activeProcess?.spawnfile || config.get<string>('command', 'stemcode');
    let cwd = config.get<string>('workingDirectory');
    if (!cwd && vscode.workspace.workspaceFolders?.length) {
        cwd = vscode.workspace.workspaceFolders[0].uri.fsPath;
    }

    return { command, cwd: cwd || undefined };
}
