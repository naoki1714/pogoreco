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
    private sealed record Step(string Title, Func<UIElement> Body, string? Page = null);

    private readonly Grid _overlay;
    private readonly Border _host = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
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
                "Pogostuck のプレイを自動で記録して、高さの推移や記録を振り返るアプリです。",
                "ゴールしていない途中経過も、グラフで残ります。",
                "この案内は、左下の「？」からいつでも見直せます。")),
            new("読むのは、この 3 つだけ", () => Body(
                "・Steam クラウドのセーブデータ",
                "・ゲームのログファイル",
                "・画面左上の高さとタイム（OCR）",
                "ゲームのメモリや入力には触りません。記録はこの PC の中にだけ保存します。")),
            new("Steam と Pogostuck のフォルダを読みます", () => Body(
                "上の 3 つは、Steam と Pogostuck のフォルダから読み取ります。読むだけで、書き換えたり外へ送ったりはしません。",
                "どのフォルダを読んでいるかは、左下の歯車（設定）で確認できます。")),
            new("ゲームは「記録つき」で起動します", () => Body(
                "デスクトップの「Pogostuck（記録つき）」か、右上の「記録つきで Pogostuck を起動」から遊んでください。",
                "Steam から直接起動すると、高さが記録できません。ショートカットは左下の「ショートカットを作成」で作れます。")),
            new("ダッシュボード", () => Body(
                "いまのラン、総プレイ時間、最近のセッション、マップ別の記録をひと目で見られます。"), "ダッシュボード"),
            new("ラン", () => Body(
                "1 回の挑戦ごとの高さのグラフです。区間の色分け、エリアごとの滞在時間、落下の記録、前のランとの差が見られます。"), "ラン"),
            new("セッション", () => Body(
                "ゲームを起動してから閉じるまでの 1 回分の記録です。ランごとの結果と到達地点が見られます。"), "セッション"),
            new("マップ", () => Body(
                "マップ・モードごとの自己ベスト、区間ごとの到達数、ゴールタイムの推移です。"), "マップ"),
            new("最後に", () => Body(
                "ゲームの Options → Misc → Show speedrun splits を「Minimal」にすると、区間の通過も記録され、ランの色分けが使えます。",
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

        // 位置も大きさも固定（ボタンの場所が変わらないように）。本文だけが中で変わる
        var footer = Ui.Stack(Ui.Row(dots).WithMargin(new Thickness(0, 0, 0, 0)), buttons);
        var head = Ui.Stack(
            Ui.Muted($"{_index + 1} / {_steps.Count}").WithMargin(new Thickness(0, 0, 0, 4)),
            Ui.H2(step.Title).WithMargin(new Thickness(0, 0, 0, 10)));
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var body = step.Body();
        Grid.SetRow(body, 1);
        Grid.SetRow(footer, 2);
        layout.Children.Add(head);
        layout.Children.Add(body);
        layout.Children.Add(footer);
        var card = Ui.Card(layout);
        card.Margin = new Thickness(0);
        card.Width = 540;
        card.Height = 330;
        _host.Child = card;
    }

    private static UIElement Body(params string[] lines)
    {
        var stack = Ui.Stack();
        foreach (var line in lines) stack.Children.Add(Ui.Text(line, 14, Theme.TextSecondary, margin: new Thickness(0, 0, 0, 6)));
        return stack;
    }
}
