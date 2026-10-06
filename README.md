# VehicleVision.PleasanterTools.McpOAuthWrapper

Pleasanter の MCP サーバーを、Claude などの AI アプリから OAuth で接続するための認証ラッパーです。

## インストール

1. Windows または Linux に [ASP.NET Core Runtime 10](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) をインストールします。
2. GitHub にログインし、[Actions の CI](https://github.com/vehiclevisionjp/VehicleVision.PleasanterTools.McpOAuthWrapper/actions/workflows/ci.yml) で develop の成功した実行から **McpOAuthWrapper** をダウンロードして展開します。
3. 展開先で次のコマンドを実行し、`http://127.0.0.1:5180/health/live` に `Healthy` が表示されることを確認します。

   ```text
   dotnet VehicleVision.PleasanterTools.McpOAuthWrapper.dll --urls http://127.0.0.1:5180
   ```

4. 停止して [導入手順](_documents/導入手順.md) に従い、データベースの読み取り専用接続、OAuth クライアント、証明書と公開 HTTPS URL を設定します。設定完了後に `App_Data/Parameters/General.json` の `Enabled` を `true` にして起動し、AI アプリへ公開 URL の `/mcp` を登録します。

初期設定は接続を無効にしており、起動確認だけでは MCP を利用できません。API キーは先に Pleasanter で発行しておきます。

## 使い方

1. AI アプリで管理者が用意したコネクタを選びます。接続先はラッパーの `/mcp` です。
2. ラッパーのログイン画面で Pleasanter のログイン ID とパスワードを入力します。
3. 自分のアカウントを使うか、管理者が用意した共通アカウントを使うかを選びます。共通アカウントを設定していない場合、選択肢は表示されません。
4. 接続する AI アプリと使用するアカウントを確認し、利用を許可します。
5. AI アプリへ戻ると、Pleasanter の MCP 機能を利用できます。

ラッパーへ API キーを登録する操作はありません。キーは Pleasanter の Users テーブルから通信のたびに読み取り、ラッパーには保存しません。キーが未発行なら、先に Pleasanter 本体で発行してください。

個人アカウントでは本人の権限、共通アカウントではそのアカウントの権限で処理します。共通アカウントを選んでも、ラッパーへのログインは利用者本人のアカウントで行います。

## 現在の対応範囲

OAuth 認可、個人／共通アカウントの選択、MCP の中継を実装した初期版です。**Pleasanter 実機と Claude の組織コネクタを通した接続試験は未実施です。** 初期設定では接続を無効にしています。

Pleasanter のローカル認証が対象です。二段階認証、パスキー、LDAP などを有効にした構成では、この初期版によるログインを提供しません。

## 資料

- [管理者向け導入手順](_documents/導入手順.md)
- [開発者向けガイド](_documents/開発ガイド.md)
- [Pleasanter](https://github.com/Implem/Implem.Pleasanter)

## ライセンス

AGPL v3（AGPL-3.0-or-later）。[LICENSE](LICENSE) と [NOTICE](NOTICE) を参照してください。
