# Pleasanter 参照ソース

`Implem.Pleasanter` は <https://github.com/Implem/Implem.Pleasanter> の Git サブモジュールです。初期調査は `Pleasanter_1.5.8.1`（`626a173`）で実施しました。現在の固定位置は `git submodule status` を正としてください。

本体とラッパーはともに AGPL v3 系です。このサブモジュールは API の事実確認に使う参照専用です。薄い独立したラッパーを保つため、本体コードのコピー・流用・リンク・プロジェクト参照は行いません。CI・CodeQL・配布物はこのフォルダを取得しません。

上流の `main` の先頭を追います（2026-10-19 に、リリースタグへの固定から変更）。日次 Actions が先頭との差を検出して Issue と Draft PR を作り、開いている PR があれば同じ PR へコミットを足します。`main` はリリース前の変更を含むため、「どの版か」は確認したコミットと `<Version>`（`Implem.Pleasanter/Implem.Pleasanter.csproj`）で言います。リリース版との対応は `Pleasanter_*` タグで確かめます。更新時は MCP の認証・トランスポート仕様を新しいソースで確認し直してください。

クローンごとに `git config submodule.recurse true` を設定してください。追従 PR のマージで親のポインタが進んでも、これが無いと手元のサブモジュールの中身は古いまま残り、`pull` のあとに差分として見えます。`.gitmodules` の `ignore = all` で隠さないでください（中身が古いまま残ります）。
