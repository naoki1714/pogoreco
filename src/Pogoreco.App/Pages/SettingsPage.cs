using System.Windows;
using System.Windows.Controls;
using Pogoreco.App.Charts;

namespace Pogoreco.App.Pages;

/// <summary>設定のページ: 何をどこから読んでいるか、フォルダの場所、記録の状態。案内をもう一度見ることもできる。</summary>
internal static class SettingsPage
{
    public static UIElement Build(Navigator nav, Action showTour)
    {
        var data = nav.Data;
        var page = Ui.Stack(Ui.H1("設定").WithMargin(new Thickness(0, 0, 0, 16)));

        if (data.GameDir is null)
            page.Children.Add(Ui.Notice("Pogostuck のインストール先が見つかりませんでした。Steam で Pogostuck をインストールしてください。", warn: true));

        page.Children.Add(Ui.Card(Ui.Stack(
            Ui.H2("読んでいるもの").WithMargin(new Thickness(0, 0, 0, 4)),
            Ui.Muted("pogoreco はこれらを読むだけです。ゲームのメモリや入力には触らず、記録は外へ送りません。").WithMargin(new Thickness(0, 0, 0, 10)),
            Source("Steam クラウドのセーブデータ", "ランの軌跡（どの区間を通ったか・リセット）とゴールのタイム"),
            Source("ゲームのログファイル（acklog.txt）", "プレイの開始と終了、マップ・モード、区間の通過、落下"),
            Source("画面左上の高さとタイム（OCR）", "ゲームのウィンドウの左上だけを 1 秒ごとに撮って文字を読む。画像は保存しない"))));

        page.Children.Add(Ui.Card(Ui.Stack(
            Ui.H2("フォルダ").WithMargin(new Thickness(0, 0, 0, 4)),
            Ui.Muted("Steam のレジストリとライブラリ一覧から、自動で見つけています。").WithMargin(new Thickness(0, 0, 0, 10)),
            Row("Steam", data.SteamDir),
            Row("Pogostuck（ログ）", data.GameDir),
            Row("クラウドのセーブ", data.RemoteDir),
            Row("pogoreco の記録", data.Store.Root))));

        page.Children.Add(Ui.Card(Ui.Stack(
            Ui.H2("状態").WithMargin(new Thickness(0, 0, 0, 10)),
            Row("記録係", data.RecorderRunning ? "動いています" : "動いていません"),
            Row("スプリット表示", data.SplitsEnabled switch
            {
                true => "オン（区間の通過を記録できます）",
                false => "オフ（ゲームの Options → Misc で「Minimal」にすると区間が記録されます）",
                _ => "不明",
            }),
            Ui.Button("案内をもう一度見る", showTour).WithMargin(new Thickness(0, 12, 0, 0)))));
        return page;
    }

    private static UIElement Source(string title, string what) => Ui.Stack(
        Ui.Text(title, 14, bold: true, margin: new Thickness(0, 6, 0, 2)),
        Ui.Text(what, 13, Theme.TextSecondary, margin: new Thickness(0, 0, 0, 4)));

    private static UIElement Row(string label, string? value)
    {
        var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var v = Ui.Text(value ?? "見つかりません", 13, value is null ? Theme.Warning : Theme.TextPrimary);
        Grid.SetColumn(v, 1);
        grid.Children.Add(Ui.Text(label, 13, Theme.TextSecondary));
        grid.Children.Add(v);
        return grid;
    }
}
