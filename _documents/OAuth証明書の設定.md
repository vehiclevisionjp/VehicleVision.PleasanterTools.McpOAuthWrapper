# OAuth 証明書の設定

署名用と暗号化用の証明書を用途ごとに選びます。両用途で同じ証明書を使うことはできますが、別々の RSA 証明書を推奨します。公開 HTTPS の証明書は終端プロキシで設定します。

| 方式 | 設定 | 配置条件 |
| --- | --- | --- |
| 自動生成 | 対象用途の Path／Base64／Thumbprint／Cloud を未指定 | 永続化した StateDirectory が必要 |
| ファイル | SigningCertificatePath／EncryptionCertificatePath | 秘密鍵付き PFX のパス |
| Base64 | SigningCertificateBase64／EncryptionCertificateBase64 | PFX 全体を Base64 にした秘密設定 |
| OS ストア | SigningCertificateThumbprint／EncryptionCertificateThumbprint | Windows の CurrentUser または LocalMachine ストア |
| クラウド | SigningCertificateCloud／EncryptionCertificateCloud | Azure／AWS／GCP／OCI のシークレットと取得権限 |

用途ごとに方式を一つだけ設定します。二重指定、取得失敗、不正な PFX、秘密鍵の欠落、有効期間外、RSA 2048 ビット未満、Key Usage の不一致、秘密鍵の利用権限不足は起動エラーになります。自己署名証明書を使えるため、CA の発行やルート証明書の配布は不要です。

## 自動生成

未指定の用途では、有効期限10年の自己署名 RSA 4096 ビット証明書を初回起動時に生成します。署名用は DigitalSignature、暗号化用は KeyEncipherment とし、StateDirectory/certificates/signing.pfx と encryption.pfx に保存します。再起動時は保存済みの PFX を読み込み、期限切れや破損時に上書きしません。同時起動でも先に保存された証明書を使います。

新規に作る certificates ディレクトリーは、Windows では実行アカウントだけに継承付きアクセス権を付け、Linux ではディレクトリーを700、PFXを600で作成します。既存ディレクトリーの権限は変更しないため、配置側でアクセスを制限してください。CertificatePassword を設定した場合はその値で PFX を保護します。未指定の場合はパスワードなしとなるため、ファイル権限とバックアップの保護が必要です。再起動時にパスワードを変えると既存 PFX を読み込めません。

配布物の更新で上書きされない StateDirectory を設定し、証明書と OAuth の状態を一緒に保持してください。Redis の複数台構成では証明書ディレクトリーを共有するか、他の方式で同じ証明書を全台へ供給します。証明書を削除して再生成すると、発行済みトークンや保護済み状態を利用できなくなる場合があります。

### 自動生成した証明書を削除した場合

PFX ファイルが存在しない場合は、次回起動時にその用途の証明書を再生成します。ファイル名は同じでも秘密鍵・公開鍵・拇印は別のものになり、元の証明書では保護された状態を引き継げません。**証明書の削除を更新手順として使わないでください。** 起動中に削除した場合はメモリ上の証明書を使い続けるため、影響が再起動まで表れないことがあります。

| 削除したファイル | 再生成による影響 |
| --- | --- |
| signing.pfx | 旧署名鍵を使ったトークン等の署名を検証できなくなり、発行済みの認可コードやアクセス／更新トークンが拒否される可能性があります。利用者は再認可が必要になります |
| encryption.pfx | 旧暗号化鍵で保護したトークン等と、保存済みの Data Protection 鍵を復号できなくなります。同意画面、CSRF 検証、MCP セッションなども正常に処理できなくなる可能性があり、再認可だけで復旧できるとは限りません |
| 両方 | 上記の両方が発生します。oauth.db や Redis のデータが残っていても、旧証明書がなければ保護された内容を復号できません |

複数台構成で一台だけ別の証明書を生成すると、接続先の台によって成功・失敗が変わります。共有ディレクトリーから削除した場合も、既に起動中の台と再起動した台が異なる鍵を使うため、全台の証明書を揃えて復旧する必要があります。

誤って削除した場合は影響する全インスタンスを停止し、再生成された PFX を退避したうえで、元の PFX を保護したバックアップから同じパスへ復元します。元の CertificatePassword と、その証明書に対応する OAuth 状態・Data Protection 鍵も保持してください。別のタイミングで再生成や状態更新が進んでいる場合は、対応するバックアップ一式からの復元を検討します。

元の証明書を復元できない場合、旧鍵で暗号化された内容は復旧できません。影響する OAuth 状態と Data Protection 鍵を管理者が計画的に初期化し、全台の新しい証明書を揃え、利用者の再接続・再認可と必要なクライアントの再登録を行います。初期化は既存の認可等を失う操作です。アプリは証明書の再生成時に OAuth 状態を自動削除しません。

## ファイルと Base64

ファイルは [導入手順の手動生成](導入手順.md#oauth-用証明書を生成する) に従います。相対パスはアプリのコンテンツルートを基準に解決します。

Base64 は PFX 全体をエンコードした値です。環境変数 MCP_GENERAL_SigningCertificateBase64／MCP_GENERAL_EncryptionCertificateBase64 や配置先の秘密設定から供給し、公開設定例へ値を書かないでください。証明書はメモリ上で読み込み、秘密鍵をファイルや OS ストアへ書き出しません。CertificatePassword は両用途共通です。

## Windows 証明書ストア

次はマシンの個人ストアから読み込む例です。拇印は実際の40桁の値に置き換えます。

```json
{
  "SigningCertificateThumbprint": "<署名証明書の拇印>",
  "EncryptionCertificateThumbprint": "<暗号化証明書の拇印>",
  "CertificateStoreName": "My",
  "CertificateStoreLocation": "LocalMachine"
}
```

既定は My／CurrentUser です。IIS では LocalMachine を選び、アプリケーションプールの実行アカウントへ秘密鍵の読み取り・利用権限を付けます。秘密鍵はエクスポート不要で、OS の暗号プロバイダー経由で署名・復号します。Linux の OS キーストアはこの方式の対象外です。

## クラウドからの直接取得

各社の公式 SDK を使い、起動時に PFX を読み込みます。取得した PFX はローカルへ保存しません。証明書はプロセス内で再利用し、クラウドのバージョン変更は再起動時に反映します。取得に失敗した場合は起動を拒否します。非エクスポート鍵や HSM への署名・復号の委譲には対応していません。

SigningCertificateCloud と EncryptionCertificateCloud に同じ形式の設定を指定します。環境変数で指定する場合は MCP_GENERAL_SigningCertificateCloud__Provider、MCP_GENERAL_SigningCertificateCloud__SecretId のように階層を二重のアンダースコアで区切ります。

### Azure Key Vault

Key Vault のエクスポート可能な RSA 証明書に付随する Secret、または Base64 PFX を値に持つ Secret を指定します。証明書の公開部分を返す /certificates/ の URI ではなく /secrets/ の URI を使います。

```json
{
  "SigningCertificateCloud": {
    "Provider": "Azure",
    "SecretId": "https://<vault>.vault.azure.net/secrets/mcp-oauth-signing/<version>"
  },
  "EncryptionCertificateCloud": {
    "Provider": "Azure",
    "SecretId": "https://<vault>.vault.azure.net/secrets/mcp-oauth-encryption/<version>"
  }
}
```

DefaultAzureCredential の認証チェーンを使います。本番では配置先のマネージド ID または Workload Identity に secrets/get 権限（RBAC では Key Vault Secrets User）を付け、ネットワーク接続を許可します。開発では Azure CLI などの認証を使えます。Key Vault 証明書の PFX のパスワードは空です。通常の Secret に自分のパスワード付き PFX を置く場合は CertificatePassword を設定します。URI のバージョンを省略すると最新を取得します。App Service の Key Vault 参照を既存の Base64 設定へ渡す方式も引き続き利用できます。

### AWS Secrets Manager

SecretBinary に PFX のバイト列を保存するか、SecretString に PFX 全体の Base64 文字列を保存します。SecretString の JSON オブジェクトには対応していません。

```json
{
  "SigningCertificateCloud": {
    "Provider": "AWS",
    "SecretId": "<署名用シークレットの完全な ARN>",
    "Region": "ap-northeast-1",
    "Version": "<version-id>"
  },
  "EncryptionCertificateCloud": {
    "Provider": "AWS",
    "SecretId": "<暗号化用シークレットの完全な ARN>",
    "Region": "ap-northeast-1",
    "Version": "<version-id>"
  }
}
```

AWS SDK の標準認証チェーンを使います。本番では EC2／ECS の IAM ロール、EKS の Web Identity などの一時資格情報を使い、対象シークレットへの secretsmanager:GetSecretValue を許可します。カスタマー管理 KMS キーを使う場合は kms:Decrypt も必要です。Version を空にすると AWSCURRENT を取得します。パスワード付き PFX の場合は CertificatePassword を指定します。

### GCP Secret Manager

Secret のペイロードへ PFX のバイト列を保存します。Base64 の文字列ではありません。

```json
{
  "SigningCertificateCloud": {
    "Provider": "GCP",
    "SecretId": "projects/<project>/secrets/mcp-oauth-signing/versions/1"
  },
  "EncryptionCertificateCloud": {
    "Provider": "GCP",
    "SecretId": "projects/<project>/secrets/mcp-oauth-encryption/versions/1"
  }
}
```

Application Default Credentials を使います。本番では配置先のサービスアカウントや Workload Identity を使い、対象 Secret に roles/secretmanager.secretAccessor を付けます。Secret Manager API とネットワーク接続を有効にします。Version フィールドは空にし、SecretId 内でバージョンを指定します。latest も利用できますが、段階的な更新には番号で固定します。パスワード付き PFX の場合は CertificatePassword を指定します。

### OCI Vault Secrets

PFX のバイト列を Base64 にして Secret に保存し、シークレット OCID を指定します。

```json
{
  "SigningCertificateCloud": {
    "Provider": "OCI",
    "SecretId": "<署名用シークレットの OCID>",
    "Region": "ap-tokyo-1",
    "Version": "1",
    "OciAuthentication": "InstancePrincipal"
  },
  "EncryptionCertificateCloud": {
    "Provider": "OCI",
    "SecretId": "<暗号化用シークレットの OCID>",
    "Region": "ap-tokyo-1",
    "Version": "1",
    "OciAuthentication": "InstancePrincipal"
  }
}
```

Compute 上では既定の InstancePrincipal、対応するリソース上では ResourcePrincipal を使います。対象リソースを動的グループへ所属させ、対象 Secret の secret-bundles を読むポリシーを設定します。開発端末では ConfigFile を選び、OCI SDK 標準の設定ファイルと OciConfigProfile（既定 DEFAULT）を使えます。API 署名キーはラッパーの設定に保存しません。Version は正の整数のバージョン番号で、空の場合は CURRENT を取得します。パスワード付き PFX の場合は CertificatePassword を指定します。

## 有効期限と交換

10年は自動生成証明書の有効期限で、トークンの有効期限を延ばすものではありません。期限切れ前や漏えい時には証明書を交換してください。複数台では同じ世代を使い、Secret の自動更新によって台ごとに異なる鍵を取得しないようにバージョンを固定します。

現在は各用途につき一世代を読み込み、複数世代を同時に登録するローテーションには対応していません。暗号化用証明書は保存済み Data Protection 鍵の復号にも必要です。単純な差し替えで既存の認可・トークン・セッションが利用できなくなる可能性があるため、旧証明書と状態をバックアップし、再認可を伴う更新として計画してください。

## 確認状況と出典

自動生成・再読込・状態の復号、入力の排他、クラウド取得後の PFX 読み込みと秘密値の非公開は自動テストで確認します。各クラウド実環境の認証・IAM・ネットワークを通す試験は未実施です。Windows ストアの非エクスポート鍵は Windows のテスト対象です。

参照日：2026-10-10。

- [OpenIddict の署名・暗号化証明書](https://documentation.openiddict.com/configuration/encryption-and-signing-credentials.html)
- [Azure Key Vault の証明書エクスポート](https://learn.microsoft.com/en-us/azure/key-vault/certificates/how-to-export-certificate)
- [DefaultAzureCredential](https://learn.microsoft.com/en-us/dotnet/api/azure.identity.defaultazurecredential)
- [AWS Secret 取得](https://docs.aws.amazon.com/secretsmanager/latest/userguide/retrieving-secrets-net-sdk.html)、[認証チェーン](https://docs.aws.amazon.com/sdk-for-net/v4/developer-guide/creds-assign.html)
- [GCP Secret バージョンの取得](https://cloud.google.com/secret-manager/docs/access-secret-version)
- [OCI .NET SDK](https://docs.oracle.com/en-us/iaas/Content/API/SDKDocs/dotnetsdk.htm)、[Resource Principal](https://docs.oracle.com/en-us/iaas/tools/dotnet/latest/api/Oci.Common.Auth.ResourcePrincipalAuthenticationDetailsProvider.html)
