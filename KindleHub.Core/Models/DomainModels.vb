Imports System.ComponentModel
Imports System.Runtime.CompilerServices
Imports System.Linq
Imports System.Collections.Generic
Imports System.Text.RegularExpressions

Public Enum UserTier
    Free
    Plus
    Pro
End Enum

Public Class UserProfile
    Public Property UserId As String
    Public Property Email As String
    Public Property DisplayName As String
    Public Property AuthToken As String
    Public Property EncKey As String
    Public Property SyncEnabled As Boolean
    Public Property ProfileJoined As Long
    Public Property Tier As UserTier
    Public Property UpdatedAt As DateTimeOffset?

    Public ReadOnly Property TierFormatted As String
        Get
            Return "Tier: " & Tier.ToString()
        End Get
    End Property
End Class

Public Class AuthResult
    Public Property Success As Boolean
    Public Property Username As String
    Public Property Adjusted As Boolean
    Public Property [Error] As String
    Public Property Profile As UserProfile
    ''' <summary>SHA-256 auth token (kh_users.hash == the AES-256 key). Only set on success.</summary>
    Public Property AuthToken As String
    ''' <summary>Decrypted account-state JSON pulled on this login (Nothing for a fresh account).</summary>
    Public Property StateJson As String
    ''' <summary>Server-side last-sync timestamp (kh_users.updated_at) when present.</summary>
    Public Property ServerUpdatedAt As DateTimeOffset?
End Class

Public Class Group
    Public Property Code As String
    Public Property Name As String
    Public Property Creator As String
    Public Property CreatedAt As DateTimeOffset
    Public Property MemberCount As Integer

    ''' <summary>True when this row is a public Topic (name carries the "TOPIC: " directory prefix).</summary>
    Public ReadOnly Property IsTopic As Boolean
        Get
            Return If(Name, "").StartsWith("TOPIC: ", StringComparison.Ordinal)
        End Get
    End Property

    Public Property Blurb As String
    Public Property TopicCategory As String

    Public ReadOnly Property MemberCountFormatted As String
        Get
            Return MemberCount.ToString() & " members"
        End Get
    End Property
End Class

Public Class CommunityNote
    Public Property Id As String
    Public Property Text As String
    Public Property Tags As New List(Of String)()
    Public Property DateText As String
    Public Property Pinned As Boolean
    Public ReadOnly Property TagsLabel As String
        Get
            Return String.Join(" · ", Tags)
        End Get
    End Property
End Class

Public Class SavedFlipbook
    Public Property Id As String
    Public Property Name As String
    Public Property Wire As String
    Public Property SavedAt As Long
    Public ReadOnly Property PreviewMessage As Message
        Get
            Return New Message With {.Id = Id, .Text = Wire}
        End Get
    End Property
End Class

''' <summary>A key/value row returned by the official app cloud-save RPC.</summary>
Public Class CloudSave
    Public Property Key As String
    Public Property Value As String
    Public Property SavedAt As DateTimeOffset
    Public Property ModifiedBy As String
    Public ReadOnly Property Preview As String
        Get
            Dim text = If(Value, "").Replace(vbCr, " ").Replace(vbLf, " ")
            Return If(text.Length > 700, text.Substring(0, 700) & "…", text)
        End Get
    End Property
End Class

''' <summary>Count-only response from the official moderator statistics endpoint.</summary>
Public Class ModeratorStats
    Public Property Level As String
    Public Property Users As Integer
    Public Property OnlineNow As Integer
    Public Property VisitsToday As Integer
    Public Property Visitors7d As Integer
    Public Property Messages As Integer
    Public Property Groups As Integer
    Public Property FeedbackOpen As Integer
    Public Property AppsPending As Integer?
End Class

''' <summary>A row from the signed-in topic directory RPC (kh_app_list, app "khtopics").</summary>
Public Class TopicListing
    Public Property Code As String
    Public Property RawName As String
    Public Property Creator As String
    Public Property CreatedAt As DateTimeOffset

    Public ReadOnly Property Title As String
        Get
            Dim parsed = TopicHelper.Parse(RawName)
            Return parsed.Title
        End Get
    End Property

    Public ReadOnly Property Blurb As String
        Get
            Return TopicHelper.Parse(RawName).Blurb
        End Get
    End Property

    Public ReadOnly Property CategoryId As String
        Get
            Return TopicHelper.Parse(RawName).Cat
        End Get
    End Property
End Class

Public Structure TopicParts
    Public Cat As String
    Public Title As String
    Public Blurb As String
End Structure

''' <summary>Port of the official _khTopicParse name encoder/parser.</summary>
Public Module TopicHelper
    Public Const TopicPrefix As String = "TOPIC: "

    Public Class TopicCategoryInfo
        Public Property Id As String
        Public Property Name As String
    End Class

    Public ReadOnly Property Categories As TopicCategoryInfo() = {
        New TopicCategoryInfo With {.Id = "gen", .Name = "General"},
        New TopicCategoryInfo With {.Id = "book", .Name = "Books & Reading"},
        New TopicCategoryInfo With {.Id = "tech", .Name = "E-readers & Tech"},
        New TopicCategoryInfo With {.Id = "game", .Name = "Games"},
        New TopicCategoryInfo With {.Id = "help", .Name = "Help & Questions"},
        New TopicCategoryInfo With {.Id = "make", .Name = "Creative"},
        New TopicCategoryInfo With {.Id = "off", .Name = "Off-topic"}
    }

    Public Function CategoryName(id As String) As String
        For Each c In Categories
            If c.Id = id Then Return c.Name
        Next
        Return "General"
    End Function

    Public Function CategoryIdByName(name As String) As String
        For Each c In Categories
            If String.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase) Then Return c.Id
        Next
        Return "gen"
    End Function

    Public Function Parse(raw As String) As TopicParts
        Dim s = If(raw, "")
        If s.StartsWith(TopicPrefix, StringComparison.Ordinal) Then
            s = s.Substring(TopicPrefix.Length)
        End If
        Dim gen = "gen"
        Dim m = System.Text.RegularExpressions.Regex.Match(s, "^\[([a-z]{3,4})\]\s*")
        If m.Success Then
            For Each c In Categories
                If c.Id = m.Groups(1).Value Then gen = c.Id
            Next
            s = s.Substring(m.Length)
        End If
        Dim blurb = ""
        Dim sep = s.IndexOf(" :: ", StringComparison.Ordinal)
        If sep >= 0 Then
            blurb = s.Substring(sep + 4)
            s = s.Substring(0, sep)
        End If
        Return New TopicParts With {.Cat = gen, .Title = If(String.IsNullOrEmpty(s), "Topic", s), .Blurb = blurb}
    End Function

    ''' <summary>Builds a stored topic name from parts, matching the official create path.</summary>
    Public Function BuildName(title As String, blurb As String, categoryId As String) As String
        Dim name = TopicPrefix & "[" & If(String.IsNullOrEmpty(categoryId), "gen", categoryId) & "] " & If(title, "").Trim()
        If Not String.IsNullOrWhiteSpace(blurb) Then
            name &= " :: " & blurb.Trim()
        End If
        Return name
    End Function
End Module

Public Class Message
    Implements INotifyPropertyChanged

    Public Property Id As String
    Public Property GroupCode As String
    Public Property UserId As String
    Public Property DisplayName As String
    Public Property Text As String
    Public Property Timestamp As DateTimeOffset
    ''' <summary>reaction emoji -> whether the current user has toggled it (server stores user-id arrays).</summary>
    Public Property Reactions As Dictionary(Of String, Integer)
    Public Property ReactionEmoji As Dictionary(Of String, List(Of String))
    Public Property DeviceHint As String
    Public Property LocationHint As String
    Public Property Important As Boolean
    Public Property Edited As Boolean
    Public Property ReplyTo As String
    Private _communityComments As New System.Collections.ObjectModel.ObservableCollection(Of Message)()
    Public Property CommunityComments As System.Collections.ObjectModel.ObservableCollection(Of Message)
        Get
            Return _communityComments
        End Get
        Set(value As System.Collections.ObjectModel.ObservableCollection(Of Message))
            _communityComments = If(value, New System.Collections.ObjectModel.ObservableCollection(Of Message)())
            RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs(NameOf(CommunityComments)))
            RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs(NameOf(CommunityCommentCount)))
        End Set
    End Property
    Public ReadOnly Property CommunityCommentCount As Integer
        Get
            Return If(_communityComments Is Nothing, 0, _communityComments.Count)
        End Get
    End Property
    Private _communityPostEditDraft As String = ""
    Public Property CommunityPostEditDraft As String
        Get
            Return _communityPostEditDraft
        End Get
        Set(value As String)
            If _communityPostEditDraft = value Then Return
            _communityPostEditDraft = value
            RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs(NameOf(CommunityPostEditDraft)))
        End Set
    End Property
    Private _isCommunityPostEditing As Boolean
    Public Property IsCommunityPostEditing As Boolean
        Get
            Return _isCommunityPostEditing
        End Get
        Set(value As Boolean)
            If _isCommunityPostEditing = value Then Return
            _isCommunityPostEditing = value
            RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs(NameOf(IsCommunityPostEditing)))
        End Set
    End Property
    Private _communityCommentDraft As String = ""
    Public Property CommunityCommentDraft As String
        Get
            Return _communityCommentDraft
        End Get
        Set(value As String)
            If _communityCommentDraft = value Then Return
            _communityCommentDraft = value
            RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs(NameOf(CommunityCommentDraft)))
        End Set
    End Property
    Private _communityCommentsExpanded As Boolean
    Public Property CommunityCommentsExpanded As Boolean
        Get
            Return _communityCommentsExpanded
        End Get
        Set(value As Boolean)
            If _communityCommentsExpanded = value Then Return
            _communityCommentsExpanded = value
            RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs(NameOf(CommunityCommentsExpanded)))
        End Set
    End Property
    ''' <summary>Write token the sender uses to edit/unsend/react on their own row.</summary>
    Public Property OwnerSecret As String
    Public Property IsMine As Boolean
    Private _nameStyle As String
    Public Property NameStyle As String
        Get
            Return _nameStyle
        End Get
        Set(value As String)
            If _nameStyle = value Then Return
            _nameStyle = value
            RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs(NameOf(NameStyle)))
            RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs(NameOf(StyledDisplayName)))
        End Set
    End Property
    Private _profileFrame As String
    Public Property ProfileFrame As String
        Get
            Return _profileFrame
        End Get
        Set(value As String)
            If _profileFrame = value Then Return
            _profileFrame = value
            RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs(NameOf(ProfileFrame)))
            RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs(NameOf(HasProfileFrame)))
        End Set
    End Property
    Public ReadOnly Property HasProfileFrame As Boolean
        Get
            Return Not String.IsNullOrEmpty(ProfileFrame)
        End Get
    End Property
    Private _profileFrameImage As Object
    ''' <summary>UI supplied overlay image loaded from the official frame asset catalog.</summary>
    Public Property ProfileFrameImage As Object
        Get
            Return _profileFrameImage
        End Get
        Set(value As Object)
            If Object.Equals(_profileFrameImage, value) Then Return
            _profileFrameImage = value
            RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs(NameOf(ProfileFrameImage)))
        End Set
    End Property
    Private _profileRole As String
    Public Property ProfileRole As String
        Get
            Return _profileRole
        End Get
        Set(value As String)
            If _profileRole = value Then Return
            _profileRole = value
            RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs(NameOf(ProfileRole)))
            RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs(NameOf(ProfileRoleLabel)))
            RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs(NameOf(HasProfileRole)))
        End Set
    End Property
    Private _profilePlan As String
    Public Property ProfilePlan As String
        Get
            Return _profilePlan
        End Get
        Set(value As String)
            If _profilePlan = value Then Return
            _profilePlan = value
            RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs(NameOf(ProfilePlan)))
            RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs(NameOf(ProfileRoleLabel)))
            RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs(NameOf(HasProfileRole)))
        End Set
    End Property
    Public ReadOnly Property ProfileRoleLabel As String
        Get
            Dim roleLabel As String
            Select Case If(ProfileRole, "").ToLowerInvariant()
                Case "creator" : roleLabel = "Creator"
                Case "ultra" : roleLabel = "Ultra"
                Case "mod" : roleLabel = "Moderator"
                Case Else : roleLabel = ""
            End Select
            Dim planLabel As String
            Select Case If(ProfilePlan, "").ToLowerInvariant()
                Case "plus" : planLabel = "Plus"
                Case "pro" : planLabel = "Pro"
                Case "max" : planLabel = "Max"
                Case Else : planLabel = ""
            End Select
            If String.IsNullOrEmpty(roleLabel) Then Return planLabel
            If String.IsNullOrEmpty(planLabel) Then Return roleLabel
            Return roleLabel & " · " & planLabel
        End Get
    End Property
    Public ReadOnly Property HasProfileRole As Boolean
        Get
            Return Not String.IsNullOrEmpty(ProfileRoleLabel)
        End Get
    End Property
    Public ReadOnly Property StyledDisplayName As String
        Get
            Dim name = If(DisplayName, "Reader")
            Select Case If(NameStyle, "")
                Case "mark" : Return "◆ " & name
                Case "bold" : Return name.ToUpperInvariant()
                Case "rule" : Return "┃ " & name
                Case "both" : Return "◆ ┃ " & name
                Case "star" : Return "★ " & name
                Case "box" : Return "【" & name & "】"
                Case "serif" : Return "𝑺 " & name
                Case "crown" : Return "♛ " & name
                Case "chip" : Return "▣ " & name
                Case "frame" : Return "▤ " & name
                Case "under" : Return "＿ " & name
                Case Else : Return name
            End Select
        End Get
    End Property

    Private _avatarCode As String
    Public Property AvatarCode As String
        Get
            Return _avatarCode
        End Get
        Set(value As String)
            If _avatarCode = value Then Return
            _avatarCode = value
            RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs(NameOf(AvatarCode)))
            RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs(NameOf(AvatarInitial)))
        End Set
    End Property
    Private _isContinuation As Boolean
    Public Property IsContinuation As Boolean
        Get
            Return _isContinuation
        End Get
        Set(value As Boolean)
            If _isContinuation = value Then Return
            _isContinuation = value
            RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs(NameOf(IsContinuation)))
            RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs(NameOf(ShowSenderName)))
            RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs(NameOf(ShowIncomingAvatar)))
            RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs(NameOf(ShowOwnAvatar)))
        End Set
    End Property
    Private _showDateDivider As Boolean
    Public Property ShowDateDivider As Boolean
        Get
            Return _showDateDivider
        End Get
        Set(value As Boolean)
            If _showDateDivider = value Then Return
            _showDateDivider = value
            RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs(NameOf(ShowDateDivider)))
        End Set
    End Property
    Public ReadOnly Property ShowSenderName As Boolean
        Get
            Return Not IsContinuation
        End Get
    End Property
    Public ReadOnly Property ShowIncomingAvatar As Boolean
        Get
            Return Not IsContinuation
        End Get
    End Property
    Public ReadOnly Property ShowOwnAvatar As Boolean
        Get
            Return IsMine AndAlso Not IsContinuation
        End Get
    End Property
    Public ReadOnly Property AvatarInitial As String
        Get
            If String.IsNullOrWhiteSpace(DisplayName) Then Return "?"
            Return DisplayName.Trim().Substring(0, 1).ToUpperInvariant()
        End Get
    End Property

    Private _isStarred As Boolean
    ''' <summary>Starred locally in this session (the official keeps it device-local).</summary>
    Public Property IsStarred As Boolean
        Get
            Return _isStarred
        End Get
        Set(value As Boolean)
            If _isStarred = value Then Return
            _isStarred = value
            RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs(NameOf(IsStarred)))
        End Set
    End Property

    Private _isSelected As Boolean
    ''' <summary>UI state: the row whose action bar (reply / react / star / notes / DM / report
    ''' / copy) the user has opened by tapping the message, like the official client.</summary>
    Public Property IsSelected As Boolean
        Get
            Return _isSelected
        End Get
        Set(value As Boolean)
            If _isSelected = value Then Return
            _isSelected = value
            RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs(NameOf(IsSelected)))
        End Set
    End Property

    Private _replyPreview As String
    ''' <summary>Populated after load: "name: snippet" of the message this one replies to.</summary>
    Public Property ReplyPreview As String
        Get
            Return _replyPreview
        End Get
        Set(value As String)
            If _replyPreview = value Then Return
            _replyPreview = value
            RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs(NameOf(ReplyPreview)))
            RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs(NameOf(HasReplyPreview)))
        End Set
    End Property
    Public ReadOnly Property HasReply As Boolean
        Get
            Return Not String.IsNullOrEmpty(ReplyTo)
        End Get
    End Property

    ''' <summary>True when a quoted-parent preview line should render above this message.</summary>
    Public ReadOnly Property HasReplyPreview As Boolean
        Get
            Return HasReply AndAlso Not String.IsNullOrEmpty(ReplyPreview)
        End Get
    End Property

    ''' <summary>A data: image URI extracted from the official "KHIMG1:" media marker, or Nothing.</summary>
    Public ReadOnly Property ImageDataUri As String
        Get
            Return ChatMedia.TryParseImage(Text)
        End Get
    End Property

    Public ReadOnly Property IsImage As Boolean
        Get
            Return ImageDataUri IsNot Nothing
        End Get
    End Property

    Public ReadOnly Property NotImage As Boolean
        Get
            Return Not IsImage
        End Get
    End Property

    ''' <summary>Whether the plain text line should be shown. Messages that render a
    ''' dedicated card (app share, story, flipbook) or an image suppress it, so the
    ''' row never shows both "[App]" and the card describing the same thing.</summary>
    Public ReadOnly Property ShowPlainText As Boolean
        Get
            Return Not IsImage AndAlso ChatMedia.TryParseAppShare(Text) Is Nothing AndAlso ChatMedia.TryParseStory(Text) Is Nothing AndAlso ChatMedia.TryParseFlipbook(Text) Is Nothing
        End Get
    End Property

    ''' <summary>The text shown in the chat line. For encoded KH_ messages this returns
    ''' a human label ("[photo]", "[App]", "[Poll] …") instead of the raw wire blob.</summary>
    Public ReadOnly Property DisplayText As String
        Get
            If IsImage Then Return "[photo]"
            If ChatMedia.IsEncodedMsg(Text) Then Return ChatMedia.DescribeForChat(Text)
            Return Text
        End Get
    End Property

    ''' <summary>Parsed sticker URL for KHSTK1, or Nothing.</summary>
    Public ReadOnly Property StickerUrl As String
        Get
            Return ChatMedia.TryParseSticker(Text)
        End Get
    End Property

    ''' <summary>Parsed sticker prompt for KHSTK1, or Nothing.</summary>
    Public ReadOnly Property StickerPrompt As String
        Get
            Return ChatMedia.TryParseStickerPrompt(Text)
        End Get
    End Property

    ''' <summary>Parsed poll options (up to 4) for KHPOLL1, or Nothing.</summary>
    Public ReadOnly Property PollOptions As String()
        Get
            Return ChatMedia.TryParsePollOptions(Text)
        End Get
    End Property

    ''' <summary>True when this message is a poll with options to render.</summary>
    Public ReadOnly Property HasPollOptions As Boolean
        Get
            Return PollOptions IsNot Nothing AndAlso PollOptions.Length > 0
        End Get
    End Property

    Private _pollVoteCounts As Integer()
    Public Property PollVoteCounts As Integer()
        Get
            Return _pollVoteCounts
        End Get
        Set(value As Integer())
            _pollVoteCounts = value
            RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs(NameOf(PollVoteCounts)))
            RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs(NameOf(PollTotalVotes)))
            RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs(NameOf(PollOptionItems)))
        End Set
    End Property

    Public ReadOnly Property PollTotalVotes As Integer
        Get
            If _pollVoteCounts Is Nothing Then Return 0
            Dim sum = 0
            For Each c In _pollVoteCounts
                sum += c
            Next
            Return sum
        End Get
    End Property

    Public ReadOnly Property PollOptionItems As Object()
        Get
            If PollOptions Is Nothing OrElse PollOptions.Length = 0 Then Return Array.Empty(Of Object)()
            Dim list = New List(Of PollOptionItem)()
            For i = 0 To PollOptions.Length - 1
                Dim count = If(_pollVoteCounts IsNot Nothing AndAlso i < _pollVoteCounts.Length, _pollVoteCounts(i), 0)
                list.Add(New PollOptionItem With {.Text = PollOptions(i), .Votes = count, .Index = i})
            Next
            Return list.ToArray()
        End Get
    End Property

    Public Class PollOptionItem
        Public Property Text As String
        Public Property Votes As Integer
        Public Property Index As Integer
    End Class

    ''' <summary>True when this message is a KHFLIP1 flipbook (as opposed to a drawing).</summary>
    Public ReadOnly Property IsFlipbook As Boolean
        Get
            Return ChatMedia.TryParseFlipbook(Text) IsNot Nothing
        End Get
    End Property

    ''' <summary>Number of frames in a flipbook message, or 0.</summary>
    Public ReadOnly Property FlipbookFrameCount As Integer
        Get
            Dim fb = ChatMedia.TryParseFlipbook(Text)
            If fb Is Nothing OrElse fb.Frames Is Nothing Then Return 0
            Return fb.Frames.Length
        End Get
    End Property

    ''' <summary>Author-supplied flipbook name, or Nothing.</summary>
    Public ReadOnly Property FlipbookName As String
        Get
            Dim fb = ChatMedia.TryParseFlipbook(Text)
            If fb Is Nothing Then Return Nothing
            Return If(String.IsNullOrWhiteSpace(fb.Name), Nothing, fb.Name)
        End Get
    End Property

    ''' <summary>True when this message is a KHSTORY1 story, so the row can offer to open it.</summary>
    Public ReadOnly Property IsStory As Boolean
        Get
            Return ChatMedia.TryParseStory(Text) IsNot Nothing
        End Get
    End Property

    ''' <summary>True when this message is a KHSTK1 sticker, so the row can draw its glyph.</summary>
    Public ReadOnly Property IsSticker As Boolean
        Get
            Return ChatMedia.IsStickerMessage(Text)
        End Get
    End Property

    ''' <summary>Parsed app identifier for KHAPP1, or Nothing.</summary>
    Public ReadOnly Property AppId As String
        Get
            Return ChatMedia.TryParseAppId(Text)
        End Get
    End Property

    ''' <summary>The full app share carried by a KHAPP1 message, or Nothing.</summary>
    Public ReadOnly Property AppShare As ChatMedia.AppShare
        Get
            Return ChatMedia.TryParseAppShare(Text)
        End Get
    End Property

    ''' <summary>True when this message is a KHAPP1 app share, so the row can offer to open it.</summary>
    Public ReadOnly Property IsAppShare As Boolean
        Get
            Return ChatMedia.TryParseAppShare(Text) IsNot Nothing
        End Get
    End Property

    ''' <summary>Inverse of <see cref="IsAppShare"/> — used to hide the plain
    ''' "[App]" text row when the tappable card is showing instead.</summary>
    Public ReadOnly Property NotAppShare As Boolean
        Get
            Return ChatMedia.TryParseAppShare(Text) Is Nothing
        End Get
    End Property

    ''' <summary>Display name of a shared app, falling back to a generic label.</summary>
    Public ReadOnly Property AppLabel As String
        Get
            Dim app = ChatMedia.TryParseAppShare(Text)
            If app Is Nothing Then Return ""
            Dim lbl = If(app.Label, "").Trim()
            Return If(lbl.Length = 0, "Shared app", lbl)
        End Get
    End Property

    ''' <summary>Size of the shared app's HTML, for the card's subtitle.</summary>
    Public ReadOnly Property AppSizeLabel As String
        Get
            Dim app = ChatMedia.TryParseAppShare(Text)
            If app Is Nothing Then Return ""
            Dim n = If(app.Html, "").Length
            If n <= 0 Then Return "empty"
            Return If(n < 1024, $"{n:N0} characters", $"{n / 1024.0:0.#} KB of HTML")
        End Get
    End Property

    ''' <summary>Parsed story label for KHSTORY1, or Nothing.</summary>
    Public ReadOnly Property StoryLabel As String
        Get
            Return ChatMedia.TryParseStoryLabel(Text)
        End Get
    End Property

    ''' <summary>Parsed vote counts/state for KHVOTE1, or Nothing.</summary>
    Public ReadOnly Property VoteSummary As String
        Get
            Return ChatMedia.TryParseVoteSummary(Text)
        End Get
    End Property

    Public ReadOnly Property TimestampFormatted As String
        Get
            Return Timestamp.ToLocalTime().ToString("h:mm tt")
        End Get
    End Property

    Public ReadOnly Property AgeFormatted As String
        Get
            Return DisplayUtil.RelativeAge(Timestamp)
        End Get
    End Property

    ''' <summary>One-line "👍 2  ♥ 1" summary of reaction counts (empty if none).</summary>
    Public ReadOnly Property ReactionsText As String
        Get
            If Reactions Is Nothing OrElse Reactions.Count = 0 Then Return ""
            Return String.Join("  ", From kv In Reactions Order By kv.Key Select kv.Key & " " & kv.Value)
        End Get
    End Property

    Public ReadOnly Property HasReactions As Boolean
        Get
            Return Reactions IsNot Nothing AndAlso Reactions.Count > 0
        End Get
    End Property

    Public Event PropertyChanged As PropertyChangedEventHandler Implements INotifyPropertyChanged.PropertyChanged

    Friend Sub RaiseAll()
        RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs(Nothing))
    End Sub
End Class

Public Module DisplayUtil
    Public Function RelativeAge(ts As DateTimeOffset) As String
        Dim mins = Math.Round((DateTimeOffset.UtcNow - ts).TotalMinutes)
        If mins < 1 Then Return "just now"
        If mins < 60 Then Return CInt(mins) & "m ago"
        If mins < 1440 Then Return CInt(Math.Round(mins / 60)) & "h ago"
        Return CInt(Math.Round(mins / 1440)) & "d ago"
    End Function
End Module

Public Class LeaderboardEntry
    Public Property Id As String
    Public Property Game As String
    Public Property Score As Long
    Public Property DisplayName As String
    Public Property UserId As String
    Public Property [Date] As DateTimeOffset

    Public ReadOnly Property ScoreFormatted As String
        Get
            Return Score.ToString("N0")
        End Get
    End Property

    Public ReadOnly Property DateFormatted As String
        Get
            Return [Date].ToString("MMM dd, yyyy")
        End Get
    End Property
End Class

''' <summary>One row of kh_presence: a signed-in device seen recently.</summary>
Public Class PresenceEntry
    Public Property UserId As String
    Public Property DisplayName As String
    Public Property Avatar As String
    Public Property Profile As String
    Public Property LastSeen As DateTimeOffset

    Public ReadOnly Property AgeFormatted As String
        Get
            Return DisplayUtil.RelativeAge(LastSeen)
        End Get
    End Property
End Class

Public Class PublicProfileDetails
    Public Property Avatar As String
    Public Property ProfileFrame As String
    Public Property NameStyle As String
    Public Property Role As String
    Public Property Plan As String
End Class

''' <summary>App-store catalogue row (kh_store_apps minus the html body).
''' Columns mirror the live server: id,name,cat,author,model,created_at,downloads,preview,icon_art,rating_sum,rating_count.</summary>
Public Class AppCatalog
    Public Property Id As String
    Public Property Name As String
    Public Property Category As String
    Public Property Author As String
    Public Property Model As String
    Public Property CreatedAt As DateTimeOffset
    Public Property Downloads As Integer
    Public Property Preview As String
    Public Property IconArt As String
    Public Property AgeRating As String
    Public Property RatingSum As Integer
    Public Property RatingCount As Integer

    Public ReadOnly Property Rating As Double
        Get
            Return If(RatingCount > 0, CType(RatingSum, Double) / RatingCount, 0.0)
        End Get
    End Property

    Public ReadOnly Property TitleWithCategory As String
        Get
            Return Name & " · " & Category
        End Get
    End Property

    ''' <summary>Icon art is stored as a data: URI (see _khIconArtValid in the
    ''' web client), so it can go straight to an image converter.</summary>
    Public ReadOnly Property HasIcon As Boolean
        Get
            Return Not String.IsNullOrWhiteSpace(IconArt) AndAlso
                   IconArt.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)
        End Get
    End Property

    ''' <summary>True when there is no icon, so the initials placeholder should show.</summary>
    Public ReadOnly Property ShowInitials As Boolean
        Get
            Return Not HasIcon
        End Get
    End Property

    Public ReadOnly Property RatingFormatted As String
        Get
            Return If(RatingCount > 0, String.Format("{0:0.0} ({1})", Rating, RatingCount), "not rated yet")
        End Get
    End Property

    ''' <summary>1-2 letter placeholder shown in the app's icon tile.</summary>
    Public ReadOnly Property Initials As String
        Get
            Dim n = If(Name, "").Trim()
            If n.Length = 0 Then Return "?"
            Dim parts = n.Split({" "c}, StringSplitOptions.RemoveEmptyEntries)
            Return If(parts.Length >= 2, (parts(0)(0) & parts(1)(0)).ToUpperInvariant(), n.Substring(0, Math.Min(2, n.Length)).ToUpperInvariant())
        End Get
    End Property

    Public ReadOnly Property DownloadsFormatted As String
        Get
            Return Downloads.ToString("N0") & " installs"
        End Get
    End Property
End Class

Public Class PublishedApp
    Public Property Id As String
    Public Property Name As String
    Public Property Category As String
    Public Property Html As String
    Public Property Author As String
    Public Property OwnerSecret As String
End Class

Public Class ServerStatus
    Public Property Online As Boolean
    Public Property UsersOnline As Integer
    Public Property GamesPlayed As Long
    Public Property Version As String
End Class

Public Class SyncState
    Public Property LastSync As DateTimeOffset?
    Public Property PendingUpload As Boolean
    Public Property PendingDownload As Boolean
    Public Property ConflictCount As Integer
End Class

''' <summary>A parsed multiplayer event received over chat (JSON in kh_messages.text).</summary>
''' <summary>A downloadable app the user has opened from the store. HTML is cached to disk.</summary>
Public Class StoredApp
    Public Property AppId As String
    Public Property Name As String
    Public Property Category As String
    Public Property HtmlPath As String
    Public Property LocalizedOn As DateTimeOffset
End Class

''' <summary>Keep a small set of ids we've already rendered so chat polling doesn't duplicate rows.</summary>
Public Class SeenMessageIndex
    Private ReadOnly _ids As New HashSet(Of String)(StringComparer.Ordinal)

    Public Function Note(id As String) As Boolean
        If String.IsNullOrEmpty(id) Then Return False
        If _ids.Contains(id) Then Return False
        _ids.Add(id)
        If _ids.Count > 2000 Then
            Dim keep = _ids.Skip(_ids.Count - 1000).ToList()
            _ids.Clear()
            For Each k In keep
                _ids.Add(k)
            Next
        End If
        Return True
    End Function
End Class

''' <summary>A store app whose full HTML body was downloaded for play.</summary>
Public Class DownloadedApp
    Public Property Id As String
    Public Property Name As String
    Public Property Category As String
    Public Property Author As String
    Public Property Html As String
End Class

''' <summary>One parsed multiplayer relay envelope received from a room (JSON body over encrypted chat).</summary>
Public Class RoomMessageEnvelope
    Public Property Message As Message
    Public Property Type As String
    Public Property Data As System.Text.Json.JsonDocument

    Public Function Str(field As String) As String
        Try
            Dim el As System.Text.Json.JsonElement
            If Data IsNot Nothing AndAlso Data.RootElement.TryGetProperty(field, el) AndAlso el.ValueKind = System.Text.Json.JsonValueKind.String Then Return el.GetString()
        Catch
        End Try
        Return ""
    End Function

    Public Function Number(field As String) As Long
        Try
            Dim el As System.Text.Json.JsonElement
            If Data IsNot Nothing AndAlso Data.RootElement.TryGetProperty(field, el) Then
                If el.ValueKind = System.Text.Json.JsonValueKind.Number Then Return el.GetInt64()
                If el.ValueKind = System.Text.Json.JsonValueKind.String Then
                    Dim v As Long
                    If Int64.TryParse(el.GetString(), v) Then Return v
                End If
            End If
        Catch
        End Try
        Return 0
    End Function

    Public Function Array(field As String) As System.Collections.Generic.List(Of String)
        Dim list As New System.Collections.Generic.List(Of String)()
        Try
            Dim el As System.Text.Json.JsonElement
            If Data IsNot Nothing AndAlso Data.RootElement.TryGetProperty(field, el) AndAlso el.ValueKind = System.Text.Json.JsonValueKind.Array Then
                For Each v In el.EnumerateArray()
                    If v.ValueKind = System.Text.Json.JsonValueKind.String Then list.Add(v.GetString())
                Next
            End If
        Catch
        End Try
        Return list
    End Function
End Class
