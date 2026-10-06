using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenIddict.Abstractions;
using StackExchange.Redis;

namespace VehicleVision.PleasanterTools.McpOAuthWrapper.Bridge.Kvs;

public class KvsEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Version { get; set; } = "";
    public Dictionary<string, JsonElement> Values { get; set; } = [];
}
public sealed class KvsApplication : KvsEntity;
public sealed class KvsAuthorization : KvsEntity;
public sealed class KvsScope : KvsEntity;
public sealed class KvsToken : KvsEntity;

public sealed class KvsBackend(BridgeOptions options, IConnectionMultiplexer connection)
{
    public IDatabase Database => connection.GetDatabase();
    // Lua が触るキーは Redis Cluster でも同じスロットに置く。
    public string Prefix => "{" + options.KvsKeyPrefix + "}:";
    public RedisKey Key(string suffix) => Prefix + suffix;
    public static JsonSerializerOptions Json { get; } = CreateJson();
    private static JsonSerializerOptions CreateJson()
    {
        var json = new JsonSerializerOptions(); json.Converters.Add(new CultureConverter()); return json;
    }
    private sealed class CultureConverter : JsonConverter<CultureInfo>
    {
        public override CultureInfo Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) => CultureInfo.GetCultureInfo(reader.GetString()!);
        public override void Write(Utf8JsonWriter writer, CultureInfo value, JsonSerializerOptions options) => writer.WriteStringValue(value.Name);
        public override CultureInfo ReadAsPropertyName(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) => CultureInfo.GetCultureInfo(reader.GetString()!);
        public override void WriteAsPropertyName(Utf8JsonWriter writer, CultureInfo value, JsonSerializerOptions options) => writer.WritePropertyName(value.Name);
    }
    public static TValue Get<TValue>(KvsEntity entity, string field, TValue fallback = default!) =>
        entity.Values.TryGetValue(field, out var value) ? value.Deserialize<TValue>(Json)! : fallback;
    public static void Set<TValue>(KvsEntity entity, string field, TValue value) => entity.Values[field] = JsonSerializer.SerializeToElement(value, Json);
    public static string Collection<T>() => typeof(T).Name;
    public async Task<T?> ReadAsync<T>(string id, CancellationToken ct) where T : KvsEntity
    {
        var value = await Database.HashGetAsync(Key(Collection<T>()), id).WaitAsync(ct);
        return value.IsNull ? null : JsonSerializer.Deserialize<T>(value.ToString(), Json);
    }
    public async Task<List<T>> AllAsync<T>(CancellationToken ct) where T : KvsEntity
    {
        var values = await Database.HashGetAllAsync(Key(Collection<T>())).WaitAsync(ct);
        return values.Select(x => JsonSerializer.Deserialize<T>(x.Value.ToString(), Json)!).ToList();
    }
    public async Task<T?> IndexedAsync<T>(string value, CancellationToken ct) where T : KvsEntity
    {
        var id = await Database.HashGetAsync(Key(Collection<T>() + ":index"), value).WaitAsync(ct);
        return id.IsNull ? null : await ReadAsync<T>(id.ToString(), ct);
    }
    public async Task SaveAsync<T>(T entity, bool create, CancellationToken ct) where T : KvsEntity
    {
        var old = entity.Version; entity.Version = Guid.NewGuid().ToString("N");
        var index = typeof(T) == typeof(KvsApplication) ? "ClientId" : typeof(T) == typeof(KvsScope) ? "Name" : typeof(T) == typeof(KvsToken) ? "ReferenceId" : "";
        const string script = """
            local old = redis.call('HGET', KEYS[1], ARGV[1])
            if ARGV[4] == 'create' then
              if old then return 0 end
            elseif not old or cjson.decode(old).Version ~= ARGV[2] then return 0 end
            local new = cjson.decode(ARGV[3])
            if old then
              local prevStatus = cjson.decode(old).Values.Status
              if (prevStatus == 'revoked' or prevStatus == 'rejected') and new.Values.Status ~= prevStatus then return 0 end
            end
            if old and new.Values.Type == 'urn:openiddict:params:oauth:token-type:authorization_code' and new.Values.Status == 'redeemed' and cjson.decode(old).Values.Status ~= 'valid' then return 0 end
            local idx = new.Values[ARGV[5]]
            if type(idx) == 'string' and idx ~= '' then
              local owner = redis.call('HGET', KEYS[2], idx)
              if owner and owner ~= ARGV[1] then return 0 end
            end
            if old then
              local prev = cjson.decode(old).Values[ARGV[5]]
              if type(prev) == 'string' and prev ~= idx then redis.call('HDEL', KEYS[2], prev) end
            end
            redis.call('HSET', KEYS[1], ARGV[1], ARGV[3])
            if type(idx) == 'string' and idx ~= '' then redis.call('HSET', KEYS[2], idx, ARGV[1]) end
            return 1
            """;
        var success = (long)await Database.ScriptEvaluateAsync(script,
            [Key(Collection<T>()), Key(Collection<T>() + ":index")],
            [entity.Id, old, JsonSerializer.Serialize(entity, Json), create ? "create" : "update", index]).WaitAsync(ct);
        if (success != 1) { entity.Version = old; throw new OpenIddictExceptions.ConcurrencyException("OAuth 状態が別の処理で更新されました。"); }
    }
    public async Task DeleteAsync<T>(T entity, CancellationToken ct) where T : KvsEntity
    {
        // 子の失効と削除も同じ Lua 内で行い、削除後に有効なトークンを残さない。
        const string script = """
            local old = redis.call('HGET', KEYS[1], ARGV[1])
            if not old or cjson.decode(old).Version ~= ARGV[2] then return 0 end
            local function remove(collection, index, id, value)
              local row = cjson.decode(value)
              for _, field in ipairs({'ClientId','Name','ReferenceId'}) do
                local idx = row.Values[field]
                if type(idx) == 'string' then redis.call('HDEL', index, idx) end
              end
              redis.call('HDEL', collection, id)
            end
            if ARGV[3] == 'KvsApplication' or ARGV[3] == 'KvsAuthorization' then
              local tokens = redis.call('HGETALL', KEYS[3])
              for i=1,#tokens,2 do
                local row = cjson.decode(tokens[i+1])
                local field = ARGV[3] == 'KvsApplication' and 'ApplicationId' or 'AuthorizationId'
                if row.Values[field] == ARGV[1] then remove(KEYS[3],KEYS[4],tokens[i],tokens[i+1]) end
              end
            end
            if ARGV[3] == 'KvsApplication' then
              local auths = redis.call('HGETALL', KEYS[5])
              for i=1,#auths,2 do
                if cjson.decode(auths[i+1]).Values.ApplicationId == ARGV[1] then redis.call('HDEL',KEYS[5],auths[i]) end
              end
            end
            remove(KEYS[1],KEYS[2],ARGV[1],old)
            return 1
            """;
        var deleted = (long)await Database.ScriptEvaluateAsync(script,
            [Key(Collection<T>()), Key(Collection<T>() + ":index"), Key("KvsToken"), Key("KvsToken:index"), Key("KvsAuthorization")],
            [entity.Id, entity.Version, Collection<T>()]).WaitAsync(ct);
        if (deleted != 1) throw new OpenIddictExceptions.ConcurrencyException("OAuth 状態が別の処理で更新されました。");
    }
}

public abstract class KvsStore<T>(KvsBackend backend) where T : KvsEntity, new()
{
    protected KvsBackend Backend => backend;
    protected static TValue Get<TValue>(T entity, string field, TValue fallback = default!) => KvsBackend.Get(entity, field, fallback);
    protected static void Set<TValue>(T entity, string field, TValue value) => KvsBackend.Set(entity, field, value);
    public async ValueTask<long> CountAsync(CancellationToken cancellationToken) => (await backend.AllAsync<T>(cancellationToken)).Count;
    public async ValueTask<long> CountAsync<TResult>(Func<IQueryable<T>, IQueryable<TResult>> query, CancellationToken cancellationToken) => query((await backend.AllAsync<T>(cancellationToken)).AsQueryable()).LongCount();
    public async ValueTask CreateAsync(T entity, CancellationToken cancellationToken) => await backend.SaveAsync(entity, true, cancellationToken);
    public async ValueTask UpdateAsync(T entity, CancellationToken cancellationToken) => await backend.SaveAsync(entity, false, cancellationToken);
    public async ValueTask DeleteAsync(T entity, CancellationToken cancellationToken) => await backend.DeleteAsync(entity, cancellationToken);
    public ValueTask<T> InstantiateAsync(CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); return ValueTask.FromResult(new T()); }
    public async ValueTask<T?> FindByIdAsync(string identifier, CancellationToken cancellationToken) => await backend.ReadAsync<T>(identifier, cancellationToken);
    public async ValueTask<TResult?> GetAsync<TState, TResult>(Func<IQueryable<T>, TState, IQueryable<TResult>> query, TState state, CancellationToken cancellationToken) => query((await backend.AllAsync<T>(cancellationToken)).AsQueryable(), state).FirstOrDefault();
    public async IAsyncEnumerable<T> ListAsync(int? count, int? offset, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var row in (await backend.AllAsync<T>(cancellationToken)).OrderBy(x => x.Id, StringComparer.Ordinal).Skip(offset ?? 0).Take(count ?? int.MaxValue)) { cancellationToken.ThrowIfCancellationRequested(); yield return row; }
    }
    public async IAsyncEnumerable<TResult> ListAsync<TState, TResult>(Func<IQueryable<T>, TState, IQueryable<TResult>> query, TState state, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var row in query((await backend.AllAsync<T>(cancellationToken)).AsQueryable(), state)) { cancellationToken.ThrowIfCancellationRequested(); yield return row; }
    }
    protected async IAsyncEnumerable<T> FilterAsync(Func<T, bool> filter, [EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var row in await backend.AllAsync<T>(ct)) { ct.ThrowIfCancellationRequested(); if (filter(row)) yield return row; }
    }
    protected static bool Match(T row, string field, string? value) => value is null || Get<string?>(row, field) == value;
    protected async ValueTask<long> RevokeWhereAsync(Func<T, bool> filter, CancellationToken ct)
    {
        long count = 0;
        await foreach (var row in FilterAsync(filter, ct))
        {
            var current = row;
            for (int retry = 0; retry < 3; retry++)
            {
                if (!filter(current)) break;
                Set(current, "Status", OpenIddictConstants.Statuses.Revoked);
                try { await UpdateAsync(current, ct); count++; break; }
                catch (OpenIddictExceptions.ConcurrencyException) when (retry < 2) { current = await backend.ReadAsync<T>(row.Id, ct); if (current is null) break; }
            }
        }
        return count;
    }
    public async ValueTask<long> PruneAsync(DateTimeOffset threshold, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow; long count = 0;
        var tokens = typeof(T) == typeof(KvsAuthorization) ? await backend.AllAsync<KvsToken>(cancellationToken) : [];
        await foreach (var row in FilterAsync(x => Get<DateTimeOffset?>(x, "CreationDate") < threshold, cancellationToken))
        {
            bool expired = typeof(T) == typeof(KvsToken) ? Get<DateTimeOffset?>(row, "ExpirationDate") < now || Get<string>(row, "Status") is not ("valid" or "inactive")
                : Get<string>(row, "Status") != "valid" || Get<string>(row, "Type") == OpenIddictConstants.AuthorizationTypes.AdHoc && !tokens.Any(t => KvsBackend.Get<string>(t, "AuthorizationId") == row.Id && KvsBackend.Get<string>(t, "Status") == "valid" && KvsBackend.Get<DateTimeOffset?>(t, "ExpirationDate") > now);
            if (!expired) continue;
            try { await DeleteAsync(row, cancellationToken); count++; } catch (OpenIddictExceptions.ConcurrencyException) { }
        }
        return count;
    }
}
