# FavGCalSchedulerClone

FavGCalScheduler の操作感を参考にした、個人利用向けの Windows カレンダーアプリです。

Google Calendar の予定をローカル SQLite に同期し、月/週/日表示、予定編集、ToDo、通知、検索、バックアップなどを1つのデスクトップアプリで扱えます。

> [!NOTE]
> FavGCalScheduler の完全な複製を目的としたアプリではありません。日常利用に必要な機能を中心に実装し、一部機能は意図的に仕様対象外としています。

## 主な機能

### カレンダー・予定管理

- FavGCalScheduler 風の日本語UI、月ナビゲーション、月/週/日表示
- ローカル SQLite キャッシュによる予定の保存、編集、削除
- 選択日の予定、7日先までの予定、未処理/処理済ToDoの表示
- 複数日にまたがる予定の連結表示
- 月/週表示からのドラッグによる日付移動
- スケジュール一覧、検索、予定/ToDo編集ダイアログ
- ISO週番号、隠れ件数表示、ウィンドウ位置/サイズ/最大化状態の復元
- 「フォーカス解除時に今日へ戻す」のON/OFF

### Google Calendar 同期

- Google Calendar API による `primary` カレンダーとの双方向同期
- 自動同期、手動同期前プレビュー
- Google互換 `ColorId` による予定ラベル色の保持・表示・編集
- ローカル未同期変更を保護する競合処理
- 同期診断ログ、最終同期結果、未同期件数、syncToken状態の確認
- DPAPI による Google OAuth トークンのユーザー単位保護
- 初回同期では既定で過去5年分の予定を取得

### ToDo

- FavGCalScheduler の `#todoA56%` 形式に対応
- 優先度、進捗率、未処理/処理済み表示
- UIからの進捗更新、完了操作
- 期限日のローカル時刻08:15にアプリ内通知
- 起動していなかった場合は期限日当日の起動時に遅延通知

### 祝日・日表示

- 内閣府公表CSVを同梱した日本の祝日表示
- 設定画面から祝日CSVをオンライン更新
- `#holiday` による休日色指定
- 本アプリ独自の `#workday` による土日祝日の平日色指定
- `#workday` は公式祝日および `#holiday` より優先

更新した祝日CSVは次の場所に保存されます。

```text
%LocalAppData%\FavGCalSchedulerClone\JapaneseHolidays.csv
```

### データ保護・入出力

- ローカル SQLite DB の ZIP バックアップと安全なリストア
- 現在表示年の予定CSVエクスポート
- CSVからの新規予定インポート
- FavGCalSchedulerから取り込んだ予定色やToDo情報の修復機能
- 件名/場所の入力履歴

### 通知・常駐

- 通知音のテスト再生
- Windows通知のON/OFF
- タスクトレイ常駐
- 多重起動防止
- 閉じるボタンでは終了せず、トレイメニューから明示的に終了

## 仕様範囲

本アプリでは、日常的に使用する予定管理、Google Calendar同期、ToDo、通知、バックアップを中心にしています。

### 意図的に仕様対象外としている機能

- 繰り返し予定の作成・詳細編集
- 「今回のみ」「これ以降」「シリーズ全体」といった繰り返し予定固有の編集機能
- FavGCalSchedulerの全画面・全アイコン・全操作の完全再現

Google側に既存の繰り返し予定が存在する場合でも、繰り返し予定機能そのものを正式サポートしていることを意味しません。

### 現在実装していない機能

- メール通知
- スリープ復帰を利用した通知
- 天気表示
- お知らせ機能
- SMTPメール通知

これらは現在の本アプリの主要用途には含めていません。

## タグ仕様

| タグ | 種別 | 動作 |
| --- | --- | --- |
| `#holiday` | FavGCalScheduler互換 | その日のカレンダー表示色を休日色にします。 |
| `#workday` | 本アプリ独自 | 土日祝日でも平日色で表示します。`#holiday` と同じ日にある場合は `#workday` を優先します。 |
| `#todoA56%` | FavGCalScheduler互換 | ToDoとして扱います。英字は優先度、数字は進捗率です。進捗100%未満は未処理、`#todo100%` や `#todoA100%` は処理済みとして表示します。 |

ToDoタグはGoogle Calendar上では通常の予定タイトルまたは説明欄内の文字列として保持されます。本アプリで進捗を更新すると、既存の `#todo...%` を置き換え、同じToDoタグを重複して追加しないようにします。

日セル用 directive として扱うのは `#holiday` と `#workday` です。これらは予定ラベル色とは別の仕組みです。

## Google OAuth 設定

このアプリは個人利用を前提としています。Googleの審査申請、公開用プライバシーポリシー、ブランド審査は行っていません。

1. Google Cloud で個人用プロジェクトを作成します。
2. Google Calendar API を有効化します。
3. OAuth同意画面を `External` で作成します。
4. 個人利用では `In production` にします。`Testing` のままだと refresh token が短期間で期限切れになる場合があります。
5. OAuth client ID を `Desktop app` として作成し、JSONをダウンロードします。
6. アプリの `カレンダー` タブでJSONを選択し、Google認証を実行します。
7. 未検証アプリ警告が表示された場合は、本人が内容を確認して通過します。

> [!WARNING]
> OAuth client JSON はリポジトリや配布ZIPに含めないでください。

## 開発

### 必要環境

- Windows
- .NET 9 SDK

アプリは `net9.0-windows10.0.17763.0` を対象にしています。

### ビルド・テスト・起動

```powershell
dotnet build .\FavGCalSchedulerClone.sln
dotnet test .\FavGCalSchedulerClone.sln
dotnet run --project .\FavGCalSchedulerClone.App\FavGCalSchedulerClone.App.csproj
```

詳細な手動確認項目と FavGCalScheduler 実機比較メモは [docs/TESTING.md](docs/TESTING.md) にまとめています。

## 個人利用 ZIP の作成

```powershell
.\scripts\publish-release.ps1
Compress-Archive -Path .\publish\* -DestinationPath .\FavGCalSchedulerClone-win-x64.zip -Force
```

発行済みアプリは常に次の実行ファイルを使用します。

```text
publish\FavGCalSchedulerClone.App.exe
```

`bin` と `obj` は開発時の中間出力であり、配布・起動には使用しません。
