// The browser's time zone, so the server can show timestamps in the user's local time (BrowserTimeZone.cs).
// offsetMinutes is ahead of UTC (JavaScript's getTimezoneOffset is behind it), used when the server doesn't know the IANA id.
export function timeZone() {
    let id = null;
    try {
        id = Intl.DateTimeFormat().resolvedOptions().timeZone ?? null;
    } catch {
        id = null;
    }
    return { id: id, offsetMinutes: -new Date().getTimezoneOffset() };
}
