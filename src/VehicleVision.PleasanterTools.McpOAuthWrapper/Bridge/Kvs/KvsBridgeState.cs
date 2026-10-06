using StackExchange.Redis;
namespace VehicleVision.PleasanterTools.McpOAuthWrapper.Bridge.Kvs;

public sealed class KvsBridgeState(KvsBackend backend) : IBridgeState
{
    public async Task<bool> TryAttemptAsync(string id, CancellationToken ct)
    {
        const string script = "local n=redis.call('INCR',KEYS[1]); if n==1 then redis.call('PEXPIRE',KEYS[1],900000) end; return n";
        return (long)await backend.Database.ScriptEvaluateAsync(script, [backend.Key("attempt:" + id)]).WaitAsync(ct) <= 5;
    }
    // 成功した試行の 1 回分だけを戻す。過去の失敗は残し、成功で制限を回避させない。
    public async Task ReleaseAsync(string id, CancellationToken ct)
    {
        const string script = "if redis.call('EXISTS',KEYS[1])==1 then if redis.call('DECR',KEYS[1])<=0 then redis.call('DEL',KEYS[1]) end end return 1";
        await backend.Database.ScriptEvaluateAsync(script, [backend.Key("attempt:" + id)]).WaitAsync(ct);
    }
    public async Task CreateConsentAsync(string id, CancellationToken ct)
    {
        if (!await backend.Database.StringSetAsync(backend.Key("consent:" + id), "1", TimeSpan.FromMinutes(5), When.NotExists).WaitAsync(ct)) throw new InvalidOperationException("同意の識別子が重複しました。");
    }
    public async Task<bool> ConsumeConsentAsync(string id, CancellationToken ct) => !(await backend.Database.StringGetDeleteAsync(backend.Key("consent:" + id)).WaitAsync(ct)).IsNull;
}
