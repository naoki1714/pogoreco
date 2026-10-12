# pogoreco

[Pogostuck: Rage With Your Friends](https://store.steampowered.com/app/688130/) のプレイを、手入力なしで自動で記録し、高度の推移などを見られる Windows 用のデスクトップアプリです（非公式）。

ゴールしていない途中経過も、ラン開始からの経過時間と高さのグラフで残ります。ホッピングで登っては落ちる、あの長い挑戦を振り返るためのツールです。

## できること

- **ダッシュボード**: いまのランの高さのグラフ、総プレイ時間、日別プレイ時間、最近のセッション、マップ別の記録。
- **ラン**: ラン（1 回の挑戦）ごとの高さのグラフ。区間（チェックポイント）・エリアでの色分け、エリアごとの累計滞在時間、落下の記録（Map 2 は斧に当たった回数も）、前のランとの差、セッションごとの最高到達。全マップを横断した一覧で、並べ替え・マップでの絞り込みができます。
- **セッション**: 遊んだ 1 回ごとの記録（ランごとの結果、到達地点）。
- **マップ**: マップ・モードごとの自己ベスト、区間ごとの到達数と「どこで終わったか」、ゴールタイムの推移。

グラフはどれも「表で見る」で同じ内容の表に切り替えられます。

## インストールと使い方

Windows 10（2004 以降）／ 11 と、Steam 版の Pogostuck が必要です。画面左上の高さの表示を Windows の OCR（英語）で読むので、読めない場合は Windows の設定 → 時刻と言語 → 言語と地域で「英語（米国）」を追加して、OCR を有効にしてください（ふつうは入っています）。

1. [Releases](https://github.com/naoki1714/pogoreco/releases) から `Pogoreco-win-Setup.exe` をダウンロードして実行します。.NET を別に入れる必要はありません。展開するだけで使える `Pogoreco-win-Portable.zip` もあります（自動更新はありません）。初めて開くとき、Windows の SmartScreen が「WindowsによってPCが保護されました」と警告することがあります（コード署名をしていないためです）。「詳細情報」→「実行」で開けます。
2. `Pogoreco.exe`（画面）を開き、右上の「ショートカットを作成」を押します。デスクトップに「Pogostuck（記録つき）」ができます。
3. **ゲームはこのショートカットから起動します**（画面の「記録つきで Pogostuck を起動」ボタンでも同じです）。ゲームが終わると記録係も自動で終わります。常駐しません。
4. 記録を見たいときに `Pogoreco.exe` を開きます。

区間・エリアの色分けなどを使うには、ゲームの Options → Misc → Show speedrun splits を「Minimal」にして遊んでください（区間を通過した記録が残ります）。新しい版が出ると、画面に「新しい版があります」と出るので、ゲームを終えてから「更新して再起動」を押します（インストーラーで入れた版のみ）。

記録は `%APPDATA%\pogoreco` に保存されます。アンインストールしても残るので、消したいときはこのフォルダを削除してください。

## 仕組み

ゲームには触りません。メモリの読み書きや入力の操作はしません。読むのは、ゲームが（`-diag` 付きで起動したとき）出力するログ、Steam クラウドのセーブ・リプレイのファイル、画面左上の高さの表示（画面を撮って OCR）だけです。記録はすべてあなたの PC の中にあり、外には送りません。

記録係（`Pogoreco.Recorder.exe`）がゲームと一緒に起動して、遊んでいる間だけ、高さ（1 秒ごと）・ログ・セーブを記録します。画面（`Pogoreco.exe`）は記録を見るときだけ開く別のアプリです。

## 開発

C#（.NET 10）・WPF。.NET 10 SDK が必要です。ビルドの前に、起動中の `Pogoreco.exe` を閉じてください（実行ファイルが使用中だとビルドが失敗します）。

```powershell
dotnet build Pogoreco.slnx -c Release
dotnet test tests/Pogoreco.Core.Tests
dotnet run --project src/Pogoreco.App -c Release
```

- `src/Pogoreco.Core`: ログ・セーブ・リプレイの解析と集計（画面に依存しない）
- `src/Pogoreco.Platform`: Windows 向けの共通部分（Steam の場所、データの保存先など）
- `src/Pogoreco.Recorder`: 記録係（画面取得と OCR）
- `src/Pogoreco.App`: 画面（WPF）
- `scripts/`: 配布物（インストーラー・zip）を作るスクリプト（インストーラー用に `dotnet tool install -g vpk` が必要）

## 謝辞・免責・ライセンス

ゲームのログの読み方（`-diag` ログの形式、マップ・モード・区間の番号と名前など）は、[derJunker/pogostuck-split-tracker](https://github.com/derJunker/pogostuck-split-tracker) の調査を参考にさせていただきました。ありがとうございます。

このプロジェクトは非公式で、Pogostuck の開発元・Steam とは関係ありません。Pogostuck は各権利者のゲームです。自己責任でお使いください。

ライセンスは [MIT License](LICENSE) です。
