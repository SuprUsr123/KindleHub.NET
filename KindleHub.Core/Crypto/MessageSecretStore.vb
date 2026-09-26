Imports System.Collections.Concurrent
Imports System.Threading
Imports System.IO
Imports System.Text.Json

''' <summary>
''' kh_messages rows are gated for edits/unsend by a per-row owner_secret that
''' the server NEVER returns on a read (its rowOut drops it). The token is
''' generated client-side at send time, so we must remember our own tokens to
''' edit later. Persisted under the user profile directory so it survives
''' restarts; Flush persists a snapshot on change + timer + explicit Save calls.
''' </summary>
Public Module MessageSecretStore
    Private ReadOnly Items As New ConcurrentDictionary(Of String, String)(StringComparer.Ordinal)
    Private _path As String
    Private _dirty As Boolean = False
    Private _timer As Timer

    Public Sub Initialise(path As String)
        _path = path
        Try
            If Not String.IsNullOrEmpty(path) AndAlso File.Exists(path) Then
                Dim loaded = JsonSerializer.Deserialize(Of Dictionary(Of String, String))(File.ReadAllText(path))
                If loaded IsNot Nothing Then
                    For Each kvp In loaded
                        Items(kvp.Key) = kvp.Value
                    Next
                End If
            End If
        Catch
        End Try
        _timer = New Timer(Sub() Flush(), Nothing, Timeout.Infinite, Timeout.Infinite)
        Try
            _timer.Change(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10))
        Catch
        End Try
    End Sub

    Public Function Key(groupCode As String, messageId As String) As String
        Return If(groupCode, "").Trim() & "|" & If(messageId, "").Trim()
    End Function

    Public Sub Note(groupCode As String, messageId As String, secret As String)
        If String.IsNullOrEmpty(messageId) OrElse String.IsNullOrEmpty(secret) Then Return
        Items(Key(groupCode, messageId)) = secret
        _dirty = True
    End Sub

    Public Function Lookup(groupCode As String, messageId As String) As String
        Dim secret As String = Nothing
        If Items.TryGetValue(Key(groupCode, messageId), secret) Then Return secret
        Return Nothing
    End Function

    Public Sub Flush()
        Try
            If String.IsNullOrEmpty(_path) OrElse Not _dirty Then Return
            Dim snapshot = New Dictionary(Of String, String)(StringComparer.Ordinal)
            For Each kv In Items.ToArray()
                snapshot(kv.Key) = kv.Value
            Next
            File.WriteAllText(_path, JsonSerializer.Serialize(snapshot))
            _dirty = False
        Catch
        End Try
    End Sub

    Public Sub Shutdown()
        Flush()
        If _timer IsNot Nothing Then _timer.Dispose()
    End Sub
End Module

''' <summary>Persisted "joined rooms" + last read + unread markers (localStorage analogue).</summary>
Public Class JoinedRoomsStore
    Public Class JoinedRecord
        Public Property Code As String
        Public Property Name As String
        Public Property JoinedAt As Long
        Public Property LastSeenId As String
        Public Property Unread As Integer = 0
    End Class

    ''' <summary>
    ''' Local cache of the rooms we've read since the last poll. Not a server truth.
    ''' (The server's kh_messages table never returns owner_secret or pinned flags.)
    ''' </summary>
    Public Property Rooms As New List(Of JoinedRecord)()

    Public Sub NoteSeen(code As String, name As String, lastSeenId As String)
        If String.IsNullOrEmpty(code) Then Return
        SetSeen(code, lastSeenId)
    End Sub

    Public Sub AddRoom(code As String, name As String)
        If String.IsNullOrEmpty(code) Then Return
        Dim idx = Rooms.FindIndex(Function(r) r.Code = code)
        If idx >= 0 Then
            Rooms(idx).Name = If(name, Rooms(idx).Name)
        Else
            Rooms.Add(New JoinedRecord With {.Code = code, .Name = If(name, ""), .JoinedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()})
        End If
    End Sub

    ''' <summary>Update the latest-read message id + unread count, given current newest id.</summary>
    Public Sub SetSeen(code As String, newestId As String)
        If String.IsNullOrEmpty(code) Then Return
        Dim rec = Rooms.Find(Function(r) r.Code = code)
        If rec Is Nothing Then
            rec = New JoinedRecord With {.Code = code, .JoinedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}
            Rooms.Add(rec)
        End If
        rec.LastSeenId = If(newestId, rec.LastSeenId)
    End Sub

    Public Function LastSeenId(code As String) As String
        Dim r = Rooms.Find(Function(x) x.Code = code)
        Return If(r?.LastSeenId, "")
    End Function
End Class
