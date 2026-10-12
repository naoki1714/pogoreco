using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Pogoreco.App.Charts;
using Pogoreco.App.Pages;

namespace Pogoreco.App;

/// <summary>画面の枠: 左のメニュー、上の状態表示、ページ。記録中は 10 秒ごとに読み直す。</summary>
public partial class MainWindow : Window
{
    private readonly AppData _data = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(10) };
    private readonly List<(string Title, Func<Navigator, UIElement> Build)> _menu =
    [
        ("ダッシュボード", n => DashboardPage.Build(n)),
        ("ラン", n => RunsPage.Build(n)),
        ("セッション", n => SessionsPage.Build(n)),
        ("マップ", n => MapsPage.Build(n)),
    ];
    private readonly Navigator _nav;
    private string? _message;
    private string? _newVersion;

    public MainWindow()
    {
        InitializeComponent();
        AppTitle.FontFamily = Theme.Display;
        var version = typeof(MainWindow).Assembly.GetName().Version;
        if (version is not null) Subtitle.Text += $"　v{version.Major}.{version.Minor}.{version.Build}";
        _nav = new Navigator(_data, Show);
        foreach (var (title, build) in _menu)
        {
            var b = new Button { Content = title, Style = (Style)FindResource("NavButton") };
            b.Click += (_, _) => _nav.Go(title, build);
            Nav.Children.Add(b);
        }
        Loaded += (_, _) =>
        {
            Reload();
            _nav.Go(_menu[0].Title, _menu[0].Build);
            _ = CheckUpdateAsync();
        };
        _timer.Tick += (_, _) =>
        {
            // 記録中・ゲーム中だけ読み直す（そうでなければデータは変わらない）
            if (AppData.IsRecorderRunning() || _data.GameRunning) { Reload(); _nav.Refresh(); }
        };
        _timer.Start();
    }

    private async Task CheckUpdateAsync()
    {
        _newVersion = await Updater.CheckAsync();
        if (_newVersion is not null) RenderStatus();
    }

    private async void ApplyUpdate()
    {
        if (AppData.IsRecorderRunning() || _data.GameRunning)
        {
            _message = "ゲームを終えて記録係が止まってから更新してください（記録が途切れないように）。";
            RenderStatus();
            return;
        }
        _message = "更新をダウンロードしています。終わると自動で再起動します…";
        RenderStatus();
        try { await Updater.ApplyAsync(); }
        catch (Exception e) { _message = $"更新に失敗しました: {e.Message}"; RenderStatus(); }
    }

    private void Reload()
    {
        try { _data.Refresh(); }
        catch (Exception e) { _message = $"読み込みに失敗しました: {e.Message}"; }
        RenderStatus();
    }

    private void Show(string title, UIElement page, bool keepScroll)
    {
        foreach (Button b in Nav.Children)
        {
            var active = (string)b.Content == title;
            b.Background = active ? (System.Windows.Media.Brush)FindResource("NavActiveBrush") : System.Windows.Media.Brushes.Transparent;
            b.Foreground = active ? Theme.TextPrimary : Theme.TextSecondary;
            b.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
        }
        var offset = Scroller.VerticalOffset;
        PageHost.Content = page;
        if (keepScroll) Dispatcher.BeginInvoke(() => Scroller.ScrollToVerticalOffset(offset), DispatcherPriority.Loaded);
        else Scroller.ScrollToTop();
    }

    private void RenderStatus()
    {
        StatusArea.Children.Clear();
        var status = _data.RecorderRunning
            ? Ui.Badge("記録中 — ゲームを閉じると記録係も終わります", Theme.Good)
            : _data.GameRunning
                ? Ui.Badge("Pogostuck は動いていますが、記録係は動いていません", Theme.Warning)
                : Ui.Badge("Pogostuck は起動していません", Theme.Deemph);
        var bar = new DockPanel { LastChildFill = false };
        bar.Children.Add(status);
        var buttons = Ui.Row(
            Ui.Button("ショートカットを作成", CreateShortcut),
            Ui.Button("更新", () => { Reload(); _nav.Refresh(); }),
            Ui.Button("記録つきで Pogostuck を起動", StartRecording, primary: true));
        DockPanel.SetDock(buttons, Dock.Right);
        bar.Children.Add(buttons);
        StatusArea.Children.Add(Ui.Card(bar));

        if (_newVersion is not null)
        {
            var row = new DockPanel { LastChildFill = false };
            row.Children.Add(Ui.Text($"新しい版 v{_newVersion} があります。", 14, Theme.TextPrimary).WithMargin(new Thickness(0, 6, 0, 0)));
            var btn = Ui.Button("更新して再起動", ApplyUpdate, primary: true);
            DockPanel.SetDock(btn, Dock.Right);
            row.Children.Add(btn);
            StatusArea.Children.Add(Ui.Card(row));
        }
        if (_message is not null) StatusArea.Children.Add(Ui.Notice(_message));
        if (_data.GameDir is null)
            StatusArea.Children.Add(Ui.Notice("Pogostuck のインストール先が見つかりませんでした。Steam で Pogostuck をインストールしてください。", warn: true));
        if (_data.GameRunning && !_data.RecorderRunning)
            StatusArea.Children.Add(Ui.Notice("いまのプレイは画面の高さを記録できていません。次からはデスクトップの「Pogostuck（記録つき）」か、上のボタンから起動してください。", warn: true));
    }

    private void StartRecording()
    {
        _message = AppData.StartRecorder()
            ? "記録係を起動しました。Pogostuck が起動します（すでに起動していればそのまま記録を始めます）。"
            : "記録係（Pogoreco.Recorder.exe）が見つかりませんでした。";
        RenderStatus();
    }

    private void CreateShortcut()
    {
        try
        {
            var link = _data.CreateShortcut();
            _message = link is null ? "記録係（Pogoreco.Recorder.exe）が見つかりませんでした。" : "デスクトップに「Pogostuck（記録つき）」を作りました。次からはこれで起動すると、プレイが記録されます。";
        }
        catch (Exception e) { _message = $"ショートカットを作れませんでした: {e.Message}"; }
        RenderStatus();
    }
}

/// <summary>ページの移動。詳細ページから「戻る」で一覧に戻れるように、直前のページを覚えておく。</summary>
public sealed class Navigator(AppData data, Action<string, UIElement, bool> show)
{
    private readonly Stack<(string Title, Func<Navigator, UIElement> Build)> _history = new();
    private (string Title, Func<Navigator, UIElement> Build)? _current;

    public AppData Data { get; } = data;

    public void Go(string title, Func<Navigator, UIElement> build)
    {
        _history.Clear();
        _current = (title, build);
        show(title, build(this), false);
    }

    /// <summary>詳細ページへ進む。menuTitle を渡すと、別のメニュー（左のサイドバー）に切り替えて進む。</summary>
    public void Push(Func<Navigator, UIElement> build, string? menuTitle = null)
    {
        if (_current is { } c) _history.Push(c);
        var title = menuTitle ?? _current?.Title ?? "";
        _current = (title, build);
        show(title, build(this), false);
    }

    /// <summary>同じページの表示を切り替える（地図やランの選択など。戻る履歴には積まない）。</summary>
    public void Replace(Func<Navigator, UIElement> build)
    {
        var title = _current?.Title ?? "";
        _current = (title, build);
        show(title, build(this), false);
    }

    public void Back()
    {
        if (_history.Count == 0) return;
        _current = _history.Pop();
        show(_current.Value.Title, _current.Value.Build(this), false);
    }

    public bool CanGoBack => _history.Count > 0;

    /// <summary>データを読み直したあと、いまのページを描き直す（スクロール位置は保つ）。</summary>
    public void Refresh()
    {
        if (_current is { } c) show(c.Title, c.Build(this), true);
    }
}
