using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using CompanyHarness.Desktop.Core.Runtime;
using CompanyHarness.Desktop.Core.Storage;

namespace CompanyHarness.Desktop;

public partial class App : Application
{
    private const string DefaultCredentialTarget = "SmartWheelchair.CompanyHarness.VirtualKey";
    private const uint AllowSetForegroundWindowAny = uint.MaxValue;

    private SingleInstanceCoordinator? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += HandleDispatcherUnhandledException;
        base.OnStartup(e);

        var launchContext = StartupLaunchContext.Parse(e.Args);
        WaitForPreviousApplicationInstance(launchContext);
        _singleInstance = new SingleInstanceCoordinator(ResolveSingleInstanceName());
        if (e.Args.Contains("--clear-user-data", StringComparer.OrdinalIgnoreCase))
        {
            RunUserDataCleanup();
            return;
        }

        if (!_singleInstance.IsPrimaryInstance)
        {
            _ = AllowSetForegroundWindow(AllowSetForegroundWindowAny);
            _singleInstance.SignalPrimaryInstance();
            Shutdown(0);
            return;
        }

        var mainWindow = new MainWindow(launchContext);
        MainWindow = mainWindow;
        _singleInstance.StartListening(() =>
            Dispatcher.BeginInvoke(
                DispatcherPriority.Normal,
                new Action(BringMainWindowToFront)));
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        _singleInstance = null;
        base.OnExit(e);
    }

    private void BringMainWindowToFront()
    {
        if (MainWindow is not { } window || !window.IsLoaded)
        {
            return;
        }

        if (!window.IsVisible)
        {
            window.Show();
        }

        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();
        var handle = new WindowInteropHelper(window).Handle;
        if (handle != IntPtr.Zero)
        {
            _ = SetForegroundWindow(handle);
        }
    }

    private void RunUserDataCleanup()
    {
        if (_singleInstance is not { IsPrimaryInstance: true })
        {
            MessageBox.Show(
                "超智能 Harness 仍在运行。请先关闭应用，再重新执行卸载。",
                "超智能 Harness",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            Shutdown(2);
            return;
        }

        try
        {
            var expectedParent = Path.GetFullPath(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SmartWheelchair"));
            var dataRoot = Path.GetFullPath(Path.Combine(expectedParent, "CompanyHarness"));
            new UserDataCleaner(
                new WindowsCredentialStore(),
                DefaultCredentialTarget,
                dataRoot,
                expectedParent).Clear();

            Shutdown(0);
        }
        catch (Exception)
        {
            MessageBox.Show(
                "无法完整清除本地数据。请联系管理员处理剩余文件。",
                "超智能 Harness",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(3);
        }
    }

    private static string ResolveSingleInstanceName()
    {
        var userIdentity = WindowsIdentity.GetCurrent().User?.Value;
        if (string.IsNullOrWhiteSpace(userIdentity))
        {
            userIdentity = Environment.UserName.Replace('\\', '_').Replace('/', '_');
        }

        return $"Local\\SmartWheelchair.CompanyHarness.{userIdentity}";
    }

    private static void WaitForPreviousApplicationInstance(StartupLaunchContext launchContext)
    {
        if (launchContext.RestartAfterProcessId is not { } processId
            || processId == Environment.ProcessId)
        {
            return;
        }

        try
        {
            using var previousInstance = Process.GetProcessById(processId);
            previousInstance.WaitForExit(milliseconds: 30_000);
        }
        catch (ArgumentException)
        {
            // The previous instance already exited.
        }
        catch (InvalidOperationException)
        {
            // The previous instance already exited.
        }
    }

    private void HandleDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs args)
    {
        MessageBox.Show(
            "应用遇到未处理错误，请重新启动。如问题持续，请联系管理员。",
            "超智能 Harness",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        args.Handled = true;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr windowHandle);
}
