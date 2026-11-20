Imports System.Net.Http
Imports System.Text
Imports System.Text.Json
Imports System.Text.Json.Serialization
Imports System.Text.Json.Nodes
Imports System.Text.RegularExpressions
Imports System.Threading
Imports System.Threading.Tasks
Imports Microsoft.Extensions.Logging
Imports System.Collections.Generic
Imports System.Linq
Imports System.Security.Cryptography
Imports KindleHub.Core

Public Class KindleHubApiOptions
    Public Property BaseUrl As String = "https://kindlehub.pro/t/api"
    Public Const DirectBaseUrl As String = "https://kindlehub-api.arancool3000.workers.dev"
    Public Property Timeout As TimeSpan = TimeSpan.FromSeconds(30)
    ''' <summary>Web parity: GET reads get the shortest clock (8s), writes 20s, and
    ''' kh_users upserts 60s — matching _sbFetch's per-call timeout budget.</summary>
    Public Property ReadTimeout As TimeSpan = TimeSpan.FromSeconds(8)
    Public Property WriteTimeout As TimeSpan = TimeSpan.FromSeconds(20)
    Public Property AccountUpsertTimeout As TimeSpan = TimeSpan.FromSeconds(60)
    ''' <summary>Identify the native client honestly; no browser fingerprint headers are required.</summary>
    Public Property UserAgent As String = "KindleHub.NET/1.0"

End Class

Public Class CreateTopicRequest
    Public Property Title As String
    Public Property Blurb As String
    Public Property CategoryId As String
End Class

''' <summary>
''' Ported Rest client. The gateway emulates the Supabase REST surface but (a) requires an exact
''' filter on chat/group reads, (b) proves a caller with X-KH-Secret = account hash, and (c) stores
''' text encrypted client-side. Every method mirrors kh-app.js; multiplayer rides the same encrypted
''' chat transport with JSON bodies (so no separate relay wire format exists).
''' </summary>
Public Interface IKindleHubApiClient
    ' Auth
    Function RegisterAsync(request As RegisterRequest, cancellationToken As CancellationToken) As Task(Of AuthResult)
    Function LoginAsync(request As LoginRequest, cancellationToken As CancellationToken) As Task(Of AuthResult)
    Function SyncAccountAsync(authToken As String, email As String, stateJson As String, cancellationToken As CancellationToken) As Task(Of String)
    Function FetchAccountAsync(authToken As String, cancellationToken As CancellationToken) As Task(Of String)
    Function CheckUserExistsAsync(username As String, cancellationToken As CancellationToken) As Task(Of Boolean)
    Function GetServerStatusAsync(cancellationToken As CancellationToken) As Task(Of ServerStatus)

    ' Messages
    Function SendMessageAsync(request As SendMessageRequest, authToken As String, cancellationToken As CancellationToken) As Task(Of Message)
    Function FetchMessagesAsync(request As FetchMessagesRequest, authToken As String, cancellationToken As CancellationToken) As Task(Of List(Of Message))
    Function EditMessageAsync(groupCode As String, messageId As String, ownerSecret As String, newText As String, cancellationToken As CancellationToken) As Task(Of Boolean)
    Function UnsendMessageAsync(messageId As String, ownerSecret As String, cancellationToken As CancellationToken) As Task(Of Boolean)
    Function ToggleImportantAsync(messageId As String, ownerSecret As String, important As Boolean, cancellationToken As CancellationToken) As Task(Of Boolean)
    Function ToggleReactionAsync(messageId As String, emoji As String, userId As String, authToken As String, cancellationToken As CancellationToken) As Task(Of Boolean)

    ' Groups
    Function LookupGroupAsync(groupCode As String, cancellationToken As CancellationToken) As Task(Of Group)
    Function CreateGroupAsync(request As CreateGroupRequest, authToken As String, cancellationToken As CancellationToken) As Task(Of Group)
    Function FetchGroupsByCodesAsync(codes As IEnumerable(Of String), cancellationToken As CancellationToken) As Task(Of List(Of Group))
    Function ListTopicsAsync(authToken As String, limit As Integer, cancellationToken As CancellationToken) As Task(Of List(Of TopicListing))
    Function ListCloudSavesAsync(authToken As String, appId As String, room As String, limit As Integer, cancellationToken As CancellationToken) As Task(Of List(Of CloudSave))
    Function SetRecoveryEmailAsync(authToken As String, email As String, cancellationToken As CancellationToken) As Task
    Function FetchModeratorStatsAsync(moderatorCode As String, cancellationToken As CancellationToken) As Task(Of ModeratorStats)
    Function ClaimModeratorCodeAsync(authToken As String, displayName As String, inviteCode As String, cancellationToken As CancellationToken) As Task
    Function SubmitModeratorApplicationAsync(authToken As String, displayName As String, timeUsing As String, ageRange As String, reason As String, priorExperience As String, cancellationToken As CancellationToken) As Task
    Function CreateTopicAsync(request As CreateTopicRequest, authToken As String, displayName As String, cancellationToken As CancellationToken) As Task(Of Group)

    ' Store
    Function FetchAppsAsync(request As FetchAppsRequest, cancellationToken As CancellationToken) As Task(Of List(Of AppCatalog))
    Function DownloadAppAsync(appId As String, cancellationToken As CancellationToken) As Task(Of DownloadedApp)
    Function DownloadAppAsync(appId As String, ownerSecret As String, cancellationToken As CancellationToken) As Task(Of DownloadedApp)
    Function DownloadAppWithSecretAsync(appId As String, ownerSecret As String, cancellationToken As CancellationToken) As Task(Of DownloadedApp)
    Function CountAppDownloadAsync(appId As String, cancellationToken As CancellationToken) As Task
    Function PublishAppAsync(request As PublishAppRequest, authToken As String, ownerSecret As String, cancellationToken As CancellationToken) As Task(Of PublishedApp)

    ' Scores / presence
    Function SubmitScoreAsync(request As SubmitScoreRequest, authToken As String, cancellationToken As CancellationToken) As Task(Of Boolean)
    Function FetchScoresAsync(request As FetchScoresRequest, cancellationToken As CancellationToken) As Task(Of List(Of LeaderboardEntry))
    Function ListKnownGamesAsync(cancellationToken As CancellationToken) As Task(Of List(Of String))
    Function PingPresenceAsync(authToken As String, displayName As String, gameRoom As String, avatar As String, profile As String, cancellationToken As CancellationToken) As Task
    Function FetchPresenceAsync(minutesActive As Integer, limit As Integer, cancellationToken As CancellationToken) As Task(Of List(Of PresenceEntry))
    Function FetchAvatarCodesAsync(userIds As IEnumerable(Of String), cancellationToken As CancellationToken) As Task(Of Dictionary(Of String, String))
    Function FetchPublicProfilesAsync(userIds As IEnumerable(Of String), cancellationToken As CancellationToken) As Task(Of Dictionary(Of String, PublicProfileDetails))
    Function SearchFriendUsersAsync(query As String, authToken As String, cancellationToken As CancellationToken) As Task(Of List(Of FriendUser))

    ' Multiplayer relay (JSON envelopes riding the encrypted chat transport)
    Function SendRoomEventAsync(groupCode As String, eventJson As String, displayName As String, authToken As String, cancellationToken As CancellationToken) As Task(Of Message)
    Function PollRoomEventsAsync(groupCode As String, authToken As String, limit As Integer, cancellationToken As CancellationToken) As Task(Of List(Of RoomMessageEnvelope))

    ' Reports + DM invitations
    Function ReportMessageAsync(reason As String, note As String, msg As Message, groupName As String, reporterName As String, reporterUserId As String, cancellationToken As CancellationToken) As Task(Of Boolean)
    Function ReportUsernameAsync(reason As String, note As String, displayName As String, userId As String, securityFlag As Boolean, roomCode As String, groupName As String, msgId As String, reporterName As String, reporterUserId As String, cancellationToken As CancellationToken) As Task(Of Boolean)

    ' Reply previews (batched id read) + store unpublish (owner_secret-gated delete)
    Function FetchMessagesByIdsAsync(ids As IEnumerable(Of String), groupCode As String, authToken As String, cancellationToken As CancellationToken) As Task(Of List(Of Message))
    Function UnpublishAppAsync(appId As String, ownerSecret As String, cancellationToken As CancellationToken) As Task(Of Boolean)

    ' ── Mail ────────────────────────────────────────────────────────────────
    ' One row per message in kh_mail. The server forces every read to the caller's
    ' own mailbox (received OR sent), so the Inbox/Sent split is a client-side
    ' view of a single fetch. Both body AND subject are E2E-encrypted under
    ' SHA-256("khmsg::mail:" + normalized recipient) — same scheme as the web client.
    Function FetchMailAsync(authToken As String, cancellationToken As CancellationToken) As Task(Of List(Of MailItem))
    Function SendMailAsync(request As SendMailRequest, authToken As String, cancellationToken As CancellationToken) As Task(Of MailItem)
    ' Unsend: only the sending device can remove its own mail (owner_secret gate).
    Function DeleteMailAsync(mailId As String, ownerSecret As String, cancellationToken As CancellationToken) As Task(Of Boolean)

    ' ── Web-build parity additions (v10.70.1) ───────────────────────────────
    ' Inbox events ride the kh_inbox_send RPC when available (p_hash/p_to/p_payload),
    ' falling back to a direct insert into the recipient's 800000xxxxxx relay room.
    Function SendInboxEventAsync(authToken As String, toUserId As String, payloadJson As String, displayName As String, cancellationToken As CancellationToken) As Task(Of Boolean)
    ' Leaderboard runs need a per-game "khr1." integrity token (kh_run_start {p_hash,p_game}).
    Function GetRunTokenAsync(authToken As String, game As String, cancellationToken As CancellationToken) As Task(Of String)
    ' Store ratings (kh_store_rate {p_hash,p_id,p_stars}) and the moderator-only
    ' pending/review pair (kh_store_pending / kh_store_review with the mod token).
    Function RateAppAsync(authToken As String, appId As String, stars As Integer, cancellationToken As CancellationToken) As Task
    Function FetchPendingStoreAppsAsync(moderatorCode As String, cancellationToken As CancellationToken) As Task(Of List(Of AppCatalog))
    Function ReviewStoreAppAsync(moderatorCode As String, appId As String, decision As String, reason As String, cancellationToken As CancellationToken) As Task
    ' Room-owner cleanup: DELETE with X-KH-Secret = account token plus X-KH-Room-Owner: 1.
    Function DeleteMessageAsRoomOwnerAsync(messageId As String, authToken As String, cancellationToken As CancellationToken) As Task(Of Boolean)
End Interface

Public Class FriendUser
    Public Property Hash As String
    Public Property Name As String
End Class

''' <summary>One mail row, body already decrypted.</summary>
Public Class MailItem
    Public Property Id As String
    Public Property ToUser As String
    Public Property FromUser As String
    Public Property FromId As String
    Public Property Subject As String
    Public Property Body As String
    Public Property Timestamp As DateTimeOffset
    Public Property ReplyTo As String
    ''' <summary>The auth token that owns this row, so Unsend can prove authorship.</summary>
    Public Property OwnerSecret As String
    Public ReadOnly Property IsMine As Boolean
        Get
            Return Not String.IsNullOrEmpty(FromId)
        End Get
    End Property
    Public ReadOnly Property TimestampFormatted As String
        Get
            Return If(Timestamp = DateTimeOffset.MinValue, "", Timestamp.LocalDateTime.ToString("d MMM HH:mm"))
        End Get
    End Property
End Class

Public Class SendMailRequest
    Public Property ToUser As String
    Public Property FromUser As String
    Public Property Subject As String
    Public Property Body As String
    Public Property ReplyTo As String
End Class

Public Class KindleHubApiClient
    Implements IKindleHubApiClient

    Public Const GlobalGroupCode As String = "000000000000"
    ''' <summary>Crosschat — the second global room the site exposes. Treated exactly
    ''' like Global Chat for scroll-window sizing and the global-chat flag.</summary>
    Public Const CrossChatGroupCode As String = "000000000001"
    Public Const OpenGamesLobby As String = "800000777777"
    Private ReadOnly Hex64 As New Regex("^[0-9a-fA-F]{64}$", RegexOptions.Compiled)

    Private ReadOnly _httpClient As HttpClient
    Private ReadOnly _options As KindleHubApiOptions
    Private ReadOnly _logger As ILogger(Of KindleHubApiClient)

    ' Web parity state: per-table GET cache, per-game score run tokens, the
    ' kh_banned_usernames set used to filter leaderboards, and the 30-second
    ' same-score dedupe window of _khSubmitScore.
    Private Class CacheEntry
        Public Property Expires As DateTimeOffset
        Public Property Rows As List(Of JsonElement)
    End Class
    Private ReadOnly _cacheLock As New Object()
    Private _cacheGeneration As Long
    Private ReadOnly _getCache As New Dictionary(Of String, CacheEntry)(StringComparer.Ordinal)
    Private ReadOnly _runTokens As New Dictionary(Of String, (Token As String, At As DateTimeOffset))(StringComparer.Ordinal)
    Private ReadOnly _publicIds As New System.Collections.Concurrent.ConcurrentDictionary(Of String, (Id As String, At As DateTimeOffset))(StringComparer.Ordinal)
    Private ReadOnly _accountEnvelopes As New System.Collections.Concurrent.ConcurrentDictionary(Of String, AccountEnvelope)(StringComparer.Ordinal)
    Private ReadOnly _scoreDedupe As New Dictionary(Of String, DateTimeOffset)(StringComparer.Ordinal)
    Private ReadOnly _banNames As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
    Private _banNamesAt As DateTimeOffset = DateTimeOffset.MinValue

    Public Sub New(httpClient As HttpClient, options As KindleHubApiOptions, logger As ILogger(Of KindleHubApiClient))
        _httpClient = httpClient
        _options = options
        _logger = logger
        _httpClient.BaseAddress = New Uri(_options.BaseUrl.TrimEnd("/"c) & "/")
        _httpClient.Timeout = _options.Timeout
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", _options.UserAgent)

    End Sub

    Private Function JsonOpts() As JsonSerializerOptions
        Static opts = New JsonSerializerOptions With {
            .PropertyNameCaseInsensitive = True,
            .DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        }
        Return opts
    End Function

    ' ───────────────────── auth (authLogin / authRegister / _saveUser) ─────────────────────
    Public Async Function RegisterAsync(request As RegisterRequest, cancellationToken As CancellationToken) As Task(Of AuthResult) Implements IKindleHubApiClient.RegisterAsync
        Dim authToken = KeyDerivation.DeriveAuthToken(request.Username, request.Password)
        Dim canonical = KeyDerivation.CanonicalizeUsername(request.Username).ToLowerInvariant()

        ' Freedom-first registration: only basic sanity on the username (length +
        ' charset); the server has the final word on names and conflicts. The
        ' password-leak check is OPT-IN (CheckPasswordLeaks) and NEVER blocks —
        ' it only produces a warning. Even when it runs, only the first 5 chars of
        ' the password's SHA-1 leave the device (HIBP k-anonymity), never the
        ' password or its full hash.
        Dim usernameProblem = ValidateUsername(canonical)
        If usernameProblem IsNot Nothing Then
            Return New AuthResult With {.Success = False, .Error = usernameProblem}
        End If
        Dim passwordWarning As String = Nothing
        If request.CheckPasswordLeaks AndAlso Await IsPasswordPwnedAsync(request.Password, cancellationToken) Then
            passwordWarning = "That password appears in public breach lists. It still works, but a more unique one is safer."
        End If

        If Await CheckHashExistsAsync(authToken, cancellationToken) Then
            Return New AuthResult With {.Success = False, .Error = "An account with this email already exists."}
        End If

        Dim packed = AccountEncryption.PackAccount("{}", authToken)
        Dim now = UtcIso()
        Dim payload = New With {
            Key .hash = authToken,
            Key .email = canonical,
            Key .state = packed,
            Key .updated_at = now
        }
        Try
            Await PostKhUsersWithRetryAsync(payload, authToken, cancellationToken)
            Return New AuthResult With {
                .Success = True,
                .Username = canonical,
                .AuthToken = authToken,
                .StateJson = "{}",
                .Adjusted = False,
                .Warning = passwordWarning
            }
        Catch ex As ServerException When ex.StatusCode = 409
            Return New AuthResult With {.Success = False, .Error = "That username is already registered."}
        End Try
    End Function

    Public Async Function LoginAsync(request As LoginRequest, cancellationToken As CancellationToken) As Task(Of AuthResult) Implements IKindleHubApiClient.LoginAsync
        Dim authToken = KeyDerivation.DeriveAuthToken(request.Username, request.Password)
        Dim canonical = KeyDerivation.CanonicalizeUsername(request.Username).ToLowerInvariant()

        ' authLogin looks itself up BY HASH (the hash encodes the password, so a wrong
        ' password simply finds no row); state is only served with a hash= filter.
        Dim rows = Await GetRowsAsync($"rest/v1/kh_users?hash=eq.{Uri.EscapeDataString(authToken)}&select=state,updated_at", cancellationToken)
        If rows IsNot Nothing AndAlso rows.Count > 0 Then
            Dim packedState = JsonStr(rows(0), "state")
            Dim updatedAt = JsonDate(rows(0), "updated_at")
            If Not String.IsNullOrEmpty(packedState) Then
                Dim decrypted As String = Nothing
                If packedState.StartsWith("KHSL1:", StringComparison.Ordinal) Then
                    Return New AuthResult With {.Success = False, .Error = "This account uses Superlock. Unlock it with your PIN in the web app; PIN unlock is not supported by this client."}
                ElseIf AccountEnvelope.IsWrapped(packedState) Then
                    Dim envelope = Await Task.Run(Function()
                                                     Dim opened As String = Nothing
                                                     Dim result = AccountEnvelope.Open(packedState, request.Password, opened)
                                                     Return (Context:=result, State:=opened)
                                                 End Function, cancellationToken)
                    decrypted = envelope.State
                    If envelope.Context IsNot Nothing Then _accountEnvelopes(authToken) = envelope.Context
                Else
                    decrypted = AccountEncryption.UnpackAccount(packedState, authToken)
                    Dim previous As AccountEnvelope = Nothing
                    _accountEnvelopes.TryRemove(authToken, previous)
                End If
                If decrypted Is Nothing Then
                    Return New AuthResult With {.Success = False, .Error = "Decryption failed, wrong password?"}
                End If
                Return New AuthResult With {
                    .Success = True,
                    .Username = canonical,
                    .AuthToken = authToken,
                    .StateJson = decrypted,
                    .ServerUpdatedAt = updatedAt,
                    .Adjusted = False
                }
            End If
            Return New AuthResult With {
                .Success = False,
                .Error = "This account is empty on the cloud copy. Open it on a signed-in device and press Sync Now, then try again."
            }
        End If

        ' No row for that hash: disambiguate with an email lookup (mirrors authLogin).
        Dim emailRows = Await GetRowsAsync($"rest/v1/kh_users?email=eq.{Uri.EscapeDataString(canonical)}&select=email&limit=1", cancellationToken)
        If emailRows IsNot Nothing AndAlso emailRows.Count > 0 Then
            Return New AuthResult With {
                .Success = False,
                .Error = "That account exists, so the username is right and it is the password being refused (passwords are case-sensitive)."
            }
        End If
        Return New AuthResult With {
            .Success = False,
            .Error = "The server answered, and there is no account under " & ChrW(&H201C) & canonical & ChrW(&H201D) & ". Check the spelling."
        }
    End Function

    Public Async Function SyncAccountAsync(authToken As String, email As String, stateJson As String, cancellationToken As CancellationToken) As Task(Of String) Implements IKindleHubApiClient.SyncAccountAsync
        If String.IsNullOrEmpty(stateJson) Then Throw New ValidationException("Refused to write an empty account. Nothing was changed.")
        ' Read the current encryption envelope before writing: another device may
        ' have enabled stronger encryption since this device last signed in.
        Dim remote = Await GetRowsAsync($"rest/v1/kh_users?hash=eq.{Uri.EscapeDataString(authToken)}&select=state", authToken, cancellationToken)
        Dim envelope As AccountEnvelope = Nothing
        Dim packed As String
        If remote.Count > 0 AndAlso AccountEnvelope.IsWrapped(JsonStr(remote(0), "state")) Then
            If Not _accountEnvelopes.TryGetValue(authToken, envelope) OrElse envelope.Unpack(JsonStr(remote(0), "state")) Is Nothing Then
                Throw New AuthenticationException("Unlock this account with your password again before syncing. The cloud copy was not changed.")
            End If
            packed = envelope.Pack(stateJson)
        Else
            If _accountEnvelopes.ContainsKey(authToken) Then Throw New AuthenticationException("The cloud account encryption changed. Sign in again before syncing.")
            packed = AccountEncryption.PackAccount(stateJson, authToken)
        End If
        Dim canonicalEmail = KeyDerivation.CanonicalizeUsername(email).ToLowerInvariant()
        Dim now = UtcIso()
        Dim payload = New With {
            Key .hash = authToken,
            Key .email = canonicalEmail,
            Key .state = packed,
            Key .updated_at = now
        }
        Try
            Await PostKhUsersWithRetryAsync(payload, authToken, cancellationToken)
        Catch ex As ServerException When ex.StatusCode = 409
            Throw New ValidationException("That username is registered to a different account. Delete that account first.")
        End Try
        Return now
    End Function

    Public Async Function FetchAccountAsync(authToken As String, cancellationToken As CancellationToken) As Task(Of String) Implements IKindleHubApiClient.FetchAccountAsync
        Dim rows = Await GetRowsAsync($"rest/v1/kh_users?hash=eq.{Uri.EscapeDataString(authToken)}&select=state,updated_at", cancellationToken)
        If rows Is Nothing OrElse rows.Count = 0 Then Return Nothing
        Dim packed = JsonStr(rows(0), "state")
        If AccountEnvelope.IsWrapped(packed) Then
            Dim envelope As AccountEnvelope = Nothing
            If Not _accountEnvelopes.TryGetValue(authToken, envelope) Then Throw New AuthenticationException("Sign in with your password to unlock this encrypted account.")
            Dim opened = envelope.Unpack(packed)
            If opened Is Nothing Then Throw New AuthenticationException("Account encryption changed. Sign in with your password again.")
            Return opened
        End If
        Return AccountEncryption.UnpackAccount(packed, authToken)
    End Function

    Public Async Function CheckUserExistsAsync(username As String, cancellationToken As CancellationToken) As Task(Of Boolean) Implements IKindleHubApiClient.CheckUserExistsAsync
        Dim canonical = KeyDerivation.CanonicalizeUsername(username).ToLowerInvariant()
        Dim rows = Await GetRowsAsync($"rest/v1/kh_users?email=eq.{Uri.EscapeDataString(canonical)}&select=email&limit=1", cancellationToken)
        Return rows IsNot Nothing AndAlso rows.Count > 0
    End Function

    Private Async Function CheckHashExistsAsync(authToken As String, cancellationToken As CancellationToken) As Task(Of Boolean)
        Dim rows = Await GetRowsAsync($"rest/v1/kh_users?hash=eq.{Uri.EscapeDataString(authToken)}&select=hash&limit=1", cancellationToken)
        Return rows IsNot Nothing AndAlso rows.Count > 0
    End Function

    Public Async Function GetServerStatusAsync(cancellationToken As CancellationToken) As Task(Of ServerStatus) Implements IKindleHubApiClient.GetServerStatusAsync
        Try
            ' Bypass the cache: a previous successful read does not prove reachability now.
            Await GetRowsCoreAsync("rest/v1/kh_scores?select=game&limit=1", Nothing, cancellationToken)
            Return New ServerStatus With {.Online = True}
        Catch ex As OperationCanceledException When cancellationToken.IsCancellationRequested
            Throw
        Catch ex As Exception
            Return New ServerStatus With {.Online = False}
        End Try
    End Function

    ' ───────────────────── chat (_groupSend / _groupFetchMessages) ─────────────────────
    Public Async Function SendMessageAsync(request As SendMessageRequest, authToken As String, cancellationToken As CancellationToken) As Task(Of Message) Implements IKindleHubApiClient.SendMessageAsync
        Dim userId = If(String.IsNullOrEmpty(request.UserId), UserIdFromToken(authToken) & "", request.UserId)
        If String.IsNullOrEmpty(userId) AndAlso Hex64.IsMatch(If(authToken, "")) Then
            userId = authToken.Substring(0, 16).ToLowerInvariant()
        End If
        Dim ownerSecret = ChatEncryption.NewSecret()
        Dim messageId = RoomCodes.NewMessageId()
        Dim wireText = request.Text
        If request.Encrypted Then wireText = ChatEncryption.Encrypt(request.GroupCode, request.Text)
        MessageSecretStore.Note(request.GroupCode, messageId, ownerSecret)

        Dim payload = New With {
            Key .id = messageId,
            Key .group_code = request.GroupCode,
            Key .user_id = userId,
            Key .display_name = TrimStr(request.DisplayName, 40),
            Key .text = wireText,
            Key .ts = UtcIso(),
            Key .edited = False,
            Key .important = request.Important,
            Key .reply_to = If(String.IsNullOrEmpty(request.ReplyTo), Nothing, request.ReplyTo),
            Key .owner_secret = ownerSecret
        }
        Await PostVoidAsync("rest/v1/kh_messages", payload, InsertHeaders(authToken), cancellationToken)

        Return New Message With {
            .Id = messageId,
            .GroupCode = request.GroupCode,
            .UserId = userId,
            .DisplayName = request.DisplayName,
            .Text = request.Text,
            .ReplyTo = request.ReplyTo,
            .Timestamp = DateTimeOffset.UtcNow,
            .Important = request.Important,
            .OwnerSecret = ownerSecret,
            .IsMine = True,
            .Reactions = New Dictionary(Of String, Integer)(),
            .ReactionEmoji = New Dictionary(Of String, List(Of String))()
        }
    End Function

    Public Async Function FetchMessagesAsync(request As FetchMessagesRequest, authToken As String, cancellationToken As CancellationToken) As Task(Of List(Of Message)) Implements IKindleHubApiClient.FetchMessagesAsync
        ' A kh_messages read must carry group_code= (or id=) — the gateway refuses blind reads.
        Dim myId = Await ResolvePublicIdAsync(authToken, cancellationToken)
        If String.IsNullOrEmpty(myId) AndAlso Hex64.IsMatch(If(authToken, "")) Then myId = authToken.Substring(0, 16).ToLowerInvariant()
        Dim limit = Math.Max(1, Math.Min(request.Limit, 200))
        Dim url = $"rest/v1/kh_messages?group_code=eq.{Uri.EscapeDataString(request.GroupCode)}&order=ts.desc&limit={limit}" &
                  $"&select=id,group_code,user_id,display_name,text,ts,edited,important,reactions,reply_to"
        If request.Offset > 0 Then url &= $"&offset={request.Offset}"
        If Not String.IsNullOrEmpty(request.AfterId) Then url &= "&id=gt." & Uri.EscapeDataString(request.AfterId)
        If request.AfterTimestamp.HasValue Then
            Dim afterTimestamp = request.AfterTimestamp.Value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", Globalization.CultureInfo.InvariantCulture)
            url &= "&ts=gte." & Uri.EscapeDataString(afterTimestamp)
        End If

        Dim rows = Await GetRowsAsync(url, authToken, cancellationToken)
        Dim results As New List(Of Message)()
        If rows Is Nothing Then Return results
        For Each row In rows
            Dim wire = JsonStr(row, "text")
            Dim plaintext = If(request.Encrypted, ChatEncryption.Decrypt(request.GroupCode, wire), wire)
            Dim ts = JsonDate(row, "ts")
            Dim msg As New Message With {
                .Id = JsonStr(row, "id"),
                .GroupCode = JsonStr(row, "group_code"),
                .UserId = JsonStr(row, "user_id"),
                .DisplayName = JsonStr(row, "display_name"),
                .Text = plaintext,
                .ReplyTo = JsonStr(row, "reply_to"),
                .Timestamp = If(ts.HasValue, ts.Value, DateTimeOffset.UtcNow),
                .Edited = JsonBool(row, "edited"),
                .Important = JsonBool(row, "important"),
                .OwnerSecret = MessageSecretStore.Lookup(JsonStr(row, "group_code"), JsonStr(row, "id")),
                .IsMine = Not String.IsNullOrEmpty(myId) AndAlso String.Equals(JsonStr(row, "user_id"), myId, StringComparison.Ordinal),
                .Reactions = New Dictionary(Of String, Integer)(),
                .ReactionEmoji = New Dictionary(Of String, List(Of String))()
            }
            If Not String.IsNullOrEmpty(msg.OwnerSecret) Then msg.IsMine = True
            If row.TryGetProperty("reactions", Nothing) AndAlso row.GetProperty("reactions").ValueKind = JsonValueKind.Object Then
                For Each prop In row.GetProperty("reactions").EnumerateObject()
                    If prop.Value.ValueKind = JsonValueKind.Array Then
                        Dim ids As New List(Of String)()
                        For Each v In prop.Value.EnumerateArray()
                            If v.ValueKind = JsonValueKind.String Then ids.Add(v.GetString())
                        Next
                        msg.ReactionEmoji(prop.Name) = ids
                        If ids.Count > 0 Then msg.Reactions(prop.Name) = ids.Count
                    End If
                Next
            End If
            results.Add(msg)
        Next
        results.Sort(Function(a, b) a.Timestamp.CompareTo(b.Timestamp))
        Return results
    End Function

    Public Async Function EditMessageAsync(groupCode As String, messageId As String, ownerSecret As String, newText As String, cancellationToken As CancellationToken) As Task(Of Boolean) Implements IKindleHubApiClient.EditMessageAsync
        If String.IsNullOrEmpty(ownerSecret) OrElse ownerSecret.Length < 16 Then Return False
        Dim wire = ChatEncryption.Encrypt(groupCode, newText)
        Return Await PatchAsync($"rest/v1/kh_messages?id=eq.{Uri.EscapeDataString(messageId)}",
                                New With {Key .text = wire, Key .edited = True}, ownerSecret, cancellationToken)
    End Function

    Public Async Function UnsendMessageAsync(messageId As String, ownerSecret As String, cancellationToken As CancellationToken) As Task(Of Boolean) Implements IKindleHubApiClient.UnsendMessageAsync
        If String.IsNullOrEmpty(ownerSecret) OrElse ownerSecret.Length < 16 Then Return False
        Dim url = $"rest/v1/kh_messages?id=eq.{Uri.EscapeDataString(messageId)}"
        Return Await DeleteAsync(url, ownerSecret, cancellationToken)
    End Function

    Public Async Function ToggleImportantAsync(messageId As String, ownerSecret As String, important As Boolean, cancellationToken As CancellationToken) As Task(Of Boolean) Implements IKindleHubApiClient.ToggleImportantAsync
        If String.IsNullOrEmpty(ownerSecret) OrElse ownerSecret.Length < 16 Then Return False ' only the sender can change this
        Return Await PatchAsync($"rest/v1/kh_messages?id=eq.{Uri.EscapeDataString(messageId)}",
                                New With {Key .important = important}, ownerSecret, cancellationToken)
    End Function

    Public Async Function ToggleReactionAsync(messageId As String, emoji As String, userId As String, authToken As String, cancellationToken As CancellationToken) As Task(Of Boolean) Implements IKindleHubApiClient.ToggleReactionAsync
        ' Server-side atomic toggle: RPC kh_set_reaction. Fails soft on rate limits.
        Dim payload = New With {
            Key .p_msg_id = messageId,
            Key .p_key = TrimStr(emoji, 20),
            Key .p_user_id = TrimStr(If(String.IsNullOrEmpty(userId), "anon", userId), 100)
        }
        Try
            Await PostVoidAsync("rest/v1/rpc/kh_set_reaction", payload, MinimalHeaders(authToken), cancellationToken)
            Return True
        Catch
            Return False
        End Try
    End Function

    ' ───────────────────── groups + topics ─────────────────────
    Public Async Function LookupGroupAsync(groupCode As String, cancellationToken As CancellationToken) As Task(Of Group) Implements IKindleHubApiClient.LookupGroupAsync
        Dim rows = Await GetRowsAsync($"rest/v1/kh_groups?code=eq.{Uri.EscapeDataString(groupCode)}&select=code,name,creator,created_at", cancellationToken)
        If rows Is Nothing OrElse rows.Count = 0 Then Return Nothing
        Return ParseGroup(rows(0))
    End Function

    Public Async Function CreateGroupAsync(request As CreateGroupRequest, authToken As String, cancellationToken As CancellationToken) As Task(Of Group) Implements IKindleHubApiClient.CreateGroupAsync
        ' _groupCreate is a plain INSERT — never on_conflict (that lets a caller re-own a room).
        If String.IsNullOrEmpty(request.Code) Then request.Code = RoomCodes.NewRoomCode()
        Dim creator = If(String.IsNullOrEmpty(request.Creator), "Reader", request.Creator)
        Dim now = UtcIso()
        Dim payload = New With {
            Key .code = request.Code,
            Key .name = TrimStr(request.Name, 220),
            Key .creator = TrimStr(creator, 40),
            Key .created_at = now,
            Key .updated_at = now
        }
        Dim alreadyExists = False
        Try
            Await PostVoidAsync("rest/v1/kh_groups", payload, InsertHeaders(authToken), cancellationToken)
        Catch ex As ServerException When ex.StatusCode = 409
            alreadyExists = True
        End Try
        If alreadyExists Then
            Dim existing = Await LookupGroupAsync(request.Code, cancellationToken)
            If existing IsNot Nothing Then Return existing
        End If
        Dim parts = TopicHelper.Parse(request.Name)
        Return New Group With {
            .Code = request.Code,
            .Name = request.Name,
            .Creator = request.Creator,
            .CreatedAt = DateTimeOffset.UtcNow,
            .TopicCategory = parts.Cat,
            .Blurb = parts.Blurb
        }
    End Function

    Public Async Function FetchGroupsByCodesAsync(codes As IEnumerable(Of String), cancellationToken As CancellationToken) As Task(Of List(Of Group)) Implements IKindleHubApiClient.FetchGroupsByCodesAsync
        Dim groups As New List(Of Group)()
        Dim clean = If(codes Is Nothing, New String() {}, codes.ToArray()).Where(Function(c) Not String.IsNullOrEmpty(c)).Distinct(StringComparer.Ordinal).ToList()
        Const chunk = 40
        For start = 0 To clean.Count - 1 Step chunk
            Dim batch = clean.GetRange(start, Math.Min(chunk, clean.Count - start))
            ' code=in.(a,b,c) is allowed (a set of exact codes, not an unfiltered scan).
            Dim inVal = "(" & String.Join(",", batch.Select(Function(c) Uri.EscapeDataString(c))) & ")"
            Dim rows = Await GetRowsAsync($"rest/v1/kh_groups?code=in.{inVal}&select=code,name,creator,created_at&order=created_at.desc&limit={batch.Count}", cancellationToken)
            If rows Is Nothing Then Continue For
            For Each row In rows
                groups.Add(ParseGroup(row))
            Next
        Next
        Return groups
    End Function

    Public Async Function ListTopicsAsync(authToken As String, limit As Integer, cancellationToken As CancellationToken) As Task(Of List(Of TopicListing)) Implements IKindleHubApiClient.ListTopicsAsync
        If Not Hex64.IsMatch(If(authToken, "")) Then Throw New AuthenticationException("Sign in to see topics.")
        ' The gateway blocks unfiltered kh_groups reads, so the official client discovers
        ' topic rooms through the shared KV directory (kh_app_list, app "khtopics").
        Dim payload = New With {
            Key .p_hash = authToken.ToLowerInvariant(),
            Key .p_app = "khtopics",
            Key .p_room = "index",
            Key .p_limit = Math.Max(1, Math.Min(limit, 200))
        }
        Dim doc = PostJsonAsync("rest/v1/rpc/kh_app_list", payload, Nothing, cancellationToken)
        Dim listings As New List(Of TopicListing)()
        Try
            Dim body = JsonDocument.Parse(Await doc)
            If Not body.RootElement.TryGetProperty("rows", Nothing) Then Return listings
            For Each r In body.RootElement.GetProperty("rows").EnumerateArray()
                If r.ValueKind <> JsonValueKind.Object Then Continue For
                Dim code = If(r.TryGetProperty("k", Nothing) AndAlso r.GetProperty("k").ValueKind = JsonValueKind.String, r.GetProperty("k").GetString(), "")
                Dim rawV = If(r.TryGetProperty("v", Nothing) AndAlso r.GetProperty("v").ValueKind = JsonValueKind.String, r.GetProperty("v").GetString(), "")
                Dim atDate = If(r.TryGetProperty("at", Nothing) AndAlso r.GetProperty("at").ValueKind = JsonValueKind.String, r.GetProperty("at").GetString(), "")
                Dim byAuthor = If(r.TryGetProperty("by", Nothing) AndAlso r.GetProperty("by").ValueKind = JsonValueKind.String, r.GetProperty("by").GetString(), "")
                code = code.Trim()
                If code.Length < 6 Then Continue For
                Try
                    Dim v = JsonDocument.Parse(rawV).RootElement
                    If v.TryGetProperty("x", Nothing) AndAlso v.GetProperty("x").ValueKind = JsonValueKind.True Then Continue For
                    If Not v.TryGetProperty("n", Nothing) Then Continue For
                    Dim name = v.GetProperty("n").GetString() & ""
                    If Not name.StartsWith(TopicHelper.TopicPrefix, StringComparison.Ordinal) Then Continue For
                    Dim creator = ""
                    If v.TryGetProperty("a", Nothing) AndAlso v.GetProperty("a").ValueKind = JsonValueKind.String Then creator = v.GetProperty("a").GetString()
                    Dim parsed As DateTimeOffset
                    If Not DateTimeOffset.TryParse(atDate, parsed) Then parsed = DateTimeOffset.UtcNow
                    listings.Add(New TopicListing With {
                        .Code = code,
                        .RawName = name,
                        .Creator = If(String.IsNullOrEmpty(creator), byAuthor, creator),
                        .CreatedAt = parsed
                    })
                Catch
                    ' Not JSON (legacy row) or malformed - skip it.
                End Try
            Next
        Catch
            Return listings
        End Try
        Return listings.OrderByDescending(Function(t) t.CreatedAt).ToList()
    End Function

    Public Async Function ListCloudSavesAsync(authToken As String, appId As String, room As String, limit As Integer, cancellationToken As CancellationToken) As Task(Of List(Of CloudSave)) Implements IKindleHubApiClient.ListCloudSavesAsync
        If Not Hex64.IsMatch(If(authToken, "")) Then Throw New AuthenticationException("Sign in to view cloud saves.")
        Dim cleanApp = Regex.Replace(If(appId, ""), "[^A-Za-z0-9_.-]", "")
        Dim cleanRoom = Regex.Replace(If(room, "main"), "[^A-Za-z0-9_.-]", "")
        If cleanApp.Length > 64 Then cleanApp = cleanApp.Substring(0, 64)
        If cleanRoom.Length > 64 Then cleanRoom = cleanRoom.Substring(0, 64)
        If cleanApp.Length = 0 Then Throw New ArgumentException("Enter a valid app ID.", NameOf(appId))
        If cleanRoom.Length = 0 Then cleanRoom = "main"
        Dim payload = New With {
            Key .p_hash = authToken.ToLowerInvariant(),
            Key .p_app = cleanApp,
            Key .p_room = cleanRoom,
            Key .p_limit = Math.Max(1, Math.Min(limit, 200))
        }
        Dim result As New List(Of CloudSave)()
        Using doc = JsonDocument.Parse(Await PostJsonAsync("rest/v1/rpc/kh_app_list", payload, Nothing, cancellationToken))
            Dim rows As JsonElement
            If Not doc.RootElement.TryGetProperty("rows", rows) OrElse rows.ValueKind <> JsonValueKind.Array Then Return result
            For Each row In rows.EnumerateArray()
                If row.ValueKind <> JsonValueKind.Object Then Continue For
                Dim savedAt As DateTimeOffset
                Dim atText = JsonStr(row, "at")
                If Not DateTimeOffset.TryParse(atText, savedAt) Then savedAt = DateTimeOffset.MinValue
                result.Add(New CloudSave With {
                    .Key = JsonStr(row, "k"),
                    .Value = JsonStr(row, "v"),
                    .SavedAt = savedAt,
                    .ModifiedBy = JsonStr(row, "by")
                })
            Next
        End Using
        Return result.OrderByDescending(Function(item) item.SavedAt).ToList()
    End Function

    Public Async Function SetRecoveryEmailAsync(authToken As String, email As String, cancellationToken As CancellationToken) As Task Implements IKindleHubApiClient.SetRecoveryEmailAsync
        If Not Hex64.IsMatch(If(authToken, "")) Then Throw New AuthenticationException("Sign in first.")
        Dim cleanEmail = If(email, "").Trim()
        If cleanEmail.Length > 254 OrElse Not Regex.IsMatch(cleanEmail, "^[^\s@]+@[^\s@]+\.[^\s@]{2,}$") Then Throw New ValidationException("Enter a valid recovery email.")
        Await PostJsonAsync("rest/v1/rpc/kh_recovery_set", New With {
            Key .p_hash = authToken.ToLowerInvariant(),
            Key .p_email = cleanEmail
        }, Nothing, cancellationToken)
    End Function

    Public Async Function FetchModeratorStatsAsync(moderatorCode As String, cancellationToken As CancellationToken) As Task(Of ModeratorStats) Implements IKindleHubApiClient.FetchModeratorStatsAsync
        If String.IsNullOrWhiteSpace(moderatorCode) Then Throw New ValidationException("Enter a moderator code.")
        Using doc = JsonDocument.Parse(Await PostJsonAsync("rest/v1/rpc/kh_mod_stats", New With {Key .p_token = moderatorCode.Trim()}, Nothing, cancellationToken))
            Dim root = doc.RootElement
            Return New ModeratorStats With {
                .Level = JsonStr(root, "level"),
                .Users = JsonInt(root, "users"),
                .OnlineNow = JsonInt(root, "onlineNow"),
                .VisitsToday = JsonInt(root, "visitsToday"),
                .Visitors7d = JsonInt(root, "visitors7d"),
                .Messages = JsonInt(root, "messages"),
                .Groups = JsonInt(root, "groups"),
                .FeedbackOpen = JsonInt(root, "feedbackOpen"),
                .AppsPending = If(root.TryGetProperty("appsPending", Nothing), CType(JsonInt(root, "appsPending"), Integer?), Nothing)
            }
        End Using
    End Function

    Public Async Function ClaimModeratorCodeAsync(authToken As String, displayName As String, inviteCode As String, cancellationToken As CancellationToken) As Task Implements IKindleHubApiClient.ClaimModeratorCodeAsync
        If Not Hex64.IsMatch(If(authToken, "")) Then Throw New AuthenticationException("Sign in first.")
        Dim code = If(inviteCode, "").Trim().ToUpperInvariant()
        If code.Length < 4 OrElse code.Length > 128 Then Throw New ValidationException("Enter a valid invite code.")
        Dim codeHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code))).ToLowerInvariant()
        Await PostJsonAsync("rest/v1/rpc/kh_mod_claim", New With {
            Key .p_code_hash = codeHash,
            Key .p_name = TrimStr(If(displayName, "Reader"), 60),
            Key .p_uid = authToken.Substring(0, 16).ToLowerInvariant()
        }, Nothing, cancellationToken)
    End Function

    Public Async Function SubmitModeratorApplicationAsync(authToken As String, displayName As String, timeUsing As String, ageRange As String, reason As String, priorExperience As String, cancellationToken As CancellationToken) As Task Implements IKindleHubApiClient.SubmitModeratorApplicationAsync
        If Not Hex64.IsMatch(If(authToken, "")) Then Throw New AuthenticationException("Sign in first.")
        Dim why = TrimStr(If(reason, ""), 700)
        If why.Length < 20 Then Throw New ValidationException("Please write at least a sentence about why you want to help.")
        Dim uid = authToken.Substring(0, 16).ToLowerInvariant()
        Dim name = TrimStr(If(displayName, "Reader"), 60)
        Dim text = "[REPORT] Moderator application" & vbLf &
                   "Account: " & name & vbLf & "Uid: " & uid & vbLf &
                   "Using KindleHub: " & TrimStr(timeUsing, 40) & vbLf &
                   "Age range: " & TrimStr(ageRange, 32) & vbLf &
                   "Why: " & why.Replace(vbCr, " ").Replace(vbLf, " ") & vbLf &
                   "Before: " & TrimStr(If(priorExperience, "not given"), 200).Replace(vbCr, " ").Replace(vbLf, " ") & vbLf &
                   "By: " & name & " (uid=" & uid & ")"
        Await PostJsonAsync("rest/v1/kh_feedback", New With {
            Key .id = RelayRooms.ReportId("modapp"),
            Key .type = "bug",
            Key .text = LeftOf(text, 1990),
            Key .votes = 0,
            Key .date = UtcIso()
        }, Nothing, cancellationToken)
    End Function

    Public Async Function CreateTopicAsync(request As CreateTopicRequest, authToken As String, displayName As String, cancellationToken As CancellationToken) As Task(Of Group) Implements IKindleHubApiClient.CreateTopicAsync
        If Not Hex64.IsMatch(If(authToken, "")) Then Throw New AuthenticationException("Sign in first.")
        Dim code = RoomCodes.NewRoomCode()
        Dim name = TopicHelper.BuildName(request.Title, request.Blurb, request.CategoryId)
        Dim group = Await CreateGroupAsync(New CreateGroupRequest With {
            .Code = code,
            .Name = name,
            .Creator = If(String.IsNullOrEmpty(displayName), "Reader", displayName)
        }, authToken, cancellationToken)

        ' Publish it into the topic directory so others can discover it (v7805 shape).
        Dim value = JsonSerializer.Serialize(New With {
            Key .n = TrimStr(name, 220),
            Key .t = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Key .a = TrimStr(displayName, 40)
        })
        Try
            Await PostJsonAsync("rest/v1/rpc/kh_app_put", New With {
                Key .p_hash = authToken.ToLowerInvariant(),
                Key .p_app = "khtopics",
                Key .p_room = "index",
                Key .p_key = TrimStr(code, 16),
                Key .p_value = value
            }, Nothing, cancellationToken)
        Catch
            ' The room itself still exists; others just won't see it in the directory.
        End Try
        Return group
    End Function

    ' ───────────────────── app store ─────────────────────
    Public Async Function FetchAppsAsync(request As FetchAppsRequest, cancellationToken As CancellationToken) As Task(Of List(Of AppCatalog)) Implements IKindleHubApiClient.FetchAppsAsync
        Dim fetchLimit = Math.Max(1, Math.Min(request.Limit * 2, 200))
        Dim rows = Await GetRowsAsync("rest/v1/kh_store_apps?select=id,name,cat,author,model,created_at,downloads,preview,icon_art,age_rating,rating_sum,rating_count&order=downloads.desc,created_at.desc&limit=" & fetchLimit, cancellationToken)
        Dim list As New List(Of AppCatalog)()
        If rows Is Nothing Then Return list
        For Each row In rows
            list.Add(ParseCatalog(row))
        Next
        If Not String.IsNullOrEmpty(request.Category) AndAlso Not String.Equals(request.Category, "All", StringComparison.OrdinalIgnoreCase) Then
            list = list.Where(Function(x) String.Equals(x.Category, request.Category, StringComparison.OrdinalIgnoreCase)).ToList()
        End If
        If Not String.IsNullOrWhiteSpace(request.Search) Then
            Dim needle = request.Search.Trim().ToUpperInvariant()
            list = list.Where(Function(x) (If(x.Name, "").ToUpperInvariant().Contains(needle)) OrElse
                                           (If(x.Author, "").ToUpperInvariant().Contains(needle))).ToList()
        End If
        If list.Count > request.Limit Then list = list.Take(request.Limit).ToList()
        Return list
    End Function

    Public Async Function DownloadAppAsync(appId As String, cancellationToken As CancellationToken) As Task(Of DownloadedApp) Implements IKindleHubApiClient.DownloadAppAsync
        Return Await DownloadAppAsync(appId, Nothing, cancellationToken)
    End Function

    Public Async Function DownloadAppAsync(appId As String, ownerSecret As String, cancellationToken As CancellationToken) As Task(Of DownloadedApp) Implements IKindleHubApiClient.DownloadAppAsync
        ' The same row-query as the web client: select=html&id=eq. When we hold the author's
        ' secret, the gateway also serves the row while it is still review='pending'
        ' (api-worker: approved OR owner_secret = X-KH-Secret), so authors can run their own app.
        Dim url = $"rest/v1/kh_store_apps?select=id,name,cat,author,html&id=eq.{Uri.EscapeDataString(appId)}&limit=1"
        Dim secret = If(Hex64.IsMatch(If(ownerSecret, "")), ownerSecret, Nothing)
        Dim rows = Await GetRowsAsync(url, secret, cancellationToken)
        If rows Is Nothing OrElse rows.Count = 0 Then Throw New NotFoundException("That app is no longer in the store.")
        Dim row = rows(0)
        Return New DownloadedApp With {
            .Id = JsonStr(row, "id"),
            .Name = JsonStr(row, "name"),
            .Category = JsonStr(row, "cat"),
            .Author = JsonStr(row, "author"),
            .Html = JsonStr(row, "html")
        }
    End Function

    ''' <summary>Owner-secret-aware variant used to re-open this device's own apps.</summary>
    Public Async Function DownloadAppWithSecretAsync(appId As String, ownerSecret As String, cancellationToken As CancellationToken) As Task(Of DownloadedApp) Implements IKindleHubApiClient.DownloadAppWithSecretAsync
        Return Await DownloadAppAsync(appId, ownerSecret, cancellationToken)
    End Function

    Public Async Function CountAppDownloadAsync(appId As String, cancellationToken As CancellationToken) As Task Implements IKindleHubApiClient.CountAppDownloadAsync
        Try
            Await PostVoidAsync("rest/v1/rpc/kh_store_download", New With {Key .p_id = Safe(appId)}, MinimalHeaders(Nothing), cancellationToken)
        Catch
            ' Best-effort download counter; a failed count must not fail an install.
        End Try
    End Function

    Public Async Function PublishAppAsync(request As PublishAppRequest, authToken As String, ownerSecret As String, cancellationToken As CancellationToken) As Task(Of PublishedApp) Implements IKindleHubApiClient.PublishAppAsync
        Dim secret = If(String.IsNullOrEmpty(ownerSecret), authToken, ownerSecret)
        If Not Hex64.IsMatch(If(secret, "")) Then Throw New AuthenticationException("Sign in to publish an app.")
        Dim id = "pub_" & Guid.NewGuid().ToString("n").Substring(0, 11)
        Dim cat = If(String.IsNullOrEmpty(request.Category), "Fun", request.Category)
        Dim payload = New With {
            Key .id = id,
            Key .name = TrimStr(If(request.Name, "Untitled App"), 60),
            Key .html = If(request.Html, ""),
            Key .cat = TrimStr(cat, 24),
            Key .author = TrimStr(If(request.Author, "user"), 40),
            Key .created_at = UtcIso(),
            Key .downloads = 0,
            Key .owner_secret = secret.ToLowerInvariant()
        }
        Await PostVoidAsync("rest/v1/kh_store_apps", payload, InsertHeaders(secret), cancellationToken)
        Return New PublishedApp With {
            .Id = id,
            .Name = payload.name,
            .Category = payload.cat,
            .Html = payload.html,
            .Author = payload.author,
            .OwnerSecret = secret
        }
    End Function

    ' ───────────────────── leaderboard + presence ─────────────────────
    Public Async Function SubmitScoreAsync(request As SubmitScoreRequest, authToken As String, cancellationToken As CancellationToken) As Task(Of Boolean) Implements IKindleHubApiClient.SubmitScoreAsync
        ' kh_scores writes are refused for anonymous accounts (leaderboard integrity guard).
        If Not Hex64.IsMatch(If(authToken, "")) Then Throw New AuthenticationException("Sign in to post a score to the global leaderboard.")
        ' Web parity (_khSubmitScore): integer 1..1,000,000,000, a per-game run token
        ' from kh_run_start riding along as `run`, an optional replay `proof`, and a
        ' 30-second same-score dedupe per game and account.
        Dim score = CLng(Math.Truncate(CDbl(request.Score)))
        If score < 1L OrElse score > 1000000000L Then Throw New ValidationException("Scores must be whole numbers between 1 and 1,000,000,000.")
        Dim game = TrimStr(If(request.Game, "game"), 40)
        Dim dedupeKey = game & "|" & authToken.Substring(0, 16) & "|" & score.ToString(Globalization.CultureInfo.InvariantCulture)
        SyncLock _scoreDedupe
            Dim last As DateTimeOffset
            If _scoreDedupe.TryGetValue(dedupeKey, last) AndAlso last > DateTimeOffset.UtcNow.AddSeconds(-30) Then Return True
        End SyncLock
        Dim runToken = Await GetRunTokenAsync(authToken, game, cancellationToken)
        Dim userId = authToken.Substring(0, 16).ToLowerInvariant()
        Dim now = DateTimeOffset.UtcNow
        Dim proofText = If(String.IsNullOrWhiteSpace(request.Proof), Nothing, LeftOf(request.Proof, 4000))
        Dim payload = New With {
            Key .id = userId & "_" & game & "_" & now.ToUnixTimeMilliseconds() & "_" & Random.Shared.Next(1000),
            Key .game = game,
            Key .score = score,
            Key .display_name = TrimStr(If(request.DisplayName, "Reader"), 40),
            Key .user_id = userId,
            Key .date = UtcIso(),
            Key .run = If(runToken.Length = 0, Nothing, runToken),
            Key .proof = proofText
        }
        Dim retryNeeded = False
        Try
            Await PostVoidAsync("rest/v1/kh_scores", payload, InsertHeaders(authToken), cancellationToken)
            MarkScoreDedupe(dedupeKey)
            Return True
        Catch ex As ServerException When ex.StatusCode = 409
            MarkScoreDedupe(dedupeKey)
            Return True
        Catch ex As ServerException When ex.StatusCode = 422 AndAlso Not ex.Message.Contains("SCORE_TOKEN")
            ' Policy rejections (SCORE_* errors) are swallowed like the web client does.
            Return False
        Catch ex As ServerException
            If Not ex.Message.Contains("SCORE_TOKEN") Then Throw
            retryNeeded = True ' the token expired server-side: refresh and retry below
        End Try
        If retryNeeded Then
            SyncLock _runTokens
                _runTokens.Remove(authToken.ToLowerInvariant() & "|" & game.ToLowerInvariant())
            End SyncLock
            runToken = Await GetRunTokenAsync(authToken, game, cancellationToken)
            Dim retry = New With {
                Key .id = payload.id & "_r",
                Key .game = payload.game,
                Key .score = payload.score,
                Key .display_name = payload.display_name,
                Key .user_id = payload.user_id,
                Key .date = UtcIso(),
                Key .run = If(runToken.Length = 0, Nothing, runToken),
                Key .proof = payload.proof
            }
            Await PostVoidAsync("rest/v1/kh_scores", retry, InsertHeaders(authToken), cancellationToken)
            MarkScoreDedupe(dedupeKey)
            Return True
        End If
        Return False
    End Function

    ''' <summary>Per-game leaderboard run token via kh_run_start {p_hash, p_game}. The
    ''' gateway requires a fresh "khr1." token on every kh_scores write; the web client
    ''' caches one per game for a day (kh_runtok) and a token stays submit-valid 20h.</summary>
    Public Async Function GetRunTokenAsync(authToken As String, game As String, cancellationToken As CancellationToken) As Task(Of String) Implements IKindleHubApiClient.GetRunTokenAsync
        If Not Hex64.IsMatch(If(authToken, "")) Then Return ""
        Dim cleanGame = TrimStr(If(game, "game"), 40).ToLowerInvariant()
        Dim key = authToken.ToLowerInvariant() & "|" & cleanGame
        SyncLock _runTokens
            Dim hit = (Token := "", At := DateTimeOffset.MinValue)
            If _runTokens.TryGetValue(key, hit) AndAlso hit.Token.StartsWith("khr1.", StringComparison.Ordinal) AndAlso hit.At > DateTimeOffset.UtcNow.AddHours(-20) Then
                Return hit.Token
            End If
        End SyncLock
        Dim payload = New With {
            Key .p_hash = authToken.ToLowerInvariant(),
            Key .p_game = cleanGame
        }
        Dim token = ""
        Try
            Dim body = Await PostJsonAsync("rest/v1/rpc/kh_run_start", payload, MinimalHeaders(authToken), cancellationToken)
            Using doc = JsonDocument.Parse(body)
                token = JsonStr(doc.RootElement, "token")
            End Using
        Catch ex As OperationCanceledException
            Throw
        Catch
            ' Token fetch is fail-open: the gateway still accepts submissions without
            ' one when the RPC is unavailable, so the game keeps playing either way.
            Return ""
        End Try
        If Not token.StartsWith("khr1.", StringComparison.Ordinal) Then Return ""
        SyncLock _runTokens
            _runTokens(key) = (token, DateTimeOffset.UtcNow)
        End SyncLock
        Return token
    End Function

    Private Sub MarkScoreDedupe(key As String)
        SyncLock _scoreDedupe
            If _scoreDedupe.Count > 128 Then _scoreDedupe.Clear()
            _scoreDedupe(key) = DateTimeOffset.UtcNow
        End SyncLock
    End Sub

    Public Async Function FetchScoresAsync(request As FetchScoresRequest, cancellationToken As CancellationToken) As Task(Of List(Of LeaderboardEntry)) Implements IKindleHubApiClient.FetchScoresAsync
        Dim rows = Await GetRowsAsync($"rest/v1/kh_scores?game=eq.{Uri.EscapeDataString(request.Game)}&order=score.desc&limit=" &
                                      Math.Max(1, Math.Min(request.Limit, 100)) & "&select=id,score,display_name,date,user_id", cancellationToken)
        Dim results As New List(Of LeaderboardEntry)()
        If rows Is Nothing Then Return results
        ' Web parity: the leaderboard hides rows from banned names (kh_banned_usernames)
        ' and collapses duplicate names to their best row.
        Dim bans = Await FetchBannedNamesAsync(cancellationToken)
        Dim seenNames As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        For Each row In rows
            Dim d = JsonDate(row, "date")
            Dim name = JsonStr(row, "display_name")
            If Not String.IsNullOrEmpty(name) AndAlso bans.Contains(name) Then Continue For
            If String.IsNullOrEmpty(name) Then name = "Reader"
            If Not seenNames.Add(name.Trim()) Then Continue For
            results.Add(New LeaderboardEntry With {
                .Id = JsonStr(row, "id"),
                .Game = request.Game,
                .Score = JsonLong(row, "score"),
                .DisplayName = name,
                .UserId = JsonStr(row, "user_id"),
                .Date = If(d.HasValue, d.Value, DateTimeOffset.MinValue)
            })
        Next
        Return results
    End Function

    ' ───────────────────── mail ─────────────────────
    ''' <summary>The website's <code>_mailNorm</code>: trim, lowercase, drop
    ''' everything from the first <code>@</code>, cap at 40. The mailbox gate in
    ''' the worker resolves recipients through the same function, so a name that
    ''' differs here is a name the recipient can never query.</summary>
    Private Shared Function MailNorm(u As String) As String
        Dim s = If(u, "").Trim().ToLowerInvariant()
        Dim at = s.IndexOf("@"c)
        If at >= 0 Then s = s.Substring(0, at)
        Return If(s.Length > 40, s.Substring(0, 40), s)
    End Function

    Public Shared Function NormalizeMailUser(u As String) As String
        Return MailNorm(u)
    End Function

    ''' <summary>The key the website derives for a mail row: the recipient's
    ''' canonical name. Both subject and body are sealed under it, and because the
    ''' name is public a sender re-derives it to read their own Sent copy.</summary>
    Public Shared Function MailKeySuffix(toUser As String) As String
        Return "mail:" & If(toUser, "")
    End Function

    Public Async Function FetchMailAsync(authToken As String, cancellationToken As CancellationToken) As Task(Of List(Of MailItem)) Implements IKindleHubApiClient.FetchMailAsync
        ' The worker forces the filter to the caller's own mail when the 64-hex
        ' secret is present, so no username filter is sent from here.
        If Not Hex64.IsMatch(If(authToken, "")) Then Throw New AuthenticationException("Sign in to read your mail.")
        Dim rows = Await GetRowsAsync("rest/v1/kh_mail?order=ts.desc&limit=200&select=id,to_user,from_user,from_id,subject,body,ts,reply_to,owner_secret",
                                      authToken, cancellationToken)
        Dim results As New List(Of MailItem)()
        If rows Is Nothing Then Return results
        For Each row In rows
            ' The website seals both columns under the recipient's name, so a sender
            ' who knows the address can read their own Sent copy and the recipient
            ' their inbox — and a note to yourself is sealed to your own name too.
            Dim key = MailKeySuffix(MailNorm(JsonStr(row, "to_user")))
            Dim body = ""
            Dim cipher = JsonStr(row, "body")
            If Not String.IsNullOrEmpty(cipher) Then
                Try
                    body = ChatEncryption.Decrypt(key, cipher)
                Catch ex As Exception
                    ' A body we cannot open is still worth listing — show it as such
                    ' rather than dropping the mail.
                    body = "(This message could not be decrypted on this device.)"
                End Try
            End If
            ' The subject is sealed the same way, not plaintext: read it back or an
            ' inbox list shows the base64 blob instead of what the letter was about.
            Dim subject = ""
            Dim subjCipher = JsonStr(row, "subject")
            If Not String.IsNullOrEmpty(subjCipher) Then
                Try
                    subject = ChatEncryption.Decrypt(key, subjCipher)
                Catch ex As Exception
                    subject = subjCipher
                End Try
            End If
            results.Add(New MailItem With {
                .Id = JsonStr(row, "id"),
                .ToUser = JsonStr(row, "to_user"),
                .FromUser = JsonStr(row, "from_user"),
                .FromId = JsonStr(row, "from_id"),
                .Subject = subject,
                .Body = body,
                .Timestamp = JsonDate(row, "ts").GetValueOrDefault(DateTimeOffset.MinValue),
                .ReplyTo = JsonStr(row, "reply_to"),
                .OwnerSecret = JsonStr(row, "owner_secret")
            })
        Next
        Return results
    End Function

    Public Async Function SendMailAsync(request As SendMailRequest, authToken As String, cancellationToken As CancellationToken) As Task(Of MailItem) Implements IKindleHubApiClient.SendMailAsync
        If Not Hex64.IsMatch(If(authToken, "")) Then Throw New AuthenticationException("Sign in to send mail.")
        ' Store the recipient's CANONICAL name. The website does the same, and the
        ' worker resolves a mailbox through the identical function, so a name saved
        ' any other way is a row the recipient can never query.
        Dim toUser = MailNorm(If(request?.ToUser, ""))
        If String.IsNullOrEmpty(toUser) Then Throw New ArgumentException("Enter a recipient.", NameOf(request.ToUser))
        Dim userId = authToken.Substring(0, 16).ToLowerInvariant()
        Dim id = "m_" & userId & "_" & DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() & "_" & Random.Shared.Next(100000)
        Dim subject = If(String.IsNullOrWhiteSpace(request.Subject), "(no subject)", request.Subject.Trim())
        If subject.Length > 160 Then subject = subject.Substring(0, 160)
        Dim body = If(request.Body, "")
        If body.Length > 8000 Then body = body.Substring(0, 8000)

        ' Both columns are sealed under the recipient's name — the website's scheme,
        ' which is what keeps the two clients reading each other's mail. The name is
        ' public, so a sender re-derives the key to open their own Sent copy.
        Dim key = MailKeySuffix(toUser)
        Dim payload = New With {
            Key .id = id,
            Key .to_user = toUser,
            Key .from_user = MailNorm(If(request.FromUser, "")),
            Key .from_id = userId,
            Key .subject = ChatEncryption.Encrypt(key, subject),
            Key .body = ChatEncryption.Encrypt(key, body),
            Key .ts = UtcIso(),
            Key .reply_to = TrimStr(If(request.ReplyTo, ""), 120),
            Key .owner_secret = authToken.ToLowerInvariant()
        }
        Await PostVoidAsync("rest/v1/kh_mail", payload, InsertHeaders(authToken), cancellationToken)

        Return New MailItem With {
            .Id = id,
            .ToUser = toUser,
            .FromId = userId,
            .Subject = subject,
            .Body = body,
            .Timestamp = DateTimeOffset.UtcNow,
            .ReplyTo = TrimStr(If(request.ReplyTo, ""), 120),
            .OwnerSecret = authToken.ToLowerInvariant()
        }
    End Function

    Public Async Function DeleteMailAsync(mailId As String, ownerSecret As String, cancellationToken As CancellationToken) As Task(Of Boolean) Implements IKindleHubApiClient.DeleteMailAsync
        If Not Hex64.IsMatch(If(ownerSecret, "")) Then Return False
        If String.IsNullOrEmpty(mailId) Then Return False
        Return Await DeleteAsync("rest/v1/kh_mail?id=eq." & Uri.EscapeDataString(mailId), ownerSecret, cancellationToken)
    End Function

    Public Async Function ListKnownGamesAsync(cancellationToken As CancellationToken) As Task(Of List(Of String)) Implements IKindleHubApiClient.ListKnownGamesAsync        ' Aggregate the games we already know about: recent scores (public) union the
        ' games hosted right now in the open-games lobby (the lobby is chat: decode with its room key).
        Dim known As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        Try
            Dim rows = Await GetRowsAsync("rest/v1/kh_scores?select=game&order=date.desc&limit=100", cancellationToken)
            If rows IsNot Nothing Then
                For Each r In rows
                    Dim g = JsonStr(r, "game")
                    If Not String.IsNullOrEmpty(g) Then known.Add(g)
                Next
            End If
        Catch
        End Try
        Try
            Dim lobby = Await FetchMessagesAsync(New FetchMessagesRequest With {
                .GroupCode = OpenGamesLobby,
                .Limit = 200,
                .Encrypted = True
            }, Nothing, cancellationToken)
            Dim fresh = DateTimeOffset.UtcNow.AddMinutes(-15).ToUnixTimeMilliseconds()
            For Each m In lobby
                Dim evt = TryParseJson(m.Text)
                If evt IsNot Nothing Then
                    Dim t = Val(evt, "type")
                    Dim tsRaw = ValRaw(evt, "ts")
                    If t = "OPEN" AndAlso (String.IsNullOrEmpty(tsRaw) OrElse ToLong(tsRaw) >= fresh) Then
                        Dim g = Val(evt, "game")
                        If Not String.IsNullOrEmpty(g) Then known.Add(g)
                    End If
                End If
            Next
        Catch
        End Try
        ' Curated fallbacks so the picker is never empty.
        If known.Count = 0 Then
            For Each g In { "snake", "tictactoe", "2048", "memory", "wordle", "trivia", "platformer", "artillery", "arena", "Last Ladder", "chess", "Snakes & Ladders", "battleship" }
                known.Add(g)
            Next
        End If
        Return known.OrderBy(Function(s) s, StringComparer.OrdinalIgnoreCase).ToList()
    End Function

    Public Async Function PingPresenceAsync(authToken As String, displayName As String, gameRoom As String, avatar As String, profile As String, cancellationToken As CancellationToken) As Task Implements IKindleHubApiClient.PingPresenceAsync
        ' kh_presence has no game_room column. Public profile data follows the
        ' official compact profile blob schema in its existing profile column.
        If Not Hex64.IsMatch(If(authToken, "")) Then Return
        Dim uid = authToken.Substring(0, 16).ToLowerInvariant()
        Dim profileData As JsonObject
        Try
            Using doc = JsonDocument.Parse(If(String.IsNullOrWhiteSpace(profile), "{}", profile))
                If doc.RootElement.ValueKind = JsonValueKind.Object Then
                    profileData = JsonNode.Parse(doc.RootElement.GetRawText()).AsObject()
                Else
                    profileData = New JsonObject()
                End If
            End Using
        Catch
            profileData = New JsonObject()
        End Try
        ' The current web client sends its compact profile card as JSON text
        ' inside the profile column, not as a nested object.
        Dim payload As New JsonObject From {
            {"user_id", JsonValue.Create(uid)},
            {"display_name", JsonValue.Create(TrimStr(If(displayName, "Reader"), 40))},
            {"last_seen", JsonValue.Create(UtcIso())},
            {"profile", JsonValue.Create(If(profileData.Count = 0, "", profileData.ToJsonString()))}
        }
        ' Web parity: a beat never sends an empty avatar — omitting the field keeps
        ' the picture other clients already see instead of erasing it.
        If IsProfileAvatarCode(avatar) Then payload("avatar") = JsonValue.Create(avatar)
        Try
            Await PostVoidAsync("rest/v1/kh_presence?on_conflict=user_id", payload, UpsertHeaders(authToken), cancellationToken)
        Catch
            ' Best-effort heartbeat.
        End Try
    End Function

    Public Const OnlineWindowMinutes As Integer = 7
    Public Const PresenceBeatSeconds As Integer = 100
    Public Const PresenceBeatSimpleModeSeconds As Integer = 180
    Public Const DiscoveryWindowDays As Integer = 14

    Public Async Function FetchPresenceAsync(minutesActive As Integer, limit As Integer, cancellationToken As CancellationToken) As Task(Of List(Of PresenceEntry)) Implements IKindleHubApiClient.FetchPresenceAsync
        Dim cutoff = DateTimeOffset.UtcNow.AddMinutes(-Math.Max(1, minutesActive)).ToString("O")
        ' Web parity: discovery sweeps read a 14-day window at limit 400, so the
        ' former hard cap of 100 was too low for the People view.
        Dim rows = Await GetRowsAsync($"rest/v1/kh_presence?last_seen=gt.{Uri.EscapeDataString(cutoff)}&select=user_id,display_name,last_seen,avatar,profile&order=last_seen.desc&limit=" &
                                      Math.Max(1, Math.Min(limit, 400)), cancellationToken)
        Dim list As New List(Of PresenceEntry)()
        If rows Is Nothing Then Return list
        For Each row In rows
            Dim d = JsonDate(row, "last_seen")
            Dim p As New PresenceEntry With {
                .UserId = JsonStr(row, "user_id"),
                .DisplayName = JsonStr(row, "display_name"),
                .Avatar = If(IsProfileAvatarCode(JsonStr(row, "avatar")), JsonStr(row, "avatar"), ""),
                .Profile = JsonStr(row, "profile"),
                .LastSeen = If(d.HasValue, d.Value, DateTimeOffset.MinValue)
            }
            list.Add(p)
        Next
        Return list
    End Function

    Public Async Function FetchAvatarCodesAsync(userIds As IEnumerable(Of String), cancellationToken As CancellationToken) As Task(Of Dictionary(Of String, String)) Implements IKindleHubApiClient.FetchAvatarCodesAsync
        Dim ids = If(userIds, Enumerable.Empty(Of String)()).Where(Function(id) Regex.IsMatch(If(id, ""), "^(?:[a-fA-F0-9]{16}|p[a-z2-7]{15})$")).Distinct(StringComparer.OrdinalIgnoreCase).Take(100).ToList()
        Dim result As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
        If ids.Count = 0 Then Return result
        Dim filter = String.Join(",", ids.Select(Function(id) id.ToLowerInvariant()))
        Dim rows = Await GetRowsAsync("rest/v1/kh_presence?user_id=in.(" & filter & ")&select=user_id,avatar&limit=100", cancellationToken)
        If rows Is Nothing Then Return result
        For Each row In rows
            Dim id = JsonStr(row, "user_id")
            Dim avatar = JsonStr(row, "avatar")
            If Regex.IsMatch(id, "^(?:[a-fA-F0-9]{16}|p[a-z2-7]{15})$") AndAlso IsProfileAvatarCode(avatar) Then result(id) = avatar
        Next
        Return result
    End Function

    Public Async Function FetchPublicProfilesAsync(userIds As IEnumerable(Of String), cancellationToken As CancellationToken) As Task(Of Dictionary(Of String, PublicProfileDetails)) Implements IKindleHubApiClient.FetchPublicProfilesAsync
        Dim ids = If(userIds, Enumerable.Empty(Of String)()).Where(Function(id) Regex.IsMatch(If(id, ""), "^(?:[a-fA-F0-9]{16}|p[a-z2-7]{15})$")).Distinct(StringComparer.OrdinalIgnoreCase).Take(100).ToList()
        Dim result As New Dictionary(Of String, PublicProfileDetails)(StringComparer.OrdinalIgnoreCase)
        If ids.Count = 0 Then Return result
        Dim filter = String.Join(",", ids.Select(Function(id) id.ToLowerInvariant()))
        Dim rows = Await GetRowsAsync("rest/v1/kh_presence?user_id=in.(" & filter & ")&select=user_id,avatar,profile&limit=100", cancellationToken)
        If rows Is Nothing Then Return result
        For Each row In rows
            Dim id = JsonStr(row, "user_id")
            If Not Regex.IsMatch(id, "^(?:[a-fA-F0-9]{16}|p[a-z2-7]{15})$") Then Continue For
            Dim details As New PublicProfileDetails With {
                .Avatar = If(IsProfileAvatarCode(JsonStr(row, "avatar")), JsonStr(row, "avatar"), "")
            }
            Try
                Using doc = JsonDocument.Parse(JsonStr(row, "profile"))
                    If doc.RootElement.ValueKind = JsonValueKind.Object Then
                        details.ProfileFrame = JsonStr(doc.RootElement, "fr")
                        details.NameStyle = JsonStr(doc.RootElement, "ns")
                        details.Role = JsonStr(doc.RootElement, "r")
                        details.Plan = JsonStr(doc.RootElement, "pl")
                    End If
                End Using
            Catch
            End Try
            result(id) = details
        Next
        Return result
    End Function

    Public Async Function SearchFriendUsersAsync(query As String, authToken As String, cancellationToken As CancellationToken) As Task(Of List(Of FriendUser)) Implements IKindleHubApiClient.SearchFriendUsersAsync
        Dim result As New List(Of FriendUser)()
        Dim term = If(query, "").Trim().Replace("%", "")
        If term.Length < 2 Then Return result
        If Not Hex64.IsMatch(If(authToken, "")) Then Throw New AuthenticationException("Sign in to find people.")
        Dim rows = Await GetRowsAsync("rest/v1/kh_users?email=ilike." & Uri.EscapeDataString("*" & term & "*") & "&select=hash,email&limit=20", authToken, cancellationToken)
        For Each row In rows
            Dim hash = JsonStr(row, "hash"), email = JsonStr(row, "email")
            Dim pid = JsonStr(row, "pid").ToLowerInvariant()
            Dim uid = If(Regex.IsMatch(pid, "^p[a-z2-7]{15}$"), pid, If(hash.Length >= 16, hash.Substring(0, 16), ""))
            If Regex.IsMatch(uid, "^(?:[a-fA-F0-9]{16}|p[a-z2-7]{15})$") AndAlso email.Length > 0 AndAlso
               Not authToken.StartsWith(uid, StringComparison.OrdinalIgnoreCase) Then
                result.Add(New FriendUser With {.Hash = uid, .Name = email.Split("@"c)(0)})
            End If
        Next
        Return result
    End Function

    Private Shared Function IsProfileAvatarCode(value As String) As Boolean
        Return Not String.IsNullOrEmpty(value) AndAlso value.Length <= 400 AndAlso
               (value.StartsWith("KHAV1:", StringComparison.Ordinal) OrElse value.StartsWith("KHAV2:", StringComparison.Ordinal))
    End Function

    ''' <summary>Send a plain-text relay JSON event over the encrypted chat transport
    ''' (mirrors KH_MP.send: the payload is JSON.stringify'd, then _groupSend encrypts it).</summary>
    Public Async Function SendRoomEventAsync(groupCode As String, eventJson As String, displayName As String, authToken As String, cancellationToken As CancellationToken) As Task(Of Message) Implements IKindleHubApiClient.SendRoomEventAsync
        Return Await SendMessageAsync(New SendMessageRequest With {
            .GroupCode = groupCode,
            .Text = eventJson,
            .DisplayName = displayName,
            .Encrypted = True
        }, authToken, cancellationToken)
    End Function

    ''' <summary>Poll a room for its last N messages and surface each one whose decrypted
    ''' text parses as a JSON envelope (the multiplayer relay, KH_MP.subscribe).</summary>
    Public Async Function PollRoomEventsAsync(groupCode As String, authToken As String, limit As Integer, cancellationToken As CancellationToken) As Task(Of List(Of RoomMessageEnvelope)) Implements IKindleHubApiClient.PollRoomEventsAsync
        Dim messages = Await FetchMessagesAsync(New FetchMessagesRequest With {
            .GroupCode = groupCode,
            .Limit = limit,
            .Encrypted = True
        }, authToken, cancellationToken)
        Dim out As New List(Of RoomMessageEnvelope)()
        For Each m In messages
            Dim evt = TryParseJson(m.Text)
            If evt Is Nothing Then Continue For
            out.Add(New RoomMessageEnvelope With {
                .Message = m,
                .Type = Val(evt, "type"),
                .Data = evt
            })
        Next
        Return out
    End Function

    ''' <summary>Reads specific message rows by id so a chat line can show its quoted parent.
    ''' The gateway scopes reads by id=eq|in (an id-filtered read is a member-level read).
    ''' Missing/deleted parents are simply absent from the returned list.</summary>
    Public Async Function FetchMessagesByIdsAsync(ids As IEnumerable(Of String), groupCode As String, authToken As String, cancellationToken As CancellationToken) As Task(Of List(Of Message)) Implements IKindleHubApiClient.FetchMessagesByIdsAsync
        Dim clean = (If(ids Is Nothing, New String() {}, ids.ToArray())).
            Where(Function(s) Not String.IsNullOrEmpty(s)).
            Distinct(StringComparer.Ordinal).
            Take(40).
            ToArray()
        If clean.Length = 0 Then Return New List(Of Message)()
        Dim inVal = "(" & String.Join(",", clean.Select(Function(c) Uri.EscapeDataString(c))) & ")"
        Dim url = $"rest/v1/kh_messages?id=in.{inVal}&select=id,group_code,user_id,display_name,text,ts,edited,important&limit={clean.Length}"
        Dim request = New FetchMessagesRequest With {.GroupCode = If(groupCode, ""), .Limit = clean.Length, .Encrypted = True}
        Dim rows = Await GetRowsAsync(url, cancellationToken)
        Dim results As New List(Of Message)()
        If rows Is Nothing Then Return results
        For Each row In rows
            Dim room = If(groupCode, JsonStr(row, "group_code"))
            Dim wire = JsonStr(row, "text")
            Dim ts = JsonDate(row, "ts")
            results.Add(New Message With {
                .Id = JsonStr(row, "id"),
                .GroupCode = room,
                .UserId = JsonStr(row, "user_id"),
                .DisplayName = JsonStr(row, "display_name"),
                .Text = ChatEncryption.Decrypt(room, wire),
                .Timestamp = If(ts.HasValue, ts.Value, DateTimeOffset.UtcNow),
                .Edited = JsonBool(row, "edited"),
                .Important = JsonBool(row, "important"),
                .OwnerSecret = MessageSecretStore.Lookup(room, JsonStr(row, "id")),
                .Reactions = New Dictionary(Of String, Integer)(),
                .ReactionEmoji = New Dictionary(Of String, List(Of String))()
            })
        Next
        Return results
    End Function

    ''' <summary>Removes your own published app (SECRET_DELETE: the row's owner_secret
    ''' must match the secret we send, so only the author can delete a store row).</summary>
    Public Async Function UnpublishAppAsync(appId As String, ownerSecret As String, cancellationToken As CancellationToken) As Task(Of Boolean) Implements IKindleHubApiClient.UnpublishAppAsync
        If Not Hex64.IsMatch(If(ownerSecret, "")) Then Throw New AuthenticationException("Sign in to remove your published app.")
        Return Await DeleteAsync($"rest/v1/kh_store_apps?id=eq.{Uri.EscapeDataString(appId)}", ownerSecret, cancellationToken)
    End Function

    ' ───────────────────── HTTP plumbing ─────────────────────
    Private Overloads Async Function GetRowsAsync(url As String, cancellationToken As CancellationToken) As Task(Of List(Of JsonElement))
        Return Await GetRowsAsync(url, Nothing, cancellationToken)
    End Function

    ''' <summary>Per-table GET cache TTLs, mirroring the web client's result cache.
    ''' Message and presence reads are never cached (they drive live polling).</summary>
    Private Shared Function CacheTtl(url As String) As TimeSpan
        If url.Contains("kh_messages") OrElse url.Contains("kh_presence") OrElse url.Contains("kh_mail") OrElse url.Contains("kh_users") Then Return TimeSpan.Zero
        If url.Contains("kh_scores") Then Return TimeSpan.FromSeconds(30)
        If url.Contains("kh_store_apps") Then Return TimeSpan.FromSeconds(120)
        If url.Contains("kh_groups") Then Return TimeSpan.FromSeconds(60)
        If url.Contains("kh_banned_usernames") Then Return TimeSpan.FromMinutes(10)
        Return TimeSpan.FromSeconds(20)
    End Function

    Private Overloads Async Function GetRowsAsync(url As String, secret As String, cancellationToken As CancellationToken) As Task(Of List(Of JsonElement))
        Dim cacheKey = url & "|" & If(secret, "").ToLowerInvariant()
        Dim ttl = CacheTtl(url)
        If ttl > TimeSpan.Zero Then
            SyncLock _cacheLock
                Dim hit As CacheEntry = Nothing
                If _getCache.TryGetValue(cacheKey, hit) Then
                    If hit.Expires > DateTimeOffset.UtcNow Then Return hit.Rows
                    _getCache.Remove(cacheKey)
                End If
            End SyncLock
        End If
        Dim generation As Long
        SyncLock _cacheLock
            generation = _cacheGeneration
        End SyncLock
        Dim rows = Await GetRowsCoreAsync(url, secret, cancellationToken)
        If ttl > TimeSpan.Zero Then
            SyncLock _cacheLock
                If generation <> _cacheGeneration Then Return rows
                If _getCache.Count > 80 Then _getCache.Clear() ' web cache holds ~80 entries
                _getCache(cacheKey) = New CacheEntry With {.Expires = DateTimeOffset.UtcNow.Add(ttl), .Rows = rows}
            End SyncLock
        End If
        Return rows
    End Function

    Private Async Function GetRowsCoreAsync(url As String, secret As String, cancellationToken As CancellationToken) As Task(Of List(Of JsonElement))
        Using linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            linked.CancelAfter(_options.ReadTimeout)
            Using req As New HttpRequestMessage(HttpMethod.Get, url)
                req.Headers.TryAddWithoutValidation("Accept", "application/json")
                req.Headers.TryAddWithoutValidation("x-kh-want-pid", "1")
                If Not String.IsNullOrEmpty(secret) Then req.Headers.TryAddWithoutValidation("X-KH-Secret", secret.ToLowerInvariant())
                Try
                    Using resp = Await _httpClient.SendAsync(req, linked.Token)
                        Dim body = Await resp.Content.ReadAsStringAsync(linked.Token)
                        If Not resp.IsSuccessStatusCode Then
                            _logger.LogWarning("GET {Path} failed: {Status}", url.Split("?"c)(0), CInt(resp.StatusCode))
                            Throw New ServerException($"API request failed: {resp.StatusCode}", CInt(resp.StatusCode))
                        End If
                        If String.IsNullOrWhiteSpace(body) Then Return New List(Of JsonElement)()
                        Using doc = JsonDocument.Parse(body)
                            If doc.RootElement.ValueKind = JsonValueKind.Array Then
                                Return doc.RootElement.EnumerateArray().Select(Function(row) row.Clone()).ToList()
                            End If
                            Return New List(Of JsonElement) From {doc.RootElement.Clone()}
                        End Using
                    End Using
                Catch ex As ServerException
                    Throw
                Catch ex As Exception
                    If TypeOf ex Is OperationCanceledException Then Throw
                    _logger.LogWarning("GET {Path} could not reach the server", url.Split("?"c)(0))
                    Throw New NetworkException("Could not reach the server: " & ex.Message, ex)
                End Try
            End Using
        End Using
    End Function

    ''' <summary>Retry a kh_users upsert like the web client does for account saves
    ''' (two extra attempts with a short backoff before giving up).</summary>
    Private Async Function PostKhUsersWithRetryAsync(payload As Object, authToken As String, cancellationToken As CancellationToken) As Task
        Dim attempt = 0
        While True
            attempt += 1
            Try
                Await PostVoidAsync("rest/v1/kh_users?on_conflict=hash", payload, UpsertHeaders(authToken), cancellationToken)
                Return
            Catch ex As ServerException
                Throw
            Catch ex As Exception
                If TypeOf ex Is OperationCanceledException OrElse attempt > 3 Then Throw
            End Try
            Await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken)
        End While
    End Function

    Private Async Function PostVoidAsync(url As String, payload As Object, headers As Dictionary(Of String, String), cancellationToken As CancellationToken) As Task
        Await PostInternalAsync(url, payload, headers, cancellationToken)
    End Function

    Private Async Function PostJsonAsync(url As String, payload As Object, headers As Dictionary(Of String, String), cancellationToken As CancellationToken) As Task(Of String)
        Return Await PostInternalAsync(url, payload, headers, cancellationToken)
    End Function

    Public Async Function ReportMessageAsync(reason As String, note As String, msg As Message, groupName As String, reporterName As String, reporterUserId As String, cancellationToken As CancellationToken) As Task(Of Boolean) Implements IKindleHubApiClient.ReportMessageAsync
        ' Exact official [REPORT] shape (kh-app.js _khReportPost) written to kh_feedback.
        Dim label = ChatMedia.DescribeForReport(If(msg.Text, ""))
        Dim clipped = label.Substring(0, Math.Min(label.Length, 300)).Replace(vbLf, " "c).Replace(vbCr, " "c)
        Dim textBuilder As New StringBuilder()
        textBuilder.Append("[REPORT] ").AppendLine(reason)
        textBuilder.Append("Message: ").Append(Chr(34)).Append(clipped).Append(Chr(34)).AppendLine()
        textBuilder.Append("By: ").Append(msg.DisplayName).Append(" (uid=").Append(LeftOf(msg.UserId, 16)).AppendLine(")")
        textBuilder.Append("Group: ").Append(groupName).Append(" (").Append(msg.GroupCode).AppendLine(")")
        textBuilder.Append("Message id: ").AppendLine(msg.Id).Append("Reporter: ").Append(reporterName)
        If Not String.IsNullOrEmpty(reporterUserId) Then textBuilder.Append(" uid=").Append(LeftOf(reporterUserId, 16))
        If Not String.IsNullOrWhiteSpace(note) Then textBuilder.AppendLine().Append("Note: ").Append(LeftOf(note, 300))
        Dim payload = New With {
            Key .id = RelayRooms.ReportId("rep"),
            Key .type = "bug",
            Key .text = LeftOf(textBuilder.ToString(), 1990),
            Key .votes = 0,
            Key .date = UtcIso()
        }
        Try
            Await PostJsonAsync("rest/v1/kh_feedback", payload, Nothing, cancellationToken)
            Return True
        Catch
            Return False
        End Try
    End Function

    Public Async Function ReportUsernameAsync(reason As String, note As String, displayName As String, userId As String, securityFlag As Boolean, roomCode As String, groupName As String, msgId As String, reporterName As String, reporterUserId As String, cancellationToken As CancellationToken) As Task(Of Boolean) Implements IKindleHubApiClient.ReportUsernameAsync
        Dim textBuilder As New StringBuilder()
        If securityFlag Then textBuilder.Append("[REPORT] [SECURITY] [USERNAME] ") Else textBuilder.Append("[REPORT] [USERNAME] ")
        textBuilder.AppendLine(reason)
        textBuilder.Append("Username: ").Append(Chr(34)).Append(LeftOf(displayName, 80)).Append(Chr(34)).AppendLine()
        textBuilder.Append("User id: ").Append(LeftOf(userId, 16))
        If Not String.IsNullOrEmpty(groupName) AndAlso Not String.IsNullOrEmpty(msgId) Then
            textBuilder.AppendLine()
            textBuilder.Append("Group: ").Append(groupName).Append(" (").Append(roomCode).AppendLine(")")
            textBuilder.Append("Message id: ").AppendLine(msgId)
        End If
        textBuilder.AppendLine("Reporter: ").Append(reporterName)
        If Not String.IsNullOrEmpty(reporterUserId) Then textBuilder.Append(" uid=").Append(LeftOf(reporterUserId, 16))
        If note IsNot Nothing AndAlso note.Trim().Length > 0 Then textBuilder.AppendLine().Append("Note: ").Append(LeftOf(note, 300))
        Dim payload = New With {
            Key .id = RelayRooms.ReportId("urep"),
            Key .type = "bug",
            Key .text = LeftOf(textBuilder.ToString(), 1990),
            Key .votes = 0,
            Key .date = UtcIso()
        }
        Try
            Await PostJsonAsync("rest/v1/kh_feedback", payload, Nothing, cancellationToken)
            Return True
        Catch
            Return False
        End Try
    End Function

    Private Sub InvalidateReads()
        SyncLock _cacheLock
            _cacheGeneration += 1
            _getCache.Clear()
        End SyncLock
    End Sub

    Private Async Function PostInternalAsync(url As String, payload As Object, headers As Dictionary(Of String, String), cancellationToken As CancellationToken) As Task(Of String)
        Using linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            linked.CancelAfter(If(url.Contains("kh_users"), _options.AccountUpsertTimeout, _options.WriteTimeout))
            Using req As New HttpRequestMessage(HttpMethod.Post, url)
                req.Content = New StringContent(JsonSerializer.Serialize(payload, JsonOpts()), Encoding.UTF8, "application/json")
                ApplyHeaders(req, headers)
                Using resp = Await _httpClient.SendAsync(req, linked.Token)
                    Dim body = Await resp.Content.ReadAsStringAsync(linked.Token)
                    If Not resp.IsSuccessStatusCode Then
                        _logger.LogWarning("POST {Path} failed: {Status}", url.Split("?"c)(0), CInt(resp.StatusCode))
                        Throw New ServerException(SafeErr(body, resp), CInt(resp.StatusCode))
                    End If
                    InvalidateReads()
                    Return body
                End Using
            End Using
        End Using
    End Function

    Private Async Function PatchAsync(url As String, payload As Object, ownerSecret As String, cancellationToken As CancellationToken) As Task(Of Boolean)
        Using req As New HttpRequestMessage(New HttpMethod("PATCH"), url)
            req.Content = New StringContent(JsonSerializer.Serialize(payload, JsonOpts()), Encoding.UTF8, "application/json")
            req.Headers.TryAddWithoutValidation("X-KH-Secret", ownerSecret)
            req.Headers.TryAddWithoutValidation("Prefer", "return=minimal")
            Using resp = Await _httpClient.SendAsync(req, cancellationToken)
                If Not resp.IsSuccessStatusCode Then
                    Dim body = Await resp.Content.ReadAsStringAsync(cancellationToken)
                    _logger.LogWarning("PATCH {Path} failed: {Status}", url.Split("?"c)(0), CInt(resp.StatusCode))
                    Return False
                End If
                InvalidateReads()
                Return True
            End Using
        End Using
    End Function

    Private Async Function DeleteAsync(url As String, ownerSecret As String, cancellationToken As CancellationToken) As Task(Of Boolean)
        Using req As New HttpRequestMessage(HttpMethod.Delete, url)
            If Not String.IsNullOrEmpty(ownerSecret) Then req.Headers.TryAddWithoutValidation("X-KH-Secret", ownerSecret)
            Using resp = Await _httpClient.SendAsync(req, cancellationToken)
                If resp.IsSuccessStatusCode Then InvalidateReads()
                Return resp.IsSuccessStatusCode
            End Using
        End Using
    End Function

    Private Shared Function JsonStr(el As JsonElement, name As String) As String
        Dim child As JsonElement
        Try
            If el.TryGetProperty(name, child) Then
                If child.ValueKind = JsonValueKind.String Then Return If(child.GetString(), "")
                If child.ValueKind = JsonValueKind.Number Then Return child.GetRawText()
                If child.ValueKind = JsonValueKind.Array OrElse child.ValueKind = JsonValueKind.Object Then Return child.GetRawText()
            End If
        Catch
        End Try
        Return ""
    End Function

    Private Shared Function JsonInt(el As JsonElement, name As String) As Integer
        Dim child As JsonElement
        If el.ValueKind = JsonValueKind.Object AndAlso el.TryGetProperty(name, child) AndAlso child.ValueKind = JsonValueKind.Number Then
            Dim value As Integer
            If child.TryGetInt32(value) Then Return value
        End If
        Return 0
    End Function

    Private Shared Function JsonBool(el As JsonElement, name As String) As Boolean
        Dim child As JsonElement
        Try
            If el.TryGetProperty(name, child) Then
                If child.ValueKind = JsonValueKind.True Then Return True
                If child.ValueKind = JsonValueKind.False Then Return False
                If child.ValueKind = JsonValueKind.Number Then Return child.GetInt32() = 1
            End If
        Catch
        End Try
        Return False
    End Function

    Private Shared Function JsonLong(el As JsonElement, name As String) As Long
        Dim child As JsonElement
        Try
            If el.TryGetProperty(name, child) Then
                If child.ValueKind = JsonValueKind.Number Then
                    Try
                        Return child.GetInt64()
                    Catch
                        Return CLng(Math.Truncate(child.GetDouble()))
                    End Try
                End If
                If child.ValueKind = JsonValueKind.String Then
                    Dim v As Long
                    If Int64.TryParse(child.GetString(), v) Then Return v
                End If
            End If
        Catch
        End Try
        Return 0
    End Function

    Private Shared Function JsonDate(el As JsonElement, name As String) As DateTimeOffset?
        Dim s = JsonStr(el, name)
        If String.IsNullOrEmpty(s) Then Return Nothing
        Dim dt As DateTimeOffset
        If DateTimeOffset.TryParse(s, dt) Then Return dt
        Return Nothing
    End Function

    Private Sub ApplyHeaders(req As HttpRequestMessage, headers As Dictionary(Of String, String))
        If headers Is Nothing Then Return
        For Each kv In headers
            req.Headers.TryAddWithoutValidation(kv.Key, kv.Value)
        Next
    End Sub

    Private Function InsertHeaders(secret As String) As Dictionary(Of String, String)
        Dim h = New Dictionary(Of String, String) From {
            {"Prefer", "return=representation"},
            {"Content-Type", "application/json"}
        }
        If Hex64.IsMatch(If(secret, "")) Then h.Add("X-KH-Secret", secret.ToLowerInvariant())
        Return h
    End Function

    Private Function UpsertHeaders(secret As String) As Dictionary(Of String, String)
        Dim h = New Dictionary(Of String, String) From {
            {"Prefer", "resolution=merge-duplicates,return=representation"},
            {"Content-Type", "application/json"}
        }
        If Hex64.IsMatch(If(secret, "")) Then h.Add("X-KH-Secret", secret.ToLowerInvariant())
        Return h
    End Function

    Private Function MinimalHeaders(secret As String) As Dictionary(Of String, String)
        Dim h = New Dictionary(Of String, String) From {
            {"Prefer", "return=minimal"},
            {"Content-Type", "application/json"}
        }
        If Hex64.IsMatch(If(secret, "")) Then h.Add("X-KH-Secret", secret.ToLowerInvariant())
        Return h
    End Function

    ' ───────────────────── web-build parity additions ─────────────────────
    ''' <summary>Deliver an inbox event through the official RPC route
    ''' (kh_inbox_send {p_hash,p_to,p_payload}), falling back to a direct insert
    ''' into the recipient's 800000xxxxxx relay room when the RPC is unavailable.</summary>
    Public Async Function SendInboxEventAsync(authToken As String, toUserId As String, payloadJson As String, displayName As String, cancellationToken As CancellationToken) As Task(Of Boolean) Implements IKindleHubApiClient.SendInboxEventAsync
        If Not Hex64.IsMatch(If(authToken, "")) Then Throw New AuthenticationException("Sign in first.")
        Dim uid = TrimStr(If(toUserId, ""), 16).ToLowerInvariant()
        If Not Regex.IsMatch(uid, "^(?:[a-f0-9]{16}|p[a-z2-7]{15})$") Then Throw New ValidationException("A valid recipient user id is required.")
        Dim eventData As JsonNode
        Try
            eventData = JsonNode.Parse(If(payloadJson, "{}"))
        Catch ex As JsonException
            Throw New ValidationException("An inbox event must be a JSON object.")
        End Try
        If TypeOf eventData IsNot JsonObject Then Throw New ValidationException("An inbox event must be a JSON object.")
        Dim payload = New With {
            Key .p_hash = authToken.ToLowerInvariant(),
            Key .p_to = uid,
            Key .p_payload = eventData
        }
        Dim sent = False
        Try
            Await PostVoidAsync("rest/v1/rpc/kh_inbox_send", payload, MinimalHeaders(authToken), cancellationToken)
            sent = True
        Catch ex As OperationCanceledException
            Throw
        Catch ex As ServerException When ex.StatusCode >= 400 AndAlso ex.StatusCode < 500 AndAlso ex.StatusCode <> 404
            ' A refusal is authoritative. Do not turn it into a different write.
            Throw
        Catch
            sent = False
        End Try
        If Not sent AndAlso uid.StartsWith("p", StringComparison.Ordinal) Then Return False
        If Not sent Then
            ' Legacy route: post the event straight into the recipient's inbox room.
            Try
                Await SendMessageAsync(New SendMessageRequest With {
                    .GroupCode = RelayRooms.InboxRoomCodeFor(uid),
                    .Text = payloadJson,
                    .DisplayName = displayName,
                    .Encrypted = True
                }, authToken, cancellationToken)
                sent = True
            Catch
                sent = False
            End Try
        End If
        Return sent
    End Function

    ''' <summary>Rate a store app (kh_store_rate {p_hash,p_id,p_stars}, 1..5).</summary>
    Public Async Function RateAppAsync(authToken As String, appId As String, stars As Integer, cancellationToken As CancellationToken) As Task Implements IKindleHubApiClient.RateAppAsync
        If Not Hex64.IsMatch(If(authToken, "")) Then Throw New AuthenticationException("Sign in to rate apps.")
        If stars < 1 OrElse stars > 5 Then Throw New ValidationException("Ratings run from 1 to 5 stars.")
        Await PostJsonAsync("rest/v1/rpc/kh_store_rate", New With {
            Key .p_hash = authToken.ToLowerInvariant(),
            Key .p_id = Safe(appId),
            Key .p_stars = stars
        }, MinimalHeaders(authToken), cancellationToken)
    End Function

    ''' <summary>Moderator queue of apps awaiting AI review (kh_store_pending).</summary>
    Public Async Function FetchPendingStoreAppsAsync(moderatorCode As String, cancellationToken As CancellationToken) As Task(Of List(Of AppCatalog)) Implements IKindleHubApiClient.FetchPendingStoreAppsAsync
        If String.IsNullOrWhiteSpace(moderatorCode) Then Throw New ValidationException("Enter a moderator code.")
        Dim result As New List(Of AppCatalog)()
        Using doc = JsonDocument.Parse(Await PostJsonAsync("rest/v1/rpc/kh_store_pending", New With {Key .p_token = moderatorCode.Trim()}, Nothing, cancellationToken))
            Dim rows As JsonElement
            If doc.RootElement.ValueKind = JsonValueKind.Array Then
                rows = doc.RootElement
            ElseIf doc.RootElement.TryGetProperty("rows", rows) Then
                ' RPC returned {rows:[…]} — same shape as kh_app_list.
            Else
                Return result
            End If
            For Each row In rows.EnumerateArray()
                If row.ValueKind = JsonValueKind.Object Then result.Add(ParseCatalog(row))
            Next
        End Using
        Return result
    End Function

    ''' <summary>Moderator verdict on a pending app (kh_store_review): decision is
    ''' "approve"/"decline" with an optional reason.</summary>
    Public Async Function ReviewStoreAppAsync(moderatorCode As String, appId As String, decision As String, reason As String, cancellationToken As CancellationToken) As Task Implements IKindleHubApiClient.ReviewStoreAppAsync
        If String.IsNullOrWhiteSpace(moderatorCode) Then Throw New ValidationException("Enter a moderator code.")
        Dim d = If(decision, "").Trim().ToLowerInvariant()
        If d <> "approve" AndAlso d <> "decline" Then Throw New ValidationException("Decision must be approve or decline.")
        Await PostJsonAsync("rest/v1/rpc/kh_store_review", New With {
            Key .p_token = moderatorCode.Trim(),
            Key .p_id = Safe(appId),
            Key .p_decision = d,
            Key .p_reason = LeftOf(If(reason, ""), 300)
        }, Nothing, cancellationToken)
    End Function

    ''' <summary>Room-owner cleanup: the gateway accepts a delete carrying the room
    ''' owner's account secret plus X-KH-Room-Owner: 1 (moderation of one's own room).</summary>
    Public Async Function DeleteMessageAsRoomOwnerAsync(messageId As String, authToken As String, cancellationToken As CancellationToken) As Task(Of Boolean) Implements IKindleHubApiClient.DeleteMessageAsRoomOwnerAsync
        If Not Hex64.IsMatch(If(authToken, "")) Then Return False
        If String.IsNullOrEmpty(messageId) Then Return False
        Dim url = $"rest/v1/kh_messages?id=eq.{Uri.EscapeDataString(messageId)}"
        Using req As New HttpRequestMessage(HttpMethod.Delete, url)
            req.Headers.TryAddWithoutValidation("X-KH-Secret", authToken.ToLowerInvariant())
            req.Headers.TryAddWithoutValidation("X-KH-Room-Owner", "1")
            Using resp = Await _httpClient.SendAsync(req, cancellationToken)
                If resp.IsSuccessStatusCode Then InvalidateReads()
                Return resp.IsSuccessStatusCode
            End Using
        End Using
    End Function

    ''' <summary>The banned-name set used to filter leaderboards, refreshed from
    ''' kh_banned_usernames every 10 minutes (best-effort, like the web client).</summary>
    Private Async Function FetchBannedNamesAsync(cancellationToken As CancellationToken) As Task(Of HashSet(Of String))
        If _banNamesAt > DateTimeOffset.UtcNow.AddMinutes(-10) Then Return _banNames
        Try
            Dim rows = Await GetRowsAsync("rest/v1/kh_banned_usernames?select=email&limit=1000", cancellationToken)
            Dim fresh As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            For Each r In rows
                Dim e = JsonStr(r, "email")
                If Not String.IsNullOrEmpty(e) Then fresh.Add(e)
            Next
            _banNames.Clear()
            _banNames.UnionWith(fresh)
            _banNamesAt = DateTimeOffset.UtcNow
        Catch
            ' Keep the previous set; filtering stays best-effort.
        End Try
        Return _banNames
    End Function

    ''' <summary>Freedom-first: only basic sanity (non-empty, ≤40 chars, safe charset).
    ''' Reserved-name and staff-name gates are the SERVER's job — the desktop no longer
    ''' blocks any name locally. canonical is already lowercase via CanonicalizeUsername.</summary>
    Public Shared Function ValidateUsername(canonical As String) As String
        If String.IsNullOrEmpty(canonical) Then Return "Pick a username."
        If canonical.Length > 40 Then Return "Usernames are at most 40 characters."
        If Not Regex.IsMatch(canonical, "^[a-z0-9._-]+$") Then Return "Usernames may only use letters, numbers, dots, underscores and dashes."
        Return Nothing
    End Function

    ''' <summary>HIBP k-anonymity check (api.pwnedpasswords.com/range/&lt;first 5 of SHA-1&gt;).
    ''' Fails OPEN on network errors, like the web client's _khPwLeaked.</summary>
    Public Async Function IsPasswordPwnedAsync(password As String, cancellationToken As CancellationToken) As Task(Of Boolean)
        If String.IsNullOrEmpty(password) Then Return False
        Try
            Dim hashHex = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(password))).ToLowerInvariant()
            Using req As New HttpRequestMessage(HttpMethod.Get, "https://api.pwnedpasswords.com/range/" & hashHex.Substring(0, 5))
                req.Headers.TryAddWithoutValidation("Add-Padding", "true")
                Using linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
                    linked.CancelAfter(TimeSpan.FromSeconds(5))
                    Using resp = Await _httpClient.SendAsync(req, linked.Token)
                        If Not resp.IsSuccessStatusCode Then Return False
                        Dim body = Await resp.Content.ReadAsStringAsync(linked.Token)
                        Dim suffix = hashHex.Substring(5)
                        For Each line In body.Split(vbLf)
                            If line.Trim().StartsWith(suffix, StringComparison.Ordinal) Then Return True
                        Next
                        Return False
                    End Using
                End Using
            End Using
        Catch
            Return False
        End Try
    End Function

    ' ───────────────────── small helpers ─────────────────────
    Private Function ParseGroup(row As JsonElement) As Group
        Dim name = JsonStr(row, "name")
        Dim parts = TopicHelper.Parse(name)
        Dim d = JsonDate(row, "created_at")
        Return New Group With {
            .Code = JsonStr(row, "code"),
            .Name = name,
            .Creator = JsonStr(row, "creator"),
            .CreatedAt = If(d.HasValue, d.Value, DateTimeOffset.MinValue),
            .TopicCategory = parts.Cat,
            .Blurb = parts.Blurb
        }
    End Function

    Private Function ParseCatalog(row As JsonElement) As AppCatalog
        Dim d = JsonDate(row, "created_at")
        Return New AppCatalog With {
            .Id = JsonStr(row, "id"),
            .Name = JsonStr(row, "name"),
            .Category = JsonStr(row, "cat"),
            .Author = JsonStr(row, "author"),
            .Model = JsonStr(row, "model"),
            .Preview = JsonStr(row, "preview"),
            .IconArt = JsonStr(row, "icon_art"),
            .AgeRating = JsonStr(row, "age_rating"),
            .Downloads = CInt(JsonLong(row, "downloads")),
            .RatingSum = CInt(JsonLong(row, "rating_sum")),
            .RatingCount = CInt(JsonLong(row, "rating_count")),
            .CreatedAt = If(d.HasValue, d.Value, DateTimeOffset.MinValue)
        }
    End Function

    Private Function SafeErr(body As String, resp As HttpResponseMessage) As String
        If String.IsNullOrWhiteSpace(body) Then Return $"API request failed: {resp.StatusCode}"
        Return LeftOf(body, 300)
    End Function

    Private Function Safe(s As String) As String
        Return If(s, "")
    End Function

    Private Function LeftOf(s As String, n As Integer) As String
        If s Is Nothing Then Return ""
        Return If(s.Length <= n, s, s.Substring(0, n))
    End Function

    Private Function TrimStr(s As String, maxLen As Integer) As String
        s = If(s, "")
        Return If(s.Length <= maxLen, s, s.Substring(0, maxLen))
    End Function

    Private Async Function ResolvePublicIdAsync(token As String, cancellationToken As CancellationToken) As Task(Of String)
        If Not Hex64.IsMatch(If(token, "")) Then Return ""
        Dim hit = (Id:="", At:=DateTimeOffset.MinValue)
        If _publicIds.TryGetValue(token, hit) AndAlso hit.At > DateTimeOffset.UtcNow.AddMinutes(-5) Then Return hit.Id
        Dim uid = UserIdFromToken(token)
        Try
            Using doc = JsonDocument.Parse(Await PostJsonAsync("rest/v1/rpc/kh_public_id", New With {Key .p_hash = token}, MinimalHeaders(token), cancellationToken))
                Dim pid = JsonStr(doc.RootElement, "pid")
                If Regex.IsMatch(pid, "^p[a-z2-7]{15}$") Then uid = pid
            End Using
        Catch ex As OperationCanceledException
            Throw
        Catch
            ' Older backends still identify accounts by their 16-hex prefix.
        End Try
        _publicIds(token) = (uid, DateTimeOffset.UtcNow)
        Return uid
    End Function

    Private Function UserIdFromToken(token As String) As String
        If Hex64.IsMatch(If(token, "")) Then Return token.Substring(0, 16).ToLowerInvariant()
        Return ""
    End Function

    Private Shared Function UtcIso() As String
        Return DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
    End Function

    Private Shared Function ToLong(raw As String) As Long
        Dim v As Long
        If Int64.TryParse(raw, v) Then Return v
        Return 0
    End Function

    Private Shared Function Val(doc As JsonDocument, name As String) As String
        If doc Is Nothing Then Return ""
        Try
            Dim el As JsonElement
            If doc.RootElement.TryGetProperty(name, el) Then
                If el.ValueKind = JsonValueKind.String Then Return If(el.GetString(), "")
                If el.ValueKind <> JsonValueKind.Null Then Return el.ToString()
            End If
        Catch
        End Try
        Return ""
    End Function

    Private Shared Function ValRaw(doc As JsonDocument, name As String) As String
        If doc Is Nothing Then Return ""
        Try
            Dim el As JsonElement
            If doc.RootElement.TryGetProperty(name, el) Then
                If el.ValueKind = JsonValueKind.String Then Return el.GetString()
                Return el.GetRawText()
            End If
        Catch
        End Try
        Return ""
    End Function

    Private Shared Function TryParseJson(s As String) As JsonDocument
        Try
            Return JsonDocument.Parse(s)
        Catch
            Return Nothing
        End Try
    End Function
End Class
