using System.Data.Common;
using VehicleVision.PleasanterTools.McpOAuthWrapper.Bridge;

var dbms = args[0];
var password = Environment.GetEnvironmentVariable("SMOKE_ADMIN_PASSWORD") ?? throw new InvalidOperationException();
var readerPassword = Environment.GetEnvironmentVariable("SMOKE_READER_PASSWORD") ?? throw new InvalidOperationException();
if (!System.Text.RegularExpressions.Regex.IsMatch(readerPassword, "^[A-Za-z0-9!]+$")) throw new InvalidOperationException();
var port = int.Parse(Environment.GetEnvironmentVariable("SMOKE_PORT")!);
string Connection(string user, string pass, string database) => dbms switch
{
    "SQLServer" => $"Server=127.0.0.1,{port};Database={database};User ID={user};Password={pass};Encrypt=True;TrustServerCertificate=True;Connect Timeout=3",
    "PostgreSQL" => $"Host=127.0.0.1;Port={port};Database={database};Username={user};Password={pass};Timeout=3;Search Path='\"Implem.Pleasanter\"'",
    "MySQL" => $"Server=127.0.0.1;Port={port};Database={database};User ID={user};Password={pass};Connection Timeout=3",
    _ => throw new InvalidOperationException()
};
var adminUser = dbms switch { "SQLServer" => "sa", "PostgreSQL" => "postgres", _ => "root" };
var adminOptions = new RdsOptions { Dbms = dbms, UserConnectionString = Connection(adminUser, password, dbms == "SQLServer" ? "master" : "smoke") };
await using var admin = new PleasanterConnectionFactory(adminOptions).Create();
bool ready = false;
for (int i = 0; i < 60; i++)
{
    try { await admin.OpenAsync(); ready = true; break; }
    catch (DbException) { await Task.Delay(1000); }
}
if (!ready) throw new InvalidOperationException("データベース起動待ちがタイムアウトしました。");
async Task Execute(string sql)
{
    await using var command = admin.CreateCommand(); command.CommandText = sql; await command.ExecuteNonQueryAsync();
}
string Quote(string name) => dbms switch { "SQLServer" => "[" + name + "]", "MySQL" => "`" + name + "`", _ => "\"" + name + "\"" };
string[] names = ["TenantId", "UserId", "LoginId", "ApiKey", "Disabled", "Lockout", "LoginExpirationLimit", "LoginExpirationPeriod", "LastLoginTime"];
if (dbms == "SQLServer")
{
    await Execute("CREATE DATABASE smoke");
    await Execute($"CREATE LOGIN mcp_reader WITH PASSWORD='{readerPassword}'");
    await admin.ChangeDatabaseAsync("smoke");
    await Execute("CREATE USER mcp_reader FOR LOGIN mcp_reader");
}
if (dbms == "PostgreSQL") await Execute("CREATE SCHEMA \"Implem.Pleasanter\"");
string flag = dbms == "PostgreSQL" ? "boolean" : dbms == "SQLServer" ? "bit" : "tinyint(1)";
string date = dbms == "PostgreSQL" ? "timestamp" : dbms == "SQLServer" ? "datetime2" : "datetime";
await Execute($"CREATE TABLE {Quote("Users")} ({Quote(names[0])} int NOT NULL, {Quote(names[1])} int NOT NULL, {Quote(names[2])} varchar(256) NOT NULL, {Quote(names[3])} varchar(256) NULL, {Quote(names[4])} {flag} NOT NULL, {Quote(names[5])} {flag} NOT NULL, {Quote(names[6])} {date} NULL, {Quote(names[7])} int NOT NULL, {Quote(names[8])} {date} NULL)");
var key = Guid.NewGuid().ToString("N");
await using (var insert = admin.CreateCommand())
{
    insert.CommandText = $"INSERT INTO {Quote("Users")} ({string.Join(",",names.Select(Quote))}) VALUES ({string.Join(",",Enumerable.Range(0,9).Select(i=>"@p"+i))})";
    object[] values = [1, 42, "select-smoke", key, false, false, DBNull.Value, 30, new DateTime(2026,10,6,0,0,0,DateTimeKind.Unspecified)];
    for (int i=0;i<values.Length;i++) { var p=insert.CreateParameter();p.ParameterName="@p"+i;p.Value=values[i];insert.Parameters.Add(p); }
    await insert.ExecuteNonQueryAsync();
}
string columns = string.Join(",",names.Select(Quote));
if (dbms == "SQLServer") await Execute($"GRANT SELECT ({columns}) ON dbo.Users TO mcp_reader");
if (dbms == "PostgreSQL")
{
    await Execute($"CREATE ROLE mcp_reader LOGIN PASSWORD '{readerPassword}'");
    await Execute("GRANT USAGE ON SCHEMA \"Implem.Pleasanter\" TO mcp_reader");
    await Execute($"GRANT SELECT ({columns}) ON \"Implem.Pleasanter\".\"Users\" TO mcp_reader");
}
if (dbms == "MySQL")
{
    await Execute($"CREATE USER 'mcp_reader'@'%' IDENTIFIED BY '{readerPassword}'");
    await Execute($"GRANT SELECT ({columns}) ON smoke.Users TO 'mcp_reader'@'%'");
}
var options = new RdsOptions { Dbms = dbms, UserConnectionString = Connection("mcp_reader", readerPassword, "smoke"), SqlCommandTimeOut = 10 };
var factory = new PleasanterConnectionFactory(options);
var store = new PleasanterUserStore(factory, options);
var byId = await store.FindByIdAsync(1,42,CancellationToken.None);
var byKey = await store.FindByApiKeyAsync(1,key,CancellationToken.None);
if (byId is null || byKey != byId || byId.LoginId != "select-smoke" || !byId.VerifyApiKey(key) || byId.Disabled || byId.Lockout || byId.LoginExpirationLimit != null || byId.LoginExpirationPeriod != 30 || byId.LastLoginTime != new DateTime(2026,10,6)) throw new InvalidOperationException("SELECT の列変換結果が一致しません。");
if (await store.FindByIdAsync(2,42,CancellationToken.None) != null || await store.FindByApiKeyAsync(1,"' OR 1=1 --",CancellationToken.None) != null) throw new InvalidOperationException("検索条件が一致しません。");
await using var reader = factory.Create(); await reader.OpenAsync();
await using var write = reader.CreateCommand();write.CommandText=$"DELETE FROM {Quote("Users")} WHERE {Quote("UserId")}=42";
bool denied=false;
try { await write.ExecuteNonQueryAsync(); }
catch (DbException e) when (dbms == "PostgreSQL" && e.SqlState == "42501" || dbms == "SQLServer" && e is Microsoft.Data.SqlClient.SqlException { Number:229 } || dbms == "MySQL" && e is MySqlConnector.MySqlException { Number:1142 }) { denied=true; }
if(!denied) throw new InvalidOperationException("読み取り専用権限ではありません。");
await using var version = admin.CreateCommand();version.CommandText=dbms switch { "SQLServer"=>"SELECT CAST(SERVERPROPERTY('ProductVersion') AS varchar(32))", "PostgreSQL"=>"SHOW server_version",_=>"SELECT VERSION()" };
Console.WriteLine($"{dbms} {await version.ExecuteScalarAsync()}: 接続・9列SELECT・ID/キー検索・テナント分離・書き込み拒否 OK");
