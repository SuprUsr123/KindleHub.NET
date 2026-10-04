# KindleHub Pro API Reference

This document describes the service contract used by the official web client and
the desktop client. It is an API reference: endpoint paths, table fields, request
shapes, authentication, encryption, and interoperable wire formats are the source
of truth. The official browser implementation is in `../AppFetch/kindlehub.pro/kh-app.js`
and its view modules; the desktop REST adapter is
`KindleHub.Core/Api/KindleHubApiClient.vb`.

## Service and request conventions

| Property | Value |
|---|---|
| API gateway | `https://kindlehub-api.arancool3000.workers.dev` |
| Data API | Supabase-compatible REST endpoints under `/rest/v1/` |
| JSON | UTF-8 JSON; table and field names are snake_case |
| Authenticated write header | `X-KH-Secret: <64-character lowercase account token>` |
| Conflict upsert | `?on_conflict=<column>` with `Prefer: resolution=merge-duplicates` |

The gateway enforces its own filter and secret rules. A successful request to a
Supabase-shaped URL does not imply that an unrestricted table read or write is
allowed. Reads generally use `select=...`, an exact or bounded filter, ordering,
and a capped `limit`. The desktop client centralizes request construction and
response parsing in `KindleHubApiClient`.

## Authentication and encrypted account state

### Account identity

The web and desktop clients derive the same account token locally:

```text
canonical = lowercase(canonicalize(username))
authToken = SHA-256(UTF-8("kh::" + canonical + "::" + password))
userId    = lowercase(authToken[0..16])
```

The password is case-sensitive and is not sent to the API. The 64-character
token is the account proof for writes and the key material used by account
encryption. Treat it as a credential; never log it or include it in a public
request URL.

### `kh_users`

| Column | Meaning |
|---|---|
| `hash` | Account token; unique account key |
| `email` | Canonical username |
| `state` | Encrypted account-state envelope |
| `updated_at` | Last successful account sync |

| Operation | Request |
|---|---|
| Register | `POST /rest/v1/kh_users?on_conflict=hash`; body `{hash,email,state,updated_at}` |
| Login/restore | `GET /rest/v1/kh_users?hash=eq.<token>&select=state,updated_at` |
| Name availability | `GET /rest/v1/kh_users?email=eq.<canonical>&select=email&limit=1` |
| Sync | Same upsert as register; `state` is encrypted before upload |

Account-state JSON is encrypted client-side. Unknown keys must survive edits so
newer web-client preferences are not erased by an older desktop client. Current
shared keys include `profileName`, `profileAvatar`, `fontSize`, `theme`,
`simpleMode`, `syncEnabled`, `notes`, `msgGroups`, `leftGroups`, `friends`,
`friendRequests`, and `friendReqTombs`. Friend rows use `{hash,uid,name,since,mid}`;
pending requests use `{hash,uid,name,ts,msgTs,mid}`. The official
Messages client stores joined chat rooms as `msgGroups`, an array of
`{code,name,joinedAt}` rows, and excludes rooms in `leftGroups`. Desktop room
joins use that same shape and sync it through the encrypted `kh_users.state`
account vault so the joined room list follows the account to other clients.
Global/fixed chats, inbox rooms, and multiplayer relay rooms are not user-joined
message groups and are not written to `msgGroups`.

Friend lists and requests are encrypted account-state data, not presence rows.
The desktop Friends section refreshes synced state and polls the signed-in
account's inbox for friend-request and acceptance events. Resolving requests or
removing friends records the event id in `friendReqTombs` to prevent old inbox
replays from restoring resolved state. Preserve these keys when editing other
account-state fields.

## Profile pictures and online presence

### Shared picture representation

The official client stores the profile picture at account-state key
`profileAvatar` and publishes the same value in `kh_presence.avatar`. This lets
the signed-in user sync the editable picture through `kh_users.state`, while
other clients can load it without decrypting that account state.

Avatar codes are compact, interoperable pixel art rather than image URLs:

```text
KHAV1:<background-index>.<hex-packed-1-bit-pixels>
KHAV2:<background-index>.<base64-packed-4-bit-pixels>
```

Both versions use a 22 × 22 grid (484 pixels) and a background palette index in
`0..11`. `KHAV1` packs four 1-bit pixels into each hexadecimal digit, least
significant bit first. `KHAV2` packs two 4-bit palette indices into each byte,
high nibble first; the 16 ink colors are shared with the web client's avatar
palette. Empty/zero pixels show the selected background. Clients should validate
the prefix, background index, encoded length, and decoded palette values before
rendering. The desktop codec is `KindleHub.Client/Models/ProfileAvatar.cs`.

### `kh_presence`

| Column | Meaning |
|---|---|
| `user_id` | 16-character account id; unique presence row |
| `display_name` | Name shown in online lists |
| `last_seen` | ISO timestamp refreshed by heartbeat |
| `avatar` | `KHAV1`/`KHAV2` string or empty string |
| `profile` | Optional public profile JSON (`p`, `h`, `b`, `s`, `f`, `j`, `fr`, `g`, etc.) |

| Operation | Request |
|---|---|
| Heartbeat | Upsert `POST /rest/v1/kh_presence?on_conflict=user_id` with `{user_id,display_name,last_seen,avatar}` |
| Online list | `GET /rest/v1/kh_presence?last_seen=gt.<ISO>&select=user_id,display_name,last_seen,avatar&order=last_seen.desc&limit=N` |
| Batch pictures | `GET /rest/v1/kh_presence?user_id=in.(<id>,...)&select=user_id,avatar&limit=N` |

The official client refreshes presence periodically and caches avatar codes by
user id. The desktop heartbeat must preserve the account's current `profileAvatar`
value; sending an empty `avatar` on each ping erases the picture other clients
would see. The optional `profile` column is a public-profile JSON blob: for
example, `g` is a game-stat line, not a room code. The desktop heartbeat omits
`profile` so an upsert leaves web-client profile data intact. Missing/invalid
avatar data falls back to an initial; it must not make the player row disappear.

The desktop Messages view resolves message authors through this same batch
lookup, keyed by each message's `user_id`; chat message payloads do not include
avatar snapshots. The client caches codes for the active session and falls back
to the sender's initial when a presence row or valid picture is unavailable.
The desktop polls the active room every two seconds and keeps a bottom-follow
anchor as messages, reply previews, and images change transcript height. It
stops following when the reader scrolls away. Replies are independent message
rows with `reply_to` pointing to the parent id, show a quoted preview, and are
not visually grouped into the preceding message. Consecutive non-reply messages
from one author may share a sender header for five minutes; local-date dividers
are presentation only.

## Scores and leaderboards

### `kh_scores`

| Column | Meaning |
|---|---|
| `id` | Unique score submission id |
| `game` | Canonical game slug |
| `score` | Non-negative integer score |
| `display_name` | Name captured when submitted |
| `user_id` | 16-character account id |
| `date` | ISO submission timestamp |

Submit with `POST /rest/v1/kh_scores` and `X-KH-Secret`. Read a game's public
leaderboard with
`GET /rest/v1/kh_scores?game=eq.<slug>&order=score.desc&limit=N&select=id,score,display_name,date,user_id`.
Profile pictures are joined separately through `kh_presence` using the returned
`user_id` values; old score rows do not contain an avatar snapshot.

Single-player records may be submitted through the leaderboard API. Online
crossplay matches use room state and are not personal score submissions.

## Social rooms, messages, and multiplayer

### `kh_groups`

Groups/rooms are addressed by `code`. Common code families are `DM:*` for direct
messages, the global-chat code for public chat, `800000` plus the account id for
the user's inbox, and `mp-{game}-{id}` for multiplayer rooms. Topic groups use a
`TOPIC: ` name prefix. Group creation is an authenticated upsert/insert; lookups
and lists request only the relevant codes or bounded directory rows.

### `kh_messages`

| Column | Meaning |
|---|---|
| `id` | Message id |
| `group_code` | Containing room |
| `user_id` | Sender id |
| `display_name` | Sender name |
| `text` | Encrypted message or a recognized `KH*` media marker |
| `ts` | ISO timestamp |
| `reactions` | JSON reaction counts |
| `device_hint`, `location_hint` | Moderation hints where supplied |

Normal message text is encrypted client-side before it is written. Bounded
message reads include the caller's `X-KH-Secret` and the required exact
`group_code` filter. The desktop wire envelopes are `enc1:<iv>.<ciphertext>.<tag>` for AES-GCM and `enc2:` for
gzip-compressed AES-GCM payloads. Room keys and message keys are client-side;
the service relays ciphertext and room metadata.

Multiplayer actions are JSON event bodies sent as encrypted room messages. The
room transports membership and state; each game defines its own event types and
turn validation. Common crossplay game slugs include `ttt`, `connect4`,
`dotsboxes`, and `reversi`. Match results do not go to `kh_scores`.

### Other social operations

Reactions, edits, unsends, reports, and invitation events are authenticated
operations over `kh_messages` or worker-supported report routes. Invitation
notifications are delivered to the recipient's inbox room. Direct messages and
community topics use the same room/message transport; clients must preserve the
server's room filter and owner-secret requirements for mutations.

## Mail

### `kh_mail`

| Column | Meaning |
|---|---|
| `id` | Mail id |
| `to_user`, `from_user` | Canonical account names |
| `from_id` | Sender's 16-character account id |
| `subject`, `body` | Encrypted client-side with the shared mail key |
| `ts` | ISO timestamp |
| `reply_to` | Optional parent message id |
| `owner_secret` | Sender token used for sender-side unsend |

Inbox and Sent share one bounded read. The gateway constrains authenticated
reads to mail belonging to the caller; the client separates Inbox/Sent locally.
The official and desktop clients derive the encryption key from the normalized
recipient name, so sender and recipient can decrypt the same subject and body.
Unsend is an owner-secret-gated delete.

## App Store

| Operation | Request |
|---|---|
| Catalogue | `GET /rest/v1/kh_store_apps?select=...&order=downloads.desc&limit=N` |
| Download | `GET /rest/v1/kh_store_apps?id=eq.<id>&select=id,name,cat,author,html&limit=1` |
| Publish | `POST /rest/v1/kh_store_apps` with owner secret; includes app metadata and HTML |
| Count download | `POST /rest/v1/kh_store_download` |
| Unpublish | `DELETE /rest/v1/kh_store_apps?id=eq.<id>` with owner secret |

Public listing is limited to approved apps. Authors may fetch their own pending
app when the matching `owner_secret` is supplied. HTML is run locally after the
download; it is not executed by the API.

The desktop App Store is a native Avalonia surface for catalogue search,
category filters, app metadata, downloads, publishing, and local app management.
Downloaded HTML is stored under the user's application-data
`KindleHubPro/apps` directory and launched through the operating-system browser;
the desktop client does not embed app content in a webview. A standalone shim
provides app-scoped local storage and a restrictive content security policy.

Portable app codes use the official `KHAPP1:` prefix and base64-encoded UTF-8
JSON containing `{label,color,icon,html}`. The native Import/Share dialog accepts
these codes, enforces the 512 KiB HTML limit, stores imported apps locally, and
can export a downloaded app in the same format. This is a device-local transfer,
not a catalogue publish. Publishing accepts an HTML file or clipboard content
and uses the shared store API.

## Desktop API mapping

| Layer | Responsibility |
|---|---|
| `KindleHubApiClient.vb` | REST paths, headers, serialization, filters, and table-row parsing |
| `KindleHubCore.vb` | Authenticated account context, encrypted state, and high-level API methods |
| `AccountState.vb` | Safe JSON updates that retain unknown web-client state keys |
| `LeaderboardViewModel.cs` | Loads scores and presence, then resolves leaderboard avatar codes in one bounded batch |
| `SettingsViewModel.cs` | Edits the shared pixel grid and stores its official avatar code under `profileAvatar` |
| `ProfileAvatar.cs` | Validates, encodes, and renders the shared KHAV1/KHAV2 formats |
| `CommunityViewModel.cs` | Loads/syncs friend rows and processes inbox friend events |
| `MessagesViewModel.cs` | Polls active rooms and sends replies as separate `reply_to` messages |
| `AppStoreViewModel.cs` | Native catalogue, downloads, publishing, and KHAPP1 transfer |

When changing an API contract, update this reference and the official-client
interop behavior together. Keep requests bounded, do not weaken gateway filters,
and do not replace encrypted account or message data with client-only formats.
