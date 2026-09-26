Imports System.IO
Imports System.Text.Json

''' <summary>
''' Device-side session persistence so the desktop client doesn't make the user
''' re-enter credentials on every launch. Only the auth token + display fields
''' are stored — never the password (the token is a one-way SHA-256 digest, so
''' it can't be reversed to the password).
''' </summary>
Public Module SessionStore

    Private Function SessionPath() As String
        Return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                            "KindleHubPro", "session.json")
    End Function

    ''' <summary>True when a usable session file exists on disk.</summary>
    Public Function HasSavedSession() As Boolean
        Return File.Exists(SessionPath())
    End Function

    ''' <summary>Write the current session to disk (called after a successful login).</summary>
    Public Sub SaveSession(authToken As String, email As String, displayName As String, userId As String)
        Try
            Dim dir = Path.GetDirectoryName(SessionPath())
            If Not String.IsNullOrEmpty(dir) Then Directory.CreateDirectory(dir)
            Dim snap = New With {
                Key .authToken = authToken,
                Key .email = If(email, ""),
                Key .displayName = If(displayName, ""),
                Key .userId = If(userId, "")
            }
            File.WriteAllText(SessionPath(), JsonSerializer.Serialize(snap))
        Catch
            ' Session persistence is best-effort; never block a login on it.
        End Try
    End Sub

    ''' <summary>Read the saved session; Nothing when nothing is stored.</summary>
    Public Function LoadSession() As SavedSession
        Try
            If Not File.Exists(SessionPath()) Then Return Nothing
            Dim doc = JsonDocument.Parse(File.ReadAllText(SessionPath()))
            Dim root = doc.RootElement
            Dim tokEl As JsonElement, emailEl As JsonElement, dispEl As JsonElement, uidEl As JsonElement
            Dim authToken As String = "", email As String = "", disp As String = "", uid As String = ""
            If root.TryGetProperty("authToken", tokEl) AndAlso tokEl.ValueKind = JsonValueKind.String Then authToken = tokEl.GetString()
            If root.TryGetProperty("email", emailEl) AndAlso emailEl.ValueKind = JsonValueKind.String Then email = emailEl.GetString()
            If root.TryGetProperty("displayName", dispEl) AndAlso dispEl.ValueKind = JsonValueKind.String Then disp = dispEl.GetString()
            If root.TryGetProperty("userId", uidEl) AndAlso uidEl.ValueKind = JsonValueKind.String Then uid = uidEl.GetString()
            If String.IsNullOrEmpty(authToken) Then Return Nothing
            Return New SavedSession With {.AuthToken = authToken, .Email = email, .DisplayName = If(disp, email), .UserId = uid}
        Catch
            Return Nothing
        End Try
    End Function

    ''' <summary>Drop the saved session (called on explicit logout / failed validation).</summary>
    Public Sub ClearSession()
        Try
            If File.Exists(SessionPath()) Then File.Delete(SessionPath())
        Catch
        End Try
    End Sub
End Module

''' <summary>A deserialised saved session row.</summary>
Public Class SavedSession
    Public Property AuthToken As String
    Public Property Email As String
    Public Property DisplayName As String
    Public Property UserId As String
End Class