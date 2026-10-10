using Amazon;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Google.Cloud.SecretManager.V1;
using Oci.Common.Auth;
using Oci.SecretsService;
using Oci.SecretsService.Models;
using Oci.SecretsService.Requests;

namespace VehicleVision.PleasanterTools.McpOAuthWrapper.Bridge;

// 起動時にだけ取得する。証明書の秘密値を設定オブジェクトやローカルファイルへ戻さない。
internal static class CloudCertificateReader
{
    internal static async Task<byte[]> ReadAsync(CloudCertificateOptions options, CancellationToken cancellationToken)
    {
        Validate(options);
        switch (options.Provider.ToLowerInvariant())
        {
            case "azure":
                var id = new Uri(options.SecretId);
                var segments = id.AbsolutePath.Trim('/').Split('/');
                var azure = new SecretClient(new Uri(id.GetLeftPart(UriPartial.Authority)), new DefaultAzureCredential());
                var secret = await azure.GetSecretAsync(segments[1], segments.Length == 3 ? segments[2] : null, cancellationToken);
                return Convert.FromBase64String(secret.Value.Value);
            case "aws":
                using (var aws = new AmazonSecretsManagerClient(RegionEndpoint.GetBySystemName(options.Region)))
                {
                    var response = await aws.GetSecretValueAsync(new GetSecretValueRequest
                    {
                        SecretId = options.SecretId,
                        VersionId = string.IsNullOrWhiteSpace(options.Version) ? null : options.Version
                    }, cancellationToken);
                    return DecodeAws(response);
                }
            case "gcp":
                var gcp = await SecretManagerServiceClient.CreateAsync(cancellationToken);
                var payload = await gcp.AccessSecretVersionAsync(options.SecretId, cancellationToken);
                return payload.Payload.Data.ToByteArray();
            case "oci":
                IBasicAuthenticationDetailsProvider authentication = options.OciAuthentication switch
                {
                    "InstancePrincipal" => new InstancePrincipalsAuthenticationDetailsProvider(),
                    "ResourcePrincipal" => ResourcePrincipalAuthenticationDetailsProvider.GetProvider(),
                    "ConfigFile" => new ConfigFileAuthenticationDetailsProvider(options.OciConfigProfile),
                    _ => throw new InvalidOperationException("OCI の認証方式を確認してください。")
                };
                using (var oci = new SecretsClient(authentication))
                {
                    oci.SetRegion(options.Region);
                    var bundle = await oci.GetSecretBundle(new GetSecretBundleRequest
                    {
                        SecretId = options.SecretId,
                        VersionNumber = string.IsNullOrWhiteSpace(options.Version) ? null : long.Parse(options.Version)
                    }, cancellationToken: cancellationToken);
                    return DecodeOci(bundle.SecretBundle.SecretBundleContent);
                }
            default:
                throw new InvalidOperationException("クラウドの Provider は Azure、AWS、GCP、OCI のいずれかを指定してください。");
        }
    }

    internal static byte[] DecodeAws(GetSecretValueResponse response)
    {
        // SDK の SecretBinary は既に復号されたバイト列。Base64 の二重復号をしない。
        if (response.SecretBinary is not null) return response.SecretBinary.ToArray();
        return Convert.FromBase64String(response.SecretString ?? throw new InvalidOperationException("PFX がありません。"));
    }

    internal static byte[] DecodeOci(SecretBundleContentDetails content)
        => content is Base64SecretBundleContentDetails base64
            ? Convert.FromBase64String(base64.Content)
            : throw new InvalidOperationException("PFX の内容形式を確認してください。");

    internal static void Validate(CloudCertificateOptions options)
    {
        var provider = options.Provider.ToLowerInvariant();
        if (provider is not ("azure" or "aws" or "gcp" or "oci") || string.IsNullOrWhiteSpace(options.SecretId))
            throw new InvalidOperationException("クラウドの Provider と SecretId を確認してください。");
        if (provider == "azure")
        {
            if (!Uri.TryCreate(options.SecretId, UriKind.Absolute, out var uri) || uri.Scheme != "https"
                || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)
                || !uri.IsDefaultPort || !IsAzureVaultHost(uri.Host)
                || !System.Text.RegularExpressions.Regex.IsMatch(uri.AbsolutePath, "^/secrets/[A-Za-z0-9-]+(/[A-Za-z0-9]+)?$"))
                throw new InvalidOperationException("Azure は Key Vault の HTTPS Secret URI を指定してください。");
            if (!string.IsNullOrWhiteSpace(options.Version))
                throw new InvalidOperationException("Azure のバージョンは SecretId の URI に含めてください。");
        }
        if (provider == "gcp" && (!System.Text.RegularExpressions.Regex.IsMatch(options.SecretId,
            "^projects/[A-Za-z0-9:_-]+/secrets/[A-Za-z0-9_-]+/versions/[A-Za-z0-9_-]+$") || !string.IsNullOrWhiteSpace(options.Version)))
            throw new InvalidOperationException("GCP は projects/.../secrets/.../versions/... を SecretId に指定してください。");
        if (provider is "aws" or "oci" && !System.Text.RegularExpressions.Regex.IsMatch(options.Region, "^[a-z0-9-]+$"))
            throw new InvalidOperationException("クラウドの Region を指定してください。");
        if (provider == "oci" && (options.OciAuthentication is not ("InstancePrincipal" or "ResourcePrincipal" or "ConfigFile")
            || (!string.IsNullOrWhiteSpace(options.Version) && (!long.TryParse(options.Version, out var version) || version <= 0))))
            throw new InvalidOperationException("OCI の認証方式とバージョン番号を確認してください。");
    }

    private static bool IsAzureVaultHost(string host) => new[]
    {
        ".vault.azure.net", ".vault.azure.cn", ".vault.usgovcloudapi.net", ".vault.microsoftazure.de"
    }.Any(suffix => host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && host.Length > suffix.Length);
}
