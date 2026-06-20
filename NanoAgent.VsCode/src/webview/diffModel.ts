// Parses NanoAgent tool-call input into a renderable diff model.
// Pure (no node deps) so it can be imported by both the extension and the webview bundle.
// ponytail: naive per-line classifier, no word-level intra-line diff. Upgrade to a
// proper LCS/word-diff only if reviewers ask for inline highlighting.

export type DiffLine = { type: 'add' | 'del' | 'ctx' | 'meta'; text: string };
export type FileDiff = { path: string; lines: DiffLine[] };

export function buildDiffModel(call: { kind?: string; title?: string; rawInput?: unknown }): FileDiff[] | null {
    if (!call) {
        return null;
    }

    const input = call.rawInput;

    // 1. apply_patch: a "*** Begin Patch" block, given as a string or {patch|diff}.
    let patch: string | null = null;
    if (typeof input === 'string' && input.includes('*** ')) {
        patch = input;
    } else if (input && typeof input === 'object') {
        const obj = input as Record<string, unknown>;
        for (const key of ['patch', 'diff', 'patch_text', 'patchText']) {
            if (typeof obj[key] === 'string' && (obj[key] as string).includes('*** ')) {
                patch = obj[key] as string;
                break;
            }
        }
    }

    if (patch) {
        const files: FileDiff[] = [];
        let current: FileDiff | null = null;
        for (const raw of patch.split('\n')) {
            const begin = /^\*\*\* (Begin|End) Patch/.exec(raw);
            if (begin) {
                continue;
            }

            const fileHeader = /^\*\*\* (Add|Update|Delete) File: (.+)$/.exec(raw);
            if (fileHeader) {
                current = { path: fileHeader[2].trim(), lines: [] };
                files.push(current);
                continue;
            }

            if (!current) {
                continue;
            }

            if (raw.startsWith('@@')) {
                current.lines.push({ type: 'meta', text: raw });
            } else if (raw.startsWith('+')) {
                current.lines.push({ type: 'add', text: raw.slice(1) });
            } else if (raw.startsWith('-')) {
                current.lines.push({ type: 'del', text: raw.slice(1) });
            } else {
                current.lines.push({ type: 'ctx', text: raw.startsWith(' ') ? raw.slice(1) : raw });
            }
        }

        return files.length > 0 ? files : null;
    }

    // 2. file_write style: { path, content } — show the whole file as added.
    const isEdit = call.kind === 'edit' || /apply_?patch|file_?write|\bwrite\b|\bedit\b/i.test(call.title || '');
    if (isEdit && input && typeof input === 'object') {
        const obj = input as Record<string, unknown>;
        const path = firstString(obj, ['path', 'file_path', 'filePath', 'file']);
        const content = firstString(obj, ['content', 'text', 'new_text', 'newText', 'contents']);
        if (path && content !== null) {
            return [{ path, lines: content.split('\n').map((text): DiffLine => ({ type: 'add', text })) }];
        }
    }

    return null;

    function firstString(obj: Record<string, unknown>, keys: string[]): string | null {
        for (const key of keys) {
            if (typeof obj[key] === 'string') {
                return obj[key] as string;
            }
        }
        return null;
    }
}
