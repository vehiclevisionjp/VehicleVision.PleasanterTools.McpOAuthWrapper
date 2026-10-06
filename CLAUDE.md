# Claude Code 向け指示

@.github/copilot-instructions.md

現在の仕様と未決定事項は `_documents/アーキテクチャ方針.md`、運用は `_documents/開発運用.md` を読むこと。

Pleasanter 本体は `_reference/Implem.Pleasanter` の参照専用サブモジュールであり、実装へ取り込まない。外部メモリは現時点では使っていない。必要なら `.ClaudeMemory` 接尾辞の Internal リポジトリに共有し、正式な規約は共通指示へ残す。
