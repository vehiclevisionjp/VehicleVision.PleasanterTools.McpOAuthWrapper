# VehicleVision.PleasanterTools.McpOAuthWrapper

Pleasanter の MCP サーバーを、Claude などの AI アプリから OAuth で接続するための認証ラッパーです。

## インストール

1. Windows または Linux に [ASP.NET Core Runtime 10](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) をインストールします。
2. [Release ページ](https://github.com/vehiclevisionjp/VehicleVision.PleasanterTools.McpOAuthWrapper/releases)で使用するバージョンを選び、Assets の **McpOAuthWrapper.zip** をダウンロードして展開します。Source code の ZIP は配布アプリではありません。
3. 展開先で次のコマンドを実行し、`http://127.0.0.1:5180/health/live` に `Healthy` が表示されることを確認します。

   ```text
   dotnet VehicleVision.PleasanterTools.McpOAuthWrapper.dll --urls http://127.0.0.1:5180
   ```

4. 停止して [導入手順](_documents/導入手順.md) に従い、データベースの読み取り専用接続、OAuth クライアント、証明書と公開 HTTPS URL を設定します。設定完了後に `App_Data/Parameters/General.json` の `Enabled` を `true` にして起動し、AI アプリへ公開 URL の `/mcp` を登録します。

初期設定は接続を無効にしており、起動確認だけでは MCP を利用できません。API キーは先に Pleasanter で発行しておきます。

## 使い方

1. AI アプリで管理者が用意したコネクタを選びます。接続先はラッパーの `/mcp` です。
2. ラッパーのログイン画面で、パスワード欄に Pleasanter で発行済みの API キーを入力します。ログイン ID は固定値（初期値 `apikey`）が自動表示されます。
3. 自分のアカウントを使うか、管理者が用意した共通アカウントを使うかを選びます。共通アカウントを設定していない場合、選択肢は表示されません。
4. 接続する AI アプリと使用するアカウントを確認し、利用を許可します。
5. AI アプリへ戻ると、Pleasanter の MCP 機能を利用できます。

API キーは接続時の認証に使い、ラッパーには保存しません。MCP 通信のたびに Pleasanter の Users テーブルから読み取ります。キーが未発行のアカウントはログインできません。先に Pleasanter で発行してください。キーの削除・再発行後は古い OAuth トークンを利用できないため、新しいキーで再接続します。

個人の API キーでログインした場合は、その本人を識別します。管理者が共通アカウントを設定していれば、その権限で接続することも選べます。共通の API キーを入力してログインした場合、識別・操作の主体は共通アカウントとなり、利用者個人は識別できません。

## 現在の対応範囲

OAuth 認可、個人／共通アカウントの選択、MCP の中継を実装した初期版です。Docker 上の Pleasanter 1.5.8.1／PostgreSQL 17 で OAuth 認可と MCP 接続を確認しています。SQL Server 2025・PostgreSQL 17・MySQL 8.4 は Docker で読み取り専用接続と Users の SELECT を確認済みです。Claude の組織コネクタ、Azure App Service・IIS の実機確認は未実施です。 初期設定では接続を無効にしています。

API キーを持っていることを認証根拠とします。Pleasanter のログインパスワードは使いません。LDAP・パスキー・二段階認証を使用する環境でも、Pleasanter で発行済みの API キーを使う方式です。ラッパーがそれらのログイン処理を実行したり、完了した証明を受け取ったりするものではありません。

画面は日本語・英語・中国語・ドイツ語・韓国語・スペイン語・ベトナム語に対応します。

OAuth の状態は SQLite（既定）または Redis／Valkey 互換 KVS に保存できます。KVS モードの設定とインスタンス間での共有方法は [導入手順](_documents/導入手順.md)を参照してください。API キーそのものはいずれの保存先にも保存しません。

## 資料

- [Claude・ChatGPT の接続手順](_documents/AIアプリ接続手順.md)
- [画面と接続の流れ](_documents/利用ガイド.md)
- [管理者向け導入手順](_documents/導入手順.md)
- [開発者向けガイド](_documents/開発ガイド.md)
- [Pleasanter](https://github.com/Implem/Implem.Pleasanter)

## ライセンス

AGPL v3（AGPL-3.0-or-later）。[LICENSE](LICENSE) と [NOTICE](NOTICE) を参照してください。
