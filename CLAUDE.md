# Claude Code 向け指示

@.github/copilot-instructions.md

現在の仕様と未決定事項は `_documents/アーキテクチャ方針.md`、運用は `_documents/開発運用.md` を読むこと。

Pleasanter 本体は Git で追跡しない参照専用ソースであり、実装へ取り込まない。
挙動を確かめる前に毎回 `bash scripts/sync-reference.sh` で上流 `main` の先頭を取得する。
既定先は `_reference/Implem.Pleasanter`。`PLEASANTER_REFERENCE_DIR` があれば、その場所を直接読む。
CI では取得せず、文書には確認した完全な SHA・固定リンク・参照日を残す。実機未検証なら明記する。

外部メモリは現時点では使っていない。
必要なら `.ClaudeMemory` 接尾辞の Internal リポジトリに共有し、正式な規約は共通指示へ残す。
