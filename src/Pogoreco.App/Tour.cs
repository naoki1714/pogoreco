using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Pogoreco.App.Charts;

namespace Pogoreco.App;

/// <summary>
/// はじめて開いたときに出す、ステップ式のポップアップ案内。
/// ページの説明のステップでは、うしろの画面をそのページに切り替えながら説明する。
/// 一度見終わる（またはスキップする）と、記録フォルダに印を残して次からは自動では出さない。
/// </summary>
internal sealed class Tour
{
    private sealed record Step(string Title, Func<UIElement> Body, string? Page = null, bool AtBottom = false);

    private readonly Grid _overlay;
    private readonly Border _host = new() { HorizontalAlignment = HorizontalAlignment.Center };
    private readonly List<Step> _steps;
    private readonly Panel _root;
    private readonly AppData _data;
    private readonly Action<string> _goPage;
    private int _index;

    private Tour(Panel root, AppData data, Action<string> goPage)
    {
        _root = root;
        _data = data;
        _goPage = goPage;
        _overlay = new Grid { Background = new SolidColorBrush(Color.FromArgb(0x55, 0x1F, 0x2D, 0x26)) };
        Grid.SetColumnSpan(_overlay, 2);
        Panel.SetZIndex(_overlay, 100);
        _overlay.Children.Add(_host);
        _steps =
        [
            new("ようこそ、pogoreco へ", () => Body(
                "Pogostuck のプレイを自動で記録して、高さの推移や記録を振り返るためのアプリです。",
                "ゴールしていない途中経過も、グラフで残ります。",
                "この案内は 1 分ほどです。左下の「？」から、いつでも見直せます。")),
            new("データはこの 3 つから集めます", () => Body(
                "① Steam クラウドのセーブデータ … ランの軌跡とゴールのタイム",
                "② ログファイル（acklog.txt）… プレイの開始と終了、マップ・モード、区間の通過、落下",
                "③ 画面の取得と OCR … ゲーム画面の左上の「高さ」と「タイム」を 1 秒ごとに読む（撮るのは左上だけで、画像は保存しません）",
                "ゲームのメモリや入力には触りません。記録はすべてこの PC の中です。")),
            new("場所は自動で見つけます", Locations),
            new("ゲームは「記録つき」で起動します", () => Body(
                "デスクトップの「Pogostuck（記録つき）」か、右上の「記録つきで Pogostuck を起動」から遊んでください。",
                "Steam から直接起動すると、高さの記録が取れません。ショートカットは、左下の「ショートカットを作成」で作れます。",
                "ゲームを終えると、記録係も自動で終わります。左下の表示が「記録中」なら、記録できています。")),
            new("ダッシュボード", () => Body(
                "いまのランの高さのグラフ、総プレイ時間、最近のセッション、マップ別の記録が見られる最初のページです。",
                "グラフの右上の「>」で、そのランの詳細に進めます。"), "ダッシュボード", AtBottom: true),
            new("ラン", () => Body(
                "ラン（リセットかゴールまでの 1 回の挑戦）ごとの高さのグラフです。",
                "区間・エリアの色分け、エリアごとの累計滞在時間、落下の記録、前のランとの差が見られます。一覧は並べ替え・マップでの絞り込みができます。"), "ラン", AtBottom: true),
            new("セッション", () => Body(
                "ゲームを起動してから閉じるまでの 1 回分の記録です。",
                "ランごとの結果と到達地点が見られ、行を選ぶと詳細から各ランにも飛べます。"), "セッション", AtBottom: true),
            new("マップ", () => Body(
                "マップ・モードごとの自己ベスト、区間ごとの到達数と「どこで終わったか」、ゴールタイムの推移です。",
                "「ランで見る」で、そのマップのランだけを一覧できます。"), "マップ", AtBottom: true),
            new("最後に（おすすめの設定）", () => Body(
                "ゲームの Options → Misc → Show speedrun splits を「Minimal」にすると、区間（チェックポイント）の通過も記録され、ランの色分けやエリアごとの集計が出ます。",
                "どのグラフも「表で見る」で表に切り替えられます。",
                "さあ、遊んでみましょう！")),
        ];
    }

    private static string DoneMark(AppData data) => Path.Combine(data.Store.Root, "tour-done");

    /// <summary>まだ案内を見ていなければ始める（見た印があれば何もしない）。</summary>
    public static void StartIfFirstTime(Panel root, AppData data, Action<string> goPage)
    {
        if (!File.Exists(DoneMark(data))) Start(root, data, goPage);
    }

    public static void Start(Panel root, AppData data, Action<string> goPage)
    {
        var tour = new Tour(root, data, goPage);
        root.Children.Add(tour._overlay);
        tour.Show();
    }

    private void Finish()
    {
        _root.Children.Remove(_overlay);
        try
        {
            Directory.CreateDirectory(_data.Store.Root);
            File.WriteAllText(DoneMark(_data), DateTime.Now.ToString("O"));
        }
        catch (IOException) { /* 印が残せなくても、次回また案内が出るだけ */ }
    }

    private void Show()
    {
        var step = _steps[_index];
        if (step.Page is not null) _goPage(step.Page);

        // ページを説明するステップでは、うしろのページが見えるよう下に寄せる（メニューの幅ぶん右へ）
        _host.VerticalAlignment = step.AtBottom ? VerticalAlignment.Bottom : VerticalAlignment.Center;
        _host.Margin = step.AtBottom ? new Thickness(220, 0, 0, 36) : new Thickness(0);

        var dots = Ui.Row();
        for (int i = 0; i < _steps.Count; i++)
            dots.Children.Add(new System.Windows.Shapes.Ellipse
            {
                Width = 7,
                Height = 7,
                Margin = new Thickness(0, 0, 5, 0),
                Fill = i == _index ? Theme.TextPrimary : Theme.Border,
            });

        var last = _index == _steps.Count - 1;
        var buttons = new DockPanel { Margin = new Thickness(0, 16, 0, 0), LastChildFill = false };
        var right = Ui.Row();
        if (_index > 0) right.Children.Add(Ui.Button("戻る", () => { _index--; Show(); }));
        right.Children.Add(Ui.Button(last ? "はじめる" : "次へ", () => { if (last) Finish(); else { _index++; Show(); } }, primary: true));
        DockPanel.SetDock(right, Dock.Right);
        buttons.Children.Add(right);
        if (!last)
        {
            var skip = Ui.Link("スキップ", Finish);
            skip.VerticalAlignment = VerticalAlignment.Center;
            buttons.Children.Add(skip);
        }

        var card = Ui.Card(Ui.Stack(
            Ui.Muted($"{_index + 1} / {_steps.Count}").WithMargin(new Thickness(0, 0, 0, 4)),
            Ui.H2(step.Title).WithMargin(new Thickness(0, 0, 0, 10)),
            step.Body(),
            Ui.Row(dots).WithMargin(new Thickness(0, 14, 0, 0)),
            buttons));
        card.Margin = new Thickness(0);
        card.MinWidth = 480;
        card.MaxWidth = 600;
        _host.Child = card;
    }

    private static UIElement Body(params string[] lines)
    {
        var stack = Ui.Stack();
        foreach (var line in lines) stack.Children.Add(Ui.Text(line, 14, Theme.TextSecondary, margin: new Thickness(0, 0, 0, 6)));
        return stack;
    }

    /// <summary>自動で見つけたインストール先を、そのまま見せる。</summary>
    private UIElement Locations()
    {
        var stack = Ui.Stack(Ui.Text(
            _data.GameDir is null
                ? "Pogostuck のインストール先が見つかりませんでした。Steam で Pogostuck をインストールしてください。"
                : "Steam と Pogostuck のインストール先は、設定しなくても自動で見つけます（Steam のレジストリとライブラリ一覧から探すので、別のドライブでも大丈夫です）。",
            14, _data.GameDir is null ? Theme.Warning : Theme.TextSecondary, margin: new Thickness(0, 0, 0, 8)));
        stack.Children.Add(PathRow("Steam", _data.SteamDir));
        stack.Children.Add(PathRow("ゲーム・ログ", _data.GameDir));
        stack.Children.Add(PathRow("クラウドのセーブ", _data.RemoteDir));
        stack.Children.Add(PathRow("pogoreco の記録", _data.Store.Root));
        return stack;
    }

    private static UIElement PathRow(string label, string? path)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var value = Ui.Text(path ?? "見つかりません", 12, path is null ? Theme.Warning : Theme.TextPrimary);
        Grid.SetColumn(value, 1);
        grid.Children.Add(Ui.Text(label, 12, Theme.TextSecondary));
        grid.Children.Add(value);
        return grid;
    }
}
