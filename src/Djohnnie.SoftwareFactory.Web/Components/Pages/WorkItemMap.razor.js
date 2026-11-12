// Reports the size of the work item map's stage to the page, once and after every resize (debounced),
// so the layout can spread the items over the room there is or fit them closer together.
export function observe(element, page) {
    let timer = 0;
    let last = "";
    const report = () => {
        const width = Math.round(element.clientWidth);
        const height = Math.round(element.clientHeight);
        const size = `${width}x${height}`;
        if (width > 0 && height > 0 && size !== last) {
            last = size;
            page.invokeMethodAsync("Resize", width, height).catch(() => { });
        }
    };
    const observer = new ResizeObserver(() => {
        clearTimeout(timer);
        timer = setTimeout(report, 120);
    });
    observer.observe(element);
    report();
    return {
        dispose() {
            clearTimeout(timer);
            observer.disconnect();
        }
    };
}
