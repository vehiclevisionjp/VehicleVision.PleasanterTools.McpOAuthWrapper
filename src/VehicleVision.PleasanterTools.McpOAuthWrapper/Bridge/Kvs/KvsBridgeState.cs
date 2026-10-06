using StackExchange.Redis;
namespace VehicleVision.PleasanterTools.McpOAuthWrapper.Bridge.Kvs;

public sealed class KvsBridgeState(KvsBackend backend) : IBridgeState
{
    public async Task<bool> TryAttemptAsync(string id, CancellationToken ct)
    {
        const string script = "local n=redis.call('INCR',KEYS[1]); if n==1 then redis.call('PEXPIRE',KEYS[1],900000) end; return n";
        return (long)await backend.Database.ScriptEvaluateAsync(script, [backend.Key("attempt:" + id)]).WaitAsync(ct) <= 5;
    }
    public async Task ResetAsync(string id, CancellationToken ct) => await backend.Database.KeyDeleteAsync(backend.Key("attempt:" + id)).WaitAsync(ct);
    public async Task CreateConsentAsync(string id, CancellationToken ct)
    {
        if (!await backend.Database.StringSetAsync(backend.Key("consent:" + id), "1", TimeSpan.FromMinutes(5), When.NotExists).WaitAsync(ct)) throw new InvalidOperationException("同意の識別子が重複しました。");
    }
    public async Task<bool> ConsumeConsentAsync(string id, CancellationToken ct) => !(await backend.Database.StringGetDeleteAsync(backend.Key("consent:" + id)).WaitAsync(ct)).IsNull;
}
