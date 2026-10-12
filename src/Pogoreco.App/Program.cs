using Velopack;

namespace Pogoreco.App;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        // インストール・更新・アンインストールのときに Velopack が呼ぶ処理。普通の起動では何もせず戻る
        VelopackApp.Build().Run();
        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
