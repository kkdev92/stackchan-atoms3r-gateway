# Configuration

The reference host uses eight sections under `StackChan:`. Environment variables use the double-underscore form:
`StackChan__<Section>__<Key>`.

At startup, the gateway logs the effective values needed to diagnose limits and provider
selection. It logs secret lengths instead of secret values, but it also logs endpoints and model
names. Review and redact the output before sharing it outside your environment.

## Where a value can come from

|  |  |
| --- | --- |
| `appsettings.json` | Defaults, shipped in the reference host. Secrets are empty strings here and must stay that way |
| Environment variables | `StackChan__Atoms3R__Token=…`. This is where secrets belong |
| `start-gateway.ps1` | `-Token`, `-Offline`, `-Urls`, `-AllowUnauthenticatedLan`. Sets the environment variables above and starts the host |

Do not add secrets to `appsettings.json`. It is a tracked file, so Git ignore rules do not
protect values added to it. Use environment variables or the startup script options instead.

## `StackChan:Runtime`

| Key | Default | What it decides |
| --- | --- | --- |
| `MaxConcurrentTurns` | `2` | Turns running at once. The gate is checked **before** a session is registered, so a refused turn leaves no state behind |
| `TurnTimeoutSeconds` | `120` | Limit for the whole turn. It bounds model and provider stalls even while SSE keep-alives continue. Allow enough time for cold model startup, but keep the value finite so a device cannot remain in `thinking` indefinitely |
| `MaxSessions` | `128` | Maximum conversations retained by the runtime. Size this above the expected number of active devices; idle entries are removed before the runtime evicts the least-recently-used entry |
| `SessionIdleTimeoutMinutes` | `120` | Inactivity period before a conversation is forgotten. The next turn starts with a new conversation history |

## `StackChan:Atoms3R`

| Key | Default | What it decides |
| --- | --- | --- |
| `Token` | *(empty)* | The device token. **Required** unless `Offline:Enabled` is true. Compared with a fixed-time primitive; request length remains observable. The firmware needs exactly 32 characters, so `set(31)` in the startup dump names the problem |
| `MaxRequestBodyBytes` | `2097152` | Request body cap, sized for one WAV. Checked from the declared length first, and counted while reading when the request is chunked |
| `MaxSpokenTextBytes` | `4096` | Cap on an utterance sent as text instead of audio. See below |
| `KeepAliveIntervalSeconds` | `3` | SSE comment interval during silence. Valid values are 1–25 seconds, below the device's 30-second inter-event timeout |

**Why the utterance has its own cap.** The targeted firmware refuses input over 480 bytes before
sending it. The 4,096-byte gateway limit also accommodates clients that call the endpoint
directly. The body cap alone is insufficient: text can fit inside a 2 MiB body, reach the system
prompt and conversation history, and only later be truncated to 512 bytes for the device wire.

## `StackChan:WhisperCpp`

| Key | Default | What it decides |
| --- | --- | --- |
| `Endpoint` | `http://127.0.0.1:8081` | whisper.cpp server |
| `Path` | `/inference` | Inference path |
| `Language` | `ja` | `auto` leaves detection to the recognizer |
| `MinLanguageProbability` | `0.5` | Below this the result is discarded. See below |
| `TimeoutSeconds` | `30` | One attempt. The effective wait is `min(remaining turn budget, this)` |
| `MaxResponseBytes` | `4194304` | Response cap. A normal response is tens of KB |

whisper.cpp can return text for silence or other non-speech audio. The gateway discards results
whose language probability is below `MinLanguageProbability`. Confidence values depend on the
model and audio environment, especially with `Language=auto`, so tune the threshold with samples
from your deployment. Setting it to `0` disables this check and uses the plain `json` response.

## `StackChan:PiperPlus`

| Key | Default | What it decides |
| --- | --- | --- |
| `Endpoint` | `http://127.0.0.1:5000` | Synthesis server |
| `Path` | `/tts_live.wav` | `/tts_live.wav` streams while synthesizing; `/tts_stream.wav` returns when finished |
| `LengthScale` | `1.0` | Speaking rate. `0.5` is fast, `2.0` is slow |
| `Character` | *(empty)* | Voice name. Empty leaves it to the server; the server warns about an empty string, so it is omitted rather than sent |
| `TimeoutSeconds` | `30` | One sentence |
| `MaxResponseBytes` | `8388608` | WAV response cap. Applied before decoding or resampling so an unexpected response cannot grow without limit |

The returned sample rate depends on the voice model (Japanese models are commonly 22050 Hz).
It is resampled to 16 kHz mono before it reaches the runtime.

## `StackChan:Agent`

| Key | Default | What it decides |
| --- | --- | --- |
| `Endpoint` | `http://127.0.0.1:5273/v1` | OpenAI-compatible endpoint |
| `Model` | `Phi-4-mini-instruct-generic-cpu:5` | Model id as the endpoint publishes it. `GET {Endpoint}/models` lists them |
| `ApiKey` | `local` | A placeholder for a local endpoint |
| `Name` | `StackChan` | Agent name |
| `MaxOutputTokens` | `512` | One generation |
| `MaxHistoryMessages` | `10` | Maximum chat messages sent from a session's history. Older messages are removed before each generation |
| `Instructions` | *(empty)* | System prompt. The SDK has no language or persona default; the reference host supplies its own, and startup fails if the value is empty |
| `MaxSessions` | `128` | Conversation histories kept |
| `SessionIdleTimeoutMinutes` | `120` | Idle before a history is dropped |

## `StackChan:Weather`

| Key | Default | What it decides |
| --- | --- | --- |
| `Endpoint` | `https://api.weatherapi.com/v1` | Weather service |
| `ApiKey` | *(empty)* | Keep this value out of `appsettings.json`. The key travels in the query string, so provider logs have their query stripped |
| `DefaultLocation` | `Tokyo` | Used when the model calls the capability without a location |
| `Language` | `ja` | Response language |
| `TimeoutSeconds` | `10` | One call |

With no key the capability is not registered and startup still succeeds. Weather responses are
unavailable until a key is configured.

## `StackChan:Offline`

| Key | Default | What it decides |
| --- | --- | --- |
| `Enabled` | `false` | Replaces recognition, the agent and synthesis with fixed responses |
| `Transcript` | `(fixed-response mode)` | What recognition "hears" |
| `FixedResponse` | `[]` | Sentences the agent returns. Empty falls back to a built-in reply |

Fixed-response mode supports initial device setup and wire regression checks. It provides
diagnostic fixed responses rather than conversational behavior.

## `StackChan:Security`

| Key | Default | What it decides |
| --- | --- | --- |
| `AllowUnauthenticatedLan` | `false` | Permits a tokenless gateway to listen on a non-loopback address |

Fixed-response mode does not require a token, while the default listen address is `0.0.0.0`.
Using both settings would let any host on the local network start a conversation, so startup
rejects the combination unless `AllowUnauthenticatedLan` is explicitly enabled. The gateway does
not rewrite the configured listen address.

## Host settings

| Key | Default | Notes |
| --- | --- | --- |
| `Urls` | `http://0.0.0.0:8787` | The device arrives over the LAN, so loopback is not a usable default |
| `Logging:LogLevel:Default` | `Information` | The startup dump and per-turn lines are `Information` |

Note that the shipped provider endpoints are `127.0.0.1`. The downstream services may listen on
`0.0.0.0`, but the gateway does not reach them over the network by default.
