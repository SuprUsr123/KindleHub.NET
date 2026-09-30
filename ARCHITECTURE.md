# KindleHub Pro — Architecture Documentation

## 1. Account Handling

### API Gateway
- **Base URL:** `https://kindlehub-api.arancool3000.workers.dev`
- **Backend:** Supabase REST API at `/rest/v1/`

### Authentication
| Component | Details |
|-----------|---------|
| **Username** | Lowercase alphanumeric + `._-` (1-40 chars), canonicalized via `_khCanonicalUsername()` |
| **Password** | Any string |
| **Auth Token** | SHA-256(`"kh::" + lowercase(username) + "::" + password`) — derived client-side via `_userKey()` |
| **User ID** | First 16 chars of auth token |
| **Encryption Key** | Same as auth token (used for AES-GCM encryption of account state) |

### Account Flow

#### Registration (`authRegister`)
1. Validates username (format, banned list, reserved names, impersonation)
2. Derives auth token via `_userKey(username, password)`
3. Checks if user exists in `kh_users` table (Supabase)
3. Creates encrypted account state via `_khPackAccount()`
4. Saves to Supabase via `_saveUser(token, email, state)`

#### Login (`authLogin`)
1. Canonicalizes username
2. Derives auth token via `_userKey()`
3. Checks `kh_users` table
4. Attempts to decrypt offline credential cache (`_offlineCred`)
5. Merges cloud state if available

#### Data Encryption
- **All user data encrypted client-side** via AES-GCM before upload
- Functions: `_khPackAccount()` / `_decryptState()`
- Server only stores ciphertext in `kh_users.state`
- Key derived from auth token via PBKDF2 (210,000 iterations, SHA-256)

#### Sync
- Table: `kh_users` with columns `{hash, state, updated_at}`
- Background sync every ~30s when online (`_khSaveLoop`)
- Triggered on: visibility change, online event, periodic interval

#### Session Persistence
- In-memory: `v63.authToken`, `v63.userId`, `v63._encKey`, `v63.email`, `v63.syncEnabled`
- LocalStorage: `_saveCreds()` / `_offlineCred()` for offline login

---

## 2. Social Functions

### Database Tables

#### `kh_messages`
| Column | Purpose |
|--------|---------|
| `group_code` | References `kh_groups.code` |
| `user_id` | Sender's user ID (16-char prefix of auth token) |
| `display_name` | Sender's display name |
| `text` | Message content (encrypted client-side) |
| `ts` | Timestamp (ISO string) |
| `reactions` | JSON object of emoji → count |
| `device_hint` | Device fingerprint for moderation |
| `location_hint` | Approximate location for neighbourhood |

#### `kh_groups`
| Group Prefix | Purpose |
|--------------|---------|
| `DM:*` | Direct messages (1:1) |
| `inbox-*` | Notification inboxes |
| `mp-*` | Multiplayer game rooms |
| `000000000000` | Global Chat (hardcoded `KH_GLOBAL_GROUP_CODE`) |

- **History Cap:** 50 messages per group (server-side)
- **Encryption:** Messages encrypted via `_msgEncrypt`/`_msgDecrypt` using auth token
- **Realtime:** Supabase realtime subscriptions on `kh_messages` table

### Community View (`community` tab)

#### Tabs
1. **Topics** — Public forum rooms with threads/replies
2. **Neighbourhood** — Single public square (global feed)
3. **Feed/Posts** — Aggregated community posts
4. **Forum** — Alternative topic view
5. **Square** — Another neighbourhood alias

#### Topics
- Created as `kh_groups` with topic-like names
- Listed via: `_sbSelect("kh_groups", "name=like." + encodeURIComponent(TOPIC) + "*&select=code,name,creator,created_at&order=created_at.desc&limit=120")`
- Threads = messages in the group
- Upvote/downvote via reactions on messages

#### Neighbourhood
- Single public square using global group code
- Placeholder: "Say something to the neighbourhood"
- Reads from `kh_messages` with `group_code = KH_GLOBAL_GROUP_CODE`

#### Flipbooks
- Community drawing app (`flipbook` game)
- Shared via `kh_messages` in community groups
- Pixel art / animation frames posted as messages

#### Profile Editing
- **Display Name:** Set in Settings, must match own username (enforced client-side)
- **Profile Picture:** Drawn in Pixel Art app, saved to account state
- **Joined Timestamp:** `profileJoined` in account state
- **Fetch:** `_khFetchProfile(userId)` or `_khFetchProfileByName(username)` from `kh_users`

### App Downloading (App Store)

#### Store Apps
- Fetched via `_khFetchStoreApps()` → calls Supabase RPC `kh_store_download`
- Returns app metadata + HTML/JS bundles

#### Community Apps
- Published by users, AI-reviewed
- Listed in "Community" tab
- Downloaded and installed to `localStorage` as "sideloaded" apps

#### Publishing
- From "Publish" tab in App Store
- Submits to store for AI review before going live

---

## 3. Games Talking to Server

### Leaderboard System (`kh_scores` table)

All games call `_khSubmitScore(gameName, score)` which:
1. Builds payload:
```javascript
{
  id: userId + "_" + gameName + "_" + Date.now() + "_" + Math.floor(Math.random() * 1000),
  game: gameName,
  score: score,
  display_name: userDisplayName,
  user_id: userId,
  date: new Date().toISOString()
}
```
2. Inserts via `_sbInsert("kh_scores", obj)`
3. On failure: queues locally (`_khScoreQueueAdd`) → retries when online

#### Fetching Leaderboard
```javascript
_khFetchScores(gameName, limit) // default limit=10
```
- Queries: `kh_scores` where `game=eq.{gameName}` ordered by `score.desc`
- Filters banned users via `_khIsBannedSync(display_name)`

### Games with Leaderboards

| Game | Score Formula |
|------|--------------|
| `galaga` | Raw score |
| `fruitninja` | Raw score |
| `battleship` | Score |
| `solitaire` | Score |
| `digquest` | Level/depth |
| `hanoi` | Moves (lower = better) |
| `blackjack` | Chips |
| `crazy8` | Score |
| `hangman` | Streak × 10 |
| `mastermind` | (11 - turns) × 100 |
| `memory` | Pairs × 120 - moves × 3 - time |
| `ttt` (tic-tac-toe) | Score |
| `minesweeper` | Bombs × 50 - time |
| `wordle` | (7 - attempts) × 100 + wordLen × 10 |
| `snake` | Score |
| `geometrydash` | Score |
| `flappy` | Score |
| `platformer` | Score |
| `maze` | Level (campaign) or endless level |
| `nerdle` | Score |
| `picpuzzle` | 200 - moves |
| `pacman` | Score |
| `akinator` | Streak |
| `yahtzee` | Total score |
| `connections` | Score |
| `spellingbee` | Score |
| `strands` | Points |
| `perfectcircle` | Score |
| `roller` | LifetimeCoins |
| `slither` | Score |
| `crossyroad` | Score |
| `reversi` | Your discs |
| `sokoban` | Solved × 3 + optimal |
| `simon` | Score |
| `anagrams` | Solved count |
| `kakuro` | Solved |
| `futoshiki` | Solved × 10 |
| `cryptogram` | Solved × 3 + hints |
| `pegs` | 40 - moves |
| `traffic` | SetCleared × 10 + perfect × 5 + endless |
| `freecell` | 2000 - moves |
| `gomoku` | Wins × 10 + streak |
| `mahjong` | Cleared × 10 + clean |
| `dominoes` | Matches × 50 + rounds |
| `nim` | Wins × 10 + streak |
| `stronghold` | Trophies |
| `deephalls` | Depth × 100 + gold |
| `emberdeck` | Cards × 100 + hp / floor × 100 |
| `wildforms` | Caught × 100 + wins |
| `numslide` | 2000 - moves |
| `wordladder` | Score |
| `sumlines` | 3000 - time(ms)/1000 |
| `pairup` | Solved × 10 + perfect × 15 |
| `bridges` | Solved × 20 + noHint × 10 |
| `quickcount` | Score |
| `cargorun` | Score |
| `oddone` | Score |
| `tiletrader` | Score |
| `beambalance` | Score × 10 |
| `nextinline` | Score |
| `loopwire` | Points |
| `deepvein` | Gems |
| `lander` | Score |
| `lastlight` | Score |
| `bucketline` | Level |
| `unoflip` | Wins × 10 + wins2 |
| `barrage` | Best |
| `lineman` | Best |
| `sleepercrew` | (Completed ? 100 : 0) + score × 10 |

### Multiplayer Games (via `kh_groups` + `kh_messages`)

**Room Naming:** `mp-{game}-{random}` (created via `_groupCreate`)

| Game | Mode | Notes |
|------|------|-------|
| `chess` | Online 2-player | `data-online-game="chess"` |
| `ttt` (tic-tac-toe) | Online 2-player | `data-online-game="ttt"` |
| `connect4` | Online 2-player | `data-online-game="connect4"` |
| `dotsboxes` | Online 2-player | `data-online-game="dotsboxes"` |
| `pool` | Online 2-player | Max mode |
| `crazy8` | Online 2-player | Host runs game, each sees own hand, chat panel |
| `battleship` | Online 2-player | Fleet never leaves device |
| `reversi` (Othello) | Online 2-player | Pass-and-play or online; **excluded from leaderboard** |
| `bucketbrigade` | Co-op 1-4 players | Local only (not online) |

**Transport:**
1. Host creates group: `_groupCreate("mp-" + game + "-" + id, name)`
2. Game state synced via `kh_messages` in that group (encrypted)
3. Each player polls/receives realtime updates
4. **Multiplayer scores don't hit leaderboard** — "Online games are not counted in your record"

### Other Server-Interacting Games

| Game | Interaction |
|------|-------------|
| `crossword` (kh-views.js / kh-lite.js) | Submits `crossword` score = `solved.length` |
| `quizparty` (kh-views.js) | Submits `quizparty` score = total score |
| `artillery` (kh-views.js) | Submits `artillery` score = `max(1, hp × 100 - shots × 2)` |
---

## 4. Desktop Client Architecture (KindleHubClient/)

### 4.1 Project Structure

```
KindleHubClient/
├── KindleHub.Core/           ← VB.NET  (.NET 8) — business logic, API client, crypto
│   ├── Api/KindleHubApiClient.vb   ← REST calls (one method per endpoint), incl. mail
│   ├── Crypto/                     ← ChatMedia.vb (KH_ wire protocols), ChatEncryption.vb, RelayRooms.vb (ChatPrefsStore), Crypto.vb (AES-GCM, KeyDerivation)
│   ├── Models/                     ← DomainModels.vb, AccountState.vb, ApiModels.vb, RoomCodes.vb
│   ├── State/                      ← RoomRegistry.vb (shared in-memory room), SessionStore.vb (on-disk session)
│   ├── Exceptions/                 ← KindleHubExceptions.vb
│   └── KindleHubCore.vb            ← Orchestrator: auth, chat, mail, store, leaderboard, session
├── KindleHub.Client/          ← C#  (Avalonia 11.0.10) — UI layer
│   ├── ViewModels/            ← MainViewModel, SettingsViewModel, AppStoreViewModel, MessagesViewModel, GamesViewModel, LeaderboardViewModel, MailViewModel, ArcadeViewModel
│   ├── Views/                 ← .axaml (XAML) + .axaml.cs (code-behind)
│   ├── Games/                 ← the ported arcade: IGame, GameBase, GameCell, GameRegistry, GameCatalog, and one class per game
│   ├── Converters/            ← DataImageToBitmapConverter (KHIMG1 → Bitmap), FlipbookPreviewConverter
│   └── App.axaml.cs           ← DI host setup, startup navigation
└── KindleHub.Client.Gametest/  ← C# headless self-test for the arcade + mail crypto.
                                 No window needed; exits non-zero on failure. Run:
                                 dotnet run --project KindleHub.Client.Gametest -c Release
```

Core has no namespace blocks (the project compiles everything into the root
namespace), so files can be moved between folders without touching references.

**Why the split:** Core is VB.NET because the official client is also VB-flavored in places and the crypto/auth logic needed to mirror `kh-app.js` exactly. The UI is C# because Avalonia's tooling and MVVM patterns are more ergonomic in C#.

### 4.2 Authentication & Session

| Component | Detail |
|-----------|--------|
| **Username canonical** | `KeyDerivation.CanonicalizeUsername()` — lowercase, strip `._-` |
| **Auth token** | SHA-256(`"kh::" + canonical(username) + "::" + password`) — 64-hex string |
| **User ID** | First 16 chars of auth token |
| **Encryption key** | Same as auth token (AES-GCM for account state) |
| **X-KH-Secret** | Auth token sent as header on writes (inserts, deletes, edits) |

**Session persistence** (`SessionStore.vb`):
- On successful login: writes `~/.config/KindleHubPro/session.json` with `{authToken, email, displayName, userId}`.
- On startup (`MainViewModel.InitializeAsync`): calls `KindleHubCore.RestoreSessionAsync()` which re-verifies the token against `kh_users` and rehydrates `_currentProfile` + account state.
- On logout or failed validation: session file is deleted.
- **Password is never stored** — only the one-way SHA-256 digest. If the token is revoked/expired, restore fails cleanly and the user re-enters credentials.

### 4.3 Account State

- Encrypted client-side via AES-GCM before upload.
- Stored in `kh_users.state` (Supabase JSON column).
- `AccountState.vb` module: pure JSON merge helpers (`WithText`, `WithFlag`, `WithNumber`, `AppendNote`) matching the official web client's state keys:
  - `profileName`, `fontSize`, `theme`, `simpleMode`, `syncEnabled`, `notes`
- Sync every ~30s in the web client; the desktop client syncs on preference change + explicit "Sync now".

### 4.4 Chat Encryption

| Layer | Detail |
|-------|--------|
| **Room key derivation** | `RoomRegistry.GetOrCreateRoomKey()` — AES-GCM key per room |
| **Message encryption** | `ChatEncryption.vb` — `enc1:<iv_b64>.<ciphertext_b64>.<tag_b64>` (AES-GCM) |
| **Gzip variant** | `enc2:` prefix for larger payloads (same structure, gzip-compressed before encrypt) |
| **Group codes** | `800000` + 6-digit user hash for inbox; `DM:` prefix for DMs; `mp-{game}-{id}` for multiplayer |

### 4.5 KH_ Wire Protocols (Chat Media)

All markers are sent as the `text` field of `kh_messages`. The server treats them as opaque strings; the client parses them for display.

| Prefix | Type | Wire format | Parse notes |
|--------|------|-------------|-------------|
| `KHIMG1:` | Image | `KHIMG1:` + `data:image/...;base64,...` | Regex-validated; max 110 KB base64 |
| `KHAPP1:` | App share | `KHAPP1:` + b64(JSON `{shareId,label,color,icon,html}`) | html max 524288 chars |
| `KHDRAW1:` | Drawing | `KHDRAW1:` + b64(JSON `{n,w,h,layers[],author}`) | layers is non-empty array of `{type,data}` |
| `KHFLIP1:` | Flipbook | `KHFLIP1:` + b64(JSON `{n,w,h,fps,f[]}`) | w,h ≤64; frames ≤60; fps 1..15 |
| `KHPOLL1:` | Poll | `KHPOLL1:` + b64(JSON `{pid,q,opts[],o}`) | opts 2..6 items, each ≤60 chars; q ≤140; o=0/1 |
| `KHVOTE1:` | Vote | `KHVOTE1:<pid>:<idx>` or `KHVOTE1:<pid>:t:<b64(text)>` | idx = 0-based option; other-vote note max 40 chars |
| `KHSTK1:` | Sticker | `KHSTK1:` + stickerId (plain, ≤24 chars) | Catalog: like, love, smile, haha, wow, sad, fire, yes, no, star, party, hundred, think, wink, cool, sleep, angry, cry |
| `KHSTORY1:` | Story | `KHSTORY1:` + b64(JSON `{s,h,l:[{r,t}]}`) | l max 120 entries; r ≤4 chars, t ≤1800 chars; s ≤60, h ≤40; total ≤11000*2 |

**Unified helpers** (all in `ChatMedia.vb`):
- `IsEncodedMsg(text)` — starts with any known prefix
- `DescribeForChat(text)` — human label for UI list
- `DescribeForReport(text)` — collapsed tag for reports

### 4.6 App Store

| Endpoint | Method | Notes |
|----------|--------|-------|
| `GET /rest/v1/kh_store_apps?select=...&order=downloads.desc&limit=N` | Public | Catalogue listing; `downloads` + `review` = `approved` row visible |
| `GET /rest/v1/kh_store_apps?select=id,name,cat,author,html&id=eq.{id}&limit=1` | Public | Download single app's HTML for local play |
| `POST /rest/v1/kh_store_apps` | `X-KH-Secret` | Publish new app; payload: `id, name, html, cat, author, created_at, downloads, owner_secret` |
| `POST /rest/v1/kh_store_download` | Best-effort | Increment download counter |
| `DELETE /rest/v1/kh_store_apps?id=eq.{id}` | `X-KH-Secret` | Unpublish own app |

**Pending apps**: The server's RLS hides rows where `review != 'approved'` from public SELECTs. To let authors run their own pending apps, the client passes the app's `owner_secret` as `X-KH-Secret` on the download request — the gateway serves the row when `approved OR owner_secret matches`.

**Own-app tracking** (`ChatPrefsStore` in `RelayRooms.vb`):
- `RememberOwnApp(id, name, ownerSecret)` — persisted to `chat_prefs.json` under `myApps`
- `OwnApps()` — returns `List(Of OwnAppRecord)` with id, name, and secret
- `ForgetOwnApp(id)` — removes on unpublish

### 4.7 Key Design Decisions

1. **Core is VB.NET, UI is C#** — the crypto/auth logic needed to mirror `kh-app.js` exactly (VB's `If()` null-coalescing and `Implements` interface patterns map cleanly to the JS source). Avalonia UI is more ergonomic in C#.

2. **`ChatPrefsStore` lives in `RelayRooms.vb`** — it's a singleton (`ChatPrefsStore.Current`) initialized with a file path; owns `OwnApps`, `StarredMessages`, `ReportedMessages`, `DmRoomMap`. Persisted as encrypted JSON.

3. **`KindleHubApiClient` is a pure REST wrapper** — one `Async Function` per endpoint, no caching, no retry logic (caller decides). Headers are built inline: `MinimalHeaders(Nothing)` for public reads, `InsertHeaders(secret)` for writes, `UpsertHeaders(secret)` for upserts.

4. **`KindleHubCore` is the single orchestrator** — exposes high-level methods (`LoginAsync`, `SendMessageAsync`, `PublishAppAsync`, `SubmitScoreAsync`, etc.) that the ViewModels call. Holds `_currentProfile` and `_currentAccountState`. Raises `ProfileChanged`, `MessageReceived`, `LeaderboardUpdated` events.

5. **`MessageSecretStore` (singleton)** — manages per-room AES-GCM keys; initialized once in `KindleHubCoreFactory` with a path to `message_secrets.db`. Not exposed to the UI layer.

6. **`KeyDerivation` is a static VB module** — `DeriveAuthToken(username, password)` = SHA-256 of the canonical form; `DeriveUserId(token)` = first 16 chars; `CanonicalizeUsername()` = lowercase + strip `._-`.

7. **No password storage anywhere** — the session file stores only the auth token. The `EncKey` on `UserProfile` is the same as the auth token (the web client uses it as the AES key for account state).

8. **`OwnAppItem` carries `OwnerSecret`** — so the client can re-download a pending app (the server's RLS serves it when `X-KH-Secret` matches `owner_secret`). Without the secret, a pending app returns 404.

9. **Build scripts** — `build.sh` (Linux/macOS) and `build.ps1` (Windows) both call `dotnet publish` with `-c Release -r <rid> --self-contained true`. Artifacts land in `artifacts/Release/<rid>/`.

10. **No service-worker / offline web tricks** — this is a native desktop client, not a PWA. All persistence is file-based (`chat_prefs.json`, `session.json`, `message_secrets.db`, `~/.config/KindleHubPro/apps/*.html`).
