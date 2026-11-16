export function isHidden() {
    return document.hidden;
}

export function waitForVisibility(hidden) {
    if (isHidden() === hidden) return Promise.resolve();
    return new Promise((resolve, reject) => {
        const timer = setTimeout(() => {
            document.removeEventListener('visibilitychange', changed);
            reject(new Error('Timed out waiting for the requested tab visibility'));
        }, 20000);
        function changed(event) {
            if (!event.isTrusted || isHidden() !== hidden) return;
            clearTimeout(timer);
            document.removeEventListener('visibilitychange', changed);
            resolve();
        }
        document.addEventListener('visibilitychange', changed);
    });
}
