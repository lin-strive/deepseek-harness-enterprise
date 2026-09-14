using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using CompanyHarness.Contracts;
using CompanyHarness.Desktop.Core.Activation;
using CompanyHarness.Desktop.Core.Runtime;
using CompanyHarness.Desktop.Core.Storage;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Win32;

namespace CompanyHarness.Desktop;

public partial class MainWindow : Window
{
    private const string DefaultCredentialTarget = "SmartWheelchair.CompanyHarness.VirtualKey";
    private static readonly string ClientVersion = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    private const int WmNcHitTest = 0x0084;
    private const int WmSettingChange = 0x001A;
    private const int WmThemeChanged = 0x031A;
    private const int WmDwmColorizationColorChanged = 0x0320;
    private const int HtMaxButton = 9;
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const string ThemeBridgeScript = """
        (() => {
          if (window.__companyHarnessShellThemeBridgeInstalled) return;
          window.__companyHarnessShellThemeBridgeInstalled = true;

          const parseColor = value => {
            const match = String(value || '').match(/rgba?\(\s*(\d+(?:\.\d+)?)\s*[, ]\s*(\d+(?:\.\d+)?)\s*[, ]\s*(\d+(?:\.\d+)?)(?:\s*[,\/]\s*(\d+(?:\.\d+)?))?\s*\)/i);
            if (!match) return null;
            return { r: Number(match[1]), g: Number(match[2]), b: Number(match[3]), a: match[4] === undefined ? 1 : Number(match[4]) };
          };

          const luminance = color => {
            const channel = value => {
              const normalized = value / 255;
              return normalized <= 0.04045 ? normalized / 12.92 : Math.pow((normalized + 0.055) / 1.055, 2.4);
            };
            return 0.2126 * channel(color.r) + 0.7152 * channel(color.g) + 0.0722 * channel(color.b);
          };

          const detectDark = () => {
            const colorScheme = getComputedStyle(document.documentElement).colorScheme.trim().toLowerCase();
            if (colorScheme === 'dark' || colorScheme.startsWith('dark ')) return true;
            if (colorScheme === 'light' || colorScheme.startsWith('light ')) return false;

            for (const element of [document.body, document.documentElement]) {
              if (!element) continue;
              const color = parseColor(getComputedStyle(element).backgroundColor);
              if (color && color.a > 0.05) return luminance(color) < 0.35;
            }
            return window.matchMedia('(prefers-color-scheme: dark)').matches;
          };

          let lastValue;
          const report = () => {
            const value = detectDark() ? 'dark' : 'light';
            if (value === lastValue) return;
            lastValue = value;
            window.chrome?.webview?.postMessage({ source: 'company-harness-shell', type: 'theme', value });
          };

          window.__companyHarnessShellReportTheme = report;
          const observe = () => {
            const observer = new MutationObserver(report);
            const themeAttributes = ['class', 'style', 'data-theme', 'data-color-mode', 'data-ds-dark-theme'];
            observer.observe(document.documentElement, { attributes: true, attributeFilter: themeAttributes });
            if (document.body) observer.observe(document.body, { attributes: true, attributeFilter: themeAttributes });
            window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', report);
            requestAnimationFrame(report);
            setTimeout(report, 250);
          };
          document.readyState === 'loading' ? document.addEventListener('DOMContentLoaded', observe, { once: true }) : observe();
        })();
        """;

    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _startupGate = new(1, 1);
    private readonly string _applicationDataRoot;
    private readonly string _credentialTarget;
    private readonly ActivationClient _activationClient;
    private readonly IActivationProfileStore _profileStore;
    private readonly JsonShellPreferencesStore _shellPreferencesStore;
    private readonly ISecretStore _secretStore;
    private readonly HarnessProcessManager _processManager;
    private readonly StartupDiagnosticLog _startupDiagnosticLog;
    private readonly StartupLaunchContext _launchContext;

    private ActivationPreviewResponse? _activationPreview;
    private ActivationProfile? _activeProfile;
    private string? _virtualKey;
    private Uri? _localHarnessUri;
    private ShellPreferences _shellPreferences = ShellPreferences.Default;
    private bool? _harnessUsesDarkTheme;
    private bool _isApplyingThemeChoice;
    private bool _themeBridgeInstalled;
    private bool _postInstallWaitCompleted;
    private string? _lastDiagnosticDirectory;
    private WebView2? _harnessWebView;
    private HwndSource? _windowSource;
    private bool _isClosing;

    public MainWindow()
        : this(StartupLaunchContext.Default)
    {
    }

    public MainWindow(StartupLaunchContext launchContext)
    {
        InitializeComponent();

        _launchContext = launchContext;
        _applicationDataRoot = ResolveApplicationDataRoot();
        _credentialTarget = ResolveCredentialTarget();
        _profileStore = new JsonActivationProfileStore(Path.Combine(_applicationDataRoot, "profile.json"));
        _shellPreferencesStore = new JsonShellPreferencesStore(Path.Combine(_applicationDataRoot, "shell-preferences.json"));
        _startupDiagnosticLog = new StartupDiagnosticLog(_applicationDataRoot);
        _secretStore = new WindowsCredentialStore();
        _processManager = new HarnessProcessManager(new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(2),
        });

        var controlPlaneBaseUrl = ResolveControlPlaneBaseUrl();
        _activationClient = new ActivationClient(CreateControlPlaneHttpClient(controlPlaneBaseUrl));
        ApplyShellTheme(IsWindowsDarkMode());
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _shellPreferences = await _shellPreferencesStore.ReadAsync(_lifetime.Token);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _shellPreferences = ShellPreferences.Default;
        }

        UpdateThemeOptionChecks();
        ApplyResolvedShellTheme();

        try
        {
            _activeProfile = await _profileStore.ReadAsync(_lifetime.Token);
            _virtualKey = _secretStore.Read(_credentialTarget);
            if (_activeProfile is not null && !string.IsNullOrWhiteSpace(_virtualKey))
            {
                SetActiveEmployee(_activeProfile.Employee);
                await StartHarnessAsync();
                return;
            }

            ShowActivationPanel();
            ActivationCodeTextBox.Focus();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ShowActivationPanel();
            ShowActivationError("无法读取本地激活信息。请重新激活或联系管理员。", null);
        }
    }

    private async void ValidateButton_Click(object sender, RoutedEventArgs e)
    {
        HideActivationError();
        var activationCode = ActivationCodeTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(activationCode))
        {
            ShowActivationError("请输入管理员发放的激活码。", null);
            ActivationCodeTextBox.Focus();
            return;
        }

        SetActivationBusy(true);
        try
        {
            _activationPreview = await _activationClient.PreviewAsync(activationCode, ClientVersion, _lifetime.Token);
            EmployeeNameText.Text = _activationPreview.Employee.DisplayName;
            EmployeeNumberText.Text = _activationPreview.Employee.EmployeeNumber;
            EmployeeDepartmentText.Text = _activationPreview.Employee.Department;
            ActivationPanel.Visibility = Visibility.Collapsed;
            IdentityPanel.Visibility = Visibility.Visible;
            ConfirmButton.Focus();
        }
        catch (ActivationClientException exception)
        {
            ShowActivationError(exception.Message, exception.SupportId);
            ActivationCodeTextBox.Focus();
        }
        catch (HttpRequestException)
        {
            ShowActivationError("无法连接公司激活服务。请检查网络后重试。", null);
            ActivationCodeTextBox.Focus();
        }
        catch (TaskCanceledException) when (!_lifetime.IsCancellationRequested)
        {
            ShowActivationError("激活服务响应超时，请稍后重试。", null);
            ActivationCodeTextBox.Focus();
        }
        finally
        {
            SetActivationBusy(false);
        }
    }

    private async void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        if (_activationPreview is null)
        {
            ShowActivationPanel();
            ShowActivationError("身份确认已失效，请重新验证激活码。", null);
            return;
        }

        ConfirmButton.IsEnabled = false;
        ConfirmButton.Content = "正在激活…";
        try
        {
            var response = await _activationClient.ConfirmAsync(_activationPreview.ActivationSessionId, _lifetime.Token);
            ValidateCompanyPolicy(response);

            var profile = new ActivationProfile(
                response.Employee,
                response.GatewayBaseUrl,
                response.Quota,
                response.Policy.AllowedModels,
                response.Policy.DefaultModel,
                response.Policy.Models,
                DateTimeOffset.Now);

            _secretStore.Write(_credentialTarget, response.VirtualKey, response.Employee.EmployeeNumber);
            try
            {
                await _profileStore.WriteAsync(profile, _lifetime.Token);
            }
            catch
            {
                _secretStore.Delete(_credentialTarget);
                throw;
            }

            _activeProfile = profile;
            _virtualKey = response.VirtualKey;
            SetActiveEmployee(profile.Employee);
            await StartHarnessAsync();
        }
        catch (ActivationClientException exception)
        {
            ShowActivationPanel();
            ShowActivationError(exception.Message, exception.SupportId);
        }
        catch (HttpRequestException)
        {
            ShowActivationPanel();
            ShowActivationError("无法连接公司激活服务。激活码尚未在本机保存。", null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ShowActivationPanel();
            ShowActivationError($"激活未完成：{GetSafeErrorMessage(exception)}", null);
        }
        finally
        {
            ConfirmButton.IsEnabled = true;
            ConfirmButton.Content = "确认并激活";
        }
    }

    private void BackToActivation_Click(object sender, RoutedEventArgs e)
    {
        _activationPreview = null;
        IdentityPanel.Visibility = Visibility.Collapsed;
        ActivationPanel.Visibility = Visibility.Visible;
        ActivationCodeTextBox.Focus();
    }

    private async void RetryStartButton_Click(object sender, RoutedEventArgs e)
    {
        await StartHarnessAsync();
    }

    private async void RestartHarness_Click(object sender, RoutedEventArgs e)
    {
        EmployeeMenuPopup.IsOpen = false;
        await StartHarnessAsync();
    }

    private async Task StartHarnessAsync()
    {
        if (_activeProfile is null || string.IsNullOrWhiteSpace(_virtualKey))
        {
            ShowActivationPanel();
            ShowActivationError("本地激活信息不完整，请重新激活。", null);
            return;
        }

        if (_lifetime.IsCancellationRequested || !await _startupGate.WaitAsync(0))
        {
            return;
        }

        ShowStartingPanel(_launchContext.IsPostInstall && !_postInstallWaitCompleted
            ? "正在完成安装配置…"
            : "检查本地 Runtime…");
        try
        {
            await WaitForInstallerCompletionAsync(_lifetime.Token);
            await ResetStartupAttemptAsync(_lifetime.Token);
            StartingStatusText.Text = "正在启动本地 Harness…";

            var runtime = HarnessRuntimeLocator.Locate(AppContext.BaseDirectory, ClientVersion);
            var catalog = await ResolveModelCatalogAsync();
            var companyPatchPath = await HarnessCompanyPatchWriter.WriteAsync(
                _applicationDataRoot, catalog, _lifetime.Token);
            var options = new HarnessLaunchOptions(
                runtime.NodeExecutablePath,
                runtime.HarnessEntryPointPath,
                companyPatchPath,
                Path.Combine(_applicationDataRoot, "harness"),
                _activeProfile.GatewayBaseUrl,
                _virtualKey,
                catalog.DefaultModel,
                TimeSpan.FromSeconds(45));

            _localHarnessUri = await _processManager.StartAsync(options, _lifetime.Token);
            StartingStatusText.Text = "正在打开安全工作台…";
            await OpenHarnessWebViewAsync(_localHarnessUri, _lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // Application shutdown cancels any in-flight startup without surfacing an error dialog.
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await ResetStartupAttemptAsync(CancellationToken.None);
            var diagnostic = await RecordStartupFailureAsync(exception);
            ShowStartupFailure(exception, diagnostic);
        }
        finally
        {
            _startupGate.Release();
        }
    }

    private async Task WaitForInstallerCompletionAsync(CancellationToken cancellationToken)
    {
        if (_postInstallWaitCompleted)
        {
            return;
        }

        _postInstallWaitCompleted = true;
        if (!_launchContext.IsPostInstall
            || _launchContext.InstallerProcessId is not { } processId
            || processId == Environment.ProcessId)
        {
            return;
        }

        try
        {
            using var installer = Process.GetProcessById(processId);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            await installer.WaitForExitAsync(timeout.Token);
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        }
        catch (ArgumentException)
        {
            // The installer already exited between argument parsing and this check.
        }
        catch (InvalidOperationException)
        {
            // The installer already exited; normal startup can continue.
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Do not strand the user if an installer host remains alive unexpectedly.
        }
    }

    private void ShowStartupFailure(Exception exception, StartupDiagnosticRecord? diagnostic)
    {
        SetShellRunState(ShellRunState.Failed);
        StartingTitleText.Text = "工作台启动失败";
        StartingStatusText.Text = "已清理本次启动状态，可以直接重试。";
        StartingProgressBar.Visibility = Visibility.Collapsed;
        StartErrorText.Text = diagnostic is null
            ? GetSafeErrorMessage(exception)
            : $"{GetSafeErrorMessage(exception)} 诊断编号：{diagnostic.SupportId}";
        StartErrorText.Visibility = Visibility.Visible;
        RetryStartButton.IsEnabled = true;
        RetryStartButton.Content = "重新启动";
        RetryStartButton.Visibility = Visibility.Visible;
        OpenDiagnosticsButton.Visibility = diagnostic is null
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private async Task<StartupDiagnosticRecord?> RecordStartupFailureAsync(Exception exception)
    {
        try
        {
            var diagnostic = await _startupDiagnosticLog.WriteFailureAsync(
                ClientVersion,
                exception,
                _processManager.GetRecentStartupOutput(),
                CancellationToken.None);
            _lastDiagnosticDirectory = Path.GetDirectoryName(diagnostic.LogPath);
            return diagnostic;
        }
        catch (Exception logException) when (logException is IOException or UnauthorizedAccessException)
        {
            _lastDiagnosticDirectory = null;
            return null;
        }
    }

    private void OpenDiagnosticsButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_lastDiagnosticDirectory)
            || !Directory.Exists(_lastDiagnosticDirectory))
        {
            OpenDiagnosticsButton.Visibility = Visibility.Collapsed;
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = _lastDiagnosticDirectory,
            UseShellExecute = true,
        });
    }

    private async Task<ModelCatalogResponse> ResolveModelCatalogAsync()
    {
        try
        {
            var catalog = await _activationClient.GetModelCatalogAsync(_lifetime.Token);
            ValidateModelCatalog(catalog);
            _activeProfile = _activeProfile! with
            {
                AllowedModels = catalog.Models.Select(model => model.Id).ToArray(),
                DefaultModel = catalog.DefaultModel,
                Models = catalog.Models,
            };
            await _profileStore.WriteAsync(_activeProfile, _lifetime.Token);
            return catalog;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or ActivationClientException or JsonException)
        {
            var cached = CachedCatalog(_activeProfile!);
            ValidateModelCatalog(cached);
            return cached;
        }
    }

    private static ModelCatalogResponse CachedCatalog(ActivationProfile profile)
    {
        if (profile.Models is { Count: > 0 } && !string.IsNullOrWhiteSpace(profile.DefaultModel))
        {
            return new ModelCatalogResponse(profile.DefaultModel, profile.Models);
        }

        var migrated = profile.AllowedModels.Select(id => id switch
            {
                "company-fast" => "deepseek-v4-flash",
                "company-coder" or "company-pro" => "deepseek-v4-pro",
                _ => id,
            })
            .Distinct(StringComparer.Ordinal)
            .Select(DefaultModelEntry)
            .ToArray();
        var defaultModel = migrated.Any(model => model.Id == "deepseek-v4-pro")
            ? "deepseek-v4-pro"
            : migrated.FirstOrDefault()?.Id ?? throw new InvalidOperationException("公司策略没有可用模型。");
        return new ModelCatalogResponse(defaultModel, migrated);
    }

    private static ModelCatalogEntry DefaultModelEntry(string id) => id switch
    {
        "deepseek-v4-flash" => new(id, "DeepSeek V4 Flash", "低延迟通用模型，适合日常问答、资料整理和并行任务。", false, ["text"]),
        "deepseek-v4-pro" => new(id, "DeepSeek V4 Pro", "质量优先模型，适合编程、复杂推理和高质量交付。", false, ["text"]),
        "deepseek-v4-flash-vision-exp" => new(id, "DeepSeek V4 Flash Vision Exp", "支持文本和图片输入的实验模型。", true, ["text", "image"]),
        _ => new(id, id, "公司模型目录", false, ["text"]),
    };

    private async Task OpenHarnessWebViewAsync(Uri localUri, CancellationToken cancellationToken)
    {
        var webView = CreateFreshWebView();
        var webViewData = Path.Combine(_applicationDataRoot, "webview2");
        Directory.CreateDirectory(webViewData);
        var environment = await CoreWebView2Environment.CreateAsync(null, webViewData);
        await webView.EnsureCoreWebView2Async(environment);

        webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
        webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
        if (!_themeBridgeInstalled)
        {
            await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(ThemeBridgeScript);
            webView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
            _themeBridgeInstalled = true;
        }

        webView.CoreWebView2.NavigationStarting += CoreWebView2_NavigationStarting;

        OnboardingRoot.Visibility = Visibility.Collapsed;
        HarnessRoot.Visibility = Visibility.Visible;
        webView.Visibility = Visibility.Collapsed;
        WebViewLoadingPanel.Visibility = Visibility.Visible;

        var navigationCompletion = new TaskCompletionSource<CoreWebView2NavigationCompletedEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        void NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs args) =>
            navigationCompletion.TrySetResult(args);

        webView.CoreWebView2.NavigationCompleted += NavigationCompleted;
        try
        {
            webView.Source = localUri;
            var result = await navigationCompletion.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            if (!result.IsSuccess)
            {
                throw new InvalidOperationException(
                    $"Harness page navigation failed with status {result.WebErrorStatus}.");
            }
        }
        finally
        {
            webView.CoreWebView2.NavigationCompleted -= NavigationCompleted;
        }

        WebViewLoadingPanel.Visibility = Visibility.Collapsed;
        webView.Visibility = Visibility.Visible;
        SetShellRunState(ShellRunState.Running);
        try
        {
            await webView.CoreWebView2.ExecuteScriptAsync("window.__companyHarnessShellReportTheme?.()");
        }
        catch (InvalidOperationException)
        {
            // Navigation can be superseded during an explicit restart. The next document reports again.
        }
    }

    private WebView2 CreateFreshWebView()
    {
        DisposeHarnessWebView();
        var webView = new WebView2
        {
            Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        HarnessWebViewHost.Children.Add(webView);
        _harnessWebView = webView;
        return webView;
    }

    private async Task ResetStartupAttemptAsync(CancellationToken cancellationToken)
    {
        DisposeHarnessWebView();
        _localHarnessUri = null;
        _harnessUsesDarkTheme = null;
        ApplyResolvedShellTheme();
        HarnessRoot.Visibility = Visibility.Collapsed;
        WebViewLoadingPanel.Visibility = Visibility.Visible;
        await _processManager.StopAsync(cancellationToken);
    }

    private void DisposeHarnessWebView()
    {
        var webView = _harnessWebView;
        _harnessWebView = null;
        _themeBridgeInstalled = false;
        if (webView is null)
        {
            return;
        }

        if (webView.CoreWebView2 is not null)
        {
            webView.CoreWebView2.NavigationStarting -= CoreWebView2_NavigationStarting;
            webView.CoreWebView2.WebMessageReceived -= CoreWebView2_WebMessageReceived;
        }

        HarnessWebViewHost.Children.Remove(webView);
        webView.Dispose();
    }

    private void CoreWebView2_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (_localHarnessUri is null || !Uri.TryCreate(e.Uri, UriKind.Absolute, out var destination))
        {
            e.Cancel = true;
            return;
        }

        if (destination.Scheme == _localHarnessUri.Scheme
            && destination.Host == _localHarnessUri.Host
            && destination.Port == _localHarnessUri.Port)
        {
            return;
        }

        e.Cancel = true;
        if (destination.Scheme is "https" or "http")
        {
            Process.Start(new ProcessStartInfo(destination.AbsoluteUri) { UseShellExecute = true });
        }
    }

    private void CoreWebView2_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var message = JsonDocument.Parse(e.WebMessageAsJson);
            var root = message.RootElement;
            if (root.TryGetProperty("source", out var source)
                && source.GetString() == "company-harness-shell"
                && root.TryGetProperty("type", out var type)
                && type.GetString() == "theme"
                && root.TryGetProperty("value", out var value))
            {
                _harnessUsesDarkTheme = value.GetString() switch
                {
                    "dark" => true,
                    "light" => false,
                    _ => _harnessUsesDarkTheme,
                };

                if (_shellPreferences.ThemeMode == ShellThemeMode.FollowHarness)
                {
                    ApplyResolvedShellTheme();
                }
            }
        }
        catch (JsonException)
        {
            // Ignore messages that are not emitted by the small shell theme bridge.
        }
    }

    private void ActivationCodeTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (ActivationErrorBorder is not null && ActivationErrorBorder.Visibility == Visibility.Visible)
        {
            HideActivationError();
        }
    }

    private void ShowActivationPanel()
    {
        SetShellRunState(ShellRunState.Hidden);
        _harnessUsesDarkTheme = null;
        ApplyResolvedShellTheme();
        OnboardingRoot.Visibility = Visibility.Visible;
        HarnessRoot.Visibility = Visibility.Collapsed;
        ActivationPanel.Visibility = Visibility.Visible;
        IdentityPanel.Visibility = Visibility.Collapsed;
        StartingPanel.Visibility = Visibility.Collapsed;
    }

    private void ShowStartingPanel(string status)
    {
        SetShellRunState(ShellRunState.Starting);
        OnboardingRoot.Visibility = Visibility.Visible;
        HarnessRoot.Visibility = Visibility.Collapsed;
        ActivationPanel.Visibility = Visibility.Collapsed;
        IdentityPanel.Visibility = Visibility.Collapsed;
        StartingPanel.Visibility = Visibility.Visible;
        StartingTitleText.Text = "正在准备工作台";
        StartingStatusText.Text = status;
        StartingProgressBar.Visibility = Visibility.Visible;
        StartErrorText.Visibility = Visibility.Collapsed;
        RetryStartButton.IsEnabled = false;
        RetryStartButton.Visibility = Visibility.Collapsed;
        OpenDiagnosticsButton.Visibility = Visibility.Collapsed;
    }

    private void ShowActivationError(string message, string? supportId)
    {
        ActivationErrorText.Text = message;
        ActivationErrorBorder.Visibility = Visibility.Visible;
        if (string.IsNullOrWhiteSpace(supportId))
        {
            SupportIdText.Visibility = Visibility.Collapsed;
        }
        else
        {
            SupportIdText.Text = $"支持编号：{supportId}";
            SupportIdText.Visibility = Visibility.Visible;
        }
    }

    private void HideActivationError()
    {
        ActivationErrorBorder.Visibility = Visibility.Collapsed;
        SupportIdText.Visibility = Visibility.Collapsed;
    }

    private void SetActivationBusy(bool isBusy)
    {
        ValidateButton.IsEnabled = !isBusy;
        ValidateButton.Content = isBusy ? "正在验证…" : "验证激活码";
    }

    private void SetActiveEmployee(EmployeeProfile employee)
    {
        HeaderEmployeeText.Text = employee.DisplayName;
        MenuEmployeeNameText.Text = employee.DisplayName;
        MenuEmployeeDetailsText.Text = $"{employee.Department} · {employee.EmployeeNumber}";
        EmployeeMenuButton.Visibility = Visibility.Visible;
    }

    private void SetShellRunState(ShellRunState state)
    {
        TitleStatusPanel.Visibility = state == ShellRunState.Hidden ? Visibility.Collapsed : Visibility.Visible;
        if (state == ShellRunState.Hidden)
        {
            EmployeeMenuButton.Visibility = Visibility.Collapsed;
            EmployeeMenuPopup.IsOpen = false;
            return;
        }

        (TitleStatusText.Text, TitleStatusDot.Fill) = state switch
        {
            ShellRunState.Starting => ("启动中", new SolidColorBrush(Color.FromRgb(183, 121, 31))),
            ShellRunState.Running => ("运行中", (Brush)FindResource("SuccessBrush")),
            ShellRunState.Failed => ("启动失败", (Brush)FindResource("DangerBrush")),
            _ => (string.Empty, Brushes.Transparent),
        };
    }

    private void EmployeeMenuButton_Click(object sender, RoutedEventArgs e)
    {
        EmployeeMenuPopup.IsOpen = !EmployeeMenuPopup.IsOpen;
    }

    private void EmployeeMenuPopup_Closed(object? sender, EventArgs e)
    {
        EmployeeMenuButton.Focus();
    }

    private async void ShellThemeOption_Checked(object sender, RoutedEventArgs e)
    {
        if (_isApplyingThemeChoice || sender is not FrameworkElement { Tag: string themeName }
            || !Enum.TryParse<ShellThemeMode>(themeName, out var themeMode))
        {
            return;
        }

        _shellPreferences = new ShellPreferences(themeMode);
        ApplyResolvedShellTheme();
        try
        {
            await _shellPreferencesStore.WriteAsync(_shellPreferences, _lifetime.Token);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(
                this,
                "外观设置已应用，但无法保存到本机。下次启动时会恢复默认设置。",
                "超智能 Harness",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void UpdateThemeOptionChecks()
    {
        _isApplyingThemeChoice = true;
        try
        {
            FollowHarnessThemeOption.IsChecked = _shellPreferences.ThemeMode == ShellThemeMode.FollowHarness;
            FollowSystemThemeOption.IsChecked = _shellPreferences.ThemeMode == ShellThemeMode.FollowSystem;
            LightThemeOption.IsChecked = _shellPreferences.ThemeMode == ShellThemeMode.Light;
            DarkThemeOption.IsChecked = _shellPreferences.ThemeMode == ShellThemeMode.Dark;
        }
        finally
        {
            _isApplyingThemeChoice = false;
        }
    }

    private void ApplyResolvedShellTheme()
    {
        var useDarkTheme = _shellPreferences.ThemeMode switch
        {
            ShellThemeMode.Dark => true,
            ShellThemeMode.Light => false,
            ShellThemeMode.FollowHarness => _harnessUsesDarkTheme ?? IsWindowsDarkMode(),
            _ => IsWindowsDarkMode(),
        };
        ApplyShellTheme(useDarkTheme);
    }

    private void ApplyShellTheme(bool useDarkTheme)
    {
        if (SystemParameters.HighContrast)
        {
            SetShellBrush("ShellTitleBarBrush", SystemColors.WindowColor);
            SetShellBrush("ShellTitleTextBrush", SystemColors.WindowTextColor);
            SetShellBrush("ShellTitleSecondaryBrush", SystemColors.GrayTextColor);
            SetShellBrush("ShellTitleDividerBrush", SystemColors.WindowTextColor);
            SetShellBrush("ShellTitleHoverBrush", SystemColors.HighlightColor);
            SetShellBrush("ShellTitlePressedBrush", SystemColors.HighlightColor);
            SetShellBrush("ShellLogoTileBrush", SystemColors.WindowColor);
            SetShellBrush("ShellMenuBrush", SystemColors.WindowColor);
            SetShellBrush("ShellMenuBorderBrush", SystemColors.WindowTextColor);
            SetShellBrush("ShellMenuSelectedBrush", SystemColors.HighlightColor);
            TrySetDwmDarkMode(false);
            return;
        }

        var colors = useDarkTheme
            ? new Dictionary<string, string>
            {
                ["ShellTitleBarBrush"] = "#17191C",
                ["ShellTitleTextBrush"] = "#F2F4F7",
                ["ShellTitleSecondaryBrush"] = "#A8B0B9",
                ["ShellTitleDividerBrush"] = "#30343A",
                ["ShellTitleHoverBrush"] = "#292C31",
                ["ShellTitlePressedBrush"] = "#343840",
                ["ShellLogoTileBrush"] = "#F7F9FB",
                ["ShellMenuBrush"] = "#202328",
                ["ShellMenuBorderBrush"] = "#343840",
                ["ShellMenuSelectedBrush"] = "#3A281F",
            }
            : new Dictionary<string, string>
            {
                ["ShellTitleBarBrush"] = "#F7F9FB",
                ["ShellTitleTextBrush"] = "#17202A",
                ["ShellTitleSecondaryBrush"] = "#465463",
                ["ShellTitleDividerBrush"] = "#DCE3EA",
                ["ShellTitleHoverBrush"] = "#E9EEF3",
                ["ShellTitlePressedBrush"] = "#DCE3EA",
                ["ShellLogoTileBrush"] = "#00FFFFFF",
                ["ShellMenuBrush"] = "#FFFFFF",
                ["ShellMenuBorderBrush"] = "#DCE3EA",
                ["ShellMenuSelectedBrush"] = "#FFF4EC",
            };

        foreach (var (key, value) in colors)
        {
            SetShellBrush(key, (Color)ColorConverter.ConvertFromString(value));
        }

        TrySetDwmDarkMode(useDarkTheme);
    }

    private static void SetShellBrush(string resourceKey, Color color)
    {
        Application.Current.Resources[resourceKey] = new SolidColorBrush(color);
    }

    private static bool IsWindowsDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        _windowSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        _windowSource?.AddHook(WindowMessageHook);
        UpdateMaximizeButton();
        ApplyResolvedShellTheme();
    }

    private IntPtr WindowMessageHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmNcHitTest && MaximizeWindowButton.IsVisible)
        {
            var packedPoint = lParam.ToInt64();
            var screenPoint = new Point(
                unchecked((short)(packedPoint & 0xFFFF)),
                unchecked((short)((packedPoint >> 16) & 0xFFFF)));
            var buttonTopLeft = MaximizeWindowButton.PointToScreen(new Point(0, 0));
            var buttonBottomRight = MaximizeWindowButton.PointToScreen(
                new Point(MaximizeWindowButton.ActualWidth, MaximizeWindowButton.ActualHeight));
            var buttonBounds = new Rect(buttonTopLeft, buttonBottomRight);
            if (buttonBounds.Contains(screenPoint))
            {
                handled = true;
                return new IntPtr(HtMaxButton);
            }
        }
        else if (message is WmSettingChange or WmThemeChanged or WmDwmColorizationColorChanged)
        {
            if (_shellPreferences.ThemeMode == ShellThemeMode.FollowSystem
                || (_shellPreferences.ThemeMode == ShellThemeMode.FollowHarness && _harnessUsesDarkTheme is null))
            {
                Dispatcher.BeginInvoke(new Action(ApplyResolvedShellTheme));
            }
        }

        return IntPtr.Zero;
    }

    private void MinimizeWindowButton_Click(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);

    private void MaximizeWindowButton_Click(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            SystemCommands.RestoreWindow(this);
        }
        else
        {
            SystemCommands.MaximizeWindow(this);
        }
    }

    private void CloseWindowButton_Click(object sender, RoutedEventArgs e) => SystemCommands.CloseWindow(this);

    private void ExitApplication_Click(object sender, RoutedEventArgs e)
    {
        EmployeeMenuPopup.IsOpen = false;
        Close();
    }

    private void Window_StateChanged(object? sender, EventArgs e) => UpdateMaximizeButton();

    private void UpdateMaximizeButton()
    {
        if (MaximizeWindowButton is null)
        {
            return;
        }

        var isMaximized = WindowState == WindowState.Maximized;
        MaximizeWindowIcon.Visibility = isMaximized ? Visibility.Collapsed : Visibility.Visible;
        RestoreWindowIcon.Visibility = isMaximized ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetName(MaximizeWindowButton, isMaximized ? "还原" : "最大化");
    }

    private void TitleBarRoot_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        SystemCommands.ShowSystemMenu(this, PointToScreen(e.GetPosition(this)));
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) && e.SystemKey == Key.Space)
        {
            SystemCommands.ShowSystemMenu(this, PointToScreen(new Point(0, TitleBarRoot.ActualHeight)));
            e.Handled = true;
        }
    }

    private void TrySetDwmDarkMode(bool useDarkTheme)
    {
        if (!OperatingSystem.IsWindows() || _windowSource is null)
        {
            return;
        }

        var enabled = useDarkTheme ? 1 : 0;
        _ = DwmSetWindowAttribute(
            new WindowInteropHelper(this).Handle,
            DwmwaUseImmersiveDarkMode,
            ref enabled,
            Marshal.SizeOf<int>());
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr windowHandle, int attribute, ref int attributeValue, int attributeSize);

    private enum ShellRunState
    {
        Hidden,
        Starting,
        Running,
        Failed,
    }

    private static void ValidateCompanyPolicy(ActivationConfirmResponse response)
    {
        if (response.Policy.PersonalProvidersAllowed || response.Policy.TelemetryEnabled)
        {
            throw new InvalidOperationException("服务器返回了不符合公司要求的客户端策略。");
        }

        ValidateModelCatalog(new ModelCatalogResponse(response.Policy.DefaultModel, response.Policy.Models));
        if (!response.Policy.AllowedModels.SequenceEqual(response.Policy.Models.Select(model => model.Id), StringComparer.Ordinal))
            throw new InvalidOperationException("服务器返回了不一致的模型策略。");
    }

    private static void ValidateModelCatalog(ModelCatalogResponse catalog)
    {
        if (catalog.Models is null || catalog.Models.Count == 0
            || string.IsNullOrWhiteSpace(catalog.DefaultModel)
            || !catalog.Models.Any(model => model.Id == catalog.DefaultModel)
            || catalog.Models.Select(model => model.Id).Distinct(StringComparer.Ordinal).Count() != catalog.Models.Count)
            throw new InvalidOperationException("服务器返回了无效的模型目录。");
    }

    private static Uri ResolveControlPlaneBaseUrl()
    {
        var configured = Environment.GetEnvironmentVariable("COMPANY_HARNESS_CONTROL_PLANE_URL");
        var uri = new Uri(
            string.IsNullOrWhiteSpace(configured) ? "http://127.0.0.1:8765/" : configured,
            UriKind.Absolute);
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("激活服务必须使用 HTTP 或 HTTPS。");
        }

        return uri;
    }

    private static HttpClient CreateControlPlaneHttpClient(Uri baseAddress)
    {
        return new HttpClient
        {
            BaseAddress = baseAddress,
            Timeout = TimeSpan.FromSeconds(15),
        };
    }

    private static string ResolveApplicationDataRoot()
    {
        var configured = Environment.GetEnvironmentVariable("COMPANY_HARNESS_DATA_ROOT");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (!Path.IsPathFullyQualified(configured))
            {
                throw new InvalidOperationException("COMPANY_HARNESS_DATA_ROOT 必须是绝对路径。");
            }

            return Path.GetFullPath(configured);
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SmartWheelchair",
            "CompanyHarness");
    }

    private static string ResolveCredentialTarget()
    {
        var configured = Environment.GetEnvironmentVariable("COMPANY_HARNESS_CREDENTIAL_TARGET")?.Trim();
        if (string.IsNullOrWhiteSpace(configured))
        {
            return DefaultCredentialTarget;
        }

        if (configured.Length > 256 || configured.Any(char.IsControl))
        {
            throw new InvalidOperationException("COMPANY_HARNESS_CREDENTIAL_TARGET 格式无效。");
        }

        return configured;
    }

    private static string GetSafeErrorMessage(Exception exception) => exception switch
    {
        DirectoryNotFoundException => exception.Message,
        FileNotFoundException => exception.Message,
        HarnessStartupException harnessException =>
            $"本地 Harness 进程启动后异常退出（代码 {harnessException.ExitCode}）。请将诊断编号发给管理员。",
        TimeoutException => "Harness 启动超时，请重试。如问题持续，请联系管理员。",
        WebView2RuntimeNotFoundException => "未检测到 Microsoft Edge WebView2 Runtime，请安装后重试。",
        _ => "无法启动本地 Harness。请重试，如问题持续，请联系管理员。",
    };

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_isClosing)
        {
            return;
        }

        _isClosing = true;
        _lifetime.Cancel();
        _processManager.StopImmediately();

        // The last-window process exit owns final WebView2 cleanup. Explicit Dispose can block
        // indefinitely on some Windows builds and must not hold the native close path open.
        _harnessWebView = null;
        _themeBridgeInstalled = false;
        _windowSource?.RemoveHook(WindowMessageHook);
        _windowSource = null;
    }
}
