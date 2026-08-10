// Runnable self-check for the diff parser. Source lives in ../webview/diffModel.
// Run: npm run compile-tests && node out/webviews/diffView.js
import * as assert from 'assert';
import { buildDiffModel } from '../webview/diffModel';

export { buildDiffModel } from '../webview/diffModel';
export type { DiffLine, FileDiff } from '../webview/diffModel';

if (require.main === module) {
    const patch = [
        '*** Begin Patch',
        '*** Update File: src/a.ts',
        '@@ foo',
        ' keep',
        '-old',
        '+new',
        '*** Add File: src/b.ts',
        '+hello',
        '*** End Patch'
    ].join('\n');

    const model = buildDiffModel({ kind: 'edit', rawInput: { patch } });
    assert.ok(model && model.length === 2, 'two files');
    assert.strictEqual(model[0].path, 'src/a.ts');
    assert.deepStrictEqual(model[0].lines, [
        { type: 'meta', text: '@@ foo' },
        { type: 'ctx', text: 'keep' },
        { type: 'del', text: 'old' },
        { type: 'add', text: 'new' }
    ]);
    assert.strictEqual(model[1].path, 'src/b.ts');
    assert.deepStrictEqual(model[1].lines, [{ type: 'add', text: 'hello' }]);

    const write = buildDiffModel({ kind: 'edit', title: 'file_write', rawInput: { path: 'x.txt', content: 'a\nb' } });
    assert.ok(write && write.length === 1 && write[0].lines.length === 2, 'file_write -> added lines');

    assert.strictEqual(buildDiffModel({ kind: 'read', rawInput: { path: 'x' } }), null, 'non-edit -> null');

    console.log('diffView self-check passed');
}
