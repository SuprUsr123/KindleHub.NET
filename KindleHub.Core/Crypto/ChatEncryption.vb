Imports System.Security.Cryptography
Imports System.Text
Imports System.Threading.Tasks

Public Class ChatCryptoMessage
    Public Property Id As String
    Public Property DisplayName As String
    Public Property GroupCode As String
    Public Property IsImportant As Boolean
    Public Property Text As String
    Public Property Timestamp As String
    Public Property OwnerSecret As String
End Class

Public Module ChatEncryption
    Private Const PrefixRaw As String = "enc1:"
    Private Const PrefixGzip As String = "enc2:"

    Public Function NewSecret() As String
        Dim bytes(31) As Byte
        RandomNumberGenerator.Fill(bytes)
        Return Convert.ToBase64String(bytes)
    End Function

    Public Async Function EncryptAsync(groupCode As String, plaintext As String) As Task(Of String)
        Return Await Task.Run(Function() Encrypt(groupCode, plaintext))
    End Function

    Public Function Encrypt(groupCode As String, plaintext As String) As String
        plaintext = If(plaintext, "")
        Dim key = DeriveKey(groupCode)
        Dim raw = Encoding.UTF8.GetBytes(plaintext)

        Dim payload = raw
        Dim marker = PrefixRaw
        Dim gz = GzipUtil.Gzip(raw)
        If gz IsNot Nothing AndAlso gz.Length > 0 AndAlso gz.Length < raw.Length Then
            payload = gz
            marker = PrefixGzip
        End If

        Dim iv(11) As Byte
        RandomNumberGenerator.Fill(iv)
        Dim cipher(payload.Length - 1) As Byte
        Dim tag(15) As Byte
        Using aes As New AesGcm(key, 16)
            aes.Encrypt(iv, payload, cipher, tag)
        End Using

        Dim combined(cipher.Length + 15) As Byte
        Buffer.BlockCopy(cipher, 0, combined, 0, cipher.Length)
        Buffer.BlockCopy(tag, 0, combined, cipher.Length, 16)

        Return marker & Convert.ToBase64String(iv) & "." & Convert.ToBase64String(combined)
    End Function

    Public Async Function DecryptAsync(groupCode As String, cipherText As String) As Task(Of String)
        Return Await Task.Run(Function() Decrypt(groupCode, cipherText))
    End Function

    Public Function Decrypt(groupCode As String, cipherText As String) As String
        If cipherText Is Nothing Then Return Nothing
        Dim isGzip = cipherText.StartsWith(PrefixGzip, StringComparison.Ordinal)
        Dim isRaw = cipherText.StartsWith(PrefixRaw, StringComparison.Ordinal)
        If Not isGzip AndAlso Not isRaw Then Return cipherText

        Try
            Dim parts = cipherText.Substring(PrefixRaw.Length).Split("."c)
            If parts.Length <> 2 Then Return cipherText
            Dim key = DeriveKey(groupCode)
            Dim iv = Convert.FromBase64String(parts(0))
            Dim combined = Convert.FromBase64String(parts(1))
            If iv.Length <> 12 OrElse combined.Length < 16 Then Return cipherText

            Dim cipherLength = combined.Length - 16
            Dim cipher(cipherLength - 1) As Byte
            Dim tag(15) As Byte
            Buffer.BlockCopy(combined, 0, cipher, 0, cipherLength)
            Buffer.BlockCopy(combined, cipherLength, tag, 0, 16)

            Dim plain(cipherLength - 1) As Byte
            Using aes As New AesGcm(key, 16)
                aes.Decrypt(iv, cipher, tag, plain)
            End Using

            Dim bytes As Byte() = If(isGzip, GzipUtil.Gunzip(plain), plain)
            Return Encoding.UTF8.GetString(bytes)
        Catch ex As Exception
            Return "[could not decrypt chat message]"
        End Try
    End Function

    Private Function DeriveKey(groupCode As String) As Byte()
        Using sha As SHA256 = SHA256.Create()
            Return sha.ComputeHash(Encoding.UTF8.GetBytes("khmsg::" & If(groupCode, "")))
        End Using
    End Function
End Module