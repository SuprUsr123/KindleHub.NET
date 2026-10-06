using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using KindleHub.Core;
using Microsoft.Extensions.Logging;

namespace KindleHub.Client.ViewModels;

public class HomeViewModel : ViewModelBase
{
    private readonly KindleHubCore _core;
    private readonly ILogger<HomeViewModel> _logger;
    private string _welcomeText = "Welcome to KindleHub Pro";

    public string WelcomeText
    {
        get => _welcomeText;
        set => SetProperty(ref _welcomeText, value);
    }

    public ICommand NavigateCommand { get; }

    public HomeViewModel(KindleHubCore core, ILogger<HomeViewModel> logger, Action<string?> navigate)
    {
        _core = core;
        _logger = logger;
        NavigateCommand = new RelayCommand<string>(navigate);
        var name = _core.CurrentProfile?.DisplayName;
        WelcomeText = string.IsNullOrWhiteSpace(name) ? "Welcome to KindleHub Pro" : $"Welcome back, {name}";
    }
}
