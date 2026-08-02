export async function typeset(element) {
    if (!element || !window.MathJax?.typesetPromise) {
        return;
    }

    await window.MathJax.startup?.promise;
    window.MathJax.typesetClear?.([element]);
    await window.MathJax.typesetPromise([element]);
}
