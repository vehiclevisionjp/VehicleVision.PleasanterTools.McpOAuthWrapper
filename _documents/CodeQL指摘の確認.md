# CodeQL 指摘の確認

確認日：2026-10-06。対象は PR #7 の初回実装に対する4件の指摘。クエリ全体は無効化しない。

## cs/web/missing-token-validation（#1、#2）

対象は `/connect/register` と `/connect/token`。いずれもブラウザーのログイン Cookie による権限を使わないプロトコル用 API である。トークン交換の主体は OpenIddict が検証した認可コードまたはリフレッシュトークンであり、PKCE、クライアント、redirect URI、resource と対応付けられている。ブラウザーのログイン状態を作るエンドポイントではない。通常の画面用 CSRF トークンを要求すると OAuth クライアントからの交換ができない。

登録 API は明示的に有効化した場合のみ公開し、匿名の公開クライアントを管理者の完全一致 redirect URI 許可一覧に限定して登録する。Cookie から利用者の権限を引き継がず、登録自体で利用者のトークンを発行しない。JSON ボディーの API で、レート制限も適用する。

ブラウザー操作である `/account/login` と `/account/consent` には ValidateAntiForgeryToken を適用する。トークンなしのフォーム POST が 400、誤った PKCE とコード再利用が 400、未認証 MCP が 401 になるテストで、経路ごとの対策を確認した。2件は画面用 CSRF 対策の有無だけを見る検出による誤検知として記録する。

登録 API についても、許可外 redirect の 400、許可済み redirect の 201、利用者トークンと Cookie が発行されないこと、登録後も未認証 MCP が 401 になることをテストした。

## cs/user-controlled-bypass（#3、#4）

対象はログイン時の `decision == deny` と入力検証である。キャンセルは署名された認可リクエストを確認した後に access_denied を返し、認可コードを発行しない。不正な操作値や入力長ではエラー画面を返し、認証後の同意チケットを作らない。

許可へ進むには DB のパスワード比較、アカウント状態、選択したキー所有者の検証が必要である。認可コード発行時にも、暗号化チケットとリクエストの対応、DB の一度だけの nonce 消費、Users の最新状態を再検証する。入力で認証を省略する分岐はない。

deny／approve／unknown をログイン POST の操作値に送っても、誤ったパスワードから同意チケット・認可コードを得られないことを3件の回帰テストで確認した。2件は早期拒否を認証の迂回と解釈した誤検知として記録する。

## 処理状況

初回の個別処理は、明示承認のないセキュリティ記録の恒久的変更として自動承認レビューに拒否された。2026-10-06 に利用者が CodeQL の作業を明示承認したため、アラート #1～#4 を個別に false positive として処理し、各アラートに上記の根拠と承認日を記録した。クエリ・CodeQL ワークフローは無効化していない。

## 一次情報

- [CodeQL の CSRF クエリ](https://codeql.github.com/codeql-query-help/csharp/cs-web-missing-token-validation/)
- [CodeQL の認証迂回クエリ](https://codeql.github.com/codeql-query-help/csharp/cs-user-controlled-bypass/)
- [MCP Authorization](https://modelcontextprotocol.io/specification/2025-11-25/basic/authorization)
- [OpenIddict ASP.NET Core](https://documentation.openiddict.com/integrations/aspnet-core)
