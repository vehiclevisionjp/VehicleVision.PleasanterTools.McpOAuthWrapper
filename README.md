# VehicleVision.PleasanterTools.McpOAuthWrapper

Pleasanter の MCP サーバーを、Claude などの AI アプリから組織のコネクタとして利用するための認証ラッパーです。

## 何をするものか

Pleasanter の MCP サーバーへの接続には API キーが必要です。本製品はその手前に OAuth による認証の入口を設け、AI アプリへコネクタとして配布できるようにすることを目指しています。

導入後は、利用者が組織で用意したコネクタへサインインし、AI アプリから Pleasanter の MCP 機能を利用する想定です。**各利用者が自分の Pleasanter API キーを登録する方式**とし、本人のキーで Pleasanter に接続します。登録したキーはラッパーのサーバー側で管理します。

## 現在の利用状況

**現在は開発初期段階で、コネクタとしてはまだ利用できません。** OAuth 認証、API キーの管理、Pleasanter への中継は未実装です。

導入手順、管理者による接続設定、Claude などでのコネクタ登録・サインイン手順は、実装と動作確認が完了した段階で利用者向け資料として追加します。

## ライセンス

**AGPL v3（AGPL-3.0-or-later）** で公開しています。[LICENSE](LICENSE) を参照してください。

## 関連資料

- [Pleasanter 本体](https://github.com/Implem/Implem.Pleasanter)
- [開発者向けガイド](_documents/開発ガイド.md)（ソースからの起動・テスト・開発運用）
