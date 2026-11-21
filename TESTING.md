# Verification and limitations

The repair was checked against the public web application `kh-app.js?v=7acd064949`.
It uses the application's `/t/api` route and the current public-ID request header,
rather than sending forged browser headers to the direct worker.

Run `./test.sh` on macOS/Linux, or these commands on any desktop OS:

```sh
dotnet test KindleHub.Tests/KindleHub.Tests.csproj -c Release
dotnet test KindleHub.UiTests/KindleHub.UiTests.csproj -c Release
dotnet run --project KindleHub.Client.Gametest -c Release
```

.NET 10 SDK is required; install the .NET 8 runtime to run the desktop/game
executable. Tests intentionally target individual projects: restoring the entire
solution also requires the Android workload. Normal tests make no external
requests and do not restore the developer's saved session. Live checks are
explicitly opt-in: `KINDLEHUB_LIVE_TESTS=1 ./test.sh`.

| Area | Automated coverage | Real service verification |
| --- | --- | --- |
| Connection | Actual status probe; HTTP 403/429/5xx, cancellation, network failure; route and native identity | Same-site route, public reads |
| Account | Register/login outcomes; encrypted sync, fresh reads, protection against downgrading remotely strengthened accounts | Two disposable accounts: registration, login, encrypted save/reload |
| Encryption | Independent JavaScript/Node AES-GCM vectors for all four legacy account buckets, both chat formats, legacy KHA2 and recovery-enabled KHA2; wrong password and malformed envelopes | Synthetic fixture interoperability |
| Messages | Encrypted send/read, edit, delete, important, reaction, replies, group contracts, public-ID search/profiles, inbox refusal handling | Global and private chat reads/writes, edit/important/reactions; friend search and inbox delivery |
| Community | Topic directory/create, recovery, reports and moderator contracts; polls/votes, story/app/flipbook codecs, notes and persisted preferences | Authenticated topic/cloud reads; moderator actions require a role and were not exercised live |
| Mail | Recipient normalization, encrypted subject/body, fresh mailbox polling, deletion and owner-secret contract | Two-account send, receipt/decryption and deletion |
| Store | Search/filter transport, download including owner-only apps, publish/delete/rate/review contracts, cache invalidation | Catalog, publishing, owner download and unpublishing; moderator review requires a role |
| Scores/presence | Score bounds, per-account run tokens, deduplication, durable offline queue/recovery, public profiles | Score/presence/game-discovery reads, run-token authorization and presence writes; fake scores not posted |
| UI | All ten page views load/layout at desktop and mobile widths; home command, flipbook drawing/undo/resize/share | Headless smoke checks; not a full click-through of every control |
| Games | Existing 403 checks covering arcade games, rule boundaries, game completion/scoring and multiplayer Connect 4 protocol simulation | Local simulation and two-account encrypted relay round trip; full cross-device game UI not exercised |

The API contract matrix exercises serialization and destinations with an isolated
HTTP handler. It cannot prove that the production server accepts an authenticated
write. The public live suite only reads data. The separately authorized account suite
creates two disposable accounts, tests private writes and temporary app publishing,
and removes test mail/messages/apps afterward. It leaves the accounts and empty
private rooms for repeat runs. A successful mocked test is not a claim that a
moderator-only or payment-gated operation was tested against production.

To explicitly run account tests as well:

```sh
KINDLEHUB_LIVE_ACCOUNT_TESTS=1 KINDLEHUB_LIVE_TESTS=1 ./test.sh
```

The account suite stores random credentials in the OS temporary directory under
`kindlehub-dotnet-live-tests/accounts.json`, with owner-only file permissions on
Unix. Override that directory with `KINDLEHUB_TEST_ACCOUNT_DIR`; keep it outside the
repository. Accounts are reused on later runs. Credentials are never printed or
committed. These tests must only run when creating test accounts and sending test
traffic is authorized. CI runs isolated tests only.

Live verification on 2026-11-17 passed registration/login, encrypted cloud sync,
private chat send/read/edit/importance/reactions, friend search with underscores,
public-ID inbox delivery, mail receipt/decryption, publishing/owner download/delete,
encrypted multiplayer relay, inbox receipt, score-run authorization, authenticated
topic/cloud reads, and presence heartbeat publication. The test run cleaned up its app, mail and chat
messages. Live score writes and privileged moderator actions were not exercised.

KHA2 accounts can be unlocked using their normal password. Their envelope and
recovery boxes are preserved on sync; derived data keys stay in memory. After
restarting this client, sign in with the password again to unlock a KHA2 account.
PIN-based Superlock (`KHSL1`) is not supported: login explains this and sync refuses
to overwrite its cloud state. Recovery-key entry and trusted-device enrollment are
not implemented. Android/iOS runtime behavior and platform packaging were not
verified by this repair; the existing mobile host limitations still apply.

The server can change this private protocol again. Permission refusals remain
failures. In particular, inbox 4xx refusals do not trigger an alternate write, and
public recipient IDs never fall back to legacy hex-derived inbox rooms.
