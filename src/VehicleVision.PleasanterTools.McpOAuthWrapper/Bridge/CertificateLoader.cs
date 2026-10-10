using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;

namespace VehicleVision.PleasanterTools.McpOAuthWrapper.Bridge;

internal static class CertificateLoader
{
    internal static X509Certificate2 Load(BridgeOptions options, string root, bool signing,
        Func<CloudCertificateOptions, CancellationToken, Task<byte[]>>? cloudReader = null)
    {
        var purpose = signing ? "署名用" : "暗号化用";
        var path = signing ? options.SigningCertificatePath : options.EncryptionCertificatePath;
        var base64 = signing ? options.SigningCertificateBase64 : options.EncryptionCertificateBase64;
        var thumbprint = signing ? options.SigningCertificateThumbprint : options.EncryptionCertificateThumbprint;
        var cloud = signing ? options.SigningCertificateCloud : options.EncryptionCertificateCloud;
        var cloudSpecified = !string.IsNullOrWhiteSpace(cloud.Provider) || !string.IsNullOrWhiteSpace(cloud.SecretId)
            || !string.IsNullOrWhiteSpace(cloud.Region) || !string.IsNullOrWhiteSpace(cloud.Version)
            || cloud.OciAuthentication != "InstancePrincipal" || cloud.OciConfigProfile != "DEFAULT";
        var sources = new[] { !string.IsNullOrWhiteSpace(path), !string.IsNullOrWhiteSpace(base64),
            !string.IsNullOrWhiteSpace(thumbprint), cloudSpecified }.Count(value => value);
        if (sources > 1)
            throw new InvalidOperationException($"{purpose}証明書は Path、Base64、Thumbprint、Cloud のいずれか一つだけを設定してください。");
        X509Certificate2 certificate;
        byte[]? pfx = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(thumbprint))
                certificate = LoadStore(thumbprint, options.CertificateStoreName, options.CertificateStoreLocation);
            else if (cloudSpecified)
            {
                CloudCertificateReader.Validate(cloud);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                pfx = (cloudReader ?? CloudCertificateReader.ReadAsync)(cloud, timeout.Token).GetAwaiter().GetResult();
                certificate = X509CertificateLoader.LoadPkcs12(pfx, options.CertificatePassword, X509KeyStorageFlags.EphemeralKeySet);
            }
            else if (sources == 1)
                certificate = BridgeSetup.LoadCertificate(path, base64, options.CertificatePassword, root, purpose);
            else
                certificate = LoadOrCreate(Path.Combine(Path.GetFullPath(options.StateDirectory, root), "certificates"), signing,
                    options.CertificatePassword);
            Validate(certificate, purpose, signing);
            return certificate;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // SDK の例外や設定値には秘密値を含む可能性があるため、内部例外も公開しない。
            throw new InvalidOperationException($"{purpose}証明書を読み込めません。設定方式、PFX、秘密鍵、有効期限、保存先の権限、クラウド認証を確認してください。");
        }
        finally
        {
            if (pfx is not null) CryptographicOperations.ZeroMemory(pfx);
        }
    }

    private static X509Certificate2 LoadStore(string thumbprint, string name, string location)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("証明書ストア方式は Windows 専用です。");
        if (!Enum.TryParse<StoreName>(name, ignoreCase: true, out var storeName) || !Enum.IsDefined(storeName)
            || !Enum.TryParse<StoreLocation>(location, ignoreCase: true, out var storeLocation) || !Enum.IsDefined(storeLocation))
            throw new InvalidOperationException("証明書ストア名と場所を確認してください。");
        var normalized = string.Concat(thumbprint.Where(character => !char.IsWhiteSpace(character)));
        if (normalized.Length != 40 || normalized.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidOperationException("証明書の拇印を確認してください。");
        using var store = new X509Store(storeName, storeLocation);
        store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
        var matches = store.Certificates.Find(X509FindType.FindByThumbprint, normalized, validOnly: false);
        try
        {
            if (matches.Count != 1) throw new InvalidOperationException("証明書を一意に取得できません。");
            // 非エクスポート鍵も OS プロバイダーを通して利用できる。
            return new X509Certificate2(matches[0]);
        }
        finally
        {
            foreach (var match in matches) match.Dispose();
        }
    }

    private static X509Certificate2 LoadOrCreate(string directory, bool signing, string password)
    {
        CreateProtectedDirectory(directory);
        var path = Path.Combine(directory, signing ? "signing.pfx" : "encryption.pfx");
        if (!File.Exists(path))
        {
            using var rsa = RSA.Create(4096);
            var request = new CertificateRequest(signing ? "CN=McpOAuthWrapper Signing" : "CN=McpOAuthWrapper Encryption",
                rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509KeyUsageExtension(signing
                ? X509KeyUsageFlags.DigitalSignature : X509KeyUsageFlags.KeyEncipherment, critical: true));
            var now = DateTimeOffset.UtcNow;
            using var generated = request.CreateSelfSigned(now.AddMinutes(-5), now.AddYears(10));
            var pfx = generated.Export(X509ContentType.Pfx, password);
            var temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".pfx");
            try
            {
                var fileOptions = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
                if (!OperatingSystem.IsWindows()) fileOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                using (var file = new FileStream(temporary, fileOptions))
                {
                    file.Write(pfx);
                    file.Flush(flushToDisk: true);
                }
                // 同時起動時は先に保存された証明書を採用する。既存ファイルを上書きしない。
                try { File.Move(temporary, path, overwrite: false); }
                catch (IOException) when (File.Exists(path)) { }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(pfx);
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
        return X509CertificateLoader.LoadPkcs12FromFile(path, password, X509KeyStorageFlags.EphemeralKeySet);
    }

    private static void CreateProtectedDirectory(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            using var identity = WindowsIdentity.GetCurrent();
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(path).Create(security);
        }
        else
            Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        // 既存の領域の権限は変更しない。共有時は配置側で実行アカウントだけに権限を設定する。
    }

    private static void Validate(X509Certificate2 certificate, string purpose, bool signing)
    {
        try
        {
            if (!certificate.HasPrivateKey || certificate.NotAfter.ToUniversalTime() <= DateTime.UtcNow
                || certificate.NotBefore.ToUniversalTime() > DateTime.UtcNow)
                throw new InvalidOperationException($"{purpose}証明書の秘密鍵と有効期限を確認してください。");
            using var rsa = certificate.GetRSAPrivateKey();
            if (rsa is null || rsa.KeySize < 2048) throw new InvalidOperationException("RSA 2048 ビット以上の秘密鍵が必要です。");
            var usage = certificate.Extensions.OfType<X509KeyUsageExtension>().FirstOrDefault();
            if (usage is not null && !usage.KeyUsages.HasFlag(signing ? X509KeyUsageFlags.DigitalSignature : X509KeyUsageFlags.KeyEncipherment))
                throw new InvalidOperationException("証明書の用途を確認してください。");
            // 秘密鍵へのアクセス権も起動時に確認する。
            var probe = new byte[] { 1, 2, 3 };
            if (signing) rsa.SignData(probe, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            else rsa.Decrypt(rsa.Encrypt(probe, RSAEncryptionPadding.OaepSHA256), RSAEncryptionPadding.OaepSHA256);
        }
        catch
        {
            certificate.Dispose();
            throw;
        }
    }
}
