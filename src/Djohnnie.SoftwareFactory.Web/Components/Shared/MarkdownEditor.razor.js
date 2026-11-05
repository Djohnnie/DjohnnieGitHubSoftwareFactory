// Toast UI Editor (https://ui.toast.com/tui-editor) for MarkdownEditor.razor and MarkdownViewer.razor, through Toast UI's public API only.
// The ToastUIEditor NuGet package is used just for the official Toast UI script and stylesheet it ships as static assets.
import "/_content/ToastUIEditor/toastui-editor-all.min.js";

/**
 * Creates the editor in `element` and returns the object MarkdownEditor.razor calls.
 * Pasted, dropped or picked images are uploaded through `dotNetRef.UploadImage` instead of Toast UI's default base64
 * data URIs; without `options.allowImages` the image button is removed and images are refused.
 */
export function create(element, dotNetRef, options) {
    const editor = new window.toastui.Editor({
        el: element,
        height: options.height,
        minHeight: options.minHeight,
        initialEditType: options.rawMarkdown ? "markdown" : "wysiwyg",
        previewStyle: options.rawMarkdown ? "vertical" : "tab",
        initialValue: options.value ?? "",
        placeholder: options.placeholder ?? "",
        usageStatistics: false,
        autofocus: false,
        hooks: {
            addImageBlobHook: (blob, callback) => {
                uploadImage(dotNetRef, options, blob, callback);
            }
        },
        events: {
            change: () => {
                dotNetRef.invokeMethodAsync("OnEditorChange", editor.getMarkdown());
            },
            blur: () => {
                dotNetRef.invokeMethodAsync("OnEditorBlur", editor.getMarkdown());
            }
        }
    });

    if (!options.allowImages) {
        editor.removeToolbarItem("image");
    }

    const dictation = options.dictation ? addDictationButton(editor, dotNetRef) : null;

    return {
        /** Shows the dictation state on the microphone button: "idle", "listening" or "writing". */
        setDictationState: (state) => {
            dictation?.setState(state);
        },
        setMarkdown: (markdown) => {
            if (editor.getMarkdown() !== markdown) {
                editor.setMarkdown(markdown ?? "", false);
            }
        },
        destroy: () => {
            editor.destroy();
        }
    };
}

const MicIcon = '<svg viewBox="0 0 24 24" width="18" height="18" aria-hidden="true" focusable="false"><path fill="currentColor" d="M12 14c1.66 0 3-1.34 3-3V5c0-1.66-1.34-3-3-3S9 3.34 9 5v6c0 1.66 1.34 3 3 3zm5.3-3c0 3-2.54 5.1-5.3 5.1S6.7 14 6.7 11H5c0 3.41 2.72 6.23 6 6.72V21h2v-3.28c3.28-.48 6-3.3 6-6.72h-1.7z"/></svg>';

const DictationLabels = {
    idle: "Dictate (the text is fitted into the description)",
    listening: "Stop dictating",
    writing: "Writing the description..."
};

/** Adds a microphone button as the last toolbar group; clicking it calls `dotNetRef.ToggleDictation()`. */
function addDictationButton(editor, dotNetRef) {
    const button = document.createElement("button");
    button.type = "button";
    button.className = "markdown-editor-dictate";
    button.innerHTML = MicIcon;
    button.addEventListener("click", event => {
        event.preventDefault();
        dotNetRef.invokeMethodAsync("ToggleDictation");
    });

    const setState = state => {
        button.dataset.state = state;
        button.disabled = state === "writing";
        button.title = DictationLabels[state];
        button.setAttribute("aria-label", DictationLabels[state]);
        button.setAttribute("aria-pressed", state === "listening" ? "true" : "false");
    };
    setState("idle");

    // A group index past the last group adds a new group at the end of the toolbar.
    editor.insertToolbarItem({ groupIndex: 99, itemIndex: 0 }, { name: "dictate", el: button });
    return { setState };
}

function uploadImage(dotNetRef, options, blob, callback) {
    if (!options.allowImages) {
        dotNetRef.invokeMethodAsync("ReportImageError", "Images are not supported in this editor.");
        return;
    }
    if (blob.size > options.maxImageSize) {
        dotNetRef.invokeMethodAsync("ReportImageError", `Images can be at most ${Math.floor(options.maxImageSize / 1048576)} MB.`);
        return;
    }

    const name = blob.name || "image";
    dotNetRef.invokeMethodAsync("UploadImage", DotNet.createJSStreamReference(blob), name, blob.size)
        .then(url => {
            if (url) {
                callback(url, name.replace(/\.[^.]+$/, ""));
            }
        })
        .catch(error => console.error("Image upload failed", error));
}

/** Creates a read-only Toast UI viewer in `element` (used by MarkdownViewer.razor). */
export function createViewer(element, markdown) {
    const viewer = window.toastui.Editor.factory({
        el: element,
        viewer: true,
        initialValue: markdown ?? "",
        usageStatistics: false
    });

    return {
        setMarkdown: (value) => {
            viewer.setMarkdown(value ?? "");
        },
        destroy: () => {
            viewer.destroy();
        }
    };
}
