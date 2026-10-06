# エージェント向けリポジトリ指示

このファイルは `AGENTS.md` 標準（<https://agents.md/>）を読むコーディングエージェント向けの指示です。
**主な想定はローカルで動かす Codex CLI などです。**

GitHub 上のエージェントは `.github/copilot-instructions.md` を直接読みます。
Claude Code はローカルで `CLAUDE.md` を読み、そこから同じ参照元を取り込みます。
**このファイルが要るのは、AGENTS.md 標準が include の仕組みを規定しておらず、
規約の本文が無いと届かないためです。**

**規約の全文は [`.github/copilot-instructions.md`](.github/copilot-instructions.md) にあります。
以下は同ファイルの「必ず守る規約」の写しです。作業前に参照元を必ず開いてください。**

<!-- この節は .github/copilot-instructions.md から生成される。ここを直接編集しないこと。
     編集は .github/copilot-instructions.md 側で行い、`scripts/sync-agents.sh` を実行する。 -->

## 必ず守る規約

1. 応答、Issue、PR、コミット、レビュー、コメントとドキュメントは日本語で書く。CLI の進行メッセージだけは英語でよい。
2. 作業ブランチは `develop` から分岐し、PR の base も `develop` とする。`master` はリリース確定用。
3. Issue を作ってから変更し、PR 本文に `Closes #N` を書く。初期リポジトリ作成のみ直接配置する。
4. 作業中の PR は Draft とし、タイトルに `WIP:` を付ける。Draft 解除はレビュー可能、WIP 除去はマージ可能の合図。
5. CI をスキップするコミット指定を使わない。実装変更後は Release ビルドと影響するテストを実行する。
6. Pleasanter 本体は AGPL v3 の参照専用サブモジュール。コピー・流用・リンク・プロジェクト参照せず、ビルドや配布物へ含めない。ラッパーは AGPL-3.0-or-later。
7. API キーはラッパーに保存・キャッシュせず、MCP 通信ごとに Users から読み取る。キーとパスワードを OAuth の状態、トークン、ログ、公開ファイル、クライアントへ出さない。OAuth トークンはクライアントへ発行する目的以外で公開しない。未検証を動作済みとして説明しない。
8. 作業前に [共通指示](.github/copilot-instructions.md) の全文を読む。外部仕様は一次情報で確認し、出典と参照日を残す。
9. README は利用者向けの取扱説明・製品説明の入口にする。ビルド・テスト・CI・エージェント運用など開発者向けの説明は [開発ガイド](_documents/開発ガイド.md) と開発運用に分離する。
