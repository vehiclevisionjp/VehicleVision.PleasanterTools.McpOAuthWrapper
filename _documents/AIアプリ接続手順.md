# Claude・ChatGPT への接続

管理者がラッパーを導入した後、MCP 接続先として `https://<ラッパーのホスト名>/mcp` を登録します。Pleasanter の URL ではなく、ラッパーの URL を使います。API キーはラッパーのログイン画面だけに入力します。

## 管理者の準備

HTTPS でアクセスできるラッパーと、Pleasanter で発行済みの API キーが必要です。Claude のリモート接続は Anthropic のサーバーから行われます。利用者のパソコンからアクセスできるだけでは接続できません。[Claude のネットワーク要件](https://support.claude.com/en/articles/11175166-get-started-with-custom-connectors-using-remote-mcp)

この版は動的クライアント登録（DCR）と事前登録した公開クライアントに対応します。Client ID Metadata Documents（CIMD）とクライアントシークレットを使う認証には対応しません。Claude では「Register automatically」を選び、ChatGPT でも DCR を使用します。CIMD を選択しないでください。

DCR を使う場合、管理者は General.json の AllowDynamicClientRegistration を true にし、AllowedRedirectUris にクライアントの管理画面が示す戻り先 URI を完全一致で登録します。URI はクライアント・接続設定によって異なるので、ホスト名だけの許可や推測した URI を使わないでください。ChatGPT では固定の URI または接続ごとの callback ID を含む URI が使われます。[OpenAI の認証・戻り先設定](https://developers.openai.com/plugins/build/auth)

```json
{
  "AllowDynamicClientRegistration": true,
  "AllowedRedirectUris": [
    "https://<管理画面に表示された正確な戻り先>"
  ]
}
```

この抜粋は既存の設定へ追加する項目です。プレースホルダーを実際の URI に置き換え、ラッパーを再起動します。接続先や証明書などの設定は [導入手順](導入手順.md) を参照してください。接続時に登録を拒否された場合は、管理画面の戻り先と許可一覧を確認します。

## Claude

Team／Enterprise では組織管理者がコネクタを追加し、各利用者が自分の認証を行います。

1. 管理者が Organization settings → Connectors → Add → Custom → Web を開きます。
2. 名前（例：Pleasanter）とラッパーの `/mcp` URL を入力します。
3. 認証を OAuth にし、サインインを要求する設定を選びます。OAuth client は **Register automatically** を選択します。Request headers に API キーを登録する必要はありません。
4. Add で組織へ登録します。
5. 利用者が Customize → Connectors で対象コネクタの Connect を押します。
6. ラッパーの画面で API キーを入力し、アプリ・接続先・使用する権限を確認して接続を許可します。
7. 会話の「＋」→ Connectors で対象を有効にします。

個人プランでは Customize → Connectors → Add custom connector から追加します。契約プランや組織権限によって表示は異なります。[Claude 公式手順](https://support.claude.com/en/articles/11175166-get-started-with-custom-connectors-using-remote-mcp)

## ChatGPT

1. ChatGPT の Plugins ページで「＋」→ Add custom MCP server を開きます。
2. 名前とラッパーの `/mcp` URL を入力し、認証方式に **OAuth** を選択します。登録方式は DCR を使います。
3. 注意事項を確認して Create as a plugin を選びます。
4. OAuth の画面へ進み、ラッパーへ API キーを入力して接続を許可します。
5. 作成したプラグインを自分または対象ワークスペースへインストールします。会話で `@` から選び、最初は読み取り操作で確認します。

移行前の画面では Settings／Workspace settings → Apps → Create から登録し、Scan Tools → Create、組織管理者の Publish で展開します。契約・組織権限・画面の提供状況によって操作名が異なります。操作画面が示す OAuth の戻り先をラッパーの許可一覧に登録してください。[OpenAI 公式の MCP 接続手順](https://developers.openai.com/api/docs/guides/custom-mcp-server)

`mcp` は Pleasanter MCP の利用、`offline_access` はリフレッシュトークンによる接続の更新に使います。API キーを ChatGPT のクライアントシークレット欄に入力しないでください。

## 接続後の確認と解除

「Pleasanter の利用できるサイト一覧を取得して」のような読み取り操作から始めます。操作できる範囲は、同意画面で選んだ Pleasanter アカウントの権限に従います。書き込み操作は AI アプリが示す対象・入力を確認して実行してください。

接続を解除する場合は Claude／ChatGPT のコネクタ・プラグイン設定から切断します。API キーを再発行・削除した場合は新しいキーで再接続します。画面の流れは [利用ガイド](利用ガイド.md) にあります。

本手順は公式資料を確認して記載しました。Claude・ChatGPT の実アカウントを使った組織配布と接続試験は未実施です。参照日：2026-10-06。
