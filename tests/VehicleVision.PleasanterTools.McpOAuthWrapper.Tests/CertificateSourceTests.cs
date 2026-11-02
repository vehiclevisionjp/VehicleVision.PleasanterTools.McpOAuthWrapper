using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using Amazon.SecretsManager.Model;
using Oci.SecretsService.Models;
using VehicleVision.PleasanterTools.McpOAuthWrapper.Bridge;

namespace VehicleVision.PleasanterTools.McpOAuthWrapper.Tests;

public sealed class CertificateSourceTests
{
    [Fact]
    public async Task 同時生成と再読込で同じ10年証明書を使用する()
    {
        var root = CreateRoot();
        try
        {
            var options = new BridgeOptions { StateDirectory = "state", CertificatePassword = "test-password" };
            var certificates = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Task.Run(() => CertificateLoader.Load(options, root, true))));
            try
            {
                Assert.Single(certificates.Select(certificate => certificate.Thumbprint).Distinct());
                using var encryption = CertificateLoader.Load(options, root, false);
                using var restarted = CertificateLoader.Load(options, root, true);
                Assert.Equal(certificates[0].Thumbprint, restarted.Thumbprint);
                Assert.NotEqual(encryption.Thumbprint, restarted.Thumbprint);
                Assert.InRange(restarted.NotAfter.ToUniversalTime(), DateTime.UtcNow.AddYears(10).AddMinutes(-2), DateTime.UtcNow.AddYears(10).AddMinutes(2));
                Assert.Equal(4096, restarted.GetRSAPublicKey()!.KeySize);
                var folder = Path.Combine(root, "state", "certificates");
                Assert.Equal(2, Directory.GetFiles(folder).Length);
                if (OperatingSystem.IsWindows())
                {
                    using var identity = WindowsIdentity.GetCurrent();
                    var security = new DirectoryInfo(folder).GetAccessControl();
                    Assert.True(security.AreAccessRulesProtected);
                    var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>();
                    foreach (var rule in rules)
                    {
                        if (rule.AccessControlType == AccessControlType.Allow)
                        {
                            Assert.Equal(identity.User, rule.IdentityReference);
                        }
                    }
                }
                else
                {
                    Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(folder));
                    Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path.Combine(folder, "signing.pfx")));
                }
                File.WriteAllText(Path.Combine(folder, "signing.pfx"), "broken-pfx");
                Assert.Throws<InvalidOperationException>(() => CertificateLoader.Load(options, root, true));
                Assert.Equal("broken-pfx", File.ReadAllText(Path.Combine(folder, "signing.pfx")));
            }
            finally { foreach (var certificate in certificates)
                {
                    certificate.Dispose();
                }
            }
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("path", "base64")]
    [InlineData("path", "store")]
    [InlineData("base64", "cloud")]
    [InlineData("store", "cloud")]
    public void 複数方式の同時指定を読み込み前に拒否する(string first, string second)
    {
        var options = new BridgeOptions();
        foreach (var source in new[] { first, second })
        {
            if (source == "path")
            {
                options.SigningCertificatePath = "missing.pfx";
            }

            if (source == "base64")
            {
                options.SigningCertificateBase64 = "secret";
            }

            if (source == "store")
            {
                options.SigningCertificateThumbprint = "thumbprint";
            }

            if (source == "cloud")
            {
                options.SigningCertificateCloud.Provider = "Azure";
            }
        }
        var error = Assert.Throws<InvalidOperationException>(() => CertificateLoader.Load(options, ".", true));
        Assert.Contains("一つだけ", error.Message);
    }

    [Theory]
    [InlineData("Azure", "https://test.vault.azure.net/secrets/certificate/version", "")]
    [InlineData("AWS", "test-secret", "ap-northeast-1")]
    [InlineData("GCP", "projects/test-project/secrets/test-secret/versions/1", "")]
    [InlineData("OCI", "ocid1.vaultsecret.oc1.test", "ap-tokyo-1")]
    public void クラウドPFXを保存せず読み込み取得失敗時も秘密を公開しない(string provider, string id, string region)
    {
        var root = CreateRoot();
        try
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest("CN=Cloud test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var original = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));
            var bytes = original.Export(X509ContentType.Pfx);
            var options = new BridgeOptions { SigningCertificateCloud = new() { Provider = provider, SecretId = id, Region = region } };
            using var loaded = CertificateLoader.Load(options, root, true, (cloud, cancellation) =>
            {
                Assert.Equal(id, cloud.SecretId);
                Assert.True(cancellation.CanBeCanceled);
                return Task.FromResult(bytes);
            });
            Assert.Equal(original.Thumbprint, loaded.Thumbprint);
            Assert.All(bytes, value => Assert.Equal(0, value));
            var error = Assert.Throws<InvalidOperationException>(() => CertificateLoader.Load(options, root, true,
                (_, _) => throw new InvalidOperationException("secret-password-and-pfx")));
            Assert.DoesNotContain("secret-password-and-pfx", error.ToString());
            Assert.Null(error.InnerException);
            Assert.Empty(Directory.GetFiles(root, "*", SearchOption.AllDirectories));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("Azure", "http://test.vault.azure.net/secrets/x", "")]
    [InlineData("Azure", "https://attacker.example/secrets/x", "")]
    [InlineData("Azure", "https://test.vault.azure.net/certificates/x", "")]
    [InlineData("AWS", "secret", "")]
    [InlineData("GCP", "projects/p/secrets/s", "")]
    [InlineData("OCI", "secret", "")]
    [InlineData("other", "secret", "region")]
    public void クラウド設定の不備を通信前に拒否する(string provider, string id, string region)
    {
        var options = new BridgeOptions { SigningCertificateCloud = new() { Provider = provider, SecretId = id, Region = region } };
        var called = false;
        Assert.Throws<InvalidOperationException>(() => CertificateLoader.Load(options, ".", true, (_, _) =>
        { called = true; return Task.FromResult(Array.Empty<byte>()); }));
        Assert.False(called);
    }

    [Fact]
    public void AWSのバイナリと文字列およびOCIのBase64を正しく復号する()
    {
        var data = new byte[] { 0, 128, 255, 1 };
        using var stream = new MemoryStream(data);
        Assert.Equal(data, CloudCertificateReader.DecodeAws(new GetSecretValueResponse { SecretBinary = stream }));
        Assert.Equal(data, CloudCertificateReader.DecodeAws(new GetSecretValueResponse { SecretString = Convert.ToBase64String(data) }));
        Assert.Equal(data, CloudCertificateReader.DecodeOci(new Base64SecretBundleContentDetails { Content = Convert.ToBase64String(data) }));
    }

    [Fact]
    public void Windowsストアの非エクスポート鍵を拇印で読み込める()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=Store test " + Guid.NewGuid(), rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));
        using var persisted = X509CertificateLoader.LoadPkcs12(generated.Export(X509ContentType.Pfx), "", X509KeyStorageFlags.UserKeySet);
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadWrite);
        store.Add(persisted);
        try
        {
            var options = new BridgeOptions { SigningCertificateThumbprint = persisted.Thumbprint };
            using var loaded = CertificateLoader.Load(options, ".", true);
            Assert.Equal(persisted.Thumbprint, loaded.Thumbprint);
            Assert.True(loaded.HasPrivateKey);
            Assert.Throws<CryptographicException>(() => loaded.Export(X509ContentType.Pfx));
        }
        finally { store.Remove(persisted); }
    }

    [Fact]
    public void 旧世代が未指定なら何も読まず自動生成もしない()
    {
        var root = CreateRoot();
        try
        {
            var options = new BridgeOptions { StateDirectory = "state" };
            Assert.Null(CertificateLoader.LoadPrevious(options, root, true));
            Assert.Null(CertificateLoader.LoadPrevious(options, root, false));
            Assert.Empty(Directory.GetFiles(root, "*", SearchOption.AllDirectories));
            Assert.False(Directory.Exists(Path.Combine(root, "state")));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void 旧世代は期限切れでも読めるが現行は期限切れを拒否する()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=Expired previous", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var expired = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-30), DateTimeOffset.UtcNow.AddDays(-1));
        var base64 = Convert.ToBase64String(expired.Export(X509ContentType.Pfx, "test-password"));
        var options = new BridgeOptions
        {
            CertificatePassword = "test-password",
            PreviousSigningCertificateBase64 = base64,
            PreviousEncryptionCertificateBase64 = base64,
            SigningCertificateBase64 = base64
        };
        using var signing = CertificateLoader.LoadPrevious(options, ".", true);
        using var encryption = CertificateLoader.LoadPrevious(options, ".", false);
        Assert.Equal(expired.Thumbprint, signing!.Thumbprint);
        Assert.Equal(expired.Thumbprint, encryption!.Thumbprint);
        Assert.Throws<InvalidOperationException>(() => CertificateLoader.Load(options, ".", true));
    }

    [Fact]
    public void 旧世代の二重指定と不正なPFXは拒否し秘密を公開しない()
    {
        var options = new BridgeOptions { PreviousSigningCertificatePath = "missing.pfx", PreviousSigningCertificateBase64 = "secret" };
        Assert.Contains("一つだけ", Assert.Throws<InvalidOperationException>(() => CertificateLoader.LoadPrevious(options, ".", true)).Message);
        var broken = new BridgeOptions { PreviousEncryptionCertificateBase64 = Convert.ToBase64String("secret-pfx-body"u8.ToArray()), CertificatePassword = "secret-password" };
        var error = Assert.Throws<InvalidOperationException>(() => CertificateLoader.LoadPrevious(broken, ".", false));
        Assert.Contains("暗号化用の旧世代", error.Message);
        Assert.DoesNotContain("secret", error.ToString());
        Assert.Null(error.InnerException);
    }

    [Fact]
    public void 旧世代もクラウドPFXを保存せず読み込む()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=Cloud previous", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var original = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));
        var bytes = original.Export(X509ContentType.Pfx);
        var options = new BridgeOptions
        {
            PreviousSigningCertificateCloud = new() { Provider = "GCP", SecretId = "projects/p/secrets/s/versions/1" }
        };
        using var loaded = CertificateLoader.LoadPrevious(options, ".", true, (_, _) => Task.FromResult(bytes));
        Assert.Equal(original.Thumbprint, loaded!.Thumbprint);
        Assert.All(bytes, value => Assert.Equal(0, value));
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "mcp-certificate-source-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
