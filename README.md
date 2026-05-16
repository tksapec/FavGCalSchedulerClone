# FavGCalSchedulerClone

FavGCalScheduler の操作感に近づけることを目標にした、個人利用向けの Windows カレンダーアプリです。Google カレンダーの通常予定をローカル SQLite に同期し、月表示、1 日の予定、7 日先までの予定、ToDo 候補を表示します。

## 現在の実装範囲

- FavGCalScheduler 風の日本語メニュー、月ナビゲーション、月間カレンダー、下部タブ UI
- ローカル SQLite キャッシュによる予定の保存、編集、削除
- Google Calendar API による `primary` カレンダーとの双方向同期
- `#holiday` を含む予定の休日色表示
- `#work`、`#private`、`#important`、`#holiday` のタグ色分けと表示切り替え
- 本アプリ独自タグ `#workday` による土日祝日の平日色表示
- FavGCalScheduler の `#todoA56%` 形式によるToDo優先度、進捗、未処理/処理済み表示
- スケジュール追加、ToDo追加、スケジュール一覧、検索、アプリ設定、バージョン情報の軽量ダイアログ
- DPAPI による Google OAuth トークンのユーザー単位保護

## まだ未実装または簡易実装の項目

- 天気予報の取得とカレンダー上の天気表示
- 通知、メール通知、音楽再生、スリープ復帰
- 定期予定の詳細編集、今回のみ編集、削除例外
- 印刷、バックアップ、リストア、インポート、エクスポート
- アプリ設定ダイアログの永続化項目

## タグ仕様

| タグ | 種別 | 動作 |
| --- | --- | --- |
| `#holiday` | FavGCalScheduler互換 | その日のカレンダー表示色を休日色にします。 |
| `#workday` | 本アプリ独自 | 土日祝日でも、その日のカレンダー表示色を平日色にします。`#holiday` と同じ日にある場合は `#workday` を優先します。 |
| `#todoA56%` | FavGCalScheduler互換 | ToDoとして扱います。英字は優先度、数字は進捗率です。進捗が100%未満なら未処理、`#todo100%` や `#todoA100%` は処理済みとして表示します。 |
| `#work` / `#private` / `#important` | 本アプリ表示タグ | 予定バーの表示色を切り替えます。 |

ToDoタグはGoogleカレンダー上では通常の予定タイトルまたは説明欄内の文字列として残ります。本アプリでToDo進捗を更新すると、説明欄内の既存 `#todo...%` を置き換え、同じToDoタグを二重に追加しないようにします。

## Google OAuth 設定

このアプリは個人利用前提です。Google の審査申請、公開用プライバシーポリシー、ブランド審査は行っていません。

1. Google Cloud で個人用プロジェクトを作成します。
2. Google Calendar API を有効化します。
3. OAuth 同意画面を `External` で作成します。
4. 個人利用では `In production` にします。`Testing` のままだと refresh token が短期間で期限切れになる場合があります。
5. OAuth client ID を `Desktop app` として作成し、JSON をダウンロードします。
6. アプリの `カレンダー` タブで JSON を選択し、Google 認証を実行します。
7. 未検証アプリ警告が表示された場合は、本人が内容を確認して通過します。

OAuth client JSON はリポジトリや配布 ZIP に含めないでください。

## 開発

```powershell
dotnet build .\FavGCalSchedulerClone.sln
dotnet test .\FavGCalSchedulerClone.sln
dotnet run --project .\FavGCalSchedulerClone.App\FavGCalSchedulerClone.App.csproj
```

## 個人利用 ZIP 作成

```powershell
dotnet publish .\FavGCalSchedulerClone.App\FavGCalSchedulerClone.App.csproj -c Release -r win-x64 --self-contained false -o .\publish\win-x64
Compress-Archive -Path .\publish\win-x64\* -DestinationPath .\FavGCalSchedulerClone-win-x64.zip -Force
```
