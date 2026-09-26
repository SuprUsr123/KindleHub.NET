Imports System.Text.RegularExpressions

''' <summary>
''' Port of the official _khImgCode/_khImgParse/_khIsImgMsg (kh-app.js:3314-3345):
''' the chat sends an entire message encoded as "KHIMG1:" + a data: URI whose media
''' type is jpeg/png/gif/webp and whose payload is strict base64.
''' </summary>
Public Module ChatMedia
    Public Const ImagePrefix As String = "KHIMG1:"

    ''' <summary>Matches the official _KH_IMG_OK regex and _KH_IMG_SEND_BUDGET (110 KB data).</summary>
    Private ReadOnly ImgOk As New Regex("^data:image/(jpeg|jpg|png|gif|webp);base64,[A-Za-z0-9+/=]+$", RegexOptions.Compiled)
    Private Const SendBudget As Integer = 110000
    Public Const MaxLen As Integer = SendBudget * 2

    ''' <summary>Returns a plain data:image/... URI if this is a valid KHIMG1, otherwise Nothing.</summary>
    Public Function TryParseImage(text As String) As String
        Dim s = If(text, "")
        If Not s.StartsWith(ImagePrefix, StringComparison.Ordinal) Then Return Nothing
        Dim rest = s.Substring(ImagePrefix.Length)
        If rest.Length = 0 OrElse rest.Length > MaxLen Then Return Nothing
        Return If(ImgOk.IsMatch(rest), rest, Nothing)
    End Function

    Public Function IsImageMessage(text As String) As Boolean
        Return TryParseImage(text) IsNot Nothing
    End Function

    '''Report display label for a chat row (photos collapse to "[photo]").'''
    Public Function DescribeForReport(text As String) As String
        Dim t = If(text, "")
        If TryParseImage(t) IsNot Nothing Then Return "[photo]"
        Return t
    End Function

    ''' <summary>Encode an image for sending. Returns the full wire "KHIMG1:" blob or Nothing if invalid.</summary>
    Public Function EncodeImage(dataUri As String) As String
        Dim s = If(dataUri, "").Trim()
        If s.Length = 0 OrElse s.Length > SendBudget Then Return Nothing
        If Not ImgOk.IsMatch(s) Then Return Nothing
        Return ImagePrefix & s
    End Function
End Module
