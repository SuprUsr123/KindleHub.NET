# KindleHub Pro — Architecture

This document is the result of a full analysis of `kindlehub.pro.zip` and replaces the
previous API-reference-only draft. It documents the actual code in the archive:
what the repository is, how the runtime is put together, the wire protocols the
clients speak, and where the earlier reference was wrong. Section 5 lists every
correction against the previous draft with evidence.

Analysis snapshot: **v10.70.1, build mark `9ce6d7c329e9`, fetched 2026-11-15** (per
`.kh-update-state.json` and `update.log`). All line references like `kh-app.js@1252687`
are byte offsets, because the shipped bundles are single-line files.

## 1. What this repository is

`kindlehub.pro` is a **deobfuscated working mirror of the live https://kindlehub.pro
website** — a web app for Kindle e-ink browsers ("apps, 120+ games, AI & tools for your
e-reader"). The production site is a **Cloudflare Pages** project serving a static HTML
shell plus three large JS bundles; all dynamic data flows through **first-party Cloudflare
Workers** in front of a Supabase-compatible (PostgREST) datastore. Nothing in the archive
is a server: it is the client source, the sub-sites, and the tooling used to keep the
mirror in sync with production.

| Path | Size | Role |
|---|---|---|
| `index.html` | 190,814 B | App shell: CSP, stall logger, subdomain guards, boot splash, e-ink CSS, `<script src="kh-app.js">` |
| `kh-app.js` | 2,631,463 B | Core runtime: state, auth, crypto, REST layer, sync, presence, routing, multiplayer core (`KH_MP`), score pipeline |
| `kh-views.js` | 2,539,713 B | View layer: 105 `view-*` sections (features from notes to weather to app store) |
| `kh-games.js` | 2,146,180 B | Games layer: 127 launchable game objects driven by `launchGame` |
| `lite.html` | 190,814 B | Byte-identical copy of `index.html` so `/lite` has a target |
| `kh-lite.js` + `kh-lite-views.js` | 1,906,717 + 916,931 B | Standalone lite client (`window.KH_LITE=1`, 16 views, stale at v10.15.0) |
| `landing.html` | 17,915 B | Marketing page (author Aran Keyhan-Rad, pricing £0.99/mo, links `/security/`, `/legal.html`, `/guides/`) |
| `edu/` | — | **KindleHub Edu**: offline study app (`plan.js` homework planner, `questions.js` seeded question banks); only network call is the AI tutor proxy |
| `read/` | — | **KindleHub Read**: standalone Project Gutenberg reader + reading tracker (localStorage account, `/t/` tunnel routes) |
| `mod/` | — | **KindleHub Mods**: download shelf for jailbroken Kindles (Vera/WinterBreak/LanguageBreak, KUAL, MRPI, KOReader); serves from `files.json` or a worker `/mod/list` — *not* a moderator console |
| `status/` | — | **KindleHub Status**: checks `API /stats`, `/t/api/`, shared AI proxy, gutendex, gutenberg, `tube.kindlehub.pro/health` (Raspberry Pi video preparer) |
| `serve.py` | 4,979 B | Local mirror server emulating the site's `_redirects` (deep links → shell, `/lite`→lite.html, `/edu/`→edu/, JS MIME, no-cache HTML) |
| `update.py` | 39,355 B | Site-fetch/update tool: pulls the live site, detects layout, deobfuscates, rewrites the mirror (see §7) |
| `.kh-tools/` | — | Babel-based identifier renamer (`rename.js`) + node_modules (currently disabled by `update.py`) |
| `robots.txt` | 750 B | Allow-all + unusual "network interference" policy comment |
| `icon.svg`, `kindlehub.pro.code-workspace` | — | Brand mark; workspace also referencing the `../../KindleHub.NET` desktop repo |

**Git state of the archive:** two commits on `main` by "KindleHub Dev"
(`95c49b1` 2026-09-25 initial commit, `665be54` 2026-09-26 "Document desktop client
architecture"). The working tree is **dirty and ahead of HEAD**: the Nov 15 mirror update
(~20 modified files) is uncommitted, and the tracked `ARCHITECTURE.md` (368 lines,
4 sections) and `KINDLEHUB_DESKTOP_HANDOFF.md` (156 lines) are **deleted from the working
tree** but recoverable from git. `.kh-tools/node_modules` is committed wholesale, and a
prunable detached worktree (`stormy-basement`) points at the original author's machine
(`/home/suprusr124/...`). The in-repo ARCHITECTURE.md and handoff doc are still the best
statement of the .NET desktop client, which is **not** part of this archive.

## 2. System architecture

### 2.1 Request flow

```text
Kindle/Silk & desktop browsers
        │  https://kindlehub.pro (Cloudflare Pages, _redirects catch-all)
        ▼
index.html shell ──► kh-app.js ──► kh-views.js / kh-games.js (lazy)
        │
        ├── kindlehub-api.arancool3000.workers.dev   ← the API gateway
        │     ├── /rest/v1/{table}                   (PostgREST-shaped reads/writes)
        │     ├── /rest/v1/rpc/{name}                (~45 server RPCs)
        │     ├── /functions/v1/kh-gemini-proxy      (shared-AI proxy; also used by edu/)
        │     └── /stats                             (status page)
        ├── email-worker.arancool3000.workers.dev    (recovery-code mail send)
        ├── kindlehub-state.arancool3000.workers.dev (photo/gallery store, /state legacy gateway)
        ├── kindlehub-helpers.arancool3000.workers.dev, vpn.arancool3000.workers.dev (fetch tunnel)
        ├── ai.kindlehub.pro / ai2.kindlehub.pro     (local AI; KH_LOCAL_AI)
        ├── read./tube./game./game3./browse./browse2./kindletube./static./static2. kindlehub.pro
        └── third-party providers (AI, books, weather, news, media — §6)
```

All Supabase access goes through the `kindlehub-api` worker — there is no direct
`supabase.co` URL in any bundle. The worker enforces row filters, secret checks and rate
limits that a raw PostgREST URL would not.

### 2.2 Shell and boot sequence (`index.html`)

1. **Stall logger** (line 6, inline): opt-in (`kh_stall_log=1`, or auto-on for user
   `aran/arancool3000` on Kindle/Silk/Android). Monkey-patches `setTimeout`/`setInterval`/
   `requestAnimationFrame` (logs ≥30 ms callbacks into a 14-entry ring buffer), samples a
   250 ms ticker for >500 ms main-thread gaps, wraps `fetch`; flushes every 60 s and on
   `pagehide` as `kh_errors` rows `{id:"stall_"+Date.now(), text:"[STALL] …" ≤3800 chars, date}`.
2. **Subdomain guards** (lines 8–18): one-liners redirecting `edu.` → `/edu/…`,
   `mod.` → `/mod/`, `read.` → `/read/`, `status.` → `/status/`, `vexiq.` → `/vexiq/`;
   `lite.` → strips the prefix (lite is a bundle switch, not a different shell).
3. **Landing guard**: a bare `/` visit without `kh_seen_landing` (set by `?app=1`) bounces
   to `/landing`; `ksim=1` spoofs a `…Kindle/3.0` UA for simulator testing.
4. **CSP**: strict `script-src 'self' 'unsafe-eval' cdn.jsdelivr.net` with 14 per-inline
   `sha256-…` hashes; a violation handler shows one of six fallback dialogs.
5. **Boot splash** (line 115): e-ink/Kindle detection, logo splash, `__khBootDone/Fail`;
   on bundle script error re-appends `<script src="kh-app.js?khretry=…">`, clears caches
   and **unregisters service workers** (the only `serviceWorker` references in the shell
   are cleanup calls).
6. `<script src="kh-app.js" data-kh-bundle="1">` — plain name, **no `?v=` query** in this
   snapshot ("plain" layout; the Oct 5 run used hashed `?v=` names — see §7).
7. Cloudflare Insights beacon; Kindle on-screen keyboard module (`window._khKeyboard`).

`lite.html` is **byte-identical** to `index.html`; the lite bundle is swapped in for
`lite.kindlehub.pro` server-side, with `kh-lite.js` setting `window.KH_LITE=1` and
tolerated by kh-app.js (`/kh-(app|lite)\.js/` "already declared" suppression).

### 2.3 Deep-link routing

The site serves the same shell for every deep link (`/chess`, `/notes`, `/uno`, …); the
client opens the view from the path. In kh-app.js: `_khDeepSlug` @1771665 takes the first
pathname segment, `_khApplyDeepLink()` @1771900 maps it through `GAME_HELP` (→
`launchGame`) or `BUILDERS[slug]` (→ `showView`), with a `popstate` listener for
back/forward. `showView` @1775994 also handles `kapp:{id}` published apps, split views and
`pushState`. `serve.py` replicates the `_redirects` behavior for offline mirrors.

## 3. Front-end architecture

### 3.1 kh-app.js — core runtime

The file is one minified line; references below are byte offsets. It exposes a large
`window._kh*` API surface that kh-views.js/kh-games.js consume guarded by
`"function"==typeof X` (so views can load before the core finishes).

**Global state.** `SK="kindlehub_v5"` @7380; `defaultState()` @7390 builds the account
state; the live object `S` @11651 is initialized from
`_khDecodeStateBlob(localStorage.getItem(SK))`. The local blob supports prefixes
`KHZ2:` (inflate + dictionary decompress), `KHZ1:` (inflate), `KHD1:` (dictionary) and
plain JSON. State has ~140 keys — identity (`userId`, `authToken`, `email`), preferences
(`theme`, `fontSize`, `uiMode`, `wallpaper`), AI keys (`geminiKey`, `openrouterKey`,
`anthropicKey`, `openaiKey`, `aiProvider`, models), social (`friends`, `friendRequests`,
`friendReqTombs`, `chatRequests`, `msgGroups`, `leftGroups`, `blockedUsers`),
content (`notes`, `books`, `flashDecks`, `rssFeeds`, `recipes`, `flipbooks`, `kanban`,
`checklists`), games (`games`, `gamesAt`, `stats`), profile (`profileName`, `profileAvatar`,
`profileFrame`, `profileBio`, `profileHobbies`, `profilePronouns`), photos
(`photoKey`, `photoKey2`, `photoKeys`, `photoStores`) and security (`superlock`, `acctV2`,
`modCode`, `knownDevices`, `recoveryEmail`, `ageRange`). `profileName` and `simpleMode`
are optional/runtime keys, not defaults.

**Persistence and sync.** `save(e=1200)` debounces to `_persistState` @222300; hot fields
(`games`/`stats`/`gamesAt`) split into `kh_hot`; `visibilitychange`/`pagehide` flush held
writes. Cloud sync is **not** a fixed 30 s loop: `scheduleCloudSync` @23802 debounces by
load with 45 s retry backoff, `_startPeriodicSync` @206867 runs every **90 s**
(skipping hidden/busy/idle tabs), plus three post-load syncs 5 s apart. `_trimStateForCloud`
@1270810 strips device-only keys before upload. `_khKeepAlive` pings
`kh_messages?select=id&limit=1` every 6 h.

**Authentication.** `_userKey(user, pass)` @1252687 =
`SHA-256("kh::" + canonical.toLowerCase().trim() + "::" + password)` — the 64-hex token is
both the account key and the AES key; `userId = token.slice(0,16)`.
`_khCanonicalUsername` @1445273 maps `"aran"`→`"arancool3000"`. `authRegister` @1445506
enforces charset `/^[a-z0-9._-]{1,40}$/`, profanity (`_badUsername`), reserved names
(`aran`, `afriend`, `amazonenforcer` + staff words), weak-password and **HIBP leak checks**
(api.pwnedpasswords.com), device ban/mute gates and `kh_banned_usernames` lookups.
`authLogin` @1459650 has an **offline fast path**: `_offlineCred` (max 2 LRU accounts in
`kh_offline_cred`) decrypts locally before touching the network. `_saveCreds` now only
deletes the legacy plaintext `kh_creds`.

**Crypto.** Account key = raw hex-decoded token bytes (`_deriveAesKey` @1252745, AES-GCM
256, **no PBKDF2**). `_encryptState` @1266551: 12-byte IV; plaintext = one flag byte +
body, where flag `'G'` (0x47) = `gzip(dictionaryCompressed(JSON))` when `CompressionStream`
exists, `'c'` (0x63) = raw dictionary-compressed JSON on low-memory devices, legacy `'g'`
(0x67) = gzip without dictionary (decrypt-only); envelope
`b64(iv) + "." + b64(ciphertext||16-byte tag)`. Dictionary `_COMP_DICT` @9996 (~100 JSON
fragments) encoded as `0x01` + index char. Messages: `_msgKey` @808960 =
`SHA-256("khmsg::" + groupCode)`; `_msgEncrypt` @809244 emits `enc1:`/`enc2:` (gzip only
when smaller) with the same envelope; decrypt failures render a placeholder, never crash.
Mail uses the context string `"mail:" + recipient` (key = `SHA-256("khmsg::mail:" + normalized recipient)`) — both sides derive it from the recipient name.
Separately PBKDF2-protected (key-stretching, not the account key): `KHS1:` note lock
(210k iters), `KHSP1:` superchat passphrase (210k), `KHSL1:` superlock wrap (600k),
`KHA2:` account-v2 wrap incl. recovery "rec" boxed variant (120k). Photo-gallery address
= `SHA-256("khphoto::acct::" + authToken)` @323800.

**Supabase REST layer.** `_sbBase()` @776053 → `KH_DEFAULT_API_GATEWAY =
"https://kindlehub-api.arancool3000.workers.dev"` @760022. `_sbFetch` @797700 adds GET
result caching (80 entries, per-table TTL, secret in cache key), timeouts (8 s GET /
20 s writes / 60 s account upsert), retries on kh_users upserts, and a
**`vpn.arancool3000.workers.dev` fetch-tunnel fallback** on failure. Helpers: `_sbSelect`
@805366, `_sbUpsert` @806742 (`?on_conflict=` + `Prefer: resolution=merge-duplicates,
return=representation`; kh_users refuses empty `state`; supports `X-KH-Rekey-From/-Name`),
`_sbInsert` @807345 (`X-KH-Secret: S.authToken`), `_sbUpdate`, `_sbDelete`.
`_khOwnerSec` @776300 auto-attaches the account token whenever an `X-KH-Admin` header is
present.

**Tables touched:** `kh_users`, `kh_presence`, `kh_scores`, `kh_groups`, `kh_messages`,
`kh_mail`, `kh_store_apps`, `kh_feedback`, plus **`kh_errors`** (client error telemetry,
queued in `kh_err_queue`, flushed 4 s after boot and on `online`) and
**`kh_banned_usernames`** (login/register ban lookups with canned ban reasons).

**Server RPCs called (superset of the previous reference's list):**
`kh_set_reaction`, `kh_app_get`, `kh_app_put`, `kh_app_list`, `kh_app_enhance(_status)`,
`kh_recovery_set`, `kh_inbox_send`, `kh_run_start`, `kh_store_download`, `kh_store_pending`,
`kh_store_rate`, `kh_store_review`, `kh_mod_stats`, `kh_mod_claim`, `kh_mod_badges`,
`kh_mod_ranks`, `kh_mod_create`, `kh_mod_list`, `kh_smod_reports`, `kh_age_verify`,
`kh_age_mine`, `kh_age_self_set`, `kh_age_marks`, `kh_birthday_claim`, `kh_member_count`,
`kh_mod_age`, `kh_poll_delete/list/save/stats/vote/voters`, `kh_meme_list/use`,
`kh_gift_claim`, `kh_redeem_gift`, `kh_helper_link/status/unlink`, `kh_add_comment`,
`kh_follower_counts`, `kh_id_report`, `kh_id_watched`, `kh_plan_counts`, `kh_score_boost_list`,
`kh_pid_lookup`, `kh_public_id`, `kh_head_withdraw_warning`.

**Realtime vs polling.** kh-app.js core has no websockets. The chat view in kh-views.js
@2325xxx **does** subscribe to Supabase realtime (`topic:"realtime:public:kh_messages:
group_code=eq.<code>"`, phoenix `phx_join`, 25 s heartbeat) with an **adaptive polling
fallback** (`_khChatWant`, `_KH_CHAT_TICK`/`_KH_CHAT_FULL_MS`, `_groupLatestId`
short-circuit: quiet rooms poll slower, active rooms speed up). Everything else polls:
notification sweep `ts=gt.<now-120s>` batched 100 group codes per query @1589600,
`KH_MP` relay every 3 s, inbox/mail probes on intervals that skip hidden tabs.

**Presence.** `_pingPresence` @1243100 upserts `kh_presence {user_id, display_name,
last_seen, avatar: S.profileAvatar, profile}`; `_startPresence` @1249100 beats every
**100 s** (180 s in simple mode) plus on `visibilitychange`; the online window is ~7 min.
Identity falls back to `anon-<deviceId>`. Profile blob `_khMyProfileBlob` @1244649 =
`{p pronouns≤28, h hobbies≤160, b bio≤320, s status≤80, tz, f friends≤24+sf, j joined,
nameStyle…}`. Discovery reads `last_seen=gte.<-14d>&limit=400`.

**Avatars.** 22×22 grid (`AV_N=22`), 12 background colors (index 0..11), 16-ink palette.
`KHAV1:{bg}.{hex}` packs four 1-bit pixels per hex digit LSB-first (`_khFlipPack`
@126047); `KHAV2:{bg}.{b64}` packs two 4-bit ink indices per byte high-nibble-first
(`_khAv2Pack` @168646, used when any cell's ink index >1); `_khAvatarCode` @168900 picks
the version. Parse/paint via `_khAvatarParse/_khAvatarPaint`.

**Service worker.** `_khRegisterSW` @1940171 registers `sw.js?v=<version>` (https/
localhost only) and posts a `{kh:"precache", urls:[kh-app.js, kh-games.js, kh-views.js,
…]}` message; `_khPrecacheGames` falls back to `fetch(..., {cache:"force-cache"})`. The
`sw.js` file itself is served by Pages and not part of this mirror. No PWA manifest.

### 3.2 kh-views.js — view layer

A registry of 105 builder thunks, each returning `el("section",{id:"view-<name>",
className:"view"})`; views self-register teardown via `section._khStop = fn`. One named
export: `_khOpenDashboardBody` @0 (dashboard overlay `#kh-dashboard`, widget grid:
agenda, goal, habits, countdown, reading, moon, timer, remote, note, quote, news, stocks,
onthisday). Overlay openers (`_khOpenProfile`, `_khOpenAvatarEditor`, `_khOpenDrawPad`,
`_khOpenMemePicker`, `_khOpenModChat`, …) are defined in the core and referenced here.

Grouped inventory of the 105 views:

| Group | Views |
|---|---|
| Productivity/utilities | spelling, calc, tools, sheets, cipher, translate, notes, kanban, formulas, unitconv, tipcalc, passgen, countdown, tally, cite, decide, budget, qrgen, files, worldclock, habits, chores, wheel, timetable, homework, calendar, mdjournal, foodlog, sleeplog, readspeed, lifeweeks, periodic, science, gazette, dictionary, phrasebook, verses, firstaid, cookbook, recipes, stash |
| Study | study, timestables, fractions, bookbingo, events |
| Reading/media | reading, rss, gallery, imagesearch, lyrics, watchq, comics, shorts, kindletube, spotify, music (LibriVox + Internet Archive), browser |
| Communication | chat (AI chatbot), messages, topics, mail, community, feed, bluesky, fandom, wikipedia, contacts, feedback |
| Creation | asciiart, draw, pixelart, flipbook, slides, code, screenshot, memes, polls |
| Games hosted here | lifegame, quizparty (realtime lobby, no server scores), pokigames, kesschess, adventure, artillery, pet (achievement ids), truthdare, animals, bugid |
| Life/info | maps, weather, starmap, sun, moon, breathe, sports, stocks, moneytools, countries, histday, lifehacks, discover, quotes |
| System | settings, appstore |

**Messaging details.** Tabs Chats/Friends/Saved; fixed rooms are codes starting
`800000`/`900000`; 12-digit codes display as `1234-5678-9012`; per-room transcript cache
`kh_mc_<code>`; starred messages in `kh_starred_meta`. Outbound
`{id:_khMsgId(), userId, displayName, text, ts, replyTo, _pending}` through `_groupSend`
@2314850. Per-message actions: Reply, React, Copy, Translate, Star, To-notes, Important,
Pin, Edit; **unsend** deletes via `_sbDelete("kh_messages","id=eq.<id>", _getMsgSecret(id))`
@1851501 and fails with "Owner secret missing (sent from another device?)" when the
per-message secret isn't local (fallback stored in `S.localEdits`). **Reply previews
resolve parents from the in-memory message map**, not a `?id=in.(...)` batch (the desktop
client does the batch read — client-side divergence to keep in mind when porting).
Reaction values are arrays of 16-char user ids. AI moderation hooks (`_khAiModWatch`,
`_khAiModSweep`, MOD_ badges, per-room mutes) wrap the transcript.

**Friends.** `S.friends` rows `{hash, name}`; requests in `S.friendRequests` with
`S.friendReqTombs` tombstones; accept/decline via `_khAcceptFriendRequest`/
`_khDeclineFriendRequest`; DMs are real groups named `"DM: <me> & <them>"` with
`KH_MP.sendDmInvite(userId, code, name)`; group creation invites via
`kh_users?email=ilike.<name>` lookup then the same DM invite. The
`FRIEND_REQUEST`/`FRIEND_ACCEPT`/`DM_INVITE` JSON event envelopes live in the core,
not the view.

**App Store view.** Catalogue via `_khStoreList` + `_khOtherStorePages` (downloads-desc
or new-first), category chips, sandboxed preview iframe (`_khAppSandbox`,
`_khWrapAppSecure`), install/remove for built-ins and community apps, publish via
`_khPublishApp` @1790xxx — with **AI review** (statuses approved / waitlisted /
declined+reason), edit flow carrying `editId`+`editSecret`, private saves
(`S.publishedApps`, capped 60), and the **512 KiB (524,288 chars) HTML limit** enforced
at save and import. `KHAPP1:` share codes confirmed:
`"KHAPP1:"+btoa(unescape(encodeURIComponent(JSON.stringify({label,color,icon,html}))))`
@1800013, with the inverse on import.

### 3.3 kh-games.js — games layer

Pattern: one global object per game launched by `_doLaunch` (kh-app.js @1946442), a
single `switch` over **127 slugs**. `launchGame` @1941337 sets `window._currentGame`,
exits the previous immersive session, records `S.lastPlay`, waits for the lazy loader,
then dispatches. Games render into a shared immersive overlay (`enterImmersive`:
`#immersiveRoot` + title/score/help/scores strips; `immersiveContent` referenced 928×).
11 heavy games (Galaga, Solitaire, SpaceInvaders, Battleship, TowerDef, …) are
lazy-compiled IIFEs on first launch; three are OSS iframe shells (hextris, clumsybird,
micropolis) and three run Doom WASM (`doom`, `freedoom1/2`).

The shared loop `_khGameLoop({step, tick, draw, live, interp})` uses a fixed timestep
(min 8 ms, max 4 catch-up steps) with rAF interpolation when smooth scrolling is allowed,
else `setInterval` with an **e-ink draw floor of 120 ms** (`KH_EINK_DRAW_MS`) on
`isKindleBrowser()`; it suspends on `document.hidden`. `_khGameMenu` (58 call sites)
builds the daily-challenge/mode menu; `newGameGuard` confirms before abandoning a run.

Genre inventory (127 slugs):

| Genre | Slugs |
|---|---|
| Arcade (~38) | doom, freedoom1/2, hextris*, clumsybird*, micropolis*, bucketline, kitchen, kickdummy, skyscuffle, raildash, citycourier, clawmachine, barrage, lander, lastlight, snake, slither, geometrydash, flappy, platformer, pacman, crossyroad, arena, tetris, spaceinv, galaga, fruitninja, eightball (pool), digquest, stronghold, cargames (hub) |
| Casual (~13) | deepvein, cookieclicker, burrow, isle, manyhands, sketchguess, quickcount, nextinline, farm, akinator (MindReader), perfectcircle, roller, craft (InfiniteCraft) |
| Travel/car (10) | keepgoing, samepage, platewords, spotbingo, storychain, categories, rhymetime, carcricket, wouldyourather, alphabethunt |
| Strategy (~16) | silkroad, bannerlands, lastladder, ninenights, lineman, sleepercrew, nim, dquest, lanternvale, deephalls, emberdeck, wildforms, corridor, tiletrader, flightsim, towerdef |
| Puzzle (~24) | numberlink, kakuro, futoshiki, pegs, traffic, mahjong, numslide, sumlines, bridges, cargorun, beambalance, loopwire, sudoku, lightsout, mastermind, nonogram, minesweeper, picpuzzle, maze, g2048, candycrush, blockblast, hanoi, sokoban, simon |
| Word (13) | cryptogram, wordladder, pairup, oddone, hangman, wordle, wordsearch, anagrams, nerdle, connections, spellingbee, strands, trivia |
| Board (12) | gomoku, dominoes, chess, ttt, connect4, battleship, snakesladders, checkers, dotsboxes, yahtzee, reversi, set |
| Card (6) | freecell, solitaire, memory, blackjack, crazy8, unoflip |

\* OSS iframe shells. 72 slugs call `_khSubmitScore` (kh-views.js defines **no** games;
kh-games.js has **zero** direct `kh_groups`/`kh_messages` references — multiplayer goes
through the core's `KH_MP`).

### 3.4 Multiplayer core (`KH_MP`, kh-app.js @1955401)

- **Rooms.** `createRoom(name)`: short = `_khRoomDigits(6)`; **room code = `"900000" +
  short`** (12 digits); group **name** = `mp-{game}-{short}`; persisted via
  `_groupCreate` → INSERT `kh_groups {code,name,creator,created_at,updated_at}`
  @1562273. `joinRoom(short)` looks the name up or creates it, returning
  `{code, short, role:"host"|"guest"}`. Note: `mp-…` is the room *name* family; the
  *code* family is `900000xxxxxx`.
- **Transport.** `KH_MP.send` wraps event JSON as an ordinary message
  `{id:uuid, userId, displayName, text:JSON.stringify(event), ts}` → `_groupSend`
  @1570243 → `_msgEncrypt` with a per-message `owner_secret` → INSERT `kh_messages`.
  `subscribe(code, handler)` does an initial fetch then **polls every 3 s**
  (`_groupLatestId` short-circuit, hidden-tab skip, dedupe cap 400), dispatching
  `{type, payload, from:{userId,name}, id, ts}`. Fallback to local `_masterWrite` when
  Supabase is down.
- **Events.** Long form: `JOIN`, `BYE`, `CHAT`, `ACT` (crazy8 play), `STATE` (crazy8 hand
  sync), `TTT_STATE`/`MOVE_TTT`, `MOVE_C4`/`C4_STATE`, `DB_INIT`/`DB_MOVE`, `MOVE`
  (chess `{fr,fc,tr,tc,flag,promo}`); short form `t:`: `ST` (state sync
  `{b,tn,lm,ov,…}`), `SH`/`RS` (battleship shot/result), `MV`/`AC` (arena), `CL`
  (checkers click). Party games use `SG_START/SG_PIC/SG_GUESS/SG_TURN/SG_SKIP/SG_RESULT/
  SG_BYE` (Sketch & Guess). There is **no `OPEN` event** in the shipped code.
- **Invites/lobby.** `sendInvite` posts `{type:"GAME_INVITE", game, roomShort, fromName,
  fromUserId}` via the `kh_inbox_send` RPC (or the recipient's inbox group). Acceptors
  registered as `window._khGameAcceptors.{ttt, connect4, dotsboxes, reversi, battleship,
  crazy8, artillery}` plus `chess`. Open lobby room: **`800000777777`** named
  `open-games-lobby` @1959900. `openParty` runs 2–4 player co-op (Barrage, Last Ladder
  share a seeded run; Sketch & Guess is 4-player).
- **Modes.** Single-device pass-and-play (`local2` in battleship/reversi/gomoku/nim/ttt/
  connect4/dominoes), online 2-player (chess rated with a fair-play review RPC, reversi,
  ttt, connect4, dotsboxes, gomoku, battleship, crazy8, eightball, set), co-op (above).
- **Authority.** Turn validation is per game and host-replays state on `JOIN`
  ("Waiting for the host to set up the board…"); transport delivery is never game
  authority. Online results are not submitted to the leaderboard ("Two-player games stay
  out of the Ladder, the Daily and the leaderboard").

## 4. Data and service contract (verified against the shipped client)

The gateway is authoritative: it applies filter and secret rules beyond PostgREST syntax.
A successful request to a Supabase-shaped URL does not imply unrestricted access. All
timestamps are UTC ISO-8601; JSON is UTF-8; table/field names are snake_case.

### 4.1 Conventions

| Property | Value |
|---|---|
| Gateway | `https://kindlehub-api.arancool3000.workers.dev` (`KH_DEFAULT_API_GATEWAY`) |
| Data API | `/rest/v1/{table}`; RPCs at `/rest/v1/rpc/{name}` |
| Write auth | `X-KH-Secret: <64-char lowercase account token>` |
| Upsert | `?on_conflict=<col>` + `Prefer: resolution=merge-duplicates,return=representation` |
| Inserts | `Prefer: return=representation` (or `return=minimal` for fire-and-forget RPCs) |
| Filters | PostgREST forms: `eq.`, `gt.`, `gte.`, `ilike.`, `in.(…)`; URL-encode every scalar |
| Read discipline | Explicit `select=` list, exact row/room filter, `order=`, capped `limit` (web chat reads cap at 30/poll; desktop caps at 200; discovery lists 100–400) |
| Client resilience | GET cache (80 entries, per-table TTL), 8–60 s timeouts, kh_users upsert retries, `vpn.arancool3000.workers.dev` tunnel fallback |

### 4.2 Identity and encrypted account state

```text
canonical = lowercase(trim(username)); if canonical == "aran": canonical = "arancool3000"
authToken = lowercase_hex(SHA256(UTF8("kh::" + canonical + "::" + password)))   // 64 hex
userId    = authToken[0:16]                       // public, not a secret
AES key   = raw 32 bytes hex-decoded from authToken (AES-GCM-256; no PBKDF2)
```

The password is case-sensitive and never leaves the device. The token is the write
credential (`X-KH-Secret`) and the encryption key; treat it as a secret — never in URLs,
logs or crash reports.

`kh_users.state` is an encrypted string, not a JSON object:
`base64(iv) + "." + base64(ciphertext || 16-byte GCM tag)`, random 12-byte IV. The
plaintext's first byte is a format flag: `'G'` (0x47) = gzip of dictionary-compressed
UTF-8 JSON, `'c'` (0x63) = uncompressed dictionary-compressed JSON (low-memory fallback),
legacy `'g'` (0x67) = gzip without dictionary (decrypt-only today). The dictionary
(~100 shared JSON fragments, `0x01` + index-char tokens) is embedded as `_COMP_DICT`
@kh-app.js:9996. Account-state edits must be read/decrypt → modify only intended keys →
re-encrypt with a fresh IV → upsert; unknown keys must survive (older clients must not
erase newer preferences). `_trimStateForCloud` strips device keys (`_encKey`,
`superlock*`, `acctV2`, `authToken`, wallpapers, …) and caps long arrays before upload.

PBKDF2 applies only to optional extras, never to the account key: `KHS1:` note lock
(210,000 iters), `KHSP1:` superchat passphrase (210,000), `KHSL1:` superlock wrap
(600,000), `KHA2:` account-v2/recovery wrap (120,000); envelopes are
`PREFIX:salt:iters:blob`.

| `kh_users` column | Meaning |
|---|---|
| `hash` | Account token; unique key |
| `email` | Canonical username |
| `state` | Encrypted account-state envelope |
| `updated_at` | Last successful sync |

Operations: register/sync = `POST /rest/v1/kh_users?on_conflict=hash` with
`{hash,email,state,updated_at}` (registration initial state = encrypted `{}`; sync must
reject an empty state); login/restore = `GET …?hash=eq.<token>&select=state,updated_at`;
name availability = `GET …?email=eq.<canonical>&select=email&limit=1`; friend search =
`GET …?email=ilike.*<term>*&select=hash,email&limit=20` (returns `{hash:<16-char prefix>,
name:<email prefix>}`; the full hash must never be shown). A legacy configurable state
gateway (`kh_state_gateway` localStorage, `/state` GET/PUT with `X-KH-Secret`) exists but
`KH_DEFAULT_STATE_GATEWAY` is empty — the main REST path is the only active one.

### 4.3 Room codes (corrected)

| Family | Code | Notes |
|---|---|---|
| Global chat | `000000000000` | `KH_GLOBAL_GROUP_CODE` @1551541, ensured by `_ensureGlobalGroup` |
| Crosschat | `000000000001` | `KH_BRIDGE_GROUP_CODE` ("Crosschat") @1551577 |
| Open games lobby | `800000777777` | Group named `open-games-lobby` @1959900 |
| Multiplayer rooms | `900000` + 6-digit short | 12-digit **code**; the group *name* is `mp-{game}-{short}` |
| User inbox | `800000` + 6 digits | 12-digit code; each digit = `parseInt(pidHex[i],16) % 10` of the recipient's 6-hex **public id**; validated `/^800000[0-9]{6}$/`; room auto-created as `"inbox-"+hash.slice(0,8)`; sends prefer the `kh_inbox_send` RPC |
| Direct messages | random 12-digit codes | Rooms **named** `"DM: <me> & <them>"`; `DM:` is a name prefix, not a code prefix |
| Topics | random 12-digit codes | Group names begin `TOPIC: ` (`_KH_TOPIC_PREFIX`), categories gen/book/tech/game/help/make/off |
| Age rooms | `9300000000{05..18}` | `KH_AGE_ROOMS` @1551650 |

The previous reference's "inbox = `800000` + 16-char user id (16 chars total)" is wrong —
inbox codes are 12 digits derived from the public id, and the desktop handoff's
"800000 + six digits of user ID" matches the 12-digit family.

### 4.4 Messages: `kh_messages`

| Column | Meaning |
|---|---|
| `id` | Client-generated (`_khMsgId()`); keep it to dedupe |
| `group_code` | Exact room code (required filter on every read) |
| `user_id` | Sender's 16-char id |
| `display_name` | Sender name (≤40 chars) |
| `text` | `enc1:`/`enc2:` ciphertext, or a `KH*` media marker |
| `ts` | UTC ISO timestamp |
| `edited`, `important` | Boolean flags |
| `reply_to` | Optional parent id; replies are separate rows |
| `reactions` | JSON: emoji → array of 16-char user ids |
| `owner_secret` | Random per-message secret enabling author-only mutations |
| `device_hint`, `location_hint` | Moderation hints where supplied |

Encryption: room key = `SHA-256(UTF8("khmsg::" + groupCode))`; AES-GCM-256, 12-byte IV,
16-byte tag; gzip only when it shrinks the payload (`enc2:`), else `enc1:`; envelope
`<prefix>:<b64(iv)>.<b64(ciphertext||tag)>`; no associated data. Decrypt failure →
placeholder, never a crash. The web client resolves reply previews from memory; the
desktop reads parents with `GET …?id=in.(…)` (≤40 ids).

Mutations: edit = `PATCH …?id=eq.<id>` `{"text":…,"edited":true}` with
`X-KH-Secret: <owner_secret>`; unsend = `DELETE …?id=eq.<id>` same header; important =
`PATCH … {"important":true}` same header; reaction toggle =
`POST /rest/v1/rpc/kh_set_reaction` `{p_msg_id,p_key,p_user_id}` (atomic server-side,
`return=minimal`). Deletes also accept `X-KH-Room-Owner: 1` for room-owner cleanup.
Don't send the account token where an owner-secret is required — they are different
secrets.

**Inbox events** (client protocol, not a server graph): JSON serialized, encrypted as
ordinary room text, inserted into the recipient's inbox room (or via `kh_inbox_send`).
Types: `DM_INVITE {code,name,fromName,fromUserId}`, `CHAT_REQUEST {code,name,…}`,
`FRIEND_REQUEST {fromHash,fromUserId,fromName}`, `FRIEND_ACCEPT {…}`, `GAME_INVITE
{game,roomShort,fromName,fromUserId}`. Friends/requests live in encrypted account state
(`friends`, `friendRequests`, `friendReqTombs`); processed inbox ids are tombstoned so
old messages can't replay resolved state. Dedupe by message id.

### 4.5 Chat media markers (`KH*`)

All ride in `kh_messages.text`; the server treats them as opaque strings. Detection
regex @kh-app.js:30829 covers `KHIMG1|KHAPP1|KHDRAW1|KHFLIP1|KHPOLL1|KHVOTE1|KHSTK1|
KHSTORY1|KHMEME1`.

| Prefix | Payload | Limits |
|---|---|---|
| `KHIMG1:` | `data:image/{jpeg,png,gif,webp};base64,…` | send budget 110 KB, hard cap 220 KB; optional hidden `KHCAP1` caption trailer in the base64 (≤600 B) |
| `KHAPP1:` | b64(encodeURIComponent(JSON `{shareId,label,color,icon,html}`)) | html ≤ 512 KiB |
| `KHDRAW1:` | b64 JSON `{n,w,h,layers[]}` | layers = non-empty `[{type,data}]` |
| `KHFLIP1:` | b64 JSON `{n,w,h,fps,f[]}` | ≤60 frames, ≤64×64, fps 1..15 |
| `KHPOLL1:` | b64 JSON `{pid,q,opts[],o}` | q ≤140, 2–6 options ≤60 chars |
| `KHVOTE1:` | `{pid}:{idx}` or `{pid}:t:{b64(other≤40)}` | 0-based option index |
| `KHSTK1:` | sticker id `^[fawt]_[a-z0-9_]{1,22}$` | sliced to 24 |
| `KHSTORY1:` | b64 JSON `{s,h,l:[{r,t}]}` | s ≤60, h ≤40, ≤120 entries, t ≤1800 |
| `KHMEME1:` | meme id `^[a-z0-9-]{1,40}$` | served by `kh_meme_list/use` RPCs |
| `KHPIX1:` | b64 JSON `{w,h,d,pal≤36,n≤40}` | pixel-art posts, ≤64×64 |

### 4.6 Presence: `kh_presence`

Upsert on `user_id`: `{user_id, display_name, last_seen, avatar, profile}` with
`X-KH-Secret`. Beat every 100 s (180 s simple mode) + on visibility change; ~7 min
online window; **never** send an empty `avatar` on a beat — it erases the picture other
clients see; preserve `profile` keys you don't own. `profile` is a JSON *object*:
`{p,h,b,s,tz,f,sf,j,nameStyle,…}` plus short keys `fr` (frame), `ns` (name style),
`r` (role), `pl` (plan). Reads: online list
`?last_seen=gt.<cutoff>&select=user_id,display_name,last_seen,avatar,profile&order=last_seen.desc&limit=N`
(≤100); discovery sweeps use 14 days/limit 400; avatar batch
`?user_id=in.(…)&select=user_id,avatar&limit=100` (≤100 ids, 16-hex each; cache per
session; missing/invalid avatar falls back to an initial, never hides the row).

### 4.7 Scores: `kh_scores` (corrected — run tokens)

Submission requires sign-in; flow in `_khSubmitScore` @kh-app.js:1549026:

1. Clamp: integer **1..1,000,000,000** (≤0 and >1e9 rejected).
2. **Run token**: `POST /rest/v1/rpc/kh_run_start {p_hash}` returns a `"khr1."`-prefixed
   token (cached in `kh_runtok`, 24 h sweep, submit-valid 20 h). No token → queue and
   retry in 90 s.
3. 30 s same-score dedupe per game (`kh_lastsubmit_{game}`).
4. Insert `{id:"<userId>_<game>_<Date.now()>_<rand>", game ≤40ch, score, display_name
   ≤40ch ("Reader"→""), user_id, date, run:<token>, proof?:<object>}` with
   `X-KH-Secret`.
5. Server errors `SCORE_MAX/SCORE_GAME/SCORE_UPDATE` swallowed; `SCORE_TOKEN` → refresh
   the token and retry; HTTP 422 swallowed; other failures → offline queue.

Offline queue `kh_score_q`: best-score-only per game, optional proof, LRU cap 60,
flushed 6 s after boot and on `online`. Reads are public:
`?game=eq.<slug>&order=score.desc&limit=N` with **ban filtering**
(`kh_banned_usernames`-backed `_khIsBannedSync`) and per-name dedupe. Multiplayer/online
results never hit `kh_scores`. `proof` payloads (e.g. hanoi submits the full move log)
allow server-side replay; `kh_id_report` behavioral-biometrics telemetry and
`kh_chess_fairplay` flag "look closer" — nothing is auto-banned.

Spot-checked formulas: minesweeper `max(1,bombs×50−seconds)`; wordle
`max(1,(7−attempts)×100+wordLen×10)`; hanoi `100×discs+time-bonus` (+proof); freecell
`max(1,2000−moves)`; mahjong `10×cleared+cleanWins`; hangman `10×streak`; ttt
`10×streak+wins`; dominoes `50×matches+rounds`; nerdle `(tries−guesses+1)×10+len`.

### 4.8 Mail: `kh_mail`

| Column | Meaning |
|---|---|
| `id` | `m_<sender-id>_…` |
| `to_user`, `from_user` | Canonical names (trim → lowercase → before `@` → ≤40 chars) |
| `from_id` | Sender's 16-char id |
| `subject`, `body` | Encrypted with the **recipient-name context** |
| `ts`, `reply_to`, `owner_secret` | Metadata; unsend is `DELETE …?id=eq.<id>` with the owner secret |

Key derivation: `_msgEncrypt("mail:" + to_user, …)` → AES key
`SHA-256("khmsg::mail:" + normalizedRecipient)`; both sender and recipient can derive it,
so this is compatibility encryption, not recipient-only confidentiality. Body ≤8,000
chars; inbox/sent split locally by `from_id`; reads are worker-scoped to the caller
(`X-KH-Secret`), cap 200. The web UI also surfaces external providers
(gmail/outlook/yahoo/proton/icloud/fastmail) as open-in-new-tab cards. Recovery-code
email is separate: `POST https://email-worker.arancool3000.workers.dev/send` with
`{to,from_user,subject,body}` — contains a recovery secret; never log or reuse as
generic mail.

### 4.9 Groups, topics, and the app namespace

`kh_groups` rows are `{code,name,creator,created_at,updated_at}`. Look up by exact code;
create with `POST /rest/v1/kh_groups` + account secret (insert, not upsert — never
`on_conflict=code` against an existing room; on 409 look the room up instead). Batch
known-code reads at ≤40 codes per request with `code=in.(…)`. Blind directory scans are
blocked by the gateway.

**Topics** are a directory inside the app-namespace RPCs, not a scan: list =
`POST /rest/v1/rpc/kh_app_list {p_hash:<token>, p_app:"khtopics", p_room:"index",
p_limit:200}`; rows `{k:<12-digit room code>, v:<JSON string {n ≤220, t:unix-ms,
a ≤40, x:hidden}>, at, by}`; hidden (`x:true`), malformed or non-12-digit rows are not
active listings. Create = insert the `kh_groups` row, then
`POST /rest/v1/rpc/kh_app_put {p_hash, p_app:"khtopics", p_room:"index", p_key:<code ≤16>,
p_value:<JSON {n,t,a}>}`; mods delete via `kh_app_put` with a removal `p_token`.
Live previews read `kh_messages` in batches of 12 rooms (`order=ts.desc&limit=400`).

The same `kh_app_list`/`kh_app_put` key-value namespace is the **cloud-save** store:
`p_app` is the app id (`stronghold` in kh-games.js, rooms `world`/`clan_<id>`, keys ≤40
chars), user-scoped by `p_hash`, with server quota errors `APP_QUOTA/APP_ROWS/APP_VALUE`.
`kh_app_get` fetches one key.

### 4.10 App Store: `kh_store_apps`

| Operation | Request |
|---|---|
| Catalogue | `GET …?select=id,name,cat,author,model,created_at,downloads,preview,icon_art,age_rating,rating_sum,rating_count&order=downloads.desc,created_at.desc&limit=200` |
| Download | `GET …?id=eq.<id>&select=id,name,cat,author,html&limit=1` (author can pass owner secret to read a pending row) |
| Publish | `POST …` + `X-KH-Secret`, body `{id,name,html,cat,author,created_at,downloads,owner_secret}`; ids `pub_<11 hex>`, name ≤60, cat ≤24 (default `Fun`), author ≤40 |
| Rate / review / pending | `POST /rest/v1/rpc/kh_store_rate`, `kh_store_review`, `kh_store_pending` |
| Count download | `POST /rest/v1/rpc/kh_store_download {p_id}` — best-effort; install must succeed if it fails |
| Unpublish | `DELETE …?id=eq.<id>` with the owner secret |

Publishing runs an **AI review** (approved / waitlisted "every review model is busy" /
declined+reason); public listing only shows approved rows. HTML ≤512 KiB is enforced
client-side at publish and import. Downloaded HTML is untrusted: the web client runs it
in a sandboxed iframe (`_khAppSandbox`, `_khWrapAppSecure`); the desktop writes it to
`KindleHubPro/apps` and launches the system browser. `KHAPP1:` codes are the local
import/export format `{label,color,icon,html}` — device-local transfer, not a publish
API.

### 4.11 Moderation, feedback, and misc RPCs

- **Reports** → `kh_feedback` rows `{id:"urep_"|"aifix_"|"rep_"+ts, type:"bug", text ≤1990,
  votes:0, date}`; text carries `[REPORT] [SECURITY] [USERNAME]` tags, subject ids,
  reporter and AI-triage verdict ("AI: BAN"/"AI: WARN" — human confirm required).
  Moderator applications are **not** `[MODAPP]` in the current web client; they are
  redirected to Settings → Account → Apply to be a moderator.
- **Moderator ops**: `kh_mod_stats {p_token:<mod code>}` → `{level, users, onlineNow,
  visitsToday, visitors7d, messages, groups, feedbackOpen, appsPending?}`;
  `kh_mod_claim {p_code_hash:<sha256 of uppercased trimmed invite>, p_name, p_uid}` —
  the invite itself is never sent. `kh_mod_badges/ranks/create/list`, `kh_smod_reports`
  support the mod tools; `kh_mod_code` is stored in localStorage and excluded from cloud
  sync.
- **Age/verify**: `kh_age_verify`, `kh_age_mine`, `kh_age_self_set`, `kh_age_marks`,
  `kh_birthday_claim`; age rooms `9300000000{05..18}`.
- **Polls** (chat polls live on server state): `kh_poll_save/vote/voters/stats/list/delete`.
- **Misc**: `kh_member_count`, `kh_follower_counts`, `kh_gift_claim`, `kh_redeem_gift`,
  `kh_helper_link/status/unlink`, `kh_pid_lookup`/`kh_public_id` (public-id inbox
  addressing), `kh_plan_counts`, `kh_head_withdraw_warning`, `kh_score_boost_list`.
- **Photo/gallery worker** (`kindlehub-state.arancool3000.workers.dev`): `GET /photos`,
  `PUT /photo {id,data}`, `GET/DELETE /photo?id=…` with headers `X-KH-Secret:
  <SHA-256("khphoto::"+photoKey)>` and `X-KH-Account: <authToken>`; `data` is
  client-sealed. Routes are inferred from the web source, not a published schema.

## 5. Corrections against the previous reference

The previous ARCHITECTURE.md (uploaded alongside the zip, "KindleHub Pro API Reference",
952 lines) was written mostly from the desktop adapter. Checking it against the shipped
web client produced these corrections:

| # | Previous claim | What the code says | Evidence |
|---|---|---|---|
| 1 | Inbox room = `800000` + 16-char user id (16 chars) | Inbox codes are **12 digits**: `800000` + 6 digits derived from the recipient's 6-hex public id (`parseInt(hexDigit,16) % 10`), validated `/^800000[0-9]{6}$/`; sends prefer the `kh_inbox_send` RPC | kh-app.js @1958427, @1562800 |
| 2 | Multiplayer room codes are `mp-{game}-{id}` | `mp-{game}-{short}` is the group **name**; the code is `900000` + the 6-digit short | kh-app.js @1960244, @1562273 |
| 3 | Direct messages use a `DM:*` **code** family | DM rooms are ordinary random 12-digit codes; `DM:` is a room **name** prefix (`"DM: <me> & <them>"`) | kh-app.js @808573, @71819 |
| 4 | Message transport is polling only (desktop docs) / realtime (old in-repo doc) | Chat view subscribes to **Supabase realtime** (phoenix protocol, 25 s heartbeat) with adaptive-polling fallback; core sweeps, `KH_MP` (3 s) and inbox/mail probes poll | kh-views.js @2325xxx; kh-app.js @1589600 |
| 5 | (In-repo doc) account key via PBKDF2 210,000 iterations | Account key is the **raw token bytes**; PBKDF2 210k is only `KHS1:` note lock / `KHSP1:` superchat; `KHSL1:` 600k, `KHA2:` 120k | kh-app.js @1252745, @1253300, @814143, @1259978 |
| 6 | (In-repo doc) cloud sync every ~30 s (`_khSaveLoop`) | No such loop: debounced saves + **90 s** periodic sync (load/idle-scaled) + post-load syncs, 45 s retry backoff | kh-app.js @23802, @206867 |
| 7 | Scores are plain `{id,game,score,…}` rows | Submissions carry a server-issued **run token** (`khr1.`, via `kh_run_start`) plus optional `proof` (e.g. hanoi move log); 1..1e9 clamp, 30 s dedupe, offline queue cap 60 | kh-app.js @1549026, @1548362 |
| 8 | Reaction JSON maps emoji → counts | Values are **arrays of 16-char user ids**; counts are array lengths (both docs agree on the read shape) | kh-views.js @2285xxx |
| 9 | Reply parents read via `GET …?id=in.(…)` | That's the **desktop** behavior; the web client resolves parents from the in-memory transcript map | kh-views.js @2277500 |
| 10 | Moderator applications posted as `[REPORT] [MODAPP]` kh_feedback rows | The web client redirects mod applications to Settings → Account; only `[REPORT]`/`[SECURITY]`/`[USERNAME]` tags are built | kh-views.js @892155, @2267460 |
| 11 | Game relay events include `OPEN` | No `OPEN` event exists; observed: `JOIN`, `BYE`, `CHAT`, `ACT`, `STATE`, `TTT_STATE`, `MOVE_TTT`, `MOVE_C4`, `C4_STATE`, `DB_INIT`, `DB_MOVE`, `MOVE`, `ST`, `SH`, `RS`, `MV`, `AC`, `CL`, `SG_*` | kh-games.js throughout |
| 12 | (In-repo doc) `bucketbrigade` co-op exists, `pool` slug | Neither exists: `bucketline` is single-player arcade; pool is slug **`eightball`**; co-op party games are Barrage, Last Ladder, Sketch & Guess | kh-games.js `_doLaunch` |
| 13 | Media markers limited to 8 prefixes | Full set: `KHIMG1` (+`KHCAP1` trailer), `KHAPP1`, `KHDRAW1`, `KHFLIP1`, `KHPOLL1`, `KHVOTE1`, `KHSTK1`, `KHSTORY1`, **`KHMEME1`**, **`KHPIX1`** | kh-app.js @30829, @143749, @128624 |
| 14 | RPC set ends at `kh_store_download`/`kh_mod_*`/`kh_recovery_set` | ~45 RPCs in use (see §3.1), incl. `kh_run_start`, `kh_inbox_send`, `kh_poll_*`, `kh_meme_*`, `kh_age_*`, `kh_id_report`, `kh_gift_*`, `kh_pid_lookup` | kh-app.js RPC call sites |
| 15 | Tables list has 8 tables | Also `kh_errors` (client telemetry, incl. the shell's stall logger) and `kh_banned_usernames` (ban lookups) | kh-app.js @1280173, @1460455; index.html line 6 |
| 16 | `mod/` assumed to be moderator tooling | `mod/index.html` is a **Kindle jailbreak download shelf** (Vera/WinterBreak/LanguageBreak, KUAL, MRPI, KOReader) served from `files.json`/worker `/mod/list` | mod/index.html |
| 17 | Third-party meme templates via `api.imgflip.com` | Memes are first-party now: `kh_meme_list/use` RPCs + `KHMEME1:` chat cards + `api.memegen.link` | kh-app.js; kh-views.js @2408810 |
| 18 | Presence heartbeat cadence unspecified | 100 s beat (180 s simple mode), ~7 min online window, discovery window 14 days | kh-app.js @1243100, @1249100, @996997 |
| 19 | Topic rows `{n,t,a}` only | Rows also carry a hidden flag `x`, and `k` must be a 12-digit code; `p_key` ≤16 chars | kh-views.js @1723279 |
| 20 | Uploaded doc ends with a dangling table fragment (CommunityViewModel.cs …) | Merged artifact; removed here. The desktop-ViewModel rows belong to the (unavailable) desktop repo docs | uploaded ARCHITECTURE.md lines 946–953 |

Confirmed-and-kept: token derivation (incl. `aran`→`arancool3000`), userId = first 16
chars, state envelope flags `G`/`c`/`g` + dictionary, `enc1/enc2` chat envelopes,
`khmsg::` room-key derivation, `khmsg::mail:` mail context, `KHAV1/KHAV2` avatar formats,
`KHAPP1` 512 KiB limit, `kh_set_reaction` contract, `kh_recovery_set {p_hash,p_email}`,
store endpoints/owner-secret semantics, fixed codes `000000000000`, `000000000001`,
`800000777777`.

## 6. External services actually referenced

First-party: `kindlehub-api` (REST+RPC+AI proxy+`/stats`), `email-worker` (recovery
mail), `kindlehub-state` (photos + legacy `/state`), `kindlehub-helpers`, `vpn` (fetch
tunnel), `ai.kindlehub.pro`/`ai2.kindlehub.pro`, `read./tube./game./game3./browse./
browse2./kindletube./static./static2.` subdomains, `frames/*.png` profile-frame PNGs
(`KH_FRAMES`), origin relay `/rv/{a,b}`, `tube.kindlehub.pro/health` (Raspberry Pi video
preparer, checked by the status page).

Third-party by feature family (requests are assembled client-side; treat as unstable
contracts):

| Family | Hosts |
|---|---|
| AI | generativelanguage.googleapis.com, openrouter.ai, api.openai.com, api.anthropic.com, api.mistral.ai, image.pollinations.ai, r.jina.ai; keys live in account state (`geminiKey`, `openrouterKey`, `anthropicKey`, `openaiKey`, `mistralKey`) with a shared community key through `callAI()`/`kh-gemini-proxy` |
| Translation/dictionary | translate.googleapis.com, api.mymemory.translated.net, api.dictionaryapi.dev, api.lyrics.ovh |
| Books | gutenberg.org (cache/epub + files), gutendex.com, openlibrary.org, archive.org, librivox.org, standardebooks.org |
| Weather/maps | api.open-meteo.com (+ air-quality/geocoding), api.bigdatacloud.net, ipapi.co, nominatim.openstreetmap.org, overpass-api.de, router.project-osrm.org, api.rainviewer.com tiles |
| News/feeds | api.rss2json.com (primary pipeline), feeds.bbci.co.uk, news.google.com; CORS proxies api.allorigins.win / api.codetabs.com |
| Media | accounts.spotify.com / api.spotify.com, YouTube frontends (youtube-nocookie, yewtu.be, inv.nadeko.net, inv.tux.pizza, invidious.nerdvpn.de, pipedapi.kavin.rocks / drgns.space / adminforge.de / private.coffee), images.unsplash.com, picsum.photos, images-api.nasa.gov, api.artic.edu, commons.wikimedia.org, api.memegen.link, i.ytimg.com, cdn.jsdelivr.net, fonts.cdnfonts.com |
| Utilities | api.pwnedpasswords.com (registration leak check), api.adviceslip.com, v2.jokeapi.dev, www.themealdb.com, numbersapi.com, stooq.com, ntfy.sh, en.wikipedia.org |

## 7. Mirror tooling

- **update.py** (923 lines) fetches production and rewrites the mirror: fetch shell →
  extract `kh-app.js?v=<hex>` (or plain name), version from `KH_FULL_VERSION`, build mark
  from `<meta name="kh-src">`; fetch guard pages (`/edu/`, `/edu/plan.js`,
  `/edu/questions.js`, `/lite`, `/read/`, `/status/`, `/mod/`) stripping the rotating
  Cloudflare challenge script; detect layout (`query` = hashed `?v=` names, `plain` =
  plain names — this snapshot is plain); BFS-collect every `kh-*.js` bundle; deobfuscate;
  diff vs keep-set (retired hashed files are orphans); write atomically; update
  `.kh-update-state.json` + `update.log`. **Deobfuscation is currently a no-op**: the
  renamer branch is disabled ("scope renaming can break bundled runtime assumptions"),
  so bundles ship as fetched; the Oct 5 runs used the query layout (v10.15.0,
  unlabeled=56419→0), Oct 9+ runs are plain (v10.18.0, v10.70.1).
- **.kh-tools/rename.js** (byte-identical to the heredoc inside update.py) is a
  Babel-based scope-aware identifier renamer (`_0x…`, single letters, 1–3 char names →
  role-inferred stable names), currently disabled.
- **serve.py** replicates `_redirects`: `/lite`→lite.html, `/edu/…`→edu/, any single
  no-dot segment → shell, `kh-*.js?v=hash` mapped to plain files, no-cache HTML vs cached
  bundles.

## 8. Desktop client context (from the handoff doc in git)

The archive's git history carries `KINDLEHUB_DESKTOP_HANDOFF.md` (deleted from the
working tree, recoverable via `git show 665be54:KINDLEHUB_DESKTOP_HANDOFF.md`): a .NET 8
desktop client (`KindleHub.Core` VB.NET + `KindleHub.Client` C#/Avalonia 11.0.10) that
re-implements this protocol — the same token derivation, AES-GCM state format,
`enc1/enc2` chat, topics RPCs, store, and game relay — using this web source as the
compatibility reference. Avalonia-specific fixes (TextTrimming enum values,
AppStoreViewModel constructor kick) are desktop-repo concerns and are not verifiable
from this mirror. The old in-repo ARCHITECTURE.md section 4 documents that client's
structure (`KindleHubApiClient.vb`, `ChatMedia.vb`, `RoomRegistry`, `ChatPrefsStore`,
session file `~/.config/KindleHubPro/session.json`, local app HTML under
`KindleHubPro/apps` launched via the system browser).

## 9. Compatibility checklist (updated)

- UTF-8, lowercase hex, UTC ISO timestamps; `kh_presence.profile` must be a JSON object,
  never a string; ignore unknown keys.
- Keep the account token, per-message owner secrets, moderator codes and recovery data
  out of URLs and logs; the token is a reusable credential even though it isn't a bearer
  token.
- Scope every `kh_messages` read to an exact room or id list; respect per-client caps
  (web 30/poll, desktop 200, discovery sweeps 100–400) and batch RPC/in-list sizes.
- Fresh 12-byte IV per encryption; 16-byte GCM tag; never reuse an IV with a key.
- Preserve unknown account-state keys; edits are read → modify → re-encrypt → upsert.
- Scores need a fresh `khr1.` run token (`kh_run_start`); clamp 1..1e9; dedupe 30 s;
  queue offline (cap 60) and flush on `online`.
- Inbox codes are 12 digits from the public id (`kh_inbox_send` preferred); MP room
  codes are `900000xxxxxx` (name `mp-{game}-{short}`); global/crosschat/lobby are fixed.
- Tombstone processed inbox event ids (`friendReqTombs`) before syncing account state.
- Decrypt failures must render placeholders, never crash; treat store HTML, messages,
  game events and presence JSON as untrusted input.
- Handle 401/403 (policy/secret), 409 (conflict → re-read), 429 (rate limit), 422
  (swallow for scores) without crashing; don't retry non-idempotent inserts blindly.

## 10. Source map

| Concern | Where |
|---|---|
| Shell, CSP, guards, stall logger, boot | `index.html` |
| State, auth, crypto, REST/RPC layer, sync, presence, avatars, routing, `KH_MP`, score pipeline | `kh-app.js` (byte offsets per §3.1) |
| 105 feature views, chat UI, mail UI, app store UI, moderation UI | `kh-views.js` |
| 127 games, game loop, leaderboard submission call sites | `kh-games.js` |
| Lite client (16 views, same protocol, stale v10.15.0) | `kh-lite.js`, `kh-lite-views.js` |
| Edu / Read / Mods / Status sub-sites | `edu/`, `read/`, `mod/`, `status/` |
| Mirror updater + renamer + local server | `update.py`, `.kh-tools/rename.js`, `serve.py` |
| Desktop client contract (outside this archive) | `git show 665be54:ARCHITECTURE.md` §4, `git show 665be54:KINDLEHUB_DESKTOP_HANDOFF.md` |

This document reflects the mirror at v10.70.1 (2026-11-15). The live site can move
faster than this archive; before shipping an interoperating client, re-check formats
against a fresh `update.py` pull.
