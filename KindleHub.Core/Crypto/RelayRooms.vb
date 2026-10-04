Imports System.IO
Imports System.Linq
Imports System.Text.Json

''' <summary>
''' Port of the official helpers that don't belong to any one request:
'''  - _khInboxRoomCode (kh-app.js v7448): each party gets a permanent private
'''    relay room 800000 + six digits; digit i = (parseInt(uid[i],16) mod 10) of
'''    the user's 16-hex user id. The recipient's inbox is where DM_INVITE
'''    events land (v7451 guarantees the room exists via _groupCreate).
'''  - Report ids: rep_<ms>_<rand> for message reports, urep_... for username
'''    reports (kh-app.js kh_feedback inserts).
''' </summary>
Public Module RelayRooms
    ''' <summary>800000 + 6 digits derived from the first six hex chars of a user id.</summary>
    Public Function InboxRoomCodeFor(userId As String) As String
        Dim uid = If(userId, "").ToLowerInvariant()
        If uid.Length < 6 Then Return ""
        Dim sb As New System.Text.StringBuilder("800000")
        For i = 0 To 5
            Dim v = AscW(uid(i))
            Dim hexVal = If(v >= AscW("a"c), v - AscW("a"c) + 10, v - AscW("0"c))
            If hexVal < 0 OrElse hexVal > 15 Then hexVal = 0
            sb.Append(CStr(hexVal Mod 10))
        Next
        Return sb.ToString()
    End Function

    Public Function ReportId(kind As String) As String
        Return kind & "_" & DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() & "_" & Random.Shared.Next(10000)
    End Function

    ''' <summary>One of this device's own published apps: id, display name, and the
    ''' owner_secret the server gave back at publish time. The secret is needed to
    ''' re-fetch the row while it is still review='pending' (the public catalogue
    ''' hides those rows, so a plain DownloadAppAsync 404s on them).</summary>
    Public Class OwnAppRecord
        Public Property Id As String
        Public Property Name As String
        Public Property OwnerSecret As String
    End Class
End Module

''' <summary>
''' Local stores for the two chat features the official app keeps device-side
''' (never a server column): starred rows and self-reported rows, so the same
''' message isn't reported twice and stars survive restarts.
''' </summary>
Public Class ChatPrefsStore
    Implements IDisposable

    Public Shared ReadOnly Current As ChatPrefsStore = New ChatPrefsStore(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "KindleHubPro", "chat_prefs.json"))

    Private ReadOnly _path As String
    Private ReadOnly _lock As New Object()
    Private ReadOnly _starred As New HashSet(Of String)(StringComparer.Ordinal)
    Private ReadOnly _reported As New Dictionary(Of String, String)(StringComparer.Ordinal)
    ''' <summary>Dm room map: target user id (case-insensitive) -> shared 12-digit room code.</summary>
    Private ReadOnly _dmRooms As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
    ''' <summary>Apps published from this client (id -> name), so the author can manage/remove them.</summary>
    Private ReadOnly _ownApps As New Dictionary(Of String, OwnAppRecord)(StringComparer.Ordinal)

    Public Sub New(storePath As String)
        _path = storePath
        Try
            Dim dir As String = System.IO.Path.GetDirectoryName(storePath)
            If Not String.IsNullOrEmpty(dir) Then Directory.CreateDirectory(dir)
            If System.IO.File.Exists(storePath) Then
                Dim doc = JsonDocument.Parse(File.ReadAllText(storePath))
                Dim root = doc.RootElement
                If root.TryGetProperty("starred", Nothing) Then
                    For Each s In root.GetProperty("starred").EnumerateArray()
                        If s.ValueKind = JsonValueKind.String Then _starred.Add(s.GetString())
                    Next
                End If
                If root.TryGetProperty("reported", Nothing) Then
                    For Each p In root.GetProperty("reported").EnumerateObject()
                        _reported(p.Name) = If(p.Value.ValueKind = JsonValueKind.String, p.Value.GetString(), "")
                    Next
                End If
                If root.TryGetProperty("dmRooms", Nothing) Then
                    For Each p In root.GetProperty("dmRooms").EnumerateObject()
                        If p.Value.ValueKind = JsonValueKind.String Then _dmRooms(p.Name) = p.Value.GetString()
                    Next
                End If
                If root.TryGetProperty("myApps", Nothing) Then
                    For Each p In root.GetProperty("myApps").EnumerateObject()
                        If p.Value.ValueKind = JsonValueKind.Object Then
                            Dim rec As New OwnAppRecord With {
                                .Id = p.Name,
                                .Name = If(p.Value.TryGetProperty("name", Nothing) _
                                    AndAlso p.Value.GetProperty("name").ValueKind = JsonValueKind.String, _
                                    p.Value.GetProperty("name").GetString(), ""),
                                .OwnerSecret = If(p.Value.TryGetProperty("secret", Nothing) _
                                    AndAlso p.Value.GetProperty("secret").ValueKind = JsonValueKind.String, _
                                    p.Value.GetProperty("secret").GetString(), "")
                            }
                            _ownApps(rec.Id) = rec
                        End If
                    Next
                End If
            End If
        Catch
        End Try
    End Sub

    Public Function IsStarred(messageKey As String) As Boolean
        SyncLock _lock
            Return _starred.Contains(messageKey)
        End SyncLock
    End Function

    Public Function StarredKeys() As List(Of String)
        SyncLock _lock
            Return _starred.ToList()
        End SyncLock
    End Function

    ''' <summary>Returns the new star state.</summary>
    Public Function ToggleStar(messageKey As String) As Boolean
        SyncLock _lock
            If _starred.Contains(messageKey) Then
                _starred.Remove(messageKey)
                PersistLocked()
                Return False
            End If
            _starred.Add(messageKey)
            PersistLocked()
            Return True
        End SyncLock
    End Function

    Public Function ReportedReason(messageKey As String) As String
        SyncLock _lock
            Dim r As String = Nothing
            If _reported.TryGetValue(messageKey, r) Then Return r
            Return Nothing
        End SyncLock
    End Function

    Public Sub MarkReported(messageKey As String, reason As String)
        SyncLock _lock
            _reported(messageKey) = If(reason, "")
            PersistLocked()
        End SyncLock
    End Sub

    ''' <summary>Dm room map: target user id -> shared 12-digit room code (kh_dm_rooms analogue).</summary>
    Public Function DmRoomFor(userId As String) As String
        SyncLock _lock
            Dim v As String = Nothing
            If _dmRooms.TryGetValue(If(userId, "").Trim(), v) Then Return v
            Return Nothing
        End SyncLock
    End Function

    Public Sub RememberDmRoom(userId As String, roomCode As String)
        SyncLock _lock
            If String.IsNullOrEmpty(userId) OrElse String.IsNullOrEmpty(roomCode) Then Return
            _dmRooms(userId.Trim()) = roomCode
            PersistLocked()
        End SyncLock
    End Sub

    Public Function OwnApps() As List(Of OwnAppRecord)
        SyncLock _lock
            Return _ownApps.Values.ToList()
        End SyncLock
    End Function

    Public Sub RememberOwnApp(id As String, name As String, ownerSecret As String)
        SyncLock _lock
            If String.IsNullOrEmpty(id) Then Return
            _ownApps(id) = New OwnAppRecord With {.Id = id, .Name = If(name, ""), .OwnerSecret = If(ownerSecret, "")}
            PersistLocked()
        End SyncLock
    End Sub

    Public Sub ForgetOwnApp(id As String)
        SyncLock _lock
            _ownApps.Remove(id)
            PersistLocked()
        End SyncLock
    End Sub

    Private Sub PersistLocked()
        Try
            Dim snapshot = New With {
                Key .starred = _starred.ToList(),
                Key .reported = _reported.ToDictionary(Function(k) k.Key, Function(k) k.Value),
                Key .dmRooms = _dmRooms.ToDictionary(Function(k) k.Key, Function(k) k.Value),
                Key .myApps = _ownApps.ToDictionary(Function(k) k.Key, Function(k) k.Value)
            }
            File.WriteAllText(_path, JsonSerializer.Serialize(snapshot))
        Catch
        End Try
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        SyncLock _lock
            PersistLocked()
        End SyncLock
    End Sub
End Class
