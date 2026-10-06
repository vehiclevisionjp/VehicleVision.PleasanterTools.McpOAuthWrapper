# Pleasanter 参照ソース

`Implem.Pleasanter` は https://github.com/Implem/Implem.Pleasanter の Git サブモジュールです。初期調査は `Pleasanter_1.5.8.1`（`626a173`）で実施しました。現在の固定位置は `git submodule status` を正としてください。

本体とラッパーはともに AGPL v3 系です。このサブモジュールは API の事実確認に使う参照専用です。薄い独立したラッパーを保つため、本体コードのコピー・流用・リンク・プロジェクト参照は行いません。CI・CodeQL・配布物はこのフォルダを取得しません。

日次 Actions が最新リリースタグとの差を検出して Issue と Draft PR を作ります。更新時は MCP の認証・トランスポート仕様を新しいソースで確認し直してください。
