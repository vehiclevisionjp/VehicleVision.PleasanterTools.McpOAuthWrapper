namespace VehicleVision.PleasanterTools.McpOAuthWrapper.Bridge;

public sealed class BridgeOptions
{
    public bool Enabled { get; set; }
    public string Issuer { get; set; } = "https://localhost:7042/";
    public string PleasanterUrl { get; set; } = "https://localhost/";
    public int TenantId { get; set; } = 1;
    public string DatabaseTimeZoneId { get; set; } = "UTC";
    public DateTime DatabaseNow => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(DatabaseTimeZoneId));
    // 共通キーも値は保存せず、Users の所有者 ID だけを設定する。
    public int? SharedApiKeyUserId { get; set; }
    public string StateStore { get; set; } = "Sqlite";
    public string KvsConnectionString { get; set; } = "";
    public string KvsKeyPrefix { get; set; } = "pleasanter-mcp-oauth";
    public string StateDirectory { get; set; } = "App_Data/Wrapper";
    public string SigningCertificatePath { get; set; } = "";
    public string EncryptionCertificatePath { get; set; } = "";
    public string SigningCertificateBase64 { get; set; } = "";
    public string EncryptionCertificateBase64 { get; set; } = "";
    public string SigningCertificateThumbprint { get; set; } = "";
    public string EncryptionCertificateThumbprint { get; set; } = "";
    public string CertificateStoreName { get; set; } = "My";
    public string CertificateStoreLocation { get; set; } = "CurrentUser";
    public CloudCertificateOptions SigningCertificateCloud { get; set; } = new();
    public CloudCertificateOptions EncryptionCertificateCloud { get; set; } = new();
    public string CertificatePassword { get; set; } = "";
    public bool AllowDevelopmentHttp { get; set; }
    public List<string> TrustedProxyAddresses { get; set; } = [];
    public List<string> AllowedOrigins { get; set; } = [];
    public List<OAuthClient> Clients { get; set; } = [];
    public List<string> AllowedRedirectUris { get; set; } = [];
    public bool AllowDynamicClientRegistration { get; set; }
    // 動的登録で作るクライアントの上限。同じ名前と redirect URI は同じクライアントを返す。
    public int MaxDynamicClients { get; set; } = 100;

    public string Resource => new Uri(new Uri(Issuer), "mcp").AbsoluteUri;
}

public sealed class CloudCertificateOptions
{
    public string Provider { get; set; } = "";
    public string SecretId { get; set; } = "";
    public string Version { get; set; } = "";
    public string Region { get; set; } = "";
    public string OciAuthentication { get; set; } = "InstancePrincipal";
    public string OciConfigProfile { get; set; } = "DEFAULT";
}

public sealed class OAuthClient
{
    public string ClientId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public List<string> RedirectUris { get; set; } = [];
}

// App_Data/Parameters/Rds.json は本体と同じフラットなレイアウト。
public sealed class RdsOptions
{
    public string Dbms { get; set; } = "PostgreSQL";
    public string Provider { get; set; } = "Local";
    public string UserConnectionString { get; set; } = "";
    public int SqlCommandTimeOut { get; set; } = 30;
}
