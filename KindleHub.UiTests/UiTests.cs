using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using KindleHub.Client;
using KindleHub.Client.ViewModels;
using KindleHub.Client.Views;
using KindleHub.Core;
using Microsoft.Extensions.Logging.Abstractions;

[assembly: AvaloniaTestApplication(typeof(KindleHub.UiTests.TestAppBuilder))]

namespace KindleHub.UiTests;

// Load the real styles/resources without starting the production host, restoring
// a real session, or issuing requests as the developer's saved account.
public class TestApp : App
{
    public override void OnFrameworkInitializationCompleted() { }
}
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<TestApp>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

public class UiTests
{
    public static TheoryData<string> Pages => new()
    {
        "Home", "Leaderboards", "Community", "Messages", "Mail", "Games", "Arcade", "App Store", "Settings", "Flipbook"
    };

    [AvaloniaTheory]
    [MemberData(nameof(Pages))]
    public void EveryPageLoadsAndLaysOutAtDesktopAndMobileWidths(string page)
    {
        UserControl view = page switch {
            "Home" => new HomeView(), "Leaderboards" => new LeaderboardView(),
            "Community" => new CommunityView(), "Messages" => new MessagesView(),
            "Mail" => new MailView(), "Games" => new GamesView(), "Arcade" => new ArcadeView(),
            "App Store" => new AppStoreView(), "Settings" => new SettingsView(),
            _ => new FlipbookEditorView()
        };
        var window = new Window { Content = view, Width = 1100, Height = 800 };
        try {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.True(view.Bounds.Width > 0);
            Assert.NotEmpty(view.GetVisualDescendants());
            window.Width = 390;
            window.Height = 844;
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.True(view.Bounds.Width > 0);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void HomeNavigationCommandPassesSelectedDestination()
    {
        using var http = new HttpClient();
        var api = new KindleHubApiClient(http, new(), NullLogger<KindleHubApiClient>.Instance);
        using var core = new KindleHubCore(api, NullLogger<KindleHubCore>.Instance, http);
        string? destination = null;
        var vm = new HomeViewModel(core, NullLogger<HomeViewModel>.Instance, s => destination = s);
        var view = new HomeView { DataContext = vm };
        Assert.Contains("Welcome", vm.WelcomeText);
        vm.NavigateCommand.Execute("Messages");
        Assert.Equal("Messages", destination);
    }

    [AvaloniaFact]
    public void FlipbookEditingSupportsUndoFramesResizeAndSharing()
    {
        var vm = new FlipbookEditorViewModel();
        var view = new FlipbookEditorView { DataContext = vm };
        vm.BeginStroke();
        vm.ApplyCell(0, 1);
        Assert.True(vm.HasDrawing());
        vm.Undo();
        Assert.False(vm.HasDrawing());
        vm.ApplyCell(0, 1);
        vm.DuplicateFrame();
        Assert.Equal(2, vm.FrameCount);
        vm.ApplyCell(1, 1);
        Assert.Equal(0, vm.GetFrame(0)![1]); // duplicated frames own their cells
        vm.MoveFrameLeft();
        vm.SetGrid(28);
        Assert.Equal(28 * 28, vm.GetFrame(0)!.Length);
        Assert.NotEmpty(vm.BuildWire());
        string? sent = null;
        vm.SendFlipbook = s => sent = s;
        vm.Send();
        Assert.NotNull(sent);
        vm.DeleteFrame();
        vm.DeleteFrame();
        Assert.Equal(1, vm.FrameCount);
        Assert.Equal(-1, vm.CellFromPoint(new Point(-1, 0)));
        vm.Stop();
    }
}
