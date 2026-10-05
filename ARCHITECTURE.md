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

## Interoperability guide for independent clients

This section is intended for a client written in another language. It describes
the requests currently made by `KindleHubApiClient.vb`, including the worker RPCs
that are not ordinary table CRUD. The gateway is authoritative: it applies
access rules beyond PostgREST syntax, so do not assume that adding `select=*`,
omitting a room filter, or changing a secret header will work. The desktop
adapter is the most complete executable reference; the official browser code
remains the compatibility reference for shared formats.

### Base URL, headers, filters, and errors

Use `https://kindlehub-api.arancool3000.workers.dev` as the base URL. Paths in
the tables below are relative to it. Send UTF-8 JSON with
`Content-Type: application/json` on writes. Use `Accept: application/json` on
reads. Do not send a Supabase `apikey` or bearer token unless the gateway is
updated to require one; this client proves write identity with `X-KH-Secret`.

| Header | Use |
|---|---|
| `X-KH-Secret: <64 lowercase hex>` | Account-authenticated writes and protected reads. It is the derived account token; keep it secret. |
| `Prefer: return=representation` | Inserts where the caller wants the inserted row back. |
| `Prefer: resolution=merge-duplicates,return=representation` | Account and presence upserts. The conflict key must be explicit in the URL. |
| `Prefer: return=minimal` | Writes/RPCs where a response row is unnecessary. |

REST filters use PostgREST forms: `eq.<value>`, `gt.<value>`, `gte.<value>`,
`ilike.<pattern>`, and `in.(<a>,<b>)`. URL-encode each user-supplied scalar;
encode a value before inserting it into a filter. A `select` list is a
comma-separated list of column names. The gateway requires bounded reads and
an exact room or row filter for chat; unfiltered scans are rejected. Clamp
limits to the ranges listed below. All timestamps are UTC ISO-8601 strings.

Successful reads normally return a JSON array, even for a single row. RPCs
return JSON objects. Inserts configured with `return=minimal` usually return an
empty body. A failed request is an HTTP error; the desktop adapter raises an
exception for failed GET/POST calls, returns `false` for failed PATCH/DELETE
mutations, and logs at most a short response excerpt. Treat `401`/`403` as
authentication or policy failures, `404`/empty arrays as absent rows, `409` as
a unique-key conflict, `429` as a rate limit, and `5xx` as a transient service
failure. Do not retry a non-idempotent insert blindly: use a stable client
message id where the schema permits it, or read back the object first.

### Authentication, identity, and encrypted state

The account token is computed locally; there is no password submission or
server-issued bearer token:

```text
canonical = trim(username)
if lowercase(canonical) == "aran": canonical = "arancool3000"
canonical = lowercase(canonical)
authToken = lowercase_hex(SHA256(UTF8("kh::" + canonical + "::" + password)))
userId = authToken[0:16]
```

Only the username is trimmed/canonicalized. Password casing and whitespace are
significant. The token is both the `kh_users.hash` lookup key and a 32-byte
AES-256 key (hex decode the token). Never put it in a URL, log, crash report,
or UI. `userId` is a public 16-character lowercase prefix, not a write secret.

`kh_users.state` is an encrypted string, not a JSON object. Its current packed
form is `base64(iv) + "." + base64(ciphertext || tag)`, using a random 12-byte
IV, AES-256-GCM with a 16-byte tag, and the raw 32 bytes from `authToken` as
the key. Plaintext starts with a one-byte format flag: ASCII `G` (`0x47`) means
gzip of dictionary-compressed UTF-8 JSON; ASCII `c` (`0x63`) means uncompressed
dictionary-compressed UTF-8 JSON; legacy ASCII `g` (`0x67`) means gzip without
the dictionary pass. The dictionary is the ordered shared table in
`KindleHub.Core/Crypto/KindleHubCompressionDictionary.vb`; each matched entry
is encoded as byte `0x01` followed by its zero-based dictionary index as one
character. Decompression is a single left-to-right pass. Account-state formats
are versioned implicitly by this flag and the shared dictionary, so preserve
unknown JSON properties and do not reserialize state into a different wire
format without compatibility testing.

The account-state object is client-owned and evolves. Current keys include
`profileName`, `profileAvatar`, `fontSize`, `theme`, `simpleMode`, `syncEnabled`,
`notes`, `msgGroups`, `leftGroups`, `friends`, `friendRequests`, and
`friendReqTombs`. A safe edit is read/decrypt → modify only the intended keys →
encrypt with a new IV → upsert. Never replace the whole object with a partial
settings model. Room/friend list changes should be merged carefully if multiple
devices can write concurrently; the API does not expose a field-level merge.

### Endpoint index

The desktop adapter currently calls these resources:

| Resource | HTTP method and path | Purpose |
|---|---|---|
| Account | `GET/POST /rest/v1/kh_users` | Existence, login, register, encrypted state sync |
| Groups | `GET/POST /rest/v1/kh_groups` | Read/create one room or fetch known room codes |
| Messages | `GET/POST/PATCH/DELETE /rest/v1/kh_messages` | Chat, room events, message mutations, reply parent reads |
| Reaction RPC | `POST /rest/v1/rpc/kh_set_reaction` | Atomic reaction toggle |
| App namespace RPCs | `POST /rest/v1/rpc/kh_app_list`, `kh_app_put` | Topic directory and per-account app/cloud-save key-value data |
| Recovery RPC | `POST /rest/v1/rpc/kh_recovery_set` | Set recovery email |
| Moderator RPCs | `POST /rest/v1/rpc/kh_mod_stats`, `kh_mod_claim` | Aggregate stats and moderator-code claim |
| Store | `GET/POST/DELETE /rest/v1/kh_store_apps` | Catalogue, app HTML, publish, unpublish |
| Download count RPC | `POST /rest/v1/rpc/kh_store_download` | Best-effort app install count |
| Scores | `GET/POST /rest/v1/kh_scores` | Public scores and signed-in submissions |
| Mail | `GET/POST/DELETE /rest/v1/kh_mail` | Encrypted inbox/sent mail and sender unsend |
| Presence | `GET/POST /rest/v1/kh_presence` | Heartbeats, active users, avatar and public-profile lookup |
| Feedback | `POST /rest/v1/kh_feedback` | Message/name reports and moderator applications |

The desktop `GetServerStatusAsync` is local and synthetic: it returns
`{online:true,version:"1.0"}` without contacting an endpoint. A real client
should establish availability with a normal bounded request and report network
failure separately from an empty result.

## Endpoint reference

### Accounts: `kh_users`

#### Register or sync state — `POST /rest/v1/kh_users?on_conflict=hash`

Use `X-KH-Secret` and `Prefer: resolution=merge-duplicates,return=representation`.
The body is `{hash,email,state,updated_at}`. `hash` is the 64-character token,
`email` is the lowercased canonical username, `state` is the encrypted account
envelope, and `updated_at` is a UTC timestamp. Registration uses encrypted `{}`
as the initial state. Sync must reject an empty state rather than overwrite an
existing account with blank data. A username conflict can return `409`.

```http
POST /rest/v1/kh_users?on_conflict=hash
X-KH-Secret: <authToken>
Prefer: resolution=merge-duplicates,return=representation
Content-Type: application/json

{"hash":"<64-hex>","email":"reader","state":"<iv>.<ciphertext-and-tag>","updated_at":"2026-10-05T12:00:00Z"}
```

#### Login/restore — `GET /rest/v1/kh_users?hash=eq.<token>&select=state,updated_at`

There is no plaintext-password endpoint. Derive the token locally and query by
that exact hash. Decrypt `state`; no row means either the username or password
is wrong, so a second bounded query may disambiguate:
`GET /rest/v1/kh_users?email=eq.<canonical>&select=email&limit=1`. This second
lookup reveals whether the name exists, not its state. An account row with an
empty state must be reported as an unusable/unsynced account rather than
silently treated as a new account.

#### Name existence/search

`CheckUserExists` uses the exact-email query above with `limit=1`. Friend search
uses `GET /rest/v1/kh_users?email=ilike.*<term>*&select=hash,email&limit=20`;
terms shorter than two characters are ignored and `%`/`_` are stripped before
building the pattern. The API returns each match as `{hash:<first 16 hex>,
name:<email prefix before @>}`. The full hash must never be shown to another
user. The search endpoint is account-directory data; apply throttling and avoid
repeated broad queries in a UI.

### Groups and room directory: `kh_groups`

#### Look up a room

`GET /rest/v1/kh_groups?code=eq.<encoded-code>&select=code,name,creator,created_at`
returns zero or one group. Group rows have `code`, `name`, `creator`,
`created_at` and may also carry server-maintained fields such as `updated_at`.
Room codes are identifiers, not authorization secrets; the gateway determines
what subsequent reads/writes are allowed.

#### Create a room

`POST /rest/v1/kh_groups` with `X-KH-Secret` and `Prefer: return=representation`.
Body: `{code,name,creator,created_at,updated_at}`. Use a cryptographically
random room code if the service has no allocation RPC. The desktop generates a
12-digit code. A create is an insert, not an upsert: do not use
`on_conflict=code` to claim another user's existing room. On `409`, look up the
existing row and continue only if the intended flow permits joining it.

#### Fetch known rooms in batches

`GET /rest/v1/kh_groups?code=in.(<code1>,<code2>)&select=code,name,creator,created_at&order=created_at.desc&limit=N`.
Only query codes the client already knows from account state, room invites, or
fixed-room configuration. The implementation batches at most 40 codes per
request. A general directory scan is intentionally not used for groups.

#### Topic directory and topic creation

The gateway blocks a blind `kh_groups` scan. Discover topics through
`POST /rest/v1/rpc/kh_app_list` with
`{"p_hash":"<authToken>","p_app":"khtopics","p_room":"index","p_limit":N}`.
The response is an object containing `rows`; each row has `k` (room code), `v`
(a JSON string), `at`, and `by`. Parse `v` as JSON; active topic entries contain
`n` (room name beginning `TOPIC: `), optional `a` (creator), and `t` (creation
time in Unix milliseconds). Rows with `x:true`, malformed values, or no `n`
are not active listings.

Creating a topic is two steps: create its `kh_groups` row, then publish its
directory entry via `POST /rest/v1/rpc/kh_app_put` with
`{"p_hash":"<authToken>","p_app":"khtopics","p_room":"index","p_key":"<room-code>","p_value":"<JSON string>"}`.
The `p_value` JSON currently uses `{n:<topic room name>,t:<unix-ms>,a:<display name>}`.
If publishing fails, the room may exist but will not appear in topic discovery.

### Messages, replies, and mutations: `kh_messages`

#### Send message — `POST /rest/v1/kh_messages`

Authenticated insert (`X-KH-Secret`, `Prefer: return=representation`). Fields:

| Field | Meaning |
|---|---|
| `id` | Client-generated unique message id. |
| `group_code` | Exact room code. |
| `user_id` | Usually first 16 characters of the auth token. |
| `display_name` | Sender name, max 40 characters in the desktop client. |
| `text` | `enc1:`/`enc2:` ciphertext for normal text and relay JSON; some media uses documented `KH*` markers. |
| `ts` | UTC timestamp. |
| `edited`, `important` | Boolean flags. |
| `reply_to` | Optional parent message id. Replies are separate rows. |
| `owner_secret` | Random per-message secret used for author-only mutations. |

The response may be suppressed by gateway `return=minimal` behavior; retain
your locally generated id and owner secret. Keep the per-message owner secret
locally (secure storage); it is not the account token. Without it the sender
cannot edit, unsend, or change the important flag later.

#### Encryption and read — `GET /rest/v1/kh_messages?...`

Always include an exact room filter, order, a bounded limit, and explicit
columns. The desktop requests:

```text
GET /rest/v1/kh_messages?group_code=eq.<code>&order=ts.desc&limit=100
  &select=id,group_code,user_id,display_name,text,ts,edited,important,
          reactions,reply_to,device_hint,location_hint
```

The adapter caps `limit` at 200. Optional paging filters are `offset=N`,
`id=gt.<id>`, and `ts=gte.<ISO timestamp>`. Since the result is newest-first,
reverse it after parsing to display oldest-first. The API does not provide a
durable cursor contract; clients should deduplicate by `id` when polling.

Normal message text is AES-GCM encrypted on the client. Derive the room key as
`SHA256(UTF8("khmsg::" + groupCode))`; use a random 12-byte IV and 16-byte GCM
tag. Compress with gzip only when it makes the UTF-8 payload smaller. The
serialized form is `enc1:<base64(iv)>.<base64(ciphertext || tag)>` for raw bytes
and `enc2:<base64(iv)>.<base64(ciphertext || tag)>` for gzip bytes. There is no
additional associated data in the current implementation. A decrypt failure
must not crash the transcript; retain an undecryptable placeholder. `KH*`
markers may represent structured images, stickers, polls, stories, or flipbooks;
use the media parsers/format descriptions in `KindleHub.Core/Crypto/ChatMedia.vb`
before assuming every decrypted string is plain prose.

Reaction JSON returned by the gateway maps emoji keys to arrays of 16-character
user ids. Counts are array lengths. Authors, role badges, profile frames and
avatars are joined from presence data; they are not snapshots in each message.

#### Edit, delete/unsend, important flag

| Action | Request |
|---|---|
| Edit | `PATCH /rest/v1/kh_messages?id=eq.<id>` body `{"text":"<new enc1/enc2 value>","edited":true}`, header `X-KH-Secret: <owner_secret>` |
| Unsend | `DELETE /rest/v1/kh_messages?id=eq.<id>`, header `X-KH-Secret: <owner_secret>` |
| Set important | `PATCH /rest/v1/kh_messages?id=eq.<id>` body `{"important":true}`, header `X-KH-Secret: <owner_secret>` |
| Toggle reaction | `POST /rest/v1/rpc/kh_set_reaction` body `{"p_msg_id":"<id>","p_key":"<emoji>","p_user_id":"<16-char uid>"}` |

Edits use the same room-derived encryption key and must preserve the message's
room. Mutations are owner-secret gated; do not send `X-KH-Secret` with the
account token for an owner-secret operation. Reaction toggles are atomic on the
server; the RPC uses `Prefer: return=minimal`, and the desktop includes the
account token header when available.

#### Read reply parents by id

`GET /rest/v1/kh_messages?id=in.(<id1>,<id2>)&select=id,group_code,user_id,display_name,text,ts,edited,important&limit=N`.
The adapter sends at most 40 ids. Decrypt each result using its exact
`group_code`. Missing/deleted parent rows are valid; render a generic unavailable
reply preview. Replies remain separate messages with `reply_to` referencing
the parent id.

### Inbox events, DMs, friends, and game relay

There is no separate event endpoint. An event is JSON serialized, encrypted as
ordinary room text, and inserted into `kh_messages`. To send:

```json
{"type":"FRIEND_REQUEST","toHash":"<recipient 16-char hash>","fromHash":"<sender 16-char uid>","fromUserId":"<sender uid>","fromName":"Reader"}
```

Store the event in the target user's inbox room, conventionally
`800000<recipient-user-id>` (16 characters total). Poll with the normal
room-filtered messages request and decrypt using that inbox code as the room
key. Parse JSON only after decryption. Common event types used by the desktop:

| `type` | Important fields | Meaning |
|---|---|---|
| `DM_INVITE` | `code`, `name`, `fromName`, optionally `fromUserId` | Tell a recipient about a newly created direct/group room. |
| `CHAT_REQUEST` | `code`, `name`, `fromName`, `fromUserId` | Request that the recipient open a chat room. |
| `FRIEND_REQUEST` | `fromHash`, `fromUserId`, `fromName` | Add a pending friend request to local encrypted account state. |
| `FRIEND_ACCEPT` | `fromHash`, `fromUserId`, `fromName` | Add the sender to the local friends list. |
| `GAME_INVITE` | `game`, `roomShort`, `fromName` | Invite the recipient to a supported game's six-digit lobby. |

Game-specific relay events use the same transport. Send JSON in the message
`text`, with the room code as encryption context, then poll the room and accept
only events for that game's slug. Current desktop slugs are `ttt`, `connect4`,
`dotsboxes`, and `reversi`; their event schemas are implemented in
`KindleHub.Client/Games/*Session.cs`. Examples include `JOIN`, `OPEN`,
`MOVE_C4`, `C4_STATE`, `DB_MOVE`, `DB_INIT`, and `ST`. Each game must validate
turn, board dimensions, player identity, and state transitions itself: transport
delivery is not game authority.

Global chat uses code `000000000000`; Crosschat uses `000000000001`; the open
games lobby uses `800000777777`. These are fixed protocol values. A six-digit
game room suffix is not the same thing as a 12-digit chat room code. Use the
appropriate room family and event schema.

Friend relationship state is stored in encrypted account state (`friends`,
`friendRequests`, `friendReqTombs`), not a server friend table. Inbox events are
the notification channel. Deduplicate by message id, ignore tombstoned ids,
and sync changes to account state. This event convention is client protocol
behavior, not a transactional server-side friend graph.

### Presence and public profiles: `kh_presence`

#### Heartbeat/upsert

`POST /rest/v1/kh_presence?on_conflict=user_id` with upsert `Prefer` and
`X-KH-Secret`:

```json
{"user_id":"0123456789abcdef","display_name":"Reader",
 "last_seen":"2026-10-05T12:00:00Z","avatar":"KHAV2:...",
 "profile":{"p":"...","fr":"frame-id","ns":"style-id","r":"Member","pl":"Free"}}
```

`profile` must be a JSON object (or omitted), never a JSON-encoded string.
`game_room` is not a `kh_presence` column in the current adapter; game
availability is advertised through lobby room events. Preserve profile fields
you do not own; heartbeat callers should build/merge a JSON object instead of
replacing it with `null` or a string.

#### Active people and profile lookup

`GET /rest/v1/kh_presence?last_seen=gt.<UTC cutoff>&select=user_id,display_name,last_seen,avatar,profile&order=last_seen.desc&limit=N`
returns up to 100 users. Avatar batch lookup:
`GET /rest/v1/kh_presence?user_id=in.(<16-hex ids>)&select=user_id,avatar&limit=100`.
Public profile lookup uses the same filter and
`select=user_id,avatar,profile`. Batch at most 100 ids, validate each id as 16
hex chars, and cache results by `user_id` for the session. Avatar code formats
are defined above. `profile` is public JSON; current short keys include `fr`
(profile frame), `ns` (name style), `r` (role), and `pl` (plan/tier). The
complete public profile schema belongs to the official client; ignore unknown
keys and display only fields whose meaning is known.

### Scores and game discovery

#### Submit a score — `POST /rest/v1/kh_scores`

Requires a valid account token in `X-KH-Secret`. The body is
`{id,game,score,display_name,user_id,date}`. `user_id` is the first 16 token
characters; `score` is clamped to a nonnegative value (desktop maximum
999,999,999); `game` is a game slug; `date` is UTC ISO. The id should be unique
per submission. A conflict is treated as already submitted.

#### Read scores — `GET /rest/v1/kh_scores?game=eq.<slug>&order=score.desc&limit=N&select=id,score,display_name,date,user_id`

Public read, maximum 100 rows per request. Scores sort descending. Fetch
presence avatars separately using the returned `user_id`s. Known game names are
derived from up to 100 recent `game` values and current open-game lobby events;
the adapter falls back to a curated list if no score rows exist. There is no
separate game-catalog RPC in this client.

### App namespace and cloud saves

#### List keys — `POST /rest/v1/rpc/kh_app_list`

Body: `{"p_hash":"<authToken>","p_app":"<app-id>","p_room":"<room>","p_limit":N}`.
The client requires a valid 64-hex token, strips app/room values to
`[A-Za-z0-9_.-]`, caps each at 64 characters, defaults an empty room to
`main`, and clamps limit to 1–200. Response shape is `{rows:[{k,v,at,by},...]}`;
`k` is a key, `v` is the stored string value, `at` is the saved timestamp, and
`by` is the writer label. Topic discovery reuses this RPC with app `khtopics`
and room `index` (see Groups above). Cloud-save reads are user-scoped by
`p_hash`. The desktop currently exposes cloud-save listing; it does not expose
a verified general save-write method in `IKindleHubApiClient`.

#### Write namespace key — `POST /rest/v1/rpc/kh_app_put`

The verified desktop call is topic-directory publishing:
`{"p_hash":"<authToken>","p_app":"khtopics","p_room":"index","p_key":"<code>","p_value":"<JSON string>"}`.
The RPC's general key-value semantics are also used by the official app for
cloud saves, but custom clients should verify the official save schema and
limits before writing; the desktop adapter has no public generic save-write
operation to copy. Do not confuse account-state sync (`kh_users.state`) with
per-app cloud saves (`kh_app_*`).

### Recovery and moderator operations

| Operation | Request and behavior |
|---|---|
| Set recovery email | `POST /rest/v1/rpc/kh_recovery_set` body `{"p_hash":"<authToken>","p_email":"<email>"}`. Requires authenticated hash; validate a conventional email address and max 254 chars. |
| Read aggregate moderator stats | `POST /rest/v1/rpc/kh_mod_stats` body `{"p_token":"<moderator code>"}`. Response fields: `level`, `users`, `onlineNow`, `visitsToday`, `visitors7d`, `messages`, `groups`, `feedbackOpen`, optional `appsPending`. This reports counts, not personal records. |
| Claim moderator access | `POST /rest/v1/rpc/kh_mod_claim` body `{"p_code_hash":"<sha256 hex of uppercased trimmed invite>","p_name":"<display name>","p_uid":"<first 16 auth-token chars>"}`. The invite code itself is not sent to the RPC. |
| Apply to moderate | `POST /rest/v1/kh_feedback` with `type:"bug"` and a `[REPORT] [MODAPP]` text body. No secret header is added by the desktop adapter; worker validation/rate limits apply. |

`kh_mod_claim` and `kh_mod_stats` are worker RPCs, not general table reads.
Do not treat the desktop's moderator code as an account password or log it.

### App Store

#### Catalogue — `GET /rest/v1/kh_store_apps?...`

The desktop requests
`select=id,name,cat,author,model,created_at,downloads,preview,icon_art,age_rating,rating_sum,rating_count`, ordered by downloads and creation date descending. It fetches at most 200 rows, then applies category and text filtering locally. Public catalogue visibility is controlled by the worker/review state; do not assume pending rows are public.

#### Download HTML — `GET /rest/v1/kh_store_apps?id=eq.<id>&select=id,name,cat,author,html&limit=1`

Public approved rows need no secret. An author may provide their 64-hex owner
secret in `X-KH-Secret` to read their own pending row. Response fields are
`id,name,cat,author,html`; an empty result is not found/not approved for this
caller. Treat HTML as untrusted content. The desktop writes it to local storage
and launches the system browser; it does not execute downloaded HTML inside the
native process.

#### Publish/unpublish

`POST /rest/v1/kh_store_apps` uses `X-KH-Secret` and
`Prefer: return=representation`. Body fields:
`{id,name,html,cat,author,created_at,downloads,owner_secret}`. The desktop
generates ids of the form `pub_<11 hex chars>`, caps name at 60, category at
24, author at 40, starts downloads at zero, and defaults an empty category to
`Fun`. The owner secret is the supplied owner secret or, if omitted, the
account token. Successful publication may enter moderation before public
listing. Delete via
`DELETE /rest/v1/kh_store_apps?id=eq.<id>` with the same owner secret; this is
author-only.

#### Count download

`POST /rest/v1/rpc/kh_store_download` body `{"p_id":"<app-id>"}`. It is a
best-effort counter: install must still succeed if this RPC fails.

Portable app transfer codes (`KHAPP1:`) are local files/clipboard data, not API
requests. The decoded JSON is `{label,color,icon,html}` and the desktop applies
a 512 KiB HTML limit. They are not an alternate authentication or publish
format.

### Mail: `kh_mail`

#### Read Inbox and Sent — `GET /rest/v1/kh_mail?order=ts.desc&limit=200&select=...`

Requires `X-KH-Secret`. The worker scopes results to mail where the caller is
sender or recipient; do not send an arbitrary username filter. The response
columns are `id,to_user,from_user,from_id,subject,body,ts,reply_to,owner_secret`.
Inbox versus Sent is determined locally by whether `from_id` equals the
current user's id. The adapter caps reads at 200.

Normalize a mail identity by trim → lowercase → take text before first `@` →
cap at 40 characters. Both `subject` and `body` are encrypted with the chat
envelope format (`enc1`/`enc2`) but use the context string
`mail:<normalized-to_user>`; because chat key derivation prepends `khmsg::`,
the AES key is `SHA256(UTF8("khmsg::mail:" + normalizedRecipient))`. Subject is
not plaintext. Both sender and recipient can derive this legacy shared key from
the recipient name, so this is compatibility encryption, not strong
recipient-only confidentiality.

#### Send

`POST /rest/v1/kh_mail` with `X-KH-Secret`, `Prefer: return=representation`.
Body fields: `{id,to_user,from_user,from_id,subject,body,ts,reply_to,owner_secret}`.
The desktop generates an id prefixed `m_<sender-id>_`, caps subject at 160
characters and body at 8,000, stores normalized recipient and sender names,
encrypts subject/body using the recipient context above, and sets
`owner_secret` to the account token.

#### Unsend

`DELETE /rest/v1/kh_mail?id=eq.<mail-id>` with
`X-KH-Secret: <owner_secret>`. Only the sending account should be able to
delete its outgoing mail. A false/4xx result should not be represented as a
successful unsend.

### Reports: `kh_feedback`

Message and username reports are inserted with `POST /rest/v1/kh_feedback`.
The payload is `{id,type:"bug",text,votes:0,date}`. `text` is a bounded
human-readable report (maximum 1,990 characters) containing report kind,
reason, subject id/context, reporter, and optional note. Username reports can
include `[SECURITY]` when the user flags a potential account-security issue.
Moderator applications use the same table and are marked in text as
`[REPORT] [MODAPP]`. The adapter treats reports as best-effort and returns
false on failure. Do not use this endpoint for arbitrary user content or include
secrets/private message bodies beyond the compact quoted excerpt required for
the report.

## Other network resources used by the desktop client

These calls are outside the KindleHub API gateway but affect visible client
features. They do not use `X-KH-Secret`.

| Host and request | Use | Notes |
|---|---|---|
| `GET https://kindlehub.pro/frames/<frame-file>.png` | Download official profile-frame PNGs. | Only known frame ids are mapped to filenames. Desktop caches files under the app-data `KindleHubPro/profile-frames` directory and keeps decoded images in memory for the session. A network or decode failure falls back to no frame. |
| `GET https://api.imgflip.com/get_memes` | Retrieve outside meme-template catalogue. | This is Imgflip's public template list, not KindleHub content. The desktop filters/selects templates client-side and then fetches a selected image URL. |
| `GET <selected Imgflip template URL>` | Download selected template image bytes. | URL is provided by the template listing. Treat remote image bytes and metadata as untrusted; enforce timeouts and image-size limits. |
| `https://www.youtube.com/watch?v=...` | Browser launch for the virtualinsanity Easter egg. | This is an operating-system browser launch, not an API call used for app data. |

The official website also contains optional mini-apps and reader tools that
call third-party providers directly. The URL inventory in
`../AppFetch/kindlehub.pro/kh-app.js` and `kh-views.js` includes these service
families; they are not dependencies of the desktop's core protocol:

| Feature family | Referenced services/hosts (as currently present in the web source) |
|---|---|
| AI/chat/model catalogues | `api.openai.com`, `api.anthropic.com`, `generativelanguage.googleapis.com`, `openrouter.ai`, `api.mistral.ai`, `ai.kindlehub.pro`, `ai2.kindlehub.pro` |
| Translation and language | `translate.googleapis.com`, `api.mymemory.translated.net`, `api.dictionaryapi.dev`, `api.lyrics.ovh` |
| Books and reference | `gutendex.com`, `www.gutenberg.org`, `openlibrary.org`, `archive.org`, `en.wikipedia.org`, `bible-api.com`, `numbersapi.com` |
| Weather, maps, and geography | `api.open-meteo.com`, `air-quality-api.open-meteo.com`, `geocoding-api.open-meteo.com`, `nominatim.openstreetmap.org`, `overpass-api.de`, `router.project-osrm.org`, `api.rainviewer.com`, `tilecache.rainviewer.com`, `server.arcgisonline.com` |
| News and feeds | Direct RSS/news hosts, `api.rss2json.com`, `rsshub.app`, `hnrss.org`, `api.allorigins.win`, `api.codetabs.com`, `vpn.arancool3000.workers.dev`, `r.jina.ai` |
| Media, images, and discovery | `api.spotify.com`, `accounts.spotify.com`, YouTube/Invidious/Piped hosts, `image.pollinations.ai`, `images.unsplash.com`, `picsum.photos`, `images-api.nasa.gov`, `api.nasa.gov`, `api.artic.edu`, `commons.wikimedia.org` |
| Utilities | `api.pwnedpasswords.com`, `ipapi.co`, `api.adviceslip.com`, `v2.jokeapi.dev`, `www.themealdb.com`, `finance.yahoo.com`, `query1.finance.yahoo.com`, `query2.finance.yahoo.com`, `numbersapi.com` |

These mini-app integrations have provider-specific terms, credentials, rate
limits, and response formats. Their request parameters are assembled by the
web UI and are not a stable KindleHub API contract; reproduce one only when
porting that specific mini-app, and consult its provider's current documentation.
The first-party worker routes for account/mail/state and the external service
catalogue are also declared in the web `kh-app.js`; the desktop API endpoint
reference above intentionally describes the desktop adapter's verified call
shapes rather than guessing undocumented worker internals.

The checked-in official web source exposes a few additional first-party worker
calls that the desktop `IKindleHubApiClient` does not currently use:

| Worker call | Request contract visible in `kh-app.js` | Purpose and caveat |
|---|---|---|
| `POST https://email-worker.arancool3000.workers.dev/send` | Headers `Content-Type: application/json`, `X-KH-Secret: <authToken>`; body `{to,from_user,subject,body}`; JSON response may contain `error`. | Sends a recovery code email. The body contains a credential recovery secret: send only to the configured recovery address, do not log it, and do not use this endpoint as generic mail delivery. The desktop instead exposes ordinary user mail through `kh_mail`. |
| `GET <configured-state-gateway>/state?meta=1` and `GET <configured-state-gateway>/state` | `X-KH-Secret: <authToken>`; response includes metadata or `{state,updated_at}`. | Legacy/configurable encrypted account-state sync. `KH_DEFAULT_STATE_GATEWAY` is empty in the checked-in web source, so normal builds use the main `kh_users` REST path. Do not assume a default gateway is active. |
| `POST <configured-state-gateway>/state` | `Content-Type: application/json`, `X-KH-Secret: <authToken>`; body `{hash,email,state}`; response may include `updated_at`. | Legacy/configurable state write. The state string is the packed encrypted account envelope. This is a compatibility route, not a second state format. |
| `GET https://kindlehub-state.arancool3000.workers.dev/photos` | Headers `Content-Type: application/json`, `X-KH-Secret: <photo-address-secret>`, `X-KH-Account: <authToken>`; response `{photos:[...]}`. | Lists remote photo/gallery object metadata for the photo gallery feature. `photo-address-secret` is SHA-256 hex of UTF-8 `"khphoto::" + photo key`; it is not the account auth token. |
| `PUT https://kindlehub-state.arancool3000.workers.dev/photo` | Same photo headers; JSON body `{id,data}`. | Uploads a gallery item. `data` is client-sealed/encrypted and may contain a nested encrypted state payload. |
| `GET/DELETE https://kindlehub-state.arancool3000.workers.dev/photo?id=<encoded-id>` | Same photo headers. GET returns an object with `data`; DELETE removes that id. | Gallery download/delete. Remote list entries refer to ids and photo-address keys; clients can have more than one address during key rotation. |

The photo-worker routes are inferred from the checked-in website calls, not
from a published server schema. The complete envelope, ownership, migration,
key rotation, and quota behavior is in the web photo/gallery implementation;
do not implement writes based only on this summary. The desktop API currently
does not provide this gallery worker client.

Game-session polling, friend/chat invites, chat requests, and game invites do
not introduce other first-party endpoints: they use `kh_messages` in the room
families described above. Profile-frame catalog metadata and public profiles
are also separate concerns: `kh_presence.profile` chooses an id, while the PNG
bytes come from `kindlehub.pro`.

## Language-neutral examples

### Derive an account lookup token

This Python snippet shows the byte-level token derivation. It does not send or
store the password and is not a substitute for a secure password UI:

```python
import hashlib

def derive_token(username: str, password: str) -> str:
    name = username.strip()
    if name.lower() == "aran":
        name = "arancool3000"
    material = f"kh::{name.lower()}::{password}".encode("utf-8")
    return hashlib.sha256(material).hexdigest()

token = derive_token("Reader", "case-sensitive password")
user_id = token[:16]
```

Keep `token` in the platform's secret store, not preferences or logs. For
account encryption, hex-decode it to 32 bytes and follow the AES-GCM packing
format above. For a first custom client, implement public read-only endpoints
and presence first; test account encryption against known official-client
fixtures before attempting state writes.

### Generic JSON POST pattern

```python
import requests

BASE = "https://kindlehub-api.arancool3000.workers.dev"

def rpc(path, payload, token=None):
    headers = {"Accept": "application/json", "Content-Type": "application/json"}
    if token:
        headers["X-KH-Secret"] = token
    response = requests.post(BASE + path, json=payload, headers=headers, timeout=20)
    response.raise_for_status()
    return response.json() if response.content else None

rows = rpc("/rest/v1/rpc/kh_app_list", {
    "p_hash": token,
    "p_app": "khtopics",
    "p_room": "index",
    "p_limit": 50,
})
```

Always set a timeout, handle an empty successful body, and distinguish a
transport failure from a valid empty array. This helper only demonstrates HTTP
framing; every operation still needs its endpoint-specific validation and
filter rules.

## Minimal implementation recipes

The examples use shell placeholders; substitute a safely stored token and
URL-encode all untrusted values. They illustrate request shapes rather than
providing a complete crypto implementation.

### Read active people

```sh
curl -G 'https://kindlehub-api.arancool3000.workers.dev/rest/v1/kh_presence' \
  --data-urlencode 'last_seen=gt.2026-10-05T11:00:00Z' \
  --data-urlencode 'select=user_id,display_name,last_seen,avatar,profile' \
  --data-urlencode 'order=last_seen.desc' \
  --data-urlencode 'limit=50'
```

### Fetch a room's messages

```sh
curl -G 'https://kindlehub-api.arancool3000.workers.dev/rest/v1/kh_messages' \
  -H "X-KH-Secret: $KH_AUTH_TOKEN" \
  --data-urlencode 'group_code=eq.000000000000' \
  --data-urlencode 'order=ts.desc' \
  --data-urlencode 'limit=50' \
  --data-urlencode 'select=id,group_code,user_id,display_name,text,ts,edited,important,reactions,reply_to'
```

Decrypt `text` using the exact `group_code`, sort the returned rows by
timestamp ascending for display, and deduplicate by `id` between polls.

### Poll an inbox for invitations

Derive `user_id` from the first 16 token characters, compute the inbox code as
`800000` + `user_id`, fetch recent messages exactly as in the room example,
decrypt with that inbox code, and parse JSON event envelopes. Process each
message id once. This protocol uses polling; it is not a websocket subscription.

### Create and publish a topic

1. Generate a 12-digit room code and insert a `kh_groups` row with the account
   token header.
2. Build `p_value` as JSON text such as
   `{"n":"TOPIC: Example | blurb","t":1791201600000,"a":"Reader"}`.
3. Call `kh_app_put` using app `khtopics`, room `index`, and the room code as
   `p_key`.
4. Send chat rows to the new room using the room code as the encryption
   context. Topic messages use the same `kh_messages` schema as other chat.

## Compatibility checklist

- Use UTF-8, lowercase hex, UTC timestamps, and JSON object values where the
  schema expects objects (`kh_presence.profile` is a common gotcha).
- Keep account token, message owner secrets, moderator codes, and recovery data
  out of logs. The account token is a reusable credential even though it is not
  an HTTP bearer token.
- Keep every `kh_messages` read scoped to a known exact room code or known ids;
  respect maximum batch/limit sizes.
- Generate a fresh random GCM nonce for each encryption. Never reuse an IV with
  the same AES key. Include and verify the 16-byte authentication tag.
- Preserve unknown encrypted account-state JSON keys. Friends and joined-room
  state are client-side synced data, not server-managed normalized tables.
- Handle retries idempotently and deduplicate polled messages/events by id.
- Do not assume room names, ids, or short game-room codes grant access. Handle
  server `403` responses without crashing the client.
- Treat store HTML, profile metadata, room messages, game events, and all
  server-returned strings as untrusted input.

## Source map for implementers

| Concern | Reference implementation |
|---|---|
| HTTP endpoint paths, filters, row mappings, request headers | `KindleHub.Core/Api/KindleHubApiClient.vb` |
| Account context, room permissions, group/inbox helpers, high-level methods | `KindleHub.Core/KindleHubCore.vb` |
| Auth-token derivation, account-state encryption and compression | `KindleHub.Core/Crypto/Crypto.vb`, `KindleHub.Core/Crypto/KindleHubCompressionDictionary.vb` |
| Chat encryption | `KindleHub.Core/Crypto/ChatEncryption.vb` |
| Chat media markers and decoding | `KindleHub.Core/Crypto/ChatMedia.vb` |
| Room and message identifiers | `KindleHub.Core/Models/RoomCodes.vb` |
| Message event envelope model | `KindleHub.Core/Models/DomainModels.vb` |
| Game relay event types | `KindleHub.Client/Games/*Session.cs` |
| Official browser request and format behavior | `../AppFetch/kindlehub.pro/kh-app.js` and its view modules |

This document describes the adapter and shared formats present in this
repository at the time it was written. Before shipping an independent client,
compare any new protocol behavior against the current official browser code and
confirm that the gateway still accepts the documented filters and payloads.
| `CommunityViewModel.cs` | Loads/syncs friend rows and processes inbox friend events |
| `MessagesViewModel.cs` | Polls active rooms and sends replies as separate `reply_to` messages |
| `AppStoreViewModel.cs` | Native catalogue, downloads, publishing, and KHAPP1 transfer |

When changing an API contract, update this reference and the official-client
interop behavior together. Keep requests bounded, do not weaken gateway filters,
and do not replace encrypted account or message data with client-only formats.
