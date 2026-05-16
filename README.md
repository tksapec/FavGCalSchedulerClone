# FavGCalSchedulerClone

FavGCalScheduler の操作感に近づけることを目標にした、個人利用向けの Windows カレンダーアプリです。Google カレンダーの通常予定をローカル SQLite に同期し、月表示、1 日の予定、7 日先までの予定、ToDo 候補を表示します。

## 現在の実装範囲

- FavGCalScheduler 風の日本語メニュー、月ナビゲーション、月間カレンダー、下部タブ UI
- ローカル SQLite キャッシュによる予定の保存、編集、削除
- Google Calendar API による `primary` カレンダーとの双方向同期
- `#holiday` を含む予定の休日色表示
- `#work`、`#private`、`#important`、`#holiday` のタグ色分けと表示切り替え
- FavGCalScheduler の `#todoA56%` 形式の検出と ToDo 候補表示
- スケジュール追加、ToDo追加、スケジュール一覧、検索、アプリ設定、バージョン情報の軽量ダイアログ
- DPAPI による Google OAuth トークンのユーザー単位保護

## まだ未実装または簡易実装の項目

- 天気予報の取得とカレンダー上の天気表示
- 通知、メール通知、音楽再生、スリープ復帰
- 定期予定の詳細編集、今回のみ編集、削除例外
- 印刷、バックアップ、リストア、インポート、エクスポート
- アプリ設定ダイアログの永続化項目
- ToDo の処理済み管理、優先度や進捗の専用UI

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
