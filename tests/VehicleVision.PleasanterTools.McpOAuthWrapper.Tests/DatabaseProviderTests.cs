using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;
using VehicleVision.PleasanterTools.McpOAuthWrapper.Bridge;

namespace VehicleVision.PleasanterTools.McpOAuthWrapper.Tests;

public sealed class DatabaseProviderTests
{
    [Theory]
    [InlineData("", "Mandatory")]
    [InlineData("Encrypt=False", "Mandatory")]
    [InlineData("Encrypt=Optional", "Mandatory")]
    [InlineData("Encrypt=True", "Mandatory")]
    [InlineData("Encrypt=Strict", "Strict")]
    public void SQLServerの暗号化は無効化できずStrictは維持する(string input, string expected)
    {
        using var connection = new PleasanterConnectionFactory(new RdsOptions
        { Dbms = "SQLServer", UserConnectionString = "Server=localhost;" + input }).Create();
        var actual = new SqlConnectionStringBuilder(connection.ConnectionString);
        Assert.Equal(expected == "Strict" ? SqlConnectionEncryptOption.Strict : SqlConnectionEncryptOption.Mandatory, actual.Encrypt);
        Assert.False(actual.TrustServerCertificate);
    }

    [Theory]
    [InlineData("SQLServer", typeof(SqlConnection))]
    [InlineData("PostgreSQL", typeof(NpgsqlConnection))]
    [InlineData("MySQL", typeof(MySqlConnection))]
    public void 設定したデータベースの専用ドライバーを使う(string dbms, Type expected)
    {
        using var connection = new PleasanterConnectionFactory(new RdsOptions { Dbms = dbms }).Create();
        Assert.IsType(expected, connection);
    }
}
