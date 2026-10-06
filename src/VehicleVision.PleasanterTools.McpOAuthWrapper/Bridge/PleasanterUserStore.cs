using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;

namespace VehicleVision.PleasanterTools.McpOAuthWrapper.Bridge;

public sealed record PleasanterUser(int TenantId, int UserId, string LoginId,
    string ApiKey, bool Disabled, bool Lockout,
    DateTime? LoginExpirationLimit,
    int LoginExpirationPeriod, DateTime? LastLoginTime)
{
    public bool CanUseApi(DateTime now) => !Disabled && !Lockout
        && !Expired(LoginExpirationLimit, now)
        && !(LoginExpirationPeriod > 0 && LastLoginTime is { } last && last > new DateTime(1900, 1, 1)
             && last.AddDays(LoginExpirationPeriod) <= now);

    private static bool Expired(DateTime? value, DateTime now) =>
        value is { } expiry && expiry > new DateTime(1900, 1, 1) && expiry <= now;

    public bool VerifyApiKey(string apiKey) => !string.IsNullOrWhiteSpace(ApiKey)
        && CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(apiKey)),
            SHA256.HashData(Encoding.UTF8.GetBytes(ApiKey)));
}

public interface IPleasanterUserStore
{
    Task<PleasanterUser?> FindByApiKeyAsync(int tenantId, string apiKey, CancellationToken cancellationToken);
    Task<PleasanterUser?> FindByIdAsync(int tenantId, int userId, CancellationToken cancellationToken);
}

public interface IPleasanterConnectionFactory
{
    DbConnection Create();
}

public sealed class PleasanterConnectionFactory(RdsOptions options) : IPleasanterConnectionFactory
{
    public DbConnection Create() => options.Dbms.ToLowerInvariant() switch
    {
        "postgresql" => new NpgsqlConnection(options.UserConnectionString),
        "sqlserver" => new SqlConnection(options.UserConnectionString),
        "mysql" => new MySqlConnection(options.UserConnectionString),
        _ => throw new InvalidOperationException("対応していない Dbms です。")
    };
}

public sealed class PleasanterUserStore(IPleasanterConnectionFactory factory, RdsOptions options)
    : IPleasanterUserStore
{
    public Task<PleasanterUser?> FindByApiKeyAsync(int tenantId, string apiKey, CancellationToken cancellationToken)
        => FindAsync(tenantId, "ApiKey", apiKey, cancellationToken);

    public Task<PleasanterUser?> FindByIdAsync(int tenantId, int userId, CancellationToken cancellationToken)
        => FindAsync(tenantId, "UserId", userId, cancellationToken);

    private async Task<PleasanterUser?> FindAsync(int tenantId, string column, object value, CancellationToken cancellationToken)
    {
        string Quote(string identifier) => options.Dbms.ToLowerInvariant() switch
        {
            "sqlserver" => $"[{identifier}]",
            "mysql" => $"`{identifier}`",
            _ => $"\"{identifier}\""
        };
        string[] columns = ["TenantId", "UserId", "LoginId", "ApiKey", "Disabled", "Lockout",
            "LoginExpirationLimit", "LoginExpirationPeriod", "LastLoginTime"];
        await using var connection = factory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        // 識別子は固定値、利用者の入力は必ずパラメーター。SELECT のみ実行する。
        command.CommandText = $"SELECT {string.Join(", ", columns.Select(Quote))} FROM {Quote("Users")} "
            + $"WHERE {Quote("TenantId")} = @tenant AND {Quote(column)} = @value";
        command.CommandTimeout = options.SqlCommandTimeOut;
        var tenant = command.CreateParameter(); tenant.ParameterName = "@tenant"; tenant.Value = tenantId;
        var input = command.CreateParameter(); input.ParameterName = "@value"; input.Value = value;
        command.Parameters.Add(tenant); command.Parameters.Add(input);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        DateTime? Date(int index) => reader.IsDBNull(index) ? null : reader.GetDateTime(index);
        var user = new PleasanterUser(Convert.ToInt32(reader.GetValue(0)), Convert.ToInt32(reader.GetValue(1)),
            reader.GetString(2), reader.IsDBNull(3) ? "" : reader.GetString(3),
            Convert.ToBoolean(reader.GetValue(4)), Convert.ToBoolean(reader.GetValue(5)), Date(6),
            Convert.ToInt32(reader.GetValue(7)), Date(8));
        // 同じテナントでAPI キーが一意でないときは曖昧な認証をしない。
        return await reader.ReadAsync(cancellationToken) ? null : user;
    }
}
