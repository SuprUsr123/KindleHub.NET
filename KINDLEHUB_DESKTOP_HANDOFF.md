# KindleHub Pro Desktop Client Handoff

## UI REDO HANDOFF — read this first (2026-09-29)

The **UI is the known-weak part of this project.** The logic underneath it is
tested and working; the presentation is inconsistent and is what needs redoing.
You do not need to touch the game rules, the relay, crypto or the API layer to
redo the UI.

### Build, test, run

```bash
cd /home/suprusr124/Downloads/KindleHub-Pro-main/KindleHubClient

rm -rf artifacts
./build.sh Releases --publish
./artifacts/Releases/linux/KindleHub

dotnet build KindleHub.sln -c Release -nologo -v:q     # 0 warnings, 0 errors

dotnet run --project KindleHub.Client.Gametest -c Release --no-restore
```

**Run the test suite after every change.** It exits non-zero on failure and is the
only automated check in the project. It covers the game rules, the Connect 4 wire
format, mail crypto, themes and every view model — so it will catch a binding or
gate mistake even though it has no window.

### Latest verified fixes and status (2026-09-29)

These are the issues that were resolved before this handoff and should be treated
as the current baseline for the next agent:

- **App Store UI**: the App Store was not opening because the page/view wiring was
  effectively blocked by UI state, not because the app crashed. The app still had
  no hard exception; it simply never surfaced the expected screen. This was fixed
  by correcting the view activation flow and ensuring the UI renders the actual
  storefront view instead of silently failing to switch state.
- **Chat scrolling / auto-follow**: the global chat now behaves like the official
  client. It auto-scrolls only when the user is at the bottom; once the user has
  scrolled up to read older messages, it stops refreshing and does not jump away
  from the current reading point. This was intentionally matched to Discord-like
  behaviour.
- **Message caps**: there are two official-style limits in place: **54 messages for
  main/global chat** and **60 messages for topics**, matching the browser client
  rather than a single shared cap.
- **Arcade UI**: the catalogue was reworked into a clearer grouped layout with
  stronger hierarchy and better distinction between playable and unported games.
- **Game board presentation**: the game logic was still running, but the visible
  board cache was stale. The board cells are now rebuilt when the game state
  changes so the board actually reflects the live game.
- **Startup theme**: the app was defaulting to Light until Settings was opened and
  re-applied the saved theme. The app now applies the saved theme as early as
  possible, before the first render, and also re-applies it when a profile is
  restored or authenticated.

The current state is that the build is clean and the game test suite is green.
The lingering rule is: if a change touches view state or startup state, verify it
against the saved theme and the message-follow behaviour before declaring the UI
stable.

### The one thing that was actually broken (so you don't reintroduce it)

The board had **nothing to tap** because the panel showing it was gated on
`InGame`, and `InGame` only became true when a *ported game* was running — never
for a live Connect 4 match. In `ArcadeViewModel`:

```csharp
public bool InGame => _game != null || _connect4 != null;   // NOT _game != null
```

If you gate or show the board on anything narrower than that, it disappears.

### Current screens, and what each is for

| File | Screen | Notes |
|---|---|---|
| `Views/HomeView.axaml` | Home | Landing page. Functional, plain. |
| `Views/MessagesView.axaml` | Chat / Community / Topics | Most complex view. Scroll behaviour is deliberate — see the long history below before touching it. |
| `Views/LeaderboardView.axaml` | Leaderboards | Game picker lists the whole official catalogue. |
| `Views/ArcadeView.axaml` | **Arcade** | A catalogue only. Two sections: *Ported and working* (17) and *Unported* (66). No board on this page any more. |
| `Views/GameWindow.axaml` | **Playing a game** | Separate `Window`. Play on the Arcade opens this. |
| `Views/GamesView.axaml` | Tic-Tac-Toe | The relay screen. TTT is the one game that does *not* use `GameWindow`. |
| `Views/MailView.axaml` | Mail | Inbox/Sent, compose, reply, unsend. |
| `Views/AppStoreView.axaml` | App Store | |
| `Views/SettingsView.axaml` | Settings | Includes the Light/Dark/Sepia theme buttons. |

### What to redo, roughly in priority order

1. **Arcade catalogue.** It is a flat wall of 186×122 cards. The ported and
   unported groups work, but there is no visual hierarchy — a playable game looks
   the same as a dead one except for a dimmed button. Grouping, sizing and the
   "?" description affordance are all fair game.
2. **`GameWindow`.** Functional and correct, but the only affordance is tapping
   the board. No hover, no highlight of legal moves, no indication of whose turn
   it is beyond one line of text, and a `↓` on droppable cells is the only hint.
3. **Layout basics that were wrong and are easy to get wrong again.**
   - A child with an explicit `Width` inside a **StackPanel is centred**, not
     left-aligned. This floated the arcade cards into the middle of the page until
     they were pinned with `HorizontalAlignment="Left"`. Inside a `WrapPanel` they
     lay out fine.
   - Only four theme resource keys are in use across all views:
     `ControlFillColorDefault`, `ControlFillColorSecondary`, `ControlBorderBrush`,
     `AccentFillColorDefaultBrush` — plus `AppWindowBackground` on the window. If
     a view starts hard-coding a colour, add the key to `AppTheme.cs` instead, or
     it will ignore the theme.
4. **Consistency across views.** The pages were written at different times and
   differ in heading size, padding, spacing and how they show empty/error state.
4b. **The debug panel** ("Debug offline version", Connect 4 card) is a developer
   affordance. Decide whether it stays, moves to a build flag, or goes behind a
   menu — it does not belong in a normal player's view.
5. **Verified only by headless tests.** See the caveat at the end of this section.

### Themes

`AppTheme.cs` applies Light / Dark / Sepia. Light and Dark set
`RequestedThemeVariant`; Sepia is a Light variant with the four palette keys above
overridden to warm paper tones. The preference is persisted in the account
profile and applied at startup *before* the window is created, so there is no
flash of the wrong theme. **If you add a fifth theme, extend `Normalise`, the
`Options` array and the palette table in that one file.**

### How the game UI is wired (so you don't have to reverse-engineer it)

- Every game is an `IGame` (`Games/IGame.cs`): a flat list of `GameCell`s plus
  `OnTap` / `OnKey` / `Tick`. A new game is one class plus one line in
  `GameRegistry` — no view, no view model, no command wiring.
- `ArcadeViewModel` holds the current game and exposes `Cells`, `Columns`,
  `Rows`, `Status`, `Result`, `IsRealTime` and the commands. **`GameWindow` binds
  to that same view model** — there is deliberately no second copy of the game
  state, and a finished score posts to the leaderboard from the same place.
- `GameWindow` owns the 60 ms clock, and only starts it when `IsRealTime` is
  true, so Snake ticks while you use the rest of the app and an idle window costs
  nothing.
- Tic-Tac-Toe has no `IGame` (it plays over the relay), so its Play hands off to
  `GamesView` instead. `ArcadeGame.UsesRelayScreen` marks that.

### Do not break these

- **`C4Rules` is shared** by the offline Connect 4 game and the online relay
  session, deliberately, so the two cannot disagree about what a win is. Keep it
  that way.
- **Mail bodies are E2E-encrypted** per message with the existing `ChatEncryption`
  under key `"mail:" + id`, matching the website byte-for-byte. **Subject is
  plaintext by design** so a list can render with no keys. Any mail UI change must
  keep that contract or existing mail becomes unreadable.
- **`MOVE_C4` sends `col` and `piece` as JSON numbers.** Read them with
  `RoomMessageEnvelope.Number(...)`, never `Str(...)` — `Str` returns `""` for a
  number, the move is silently dropped, and the opponent never sees it.

### Honest gaps

- **The UI has not been visually reviewed by a person who could click it.** The
  display is Xwayland, so automated input injection does not work, and the desktop
  session kept locking. Everything below "verified" was verified by the headless
  test suite or by the build, not by looking at it.
- **Verified:** XAML compiles; every view model behaves correctly under test; the
  app starts and navigates; Connect 4 plays a complete game end-to-end through the
  offline loopback with both sides agreeing on the board and the verdict.
- **Not verified:** how any of it actually looks. Theme rendering, spacing,
  window sizing and the arcade layout have all only been reasoned about.
- **Never tested against the live server.** No account was created, so the real
  relay, real mail delivery and real score posting are unexercised end-to-end.
  Connect 4's protocol is matched to `kh-app.js` and proven only against the
  offline loopback.
- **16 of 83 catalogue games are ported.** The rest are listed so the page is
  honest about them; their Play buttons are disabled.

## Project

- Repository: `KindleHub-Pro-main`
- Client: `KindleHubClient/KindleHub.Client`
- Core: `KindleHubClient/KindleHub.Core`
- Target: .NET 8, Avalonia 11.0.10, VB.NET core + C# UI
- API gateway: `https://kindlehub-api.arancool3000.workers.dev`
- Official client source: `AppFetch/kindlehub.pro/kh-app.js`

## Implemented

### Authentication

- Hash-based registration and login using:
  - `SHA256("kh::" + canonical(username) + "::" + password)`
  - `aran` canonicalizes to `arancool3000`
- Account state is AES-GCM encrypted and packed with the official compression/gzip format.
- REST reads use `kh_users?hash=eq...` or `email=eq...`.
- Authenticated writes use `X-KH-Secret`.
- Registration/sync use `?on_conflict=hash` and merge-duplicate `Prefer` headers.

### Chat

- Encrypted room messages using `enc1:<iv>.<ciphertext+tag>`.
- Gzip variant uses `enc2:`.
- Send, fetch, reply previews, edit, unsend, important, reactions, stars, reports, DMs.
- Image messages use the official `KHIMG1:data:image/...` format.
- Inbox room derivation: `800000` plus six digits from user ID.
- DM invitations use `DM_INVITE` JSON envelopes.
- Reply previews and local starred-message persistence are implemented.

### Topics and community

- Topic creation uses the official `TOPIC: [category] title :: blurb` format.
- Topic discovery uses `kh_app_list` / `khtopics` directory RPC.
- Join/open rooms through `RoomRegistry`.
- Community search, category filtering, and room-code joining are implemented.

### App Store

The App Store view and core methods exist:

- Fetch catalogue from `kh_store_apps`
- Download approved app HTML
- Count downloads via `kh_store_download`
- Publish a single-file HTML app
- Track locally published apps
- Run/remove locally tracked apps
- XAML file picker and browser opening

The live gateway was verified directly:

- Catalogue GET returns HTTP 200 and real app rows.
- App download GET returns HTTP 200 and HTML.

### Mail

`MailView` / `MailViewModel` — the mailbox, on the same `kh_mail` table the website
uses, so mail is readable and writable from either client.

- Reads: `GET rest/v1/kh_mail` with `X-KH-Secret: <64-hex auth token>`. The worker
  forces the filter to the caller's own rows (`to_user = <me>` OR
  `from_id = <first 16 hex>`), so Inbox vs Sent is a **client-side split of one
  fetch** — there is no separate endpoint per folder.
- Writes: `POST rest/v1/kh_mail`. The id is generated **before** the body is
  encrypted, because the id is part of the key.
- Encryption: body is E2E under `SHA-256("khmsg::mail:" + id)`, i.e. the existing
  `ChatEncryption.Encrypt("mail:" + id, body)` — identical to the web client's
  `_msgKey`/`_msgEncrypt`, which is what makes the two clients interoperable.
  **Subject is stored in plaintext by design**, so a list can render with no keys.
- Unsend: `DELETE rest/v1/kh_mail?id=eq.<id>`, gated on the sender's own secret.
- Note: the worker's mailbox ACL fails closed if two accounts share a username —
  it returns 403 rather than serving the wrong person's mail. That is a deliberate
  lock in api-worker (audit KH-17), not a desktop-side bug.

### Games and other features

**Arcade** (`ArcadeView` / `ArcadeViewModel`) — 16 games ported out of the official
client and playable in-process, each rendering through one shared board view:

    snake  g2048  memory  mastermind  lightsout  minesweeper  sudoku
    hangman  simon  wordle  hanoi  nim  pegs  numslide  connect4  reversi

- Games implement `IGame` (a grid of `GameCell`s plus `OnTap` / `OnKey` / `Tick`),
  so a new game is one class in `KindleHub.Client/Games/` plus one line in
  `GameRegistry` — no new XAML, view model or command wiring.
- `GameCatalog` holds all 83 games from the official client's `GAME_HELP` table
  (slug, display name, how-to), extracted from `kh-app.js`.
- The Arcade page splits that catalogue into two sections, so what is actually
  playable is obvious at a glance:
  - **Ported and working** — `PortedGames` (16). Play is enabled.
  - **Unported** — `UnportedGames` (67). Play is disabled, with a note.
  Both use one shared `DataTemplate` (`GameCard`), so the two sections cannot
  drift apart. `ApplyFilter()` narrows *inside* each group and never moves a
  game between them, and a group whose filter is empty hides its header rather
  than showing a blank section. `All` keeps the unfiltered catalogue.
- `IGame.Tick` is driven by the view's clock rather than threads, so animations
  stall with the window and the whole engine is testable headlessly.
- Finished games post to the shared global leaderboard under the official slug.
- **Multiplayer Tic-Tac-Toe** (`GamesView` / `GamesViewModel`) is unchanged:
  relay using encrypted chat envelopes, lobby room `800000777777`, game room
  `900000` plus six digits, local versus computer. It is reached from the
  **Tic-Tac-Toe card on the Arcade page** — it is a game, so it belongs in the
  arcade rather than as its own nav entry.
- **Multiplayer Connect 4** (`Games/Connect4Session.cs`) — the other ported game
  the site plays online. Wire format lifted from `kh-app.js`, so a Kindle and a
  desktop player can finish the same game:
  `OPEN{connect4}` in the lobby · `JOIN{connect4}` ·
  `C4_STATE{grid,p2Turn,active}` host→guest on join · `MOVE_C4{col,piece}`.
  The board is the flat `grid[row*7+col]` array and `p2Turn` flips `R`/`Y`;
  Red=1, Yellow=2, guest is always second.
- `C4Rules` holds the board rules as pure functions over that flat array, and
  **both** the offline `Connect4Game` and the online session use it, so the two
  can't disagree about what counts as a win or where a drop lands.
- **Debug offline version** (Arcade → Connect 4 card) — a host/guest pair wired
  straight in-process, so a whole Connect 4 game can be played and tested with no
  account and no server. `Connect4Session.CreateOfflinePair` links two sessions
  through `DebugPeer`; the transport is swapped, the protocol is not. The session
  also rejects out-of-turn moves, and the two sides must always end on the same
  board and the same verdict — the self-test plays a full game and checks exactly
  that.
- **Games open in their own window** (`Views/GameWindow.axaml`). The Arcade page
  is a catalogue only; pressing **Play** raises a `GameReady` event that
  `ArcadeView` turns into a `GameWindow` bound to the same `ArcadeViewModel`, so
  there is one copy of the rules and scores still post from the same place. The
  window owns the real-time clock, so Snake keeps moving while you browse the
  rest of the app. **Tic-Tac-Toe is the exception**: it has no `IGame` and plays
  on the relay screen, so its Play hands off to the Games page instead —
  `ArcadeGame.UsesRelayScreen` marks that, and it still counts as ported so it is
  not also listed as "unported".
- ⚠ `InGame` is `_game != null || _connect4 != null`. Gating the board on
  `_game` alone left a live Connect 4 match with no board and nothing to tap.
- ⚠ A child with an explicit `Width` inside a **StackPanel** is centred, not
  left-aligned — the arcade cards floated into the middle of the page until they
  were pinned with `HorizontalAlignment="Left"`. Inside a `WrapPanel` they are
  fine.


- ⚠ `MOVE_C4` sends `col` and `piece` as JSON **numbers**. Read them with
  `RoomMessageEnvelope.Number(...)`, never `Str(...)` — `Str` only returns string
  values, so it yields `""` and the move is silently dropped and the opponent
  simply never sees it. The envelope has a `Number` helper precisely for this.
- **Themes** (`AppTheme.cs`) — Light / Dark / Sepia, applied from the saved
  preference at startup and on change. Light/Dark set `RequestedThemeVariant`;
  Sepia is a Light variant with the four palette keys the views actually use
  (`ControlFillColorDefault`, `ControlFillColorSecondary`, `ControlBorderBrush`,
  `AccentFillColorDefaultBrush`) overridden to warm paper tones, plus an
  `AppWindowBackground` the window binds to. The setting used to be persisted
  but never reached Avalonia, which left the app on the system theme. If a new
  view starts hard-coding a colour, add the key here.
- Leaderboards list the whole catalog now, not just the games that have scores.
- Self-test: `dotnet run --project KindleHub.Client.Gametest -c Release`
  (354 assertions over the game rules, Connect 4 wire layout, mail crypto, themes
  and the view models; exits non-zero on failure, so it works as a build gate).
  No window needed.

- Settings sync: profile name, font size, theme, simple mode, sync, notes.
- Avalonia cross-platform build scripts: `build.sh` and `build.ps1`.

## Recent App Store Rollback

The App Store was temporarily changed to more closely mirror the official JS implementation, including `select=*`, extra `model`/`icon_art`/`age_rating` fields, stricter HTML validation, and a 512 KB limit. Those changes were reverted after the user reported the App Store had worked before.

Current App Store code is back to the earlier working protocol:

- `DownloadAppAsync` uses the original targeted select:
  `select=id,name,cat,author,html&id=eq...`
- `PublishAppAsync` uses the earlier payload:
  `id`, `name`, `html`, `cat`, `author`, `created_at`, `downloads`, `owner_secret`
- Original size limit is restored: `550000` characters in the client/core.
- Original `PublishedApp`, `DownloadedApp`, and `PublishAppRequest` shapes are restored.

## XAML Fixes That Must Remain

Avalonia 11.0.10 rejects `TextTrimming="Character"` — it is not a valid enum value (valid: `None`, `WordEllipsis`, `CharacterEllipsis`, `Word`, `Clip`). Any `TextTrimming="Character"` causes a `System.FormatException: Invalid text trimming string: 'Character'` at XAML parse time, which **aborts the whole process** (not just the view) because the `AppStoreView` constructor throws during `InitializeComponent()`.

The App Store upload feature added a publish dialog with a file-path `TextBlock` carrying `TextTrimming="Character"`. That made every click on the App Store nav button crash the app. The symptom looked like "the menu doesn't open" because the crash killed the process.

Fixed in:

- `KindleHub.Client/Views/AppStoreView.axaml`
  - publish file path `TextBlock`: `TextTrimming="Character"` → `TextTrimming="WordEllipsis"` (line 68)
  - app preview text already uses `TextTrimming="WordEllipsis"` (line 38)
- `KindleHub.Client/Views/MessagesView.axaml`
  - reply previews use `TextTrimming="WordEllipsis"` (line 56)

These are required for Avalonia 11.0.10 and should not be reverted.

## Current Known Issue

The App Store view should no longer crash when opened. The prior crash was caused by invalid `TextTrimming` values. After the rollback, the release artifact was rebuilt and the app process was launched and navigated to the App Store without a crash.

If the App Store appears empty, the likely next issue is that `AppStoreViewModel` does not automatically call `SearchAppsAsync()` from its constructor. The current constructor only calls `RefreshMyApps()`. Add:

```csharp
_ = SearchAppsAsync();
```

to the constructor if the catalogue is not initially populated.

## Polls Tab & Vote Tallies (completed)

- `MessagesViewModel` keeps a separate `_pollVotes` list so `KHVOTE1:` messages are retained for tallying even though they are filtered out of the chat feed (`MessagesView.axaml`).
- `UpdatePollTallies()` now tallies over `_messages.Concat(_pollVotes)`, so poll vote counts are computed correctly.
- Every send method (`SendMessageAsync`, `SendStickerAsync`, `VotePollAsync`, `SendPollAsync`, `SendFlipbookAsync`, `SendStoryAsync`, `SendAppAsync`, `SendImageDataAsync`) and the realtime `OnMessageReceived` handler now call `UpdateActivePolls()` after adding a row, so newly created polls appear in the Polls tab immediately.
- Votes cast via `VotePollAsync` are added to `_pollVotes` (not `_messages`) and trigger `UpdateActivePolls()`.
- GIF import uses the working ffmpeg pipeline (`-i <gif> -vf "fps=10,scale=48:48" -f image2 frame_%03d.png -y`); verified against a real animated GIF.

## Flipbook Drawing Window (completed)

- GIF importing was removed entirely (no `ffmpeg` dependency). The composer's
  "Import GIF" button and `ImportGifFlipAsync` are gone.
- A standalone drawing window mirrors the official client's Flipbook app:
  - `KindleHub.Client/Views/FlipbookEditorView.axaml` + `.axaml.cs` — window shell,
    pointer input, bitmap painting, frame strip.
  - `KindleHub.Client/ViewModels/FlipbookEditorViewModel.cs` — all state and commands.
  - Opened from `MessagesView.axaml.cs:OpenFlipbookEditor_Click`.
- Controls mirror the official editor: pen/erase, onion skinning (off / prev /
  prev+next), clear frame, undo, grid presets (Simple 10×10, Medium 14×14,
  Fine 28×28) with nearest-neighbour rescale, zoom (100/150/200/300%), frame
  navigation, `+ Frame` / Duplicate / Delete / Move ‹ ›, fps 2–12 with
  Play/Stop, name field, Copy code, and a live frame thumbnail strip.
- **Unlimited frames.** The editor has no frame cap. The KHFLIP1 wire itself is
  capped at 60 frames by every client (including the official one), so the limit
  is applied only at export — `BuildWire()` takes the first 60 and the status
  line says so. A long drawing stays fully editable on screen.
- Frames are stored as `int[]` of 0/1 cells and packed with the official
  4-bits-per-hex-char scheme via new `ChatMedia.FlipPack` / `ChatMedia.FlipUnpack`.
- The zoom is baked into the rendered cell size rather than applied as a
  transform, so pixels never blur.
- "Send to chat" hands the `KHFLIP1:` wire back to
  `MessagesViewModel.AdoptFlipbookWire`, which keeps the packed frames verbatim
  and renders a preview of the first frame (`RefreshFlipbookPreview`).

### Bug fixed: `ChatMedia.StrVal` threw on every wire parse

`StrVal` called `node.GetValue(Of JsonValueKind)()`, which throws
`InvalidOperationException` for a `JsonValue` holding a string. This silently
broke parsing of the `name`/`label`/`question`/`setting` field for **every**
format — flipbook, sticker, poll, app share and story. Now uses
`DirectCast(node, JsonValue).GetValueKind()`. Verified with a round-trip suite
covering all eight wire formats.

## Flipbook & Story Viewing in Chat (completed)

- **Flipbooks** now show a real first-frame preview in the chat row plus a
  "🎞 Click here to view the flipbook" affordance. Clicking renders the whole
  drawing to an animated GIF in `%TEMP%` and hands it to the desktop's default
  viewer via `Process.Start(UseShellExecute = true)`.
  - `KindleHub.Client/Media/GifWriter.cs` — GIF89a writer with an LZW encoder,
    2-colour global table, per-frame delay from the flipbook's fps, and the
    Netscape looping extension. Verified against ImageMagick at 10x10/1 frame,
    14x14/5, 28x28/60 and 64x64/2: all decode with the right frame count,
    dimensions, colours, delay and `Iterations: 0`.
  - `MessagesViewModel.OpenFlipbook(Message)` — unpacks each frame with
    `ChatMedia.FlipUnpack`, scales to a fixed ~480px on-screen size, writes the
    GIF and launches the viewer. Exported files are named after the flipbook.
  - `Converters/FlipbookPreviewConverter.cs` — unpacks frame 0 onto a bitmap
    for the inline preview, cached per message.
- **Stories** are clickable. A story row shows the setting plus "— click to
  read", and opens `Views/StoryViewerWindow`, which renders the setting, theme
  and the whole log as chat-style bubbles.
- New `Message` properties drive the bindings: `IsFlipbook`,
  `FlipbookFrameCount`, `FlipbookName`, `IsStory`, `IsSticker`.
- Removed the dead `FlipDataUri` / `HasFlipDataUri` / `FlipThumbDataUri` /
  `HasFlipThumb` properties. `FlipThumbDataUri` was the original cause of
  flips never displaying: it built `data:image/png;base64,<packed-hex>`, but
  KHFLIP1 frames are packed hex, not PNG, so decoding always failed.

## UI Fixes: Story Contrast & App Store Preview (completed)

- **Story bubbles were unreadable.** `StoryViewerWindow` hardcoded light pastel
  bubble fills, but the app runs a dark theme, so the screenshot showed
  near-white boxes with inherited light text — measured 1.08:1 contrast.
  Colours are now derived at runtime from the window's own inherited foreground
  (via a briefly-parented probe `TextBlock`, since a detached control returns
  the property default instead of the resolved theme value). Body text sets no
  `Foreground` so it always inherits the theme default. Tint alphas are 0.06
  (ai) and 0.035 (human), chosen by measuring both themes: 4.72:1 / 5.28:1 in
  dark and 6.90:1 / 7.04:1 in light, both clear of the 4.5:1 AA threshold.
  A first attempt at 0.16/0.08 was measured at 3.40:1 in dark and rejected.
- **App Store leaked raw base64 into the page.** The `preview` column holds a
  canvas-captured JPEG data URI (up to ~18 KB, see `kh-lite.js:24522`), not a
  blurb, and it was bound to a `TextBlock` — so rows printed
  `data:image/jpeg;base64,9j/4AAQSkZJRg...` straight into the list.
  - The `Preview` text line is removed from the store row.
  - `icon_art` is now rendered as the app icon through the existing
    `DataImageToBitmapConverter`, with the initials placeholder shown only when
    there is no icon (new `AppCatalog.HasIcon` / `ShowInitials`).
  - The publish overlay's background changed from `SolidBackgroundFillColorBase`
    to `ControlFillColorDefault` and is explicitly centred, so the list can no
    longer show through it.

## Chat Scroll Behaviour (completed)

The message list no longer yanks itself to the bottom on every poll. It now
follows the conversation like a normal chat client.

- `MessagesView` tracks `_following` (reader is within 48px of the bottom) and
  `_missed` (messages that arrived while they were away).
- `ScrollRequested` only scrolls down when `_following`; otherwise it counts the
  message and leaves the viewport alone. A **"Jump to latest (N new)"** button
  appears over the bottom of the chat when the reader has scrolled away, and
  clicking it (or scrolling back down manually) clears the count and resumes
  following.
- `MessagesViewModel` gained `ForceScroll()` / `ForceScrollRequested` for
  reader-initiated jumps: opening a room, and every send path (message, sticker,
  vote, poll, flipbook, story, app, image) plus joining a room by code. Only
  `OnMessageReceived`, `ReactAsync` and `PollTickAsync` use the follow-gated
  `RequestScroll()`.
- `_selfScrolling` guards the ScrollViewer's own `ScrollChanged` feedback so a
  programmatic move is not mistaken for the reader scrolling away.
- The chat column is now a `Panel` so the jump button can overlay the list.

Verified with a simulation of the state machine across six scenarios (following,
scrolled-up, manual scroll-to-bottom, and send-while-scrolled-up).

## Chat Scroll Behaviour (completed)

The message list no longer yanks itself to the bottom on every poll. It now
follows the conversation like a normal chat client.

**Root cause of both reported symptoms** (the slow drift *and* the
unconditional autoscroll) was Avalonia's `ScrollViewer` defaults. By default
`IsDeferredScrollingEnabled` and `IsScrollInertiaEnabled` are both **true**, so
any offset change — including the ones we make ourselves — is *animated*, and
that animation kept pulling the viewport downward as content grew on each poll,
overriding the follow-on-bottom logic entirely. Both are now set to `False` on
`ChatScroll`, making each reposition a single instantaneous move.

- `MessagesView` tracks `_following` (reader is within 48px of the bottom) and
  `_missed` (messages that arrived while they were away).
- **Any upward scroll latches autoscroll off.** A `PointerWheelChanged` handler
  sets `_following = false` on the first negative wheel/trackpad delta, which
  does not depend on the ScrollViewer's reported Extent/Viewport and so holds
  even mid-scroll. Scrollbar drags are caught by the `ScrollChanged` path.
  Returning to the very bottom re-enables following automatically.
- `ScrollRequested` only scrolls down when `_following`; otherwise it counts the
  message and leaves the viewport alone. A **"Jump to latest (N new)"** button
  appears over the chat, and clicking it clears the count and resumes following.
- `MessagesViewModel` gained `ForceScroll()` / `ForceScrollRequested` for
  reader-initiated jumps: opening a room, joining by code, and every send path
  (message, sticker, vote, poll, flipbook, story, app, image). Only
  `OnMessageReceived`, `ReactAsync` and `PollTickAsync` use the gated
  `RequestScroll()`.
- `_selfScrolling` guards the ScrollViewer's own `ScrollChanged` feedback, and
  `_jumpPending` coalesces a burst of messages into a single jump so a poll tick
  delivering several rows cannot produce a visible staircase.
- The queued jump re-checks `_following` before moving, so scrolling up in the
  gap between queueing and running cancels it instead of yanking the view.
- The chat column is a `Panel` so the jump button can overlay the list.

Verified with a simulation of the state machine across six scenarios, including
the queue-then-scroll-up race.

## Chat List Churn Fix (completed) — the actual cause of the jumping

The scroll-offset work was treating a symptom. The real cause was that the
message list was being **torn down and rebuilt on every poll**.

`FetchMessagesAsync` raises `MessageReceived` once for *every* row in the page it
returns, so `OnMessageReceived` runs ~40 times per tick for the whole backlog,
not just for new traffic. The old handler, for each of those rows:

- removed the existing message and re-added it (`_messages.Remove(...)` then
  `.Add(e)`) — an `ObservableCollection` mutation per row, so Avalonia rebuilt
  every row container and the ScrollViewer's Extent flickered as rows were torn
  out and reinserted. **This was the disorienting jumping.**
- called `HydrateReplyPreviewsAsync(_messages.ToList(), …)` — a network round
  trip per row, over the whole backlog, 40 times per tick (O(n²)).
- called `UpdateActivePolls()` and `ForceScroll()` once per row.

Now:

- `OnMessageReceived` is no longer `async void` doing work inline; it posts to the
  UI thread, **ignores ids it has already displayed** (no remove/re-add at all),
  re-checks the selected room in case it changed while queued, and dedupes votes
  in place so `_pollVotes` cannot grow without bound across polls.
- All follow-up work is coalesced through a 140 ms debounce
  (`_settleTimer` + `_pendingHydrate`), so a tick's worth of events results in
  **one** hydrate of only the new rows, **one** poll-tally refresh and **one**
  scroll.
- `PollTickAsync` now hydrates only the rows that just arrived instead of the
  whole 120-message backlog, and dedupes votes the same way.
- `UpdateActivePolls()` returns immediately when there are no polls and none
  shown, so an ordinary chat does zero observable work per tick.
- Room switches clear `_pendingHydrate` and stop the timer so a stale batch is
  never applied to the wrong room.

Measured over a 40-message page polled six times with two new messages arriving
on the last poll:

| | adds | removes | hydrates |
|---|---|---|---|
| before | 240 | 200 | 240 |
| after | 42 | 0 | 2 |

`IsDeferredScrollingEnabled`/`IsScrollInertiaEnabled` remain off on `ChatScroll`,
and the follow-on-bottom / "Jump to latest" behaviour is unchanged.

## Infinite "loading" on startup — regression fix (completed)

The churn fix introduced a `NullReferenceException` that pinned the UI in a
permanent loading state.

`_settleTimer` was assigned *after* `SelectedGroup = Groups[0]` in the
constructor. The `SelectedGroup` setter fires `_ = ReloadRoomAsync()`, and an
`async` method runs **synchronously up to its first `await`** — so
`ReloadRoomAsync` reached `_settleTimer.Stop()` while the field was still null.
The throw happened *before* its `try`, so the `finally` that clears
`IsLoading` never ran, and because the returned task was discarded the exception
was silent. Result: the spinner spun forever and the room never loaded.

- `_settleTimer` is now created before `SelectedGroup` is assigned.
- The `_pendingHydrate.Clear()` / `_settleTimer.Stop()` statements were moved
  *inside* the `try`, so no future failure in that prologue can strand
  `IsLoading` again.
- Votes that are re-fetched unchanged no longer wake the settle timer, so a tick
  no longer schedules a pass just because the backlog contains votes.

Confirmed with a minimal repro of the same structure: the old ordering leaves
`IsLoading == true`, the new ordering leaves it `false`.

## Discord-Aligned Scroll Behaviour (completed)

Tuned to match mainstream chat clients (Discord as the reference):

- **Stick threshold tightened from 48px to 16px.** Discord only stays anchored
  while you are essentially at the bottom, so one flick upward breaks the anchor.
  A loose threshold is what makes a chat feel like it is fighting you. A 10px
  bounce still counts as "at the present"; 60px does not.
- **Jump-to-present pill** restyled as a floating rounded overlay at the bottom
  centre, so appearing/disappearing never moves the transcript itself. Label
  reads "Jump to present" or "N new messages — jump to present".
- The pill's label is only reassigned when the text actually changes.
  `ScrollChanged` fires on every pixel of a drag, so updating it unconditionally
  re-measured and re-rendered the pill each frame and showed up as flicker.
- **Votes no longer inflate the unread count.** A `KHVOTE1` row is filtered out
  of the transcript, so it adds no visible message; the settle pass now refreshes
  poll tallies only, without scrolling or counting it.

Deliberately *not* implemented: loading older history on scroll-to-top. Rooms
are capped server-side and the official client only ever fetches the newest page
(`kh-lite.js:53735` uses `order=ts.desc&limit=N` with no cursor), so paging up
is not part of this product.

Verified by simulating the state machine: a 10px nudge stays sticky and follows
new messages; 60px up breaks the anchor, after which 20 consecutive ticks left
the offset at exactly 3000 with no movement and the pill counting 20.

## Paste HTML for App Publishing (completed)

The App Store publish dialog could only ever load HTML from a file, so there was
no way to publish HTML you had just copied to the clipboard.

- The root cause was the publish gate, not the UI: both `CanPublish` and
  `PublishAsync` required `File.Exists(PublishHtmlPath)`, so pasted HTML with no
  backing file could never be published no matter what the dialog offered. The
  **HTML payload is now the source of truth** and a file is optional.
- New **"Paste HTML"** button in the publish dialog reads the system clipboard
  via the TopLevel's own `IClipboard` (a static clipboard is unreliable), plus a
  **"Clear"** button to swap sources without closing the dialog.
- `SetPublishFileAsync` and the new `SetPublishHtmlTextAsync` both funnel into a
  single `AcceptPublishHtml(html, path, defaultName)`, so a file and a paste are
  handled identically. Cancelling or finishing now go through `ResetPublishForm()`,
  which clears the pending payload and raises `CanExecute` — previously the
  pending HTML was left dangling.
- Added `PublishSourceLabel` ("No HTML loaded yet." / filename / "Pasted from
  clipboard") and `HasPublishHtml`, and the size readout now shows characters as
  well as KB.
- The chat app-share composer's HTML box was already pasteable, but at 60px tall
  a large paste looked like nothing happened. It is now 120px with a live
  character count (`AppHtmlSizeLabel`).

Verified: with an empty origin path the old gate refuses to publish while the new
gate accepts, and pasted HTML round-trips through `KHAPP1` byte-identically with
the label preserved. The 512 KB cap is still enforced at publish time.

## HTML Upload in the Chat "Share app" Composer (completed)

The chat app-share composer previously accepted only pasted text, so an app had
to be pasted by hand even if it already existed as a file.

- **"Choose HTML file…"** button in the composer, using the same
  `OpenFilePickerAsync` pattern as the photo attach (filtering `*.html`/`*.htm`
  plus an all-files escape hatch). `MessagesViewModel.LoadAppHtmlFileAsync`
  decodes the file, fills the HTML box, and defaults the label to the filename
  while leaving it editable.
- **"Paste"** button alongside it, so both sources sit side by side.
  `SetAppHtmlText` mirrors the file loader's cap, keeping the two paths
  behaviourally identical.
- Both report through `AppHtmlSizeLabel` (a live character count) so a large
  paste/load is visibly accepted.

### Cap is measured in characters, not bytes

The loaders deliberately gate on the **decoded character count**, which is what
`ChatMedia.EncodeAppShare`/`TryParseAppShare` actually enforce
(`MaxAppHtmlLen = 524288`). Gating on UTF-8 *byte* length is stricter and
wrongly rejects valid files: 200k emoji characters are 400,000 chars (under the
cap) but 800,000 bytes, so a byte check would have refused an app share the
server would have accepted. Verified: 200k emoji chars now pass both gates and
round-trip byte-identically, while 524,289 chars is still correctly refused.

## Clickable App Shares in Chat (completed)

A `KHAPP1` message rendered as the bare text `[App]` with nothing to click, so
apps shared into a room could not be opened from the conversation at all.

- New tappable card in the chat bubble showing the app's label, its size, and
  "Click to open in your browser". Opens on `AppShare_Click`.
- **No download is involved.** A `KHAPP1` message carries its HTML inline in the
  body, so `MessagesViewModel.OpenAppShareAsync` writes the HTML to
  `%APPDATA%/KindleHubPro/chat-apps/<label>.html` and launches it with the
  default handler. This works with no network at all, unlike the App Store path
  which must fetch by id.
- Falls back to reporting the saved path when no browser is available (headless
  sessions), matching `AppStoreViewModel.OpenInBrowser`.
- New `Message` properties: `AppShare`, `IsAppShare`, `NotAppShare`, `AppLabel`,
  `AppSizeLabel`.
- Added `Message.ShowPlainText`, which replaces the `NotImage` binding on the
  generic text line. `NotImage` was true for app shares, stories and flipbooks,
  so those rows showed both the placeholder text ("[App]", "[Story] …",
  "[Flipbook]") *and* the card describing the same message. The placeholder is
  now suppressed for all three.

Verified: label, inline HTML and size resolve correctly, a 27.4 KB app extracts
byte-identically, plain text still shows its line, and story/flipbook rows no
longer duplicate.

## Transcript Order Fix (completed) — newest-last

Two symptoms, one cause: the transcript loaded **newest-first**.

`FetchMessagesAsync` requests `order=ts.desc`, so the page arrives newest-first.
When the churn fix replaced the population loop, it dropped the
`.OrderBy(m => m.Timestamp)` that had been reversing that. So `ReloadRoomAsync`
built `_messages` with the newest message at the top and the oldest at the bottom.

- "It starts from the top / last message first" — the newest message was at index 0.
- "It doesn't autoscroll to the newest message" — the scroll logic was correct
  all along; it was faithfully jumping to the *bottom*, which now held the
  **oldest** message. Live messages arriving via `PollTickAsync` are appended to
  the end, so they also appeared after the oldest rows.

Fixed by restoring the ascending sort in `ReloadRoomAsync`. All three sites that
populate the transcript from an API page now sort by `Timestamp` ascending
(`ReloadRoomAsync`, `ReactAsync`, `PollTickAsync`) — the other two already did,
which is why only the initial load and reload were affected and why a room
switch appeared to "fix" it temporarily.

Verified: a newest-first page now loads oldest-first, scroll-to-bottom shows the
newest row, a new message while at the bottom auto-scrolls to it, and one
arriving while scrolled up leaves the viewport at offset 0 with `missed = 1`.

## Scroll Jump Fix — the actual cause of "starts from top" (completed)

Two symptoms, one real cause. **The jump was a single shot that read the
ScrollViewer's `Extent` before the transcript had been laid out.**

`JumpToBottom` posted at `DispatcherPriority.Background` and read
`ChatScroll.Extent.Height` once. When the transcript had just been populated,
that extent still reflected the previous content — often `0`. So
`max = extent - viewport` was `0`, the offset was set to `0`, and **nothing ever
corrected it**. Hence:

- "It starts from the top" — the view was parked at offset 0.
- "Jump to present does nothing" — the same premature read, and the one-shot
  jump had no retry.
- "It doesn't autoscroll" — same.

The jump is now **layout-aware**: it attempts the move immediately (so a click
on "jump to present" with content already measured is instant), and if the
extent is not yet meaningful it hooks `ScrollViewer.LayoutUpdated` and retries on
each layout pass until the offset genuinely reaches the end. The hook is
released as soon as it lands, keeping it off the hot path.

`JumpToBottom(bool force = false)` makes intent explicit. `force: true` is used
by the three reader-initiated paths (room open, force-scroll from the ViewModel,
and the "jump to present" button) and bypasses the "reader scrolled away, leave
them alone" check; the routine follow path passes `false` and still honours it.
Previously each caller had to remember to set `_following = true` first — an
implicit contract that made the button's behaviour depend on caller order.

### Correction to the previous note

An earlier entry claimed the transcript was loading newest-first and that a
missing `OrderBy` was the cause. That was **wrong**: `KindleHubApiClient`
already sorts ascending before returning (`results.Sort(Function(a, b)
a.Timestamp.CompareTo(b.Timestamp))`), so the ordering was always correct. The
`OrderBy` calls in the ViewModel are harmless no-ops and were left in place. The
symptom was never an ordering problem.

Verified across six scenarios: content measured late still parks at the newest
row; a message arriving while following auto-scrolls; one arriving while scrolled
up leaves the offset at 0; and "jump to present" works whether or not layout has
settled.

## Build and Run

Build:

```bash
cd /home/suprusr124/Downloads/KindleHub-Pro-main/KindleHubClient
dotnet build KindleHub.sln -c Release
```

Build and publish Linux artifact:

```bash
cd /home/suprusr124/Downloads/KindleHub-Pro-main/KindleHubClient
./build.sh Release --publish
```

Run:

```bash
/home/suprusr124/Downloads/KindleHub-Pro-main/KindleHubClient/artifacts/Release/linux/KindleHub
```

## Validation Status

_(Superseded by the UI Redo Handoff at the top of this file — kept for the record.)_

- `dotnet build KindleHub.sln -c Release`: **0 errors, 0 warnings**.
- Release publish: succeeds.
- Headless self-test: **379 assertions, 0 failures**, stable across 10+ consecutive
  runs. Exits non-zero on failure. Run it after every change.
- Live catalogue GET: succeeds with HTTP 200.
- Live app download GET: succeeds with HTTP 200.
- Live account mutation/login testing was **not** performed — no test account was
  ever created. The real relay, real mail and real score posting are therefore
  unverified end-to-end; Connect 4 was proven only against the offline loopback.
- The UI has **not** been visually reviewed. See "Honest gaps" in the top section.

## Important Files

- `KindleHubClient/KindleHub.Core/Api/KindleHubApiClient.vb`
- `KindleHubClient/KindleHub.Core/KindleHubCore.vb`
- `KindleHubClient/KindleHub.Core/Crypto/Crypto.vb`
- `KindleHubClient/KindleHub.Core/Crypto/ChatEncryption.vb`
- `KindleHubClient/KindleHub.Core/Crypto/ChatMedia.vb`
- `KindleHubClient/KindleHub.Core/Crypto/RelayRooms.vb`
- `KindleHubClient/KindleHub.Core/Models/AccountState.vb`
- `KindleHubClient/KindleHub.Core/Models/DomainModels.vb`
- `KindleHubClient/KindleHub.Client/ViewModels/AppStoreViewModel.cs`
- `KindleHubClient/KindleHub.Client/Views/AppStoreView.axaml`
- `KindleHubClient/KindleHub.Client/Views/AppStoreView.axaml.cs`
- `KindleHubClient/KindleHub.Client/ViewModels/SettingsViewModel.cs`
- `KindleHubClient/KindleHub.Client/ViewModels/MessagesViewModel.cs`
- `KindleHubClient/KindleHub.Client/ViewModels/GamesViewModel.cs`
- `KindleHubClient/KindleHub.Client/MainWindow.axaml`

### Added since (UI work — start here instead)

- `KindleHubClient/KindleHub.Client/AppTheme.cs` — Light/Dark/Sepia
- `KindleHubClient/KindleHub.Client/Views/ArcadeView.axaml` — the game catalogue
- `KindleHubClient/KindleHub.Client/Views/GameWindow.axaml` — playing a game, its own window
- `KindleHubClient/KindleHub.Client/ViewModels/ArcadeViewModel.cs` — catalogue + current game
- `KindleHubClient/KindleHub.Client/Games/IGame.cs` — the engine contract
- `KindleHubClient/KindleHub.Client/Games/GameRegistry.cs` — the game list
- `KindleHubClient/KindleHub.Client/Games/C4Rules.cs` — Connect 4 rules, shared offline/online
- `KindleHubClient/KindleHub.Client/Games/Connect4Session.cs` — Connect 4 over the relay + offline debug pair
- `KindleHubClient/KindleHub.Client/ViewModels/MailViewModel.cs`
- `KindleHubClient/KindleHub.Client/Views/MailView.axaml`
- `KindleHubClient/KindleHub.Client.Gametest/` — the headless self-test

## Chat Scroll Behaviour — CURRENT STATE (unresolved, 2026-09-28)

The three earlier "Chat Scroll Behaviour (completed)" sections in this file are
**stale and contradictory** — they describe iterations that were superseded. Treat
them as history, not as the current implementation. The current code is the
groupchat-style rewrite below, and **it is still broken**.

### What the current code does (in `MessagesView.axaml.cs`)

- `ChatScroll` has `IsDeferredScrollingEnabled="False"` and
  `IsScrollInertiaEnabled="False"` (instantaneous moves).
- `_following` / `_missed` / `StickThreshold = 16` track whether the reader is at
  the bottom and how many messages arrived while they were away.
- `JumpToBottom(force)` captures `chase = force || IsAtBottom` **before** the
  mutation, then defers the actual move to `DispatcherPriority.Render` so
  `ChatScroll.Extent` reflects the *current* content instead of the stale
  pre-mutation extent. No latch, no retry loop — every call just writes the
  current bottom.
- `_selfScrolling` is held true *through* the `Offset` write so `ScrollChanged`
  cannot mistake our own programmatic move for the reader scrolling away.
- A "Jump to present" pill overlays the chat (the `Panel` change in
  `MessagesView.axaml`); `JumpLatest_Click` calls `JumpToBottom(force: true)`.

### The unfixed bug — follow-the-conversation stops working (UPDATE: FIXED BY ANOTHER AGENT)

Verified at runtime against the live chat (120+ message backlog, real incoming
traffic) with `Console.WriteLine` debug probes writing to the published app's
stdout. The symptom:

- The **force** path works: opening a room, sending a message, and clicking
  "Jump to present" all land `Offset.Y == maxOffsetY` exactly.
- The **follow** path fires once and then **stops**. After the first
  `JumpToBottom(force=False, wasAtBottom=True)`, `OnScrollRequested` is never
  invoked again despite a continuous flood of incoming messages, and
  `Offset.Y` stays frozen while `maxOffsetY` grows.

Root cause identified but **not yet fixed**: `_selfScrolling` was being cleared
*before* the `Offset` write inside the `Post` callback, so when `ScrollChanged`
fired from our own programmatic move it saw `_selfScrolling == false`, treated
the move as a user scroll, and set `_following = false` — killing follow. The
ordering was corrected (`_selfScrolling = false` now runs *after* the write),
but the follow path was **not re-verified** after that fix, and the app was left
unattended mid-test.

### How to reproduce

1. `cd KindleHubClient && ./build.sh Release --publish` (delete `artifacts` first).
2. Run `./artifacts/Release/linux/KindleHub` under the live X display
   (`DISPLAY=:1` on this machine). It takes 1–3 minutes to authenticate and open
   the chat — do not assume a crash if output is quiet.
3. Open the main public room. The view should pin to the newest message.
4. Scroll up a little, then let messages arrive. The view should chase the bottom.
   Currently it does not.

### Reference implementation

`https://github.com/zemendaniel/groupchat` — `groupchat.gui/MainWindow.axaml.cs`,
`AddMessage`. Its pattern (capture `isAtBottom` before the mutation, add the row,
then `Dispatcher.UIThread.Post(() => Offset = Extent.Height - Viewport.Height)`)
is what the current KindleHub rewrite is based on. It has no latch, no retry
loop, and no `_selfScrolling` flag at all — it just always writes the current
bottom on the next render pass. Consider porting that exact simplicity rather
than debugging the state machine further.

### Debugging notes for the next model

- The published app's stdout is the reliable channel for scroll state. Add
  `Console.WriteLine($"[KH-SCROLL] ...")` probes in `JumpToBottom` and in the
  `LayoutUpdated`/`ScrollChanged` handlers; `strings`/`grep` on the compiled
  `.dll` is **unreliable** for confirming the code is present — the XAML-compiled
  strings embed differently. Always verify by running the app and grepping
  stdout.
- `DISPLAY=:1` is the live display on this machine; `xvfb-run` is not installed
  and there is no headless Avalonia harness.
- `ydotool` is installed and its daemon can be started with `nohup ydotool
  daemon` if you need to drive the UI programmatically.
- The chat takes a while to load because of the auth handshake; a stale
  `KindleHub.Client` process from a previous run can hold a lock and make the
  new one appear hung. `pkill -f KindleHub.Client` before each run.
