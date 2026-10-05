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
    Public Property BaseUrl As String = "https://kindlehub-api.arancool3000.workers.dev"
    Public Property Timeout As TimeSpan = TimeSpan.FromSeconds(30)
    Public Property UserAgent As String = "KindleHub.Desktop/1.0"
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
    ' view of a single fetch. Body is E2E-encrypted under SHA-256("khmsg::mail:"+id);
    ' subject is deliberately plaintext so a list can render without any keys.
    Function FetchMailAsync(authToken As String, cancellationToken As CancellationToken) As Task(Of List(Of MailItem))
    Function SendMailAsync(request As SendMailRequest, authToken As String, cancellationToken As CancellationToken) As Task(Of MailItem)
    ' Unsend: only the sending device can remove its own mail (owner_secret gate).
    Function DeleteMailAsync(mailId As String, ownerSecret As String, cancellationToken As CancellationToken) As Task(Of Boolean)
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
            Await PostVoidAsync("rest/v1/kh_users?on_conflict=hash", payload, UpsertHeaders(authToken), cancellationToken)
            Return New AuthResult With {
                .Success = True,
                .Username = canonical,
                .AuthToken = authToken,
                .StateJson = "{}",
                .Adjusted = False
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
                Dim decrypted = AccountEncryption.UnpackAccount(packedState, authToken)
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
        Dim canonicalEmail = KeyDerivation.CanonicalizeUsername(email).ToLowerInvariant()
        Dim now = UtcIso()
        Dim payload = New With {
            Key .hash = authToken,
            Key .email = canonicalEmail,
            Key .state = AccountEncryption.PackAccount(stateJson, authToken),
            Key .updated_at = now
        }
        Try
            Await PostVoidAsync("rest/v1/kh_users?on_conflict=hash", payload, UpsertHeaders(authToken), cancellationToken)
        Catch ex As ServerException When ex.StatusCode = 409
            Throw New ValidationException("That username is registered to a different account. Delete that account first.")
        End Try
        Return now
    End Function

    Public Async Function FetchAccountAsync(authToken As String, cancellationToken As CancellationToken) As Task(Of String) Implements IKindleHubApiClient.FetchAccountAsync
        Dim rows = Await GetRowsAsync($"rest/v1/kh_users?hash=eq.{Uri.EscapeDataString(authToken)}&select=state,updated_at", cancellationToken)
        If rows Is Nothing OrElse rows.Count = 0 Then Return Nothing
        Return AccountEncryption.UnpackAccount(JsonStr(rows(0), "state"), authToken)
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

    Public Function GetServerStatusAsync(cancellationToken As CancellationToken) As Task(Of ServerStatus) Implements IKindleHubApiClient.GetServerStatusAsync
        ' Reachability is proven by whatever call the UI makes next; the gateway has no
        ' unfiltered table read and no general status RPC.
        Return Task.FromResult(New ServerStatus With {.Online = True, .Version = "1.0"})
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
        Dim myId = UserIdFromToken(authToken) & ""
        If String.IsNullOrEmpty(myId) AndAlso Hex64.IsMatch(If(authToken, "")) Then myId = authToken.Substring(0, 16).ToLowerInvariant()
        Dim limit = Math.Max(1, Math.Min(request.Limit, 200))
        Dim url = $"rest/v1/kh_messages?group_code=eq.{Uri.EscapeDataString(request.GroupCode)}&order=ts.desc&limit={limit}" &
                  $"&select=id,group_code,user_id,display_name,text,ts,edited,important,reactions,reply_to,device_hint,location_hint"
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
                .DeviceHint = JsonStr(row, "device_hint"),
                .LocationHint = JsonStr(row, "location_hint"),
                .Timestamp = If(ts.HasValue, ts.Value, DateTimeOffset.UtcNow),
                .Edited = JsonBool(row, "edited"),
                .Important = JsonBool(row, "important"),
                .OwnerSecret = MessageSecretStore.Lookup(JsonStr(row, "group_code"), JsonStr(row, "id")),
                .IsMine = Not String.IsNullOrEmpty(myId) AndAlso String.Equals(JsonStr(row, "user_id"), myId, StringComparison.Ordinal),
                .Reactions = New Dictionary(Of String, Integer)(),
                .ReactionEmoji = New Dictionary(Of String, List(Of String))()
            }
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
        Dim text = "[REPORT] [MODAPP] Moderator application" & vbLf &
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
        Dim userId = authToken.Substring(0, 16).ToLowerInvariant()
        Dim game = TrimStr(If(request.Game, "game"), 48)
        Dim now = DateTimeOffset.UtcNow
        Dim payload = New With {
            Key .id = userId & "_" & game & "_" & now.ToUnixTimeMilliseconds() & "_" & Random.Shared.Next(1000),
            Key .game = game,
            Key .score = Math.Max(0L, Math.Min(request.Score, 999999999L)),
            Key .display_name = TrimStr(If(request.DisplayName, "Reader"), 40),
            Key .user_id = userId,
            Key .date = UtcIso()
        }
        Try
            Await PostVoidAsync("rest/v1/kh_scores", payload, InsertHeaders(authToken), cancellationToken)
            Return True
        Catch ex As ServerException When ex.StatusCode = 409
            Return True
        End Try
    End Function

    Public Async Function FetchScoresAsync(request As FetchScoresRequest, cancellationToken As CancellationToken) As Task(Of List(Of LeaderboardEntry)) Implements IKindleHubApiClient.FetchScoresAsync
        Dim rows = Await GetRowsAsync($"rest/v1/kh_scores?game=eq.{Uri.EscapeDataString(request.Game)}&order=score.desc&limit=" &
                                      Math.Max(1, Math.Min(request.Limit, 100)) & "&select=id,score,display_name,date,user_id", cancellationToken)
        Dim results As New List(Of LeaderboardEntry)()
        If rows Is Nothing Then Return results
        For Each row In rows
            Dim d = JsonDate(row, "date")
            results.Add(New LeaderboardEntry With {
                .Id = JsonStr(row, "id"),
                .Game = request.Game,
                .Score = JsonLong(row, "score"),
                .DisplayName = JsonStr(row, "display_name"),
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
        ' Build a JSON DOM row explicitly. The endpoint validates `profile` as
        ' an object; keeping it as JsonObject avoids accidentally sending the
        ' compact JSON text as a quoted string.
        Dim payload As New JsonObject From {
            {"user_id", JsonValue.Create(uid)},
            {"display_name", JsonValue.Create(TrimStr(If(displayName, "Reader"), 40))},
            {"last_seen", JsonValue.Create(UtcIso())},
            {"avatar", JsonValue.Create(If(IsProfileAvatarCode(avatar), avatar, ""))},
            {"profile", profileData}
        }
        Try
            Await PostVoidAsync("rest/v1/kh_presence?on_conflict=user_id", payload, UpsertHeaders(authToken), cancellationToken)
        Catch
            ' Best-effort heartbeat.
        End Try
    End Function

    Public Async Function FetchPresenceAsync(minutesActive As Integer, limit As Integer, cancellationToken As CancellationToken) As Task(Of List(Of PresenceEntry)) Implements IKindleHubApiClient.FetchPresenceAsync
        Dim cutoff = DateTimeOffset.UtcNow.AddMinutes(-Math.Max(1, minutesActive)).ToString("O")
        Dim rows = Await GetRowsAsync($"rest/v1/kh_presence?last_seen=gt.{Uri.EscapeDataString(cutoff)}&select=user_id,display_name,last_seen,avatar,profile&order=last_seen.desc&limit=" &
                                      Math.Max(1, Math.Min(limit, 100)), cancellationToken)
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
        Dim ids = If(userIds, Enumerable.Empty(Of String)()).Where(Function(id) Regex.IsMatch(If(id, ""), "^[a-fA-F0-9]{16}$")).Distinct(StringComparer.OrdinalIgnoreCase).Take(100).ToList()
        Dim result As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
        If ids.Count = 0 Then Return result
        Dim filter = String.Join(",", ids.Select(Function(id) id.ToLowerInvariant()))
        Dim rows = Await GetRowsAsync("rest/v1/kh_presence?user_id=in.(" & filter & ")&select=user_id,avatar&limit=100", cancellationToken)
        If rows Is Nothing Then Return result
        For Each row In rows
            Dim id = JsonStr(row, "user_id")
            Dim avatar = JsonStr(row, "avatar")
            If Regex.IsMatch(id, "^[a-fA-F0-9]{16}$") AndAlso IsProfileAvatarCode(avatar) Then result(id) = avatar
        Next
        Return result
    End Function

    Public Async Function FetchPublicProfilesAsync(userIds As IEnumerable(Of String), cancellationToken As CancellationToken) As Task(Of Dictionary(Of String, PublicProfileDetails)) Implements IKindleHubApiClient.FetchPublicProfilesAsync
        Dim ids = If(userIds, Enumerable.Empty(Of String)()).Where(Function(id) Regex.IsMatch(If(id, ""), "^[a-fA-F0-9]{16}$")).Distinct(StringComparer.OrdinalIgnoreCase).Take(100).ToList()
        Dim result As New Dictionary(Of String, PublicProfileDetails)(StringComparer.OrdinalIgnoreCase)
        If ids.Count = 0 Then Return result
        Dim filter = String.Join(",", ids.Select(Function(id) id.ToLowerInvariant()))
        Dim rows = Await GetRowsAsync("rest/v1/kh_presence?user_id=in.(" & filter & ")&select=user_id,avatar,profile&limit=100", cancellationToken)
        If rows Is Nothing Then Return result
        For Each row In rows
            Dim id = JsonStr(row, "user_id")
            If Not Regex.IsMatch(id, "^[a-fA-F0-9]{16}$") Then Continue For
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
        Dim term = If(query, "").Trim().Replace("%", "").Replace("_", "")
        If term.Length < 2 Then Return result
        Dim rows = Await GetRowsAsync("rest/v1/kh_users?email=ilike." & Uri.EscapeDataString("*" & term & "*") & "&select=hash,email&limit=20", cancellationToken)
        For Each row In rows
            Dim hash = JsonStr(row, "hash"), email = JsonStr(row, "email")
            ' The store keeps the full 64-character auth hash. The official client
            ' identifies a friend by its first 16 hex characters in inbox events.
            If hash.Length >= 16 AndAlso Regex.IsMatch(hash, "^[a-fA-F0-9]+$") AndAlso email.Contains("@") AndAlso Not authToken.StartsWith(hash, StringComparison.OrdinalIgnoreCase) Then
                result.Add(New FriendUser With {.Hash = hash.Substring(0, 16), .Name = email.Substring(0, email.IndexOf("@"c))})
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

    Private Overloads Async Function GetRowsAsync(url As String, secret As String, cancellationToken As CancellationToken) As Task(Of List(Of JsonElement))
        Using req As New HttpRequestMessage(HttpMethod.Get, url)
            req.Headers.TryAddWithoutValidation("Accept", "application/json")
            If Not String.IsNullOrEmpty(secret) Then req.Headers.TryAddWithoutValidation("X-KH-Secret", secret.ToLowerInvariant())
            Try
                Using resp = Await _httpClient.SendAsync(req, cancellationToken)
                    Dim body = Await resp.Content.ReadAsStringAsync(cancellationToken)
                    If Not resp.IsSuccessStatusCode Then
                        _logger.LogWarning("GET {Url} failed: {Status} {Body}", url, CInt(resp.StatusCode), LeftOf(body, 300))
                        Throw New ServerException($"API request failed: {resp.StatusCode}", CInt(resp.StatusCode))
                    End If
                    If String.IsNullOrWhiteSpace(body) Then Return New List(Of JsonElement)()
                    Dim doc = JsonDocument.Parse(body)
                    If doc.RootElement.ValueKind = JsonValueKind.Array Then
                        Return doc.RootElement.EnumerateArray().ToList()
                    End If
                    ' A PGRST116 single-object error body can still be a 4xx; treat as empty
                    Return New List(Of JsonElement) From {doc.RootElement}
                End Using
            Catch ex As ServerException
                Throw
            Catch ex As Exception
                If TypeOf ex Is OperationCanceledException Then Throw
                _logger.LogWarning(ex, "GET {Url} threw", url)
                Throw New NetworkException("Could not reach the server: " & ex.Message, ex)
            End Try
        End Using
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

    Private Async Function PostInternalAsync(url As String, payload As Object, headers As Dictionary(Of String, String), cancellationToken As CancellationToken) As Task(Of String)
        Using req As New HttpRequestMessage(HttpMethod.Post, url)
            req.Content = New StringContent(JsonSerializer.Serialize(payload, JsonOpts()), Encoding.UTF8, "application/json")
            ApplyHeaders(req, headers)
            Using resp = Await _httpClient.SendAsync(req, cancellationToken)
                Dim body = Await resp.Content.ReadAsStringAsync(cancellationToken)
                If Not resp.IsSuccessStatusCode Then
                    _logger.LogWarning("POST {Url} failed: {Status} {Body}", url, CInt(resp.StatusCode), LeftOf(body, 300))
                    Throw New ServerException(SafeErr(body, resp), CInt(resp.StatusCode))
                End If
                Return body
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
                    _logger.LogWarning("PATCH {Url} failed: {Status} {Body}", url, CInt(resp.StatusCode), LeftOf(body, 300))
                    Return False
                End If
                Return True
            End Using
        End Using
    End Function

    Private Async Function DeleteAsync(url As String, ownerSecret As String, cancellationToken As CancellationToken) As Task(Of Boolean)
        Using req As New HttpRequestMessage(HttpMethod.Delete, url)
            If Not String.IsNullOrEmpty(ownerSecret) Then req.Headers.TryAddWithoutValidation("X-KH-Secret", ownerSecret)
            Using resp = Await _httpClient.SendAsync(req, cancellationToken)
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
