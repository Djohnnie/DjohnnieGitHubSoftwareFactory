// Replaces Toast UI's default image handling (which inlines images as base64 data URIs) for one editor.
// `wrapper` is the ToastUI interop object of the ToastUIEditor package; its `instance` is the Toast UI Editor.
export function configureImages(wrapper, dotNetRef, enabled, maxSize) {
    const editor = wrapper?.instance;
    if (!editor) {
        return false;
    }

    editor.removeHook("addImageBlobHook");

    if (!enabled) {
        editor.removeToolbarItem("image");
        editor.addHook("addImageBlobHook", () => {
            dotNetRef.invokeMethodAsync("ReportImageError", "Images are not supported in this editor.");
        });
        return true;
    }

    editor.addHook("addImageBlobHook", (blob, callback) => {
        if (blob.size > maxSize) {
            dotNetRef.invokeMethodAsync("ReportImageError", `Images can be at most ${Math.floor(maxSize / 1048576)} MB.`);
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
    });
    return true;
}
