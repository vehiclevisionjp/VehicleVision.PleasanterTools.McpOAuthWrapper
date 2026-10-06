# VehicleVision.PleasanterTools.McpOAuthWrapper

Pleasanter の MCP サーバーを Claude などへ組織コネクタとして配布するための、薄い OAuth 認証ラッパーです。C#／.NET 10 で開発します。

**現在は開発環境の叩きです。OAuth 認証・API キーの管理・MCP 中継は未実装で、コネクタとしてはまだ接続できません。**

## 開発を始める

.NET 10 SDK と Git を用意します。通常のビルドでは Pleasanter 本体の取得は不要です。

```powershell
git clone https://github.com/vehiclevisionjp/VehicleVision.PleasanterTools.McpOAuthWrapper.git
cd VehicleVision.PleasanterTools.McpOAuthWrapper
dotnet restore --locked-mode
dotnet build --no-restore -c Release
dotnet test --no-build -c Release
dotnet run --project src/VehicleVision.PleasanterTools.McpOAuthWrapper --launch-profile http
```

`http://localhost:5154/health/live` は起動確認用です。`/mcp` の GET／POST／DELETE は未実装を示す HTTP 501 を返します。ヘルスチェックの成功はコネクタ対応の完了を示しません。

ソース調査が必要なときに参照用サブモジュールを取得します。

```powershell
git submodule update --init --depth 1
```

## 構成と運用

- `src/`：ASP.NET Core アプリ
- `tests/`：HTTP エンドポイントの起動・未実装応答のテスト
- `_reference/Implem.Pleasanter`：リリースタグへ固定した参照専用サブモジュール
- `_documents/`：アーキテクチャ方針と開発運用
- `.github/`：共通エージェント指示、CI、CodeQL、依存監査、PR チェック、参照更新
- `AGENTS.md`／`CLAUDE.md`：Codex／Claude Code の入口

開発は `develop`、リリース確定状態は `master` とします。今後の変更は Issue → `develop` から作業ブランチ → Draft PR の流れで進めます。詳細は [開発運用](_documents/開発運用.md) と [アーキテクチャ方針](_documents/アーキテクチャ方針.md) を参照してください。

外部メモリリポジトリは現時点では不要なため作成していません。必要になった場合は `VehicleVision.PleasanterTools.McpOAuthWrapper.ClaudeMemory` を **Internal** で作成し、公開リポジトリに組織情報や秘密を持ち込まない構成にします。

## ライセンス

ラッパーは Pleasanter 本体と同じ **AGPL v3** 系の **AGPL-3.0-or-later** です。[LICENSE](LICENSE) を参照してください。本体のコードは参照専用とし、ラッパーのビルド・配布物には含めません。

## 参考

- [Pleasanter 本体](https://github.com/Implem/Implem.Pleasanter)
- [運用・Actions の参考リポジトリ](https://github.com/vehiclevisionjp/VehicleVision.PleasanterTools.Questionnaire)
- [MCP Authorization 仕様](https://modelcontextprotocol.io/specification/2025-11-25/basic/authorization)

参照日：2026-10-06。
