using KindleHub.Core;
using KindleHub.Client.Models;

namespace KindleHub.Tests;

public class MediaTests
{
    [Fact]
    public void FlipbookPackingPreservesOddCellCountsAndWireMetadata()
    {
        int[] frame = [1, 0, 1, 1, 0, 1, 0];
        var packed = ChatMedia.FlipPack(frame, frame.Length);
        Assert.Equal(frame, ChatMedia.FlipUnpack(packed, frame.Length));
        var wire = ChatMedia.EncodeFlipbook(new() { Name = "Demo", Width = 7, Height = 1, Fps = 6, Frames = [packed] });
        var parsed = ChatMedia.TryParseFlipbook(wire);
        Assert.Equal("Demo", parsed.Name);
        Assert.Equal(packed, Assert.Single(parsed.Frames));
    }

    [Fact]
    public void PollVotesAndFreeTextRoundTrip()
    {
        var poll = ChatMedia.TryParsePoll(ChatMedia.EncodePoll(new() { Id = "id", Question = "Which?", Options = ["A", "B"], Open = true }));
        Assert.Equal("Which?", poll.Question);
        var vote = ChatMedia.ParseVote(ChatMedia.EncodeVote(poll.Id, 1));
        Assert.Equal(poll.Id, vote.PollId);
        Assert.Equal(1, vote.OptionIndex);
        var note = ChatMedia.ParseVote(ChatMedia.EncodeVoteNote(poll.Id, "Other 世界"));
        Assert.Equal("Other 世界", note.Note);
        var longNote = ChatMedia.ParseVote(ChatMedia.EncodeVoteNote(poll.Id, new string('界', 60)));
        Assert.Equal(new string('界', 40), longNote.Note);
    }

    [Fact]
    public void SharedAppsAndStoriesRoundTrip()
    {
        var app = ChatMedia.TryParseAppShare(ChatMedia.EncodeAppShare(new() { ShareId = "app", Label = "Example", Color = "blue", Icon = "X", Html = "<p>Hello</p>" }));
        Assert.Equal("<p>Hello</p>", app.Html);
        var story = ChatMedia.TryParseStory(ChatMedia.EncodeStory(new() { Setting = "Space", Theme = "Mystery", Log = [new() { Role = "user", Text = "Hello 世界" }] }));
        Assert.Equal("Hello 世界", Assert.Single(story.Log).Text);
    }

    [Theory]
    [InlineData("KHFLIP1:broken")] [InlineData("KHPOLL1:broken")] [InlineData("KHSTORY1:broken")] [InlineData("not media")]
    public void InvalidWireDataIsRejected(string text)
    {
        Assert.Null(ChatMedia.TryParseFlipbook(text));
        Assert.Null(ChatMedia.TryParsePoll(text));
        Assert.Null(ChatMedia.TryParseStory(text));
        Assert.Null(ChatMedia.TryParseAppShare(text));
    }

    [Fact]
    public void AvatarCodecPreservesEveryPaletteEntry()
    {
        var cells = Enumerable.Range(0, ProfileAvatar.CellCount).Select(i => i % 16).ToArray();
        var code = ProfileAvatar.Encode(2, cells);
        Assert.True(ProfileAvatar.TryDecode(code, out int bg, out var decoded));
        Assert.Equal(2, bg);
        Assert.Equal(cells, decoded);
        Assert.False(ProfileAvatar.TryDecode("KHAV2:2.broken", out _, out _));
    }
}
