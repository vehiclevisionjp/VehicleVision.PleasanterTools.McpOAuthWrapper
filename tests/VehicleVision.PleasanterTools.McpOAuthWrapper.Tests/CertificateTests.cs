using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Server;
using VehicleVision.PleasanterTools.McpOAuthWrapper.Bridge;

namespace VehicleVision.PleasanterTools.McpOAuthWrapper.Tests;

public sealed class CertificateTests
{
    [Theory]
    [InlineData("")]
    [InlineData("test-password")]
    public void Base64のPFXを秘密鍵付きで読み込み署名と暗号化ができる(string password)
    {
        using var original = CreateCertificate();
        var base64 = Convert.ToBase64String(original.Export(X509ContentType.Pfx, password));
        using var actual = BridgeSetup.LoadCertificate("", base64, password, ".", "署名用");
        Assert.Equal(original.Thumbprint, actual.Thumbprint);
        using var rsa = actual.GetRSAPrivateKey()!;
        var data = Encoding.UTF8.GetBytes("test");
        var signature = rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        Assert.True(rsa.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        Assert.Equal(data, rsa.Decrypt(rsa.Encrypt(data, RSAEncryptionPadding.OaepSHA256), RSAEncryptionPadding.OaepSHA256));
    }

    [Fact]
    public void 既存の相対パスとパスワード付きPFXも読み込める()
    {
        InTemporaryDirectory(root =>
        {
            using var original = CreateCertificate();
            File.WriteAllBytes(Path.Combine(root, "test.pfx"), original.Export(X509ContentType.Pfx, "test-password"));
            using var actual = BridgeSetup.LoadCertificate("test.pfx", "", "test-password", root, "暗号化用");
            Assert.Equal(original.Thumbprint, actual.Thumbprint);
            Assert.True(actual.HasPrivateKey);
        });
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("existing.pfx", "also-set")]
    public void PFX入力の未指定と二重入力を直接ローダーで拒否する(string path, string base64)
    {
        var error = Assert.Throws<InvalidOperationException>(() => BridgeSetup.LoadCertificate(path, base64, "", ".", "署名用"));
        Assert.Contains("どちらか一方", error.Message);
    }

    [Theory]
    [InlineData("@Microsoft.KeyVault(SecretUri=https://example.vault.azure.net/secrets/signing)")]
    [InlineData("not-a-pfx-secret")]
    [InlineData("bm90IGEgcGZ4")]
    public void 解決失敗や不正なシークレットを秘密値を含めず拒否する(string secret)
    {
        var error = Assert.Throws<InvalidOperationException>(() => BridgeSetup.LoadCertificate("", secret, "private-password", ".", "署名用"));
        Assert.Contains("Key Vault", error.Message);
        Assert.DoesNotContain(secret, error.ToString());
        Assert.DoesNotContain("private-password", error.ToString());
        Assert.Null(error.InnerException);
    }

    [Fact]
    public void 間違ったパスワードを秘密値を含めず拒否する()
    {
        using var original = CreateCertificate();
        var secret = Convert.ToBase64String(original.Export(X509ContentType.Pfx, "correct-password"));
        var error = Assert.Throws<InvalidOperationException>(() => BridgeSetup.LoadCertificate("", secret, "wrong-password", ".", "暗号化用"));
        Assert.DoesNotContain(secret, error.ToString());
        Assert.DoesNotContain("wrong-password", error.ToString());
        Assert.Null(error.InnerException);
    }

    [Theory]
    [InlineData(-2, -1, true)]
    [InlineData(1, 2, true)]
    [InlineData(-1, 1, false)]
    public void 有効期間外や秘密鍵のない証明書を拒否する(int startDays, int endDays, bool includePrivateKey)
    {
        using var original = CreateCertificate(startDays, endDays);
        using var publicOnly = X509CertificateLoader.LoadCertificate(original.Export(X509ContentType.Cert));
        var bytes = (includePrivateKey ? original : publicOnly).Export(X509ContentType.Pfx, "");
        var error = Assert.Throws<InvalidOperationException>(() => BridgeSetup.LoadCertificate("", Convert.ToBase64String(bytes), "", ".", "署名用"));
        Assert.Contains("秘密鍵と有効期限", error.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Productionで証明書を登録し再起動後も状態を復号できる(bool automatic)
    {
        var root = Path.Combine(Path.GetTempPath(), "mcp-certificates-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "App_Data", "Parameters"));
        try
        {
            File.WriteAllText(Path.Combine(root, "App_Data", "Parameters", "Rds.json"),
                """{"Dbms":"PostgreSQL","Provider":"Local","UserConnectionString":"test-only"}""");
            using var signing = CreateCertificate();
            using var encryption = CreateCertificate();
            // App Service が Key Vault 参照を解決した後の設定値をバインドする。
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SigningCertificateBase64"] = Convert.ToBase64String(signing.Export(X509ContentType.Pfx, "")),
                ["EncryptionCertificateBase64"] = Convert.ToBase64String(encryption.Export(X509ContentType.Pfx, ""))
            }).Build();
            var options = configuration.Get<BridgeOptions>()!;
            if (automatic)
            {
                options.SigningCertificateBase64 = "";
                options.EncryptionCertificateBase64 = "";
            }
            options.Issuer = "https://mcp.example/";
            options.PleasanterUrl = "https://pleasanter.example/";
            options.StateDirectory = Path.Combine(root, "state");
            options.Clients = [new OAuthClient { ClientId = "test", DisplayName = "test", RedirectUris = ["https://client.example/callback"] }];
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = root, EnvironmentName = "Production" });
            builder.AddBridge(options);
            await using var app = builder.Build();
            var server = app.Services.GetRequiredService<IOptions<OpenIddictServerOptions>>().Value;
            if (!automatic)
            {
                Assert.Contains(server.SigningCredentials, credential =>
                    credential.Key is X509SecurityKey key && key.Certificate.Thumbprint == signing.Thumbprint);
                Assert.Contains(server.EncryptionCredentials, credential =>
                    credential.Key is X509SecurityKey key && key.Certificate.Thumbprint == encryption.Thumbprint);
            }
            var protector = app.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("certificate-test");
            var protectedState = protector.Protect("state-value");
            Assert.Equal("state-value", protector.Unprotect(protectedState));
            var keyFiles = Directory.GetFiles(Path.Combine(root, "state", "keys"), "*.xml");
            Assert.NotEmpty(keyFiles);
            Assert.All(keyFiles, file => Assert.Contains("encryptedSecret", File.ReadAllText(file)));
            Assert.Equal(automatic ? 2 : 0, Directory.GetFiles(root, "*.pfx", SearchOption.AllDirectories).Length);
            // 再起動を想定した独立ホストでも、保存済みの鍵を復号できる。
            var restart = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = root, EnvironmentName = "Production" });
            restart.AddBridge(options);
            await using var restartedApp = restart.Build();
            var restartedProtector = restartedApp.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("certificate-test");
            Assert.Equal("state-value", restartedProtector.Unprotect(protectedState));
            if (automatic)
            {
                // 証明書だけを失うと、状態を残していても旧 Data Protection 鍵を復号できない。
                var path = Path.Combine(root, "state", "certificates", "encryption.pfx");
                using var previous = X509CertificateLoader.LoadPkcs12FromFile(path, "", X509KeyStorageFlags.EphemeralKeySet);
                File.Delete(path);
                var afterDeletion = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = root, EnvironmentName = "Production" });
                afterDeletion.AddBridge(options);
                await using var replacementApp = afterDeletion.Build();
                using var replacement = X509CertificateLoader.LoadPkcs12FromFile(path, "", X509KeyStorageFlags.EphemeralKeySet);
                Assert.NotEqual(previous.Thumbprint, replacement.Thumbprint);
                var replacementProtector = replacementApp.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("certificate-test");
                Assert.Throws<CryptographicException>(() => replacementProtector.Unprotect(protectedState));
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static X509Certificate2 CreateCertificate(int startDays = -1, int endDays = 1)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=Test only", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(startDays), DateTimeOffset.UtcNow.AddDays(endDays));
    }

    private static void InTemporaryDirectory(Action<string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "mcp-certificates-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { check(root); }
        finally { Directory.Delete(root, recursive: true); }
    }
}
