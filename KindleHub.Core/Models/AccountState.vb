Imports System.Collections.Generic
Imports System.Linq
Imports System.Text.Json
Imports System.Text.Json.Nodes

''' <summary>
''' Helpers over the (decrypted) account-state JSON, matching the official web state keys
''' used by the settings surface in kh-app.js / kh-views.js:
'''   profileName  display name shown everywhere (falls back to user / email)
'''   user         login name / fallback display
'''   fontSize     px: 13 / 16 / 19 / 22  (small / default / large / extra large)
'''   theme        "light" | "dark" | "sepia"
'''   simpleMode   Boolean
'''   syncEnabled  Boolean
'''   notes        array of {id,text,tags,date,pinned} — where "To notes" saves land
''' All operations are pure JSON merges so unknown web-client keys survive a round trip.
''' </summary>
Public Module AccountState

    Public Function GetText(stateJson As String, key As String, fallback As String) As String
        Dim root = TryParse(stateJson)
        If root Is Nothing Then Return fallback
        Dim v = root(key)
        If v Is Nothing OrElse v.GetValueKind() <> JsonValueKind.String Then Return fallback
        Return v.GetValue(Of String)()
    End Function

    Public Function GetNumber(stateJson As String, key As String, fallback As Integer) As Integer
        Dim root = TryParse(stateJson)
        If root Is Nothing Then Return fallback
        Dim v = root(key)
        If v Is Nothing OrElse v.GetValueKind() <> JsonValueKind.Number Then Return fallback
        Try
            Return CInt(Math.Truncate(v.GetValue(Of Double)()))
        Catch
            Return fallback
        End Try
    End Function

    Public Function GetFlag(stateJson As String, key As String, fallback As Boolean) As Boolean
        Dim root = TryParse(stateJson)
        If root Is Nothing Then Return fallback
        Dim v = root(key)
        If v Is Nothing Then Return fallback
        Select Case v.GetValueKind()
            Case JsonValueKind.True
                Return True
            Case JsonValueKind.False
                Return False
            Case Else
                Return fallback
        End Select
    End Function

    ''' <summary>Set a top-level key on the state; returns the new state JSON ("" state becomes {}).</summary>
    Public Function [With](stateJson As String, key As String, value As JsonNode) As String
        Dim root = TryParse(stateJson)
        If root Is Nothing Then root = New JsonObject()
        root(key) = value
        Return root.ToJsonString()
    End Function

    Public Function WithText(stateJson As String, key As String, value As String) As String
        Return [With](stateJson, key, JsonValue.Create(If(value, "")))
    End Function

    Public Function WithNumber(stateJson As String, key As String, value As Integer) As String
        Return [With](stateJson, key, JsonValue.Create(CDbl(value)))
    End Function

    Public Function WithFlag(stateJson As String, key As String, value As Boolean) As String
        Return [With](stateJson, key, JsonValue.Create(value))
    End Function

    ''' <summary>Adds a note at the front of S.notes with the official row shape
    ''' (id = base36 ms + "_" + 4 random digits, tags ["chat"], date ISO, pinned false).</summary>
    Public Function AppendNote(stateJson As String, text As String, Optional tags As String() = Nothing) As String
        Dim root = TryParse(stateJson)
        If root Is Nothing Then root = New JsonObject()

        Dim notes As JsonArray
        Dim existing = root("notes")
        If existing IsNot Nothing AndAlso existing.GetValueKind() = JsonValueKind.Array Then
            notes = existing.AsArray()
        Else
            notes = New JsonArray()
            root("notes") = notes
        End If

        If tags Is Nothing Then tags = {"chat"}
        Dim id = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString("x") & "_" & RoomCodes.GenerateDigits(4)
        Dim tagArr As New JsonArray()
        For Each t In tags
            tagArr.Add(JsonValue.Create(t))
        Next
        Dim note As New JsonObject From {
            {"id", JsonValue.Create(id)},
            {"text", JsonValue.Create(text)},
            {"tags", tagArr},
            {"date", JsonValue.Create(DateTimeOffset.UtcNow.ToString("O"))},
            {"pinned", JsonValue.Create(False)}
        }
        notes.Insert(0, note)
        While notes.Count > 300
            notes.RemoveAt(notes.Count - 1)
        End While
        Return root.ToJsonString()
    End Function

    Public Function CountNotes(stateJson As String) As Integer
        Dim root = TryParse(stateJson)
        If root Is Nothing Then Return 0
        Dim v = root("notes")
        If v Is Nothing OrElse v.GetValueKind() <> JsonValueKind.Array Then Return 0
        Return v.AsArray().Count
    End Function

    Private Function TryParse(stateJson As String) As JsonObject
        If String.IsNullOrWhiteSpace(stateJson) Then Return Nothing
        Try
            Dim node = JsonNode.Parse(stateJson)
            If TypeOf node Is JsonObject Then Return DirectCast(node, JsonObject)
        Catch
        End Try
        Return Nothing
    End Function
End Module
