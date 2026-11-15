Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text.Json
Imports System.Threading
Imports System.Threading.Tasks

''' <summary>
''' Web parity (kh_score_q): scores that could not be posted are queued per game,
''' keeping the BEST score only, LRU-capped at 60 entries, persisted to disk and
''' flushed after sign-in/boot and when the network comes back.
''' </summary>
Public Class OfflineScoreQueue
    Implements IDisposable

    Public Const MaxEntries As Integer = 60

    Private Class QueuedScore
        Public Property Game As String
        Public Property Score As Long
        Public Property DisplayName As String
        Public Property Proof As String
        Public Property EnqueuedAt As DateTimeOffset
    End Class

    Private ReadOnly _path As String
    Private ReadOnly _lock As New Object()
    Private ReadOnly _entries As New Dictionary(Of String, QueuedScore)(StringComparer.Ordinal)

    Public Shared ReadOnly Current As OfflineScoreQueue = New OfflineScoreQueue(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "KindleHubPro", "score_queue.json"))

    Public Sub New(storePath As String)
        _path = storePath
        Try
            Dim dir = Path.GetDirectoryName(storePath)
            If Not String.IsNullOrEmpty(dir) Then Directory.CreateDirectory(dir)
            If File.Exists(storePath) Then
                Dim doc = JsonDocument.Parse(File.ReadAllText(storePath))
                For Each el In doc.RootElement.EnumerateArray()
                    If el.ValueKind <> JsonValueKind.Object Then Continue For
                    Dim q As New QueuedScore With {
                        .Game = If(el.TryGetProperty("game", Nothing) AndAlso el.GetProperty("game").ValueKind = JsonValueKind.String, el.GetProperty("game").GetString(), ""),
                        .Score = If(el.TryGetProperty("score", Nothing) AndAlso el.GetProperty("score").ValueKind = JsonValueKind.Number, el.GetProperty("score").GetInt64(), 0L),
                        .DisplayName = If(el.TryGetProperty("display_name", Nothing) AndAlso el.GetProperty("display_name").ValueKind = JsonValueKind.String, el.GetProperty("display_name").GetString(), ""),
                        .Proof = If(el.TryGetProperty("proof", Nothing) AndAlso el.GetProperty("proof").ValueKind = JsonValueKind.String, el.GetProperty("proof").GetString(), ""),
                        .EnqueuedAt = DateTimeOffset.UtcNow
                    }
                    If Not String.IsNullOrEmpty(q.Game) Then _entries(q.Game.ToLowerInvariant()) = q
                Next
            End If
        Catch
        End Try
    End Sub

    Public Function PendingCount() As Integer
        SyncLock _lock
            Return _entries.Count
        End SyncLock
    End Function

    ''' <summary>Queues a failed score; an existing entry for the same game is kept
    ''' only when the new score beats it (web parity: best-score-only per game).</summary>
    Public Sub Enqueue(game As String, score As Long, displayName As String, Optional proof As String = Nothing)
        If String.IsNullOrWhiteSpace(game) OrElse score < 1 Then Return
        SyncLock _lock
            Dim key = game.Trim().ToLowerInvariant()
            Dim existing As QueuedScore = Nothing
            If _entries.TryGetValue(key, existing) AndAlso existing.Score >= score Then Return
            _entries(key) = New QueuedScore With {
                .Game = game.Trim(),
                .Score = score,
                .DisplayName = If(displayName, "Reader"),
                .Proof = proof,
                .EnqueuedAt = DateTimeOffset.UtcNow
            }
            While _entries.Count > MaxEntries
                Dim oldest = _entries.OrderBy(Function(kv) kv.Value.EnqueuedAt).First().Key
                _entries.Remove(oldest)
            End While
            PersistLocked()
        End SyncLock
    End Sub

    ''' <summary>Attempts to post every queued score; successfully posted entries are
    ''' removed. Returns the number of scores still queued.</summary>
    Public Async Function FlushAsync(apiClient As IKindleHubApiClient, authToken As String, cancellationToken As CancellationToken) As Task(Of Integer)
        If apiClient Is Nothing OrElse String.IsNullOrEmpty(authToken) Then Return PendingCount()
        Dim snapshot As List(Of KeyValuePair(Of String, QueuedScore))
        SyncLock _lock
            snapshot = _entries.ToList()
        End SyncLock
        For Each kv In snapshot
            Dim key = kv.Key
            Dim q = kv.Value
            Dim posted = False
            Try
                posted = Await apiClient.SubmitScoreAsync(New SubmitScoreRequest With {
                    .Game = q.Game, .Score = q.Score, .DisplayName = q.DisplayName, .Proof = q.Proof
                }, authToken, cancellationToken)
            Catch ex As AuthenticationException
                Exit For ' wrong account for these queued scores; keep them
            Catch
                ' network/policy failure: leave the entry for the next flush
            End Try
            If posted Then
                SyncLock _lock
                    Dim current As QueuedScore = Nothing
                    If _entries.TryGetValue(key, current) AndAlso current.Score <= q.Score Then _entries.Remove(key)
                    PersistLocked()
                End SyncLock
            End If
        Next
        Return PendingCount()
    End Function

    Private Sub PersistLocked()
        Try
            Dim arr = _entries.Values.OrderBy(Function(q) q.EnqueuedAt).Select(Function(q) New With {
                Key .game = q.Game,
                Key .score = q.Score,
                Key .display_name = q.DisplayName,
                Key .proof = q.Proof
            }).ToArray()
            File.WriteAllText(_path, JsonSerializer.Serialize(arr))
        Catch
        End Try
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        SyncLock _lock
            PersistLocked()
        End SyncLock
    End Sub
End Class
