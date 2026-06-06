using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;

namespace CargoFit;

public partial class MainWindow : Window
{
    private static readonly IBrush NavActiveBg = new SolidColorBrush(Color.Parse("#2563EB"));
    private static readonly IBrush NavActiveFg = Brushes.White;
    private static readonly IBrush NavIdleBg   = Brushes.Transparent;
    private static readonly IBrush NavIdleFg   = new SolidColorBrush(Color.Parse("#1E293B"));

    private readonly SettingsWindow _settingsWindow = new();
    private readonly PlanningView   _planningView   = new();
    private InterlockDesignerView?  _interlockView;

    public MainWindow()
    {
        InitializeComponent();
        ShowPlanning();

        LicenseManager.Verified += _ => Dispatcher.UIThread.Post(UpdateTrialBanner);
        UpdateTrialBanner();

        // เช็คอัปเดตใน background หลังแอปโหลดเสร็จ
        _ = CheckForUpdateAsync();
    }

    private void NavPlanning_Click(object? sender, RoutedEventArgs e)  => ShowPlanning();
    private void NavInterlock_Click(object? sender, RoutedEventArgs e) => ShowInterlock();

    private void ShowPlanning()
    {
        MainContent.Content = _planningView;
        SetActiveNav(planning: true);
    }

    private void ShowInterlock()
    {
        _interlockView ??= new InterlockDesignerView();
        MainContent.Content = _interlockView;
        SetActiveNav(planning: false);
    }

    private void SetActiveNav(bool planning)
    {
        NavPlanning.Background  = planning ? NavActiveBg : NavIdleBg;
        NavPlanning.Foreground  = planning ? NavActiveFg : NavIdleFg;
        NavInterlock.Background = planning ? NavIdleBg : NavActiveBg;
        NavInterlock.Foreground = planning ? NavIdleFg : NavActiveFg;
    }

    private async System.Threading.Tasks.Task CheckForUpdateAsync()
    {
        await UpdateService.CheckAsync();
        if (UpdateService.PendingUpdate is { } update)
        {
            UpdateBannerText.Text = $"🆕 มีเวอร์ชันใหม่ {update.TargetFullRelease.Version} พร้อมแล้ว";
            UpdateBanner.IsVisible = true;
        }
    }

    private async void UpdateButton_Click(object? sender, RoutedEventArgs e)
    {
        UpdateButton.IsEnabled = false;
        UpdateBannerText.Text = "⏳ กำลังดาวน์โหลด กรุณารอสักครู่...";
        await UpdateService.ApplyUpdateAsync();
        // ApplyUpdatesAndRestart() ไม่ return — แอป restart ทันทีเมื่อสำเร็จ
    }

    private void UpdateTrialBanner()
    {
        var days = LicenseManager.DaysRemaining;
        if (days is null || days > 3)
        {
            TrialBanner.IsVisible = false;
            return;
        }

        TrialBannerText.Text = days == 0
            ? "⚠ เวอร์ชันทดลองหมดอายุวันนี้ — ติดต่อปิงเพื่อต่ออายุ"
            : $"⚠ เวอร์ชันทดลองเหลืออีก {days} วัน — ติดต่อปิงเพื่อต่ออายุ";
        TrialBanner.IsVisible = true;
    }

    private void OpenSettings_Click(object? sender, RoutedEventArgs e)
    {
        var win = new Window
        {
            Title = "Settings",
            Width = 860,
            Height = 620,
            Content = _settingsWindow
        };
        // Detach on close so the same UserControl can be re-parented next time.
        win.Closed += (_, _) => win.Content = null;
        win.ShowDialog(this);
    }
}
