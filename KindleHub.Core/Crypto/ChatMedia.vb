Imports System.Text.RegularExpressions
Imports System.Text.Json
Imports System.Text.Json.Nodes
Imports System.IO
Imports System.Text

''' <summary>
''' Wire-format helpers for the official KH_ chat markers.
''' Each marker is "PREFIX:" + a base64-encoded JSON payload (KHSTK1 is plain text).
''' Mirrors kh-app.js (_kh&lt;Name&gt;Code / _kh&lt;Name&gt;Parse / _khIs&lt;Name&gt;Msg).
''' </summary>
Public Module ChatMedia

    ' ── shared base64 wire codec ────────────────────────────────────────────────

    Private Function WireEncode(obj As Object) As String
        If obj Is Nothing Then Return ""
        Dim json = JsonSerializer.Serialize(obj)
        Return Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
    End Function

    Private Function UnpackJson(wire As String) As JsonObject
        Try
            If String.IsNullOrEmpty(wire) Then Return Nothing
            Dim node = JsonNode.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(wire)))
            If TypeOf node Is JsonObject Then Return DirectCast(node, JsonObject)
            Return Nothing
        Catch
            Return Nothing
        End Try
    End Function

    Private Function UnpackJsonText(wire As String) As String
        Try
            If String.IsNullOrEmpty(wire) Then Return ""
            Return Encoding.UTF8.GetString(Convert.FromBase64String(wire))
        Catch
            Return ""
        End Try
    End Function

    Private Function StrVal(node As JsonNode, fallback As String) As String
        If node Is Nothing Then Return fallback
        If TypeOf node IsNot JsonValue Then Return fallback
        If DirectCast(node, JsonValue).GetValueKind() <> JsonValueKind.String Then Return fallback
        Dim raw = DirectCast(node, JsonValue).GetValue(Of String)()
        If raw Is Nothing Then Return fallback
        Return raw
    End Function

    ' ── KHIMG1 ──────────────────────────────────────────────────────────────────

    Public Const ImagePrefix As String = "KHIMG1:"
    Private ReadOnly ImgOk As New Regex("^data:image/(jpeg|jpg|png|gif|webp);base64,[A-Za-z0-9+/=]+$", RegexOptions.Compiled)
    Private Const SendBudget As Integer = 110000
    Public Const MaxImageLen As Integer = SendBudget * 2

    Public Function TryParseImage(text As String) As String
        Dim s = If(text, "")
        If Not s.StartsWith(ImagePrefix, StringComparison.Ordinal) Then Return Nothing
        Dim rest = s.Substring(ImagePrefix.Length)
        If rest.Length = 0 OrElse rest.Length > MaxImageLen Then Return Nothing
        Return If(ImgOk.IsMatch(rest), rest, Nothing)
    End Function

    Public Function IsImageMessage(text As String) As Boolean
        Return TryParseImage(text) IsNot Nothing
    End Function

    Public Function DescribeForReport(text As String) As String
        Dim t = If(text, "")
        If TryParseImage(t) IsNot Nothing Then Return "[photo]"
        Return t
    End Function

    Public Function EncodeImage(dataUri As String) As String
        Dim s = If(dataUri, "").Trim()
        If s.Length = 0 OrElse s.Length > SendBudget Then Return Nothing
        If Not ImgOk.IsMatch(s) Then Return Nothing
        Return ImagePrefix & s
    End Function

    ' ── KHAPP1 (shared custom app) ──────────────────────────────────────────────
    ''' Wire: "KHAPP1:" + btoa(unescape(encodeURIComponent(JSON.stringify({
    '''   shareId, label, color, icon, html }))).
    ''' html max 524288. label max 40.
    Public ReadOnly MaxAppHtmlLen As Integer = 524288

    Public Class AppShare
        Public Property ShareId As String
        Public Property Label As String
        Public Property Color As String
        Public Property Icon As String
        Public Property Html As String
    End Class

    Public Function TryParseAppShare(text As String) As AppShare
        Dim s = If(text, "")
        If Not s.StartsWith("KHAPP1:", StringComparison.Ordinal) Then Return Nothing
        Dim obj = UnpackJson(s.Substring(7))
        If obj Is Nothing Then Return Nothing
        Dim htmlNode = obj("html")
        If htmlNode Is Nothing OrElse Not TypeOf htmlNode Is JsonValue Then Return Nothing
        Dim html = htmlNode.GetValue(Of String)()
        If html.Length > MaxAppHtmlLen Then Return Nothing
        Return New AppShare With {
            .ShareId = StrVal(obj("shareId"), ""),
            .Label = StrVal(obj("label"), "App"),
            .Color = StrVal(obj("color"), ""),
            .Icon = StrVal(obj("icon"), ""),
            .Html = html
        }
    End Function

    Public Function IsAppShareMessage(text As String) As Boolean
        Return TryParseAppShare(text) IsNot Nothing
    End Function

    Public Function EncodeAppShare(app As AppShare) As String
        If app Is Nothing OrElse String.IsNullOrEmpty(app.Html) Then Return ""
        If app.Html.Length > MaxAppHtmlLen Then Return ""
        Dim obj = New JsonObject From {
            {"shareId", If(String.IsNullOrEmpty(app.ShareId), "", app.ShareId)},
            {"label", If(String.IsNullOrEmpty(app.Label), "App", app.Label.Substring(0, Math.Min(app.Label.Length, 40)))},
            {"color", If(String.IsNullOrEmpty(app.Color), "#2563eb", app.Color)},
            {"icon", If(String.IsNullOrEmpty(app.Icon), "", app.Icon)},
            {"html", app.Html}
        }
        Return "KHAPP1:" & WireEncode(obj)
    End Function

    ' ── KHFLIP1 (flipbook) ──────────────────────────────────────────────────────
    ''' Wire: "KHFLIP1:" + btoa(unescape(encodeURIComponent(JSON.stringify({
    '''   n, w, h, fps, f }))).
    ''' w<=64, h<=64, f.length<=60, fps 1..15.
    Public Class Flipbook
        Public Property Name As String
        Public Property Width As Integer
        Public Property Height As Integer
        Public Property Fps As Integer
        Public Property Frames As String()
    End Class

    Public Function TryParseFlipbook(text As String) As Flipbook
        Dim s = If(text, "")
        If Not s.StartsWith("KHFLIP1:", StringComparison.Ordinal) Then Return Nothing
        Dim obj = UnpackJson(s.Substring(8))
        If obj Is Nothing Then Return Nothing
        Dim wNode = obj("w"), hNode = obj("h"), fNode = obj("f")
        If wNode Is Nothing OrElse hNode Is Nothing OrElse fNode Is Nothing Then Return Nothing
        If Not TypeOf wNode Is JsonValue OrElse Not TypeOf hNode Is JsonValue Then Return Nothing
        Dim w = wNode.GetValue(Of Integer)(), h = hNode.GetValue(Of Integer)()
        If w > 64 OrElse h > 64 Then Return Nothing
        If Not TypeOf fNode Is JsonArray Then Return Nothing
        Dim frames = New List(Of String)()
        For Each f In fNode.AsArray()
            frames.Add(StrVal(f, ""))
        Next
        If frames.Count = 0 OrElse frames.Count > 60 Then Return Nothing
        Dim fps As Integer = 6
        If obj("fps") IsNot Nothing AndAlso TypeOf obj("fps") Is JsonValue Then fps = obj("fps").GetValue(Of Integer)()
        fps = Math.Max(1, Math.Min(15, fps))
        Return New Flipbook With {
            .Name = StrVal(obj("n"), ""),
            .Width = w,
            .Height = h,
            .Fps = fps,
            .Frames = frames.ToArray()
        }
    End Function

    Public Function IsFlipbookMessage(text As String) As Boolean
        Return TryParseFlipbook(text) IsNot Nothing
    End Function

    Public Function EncodeFlipbook(fb As Flipbook) As String
        If fb Is Nothing OrElse fb.Frames Is Nothing OrElse fb.Frames.Length = 0 Then Return ""
        If fb.Width > 64 OrElse fb.Height > 64 OrElse fb.Frames.Length > 60 Then Return ""
        Dim framesNode = New JsonArray()
        For Each frame In fb.Frames.Take(60)
            framesNode.Add(frame)
        Next
        Dim obj = New JsonObject From {
            {"n", fb.Name},
            {"w", fb.Width},
            {"h", fb.Height},
            {"fps", fb.Fps},
            {"f", framesNode}
        }
        Return "KHFLIP1:" & WireEncode(obj)
    End Function

    ''' <summary>Hex-pack a binary frame (4 bits per pixel) into the official
    ''' compact string carried inside KHFLIP1. Each 4-pixel nibble becomes one
    ''' hex char; the array length is padded up to a multiple of 4.</summary>
    Public Function FlipPack(frame As Integer(), totalCells As Integer) As String
        If frame Is Nothing Then Return ""
        Dim sb = New System.Text.StringBuilder()
        Dim n = (totalCells + 3) \ 4
        For i = 0 To n - 1
            Dim nibble = 0
            For k = 0 To 3
                Dim idx = i * 4 + k
                If idx < frame.Length AndAlso frame(idx) <> 0 Then nibble = nibble Or (1 << k)
            Next
            sb.Append(nibble.ToString("X", System.Globalization.CultureInfo.InvariantCulture))
        Next
        Return sb.ToString()
    End Function

    ''' <summary>Reverse of <see cref="FlipPack(Integer[], Integer)"/>: turn the
    ''' compact string back into a 0/1 pixel array of <c>totalCells</c> cells.</summary>
    Public Function FlipUnpack(s As String, totalCells As Integer) As Integer()
        Dim arr = New Integer(Math.Max(0, totalCells) - 1) {}
        If String.IsNullOrEmpty(s) OrElse totalCells <= 0 Then Return arr
        For i = 0 To s.Length - 1
            Dim nibble As Integer
            Dim v = Asc(s(i))
            If v >= AscW("0") AndAlso v <= AscW("9") Then
                nibble = v - AscW("0")
            ElseIf v >= AscW("A") AndAlso v <= AscW("F") Then
                nibble = v - AscW("A") + 10
            ElseIf v >= AscW("a") AndAlso v <= AscW("f") Then
                nibble = v - AscW("a") + 10
            Else
                Continue For
            End If
            For k = 0 To 3
                Dim idx = i * 4 + k
                If idx >= totalCells Then Exit For
                arr(idx) = (nibble >> k) And 1
            Next
        Next
        Return arr
    End Function

    ' ── KHPOLL1 (poll) ──────────────────────────────────────────────────────────
    ''' Wire: "KHPOLL1:" + btoa(unescape(encodeURIComponent(JSON.stringify({
    '''   pid, q, opts, o }))).
    ''' opts len 2..6, each <=60 chars. q <=140. o is 0/1.
    Public Class Poll
        Public Property Id As String
        Public Property Question As String
        Public Property Options As String()
        Public Property [Open] As Boolean
    End Class

Public Function TryParsePoll(text As String) As Poll
        Dim s = If(text, "")
        If Not s.StartsWith("KHPOLL1:", StringComparison.Ordinal) Then Return Nothing
        Dim obj = UnpackJson(s.Substring(8))
        If obj Is Nothing Then Return Nothing
        Dim pidNode = obj("pid"), optsNode = obj("opts")
        If pidNode Is Nothing OrElse optsNode Is Nothing Then Return Nothing
        If Not TypeOf optsNode Is JsonArray Then Return Nothing
        Dim opts = New List(Of String)()
        For Each o In optsNode.AsArray()
            Dim t = StrVal(o, "")
            If Not String.IsNullOrEmpty(t) Then opts.Add(t.Substring(0, Math.Min(t.Length, 60)))
        Next
        If opts.Count < 2 OrElse opts.Count > 6 Then Return Nothing
        Dim pid As String = Nothing
        If TypeOf pidNode Is JsonValue Then pid = pidNode.GetValue(Of String)()
        If pid Is Nothing Then Return Nothing
        Return New Poll With {
            .Id = pid,
            .Question = StrVal(obj("q"), "(no question)").Substring(0, Math.Min(StrVal(obj("q"), "").Length, 140)),
            .Options = opts.ToArray(),
            .[Open] = Not (obj("o") IsNot Nothing AndAlso TypeOf obj("o") Is JsonValue AndAlso obj("o").GetValue(Of Integer)() = 0)
        }
    End Function

    Public Function IsPollMessage(text As String) As Boolean
        Return TryParsePoll(text) IsNot Nothing
    End Function

    Public Function EncodePoll(poll As Poll) As String
        If poll Is Nothing OrElse poll.Options Is Nothing OrElse poll.Options.Length < 2 Then Return ""
        Dim opts = New JsonArray()
        For Each o In poll.Options.Take(6)
            Dim t = If(o, "").Substring(0, Math.Min(If(o, "").Length, 60))
            opts.Add(t)
        Next
        Dim obj = New JsonObject From {
            {"pid", poll.Id & "_" & DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString("x") & "_" & Random.Shared.Next(1000000).ToString("x")},
            {"q", If(String.IsNullOrEmpty(poll.Question), "(no question)", poll.Question.Substring(0, Math.Min(poll.Question.Length, 140)))},
            {"opts", opts},
            {"o", If(poll.Open, 1, 0)}
        }
        Return "KHPOLL1:" & WireEncode(obj)
    End Function

    ' ── KHVOTE1 (poll vote / other-vote tally) ──────────────────────────────────
    ''' Wire: "KHVOTE1:<pid>:<idx>" or "KHVOTE1:<pid>:t:<base64 text>".
    Public Class PollTally
        Public Property TotalVotes As Integer
        Public Property OptionCounts As Integer()
    End Class

    Public Const PollOtherMax As Integer = 40

    Public Function TallyVotes(messages As IEnumerable(Of Message), poll As Poll) As PollTally
        If poll Is Nothing OrElse poll.Options Is Nothing Then Return Nothing
        Dim counts = New Integer(poll.Options.Length - 1) {}
        Dim total = 0
        For Each m In messages
            If m Is Nothing OrElse m.Text Is Nothing Then Continue For
            If Not m.Text.StartsWith("KHVOTE1:", StringComparison.Ordinal) Then Continue For
            Dim vote = ParseVote(m.Text)
            If vote.PollId Is Nothing OrElse vote.PollId <> poll.Id Then Continue For
            If vote.OptionIndex.HasValue AndAlso vote.OptionIndex.Value >= 0 AndAlso vote.OptionIndex.Value < counts.Length Then
                counts(vote.OptionIndex.Value) += 1
                total += 1
            End If
        Next
        Return New PollTally With {.TotalVotes = total, .OptionCounts = counts}
    End Function

    Public Function IsVoteMessage(text As String) As Boolean
        Return If(String.IsNullOrEmpty(text), False, text.StartsWith("KHVOTE1:"))
    End Function

    Public Function ParseVote(text As String) As (PollId As String, OptionIndex As Integer?, Note As String)
        Dim s = If(text, "")
        If Not s.StartsWith("KHVOTE1:", StringComparison.Ordinal) Then Return (Nothing, Nothing, Nothing)
        Dim body = s.Substring(8)
        Dim idx As Integer? = Nothing
        Dim note = ""
        Dim pid = body
        If body.IndexOf(":t:") >= 0 Then
            Dim parts = body.Split(":t:", 2)
            pid = parts(0)
            Dim raw = UnpackJsonText(parts(1))
            note = raw.Trim().Substring(0, Math.Min(raw.Trim().Length, PollOtherMax))
        ElseIf body.IndexOf(":") >= 0 Then
            Dim parts = body.Split(":", 2)
            pid = parts(0)
            Dim n As Integer
            If Integer.TryParse(parts(1), n) AndAlso n >= 0 Then idx = n
        End If
        Return (pid, idx, note)
    End Function

    Public Function EncodeVote(pollId As String, optionIndex As Integer) As String
        Return "KHVOTE1:" & pollId & ":" & optionIndex.ToString()
    End Function

    Public Function EncodeVoteNote(pollId As String, note As String) As String
        Return "KHVOTE1:" & pollId & ":t:" & WireEncode(note)
    End Function

    Public Function TryParsePollOptions(text As String) As String()
        Dim poll = TryParsePoll(text)
        If poll Is Nothing Then Return Nothing
        Return poll.Options
    End Function

    Public Function TryParseAppId(text As String) As String
        Dim app = TryParseAppShare(text)
        If app Is Nothing Then Return Nothing
        Return app.ShareId
    End Function

    Public Function TryParseStoryLabel(text As String) As String
        Dim story = TryParseStory(text)
        If story Is Nothing Then Return Nothing
        Return story.Setting
    End Function

    Public Function TryParseVoteSummary(text As String) As String
        Dim v = ParseVote(text)
        If v.PollId Is Nothing Then Return Nothing
        Dim note = If(String.IsNullOrEmpty(v.Note), "", " (" & v.Note & ")")
        Return "Poll " & v.PollId.Substring(0, Math.Min(8, v.PollId.Length)) & If(v.OptionIndex.HasValue, " opt " & v.OptionIndex.Value.ToString() & note, "")
    End Function

    ' ── KHSTK1 (sticker) ───────────────────────────────────────────────────────
    ''' Wire: "KHSTK1:" + stickerId (plain, max 24 chars).
    Public Class StickerEntry
        Public Property Label As String
        Public Property Svg As String
    End Class

    Public ReadOnly Stickers As New Dictionary(Of String, StickerEntry)(StringComparer.Ordinal)

    Public Sub EnsureStickersLoaded()
        If Stickers.Count > 0 Then Return
        Stickers("like") = New StickerEntry With {.Label = "Thumbs up", .Svg = "<svg viewBox=""0 0 100 100"" fill=""none"" stroke=""currentColor"" stroke-width=""6"" stroke-linejoin=""round"" stroke-linecap=""round""><rect x=""14"" y=""44"" width=""16"" height=""38"" rx=""3""/><path d=""M30 44 L44 14 C51 14 55 20 53 27 L49 42 H78 C84 42 88 47 86 53 L80 78 C79 82 75 84 71 84 H30 Z""/></svg>"}
        Stickers("love") = New StickerEntry With {.Label = "Heart", .Svg = "<svg viewBox=""0 0 100 100"" fill=""currentColor""><path d=""M50 84 C20 62 10 44 10 30 C10 18 20 10 31 10 C39 10 46 15 50 22 C54 15 61 10 69 10 C80 10 90 18 90 30 C90 44 80 62 50 84 Z""/></svg>"}
        Stickers("smile") = New StickerEntry With {.Label = "Smile", .Svg = "<svg viewBox=""0 0 100 100"" fill=""none"" stroke=""currentColor"" stroke-width=""5""><circle cx=""50"" cy=""50"" r=""40""/><circle cx=""37"" cy=""42"" r=""4"" fill=""currentColor""/><circle cx=""63"" cy=""42"" r=""4"" fill=""currentColor""/><path d=""M33 60 Q50 76 67 60"" stroke-linecap=""round""/></svg>"}
        Stickers("haha") = New StickerEntry With {.Label = "Laughing", .Svg = "<svg viewBox=""0 0 100 100"" fill=""none"" stroke=""currentColor"" stroke-width=""5""><circle cx=""50"" cy=""50"" r=""40""/><path d=""M33 40 L44 46 L33 52"" stroke-linecap=""round"" stroke-linejoin=""round""/><path d=""M67 40 L56 46 L67 52"" stroke-linecap=""round"" stroke-linejoin=""round""/><path d=""M30 60 Q50 82 70 60 Z"" fill=""currentColor"" stroke=""none""/></svg>"}
        Stickers("wow") = New StickerEntry With {.Label = "Wow", .Svg = "<svg viewBox=""0 0 100 100"" fill=""none"" stroke=""currentColor"" stroke-width=""5""><circle cx=""50"" cy=""50"" r=""40""/><circle cx=""37"" cy=""40"" r=""4"" fill=""currentColor""/><circle cx=""63"" cy=""40"" r=""4"" fill=""currentColor""/><circle cx=""50"" cy=""66"" r=""10""/></svg>"}
        Stickers("sad") = New StickerEntry With {.Label = "Sad", .Svg = "<svg viewBox=""0 0 100 100"" fill=""none"" stroke=""currentColor"" stroke-width=""5""><circle cx=""50"" cy=""50"" r=""40""/><circle cx=""37"" cy=""42"" r=""4"" fill=""currentColor""/><circle cx=""63"" cy=""42"" r=""4"" fill=""currentColor""/><path d=""M33 68 Q50 54 67 68"" stroke-linecap=""round""/></svg>"}
        Stickers("fire") = New StickerEntry With {.Label = "Fire", .Svg = "<svg viewBox=""0 0 100 100"" fill=""currentColor""><path d=""M52 8 C54 26 68 30 68 46 C74 42 76 34 75 28 C84 38 88 52 88 62 C88 80 72 92 50 92 C30 92 14 79 14 61 C14 46 24 36 32 30 C33 40 38 44 44 44 C42 30 46 16 52 8 Z""/></svg>"}
        Stickers("yes") = New StickerEntry With {.Label = "Yes", .Svg = "<svg viewBox=""0 0 100 100"" fill=""none"" stroke=""currentColor"" stroke-width=""7"" stroke-linecap=""round"" stroke-linejoin=""round""><circle cx=""50"" cy=""50"" r=""40""/><path d=""M32 52 L45 65 L70 36""/></svg>"}
        Stickers("no") = New StickerEntry With {.Label = "No", .Svg = "<svg viewBox=""0 0 100 100"" fill=""none"" stroke=""currentColor"" stroke-width=""7"" stroke-linecap=""round""><circle cx=""50"" cy=""50"" r=""40""/><path d=""M36 36 L64 64 M64 36 L36 64""/></svg>"}
        Stickers("star") = New StickerEntry With {.Label = "Star", .Svg = "<svg viewBox=""0 0 100 100"" fill=""currentColor""><path d=""M50 8 L61 38 L93 38 L67 58 L77 90 L50 70 L23 90 L33 58 L7 38 L39 38 Z""/></svg>"}
        Stickers("party") = New StickerEntry With {.Label = "Party", .Svg = "<svg viewBox=""0 0 100 100"" fill=""none"" stroke=""currentColor"" stroke-width=""5"" stroke-linejoin=""round""><path d=""M18 84 L40 34 L72 66 Z"" fill=""currentColor"" stroke=""none""/><path d=""M62 20 l6 -6 M80 30 l7 -2 M70 48 l8 3 M55 12 l2 -7"" stroke-linecap=""round""/></svg>"}
        Stickers("hundred") = New StickerEntry With {.Label = "100", .Svg = "<svg viewBox=""0 0 100 100"" fill=""none"" stroke=""currentColor"" stroke-width=""8"" stroke-linecap=""round"" stroke-linejoin=""round""><path d=""M18 32 L28 26 V74""/><rect x=""42"" y=""28"" width=""20"" height=""44"" rx=""10""/><rect x=""70"" y=""28"" width=""20"" height=""44"" rx=""10""/></svg>"}
        Stickers("think") = New StickerEntry With {.Label = "Thinking", .Svg = "<svg viewBox=""0 0 100 100"" fill=""none"" stroke=""currentColor"" stroke-width=""5""><circle cx=""50"" cy=""46"" r=""36""/><circle cx=""38"" cy=""38"" r=""4"" fill=""currentColor""/><circle cx=""62"" cy=""38"" r=""4"" fill=""currentColor""/><path d=""M38 62 H62"" stroke-linecap=""round""/><circle cx=""22"" cy=""86"" r=""7"" fill=""currentColor"" stroke=""none""/><circle cx=""36"" cy=""94"" r=""4"" fill=""currentColor"" stroke=""none""/></svg>"}
        Stickers("wink") = New StickerEntry With {.Label = "Wink", .Svg = "<svg viewBox=""0 0 100 100"" fill=""none"" stroke=""currentColor"" stroke-width=""5""><circle cx=""50"" cy=""50"" r=""40""/><path d=""M29 42 Q37 34 45 42"" stroke-linecap=""round""/><circle cx=""63"" cy=""42"" r=""4"" fill=""currentColor""/><path d=""M33 60 Q50 76 67 60"" stroke-linecap=""round""/></svg>"}
        Stickers("cool") = New StickerEntry With {.Label = "Sunglasses", .Svg = "<svg viewBox=""0 0 100 100"" fill=""none"" stroke=""currentColor"" stroke-width=""5""><circle cx=""50"" cy=""50"" r=""40""/><path d=""M18 38 H82"" stroke-linecap=""round""/><rect x=""20"" y=""38"" width=""24"" height=""16"" rx=""5"" fill=""currentColor"" stroke=""none""/><rect x=""56"" y=""38"" width=""24"" height=""16"" rx=""5"" fill=""currentColor"" stroke=""none""/><path d=""M36 68 Q50 78 64 68"" stroke-linecap=""round""/></svg>"}
        Stickers("sleep") = New StickerEntry With {.Label = "Sleeping", .Svg = "<svg viewBox=""0 0 100 100"" fill=""none"" stroke=""currentColor"" stroke-width=""5""><circle cx=""50"" cy=""54"" r=""36""/><path d=""M30 48 H42 M58 48 H70"" stroke-linecap=""round""/><path d=""M38 70 Q50 62 62 70"" stroke-linecap=""round""/><path d=""M74 10 H90 L74 26 H90"" stroke-linecap=""round"" stroke-linejoin=""round"" stroke-width=""6""/></svg>"}
        Stickers("angry") = New StickerEntry With {.Label = "Angry", .Svg = "<svg viewBox=""0 0 100 100"" fill=""none"" stroke=""currentColor"" stroke-width=""5""><circle cx=""50"" cy=""50"" r=""40""/><path d=""M28 32 L44 40 M72 32 L56 40"" stroke-linecap=""round"" stroke-width=""6""/><circle cx=""38"" cy=""50"" r=""4"" fill=""currentColor""/><circle cx=""62"" cy=""50"" r=""4"" fill=""currentColor""/><path d=""M34 72 Q50 60 66 72"" stroke-linecap=""round""/></svg>"}
        Stickers("cry") = New StickerEntry With {.Label = "Cry", .Svg = "<svg viewBox=""0 0 100 100"" fill=""none"" stroke=""currentColor"" stroke-width=""5""><circle cx=""50"" cy=""50"" r=""40""/><circle cx=""37"" cy=""42"" r=""4"" fill=""currentColor""/><circle cx=""63"" cy=""42"" r=""4"" fill=""currentColor""/><path d=""M33 68 Q50 54 67 68"" stroke-linecap=""round""/></svg>"}
    End Sub

    Public Function TryParseSticker(text As String) As String
        Dim s = If(text, "")
        If Not s.StartsWith("KHSTK1:", StringComparison.Ordinal) Then Return Nothing
        Dim id = s.Substring(7).Trim()
        If String.IsNullOrEmpty(id) OrElse id.Length > 24 Then Return Nothing
        EnsureStickersLoaded()
        If Stickers.ContainsKey(id) Then Return id
        Return Nothing
    End Function

    Public Function IsStickerMessage(text As String) As Boolean
        Return TryParseSticker(text) IsNot Nothing
    End Function

    Public Function EncodeSticker(id As String) As String
        If String.IsNullOrEmpty(id) OrElse id.Length > 24 Then Return ""
        Return "KHSTK1:" & id
    End Function

    Public Function StickerLabel(id As String) As String
        EnsureStickersLoaded()
        Dim entry As StickerEntry = Nothing
        If Stickers.TryGetValue(id, entry) Then Return entry.Label
        Return id
    End Function

    Public Function TryParseStickerPrompt(text As String) As String
        Dim s = If(text, "")
        If Not s.StartsWith("KHSTK1:", StringComparison.Ordinal) Then Return Nothing
        Dim id = s.Substring(7).Trim()
        EnsureStickersLoaded()
        Dim entry As StickerEntry = Nothing
        If Stickers.TryGetValue(id, entry) Then Return entry.Label
        Return Nothing
    End Function

    ' ── KHSTORY1 (AI story) ────────────────────────────────────────────────────
    ''' Wire: "KHSTORY1:" + btoa(unescape(encodeURIComponent(JSON.stringify({
    '''   s, h, l:[{r,t}] }))).
    ''' l max 120 entries; each r<=4, t<=1800; s<=60; h<=40; total<=11000*2.
    Public Const StoryMaxLen As Integer = 11000

    Public Class StoryLogEntry
        Public Property Role As String
        Public Property Text As String
    End Class

    Public Class Story
        Public Property Setting As String
        Public Property Theme As String
        Public Property Log As StoryLogEntry()
    End Class

    Public Function TryParseStory(text As String) As Story
        Dim s = If(text, "")
        If Not s.StartsWith("KHSTORY1:", StringComparison.Ordinal) Then Return Nothing
        If s.Length > StoryMaxLen * 2 Then Return Nothing
        Dim obj = UnpackJson(s.Substring(9))
        If obj Is Nothing Then Return Nothing
        Dim logArr = obj("l")
        If logArr Is Nothing OrElse Not TypeOf logArr Is JsonArray Then Return Nothing
        Dim arr = logArr.AsArray()
        If arr.Count = 0 Then Return Nothing
        Dim log = New List(Of StoryLogEntry)()
        For i = 0 To Math.Min(arr.Count - 1, 119)
            Dim entry = arr(i)
            Dim r = StrVal(entry("r"), "ai").Substring(0, Math.Min(4, StrVal(entry("r"), "ai").Length))
            Dim t = StrVal(entry("t"), "").Substring(0, Math.Min(1800, StrVal(entry("t"), "").Length))
            log.Add(New StoryLogEntry With {.Role = r, .Text = t})
        Next
        If log.Count = 0 Then Return Nothing
        Return New Story With {
            .Setting = StrVal(obj("s"), "A story").Substring(0, Math.Min(60, StrVal(obj("s"), "A story").Length)),
            .Theme = StrVal(obj("h"), "").Substring(0, Math.Min(40, StrVal(obj("h"), "").Length)),
            .Log = log.ToArray()
        }
    End Function

    Public Function IsStoryMessage(text As String) As Boolean
        Return TryParseStory(text) IsNot Nothing
    End Function

    Public Function EncodeStory(story As Story) As String
        If story Is Nothing OrElse story.Log Is Nothing OrElse story.Log.Length = 0 Then Return ""
        Dim logArr = New JsonArray()
        For Each e In story.Log.Take(120)
            logArr.Add(New JsonObject From {
                {"r", e.Role.Substring(0, Math.Min(4, e.Role.Length))},
                {"t", e.Text.Substring(0, Math.Min(1800, e.Text.Length))}
            })
        Next
        Dim obj = New JsonObject From {
            {"s", If(String.IsNullOrEmpty(story.Setting), "A story", story.Setting.Substring(0, Math.Min(60, story.Setting.Length)))},
            {"h", If(String.IsNullOrEmpty(story.Theme), "", story.Theme.Substring(0, Math.Min(40, story.Theme.Length)))},
            {"l", logArr}
        }
        Dim coded = "KHSTORY1:" & WireEncode(obj)
        If coded.Length > StoryMaxLen * 2 Then Return ""
        Return coded
    End Function

    ' ── KHDRAW1 (shared drawing) ───────────────────────────────────────────────
    ''' Wire: "KHDRAW1:" + btoa(unescape(encodeURIComponent(JSON.stringify({
    '''   n, w, h, layers, author }))).
    ''' layers must be a non-empty array. w/h default 680/420. name max 40.
    Public Class DrawingLayer
        Public Property Type As String
        Public Property Data As String
    End Class

    Public Class Drawing
        Public Property Name As String
        Public Property Width As Integer
        Public Property Height As Integer
        Public Property Layers As DrawingLayer()
        Public Property Author As String
    End Class

    Public Function TryParseDrawing(text As String) As Drawing
        Dim s = If(text, "")
        If Not s.StartsWith("KHDRAW1:", StringComparison.Ordinal) Then Return Nothing
        Dim obj = UnpackJson(s.Substring(8))
        If obj Is Nothing Then Return Nothing
        Dim layersNode = obj("layers")
        If layersNode Is Nothing OrElse Not TypeOf layersNode Is JsonArray Then Return Nothing
        Dim layers = New List(Of DrawingLayer)()
        For Each l In layersNode.AsArray()
            layers.Add(New DrawingLayer With {
                .Type = StrVal(l("type"), ""),
                .Data = StrVal(l("data"), "")
            })
        Next
        If layers.Count = 0 Then Return Nothing
        Return New Drawing With {
            .Name = StrVal(obj("n"), "Shared drawing").Substring(0, Math.Min(40, StrVal(obj("n"), "Shared drawing").Length)),
            .Width = If(obj("w") IsNot Nothing, obj("w").GetValue(Of Integer)(), 680),
            .Height = If(obj("h") IsNot Nothing, obj("h").GetValue(Of Integer)(), 420),
            .Layers = layers.ToArray(),
            .Author = StrVal(obj("author"), "")
        }
    End Function

    Public Function IsDrawingMessage(text As String) As Boolean
        Return TryParseDrawing(text) IsNot Nothing
    End Function

    Public Function EncodeDrawing(draw As Drawing) As String
        If draw Is Nothing OrElse draw.Layers Is Nothing OrElse draw.Layers.Length = 0 Then Return ""
        Dim layersArr = New JsonArray()
        For Each l In draw.Layers
            layersArr.Add(New JsonObject From {
                {"type", l.Type},
                {"data", l.Data}
            })
        Next
        Dim obj = New JsonObject From {
            {"n", If(String.IsNullOrEmpty(draw.Name), "Shared drawing", draw.Name.Substring(0, Math.Min(40, draw.Name.Length)))},
            {"w", draw.Width},
            {"h", draw.Height},
            {"layers", layersArr},
            {"author", draw.Author}
        }
        Return "KHDRAW1:" & WireEncode(obj)
    End Function

    ' ── unified dispatcher ─────────────────────────────────────────────────────
    Public Function IsEncodedMsg(text As String) As Boolean
        Dim s = If(text, "")
        Return s.StartsWith(ImagePrefix, StringComparison.Ordinal) _
            OrElse IsAppShareMessage(s) _
            OrElse IsFlipbookMessage(s) _
            OrElse IsPollMessage(s) _
            OrElse IsVoteMessage(s) _
            OrElse IsStickerMessage(s) _
            OrElse IsStoryMessage(s) _
            OrElse IsDrawingMessage(s)
    End Function

    ''' <summary>
    ''' A human label for a known encoded message.
    ''' Mirrors _khMediaLabel in kh-app.js.
    ''' </summary>
    Public Function DescribeForChat(text As String) As String
        Dim s = If(text, "")
        If TryParseImage(s) IsNot Nothing Then Return "[photo]"
        If TryParseFlipbook(s) IsNot Nothing Then Return "[Flipbook]"
        If TryParseDrawing(s) IsNot Nothing Then Return "[Drawing]"
        If IsStickerMessage(s) Then
            Dim id = TryParseSticker(s)
            Dim lbl = StickerLabel(id)
            Return "[Sticker" & If(lbl = id, "", " · " & lbl) & "]"
        End If
        If TryParsePoll(s) IsNot Nothing Then
            Dim poll = TryParsePoll(s)
            Return "[Poll] " & poll.Question
        End If
        If TryParseStory(s) IsNot Nothing Then
            Return "[Story] " & TryParseStory(s).Setting
        End If
        If TryParseAppShare(s) IsNot Nothing Then Return "[App]"
        Return s
    End Function


End Module
