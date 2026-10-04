Imports System.Collections.Generic
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports Microsoft.Extensions.Logging
Imports System.Net.Http
Imports KindleHub.Core

''' <summary>
''' Client-side coordinator: auth + state, chat, topics, store, leaderboards, presence
''' and the multiplayer relay that rides over encrypted chat. Mirrors the behaviour of
''' the official kh-app.js surface so accounts interoperate with the web KindleHub.
''' </summary>
Public Class KindleHubCore
        Implements IDisposable

        Public Const GlobalGroupCode As String = KindleHubApiClient.GlobalGroupCode
        Public Const CrossChatGroupCode As String = KindleHubApiClient.CrossChatGroupCode
        Public Const OpenGamesLobby As String = KindleHubApiClient.OpenGamesLobby

        Private ReadOnly _apiClient As IKindleHubApiClient
        Private ReadOnly _logger As ILogger(Of KindleHubCore)
        Private ReadOnly _httpClient As HttpClient
        Private _currentProfile As UserProfile
        Private _currentAccountState As String
        Private _disposed As Boolean = False

        Public Event ProfileChanged As EventHandler(Of UserProfile)
        Public Event SyncStatusChanged As EventHandler(Of SyncState)
        Public Event MessageReceived As EventHandler(Of Message)
        Public Event LeaderboardUpdated As EventHandler(Of List(Of LeaderboardEntry))

        Public ReadOnly Property CurrentProfile As UserProfile
            Get
                Return _currentProfile
            End Get
        End Property

        Public ReadOnly Property CurrentAuthToken As String
            Get
                Return If(_currentProfile?.AuthToken, "")
            End Get
        End Property

        Public ReadOnly Property IsAuthenticated As Boolean
            Get
                Return _currentProfile IsNot Nothing AndAlso Not String.IsNullOrEmpty(_currentProfile.AuthToken)
            End Get
        End Property

        ''' <summary>The decrypted account-state JSON last synced for the signed-in account (Nothing if none).</summary>
        Public ReadOnly Property AccountStateJson As String
            Get
                Return _currentAccountState
            End Get
        End Property

        ''' <summary>
        ''' Replaces the locally held account state. Use this after mutating the parsed state so
        ''' the next <see cref="SyncAccountAsync"/> call pushes a matching cloud copy.
        ''' </summary>
        Public Sub SetAccountState(json As String)
            _currentAccountState = json
        End Sub

        ''' <summary>Reads the account-synced list of rooms opened in Messages.</summary>
        Public Function GetOpenedMessageRooms() As List(Of Group)
            Return AccountState.GetMessageRooms(_currentAccountState)
        End Function

        Public Function GetCommunityNotes() As List(Of CommunityNote)
            Return AccountState.GetNotes(_currentAccountState)
        End Function

        Public Function GetSavedFlipbooks() As List(Of SavedFlipbook)
            Return AccountState.GetSavedFlipbooks(_currentAccountState)
        End Function

        Public Async Function SaveCommunityFlipbookAsync(wire As String, cancellationToken As CancellationToken) As Task(Of Boolean)
            If Not IsAuthenticated OrElse ChatMedia.TryParseFlipbook(wire) Is Nothing Then Return False
            If Not MutateState(Function(s) AccountState.WithSavedFlipbook(s, wire)) Then Return False
            Return Await SyncAccountAsync(_currentAccountState, cancellationToken)
        End Function

        ''' <summary>Adds an opened chat room to the encrypted account vault and syncs it.</summary>
        Public Async Function SaveOpenedMessageRoomAsync(group As Group, cancellationToken As CancellationToken) As Task(Of Boolean)
            If Not IsAuthenticated OrElse group Is Nothing Then Return False
            If String.Equals(group.Code, GlobalGroupCode, StringComparison.Ordinal) OrElse
               String.Equals(group.Code, CrossChatGroupCode, StringComparison.Ordinal) OrElse
               group.Code.StartsWith("mp-", StringComparison.OrdinalIgnoreCase) OrElse
               group.Code.StartsWith("800000", StringComparison.Ordinal) Then Return True
            If Not MutateState(Function(s) AccountState.WithMessageRoom(s, group)) Then Return False
            Return Await SyncAccountAsync(_currentAccountState, cancellationToken)
        End Function

        Public Sub New(apiClient As IKindleHubApiClient, logger As ILogger(Of KindleHubCore), httpClient As HttpClient)
            _apiClient = apiClient
            _logger = logger
            _httpClient = httpClient
        End Sub

        ' ───────────────────── auth ─────────────────────
        Public Async Function RegisterAsync(username As String, password As String, cancellationToken As CancellationToken) As Task(Of AuthResult)
            _logger.LogInformation("Registering user: {Username}", username)
            Dim request = New RegisterRequest With {.Username = username, .Password = password}
            Dim result = Await _apiClient.RegisterAsync(request, cancellationToken)
            If result.Success Then
                Return Await LoginAsync(username, password, cancellationToken)
            End If
            Return result
        End Function

        Public Async Function LoginAsync(username As String, password As String, cancellationToken As CancellationToken) As Task(Of AuthResult)
            _logger.LogInformation("Logging in user: {Username}", username)
            Dim request = New LoginRequest With {.Username = username, .Password = password}
            Dim result = Await _apiClient.LoginAsync(request, cancellationToken)

            If result.Success Then
                Dim authToken = If(String.IsNullOrEmpty(result.AuthToken), KeyDerivation.DeriveAuthToken(username, password), result.AuthToken)
                Dim userId = KeyDerivation.DeriveUserId(authToken)
                Dim email = If(result.Username, "").Trim()

                _currentProfile = New UserProfile With {
                    .UserId = userId,
                    .Email = email,
                    .DisplayName = email,
                    .AuthToken = authToken,
                    .EncKey = authToken,
                    .SyncEnabled = True,
                    .ProfileJoined = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    .Tier = UserTier.Free
                }
                _currentAccountState = result.StateJson
                SessionStore.SaveSession(authToken, email, email, userId)
                RaiseEvent ProfileChanged(Me, _currentProfile)
            End If

            Return result
        End Function

        Public Sub Logout()
            _currentProfile = Nothing
            _currentAccountState = Nothing
            SessionStore.ClearSession()
            RaiseEvent ProfileChanged(Me, Nothing)
        End Sub

        ''' <summary>Try to restore a previously saved session without re-entering
        ''' credentials. Returns True when a valid session was restored.</summary>
        Public Async Function RestoreSessionAsync(cancellationToken As CancellationToken) As Task(Of Boolean)
            Dim saved = SessionStore.LoadSession()
            If saved Is Nothing OrElse String.IsNullOrEmpty(saved.AuthToken) Then Return False
            Try
                ' Re-verify the token is still live by pulling the account row.
                Dim state = Await _apiClient.FetchAccountAsync(saved.AuthToken, cancellationToken)
                Dim userId = KeyDerivation.DeriveUserId(saved.AuthToken)
                _currentProfile = New UserProfile With {
                    .UserId = If(String.IsNullOrEmpty(saved.UserId), userId, saved.UserId),
                    .Email = saved.Email,
                    .DisplayName = If(String.IsNullOrEmpty(saved.DisplayName), saved.Email, saved.DisplayName),
                    .AuthToken = saved.AuthToken,
                    .EncKey = saved.AuthToken,
                    .SyncEnabled = True,
                    .ProfileJoined = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    .Tier = UserTier.Free
                }
                _currentAccountState = If(String.IsNullOrEmpty(state), "{}", state)
                SessionStore.SaveSession(saved.AuthToken, saved.Email, saved.DisplayName, _currentProfile.UserId)
                RaiseEvent ProfileChanged(Me, _currentProfile)
                Return True
            Catch ex As Exception
                _logger.LogWarning(ex, "Saved session was no longer valid; clearing it.")
                SessionStore.ClearSession()
                Return False
            End Try
        End Function

        Public Sub UpdateDisplayName(newName As String)
            If _currentProfile IsNot Nothing Then
                _currentProfile.DisplayName = newName
                RaiseEvent ProfileChanged(Me, _currentProfile)
            End If
        End Sub

        ' ───────────────────── chat ─────────────────────
        Public Function SendMessageAsync(groupCode As String, text As String, cancellationToken As CancellationToken) As Task(Of Message)
            Return SendMessageAsync(groupCode, text, False, Nothing, cancellationToken)
        End Function

        Public Function SendMessageAsync(groupCode As String, text As String, important As Boolean, cancellationToken As CancellationToken) As Task(Of Message)
            Return SendMessageAsync(groupCode, text, important, Nothing, cancellationToken)
        End Function

        ''' <summary>Send a chat message. If replyTo targets an id in the room, this message
        ''' carries a reply_to column (the quoted-parent pointer the web client renders).</summary>
        Public Async Function SendMessageAsync(groupCode As String, text As String, important As Boolean, replyTo As String, cancellationToken As CancellationToken) As Task(Of Message)
            If Not IsAuthenticated Then Throw New AuthenticationException("Sign in to send messages.")
            Dim request = New SendMessageRequest With {
                .GroupCode = groupCode,
                .Text = text,
                .DisplayName = _currentProfile.DisplayName,
                .UserId = _currentProfile.UserId,
                .Important = important,
                .ReplyTo = replyTo,
                .Encrypted = True
            }
            Return Await _apiClient.SendMessageAsync(request, _currentProfile.AuthToken, cancellationToken)
        End Function

        Public Async Function FetchMessagesAsync(groupCode As String, limit As Integer, offset As Integer, cancellationToken As CancellationToken) As Task(Of List(Of Message))
            Dim request = New FetchMessagesRequest With {
                .GroupCode = groupCode,
                .Limit = limit,
                .Offset = offset,
                .Encrypted = True
            }
            Dim list = Await _apiClient.FetchMessagesAsync(request, If(_currentProfile?.AuthToken, ""), cancellationToken)
            For Each m In list
                RaiseEvent MessageReceived(Me, m)
            Next
            Return list
        End Function

        ''' <summary>Fetches a short overlapping time window for active-chat polling.</summary>
        Public Async Function FetchMessagesSinceAsync(groupCode As String, afterTimestamp As DateTimeOffset, limit As Integer, cancellationToken As CancellationToken) As Task(Of List(Of Message))
            Dim request = New FetchMessagesRequest With {
                .GroupCode = groupCode,
                .Limit = limit,
                .Encrypted = True,
                .AfterTimestamp = afterTimestamp
            }
            Dim list = Await _apiClient.FetchMessagesAsync(request, If(_currentProfile?.AuthToken, ""), cancellationToken)
            For Each m In list
                RaiseEvent MessageReceived(Me, m)
            Next
            Return list
        End Function

        Public Async Function EditMessageAsync(groupCode As String, messageId As String, ownerSecret As String, newText As String, cancellationToken As CancellationToken) As Task(Of Boolean)
            Return Await _apiClient.EditMessageAsync(groupCode, messageId, ownerSecret, newText, cancellationToken)
        End Function

        Public Async Function UnsendMessageAsync(messageId As String, ownerSecret As String, cancellationToken As CancellationToken) As Task(Of Boolean)
            Return Await _apiClient.UnsendMessageAsync(messageId, ownerSecret, cancellationToken)
        End Function

        Public Async Function ToggleImportantAsync(messageId As String, ownerSecret As String, important As Boolean, cancellationToken As CancellationToken) As Task(Of Boolean)
            Return Await _apiClient.ToggleImportantAsync(messageId, ownerSecret, important, cancellationToken)
        End Function

        Public Async Function ToggleReactionAsync(messageId As String, emoji As String, cancellationToken As CancellationToken) As Task(Of Boolean)
            Dim uid = If(_currentProfile?.UserId, "anon")
            Return Await _apiClient.ToggleReactionAsync(messageId, emoji, uid, If(_currentProfile?.AuthToken, ""), cancellationToken)
        End Function

        ' ───────────────────── groups / topics ─────────────────────
        Public Async Function LookupGroupAsync(code As String, cancellationToken As CancellationToken) As Task(Of Group)
            Return Await _apiClient.LookupGroupAsync(code, cancellationToken)
        End Function

        Public Async Function JoinGroupByCodeAsync(code As String, cancellationToken As CancellationToken) As Task(Of Group)
            Dim existing = Await _apiClient.LookupGroupAsync(code, cancellationToken)
            If existing IsNot Nothing Then Return existing
            Return Await _apiClient.CreateGroupAsync(New CreateGroupRequest With {
                .Code = code,
                .Name = "Room " & code,
                .Creator = If(_currentProfile?.DisplayName, "Reader")
            }, If(_currentProfile?.AuthToken, ""), cancellationToken)
        End Function

        Public Async Function FetchGroupsByCodesAsync(codes As IEnumerable(Of String), cancellationToken As CancellationToken) As Task(Of List(Of Group))
            Return Await _apiClient.FetchGroupsByCodesAsync(codes, cancellationToken)
        End Function

        Public Async Function ListOpenTopicsAsync(limit As Integer, cancellationToken As CancellationToken) As Task(Of List(Of TopicListing))
            Return Await _apiClient.ListTopicsAsync(If(_currentProfile?.AuthToken, ""), limit, cancellationToken)
        End Function

        Public Async Function CreateTopicAsync(title As String, blurb As String, categoryId As String, cancellationToken As CancellationToken) As Task(Of Group)
            Return Await _apiClient.CreateTopicAsync(New CreateTopicRequest With {
                .Title = title,
                .Blurb = blurb,
                .CategoryId = categoryId
            }, If(_currentProfile?.AuthToken, ""), If(_currentProfile?.DisplayName, "Reader"), cancellationToken)
        End Function

        ' ───────────────────── app store ─────────────────────
        Public Async Function FetchAppsAsync(category As String, search As String, limit As Integer, cancellationToken As CancellationToken) As Task(Of List(Of AppCatalog))
            Dim request = New FetchAppsRequest With {
                .Category = category,
                .Search = search,
                .Limit = limit
            }
            Return Await _apiClient.FetchAppsAsync(request, cancellationToken)
        End Function

Public Async Function DownloadAppAsync(appId As String, cancellationToken As CancellationToken) As Task(Of DownloadedApp)
        Return Await _apiClient.DownloadAppAsync(appId, cancellationToken)
    End Function

    ''' <summary>Re-download one of this device's own apps using its owner secret so the
    ''' row is served even while review='pending' (the public catalogue hides those rows).</summary>
    Public Async Function DownloadAppWithSecretAsync(appId As String, ownerSecret As String, cancellationToken As CancellationToken) As Task(Of DownloadedApp)
        Return Await _apiClient.DownloadAppWithSecretAsync(appId, ownerSecret, cancellationToken)
    End Function

        Public Async Function CountAppDownloadAsync(appId As String, cancellationToken As CancellationToken) As Task
            Await _apiClient.CountAppDownloadAsync(appId, cancellationToken)
        End Function

        Public Async Function PublishAppAsync(name As String, html As String, category As String, cancellationToken As CancellationToken) As Task(Of PublishedApp)
            If Not IsAuthenticated Then Throw New AuthenticationException("Sign in to publish an app.")
            If String.IsNullOrEmpty(html) Then Throw New ValidationException("The app HTML is empty.")
            If html.Length > 550000 Then Throw New ValidationException("App is too large (max ~512 KB), same as the web client.")
            Dim published = Await _apiClient.PublishAppAsync(New PublishAppRequest With {
                .Name = name,
                .Html = html,
                .Category = category,
                .Author = If(_currentProfile?.DisplayName, If(_currentProfile?.Email, "user"))
            }, _currentProfile.AuthToken, _currentProfile.AuthToken, cancellationToken)
            ' Remember it so the author can manage/delete it later (the server hides
            ' pending rows from the public catalogue, so there is no other handle).
            ChatPrefsStore.Current.RememberOwnApp(published.Id, published.Name, published.OwnerSecret)
            Return published
        End Function

        ''' <summary>This device's own published apps, with the owner_secret needed to
        ''' re-fetch a row while it is still review='pending'.</summary>
        Public Function OwnPublishedApps() As List(Of OwnAppRecord)
            Return ChatPrefsStore.Current.OwnApps()
        End Function

        Public Async Function UnpublishAppAsync(appId As String, cancellationToken As CancellationToken) As Task(Of Boolean)
            If Not IsAuthenticated Then Throw New AuthenticationException("Sign in first.")
            Dim ok = Await _apiClient.UnpublishAppAsync(appId, _currentProfile.AuthToken, cancellationToken)
            If ok Then ChatPrefsStore.Current.ForgetOwnApp(appId)
            Return ok
        End Function

        ' ───────────────────── leaderboard / presence ─────────────────────
        Public Async Function SubmitScoreAsync(game As String, score As Long, cancellationToken As CancellationToken) As Task(Of Boolean)
            If Not IsAuthenticated Then Throw New AuthenticationException("Sign in to post a score to the global leaderboard.")
            Dim request = New SubmitScoreRequest With {
                .Game = game,
                .Score = score,
                .DisplayName = _currentProfile.DisplayName,
                .UserId = _currentProfile.UserId
            }
            Return Await _apiClient.SubmitScoreAsync(request, _currentProfile.AuthToken, cancellationToken)
        End Function

        ' ───────────────────── mail ─────────────────────
        ''' <summary>Your whole mailbox — received and sent. The server returns both in
        ''' one read; the client splits them into folders.</summary>
        Public Async Function FetchMailAsync(cancellationToken As CancellationToken) As Task(Of List(Of MailItem))
            If Not IsAuthenticated Then Throw New AuthenticationException("Sign in to read your mail.")
            Return Await _apiClient.FetchMailAsync(_currentProfile.AuthToken, cancellationToken)
        End Function

        Public Async Function SendMailAsync(toUser As String, subject As String, body As String, replyTo As String, cancellationToken As CancellationToken) As Task(Of MailItem)
            If Not IsAuthenticated Then Throw New AuthenticationException("Sign in to send mail.")
            Dim request = New SendMailRequest With {
                .ToUser = toUser, .FromUser = _currentProfile.Email, .Subject = subject, .Body = body, .ReplyTo = replyTo
            }
            Return Await _apiClient.SendMailAsync(request, _currentProfile.AuthToken, cancellationToken)
        End Function

        ''' <summary>Unsend. The server only lets the sending account remove its own mail.</summary>
        Public Async Function UnsendMailAsync(mailId As String, cancellationToken As CancellationToken) As Task(Of Boolean)
            If Not IsAuthenticated Then Return False
            Return Await _apiClient.DeleteMailAsync(mailId, _currentProfile.AuthToken, cancellationToken)
        End Function

        Public Async Function FetchLeaderboardAsync(game As String, limit As Integer, cancellationToken As CancellationToken) As Task(Of List(Of LeaderboardEntry))
            Dim list = Await _apiClient.FetchScoresAsync(New FetchScoresRequest With {.Game = game, .Limit = limit}, cancellationToken)
            RaiseEvent LeaderboardUpdated(Me, list)
            Return list
        End Function

        Public Async Function ListKnownGamesAsync(cancellationToken As CancellationToken) As Task(Of List(Of String))
            Return Await _apiClient.ListKnownGamesAsync(cancellationToken)
        End Function

        Public Async Function PingPresenceAsync(gameRoom As String, cancellationToken As CancellationToken) As Task
            If Not IsAuthenticated Then Return
            Await _apiClient.PingPresenceAsync(_currentProfile.AuthToken, _currentProfile.DisplayName, gameRoom,
                                               CurrentPrefs().ProfileAvatar, AccountState.ProfilePresenceJson(_currentAccountState), cancellationToken)
        End Function

        Public Async Function FetchPresenceAsync(minutesActive As Integer, limit As Integer, cancellationToken As CancellationToken) As Task(Of List(Of PresenceEntry))
            Return Await _apiClient.FetchPresenceAsync(minutesActive, limit, cancellationToken)
        End Function

        Public Async Function FetchAvatarCodesAsync(userIds As IEnumerable(Of String), cancellationToken As CancellationToken) As Task(Of Dictionary(Of String, String))
            Return Await _apiClient.FetchAvatarCodesAsync(userIds, cancellationToken)
        End Function

        Public Async Function FetchPublicProfilesAsync(userIds As IEnumerable(Of String), cancellationToken As CancellationToken) As Task(Of Dictionary(Of String, PublicProfileDetails))
            Return Await _apiClient.FetchPublicProfilesAsync(userIds, cancellationToken)
        End Function

        Public Async Function FetchCloudSavesAsync(appId As String, room As String, limit As Integer, cancellationToken As CancellationToken) As Task(Of List(Of CloudSave))
            If Not IsAuthenticated Then Throw New AuthenticationException("Sign in to view cloud saves.")
            Return Await _apiClient.ListCloudSavesAsync(_currentProfile.AuthToken, appId, room, limit, cancellationToken)
        End Function

        Public Async Function SetRecoveryEmailAsync(email As String, cancellationToken As CancellationToken) As Task
            If Not IsAuthenticated Then Throw New AuthenticationException("Sign in to set a recovery email.")
            Await _apiClient.SetRecoveryEmailAsync(_currentProfile.AuthToken, email, cancellationToken)
            MutateState(Function(s) AccountState.WithText(s, "recoveryEmail", If(email, "").Trim()))
        End Function

        Public Function FetchModeratorStatsAsync(code As String, cancellationToken As CancellationToken) As Task(Of ModeratorStats)
            Return _apiClient.FetchModeratorStatsAsync(code, cancellationToken)
        End Function

        Public Function ClaimModeratorCodeAsync(code As String, cancellationToken As CancellationToken) As Task
            If Not IsAuthenticated Then Throw New AuthenticationException("Sign in first.")
            Return _apiClient.ClaimModeratorCodeAsync(_currentProfile.AuthToken, _currentProfile.DisplayName, code, cancellationToken)
        End Function

        Public Function SubmitModeratorApplicationAsync(timeUsing As String, ageRange As String, reason As String, priorExperience As String, cancellationToken As CancellationToken) As Task
            If Not IsAuthenticated Then Throw New AuthenticationException("Sign in first.")
            Return _apiClient.SubmitModeratorApplicationAsync(_currentProfile.AuthToken, _currentProfile.DisplayName, timeUsing, ageRange, reason, priorExperience, cancellationToken)
        End Function

        ' ───────────────────── multiplayer relay (over encrypted chat) ─────────────────────
        Public Function ShortCodeFrom(roomCode As String) As String
            Dim digits = New String((From c In If(roomCode, "") Where Char.IsDigit(c)).ToArray())
            If digits.Length >= 6 Then Return digits.Substring(digits.Length - 6)
            Return digits.PadLeft(6, "0"c).Substring(0, 6)
        End Function

        Public Function RoomFromShortCode(shortCode As String) As String
            Dim digits = New String((From c In If(shortCode, "") Where Char.IsDigit(c)).ToArray()).PadLeft(6, "0"c)
            Return "900000" & digits.Substring(0, 6)
        End Function

        Public Async Function OpenGameRoomAsync(gameName As String, cancellationToken As CancellationToken) As Task(Of String)
            ' KH_MP.createRoom: mint a 6-digit short code and ensure the mp-* room exists.
            Dim shortCode = RoomCodes.GenerateDigits(6)
            Dim room = RoomFromShortCode(shortCode)
            Try
                Await _apiClient.CreateGroupAsync(New CreateGroupRequest With {
                    .Code = room,
                    .Name = "mp-" & gameName & "-" & shortCode,
                    .Creator = If(_currentProfile?.DisplayName, "Reader")
                }, If(_currentProfile?.AuthToken, ""), cancellationToken)
            Catch
                ' Group already exists -> join anyway.
            End Try
            Return room
        End Function

        Public Async Function JoinGameRoomAsync(shortCode As String, gameName As String, cancellationToken As CancellationToken) As Task(Of String)
            Dim digits = New String((From c In If(shortCode, "") Where Char.IsDigit(c)).ToArray()).PadLeft(6, "0"c)
            Dim room = RoomFromShortCode(digits)
            Try
                Await _apiClient.CreateGroupAsync(New CreateGroupRequest With {
                    .Code = room,
                    .Name = "mp-" & If(gameName, "game") & "-" & digits.Substring(0, 6),
                    .Creator = If(_currentProfile?.DisplayName, "Reader")
                }, If(_currentProfile?.AuthToken, ""), cancellationToken)
            Catch
            End Try
            Return room
        End Function

        Public Async Function SendGameEventAsync(roomCode As String, payload As Object, cancellationToken As CancellationToken) As Task
            If Not IsAuthenticated Then Throw New AuthenticationException("Sign in to play multiplayer.")
            Dim json = System.Text.Json.JsonSerializer.Serialize(payload)
            Await _apiClient.SendRoomEventAsync(roomCode, json, _currentProfile.DisplayName, _currentProfile.AuthToken, cancellationToken)
        End Function

        Public Function PollGameEventsAsync(roomCode As String, cancellationToken As CancellationToken) As Task(Of List(Of RoomMessageEnvelope))
            Return _apiClient.PollRoomEventsAsync(roomCode, If(_currentProfile?.AuthToken, ""), 200, cancellationToken)
        End Function

        Public Function PollLobbyForOpenGamesAsync(cancellationToken As CancellationToken) As Task(Of List(Of OpenGameListing))
            Return PollLobbyForOpenGamesAsync(Nothing, cancellationToken)
        End Function

        Public Async Function PollLobbyForOpenGamesAsync(gameFilter As String, cancellationToken As CancellationToken) As Task(Of List(Of OpenGameListing))
            Dim auth = If(_currentProfile?.AuthToken, "")
            Dim events = Await _apiClient.PollRoomEventsAsync(OpenGamesLobby, auth, 200, cancellationToken)
            Dim fresh = DateTimeOffset.UtcNow.AddMinutes(-15).ToUnixTimeMilliseconds()
            Dim seen As New HashSet(Of String)(StringComparer.Ordinal)
            Dim myUid = If(_currentProfile?.UserId, "")
            Dim out As New List(Of OpenGameListing)()
            For Each ev In events
                If ev.Type <> "OPEN" Then Continue For
                Dim rs = ev.Str("roomShort")
                If rs Is Nothing OrElse Not System.Text.RegularExpressions.Regex.IsMatch(rs, "^[0-9]{6}$") Then Continue For
                If seen.Contains(rs) Then Continue For
                seen.Add(rs)
                If Not String.IsNullOrEmpty(gameFilter) AndAlso Not String.Equals(ev.Str("game"), gameFilter, StringComparison.OrdinalIgnoreCase) Then Continue For
                Dim ts = ev.Number("ts")
                If ts <> 0 AndAlso ts < fresh Then Continue For
                If Not String.IsNullOrEmpty(myUid) AndAlso String.Equals(ev.Str("hostUserId"), myUid, StringComparison.Ordinal) Then Continue For
                out.Add(New OpenGameListing With {
                    .Game = ev.Str("game"),
                    .RoomShort = rs,
                    .Host = ev.Str("host"),
                    .AnnouncedAt = DateTimeOffset.FromUnixTimeMilliseconds(If(ts > 0, ts, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()))
                })
            Next
            Return out.OrderByDescending(Function(o) o.AnnouncedAt).ToList()
        End Function

        ' ───────────────────── cloud state sync ─────────────────────
        Public Async Function SyncAccountAsync(stateJson As String, cancellationToken As CancellationToken) As Task(Of Boolean)
            If Not IsAuthenticated Then Throw New AuthenticationException("Not authenticated")
            Try
                Dim syncedAt = Await _apiClient.SyncAccountAsync(_currentProfile.AuthToken, _currentProfile.Email, stateJson, cancellationToken)
                _currentAccountState = stateJson
                Dim dt As DateTimeOffset
                If Not String.IsNullOrEmpty(syncedAt) AndAlso DateTimeOffset.TryParse(syncedAt, dt) Then
                    _currentProfile.UpdatedAt = dt
                End If
                Return True
            Catch ex As Exception
                _logger.LogWarning(ex, "Cloud sync failed")
                Return False
            End Try
        End Function

        Public Async Function FetchAccountStateAsync(cancellationToken As CancellationToken) As Task(Of String)
            If Not IsAuthenticated Then Throw New AuthenticationException("Not authenticated")
            Return Await _apiClient.FetchAccountAsync(_currentProfile.AuthToken, cancellationToken)
        End Function

        Public Async Function CheckUserExistsAsync(username As String, cancellationToken As CancellationToken) As Task(Of Boolean)
            Return Await _apiClient.CheckUserExistsAsync(username, cancellationToken)
        End Function

        Public Async Function CheckServerStatusAsync(cancellationToken As CancellationToken) As Task(Of ServerStatus)
            Return Await _apiClient.GetServerStatusAsync(cancellationToken)
        End Function

        ' ═══════════════ reply previews ═══════════════
        ''' <summary>Fills Message.ReplyPreview for the given rows from one batched id read.</summary>
        Public Async Function HydrateReplyPreviewsAsync(messages As List(Of Message), groupCode As String, cancellationToken As CancellationToken) As Task
            If Not IsAuthenticated Then Return
            If messages Is Nothing OrElse messages.Count = 0 Then Return
            Dim missing = (From m In messages Where m.HasReply AndAlso String.IsNullOrEmpty(m.ReplyPreview) Select m.ReplyTo).Distinct(StringComparer.Ordinal).ToList()
            If missing.Count = 0 Then Return
            Dim parents = Await _apiClient.FetchMessagesByIdsAsync(missing, groupCode, _currentProfile.AuthToken, cancellationToken)
            Dim byId = parents.ToDictionary(Function(p) p.Id, StringComparer.Ordinal)
            For Each m In messages
                If Not m.HasReply Then Continue For
                If m.ReplyPreview IsNot Nothing Then Continue For
                Dim par As Message = Nothing
                If byId.TryGetValue(m.ReplyTo, par) Then
                    Dim snippet = If(par.IsImage, "[photo]", If(par.Text, ""))
                    snippet = snippet.Replace(vbCr, " "c).Replace(vbLf, " "c).Trim()
                    If snippet.Length > 90 Then snippet = snippet.Substring(0, 87) & "…"
                    m.ReplyPreview = (If(par.DisplayName, "?")) & ": " & snippet
                Else
                    m.ReplyPreview = "deleted message"
                End If
            Next
        End Function

        Public Function CanEditRow(m As Message) As Boolean
            Return m IsNot Nothing AndAlso m.IsMine AndAlso Not String.IsNullOrEmpty(m.OwnerSecret)
        End Function

        ' ═══════════════ reports (kh_feedback) ═══════════════
        Public ReadOnly Property MyInboxRoomCode As String
            Get
                If Not IsAuthenticated Then Return ""
                Return RelayRooms.InboxRoomCodeFor(_currentProfile.UserId)
            End Get
        End Property

        Public Async Function ReportMessageAsync(reason As String, note As String, msg As Message, groupName As String, cancellationToken As CancellationToken) As Task(Of Boolean)
            Dim reporterName = If(_currentProfile?.DisplayName, If(_currentProfile?.Email, "reader"))
            Dim ok = Await _apiClient.ReportMessageAsync(reason, note, msg, groupName, reporterName, If(_currentProfile?.UserId, ""), cancellationToken)
            If ok Then ChatPrefsStore.Current.MarkReported(msg.GroupCode & "|" & msg.Id, reason)
            Return ok
        End Function

        Public Async Function ReportUsernameAsync(reason As String, note As String, displayName As String, userId As String, securityFlag As Boolean, room As Group, msgId As String, cancellationToken As CancellationToken) As Task(Of Boolean)
            Dim reporterName = If(_currentProfile?.DisplayName, If(_currentProfile?.Email, "reader"))
            Return Await _apiClient.ReportUsernameAsync(reason, note, displayName, userId, securityFlag,
                If(room?.Code, ""), If(room?.Name, ""), msgId, reporterName, If(_currentProfile?.UserId, ""), cancellationToken)
        End Function

        Public Function WasReported(roomCode As String, messageId As String) As String
            Return ChatPrefsStore.Current.ReportedReason(roomCode & "|" & messageId)
        End Function

        ' ═══════════════ direct messages ═══════════════
        ''' <summary>Opens (or re-uses) a DM with the given user: invites them via a DM_INVITE
        ''' envelope into their inbox room (the official sendDmInvite flow) and returns the
        ''' shared room. If we already have a shared room for them, nothing is sent.</summary>
        Public Async Function OpenDmAsync(targetUserId As String, targetName As String, sharedRoomCode As String, cancellationToken As CancellationToken, Optional forceInvite As Boolean = False) As Task(Of Group)
            If Not IsAuthenticated Then Throw New AuthenticationException("Sign in first.")
            If String.IsNullOrEmpty(sharedRoomCode) Then sharedRoomCode = RoomCodes.NewRoomCode()
            Dim meName = If(_currentProfile?.DisplayName, If(_currentProfile?.Email, "Reader"))
            If Not forceInvite AndAlso Not String.IsNullOrEmpty(sharedRoomCode) Then
                Dim existing = Await _apiClient.LookupGroupAsync(sharedRoomCode, cancellationToken)
                If existing IsNot Nothing Then Return existing
            End If

            ' Ensure the shared room exists (plain insert; 409 = already there).
            Dim group = Await _apiClient.CreateGroupAsync(New CreateGroupRequest With {
                .Code = sharedRoomCode,
                .Name = "DM: " & Left(meName, 20) & " & " & Left(If(targetName, "friend"), 20),
                .Creator = meName
            }, _currentProfile.AuthToken, cancellationToken)

            ' Deliver the invite into the recipient's inbox room (their first DM with us).
            Dim inbox = RelayRooms.InboxRoomCodeFor(targetUserId)
            If inbox.Length > 0 Then
                Try
                    ' recipients need their inbox row to exist for us to send them anything
                    Await _apiClient.CreateGroupAsync(New CreateGroupRequest With {
                        .Code = inbox, .Name = "inbox-" & Left(targetUserId, 8), .Creator = If(targetName, "reader")
                    }, _currentProfile.AuthToken, cancellationToken)
                Catch
                End Try
                Dim evt = New With {
                    Key .type = "DM_INVITE",
                    Key .code = sharedRoomCode,
                    Key .name = "DM: " & Left(meName, 20) & " & " & Left(If(targetName, "friend"), 20),
                    Key .fromName = meName,
                    Key .fromUserId = _currentProfile.UserId
                }
                Try
                    Await _apiClient.SendRoomEventAsync(inbox, System.Text.Json.JsonSerializer.Serialize(evt), meName, _currentProfile.AuthToken, cancellationToken)
                Catch
                End Try
            End If
            Return group
        End Function

        ''' <summary>Polls MY inbox room for DM_INVITEs written since `since` and auto-joins
        ''' (adds + returns) any shared rooms we have not opened yet (the _khDmWatch reader).</summary>
        Public Async Function PollInboxForInvitesAsync(since As DateTimeOffset, cancellationToken As CancellationToken) As Task(Of List(Of Group))
            Dim found As New List(Of Group)()
            If Not IsAuthenticated Then Return found
            Dim inbox = MyInboxRoomCode
            If inbox.Length = 0 Then Return found
            Dim myUid = If(_currentProfile.UserId, "")
            Dim evts = Await _apiClient.PollRoomEventsAsync(inbox, _currentProfile.AuthToken, 200, cancellationToken)
            For Each ev In evts
                If ev.Type <> "DM_INVITE" Then Continue For
                If ev.Message.IsMine Then Continue For
                If ev.Message.Timestamp < since Then Continue For
                Dim code = ev.Str("code").Trim()
                If Not System.Text.RegularExpressions.Regex.IsMatch(code, "^[0-9]{12}$") Then Continue For
                If RoomIsOpen(code) Then Continue For ' already added to the registry
                Dim g = Await _apiClient.LookupGroupAsync(code, cancellationToken)
                If g Is Nothing Then
                    g = New Group With {.Code = code, .Name = ev.Str("name"), .Creator = ev.Str("fromName")}
                End If
                found.Add(g)
            Next
            Return found
        End Function

        Public Function SearchFriendUsersAsync(query As String, cancellationToken As CancellationToken) As Task(Of List(Of FriendUser))
            If Not IsAuthenticated Then Return Task.FromResult(New List(Of FriendUser)())
            Return _apiClient.SearchFriendUsersAsync(query, _currentProfile.AuthToken, cancellationToken)
        End Function

        Public Async Function SendFriendInboxEventAsync(targetHash As String, eventType As String, cancellationToken As CancellationToken) As Task
            If Not IsAuthenticated Then Throw New AuthenticationException("Sign in to manage friends.")
            Dim hash = If(targetHash, "").ToLowerInvariant()
            If Not System.Text.RegularExpressions.Regex.IsMatch(hash, "^[a-f0-9]{6,16}$") Then Throw New ArgumentException("Invalid friend account.")
            Dim suffix As String = ""
            For i = 0 To 5
                suffix &= (Convert.ToInt32(hash(i).ToString(), 16) Mod 10).ToString()
            Next
            Dim inbox = "800000" & suffix
            Try
                Await _apiClient.CreateGroupAsync(New CreateGroupRequest With {.Code = inbox, .Name = "inbox-" & hash.Substring(0, 8), .Creator = _currentProfile.DisplayName}, _currentProfile.AuthToken, cancellationToken)
            Catch
            End Try
            Dim payload = New With {.type = eventType, .fromName = _currentProfile.DisplayName, .fromUserId = _currentProfile.UserId, .fromHash = _currentProfile.AuthToken.Substring(0, Math.Min(16, _currentProfile.AuthToken.Length))}
            Await _apiClient.SendRoomEventAsync(inbox, System.Text.Json.JsonSerializer.Serialize(payload), _currentProfile.DisplayName, _currentProfile.AuthToken, cancellationToken)
        End Function

        Private Function RoomIsOpen(code As String) As Boolean
            For Each r In RoomRegistry.Rooms
                If String.Equals(r?.Code, code, StringComparison.Ordinal) Then Return True
            Next
            Return False
        End Function

        ' ═══════════════ account-state preferences (the official web settings surface) ═══════════════
        Public Class AccountPrefs
            Public Property ProfileName As String
            Public Property ProfileAvatar As String
            Public Property FontSizePx As Integer
            Public Property Theme As String
            Public Property SimpleMode As Boolean
            Public Property SyncEnabled As Boolean
            Public Property NoteCount As Integer
            Public Property RecoveryEmail As String
        End Class

        Public Function CurrentPrefs() As AccountPrefs
            Dim s = If(_currentAccountState, "")
            Return New AccountPrefs With {
                .ProfileName = AccountState.GetText(s, "profileName", If(_currentProfile?.DisplayName, "")),
                .ProfileAvatar = AccountState.GetText(s, "profileAvatar", ""),
                .FontSizePx = AccountState.GetNumber(s, "fontSize", 16),
                .Theme = AccountState.GetText(s, "theme", "light"),
                .SimpleMode = AccountState.GetFlag(s, "simpleMode", False),
                .SyncEnabled = AccountState.GetFlag(s, "syncEnabled", True),
                .RecoveryEmail = AccountState.GetText(s, "recoveryEmail", ""),
                .NoteCount = AccountState.CountNotes(s)
            }
        End Function

        Public Function SetProfileName(value As String) As Boolean
            Return MutateState(Function(s) AccountState.WithText(s, "profileName", If(value, "")))
        End Function

        Public Function SetProfileAvatar(value As String) As Boolean
            Dim avatar = If(value, "")
            If avatar.Length > 400 OrElse (avatar <> "" AndAlso Not (avatar.StartsWith("KHAV1:", StringComparison.Ordinal) OrElse avatar.StartsWith("KHAV2:", StringComparison.Ordinal))) Then Return False
            Return MutateState(Function(s) AccountState.WithText(s, "profileAvatar", avatar))
        End Function

        Public Function SetFontSize(valuePx As Integer) As Boolean
            Return MutateState(Function(s) AccountState.WithNumber(s, "fontSize", valuePx))
        End Function

        Public Function SetTheme(theme As String) As Boolean
            Dim allowed = {"light", "dark", "sepia"}
            If Not allowed.Contains(theme.ToLowerInvariant()) Then Return False
            Return MutateState(Function(s) AccountState.WithText(s, "theme", theme.ToLowerInvariant()))
        End Function

        Public Function SetSimpleMode(isOn As Boolean) As Boolean
            Return MutateState(Function(s) AccountState.WithFlag(s, "simpleMode", isOn))
        End Function

        Public Function SetSyncEnabled(isOn As Boolean) As Boolean
            If Not MutateState(Function(s) AccountState.WithFlag(s, "syncEnabled", isOn)) Then Return False
            If _currentProfile IsNot Nothing Then _currentProfile.SyncEnabled = isOn
            Return True
        End Function

        ''' <summary>Local-only state mutation (mirrors the official save-on-change pattern):
        ''' returns True when AccountStateJson changed; the next SyncAccount pushes it.</summary>
        Private Function MutateState(mutator As Func(Of String, String)) As Boolean
            If _currentAccountState Is Nothing Then _currentAccountState = "{}"
            Try
                Dim nextJson = mutator(_currentAccountState)
                If nextJson Is Nothing Then Return False
                _currentAccountState = nextJson
                Return True
            Catch
                Return False
            End Try
        End Function

        ''' <summary>Mirrors the web "Star → To notes" action: prepends a note row to local
        ''' state (S.notes.unshift) and pushes the merged account state to the vault.</summary>
        Public Async Function StarToNotesAsync(text As String, cancellationToken As CancellationToken) As Task(Of Boolean)
            If Not IsAuthenticated Then Throw New AuthenticationException("Sign in first.")
            If Not MutateState(Function(s) AccountState.AppendNote(s, text)) Then Return False
            Return Await SyncAccountAsync(_currentAccountState, cancellationToken)
        End Function

        ''' <summary>Re-loads the decrypted vault copy from the server (returns True if it exists).</summary>
        Public Async Function LoadAccountStateAsync(cancellationToken As CancellationToken) As Task(Of Boolean)
            If Not IsAuthenticated Then Return False
            Dim s = Await _apiClient.FetchAccountAsync(_currentProfile.AuthToken, cancellationToken)
            If String.IsNullOrEmpty(s) Then Return False
            _currentAccountState = s
            Return True
        End Function

        Public Sub Dispose() Implements IDisposable.Dispose
            If Not _disposed Then
                _disposed = True
                MessageSecretStore.Shutdown()
                _httpClient?.Dispose()
            End If
        End Sub
    End Class

    Public Class OpenGameListing
        Public Property Game As String
        Public Property RoomShort As String
        Public Property Host As String
        Public Property AnnouncedAt As DateTimeOffset
        Public ReadOnly Property RoomCode As String
            Get
                Return "900000" & RoomShort
            End Get
        End Property
        Public ReadOnly Property AnnouncedFormatted As String
            Get
                Return DisplayUtil.RelativeAge(AnnouncedAt)
            End Get
        End Property
    End Class

    Public Module KindleHubCoreFactory
        Public Function CreateCore(options As KindleHubApiOptions, loggerFactory As ILoggerFactory, secretStorePath As String) As KindleHubCore
            Try
                MessageSecretStore.Initialise(secretStorePath)
            Catch
            End Try
            Dim httpClient = New HttpClient()
            Dim logger = loggerFactory.CreateLogger(Of KindleHubCore)()
            Dim apiLogger = loggerFactory.CreateLogger(Of KindleHubApiClient)()
            Dim apiClient = New KindleHubApiClient(httpClient, options, apiLogger)
            Return New KindleHubCore(apiClient, logger, httpClient)
        End Function

        Public Function CreateCore(loggerFactory As ILoggerFactory, secretStorePath As String) As KindleHubCore
            Return CreateCore(New KindleHubApiOptions(), loggerFactory, secretStorePath)
        End Function
    End Module
