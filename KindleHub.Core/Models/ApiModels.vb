Public Class RegisterRequest
    Public Property Username As String
    Public Property Password As String
    ''' <summary>Privacy-first default False: when True, an opt-in HIBP k-anonymity
    ''' check runs BEFORE registration — but it never blocks, it only sets
    ''' AuthResult.Warning. No password-derived data leaves the device otherwise.</summary>
    Public Property CheckPasswordLeaks As Boolean = False
End Class

Public Class LoginRequest
    Public Property Username As String
    Public Property Password As String
End Class

Public Class SubmitScoreRequest
    Public Property Game As String
    Public Property Score As Long
    Public Property DisplayName As String
    Public Property UserId As String
    ''' <summary>Optional replay evidence submitted alongside the run token (web parity:
    ''' e.g. hanoi posts its full move log so the server can re-verify the run).</summary>
    Public Property Proof As String
End Class

Public Class FetchScoresRequest
    Public Property Game As String
    Public Property Limit As Integer = 10
End Class

Public Class CreateGroupRequest
    Public Property Code As String
    Public Property Name As String
    Public Property Creator As String
End Class

Public Class SendMessageRequest
    Public Property GroupCode As String
    Public Property Text As String
    Public Property DisplayName As String
    Public Property UserId As String
    Public Property Important As Boolean = False
    ''' <summary>id of the message being replied to (kh_messages.reply_to on the server).</summary>
    Public Property ReplyTo As String
    ''' <summary>Multiplayer sends pass False: the relay payload rides as plain JSON.</summary>
    Public Property Encrypted As Boolean = True
End Class

Public Class FetchMessagesRequest
    Public Property GroupCode As String
    Public Property Limit As Integer = 50
    Public Property Offset As Integer = 0
    Public Property AfterId As String
    Public Property AfterTimestamp As DateTimeOffset?
    ''' <summary>Decrypt text as chat ciphertext (True) or treat it as a plaintext relay event.</summary>
    Public Property Encrypted As Boolean = True
End Class

Public Class FetchAppsRequest
    Public Property Category As String
    Public Property Search As String
    Public Property Limit As Integer = 50
End Class

Public Class PublishAppRequest
    Public Property Name As String
    Public Property Category As String
    Public Property Description As String
    Public Property Html As String
    Public Property Author As String
    Public Property AgeRating As String = "Everyone"
End Class

''' <summary>Open-ended key/value row shared by topic directory and app game state.</summary>
Public Class RoomEventEnvelope
    Public Property Type As String
    Public Property Game As String
    Public Property RoomShort As String
    Public Property Host As String
    Public Property HostUserId As String
    Public Property T As Long
End Class
