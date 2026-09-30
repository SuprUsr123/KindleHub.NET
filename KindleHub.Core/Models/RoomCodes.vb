Imports System.Security.Cryptography

''' <summary>Mirrors the official _khRoomDigits (random digit string of given length).</summary>
Public Module RoomCodes
    Public Const GlobalGroupCode As String = "000000000000"
    ''' <summary>Crosschat — the second global room the site exposes.</summary>
    Public Const CrossChatGroupCode As String = "000000000001"

    Public Function GenerateDigits(count As Integer) As String
        Dim sb As New System.Text.StringBuilder(count)
        Dim buf(Math.Max(count * 2, 32) - 1) As Byte
        While sb.Length < count
            RandomNumberGenerator.Fill(buf)
            For Each b In buf
                If b < 250 AndAlso sb.Length < count Then
                    sb.Append((b Mod 10).ToString())
                End If
            Next
        End While
        Return sb.ToString()
    End Function

    ''' <summary>12-digit room code for topics / DMs.</summary>
    Public Function NewRoomCode() As String
        Return GenerateDigits(12)
    End Function

    ''' <summary>Multiplayer room code (900000 + 6 digits).</summary>
    Public Function NewGameRoomCode() As String
        Return "900000" & GenerateDigits(6)
    End Function

    Public Function NewMessageId() As String
        ' Mirrors the official _newMsgId: Date.now().toString(36) + Math.random().toString(36).slice(2,6)
        Dim ms = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        Dim id = ToBase36(ms)
        ' Math.random().toString(36).slice(2, 6) -> 4 more base-36 chars from fresh randomness.
        For i = 1 To 4
            id &= Base36Alphabet(Random.Shared.Next(36))
        Next
        Return id
    End Function

    ''' <summary>
    ''' Base-36 string for a non-negative integer, most-significant digit first, using
    ''' 0-9 then a-z — identical to JavaScript's Number.prototype.toString(36). (The .NET
    ''' Convert.ToString overload only supports bases 2/8/10/16, so this is hand-rolled.)
    ''' </summary>
    Friend Function ToBase36(value As Long) As String
        If value = 0L Then Return "0"
        Dim negative = value < 0L
        Dim n = If(negative, -value, value)
        Dim sb As New System.Text.StringBuilder()
        While n > 0L
            sb.Insert(0, Base36Alphabet(CInt(n Mod 36L)))
            n \= 36L
        End While
        If negative Then sb.Insert(0, "-"c)
        Return sb.ToString()
    End Function

    Private Const Base36Alphabet As String = "0123456789abcdefghijklmnopqrstuvwxyz"
End Module
