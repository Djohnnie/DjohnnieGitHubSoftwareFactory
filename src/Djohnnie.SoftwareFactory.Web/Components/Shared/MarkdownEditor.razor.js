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

    return {
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
