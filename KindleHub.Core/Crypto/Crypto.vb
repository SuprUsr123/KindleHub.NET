Imports System.Security.Cryptography
Imports System.Text
Imports System.IO
Imports System.IO.Compression
Imports System.Linq

''' <summary>
''' Faithful port of the official KindleHub key derivation (kh-app.js):
'''   _userKey(username, password) = SHA256("kh::" + canonical(username) + "::" + password)
''' The resulting 64-hex string is BOTH the account lookup key (kh_users.hash)
''' and the AES-256 key material for the encrypted account state.
''' </summary>
Public Module KeyDerivation
    Friend Const KeyPrefix As String = "kh::"

    ''' <summary>
    ''' Canonicalises a username exactly like the official _khCanonicalUsername.
    ''' </summary>
    Public Function CanonicalizeUsername(username As String) As String
        Dim v = If(username Is Nothing, "", username).Trim()
        If v.ToLowerInvariant() = "aran" Then Return "arancool3000"
        Return v
    End Function

    ''' <summary>
    ''' Derives the auth token (kh_users.hash / AES key) from username + password.
    ''' Matches JS _userKey: SHA256("kh::" + lowercase(trim(username)) + "::" + password).
    ''' </summary>
    Public Function DeriveAuthToken(username As String, password As String) As String
        If String.IsNullOrWhiteSpace(username) Then
            Throw New ArgumentException("Username cannot be empty", NameOf(username))
        End If

        Dim canonical = CanonicalizeUsername(username).ToLowerInvariant()
        Dim input = KeyPrefix & canonical & "::" & password
        Dim bytes = Encoding.UTF8.GetBytes(input)

        Using sha256 As SHA256 = SHA256.Create()
            Return Convert.ToHexString(sha256.ComputeHash(bytes)).ToLowerInvariant()
        End Using
    End Function

    ''' <summary>
    ''' User id is the first 16 characters of the auth token (matches JS).
    ''' </summary>
    Public Function DeriveUserId(authToken As String) As String
        If String.IsNullOrWhiteSpace(authToken) OrElse authToken.Length < 16 Then
            Throw New ArgumentException("Invalid auth token", NameOf(authToken))
        End If
        Return authToken.Substring(0, 16)
    End Function

    ''' <summary>
    ''' Decodes a 64-hex auth token into the raw 32-byte AES key.
    ''' Matches JS _deriveAesKey which hex-decodes the key string.
    ''' </summary>
    Friend Function DeriveAesKeyBytes(authToken As String) As Byte()
        If String.IsNullOrWhiteSpace(authToken) OrElse authToken.Length <> 64 Then
            Throw New ArgumentException("Auth token must be 64 hex characters", NameOf(authToken))
        End If
        Return Convert.FromHexString(authToken)
    End Function
End Module

''' <summary>
''' The official _compStr/_decompStr dictionary compression. Dictionary entries
''' are replaced by Chr(1) + Chr(index); decompression is a single left-to-right
''' pass, exactly mirroring the JS implementation.
''' </summary>
Public Module KindleHubCompression
    Private ReadOnly Dictionary As String() = KindleHubCompressionDictionary.Entries

    Public Function Compress(json As String) As String
        If json Is Nothing Then json = ""
        Dim sb As New StringBuilder(json.Length)
        Dim i = 0
        While i < json.Length
            Dim matchIndex = -1
            For entryIndex = 0 To Dictionary.Length - 1
                Dim entry = Dictionary(entryIndex)
                If entry.Length > 0 AndAlso i + entry.Length <= json.Length AndAlso
                   String.CompareOrdinal(json, i, entry, 0, entry.Length) = 0 Then
                    matchIndex = entryIndex
                    Exit For
                End If
            Next

            If matchIndex >= 0 Then
                sb.Append(ChrW(1))
                sb.Append(ChrW(matchIndex))
                i += Dictionary(matchIndex).Length
            Else
                sb.Append(json(i))
                i += 1
            End If
        End While
        Return sb.ToString()
    End Function

    Public Function Decompress(compressed As String) As String
        If String.IsNullOrEmpty(compressed) Then Return compressed

        Dim sb As New StringBuilder(compressed.Length)
        Dim i = 0
        While i < compressed.Length
            If compressed(i) = ChrW(1) AndAlso i + 1 < compressed.Length Then
                Dim code = AscW(compressed(i + 1))
                If code >= 0 AndAlso code < Dictionary.Length Then
                    sb.Append(Dictionary(code))
                    i += 2
                    Continue While
                End If
            End If
            sb.Append(compressed(i))
            i += 1
        End While
        Return sb.ToString()
    End Function
End Module

''' <summary>
''' Gzip helpers backing the compression flag byte used in the account blob.
''' Mirrors _gzipBytes/_gunzipBytes (CompressionStream("gzip")).
''' </summary>
Friend Module GzipUtil
    Friend Function Gzip(bytes As Byte()) As Byte()
        Using ms As New MemoryStream()
            Using gz As New GZipStream(ms, CompressionLevel.Optimal, leaveOpen:=True)
                gz.Write(bytes, 0, bytes.Length)
            End Using
            Return ms.ToArray()
        End Using
    End Function

    Friend Function Gunzip(bytes As Byte()) As Byte()
        Using input As New MemoryStream(bytes)
            Using gz As New GZipStream(input, CompressionMode.Decompress)
                Using output As New MemoryStream()
                    gz.CopyTo(output)
                    Return output.ToArray()
                End Using
            End Using
        End Using
    End Function
End Module

''' <summary>
''' Account state packing, matching the official _encryptState/_decryptState:
'''   key   = hex-decoded 64-hex auth token (32 bytes)
'''   iv    = 12 random bytes
'''   plain = flag byte (71 = gzip, 99 = plain) + UTF-8(dictionary-compressed JSON)
'''   out   = base64(iv) + "." + base64(ciphertext + GCM tag)
''' </summary>
Public Module AccountEncryption
    Private Const FlagGzip As Byte = 71       ' 'G'
    Private Const FlagGzipRaw As Byte = 103   ' 'g' (gzip, no dictionary compression)
    Private Const FlagPlain As Byte = 99      ' 'c'

    ''' <summary>
    ''' Packs account state for storage (matches JS _khPackAccount -> _encryptState).
    ''' </summary>
    Public Function PackAccount(stateJson As String, authToken As String) As String
        Dim key = KeyDerivation.DeriveAesKeyBytes(authToken)
        Dim compressed = KindleHubCompression.Compress(stateJson)
        Dim compressedBytes = Encoding.UTF8.GetBytes(compressed)

        Dim payload As Byte()
        Dim gzipped = GzipUtil.Gzip(compressedBytes)
        If gzipped IsNot Nothing AndAlso gzipped.Length > 0 Then
            payload = New Byte(gzipped.Length) {}
            payload(0) = FlagGzip
            Buffer.BlockCopy(gzipped, 0, payload, 1, gzipped.Length)
        Else
            payload = New Byte(compressedBytes.Length) {}
            payload(0) = FlagPlain
            Buffer.BlockCopy(compressedBytes, 0, payload, 1, compressedBytes.Length)
        End If

        Dim iv(11) As Byte
        RandomNumberGenerator.Fill(iv)

        Dim ciphertext(payload.Length - 1) As Byte
        Dim tag(15) As Byte
        Using aes As New AesGcm(key, 16)
            aes.Encrypt(iv, payload, ciphertext, tag)
        End Using

        Dim combined(ciphertext.Length + tag.Length - 1) As Byte
        Buffer.BlockCopy(ciphertext, 0, combined, 0, ciphertext.Length)
        Buffer.BlockCopy(tag, 0, combined, ciphertext.Length, tag.Length)

        Return Convert.ToBase64String(iv) & "." & Convert.ToBase64String(combined)
    End Function

''' <summary>
''' Unpacks account state from storage (matches JS _decryptState).
''' Returns the JSON string, or Nothing when it cannot be decrypted.
''' </summary>
Public Function UnpackAccount(packed As String, authToken As String) As String
    If String.IsNullOrEmpty(packed) Then Return Nothing

    Dim parts = packed.Split("."c)
    If parts.Length <> 2 Then Return Nothing

    Try
        Dim key = KeyDerivation.DeriveAesKeyBytes(authToken)
        Dim iv = Convert.FromBase64String(parts(0))
        Dim combined = Convert.FromBase64String(parts(1))
        If iv.Length <> 12 OrElse combined.Length < 16 Then Return Nothing

        Dim ciphertextLength = combined.Length - 16
        Dim ciphertext(ciphertextLength - 1) As Byte
        Dim tag(15) As Byte
        Buffer.BlockCopy(combined, 0, ciphertext, 0, ciphertextLength)
        Buffer.BlockCopy(combined, ciphertextLength, tag, 0, 16)

        Dim plaintext(ciphertextLength - 1) As Byte
        Using aes As New AesGcm(key, 16)
            aes.Decrypt(iv, ciphertext, tag, plaintext)
        End Using

        If plaintext.Length = 0 Then Return Nothing
        Dim flag = plaintext(0)

        Dim body As Byte()
        Dim skipPrefix As Boolean = flag = FlagGzip OrElse flag = FlagGzipRaw OrElse flag = FlagPlain

        If flag = FlagGzip OrElse flag = FlagGzipRaw Then
            Dim offset = If(skipPrefix, 1, 0)
            Dim raw(plaintext.Length - offset - 1) As Byte
            Buffer.BlockCopy(plaintext, offset, raw, 0, raw.Length)
            body = GzipUtil.Gunzip(raw)
        Else
            Dim offset = If(skipPrefix, 1, 0)
            body = New Byte(plaintext.Length - offset - 1) {}
            Buffer.BlockCopy(plaintext, offset, body, 0, body.Length)
        End If

        Dim text = Encoding.UTF8.GetString(body)
        ' The legacy raw-gzip bucket (flag 103) is gunzipped without the dictionary
        ' pass; every other bucket gets dictionary decompression.
        Dim json = If(flag = FlagGzipRaw, text, KindleHubCompression.Decompress(text))
        Return json
    Catch
        Return Nothing
    End Try
End Function
End Module
