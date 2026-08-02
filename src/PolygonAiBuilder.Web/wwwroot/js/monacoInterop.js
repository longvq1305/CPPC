const editors = new Map();
let monacoPromise;

function loadMonaco() {
    if (window.monaco?.editor) {
        return Promise.resolve(window.monaco);
    }

    if (monacoPromise) {
        return monacoPromise;
    }

    monacoPromise = new Promise((resolve, reject) => {
        const start = () => {
            window.require.config({ paths: { vs: '/lib/monaco/vs' } });
            window.require(['vs/editor/editor.main'], () => resolve(window.monaco), reject);
        };

        if (window.require?.config) {
            start();
            return;
        }

        const loader = document.createElement('script');
        loader.src = '/lib/monaco/vs/loader.js';
        loader.onload = start;
        loader.onerror = () => reject(new Error('Không tải được Monaco Editor.'));
        document.head.appendChild(loader);
    });

    return monacoPromise;
}

export async function create(shell, value, dotnetReference) {
    if (!shell || editors.has(shell.id)) {
        return;
    }

    const fallback = shell.querySelector('.monaco-fallback');
    try {
        const monaco = await loadMonaco();
        const host = document.createElement('div');
        host.className = 'monaco-host';
        shell.appendChild(host);
        const editor = monaco.editor.create(host, {
            value: value ?? '',
            language: 'plaintext',
            automaticLayout: true,
            minimap: { enabled: false },
            wordWrap: 'on',
            lineNumbers: 'on',
            lineNumbersMinChars: 3,
            scrollBeyondLastLine: false,
            fontSize: 14,
            tabSize: 2,
            padding: { top: 12, bottom: 12 },
            accessibilityPageSize: 20
        });
        const changeSubscription = editor.onDidChangeModelContent(() => {
            dotnetReference.invokeMethodAsync('EditorChanged', editor.getValue());
        });
        fallback.hidden = true;
        editors.set(shell.id, { editor, changeSubscription, host });
    } catch (error) {
        fallback.hidden = false;
        console.warn(error);
    }
}

export function update(shell, value) {
    const instance = shell ? editors.get(shell.id) : null;
    if (instance && instance.editor.getValue() !== (value ?? '')) {
        instance.editor.setValue(value ?? '');
    }
}

export function command(shell, commandId) {
    const instance = shell ? editors.get(shell.id) : null;
    instance?.editor.trigger('statement-toolbar', commandId, null);
    instance?.editor.focus();
}

export function dispose(shell) {
    const instance = shell ? editors.get(shell.id) : null;
    if (!instance) {
        return;
    }

    instance.changeSubscription.dispose();
    instance.editor.dispose();
    instance.host.remove();
    editors.delete(shell.id);
}
