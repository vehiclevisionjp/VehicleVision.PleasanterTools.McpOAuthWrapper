using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;

namespace VehicleVision.PleasanterTools.McpOAuthWrapper.Bridge;

public sealed record PleasanterUser(int TenantId, int UserId, string LoginId,
    string PasswordHash, string ApiKey, bool Disabled, bool Lockout,
    DateTime? PasswordExpirationTime, DateTime? LoginExpirationLimit,
    int LoginExpirationPeriod, DateTime? LastLoginTime, bool EnableSecretKey)
{
    public bool CanSignIn(DateTime now) => !Disabled && !Lockout && EnableSecretKey
        && !Expired(PasswordExpirationTime, now) && !Expired(LoginExpirationLimit, now)
        && !(LoginExpirationPeriod > 0 && LastLoginTime is { } last && last > new DateTime(1900, 1, 1)
             && last.AddDays(LoginExpirationPeriod) <= now);

    private static bool Expired(DateTime? value, DateTime now) =>
        value is { } expiry && expiry > new DateTime(1900, 1, 1) && expiry <= now;

    public bool VerifyPassword(string password)
    {
        var hash = Convert.ToHexStringLower(SHA512.HashData(Encoding.UTF8.GetBytes(password)));
        return PasswordHash.Length == 128 && CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(hash), Encoding.ASCII.GetBytes(PasswordHash.ToLowerInvariant()));
    }
}

public interface IPleasanterUserStore
{
    Task<PleasanterUser?> FindByLoginAsync(int tenantId, string loginId, CancellationToken cancellationToken);
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
    public Task<PleasanterUser?> FindByLoginAsync(int tenantId, string loginId, CancellationToken cancellationToken)
        => FindAsync(tenantId, "LoginId", loginId, cancellationToken);

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
        string[] columns = ["TenantId", "UserId", "LoginId", "Password", "ApiKey", "Disabled", "Lockout",
            "PasswordExpirationTime", "LoginExpirationLimit", "LoginExpirationPeriod", "LastLoginTime", "EnableSecretKey"];
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
            reader.GetString(2), reader.IsDBNull(3) ? "" : reader.GetString(3), reader.IsDBNull(4) ? "" : reader.GetString(4),
            Convert.ToBoolean(reader.GetValue(5)), Convert.ToBoolean(reader.GetValue(6)), Date(7), Date(8),
            Convert.ToInt32(reader.GetValue(9)), Date(10), Convert.ToBoolean(reader.GetValue(11)));
        // 同じテナントでログイン ID が一意でないときは曖昧な認証をしない。
        return await reader.ReadAsync(cancellationToken) ? null : user;
    }
}
