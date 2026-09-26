using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using MeetingAssistant.Services;
using MeetingAssistant.ViewModels;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfOrientation = System.Windows.Controls.Orientation;

namespace MeetingAssistant;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel, IGlobalHotkeyService hotkeyService, ITrayService trayService)
    {
        InitializeComponent();
        ViewModel = viewModel;
        _hotkeyService = hotkeyService;
        _trayService = trayService;
        DataContext = ViewModel;
        ViewModel.UsePrompt(new WindowUserPrompt());
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
    }

    public MainViewModel ViewModel { get; }

    private readonly IGlobalHotkeyService _hotkeyService;
    private readonly ITrayService _trayService;

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyResponsiveLayout(ActualWidth, ActualHeight);
        SmoothScroll.Attach(ContentScrollViewer);
        SmoothScroll.Attach(AuthScrollViewer);
        await ViewModel.InitializeAsync();
        LocalizationService.Refresh();
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        MaximizedWindowPlacement.Attach(this);
        _hotkeyService.Attach(this);
        _trayService.Initialize(ShowWindow, ViewModel.HandleGlobalHotkey);
        ApplyNativeChrome();
        ViewModel.RefreshSystemStatus();
    }

    private void ApplyNativeChrome()
        => NativeWindowChrome.Apply(this, ThemeService.IsDark);

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        KeepContentInsideWorkArea();
        ApplyResponsiveLayout(e.NewSize.Width, e.NewSize.Height);
    }

    private void Window_StateChanged(object sender, EventArgs e) => KeepContentInsideWorkArea();

    private void KeepContentInsideWorkArea()
    {
        var inset = new Thickness(0);
        if (WindowState == WindowState.Maximized)
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (MaximizedWindowPlacement.TryMeasureOverflow(handle, out var overflow))
            {
                var dpi = VisualTreeHelper.GetDpi(this);
                var scaleX = dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1;
                var scaleY = dpi.DpiScaleY > 0 ? dpi.DpiScaleY : 1;
                inset = new Thickness(
                    overflow.Left / scaleX,
                    overflow.Top / scaleY,
                    overflow.Right / scaleX,
                    overflow.Bottom / scaleY);
            }
        }

        if (RootGrid.Margin != inset)
            RootGrid.Margin = inset;
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedTheme))
        {
            ApplyNativeChrome();
            return;
        }

        if (e.PropertyName is not (nameof(MainViewModel.CurrentView) or nameof(MainViewModel.IsAuthenticated)))
            return;

        Dispatcher.BeginInvoke(new Action(() =>
        {
            ContentScrollViewer.ScrollToTop();
            AuthScrollViewer.ScrollToTop();
            LocalizationService.Refresh();
        }), DispatcherPriority.Loaded);
    }

    private void ApplyResponsiveLayout(double width, double height)
    {
        if (width <= 0 || height <= 0)
            return;

        var spacious = width >= 1600;
        var compact = width < 1080;
        var narrow = width < 920;
        var veryNarrow = width < 820;
        var authSingleColumn = width < 980;
        var shortWindow = height < 640;

        ApplyAuthLayout(authSingleColumn, compact);
        ApplySidebarLayout(compact, spacious);
        RootGrid.LayoutTransform = spacious
            ? new ScaleTransform(width >= 1900 ? 1.08 : 1.04, width >= 1900 ? 1.08 : 1.04)
            : Transform.Identity;
        ApplyHeaderLayout(compact, narrow, veryNarrow);
        StatusBar.Visibility = shortWindow ? Visibility.Collapsed : Visibility.Visible;
        StatusBarRow.Height = new GridLength(shortWindow ? 0 : 32);

        ContentFrame.Margin = compact
            ? narrow ? new Thickness(16, 16, 16, 24) : new Thickness(20, 18, 20, 28)
            : new Thickness(32, 24, 32, 32);
        ToastBorder.Margin = compact
            ? new Thickness(0, 0, 16, 16)
            : new Thickness(0, 0, 32, 24);

        HeroArtwork.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
        DashboardHero.Padding = narrow ? new Thickness(18) : compact ? new Thickness(22) : new Thickness(26);
        DashboardWelcome.Margin = narrow ? new Thickness(0, 0, 0, 18) : new Thickness(0, 0, 0, 25);
        LastSyncBadge.Visibility = veryNarrow ? Visibility.Collapsed : Visibility.Visible;
        ApplyDashboardStatsLayout(narrow);
        ApplySetupLayout(narrow);
        ApplyRecordingLayout(narrow, veryNarrow);
        ApplyDetailLayout(narrow);
        ApplySpeakersLayout(narrow);
    }

    private void ApplyAuthLayout(bool singleColumn, bool compact)
    {
        AuthLayoutGrid.ColumnDefinitions.Clear();

        if (singleColumn)
        {
            AuthLayoutGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            AuthLayoutGrid.Margin = compact
                ? new Thickness(24, 24, 24, 30)
                : new Thickness(32, 34, 32, 40);
            AuthHero.Visibility = Visibility.Collapsed;
            Grid.SetColumn(AuthCard, 0);
            AuthCard.HorizontalAlignment = WpfHorizontalAlignment.Center;
            AuthCard.MaxWidth = 520;
            AuthCard.Padding = new Thickness(26);
        }
        else
        {
            AuthLayoutGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.05, GridUnitType.Star) });
            AuthLayoutGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.95, GridUnitType.Star) });
            AuthLayoutGrid.Margin = new Thickness(96, 58, 96, 74);
            AuthHero.Visibility = Visibility.Visible;
            Grid.SetColumn(AuthHero, 0);
            Grid.SetColumn(AuthCard, 1);
            AuthCard.HorizontalAlignment = WpfHorizontalAlignment.Right;
            AuthCard.MaxWidth = 480;
            AuthCard.Padding = new Thickness(38);
        }
    }

    private void ApplySidebarLayout(bool compact, bool spacious)
    {
        SidebarColumn.Width = new GridLength(compact ? 84 : spacious ? 280 : 248);
        SidebarLayoutGrid.Margin = compact
            ? new Thickness(8, 16, 8, 14)
            : new Thickness(16, 20, 16, 18);
        SidebarLayoutGrid.RowDefinitions[1].Height = new GridLength(compact ? 12 : 27);
        SidebarBrand.Margin = compact ? new Thickness(0) : new Thickness(6, 0, 0, 0);
        SidebarBrand.HorizontalAlignment = compact ? WpfHorizontalAlignment.Center : WpfHorizontalAlignment.Left;

        var textVisibility = compact ? Visibility.Collapsed : Visibility.Visible;
        SidebarBrandCopy.Visibility = textVisibility;
        WorkspaceLabel.Visibility = textVisibility;
        MeetingsNavLabel.Visibility = textVisibility;
        MeetingsNavBadge.Visibility = textVisibility;
        SpeakersNavLabel.Visibility = textVisibility;
        ActionsNavLabel.Visibility = textVisibility;
        SettingsNavLabel.Visibility = textVisibility;
        QuickStartLabel.Visibility = textVisibility;
        QuickStartButtonLabel.Visibility = textVisibility;
        SidebarStatusCard.Visibility = textVisibility;
        AccountCopy.Visibility = textVisibility;

        var contentAlignment = compact ? WpfHorizontalAlignment.Center : WpfHorizontalAlignment.Left;
        MeetingsNavButton.HorizontalContentAlignment = contentAlignment;
        SpeakersNavButton.HorizontalContentAlignment = contentAlignment;
        ActionsNavButton.HorizontalContentAlignment = contentAlignment;
        SettingsNavButton.HorizontalContentAlignment = contentAlignment;
        QuickStartButton.HorizontalContentAlignment = contentAlignment;
        AccountButton.HorizontalContentAlignment = contentAlignment;

        var navPadding = compact ? new Thickness(8, 10, 8, 10) : new Thickness(13, 11, 13, 11);
        MeetingsNavButton.Padding = navPadding;
        SpeakersNavButton.Padding = navPadding;
        ActionsNavButton.Padding = navPadding;
        SettingsNavButton.Padding = navPadding;
        QuickStartButton.Padding = navPadding;
        AccountButton.Padding = compact ? new Thickness(0, 8, 0, 8) : new Thickness(7, 8, 7, 8);
    }

    private void ApplyHeaderLayout(bool compact, bool narrow, bool veryNarrow)
    {
        AppHeaderRow.Height = new GridLength(compact ? narrow ? 56 : 64 : 72);
        HeaderBorder.Padding = new Thickness(compact ? 16 : 32, 0, compact ? 16 : 32, 0);
        HeaderDescription.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        HeaderPrivateLabel.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        HeaderDetails.Margin = new Thickness(0, 0, compact ? 14 : 24, 0);

        SearchBorder.Width = compact ? narrow ? veryNarrow ? 150 : 170 : 190 : 220;
        RecordHeaderLabel.Visibility = veryNarrow ? Visibility.Collapsed : Visibility.Visible;
        RecordHeaderButton.Width = veryNarrow ? 40 : double.NaN;
        RecordHeaderButton.Padding = veryNarrow
            ? new Thickness(0)
            : compact ? new Thickness(12, 8, 12, 8) : new Thickness(15, 8, 15, 8);
    }

    private void ApplyDashboardStatsLayout(bool narrow)
    {
        DashboardStatsGrid.ColumnDefinitions.Clear();
        DashboardStatsGrid.RowDefinitions.Clear();

        if (narrow)
        {
            for (var i = 0; i < 3; i++)
                DashboardStatsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Grid.SetColumn(MeetingsStatCard, 0);
            Grid.SetColumn(TodayStatCard, 0);
            Grid.SetColumn(ActionStatCard, 0);
            Grid.SetRow(MeetingsStatCard, 0);
            Grid.SetRow(TodayStatCard, 1);
            Grid.SetRow(ActionStatCard, 2);
            MeetingsStatCard.Margin = new Thickness(0, 0, 0, 8);
            TodayStatCard.Margin = new Thickness(0, 0, 0, 8);
            ActionStatCard.Margin = new Thickness(0);
        }
        else
        {
            DashboardStatsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (var i = 0; i < 3; i++)
                DashboardStatsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Grid.SetRow(MeetingsStatCard, 0);
            Grid.SetRow(TodayStatCard, 0);
            Grid.SetRow(ActionStatCard, 0);
            Grid.SetColumn(MeetingsStatCard, 0);
            Grid.SetColumn(TodayStatCard, 1);
            Grid.SetColumn(ActionStatCard, 2);
            MeetingsStatCard.Margin = new Thickness(0, 0, 8, 0);
            TodayStatCard.Margin = new Thickness(8, 0, 8, 0);
            ActionStatCard.Margin = new Thickness(8, 0, 0, 0);
        }
    }

    private void ApplySetupLayout(bool narrow)
    {
        if (narrow)
        {
            SetupLayoutGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            SetupLayoutGrid.ColumnDefinitions[1].Width = new GridLength(0);
            SetupLayoutGrid.RowDefinitions[1].Height = GridLength.Auto;
            SetupLayoutGrid.RowDefinitions[2].Height = GridLength.Auto;
            Grid.SetRow(SetupLeftColumn, 1);
            Grid.SetColumn(SetupLeftColumn, 0);
            Grid.SetRow(SetupRightColumn, 2);
            Grid.SetColumn(SetupRightColumn, 0);
            SetupLeftColumn.Margin = new Thickness(0, 0, 0, 14);
            SetupRightColumn.Margin = new Thickness(0);
        }
        else
        {
            SetupLayoutGrid.ColumnDefinitions[0].Width = new GridLength(1.1, GridUnitType.Star);
            SetupLayoutGrid.ColumnDefinitions[1].Width = new GridLength(0.9, GridUnitType.Star);
            SetupLayoutGrid.RowDefinitions[1].Height = GridLength.Auto;
            SetupLayoutGrid.RowDefinitions[2].Height = new GridLength(0);
            Grid.SetRow(SetupLeftColumn, 1);
            Grid.SetColumn(SetupLeftColumn, 0);
            Grid.SetRow(SetupRightColumn, 1);
            Grid.SetColumn(SetupRightColumn, 1);
            SetupLeftColumn.Margin = new Thickness(0, 0, 12, 0);
            SetupRightColumn.Margin = new Thickness(12, 0, 0, 0);
        }
    }

    private void ApplyRecordingLayout(bool narrow, bool veryNarrow)
    {
        if (narrow)
        {
            RecordingAudioGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            RecordingAudioGrid.ColumnDefinitions[1].Width = new GridLength(0);
            RecordingAudioGrid.RowDefinitions[0].Height = GridLength.Auto;
            RecordingAudioGrid.RowDefinitions[1].Height = GridLength.Auto;
            Grid.SetRow(RecordingMicColumn, 0);
            Grid.SetColumn(RecordingMicColumn, 0);
            Grid.SetRow(RecordingSystemColumn, 1);
            Grid.SetColumn(RecordingSystemColumn, 0);
            RecordingMicColumn.Margin = new Thickness(0, 0, 0, 16);
            RecordingSystemColumn.Margin = new Thickness(0);
        }
        else
        {
            RecordingAudioGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            RecordingAudioGrid.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
            RecordingAudioGrid.RowDefinitions[0].Height = GridLength.Auto;
            RecordingAudioGrid.RowDefinitions[1].Height = new GridLength(0);
            Grid.SetRow(RecordingMicColumn, 0);
            Grid.SetColumn(RecordingMicColumn, 0);
            Grid.SetRow(RecordingSystemColumn, 0);
            Grid.SetColumn(RecordingSystemColumn, 1);
            RecordingMicColumn.Margin = new Thickness(0, 0, 20, 0);
            RecordingSystemColumn.Margin = new Thickness(20, 0, 0, 0);
        }

        ActiveSpeakerHint.MaxWidth = veryNarrow ? 200 : 260;
        if (veryNarrow)
        {
            RecordingActions.Orientation = WpfOrientation.Vertical;
            PauseRecordingButton.Width = 220;
            ResumeRecordingButton.Width = 220;
            StopRecordingButton.Width = 220;
            PauseRecordingButton.Margin = new Thickness(0, 0, 0, 8);
            ResumeRecordingButton.Margin = new Thickness(0, 0, 0, 8);
            StopRecordingButton.Margin = new Thickness(0);
        }
        else
        {
            RecordingActions.Orientation = WpfOrientation.Horizontal;
            PauseRecordingButton.Width = 132;
            ResumeRecordingButton.Width = 132;
            StopRecordingButton.Width = 132;
            PauseRecordingButton.Margin = new Thickness(0, 0, 8, 0);
            ResumeRecordingButton.Margin = new Thickness(8, 0, 8, 0);
            StopRecordingButton.Margin = new Thickness(8, 0, 0, 0);
        }
    }

    private void ApplyDetailLayout(bool narrow)
    {
        if (narrow)
        {
            DetailColumns.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            DetailColumns.ColumnDefinitions[1].Width = new GridLength(0);
            DetailColumns.RowDefinitions[0].Height = GridLength.Auto;
            DetailColumns.RowDefinitions[1].Height = GridLength.Auto;
            Grid.SetRow(TranscriptColumn, 0);
            Grid.SetColumn(TranscriptColumn, 0);
            Grid.SetRow(SummaryColumn, 1);
            Grid.SetColumn(SummaryColumn, 0);
            TranscriptColumn.Margin = new Thickness(0);
            SummaryColumn.Margin = new Thickness(0, 20, 0, 0);
        }
        else
        {
            DetailColumns.ColumnDefinitions[0].Width = new GridLength(1.08, GridUnitType.Star);
            DetailColumns.ColumnDefinitions[1].Width = new GridLength(0.92, GridUnitType.Star);
            DetailColumns.RowDefinitions[0].Height = GridLength.Auto;
            DetailColumns.RowDefinitions[1].Height = new GridLength(0);
            Grid.SetRow(TranscriptColumn, 0);
            Grid.SetColumn(TranscriptColumn, 0);
            Grid.SetRow(SummaryColumn, 0);
            Grid.SetColumn(SummaryColumn, 1);
            TranscriptColumn.Margin = new Thickness(0, 0, 10, 0);
            SummaryColumn.Margin = new Thickness(10, 0, 0, 0);
        }
    }

    private void ApplySpeakersLayout(bool narrow)
    {
        if (narrow)
        {
            SpeakersHeaderGrid.RowDefinitions[0].Height = GridLength.Auto;
            SpeakersHeaderGrid.RowDefinitions[1].Height = GridLength.Auto;
            Grid.SetRow(SpeakersSaveButton, 1);
            SpeakersSaveButton.HorizontalAlignment = WpfHorizontalAlignment.Left;
            SpeakersSaveButton.Margin = new Thickness(0, 12, 0, 0);
        }
        else
        {
            SpeakersHeaderGrid.RowDefinitions[0].Height = GridLength.Auto;
            SpeakersHeaderGrid.RowDefinitions[1].Height = new GridLength(0);
            Grid.SetRow(SpeakersSaveButton, 0);
            SpeakersSaveButton.HorizontalAlignment = WpfHorizontalAlignment.Right;
            SpeakersSaveButton.Margin = new Thickness(0);
        }
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (ViewModel.MinimizeToTrayOnClose && !ViewModel.IsConfirmingClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        if (ViewModel.IsRecording || ViewModel.IsProcessing)
        {
            if (!ViewModel.ConfirmExit())
            {
                e.Cancel = true;
                return;
            }

            if (ViewModel.IsRecording)
            {
                e.Cancel = true;
                await ViewModel.HandleExitAsync();
                Close();
                return;
            }
        }

        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        ViewModel.Dispose();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
            return;

        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }
        else
        {
            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
                // The OS can cancel a drag while the window is restoring or closing.
            }
        }

        e.Handled = true;
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel && sender is System.Windows.Controls.PasswordBox passwordBox)
            viewModel.Password = passwordBox.Password;
    }

    private void OpenAiApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel && sender is System.Windows.Controls.PasswordBox passwordBox)
            viewModel.OpenAiApiKeyInput = passwordBox.Password;
    }

    private void AssemblyAiApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel && sender is System.Windows.Controls.PasswordBox passwordBox)
            viewModel.AssemblyAiApiKeyInput = passwordBox.Password;
    }

    private void TranscriptTimestamp_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MeetingAssistant.Models.TranscriptSegment segment })
            ViewModel.SeekTo(segment);
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (ViewModel.CurrentView != WorkspaceView.Detail) return;
        if (e.OriginalSource is System.Windows.Controls.TextBox or System.Windows.Controls.PasswordBox)
            return;

        if (e.Key == Key.Space)
        {
            ViewModel.HandlePlaybackKeys("Space");
            e.Handled = true;
        }
        else if (e.Key == Key.J)
        {
            ViewModel.HandlePlaybackKeys("J");
            e.Handled = true;
        }
        else if (e.Key == Key.K)
        {
            ViewModel.HandlePlaybackKeys("K");
            e.Handled = true;
        }
    }

    private void ShowWindow()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Show();
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }
}

public sealed class WindowUserPrompt : IUserPrompt
{
    public bool Confirm(string title, string message)
        => System.Windows.MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public string? SaveFile(string title, string filter, string defaultName)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = title,
            Filter = filter,
            FileName = defaultName
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public void CopyText(string text)
        => System.Windows.Clipboard.SetText(text ?? string.Empty);
}
