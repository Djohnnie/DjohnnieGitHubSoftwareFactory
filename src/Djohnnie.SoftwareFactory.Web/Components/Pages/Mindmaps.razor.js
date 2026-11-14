// Dragging on the mindmap page. Dragging an idea moves it (with everything below it) in the browser only, without a
// round trip per mouse move; dropping it tells the page how far it moved, or which idea it was dropped on. Dragging
// the background pans the map. A click without moving selects an idea (or, on the background, nothing); a
// double-click edits it in place.
//
// Blazor owns the positions (left/top of the ideas, d of the lines). While dragging, this module only sets the
// `translate` style of the ideas and lines that move, hides the line to the dragged idea's parent and draws that
// line on the page's spare path. After the page has rendered the new positions it calls settle(), which removes
// all of that again, so whatever Blazor rendered (changed or not) is what shows.

const dragThreshold = 4;
const doubleClickMs = 400;

export function attach(stage, world, page) {
    let pan = { x: 0, y: 0 };
    let drag = null;
    let lastClick = null;

    const applyPan = () => {
        world.style.transform = `translate(${pan.x}px, ${pan.y}px)`;
    };

    const nodeElement = id => world.querySelector(`[data-node="${id}"]`);

    // The id and everything below it, from the data-parent attributes Blazor renders.
    const subtreeOf = id => {
        const children = new Map();
        for (const element of world.querySelectorAll("[data-node]")) {
            const parent = element.dataset.parent || "";
            if (!children.has(parent)) {
                children.set(parent, []);
            }
            children.get(parent).push(element.dataset.node);
        }
        const result = new Set([id]);
        const queue = [id];
        while (queue.length > 0) {
            for (const child of children.get(queue.shift()) || []) {
                if (!result.has(child)) {
                    result.add(child);
                    queue.push(child);
                }
            }
        }
        return result;
    };

    const curve = (x1, y1, x2, y2) => {
        if (Math.abs(x2 - x1) >= Math.abs(y2 - y1)) {
            const mx = (x1 + x2) / 2;
            return `M${x1} ${y1} C${mx} ${y1} ${mx} ${y2} ${x2} ${y2}`;
        }
        const my = (y1 + y2) / 2;
        return `M${x1} ${y1} C${x1} ${my} ${x2} ${my} ${x2} ${y2}`;
    };

    // The idea under the pointer that isn't being dragged, and could take the dragged idea below it.
    const dropTargetAt = (clientX, clientY) => {
        for (const element of document.elementsFromPoint(clientX, clientY)) {
            const target = element.closest("[data-node]");
            if (target && world.contains(target) && !drag.ids.has(target.dataset.node)) {
                return target;
            }
        }
        return null;
    };

    const setDropTarget = target => {
        if (drag.target === target) {
            return;
        }
        drag.target?.classList.remove("mindmap-drop-target");
        drag.target = target;
        target?.classList.add("mindmap-drop-target");
    };

    const onPointerDown = event => {
        if (event.button !== 0 || drag || event.target.closest("button, a, input, textarea, .mindmap-overlay")) {
            return;
        }
        const element = event.target.closest("[data-node]");
        drag = {
            pointerId: event.pointerId,
            startX: event.clientX,
            startY: event.clientY,
            moved: false
        };
        if (element) {
            const id = element.dataset.node;
            const ids = subtreeOf(id);
            const parent = element.dataset.parent ? nodeElement(element.dataset.parent) : null;
            Object.assign(drag, {
                kind: "node",
                id,
                ids,
                elements: [...ids].map(nodeElement).filter(e => e),
                lines: [...world.querySelectorAll("path[data-to]")].filter(line => ids.has(line.dataset.to) && ids.has(line.dataset.from)),
                parentLine: world.querySelector(`path[data-to="${id}"]`),
                parent,
                x: Number(element.dataset.x),
                y: Number(element.dataset.y),
                target: null
            });
        } else {
            Object.assign(drag, { kind: "pan", panX: pan.x, panY: pan.y });
        }
        stage.setPointerCapture(event.pointerId);
    };

    const onPointerMove = event => {
        if (!drag || event.pointerId !== drag.pointerId) {
            return;
        }
        const dx = event.clientX - drag.startX;
        const dy = event.clientY - drag.startY;
        if (!drag.moved && Math.hypot(dx, dy) < dragThreshold) {
            return;
        }
        if (!drag.moved) {
            drag.moved = true;
            stage.classList.add(drag.kind === "node" ? "mindmap-dragging" : "mindmap-panning");
        }

        if (drag.kind === "pan") {
            pan = { x: drag.panX + dx, y: drag.panY + dy };
            applyPan();
            return;
        }

        for (const element of [...drag.elements, ...drag.lines]) {
            element.style.translate = `${dx}px ${dy}px`;
        }
        const spare = world.querySelector(".mindmap-drag-edge");
        if (drag.parent && spare) {
            if (drag.parentLine) {
                drag.parentLine.style.visibility = "hidden";
            }
            spare.setAttribute("d", curve(Number(drag.parent.dataset.x), Number(drag.parent.dataset.y), drag.x + dx, drag.y + dy));
        }
        setDropTarget(dropTargetAt(event.clientX, event.clientY));
    };

    const onPointerUp = event => {
        if (!drag || event.pointerId !== drag.pointerId) {
            return;
        }
        const current = drag;
        drag = null;
        if (stage.hasPointerCapture(event.pointerId)) {
            stage.releasePointerCapture(event.pointerId);
        }
        stage.classList.remove("mindmap-panning");

        if (event.type === "pointercancel") {
            current.moved = false;
            settle();
        }
        if (!current.moved) {
            // A second click on the same idea shortly after the first is a double-click: edit the idea in place.
            // (Detected here because pointer capture makes the browser's own dblclick target unreliable.)
            const now = performance.now();
            if (current.kind === "node" && lastClick && lastClick.id === current.id && now - lastClick.at < doubleClickMs) {
                lastClick = null;
                page.invokeMethodAsync("EditFromBrowser", current.id).catch(() => { });
                return;
            }
            lastClick = current.kind === "node" ? { id: current.id, at: now } : null;
            page.invokeMethodAsync("Select", current.kind === "node" ? current.id : null).catch(() => { });
            return;
        }
        if (current.kind === "node") {
            const target = current.target?.dataset.node ?? null;
            page.invokeMethodAsync("Move", current.id, event.clientX - current.startX, event.clientY - current.startY, target)
                .catch(() => settle());
        }
    };

    // Removes everything dragging changed; called by the page once it rendered the moved positions.
    const settle = () => {
        stage.classList.remove("mindmap-dragging");
        for (const element of world.querySelectorAll("[data-node], path[data-to]")) {
            element.style.translate = "";
            element.style.visibility = "";
            element.classList.remove("mindmap-drop-target");
        }
        world.querySelector(".mindmap-drag-edge")?.removeAttribute("d");
    };

    stage.addEventListener("pointerdown", onPointerDown);
    stage.addEventListener("pointermove", onPointerMove);
    stage.addEventListener("pointerup", onPointerUp);
    stage.addEventListener("pointercancel", onPointerUp);
    applyPan();

    // Ctrl+K (⌘K on a Mac) opens the quick switcher to jump to another mindmap.
    const onKeyDown = event => {
        if ((event.ctrlKey || event.metaKey) && !event.altKey && !event.shiftKey && event.key.toLowerCase() === "k") {
            event.preventDefault();
            page.invokeMethodAsync("OpenSwitcherFromBrowser");
        }
    };
    document.addEventListener("keydown", onKeyDown);

    return {
        settle,
        focusEditor(input) {
            input?.focus();
            input?.select();
        },
        center() {
            pan = { x: 0, y: 0 };
            applyPan();
        },
        // Brings a point of the map (an idea) to the middle of the stage, with a short glide.
        centerOn(x, y) {
            world.classList.add("mindmap-gliding");
            pan = { x: -x, y: -y };
            applyPan();
            setTimeout(() => world.classList.remove("mindmap-gliding"), 450);
        },
        dispose() {
            stage.removeEventListener("pointerdown", onPointerDown);
            stage.removeEventListener("pointermove", onPointerMove);
            stage.removeEventListener("pointerup", onPointerUp);
            stage.removeEventListener("pointercancel", onPointerUp);
            document.removeEventListener("keydown", onKeyDown);
        }
    };
}
