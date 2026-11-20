Imports System.Globalization
Imports System.Security.Cryptography
Imports System.Text
Imports System.Text.RegularExpressions

''' <summary>In-memory unlock context for the web client's KHA2 password envelopes.
''' The data key and password are never written to the session file.</summary>
Public NotInheritable Class AccountEnvelope
    Private ReadOnly _prefix As String
    Private ReadOnly _key As String

    Private Sub New(prefix As String, key As String)
        _prefix = prefix
        _key = key
    End Sub

    Public Shared Function IsWrapped(packed As String) As Boolean
        Return If(packed, "").StartsWith("KHA2:", StringComparison.Ordinal) OrElse
               If(packed, "").StartsWith("KHSL1:", StringComparison.Ordinal)
    End Function

    Public Shared Function Open(packed As String, password As String, ByRef stateJson As String) As AccountEnvelope
        stateJson = Nothing
        If Not If(packed, "").StartsWith("KHA2:", StringComparison.Ordinal) Then Return Nothing
        Dim parts = packed.Substring(5).Split(":"c)
        If parts.Length <> 3 AndAlso parts.Length <> 5 Then Return Nothing
        Dim salt = parts(0)
        If Not Regex.IsMatch(salt, "^[0-9a-f]{16,64}$") OrElse salt.Length Mod 2 <> 0 Then Return Nothing
        Dim recovery = parts.Length = 5
        Dim iterationText = parts(1)
        If recovery Then
            If Not iterationText.StartsWith("R", StringComparison.Ordinal) Then Return Nothing
            iterationText = iterationText.Substring(1)
        End If
        Dim iterations As Integer
        If Not Integer.TryParse(iterationText, NumberStyles.None, CultureInfo.InvariantCulture, iterations) OrElse
           iterations < 1 OrElse iterations > 2000000 Then Return Nothing
        Try
            ' _khA2DeriveKey -> _khSlDeriveKey("kh-a2::" + password, "", salt, iters).
            Dim material = Encoding.UTF8.GetBytes("kh-a2::" & If(password, "") & ChrW(0))
            Dim derived = Rfc2898DeriveBytes.Pbkdf2(material, Convert.FromHexString(salt), iterations, HashAlgorithmName.SHA256, 32)
            Dim key = Convert.ToHexString(derived).ToLowerInvariant()
            Dim blob = parts(parts.Length - 1)
            If recovery Then
                key = Unbox(key, parts(2))
                If key Is Nothing Then Return Nothing
            End If
            stateJson = AccountEncryption.UnpackAccount(blob, key)
            If stateJson Is Nothing Then Return Nothing
            ' Preserve all wrapper metadata, including the untouched recovery box.
            Dim prefix = packed.Substring(0, packed.Length - blob.Length)
            Return New AccountEnvelope(prefix, key)
        Catch ex As CryptographicException
            Return Nothing
        Catch ex As FormatException
            Return Nothing
        End Try
    End Function

    Public Function Pack(stateJson As String) As String
        Return _prefix & AccountEncryption.PackAccount(stateJson, _key)
    End Function

    Public Function Unpack(packed As String) As String
        ' Metadata changed on another device: require the password again rather
        ' than silently retaining an obsolete recovery key or downgrading encryption.
        If Not If(packed, "").StartsWith(_prefix, StringComparison.Ordinal) Then Return Nothing
        Return AccountEncryption.UnpackAccount(packed.Substring(_prefix.Length), _key)
    End Function

    Private Shared Function Unbox(key As String, packed As String) As String
        Dim parts = packed.Split("."c)
        If parts.Length <> 2 Then Return Nothing
        Dim iv = Convert.FromBase64String(parts(0))
        Dim combined = Convert.FromBase64String(parts(1))
        If iv.Length <> 12 OrElse combined.Length < 16 Then Return Nothing
        Dim plain(combined.Length - 17) As Byte
        Using aes As New AesGcm(Convert.FromHexString(key), 16)
            aes.Decrypt(iv, combined.AsSpan(0, combined.Length - 16), combined.AsSpan(combined.Length - 16), plain)
        End Using
        Dim result = Encoding.UTF8.GetString(plain)
        Return If(Regex.IsMatch(result, "^[0-9a-f]{64}$"), result, Nothing)
    End Function
End Class
