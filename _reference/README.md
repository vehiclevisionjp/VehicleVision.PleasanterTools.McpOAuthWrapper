# Pleasanter 参照ソース

Pleasanter 本体は AI（Claude・Copilot・Codex）が API 仕様・スキーマ・挙動を事実確認するための参照専用ソースです。
Git では追跡せず、調査前に毎回、上流 `main` の先頭を取得します。CI・CodeQL・配布物は本体を取得しません。

- 上流: <https://github.com/Implem/Implem.Pleasanter>
- 既定の取得先: `_reference/Implem.Pleasanter/`（Git 管理外）
- 共有取得先: 環境変数 `PLEASANTER_REFERENCE_DIR`（その場所を直接読む。リンクは作らない）
- この `README.md` は引き続き Git で追跡する

## 厳守事項

本体は AGPL v3、ラッパーは AGPL-3.0-or-later と商用ライセンスのデュアルライセンス（草案。[LICENSING.md](../LICENSING.md)）です。
薄い独立したラッパーを保つため、本体コードのコピー・流用・リンク・プロジェクト参照は行いません。
本体のコードを取り込むと、商用ライセンスを提供できなくなります。
取得先を編集せず、ビルド・ソリューション・配布物へ含めません。
本体の MCP を中継し Users を読み取り専用で照会するアプリ固有の構成は変えません。
実機未検証の結果を動作確認済みと書かないでください。

## 取得と更新

Git と bash が必要です。Windows では Git Bash を使い、PowerShell からも呼び出せます。
本アプリには `references.json` を置かず、本体 `main` 1 件だけを取得します。

```powershell
bash scripts/sync-reference.sh

# worktree 間で共有する場合（絶対パスを推奨）
$env:PLEASANTER_REFERENCE_DIR = 'D:\references\Implem.Pleasanter'
bash scripts/sync-reference.sh
Remove-Item Env:PLEASANTER_REFERENCE_DIR
```

未取得なら `git clone --depth 1` で取得し、既存なら `git fetch --depth 1 origin main` と
`git checkout --detach FETCH_HEAD` で更新します。
成功時は Path・短い SHA・コミット日時・Status に加え、完全な FullSHA・取得日時（UTC）・
csproj の Version を出力します。Version が読めなければ警告と `Version=unknown` を出します。

変更済み・ステージ済み・未追跡ファイルがある作業ツリー、異なる origin、
別リポジトリや非空の取得先では停止します。通信失敗は失敗コードで終了し、既存の HEAD とファイルを維持します。
新規 clone は一時取得先で成功してから配置します。並行取得はロックで停止します。
異常終了で `.sync-lock` が残った場合は、実行中の同期が無いことを確認してから空ディレクトリを取り除きます。

`PLEASANTER_REFERENCE_DIR` の相対パスはリポジトリ直下を基準にします。
共有先は直接読み、`_reference/Implem.Pleasanter` にジャンクションやシンボリックリンクを作りません。
共有先の更新中に他の AI が読まないよう調整してください。

既存 clone の参照ソースや未コミット変更は移行時にも削除しません。
古い submodule 形式の取得先が更新できない場合も、`.git/modules` の削除や deinit は行わず、
別のクリーンな共有先を指定してください。日次更新・ポインタ更新 PR は廃止し、
既存の参照更新ブランチ・Draft PR・Issue は別作業で整理します。

## 事実の記録と再現性

再現性は gitlink ではなく、**確認した完全な SHA・ソースへの固定リンク・参照日**で保ちます。
現在の取得先が同じ SHA とは限りません。過去の記述を最新ソースの根拠として扱わず、
再確認していない過去の SHA・参照日を最新値に置き換えないでください。
初期調査の `Pleasanter_1.5.8.1`（当時の記録: `626a173`）や実機検証の履歴はそのまま残します。

`main` はリリース前の変更を含みます。版の説明には確認した SHA と
`Implem.Pleasanter/Implem.Pleasanter.csproj` の Version を使い、
リリース版との対応が必要なら `Pleasanter_*` タグで確かめます。
MCP の認証・トランスポート仕様を新しい事実として記す場合は、取得したソースで確認し直してください。
実際の応答・運用権限はソース確認だけでは保証せず、実機で確認します。

## 採用元

2026-10-27 参照。以下の Template 承認済みコミットの配布手順とスクリプトを採用しました。
アプリ固有のライセンス・認証・DB・KVS・画面・配布方式は維持します。

- コミット: `3312f003fbe9827090e20bc643800824bdf30e22`
- [配布手順][distribution]
- [取得スクリプト][sync]
- [ネットワーク不要の回帰テスト][test]

[distribution]: https://github.com/vehiclevisionjp/VehicleVision.PleasanterTools.Template/blob/3312f003fbe9827090e20bc643800824bdf30e22/_documents/参照ソースのオンデマンド取得への配布手順.md
[sync]: https://github.com/vehiclevisionjp/VehicleVision.PleasanterTools.Template/blob/3312f003fbe9827090e20bc643800824bdf30e22/scripts/sync-reference.sh
[test]: https://github.com/vehiclevisionjp/VehicleVision.PleasanterTools.Template/blob/3312f003fbe9827090e20bc643800824bdf30e22/scripts/test-sync-reference.sh
