// Speech-to-text for DictationTextField.razor (one phrase) and MarkdownEditor.razor (continuous) with the Azure AI Speech
// SDK for JavaScript (browser bundle, sets window.SpeechSDK).
// The bundle is served from wwwroot/lib; the version is in the folder name, so update the path together with the files.
import "/lib/microsoft-cognitiveservices-speech-sdk-1.53.0/microsoft.cognitiveservices.speech.sdk.bundle-min.js";

/** How often a continuous recognition renews its token (tokens are valid for 10 minutes, and reused until 2 minutes before). */
const TokenRenewalMs = 60_000;

let active = null;

/**
 * `token` is a short-lived authorization token issued by the server, so the Speech key never reaches the browser.
 * With more than one locale in `languages`, Azure detects the spoken one.
 */
function createRecognizer(sdk, token, region, languages) {
    const speechConfig = sdk.SpeechConfig.fromAuthorizationToken(token, region);
    const audioConfig = sdk.AudioConfig.fromDefaultMicrophoneInput();
    if (languages.length > 1) {
        const autoDetect = sdk.AutoDetectSourceLanguageConfig.fromLanguages(languages);
        return sdk.SpeechRecognizer.FromConfig(speechConfig, autoDetect, audioConfig);
    }

    speechConfig.speechRecognitionLanguage = languages[0];
    return new sdk.SpeechRecognizer(speechConfig, audioConfig);
}

/** Releases the microphone. */
function close(recognizer) {
    try {
        recognizer.close();
    } catch {
        // Already closed.
    }
}

/**
 * Listens to the default microphone until the speaker pauses and resolves with
 * `{ status: "recognized", text }`, `{ status: "nomatch" }`, `{ status: "canceled" }` or `{ status: "error", error }`.
 */
export function recognizeOnce(token, region, languages) {
    cancel();
    const sdk = window.SpeechSDK;

    return new Promise(resolve => {
        let recognizer;
        try {
            recognizer = createRecognizer(sdk, token, region, languages);
        } catch (error) {
            resolve({ status: "error", error: String(error?.message ?? error) });
            return;
        }

        const session = {
            finish: result => {
                if (session.done) {
                    return;
                }
                session.done = true;
                if (active === session) {
                    active = null;
                }
                close(recognizer);
                resolve(result);
            }
        };
        session.cancel = () => session.finish({ status: "canceled" });
        session.stop = session.cancel;
        active = session;

        recognizer.recognizeOnceAsync(
            result => {
                if (result.reason === sdk.ResultReason.RecognizedSpeech) {
                    session.finish({ status: "recognized", text: result.text });
                } else if (result.reason === sdk.ResultReason.Canceled) {
                    const details = sdk.CancellationDetails.fromResult(result);
                    session.finish(details.reason === sdk.CancellationReason.Error
                        ? { status: "error", error: details.errorDetails }
                        : { status: "canceled" });
                } else {
                    session.finish({ status: "nomatch" });
                }
            },
            error => {
                session.finish({ status: "error", error: String(error) });
            });
    });
}

/**
 * Listens to the default microphone until `stop()` is called (or after `maxSeconds`) and resolves with
 * `{ status: "recognized", text }` (everything that was said), `{ status: "nomatch" }` when nothing was recognized,
 * `{ status: "canceled" }` after `cancel()`, or `{ status: "error", error }`.
 * While listening it reports the text so far, including the phrase being spoken, to
 * `listener.OnDictationProgress(text)`, and renews the token through `listener.GetSpeechToken()`.
 */
export function recognizeContinuous(listener, token, region, languages, maxSeconds) {
    cancel();
    const sdk = window.SpeechSDK;

    return new Promise(resolve => {
        let recognizer;
        try {
            recognizer = createRecognizer(sdk, token, region, languages);
        } catch (error) {
            resolve({ status: "error", error: String(error?.message ?? error) });
            return;
        }

        const phrases = [];
        const report = partial => {
            const text = [...phrases, partial].filter(phrase => phrase).join(" ");
            listener.invokeMethodAsync("OnDictationProgress", text).catch(() => { });
        };
        const renewal = setInterval(() => {
            listener.invokeMethodAsync("GetSpeechToken")
                .then(renewed => {
                    if (renewed) {
                        recognizer.authorizationToken = renewed;
                    }
                })
                .catch(() => { });
        }, TokenRenewalMs);

        let limit;
        const session = {
            finish: result => {
                if (session.done) {
                    return;
                }
                session.done = true;
                clearInterval(renewal);
                clearTimeout(limit);
                if (active === session) {
                    active = null;
                }
                resolve(result);
            },
            // Stops listening, but keeps the phrase that was being spoken: the SDK recognizes it before it stops.
            stop: () => {
                if (session.done || session.stopping) {
                    return;
                }
                session.stopping = true;
                recognizer.stopContinuousRecognitionAsync(
                    () => {
                        close(recognizer);
                        session.finish(phrases.length > 0 ? { status: "recognized", text: phrases.join(" ") } : { status: "nomatch" });
                    },
                    error => {
                        close(recognizer);
                        session.finish({ status: "error", error: String(error) });
                    });
            },
            cancel: () => {
                if (session.done) {
                    return;
                }
                session.finish({ status: "canceled" });
                recognizer.stopContinuousRecognitionAsync(() => close(recognizer), () => close(recognizer));
            }
        };
        limit = setTimeout(() => session.stop(), maxSeconds * 1000);
        active = session;

        recognizer.recognizing = (_, e) => {
            report(e.result.text);
        };
        recognizer.recognized = (_, e) => {
            if (e.result.reason === sdk.ResultReason.RecognizedSpeech && e.result.text) {
                phrases.push(e.result.text);
                report("");
            }
        };
        recognizer.canceled = (_, e) => {
            if (e.reason === sdk.CancellationReason.Error) {
                close(recognizer);
                session.finish({ status: "error", error: e.errorDetails });
            }
        };
        recognizer.startContinuousRecognitionAsync(
            () => { },
            error => {
                close(recognizer);
                session.finish({ status: "error", error: String(error) });
            });
    });
}

/** Stops the current recognition (if any); a continuous recognition resolves with what was said so far. */
export function stop() {
    active?.stop();
}

/** Stops the current recognition (if any) without a result and releases the microphone. */
export function cancel() {
    active?.cancel();
}
