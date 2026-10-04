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

    Public Function GetNotes(stateJson As String) As List(Of CommunityNote)
        Dim result As New List(Of CommunityNote)()
        Dim root = TryParse(stateJson)
        If root Is Nothing OrElse Not TypeOf root("notes") Is JsonArray Then Return result
        For Each item In DirectCast(root("notes"), JsonArray)
            If Not TypeOf item Is JsonObject Then Continue For
            Dim note As New CommunityNote With {
                .Id = NodeText(item("id")),
                .Text = NodeText(item("text")),
                .DateText = NodeText(item("date")),
                .Pinned = NodeFlag(item("pinned"))
            }
            If TypeOf item("tags") Is JsonArray Then
                For Each tag In DirectCast(item("tags"), JsonArray)
                    Dim value = NodeText(tag)
                    If Not String.IsNullOrWhiteSpace(value) Then note.Tags.Add(value)
                Next
            End If
            result.Add(note)
        Next
        Return result
    End Function

    Public Function GetSavedFlipbooks(stateJson As String) As List(Of SavedFlipbook)
        Dim result As New List(Of SavedFlipbook)()
        Dim root = TryParse(stateJson)
        If root Is Nothing OrElse Not TypeOf root("flipbooks") Is JsonArray Then Return result
        For Each item In DirectCast(root("flipbooks"), JsonArray)
            If Not TypeOf item Is JsonObject Then Continue For
            Dim fb As New ChatMedia.Flipbook With {
                .Name = NodeText(item("n")),
                .Width = NodeNumber(item("w")),
                .Height = NodeNumber(item("h")),
                .Fps = NodeNumber(item("fps"))
            }
            If Not TypeOf item("f") Is JsonArray Then Continue For
            fb.Frames = DirectCast(item("f"), JsonArray).Select(Function(frame) NodeText(frame)).ToArray()
            Dim wire = ChatMedia.EncodeFlipbook(fb)
            If String.IsNullOrEmpty(wire) Then Continue For
            result.Add(New SavedFlipbook With {
                .Id = NodeText(item("id")),
                .Name = If(String.IsNullOrWhiteSpace(fb.Name), "Flipbook", fb.Name),
                .Wire = wire,
                .SavedAt = NodeLong(item("at"))
            })
        Next
        Return result
    End Function

    Public Function WithSavedFlipbook(stateJson As String, wire As String) As String
        Dim fb = ChatMedia.TryParseFlipbook(wire)
        If fb Is Nothing Then Return If(stateJson, "{}")
        Dim root = TryParse(stateJson)
        If root Is Nothing Then root = New JsonObject()
        Dim books = TryCast(root("flipbooks"), JsonArray)
        If books Is Nothing Then
            books = New JsonArray()
            root("flipbooks") = books
        End If
        Dim frames As New JsonArray()
        For Each frame In fb.Frames
            frames.Add(JsonValue.Create(frame))
        Next
        books.Insert(0, New JsonObject From {
            {"id", JsonValue.Create(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString("x") & "_" & RoomCodes.GenerateDigits(4))},
            {"n", JsonValue.Create(If(String.IsNullOrWhiteSpace(fb.Name), "Flipbook", fb.Name))},
            {"w", JsonValue.Create(fb.Width)},
            {"h", JsonValue.Create(fb.Height)},
            {"fps", JsonValue.Create(fb.Fps)},
            {"f", frames},
            {"at", JsonValue.Create(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())}
        })
        While books.Count > 100
            books.RemoveAt(books.Count - 1)
        End While
        Return root.ToJsonString()
    End Function

    ''' <summary>Builds the compact public profile blob consumed by kh_presence.</summary>
    Public Function ProfilePresenceJson(stateJson As String) As String
        Dim profile As New JsonObject()
        Dim fields As (Source As String, Target As String, Limit As Integer)() = {
            ("profilePronouns", "p", 28), ("profileHobbies", "h", 160),
            ("profileBio", "b", 320), ("profileStatus", "s", 80),
            ("nameStyle", "ns", 8), ("profileFrame", "fr", 24)
        }
        For Each field In fields
            Dim value = GetText(stateJson, field.Source, "").Trim()
            If value.Length > 0 Then profile(field.Target) = JsonValue.Create(value.Substring(0, Math.Min(field.Limit, value.Length)))
        Next
        Return profile.ToJsonString()
    End Function

    Private Function NodeText(node As JsonNode) As String
        If node Is Nothing Then Return ""
        Try
            Return node.GetValue(Of String)()
        Catch
            Return ""
        End Try
    End Function

    Private Function NodeNumber(node As JsonNode) As Integer
        If node Is Nothing Then Return 0
        Try
            Return node.GetValue(Of Integer)()
        Catch
            Return 0
        End Try
    End Function

    Private Function NodeLong(node As JsonNode) As Long
        If node Is Nothing Then Return 0
        Try
            Return node.GetValue(Of Long)()
        Catch
            Return 0
        End Try
    End Function

    Private Function NodeFlag(node As JsonNode) As Boolean
        If node Is Nothing Then Return False
        Try
            Return node.GetValue(Of Boolean)()
        Catch
            Return False
        End Try
    End Function

    ''' <summary>Returns groups saved by the official client in the encrypted account-state vault.</summary>
    Public Function GetMessageRooms(stateJson As String) As List(Of Group)
        Dim result As New List(Of Group)()
        Dim root = TryParse(stateJson)
        If root Is Nothing Then Return result
        Dim rooms = root("msgGroups")
        If rooms Is Nothing OrElse rooms.GetValueKind() <> JsonValueKind.Array Then Return result
        For Each item In rooms.AsArray()
            If Not TypeOf item Is JsonObject Then Continue For
            Dim code = ""
            Dim name = ""
            Try
                code = item("code")?.GetValue(Of String)()
                name = item("name")?.GetValue(Of String)()
            Catch
            End Try
            If String.IsNullOrWhiteSpace(code) OrElse result.Any(Function(g) String.Equals(g.Code, code, StringComparison.Ordinal)) Then Continue For
            result.Add(New Group With {.Code = code, .Name = If(name, code)})
        Next
        Return result
    End Function

    ''' <summary>Add or rename a joined group using the official client's msgGroups schema.</summary>
    Public Function WithMessageRoom(stateJson As String, group As Group) As String
        If group Is Nothing OrElse String.IsNullOrWhiteSpace(group.Code) Then Return If(stateJson, "{}")
        Dim root = TryParse(stateJson)
        If root Is Nothing Then root = New JsonObject()
        Dim rooms = TryCast(root("msgGroups"), JsonArray)
        If rooms Is Nothing Then
            rooms = New JsonArray()
            root("msgGroups") = rooms
        End If
        Dim existing As JsonObject = Nothing
        For Each item In rooms
            If Not TypeOf item Is JsonObject Then Continue For
            Dim code = ""
            Try
                code = item("code")?.GetValue(Of String)()
            Catch
            End Try
            If String.Equals(code, group.Code, StringComparison.Ordinal) Then
                existing = DirectCast(item, JsonObject)
                Exit For
            End If
        Next
        If existing Is Nothing Then
            rooms.Add(New JsonObject From {
                {"code", JsonValue.Create(group.Code)},
                {"name", JsonValue.Create(If(group.Name, group.Code))},
                {"joinedAt", JsonValue.Create(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())}
            })
        Else
            existing("name") = JsonValue.Create(If(group.Name, group.Code))
        End If
        If TypeOf root("leftGroups") Is JsonArray Then
            Dim leftGroups = DirectCast(root("leftGroups"), JsonArray)
            For i = leftGroups.Count - 1 To 0 Step -1
                Try
                    If String.Equals(leftGroups(i)?.GetValue(Of String)(), group.Code, StringComparison.Ordinal) Then
                        leftGroups.RemoveAt(i)
                    End If
                Catch
                End Try
            Next
        End If
        Return root.ToJsonString()
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
