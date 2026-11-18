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
        initialEditType: "wysiwyg",
        previewStyle: "tab",
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

    // Inserted after the link button (table, image, link group) before the image button may be removed.
    if (options.allowFileLinks) {
        editor.insertToolbarItem({ groupIndex: 3, itemIndex: 3 }, {
            name: "knowledgeFileLink",
            tooltip: "Link a knowledge base file",
            el: createFileLinkButton(editor, dotNetRef)
        });
    }

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

/**
 * The toolbar button that lets MarkdownEditor.razor pick a knowledge base file (`PickFileLink`) and inserts the link at
 * the cursor with Toast UI's own addLink command, which works in both the WYSIWYG and the Markdown mode.
 */
function createFileLinkButton(editor, dotNetRef) {
    const button = document.createElement("button");
    button.type = "button";
    button.className = "toastui-editor-toolbar-icons dghsf-file-link";
    button.setAttribute("aria-label", "Link a knowledge base file");
    // Material "local_library" (outlined, like Toast UI's own icons), the knowledge base's icon in the navigation.
    button.innerHTML = '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M12 9c1.65 0 3-1.35 3-3s-1.35-3-3-3-3 1.35-3 3 1.35 3 3 3zm0-4c.55 0 1 .45 1 1s-.45 1-1 1-1-.45-1-1 .45-1 1-1zm0 6.55C9.64 9.35 6.48 8 3 8v11c3.48 0 6.64 1.35 9 3.55 2.36-2.19 5.52-3.55 9-3.55V8c-3.48 0-6.64 1.35-9 3.55zm7 5.58c-2.53.34-4.93 1.3-7 2.82-2.06-1.52-4.47-2.49-7-2.83v-6.95c2.1.38 4.05 1.35 5.64 2.83L12 14.28l1.36-1.27c1.59-1.48 3.54-2.45 5.64-2.83v6.95z"/></svg>';
    button.addEventListener("click", () => {
        dotNetRef.invokeMethodAsync("PickFileLink")
            .then(link => {
                if (link) {
                    editor.focus();
                    editor.exec("addLink", { linkUrl: link.url, linkText: link.text });
                }
            })
            .catch(error => console.error("Linking a knowledge base file failed", error));
    });
    return button;
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
