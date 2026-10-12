using System.Windows;
using Pogoreco.App.Charts;

namespace Pogoreco.App.Pages;

/// <summary>使い方のページ: データの取り方、はじめかた、各ページの説明、Steam・ゲームの場所（自動検出の結果）。</summary>
internal static class HelpPage
{
    public static UIElement Build(Navigator nav)
    {
        var page = Ui.Stack(Ui.H1("使い方").WithMargin(new Thickness(0, 0, 0, 16)));
        page.Children.Add(Sources(nav.Data));
        page.Children.Add(Tutorial());
        page.Children.Add(Pages());
        return page;
    }

    private static UIElement Paragraph(string text) => Ui.Text(text, 14, Theme.TextSecondary, margin: new Thickness(0, 0, 0, 6));

    private static UIElement Item(string title, string body) => Ui.Stack(
        Ui.Text(title, 14, bold: true, margin: new Thickness(0, 6, 0, 2)),
        Ui.Text(body, 13, Theme.TextSecondary, margin: new Thickness(0, 0, 0, 4)));

    private static UIElement Sources(AppData data)
    {
        var stack = Ui.Stack(
            Ui.H2("データの取り方").WithMargin(new Thickness(0, 0, 0, 8)),
            Paragraph("pogoreco はゲームには触りません（メモリの読み書きや入力の操作はしません）。次の 3 つを読むだけで、記録はすべてこの PC の中に保存します。"),
            Item("Steam クラウドのセーブデータ", "ランの軌跡（どの区間を通ったか、いつリセットしたか）を、ゲームが保存するファイルから読みます。ゴールのタイムもここからです。"),
            Item("ログファイル（acklog.txt）", "ゲームを「記録つき」で起動すると出力されるログから、プレイの開始・終了、マップ・モード、区間の通過、落下などを読みます。"),
            Item("画面の取得と OCR", "ゲーム画面の左上にある「今の高さ」と「タイム」を 1 秒ごとに撮り、Windows の文字認識（OCR）で読みます。ゴールまでの途中経過のグラフはこれで描きます。撮るのはゲームのウィンドウの左上だけで、画像は保存しません。"),
            Ui.H2("場所は自動で見つけます").WithMargin(new Thickness(0, 14, 0, 6)));

        stack.Children.Add(data.GameDir is null
            ? Ui.Notice("Pogostuck のインストール先が見つかりませんでした。Steam で Pogostuck をインストールしてください。", warn: true)
            : Paragraph("Steam と Pogostuck のインストール先は、設定しなくても自動で見つけます（Steam のレジストリと、Steam のライブラリ一覧から探します。別のドライブにインストールしていても大丈夫です）。"));
        stack.Children.Add(PathRow("Steam", data.SteamDir));
        stack.Children.Add(PathRow("Pogostuck（ゲーム・ログ）", data.GameDir));
        stack.Children.Add(PathRow("Steam クラウドのセーブ", data.RemoteDir));
        stack.Children.Add(PathRow("pogoreco の記録", data.Store.Root));
        return Ui.Card(stack);
    }

    private static UIElement PathRow(string label, string? path) => Ui.Row(
        Ui.Text(label, 13, Theme.TextSecondary, margin: new Thickness(0, 2, 12, 2)).WithWidth(190),
        Ui.Text(path ?? "見つかりません", 13, path is null ? Theme.Warning : Theme.TextPrimary, margin: new Thickness(0, 2, 0, 2)));

    private static UIElement Tutorial() => Ui.Card(Ui.Stack(
        Ui.H2("はじめかた").WithMargin(new Thickness(0, 0, 0, 8)),
        Item("1. ショートカットを作る", "左下の「ショートカットを作成」を押すと、デスクトップに「Pogostuck（記録つき）」ができます（すでにあるときはこのリンクは出ません）。"),
        Item("2. 記録つきで遊ぶ", "ゲームはこのショートカットか、画面右上の「記録つきで Pogostuck を起動」から起動します。Steam から直接起動すると、高さの記録が取れません。ゲームを終えると記録係も自動で終わります。"),
        Item("3. 記録を見る", "遊び終えたら（遊んでいる間でも）、この画面を開きます。左下に「記録中」と出ていれば記録できています。10 秒ごとに自動で読み直し、「更新」ボタンで今すぐ読み直せます。"),
        Item("4. 区間の色分けを使う（任意）", "ゲームの Options → Misc → Show speedrun splits を「Minimal」にすると、区間（チェックポイント）の通過が記録され、ランの色分けやエリアごとの集計が出ます。"),
        Item("5. 新しい版があるとき", "画面の上に「新しい版があります」と出ます。ゲームを終えてから「更新して再起動」を押してください。")));

    private static UIElement Pages() => Ui.Card(Ui.Stack(
        Ui.H2("ページの説明").WithMargin(new Thickness(0, 0, 0, 8)),
        Item("ダッシュボード", "いまのランの高さのグラフ、総プレイ時間、最近のセッション、マップ別の記録をひと目で見る最初のページです。「>」で、そのランの詳細に進めます。"),
        Item("ラン", "ラン（リセットかゴールまでの 1 回の挑戦）ごとの高さのグラフです。区間・エリアでの色分け、エリアごとの累計滞在時間、落下の記録、前のランとの差が見られます。一覧は並べ替えとマップでの絞り込みができ、行を選ぶと詳細が上に出ます。"),
        Item("セッション", "ゲームを起動してから閉じるまでの 1 回分の記録です。ランごとの結果と、到達地点が見られます。行を選ぶと詳細に進み、そこからランにも飛べます。"),
        Item("マップ", "マップ・モードごとの自己ベスト、区間ごとの到達数と「どこで終わったか」、ゴールタイムの推移です。「ランで見る」で、そのマップのランだけを一覧できます。"),
        Item("グラフの見方", "どのグラフも、右上の「表で見る」で同じ内容の表に切り替えられます。")));

    private static T WithWidth<T>(this T element, double width) where T : FrameworkElement
    {
        element.Width = width;
        return element;
    }
}
