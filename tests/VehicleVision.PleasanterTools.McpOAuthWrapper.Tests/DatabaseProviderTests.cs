using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;
using VehicleVision.PleasanterTools.McpOAuthWrapper.Bridge;

namespace VehicleVision.PleasanterTools.McpOAuthWrapper.Tests;

public sealed class DatabaseProviderTests
{
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
