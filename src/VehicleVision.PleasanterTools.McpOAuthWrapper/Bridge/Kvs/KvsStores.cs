#nullable disable
using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
namespace VehicleVision.PleasanterTools.McpOAuthWrapper.Bridge.Kvs;

public sealed class KvsApplicationStore(KvsBackend backend) : KvsStore<KvsApplication>(backend), IOpenIddictApplicationStore<KvsApplication>
{
    public async ValueTask<KvsApplication> FindByClientIdAsync(string identifier, CancellationToken cancellationToken) { return await Backend.IndexedAsync<KvsApplication>(identifier, cancellationToken); }
    public IAsyncEnumerable<KvsApplication> FindByPostLogoutRedirectUriAsync(string uri, CancellationToken cancellationToken) { return FilterAsync(x => Get(x, "PostLogoutRedirectUris", ImmutableArray<string>.Empty).Contains(uri), cancellationToken); }
    public IAsyncEnumerable<KvsApplication> FindByRedirectUriAsync(string uri, CancellationToken cancellationToken) { return FilterAsync(x => Get(x, "RedirectUris", ImmutableArray<string>.Empty).Contains(uri), cancellationToken); }
    public ValueTask<string> GetApplicationTypeAsync(KvsApplication application, CancellationToken cancellationToken) { return ValueTask.FromResult(Get<string>(application, "ApplicationType")); }
    public ValueTask<string> GetClientIdAsync(KvsApplication application, CancellationToken cancellationToken) { return ValueTask.FromResult(Get<string>(application, "ClientId")); }
    public ValueTask<string> GetClientSecretAsync(KvsApplication application, CancellationToken cancellationToken) { return ValueTask.FromResult(Get<string>(application, "ClientSecret")); }
    public ValueTask<string> GetClientTypeAsync(KvsApplication application, CancellationToken cancellationToken) { return ValueTask.FromResult(Get<string>(application, "ClientType")); }
    public ValueTask<string> GetConsentTypeAsync(KvsApplication application, CancellationToken cancellationToken) { return ValueTask.FromResult(Get<string>(application, "ConsentType")); }
    public ValueTask<string> GetDisplayNameAsync(KvsApplication application, CancellationToken cancellationToken) { return ValueTask.FromResult(Get<string>(application, "DisplayName")); }
    public ValueTask<ImmutableDictionary<CultureInfo, string>> GetDisplayNamesAsync(KvsApplication application, CancellationToken cancellationToken) { return ValueTask.FromResult(Get(application, "DisplayNames", ImmutableDictionary<CultureInfo, string>.Empty)); }
    public ValueTask<string> GetIdAsync(KvsApplication application, CancellationToken cancellationToken) { return ValueTask.FromResult(application.Id); }
    public ValueTask<JsonWebKeySet> GetJsonWebKeySetAsync(KvsApplication application, CancellationToken cancellationToken) { return ValueTask.FromResult(Get<JsonWebKeySet>(application, "JsonWebKeySet")); }
    public ValueTask<ImmutableArray<string>> GetPermissionsAsync(KvsApplication application, CancellationToken cancellationToken) { return ValueTask.FromResult(Get(application, "Permissions", ImmutableArray<string>.Empty)); }
    public ValueTask<ImmutableArray<string>> GetPostLogoutRedirectUrisAsync(KvsApplication application, CancellationToken cancellationToken) { return ValueTask.FromResult(Get(application, "PostLogoutRedirectUris", ImmutableArray<string>.Empty)); }
    public ValueTask<ImmutableDictionary<string, JsonElement>> GetPropertiesAsync(KvsApplication application, CancellationToken cancellationToken) { return ValueTask.FromResult(Get(application, "Properties", ImmutableDictionary<string, JsonElement>.Empty)); }
    public ValueTask<ImmutableArray<string>> GetRedirectUrisAsync(KvsApplication application, CancellationToken cancellationToken) { return ValueTask.FromResult(Get(application, "RedirectUris", ImmutableArray<string>.Empty)); }
    public ValueTask<ImmutableArray<string>> GetRequirementsAsync(KvsApplication application, CancellationToken cancellationToken) { return ValueTask.FromResult(Get(application, "Requirements", ImmutableArray<string>.Empty)); }
    public ValueTask<ImmutableDictionary<string, string>> GetSettingsAsync(KvsApplication application, CancellationToken cancellationToken) { return ValueTask.FromResult(Get(application, "Settings", ImmutableDictionary<string, string>.Empty)); }
    public ValueTask SetApplicationTypeAsync(KvsApplication application, string type, CancellationToken cancellationToken) { Set(application, "ApplicationType", type); return ValueTask.CompletedTask; }
    public ValueTask SetClientIdAsync(KvsApplication application, string identifier, CancellationToken cancellationToken) { Set(application, "ClientId", identifier); return ValueTask.CompletedTask; }
    public ValueTask SetClientSecretAsync(KvsApplication application, string secret, CancellationToken cancellationToken) { Set(application, "ClientSecret", secret); return ValueTask.CompletedTask; }
    public ValueTask SetClientTypeAsync(KvsApplication application, string type, CancellationToken cancellationToken) { Set(application, "ClientType", type); return ValueTask.CompletedTask; }
    public ValueTask SetConsentTypeAsync(KvsApplication application, string type, CancellationToken cancellationToken) { Set(application, "ConsentType", type); return ValueTask.CompletedTask; }
    public ValueTask SetDisplayNameAsync(KvsApplication application, string name, CancellationToken cancellationToken) { Set(application, "DisplayName", name); return ValueTask.CompletedTask; }
    public ValueTask SetDisplayNamesAsync(KvsApplication application, ImmutableDictionary<CultureInfo, string> names, CancellationToken cancellationToken) { Set(application, "DisplayNames", names); return ValueTask.CompletedTask; }
    public ValueTask SetJsonWebKeySetAsync(KvsApplication application, JsonWebKeySet set, CancellationToken cancellationToken) { Set(application, "JsonWebKeySet", set); return ValueTask.CompletedTask; }
    public ValueTask SetPermissionsAsync(KvsApplication application, ImmutableArray<string> permissions, CancellationToken cancellationToken) { Set(application, "Permissions", permissions); return ValueTask.CompletedTask; }
    public ValueTask SetPostLogoutRedirectUrisAsync(KvsApplication application, ImmutableArray<string> uris, CancellationToken cancellationToken) { Set(application, "PostLogoutRedirectUris", uris); return ValueTask.CompletedTask; }
    public ValueTask SetPropertiesAsync(KvsApplication application, ImmutableDictionary<string, JsonElement> properties, CancellationToken cancellationToken) { Set(application, "Properties", properties); return ValueTask.CompletedTask; }
    public ValueTask SetRedirectUrisAsync(KvsApplication application, ImmutableArray<string> uris, CancellationToken cancellationToken) { Set(application, "RedirectUris", uris); return ValueTask.CompletedTask; }
    public ValueTask SetRequirementsAsync(KvsApplication application, ImmutableArray<string> requirements, CancellationToken cancellationToken) { Set(application, "Requirements", requirements); return ValueTask.CompletedTask; }
    public ValueTask SetSettingsAsync(KvsApplication application, ImmutableDictionary<string, string> settings, CancellationToken cancellationToken) { Set(application, "Settings", settings); return ValueTask.CompletedTask; }
}

public sealed class KvsAuthorizationStore(KvsBackend backend) : KvsStore<KvsAuthorization>(backend), IOpenIddictAuthorizationStore<KvsAuthorization>
{
    public IAsyncEnumerable<KvsAuthorization> FindAsync(string subject, string client, string status, string type, System.Nullable<ImmutableArray<string>> scopes, CancellationToken cancellationToken) { return FilterAsync(x => Match(x, "Subject", subject) && Match(x, "ApplicationId", client) && Match(x, "Status", status) && Match(x, "Type", type) && (!scopes.HasValue || scopes.Value.All(s => Get(x, "Scopes", ImmutableArray<string>.Empty).Contains(s))), cancellationToken); }
    public IAsyncEnumerable<KvsAuthorization> FindByApplicationIdAsync(string identifier, CancellationToken cancellationToken) { return FilterAsync(x => Match(x, "ApplicationId", identifier), cancellationToken); }
    public IAsyncEnumerable<KvsAuthorization> FindBySubjectAsync(string subject, CancellationToken cancellationToken) { return FilterAsync(x => Match(x, "Subject", subject), cancellationToken); }
    public ValueTask<string> GetApplicationIdAsync(KvsAuthorization authorization, CancellationToken cancellationToken) { return ValueTask.FromResult(Get<string>(authorization, "ApplicationId")); }
    public ValueTask<System.Nullable<DateTimeOffset>> GetCreationDateAsync(KvsAuthorization authorization, CancellationToken cancellationToken) { return ValueTask.FromResult(Get<System.Nullable<DateTimeOffset>>(authorization, "CreationDate")); }
    public ValueTask<string> GetIdAsync(KvsAuthorization authorization, CancellationToken cancellationToken) { return ValueTask.FromResult(authorization.Id); }
    public ValueTask<ImmutableDictionary<string, JsonElement>> GetPropertiesAsync(KvsAuthorization authorization, CancellationToken cancellationToken) { return ValueTask.FromResult(Get(authorization, "Properties", ImmutableDictionary<string, JsonElement>.Empty)); }
    public ValueTask<ImmutableArray<string>> GetScopesAsync(KvsAuthorization authorization, CancellationToken cancellationToken) { return ValueTask.FromResult(Get(authorization, "Scopes", ImmutableArray<string>.Empty)); }
    public ValueTask<string> GetStatusAsync(KvsAuthorization authorization, CancellationToken cancellationToken) { return ValueTask.FromResult(Get<string>(authorization, "Status")); }
    public ValueTask<string> GetSubjectAsync(KvsAuthorization authorization, CancellationToken cancellationToken) { return ValueTask.FromResult(Get<string>(authorization, "Subject")); }
    public ValueTask<string> GetTypeAsync(KvsAuthorization authorization, CancellationToken cancellationToken) { return ValueTask.FromResult(Get<string>(authorization, "Type")); }
    public ValueTask<long> RevokeAsync(string subject, string client, string status, string type, CancellationToken cancellationToken) { return RevokeWhereAsync(x => Match(x, "Subject", subject) && Match(x, "ApplicationId", client) && Match(x, "Status", status) && Match(x, "Type", type) && Get<string>(x, "Status") != "revoked", cancellationToken); }
    public ValueTask<long> RevokeByApplicationIdAsync(string identifier, CancellationToken cancellationToken) { return RevokeWhereAsync(x => Match(x, "ApplicationId", identifier) && Get<string>(x, "Status") != "revoked", cancellationToken); }
    public ValueTask<long> RevokeBySubjectAsync(string subject, CancellationToken cancellationToken) { return RevokeWhereAsync(x => Match(x, "Subject", subject) && Get<string>(x, "Status") != "revoked", cancellationToken); }
    public ValueTask SetApplicationIdAsync(KvsAuthorization authorization, string identifier, CancellationToken cancellationToken) { Set(authorization, "ApplicationId", identifier); return ValueTask.CompletedTask; }
    public ValueTask SetCreationDateAsync(KvsAuthorization authorization, System.Nullable<DateTimeOffset> date, CancellationToken cancellationToken) { Set(authorization, "CreationDate", date); return ValueTask.CompletedTask; }
    public ValueTask SetPropertiesAsync(KvsAuthorization authorization, ImmutableDictionary<string, JsonElement> properties, CancellationToken cancellationToken) { Set(authorization, "Properties", properties); return ValueTask.CompletedTask; }
    public ValueTask SetScopesAsync(KvsAuthorization authorization, ImmutableArray<string> scopes, CancellationToken cancellationToken) { Set(authorization, "Scopes", scopes); return ValueTask.CompletedTask; }
    public ValueTask SetStatusAsync(KvsAuthorization authorization, string status, CancellationToken cancellationToken) { Set(authorization, "Status", status); return ValueTask.CompletedTask; }
    public ValueTask SetSubjectAsync(KvsAuthorization authorization, string subject, CancellationToken cancellationToken) { Set(authorization, "Subject", subject); return ValueTask.CompletedTask; }
    public ValueTask SetTypeAsync(KvsAuthorization authorization, string type, CancellationToken cancellationToken) { Set(authorization, "Type", type); return ValueTask.CompletedTask; }
}

public sealed class KvsScopeStore(KvsBackend backend) : KvsStore<KvsScope>(backend), IOpenIddictScopeStore<KvsScope>
{
    public async ValueTask<KvsScope> FindByNameAsync(string name, CancellationToken cancellationToken) { return await Backend.IndexedAsync<KvsScope>(name, cancellationToken); }
    public IAsyncEnumerable<KvsScope> FindByNamesAsync(ImmutableArray<string> names, CancellationToken cancellationToken) { return FilterAsync(x => names.Contains(Get<string>(x, "Name")), cancellationToken); }
    public IAsyncEnumerable<KvsScope> FindByResourceAsync(string resource, CancellationToken cancellationToken) { return FilterAsync(x => Get(x, "Resources", ImmutableArray<string>.Empty).Contains(resource), cancellationToken); }
    public ValueTask<string> GetDescriptionAsync(KvsScope scope, CancellationToken cancellationToken) { return ValueTask.FromResult(Get<string>(scope, "Description")); }
    public ValueTask<ImmutableDictionary<CultureInfo, string>> GetDescriptionsAsync(KvsScope scope, CancellationToken cancellationToken) { return ValueTask.FromResult(Get(scope, "Descriptions", ImmutableDictionary<CultureInfo, string>.Empty)); }
    public ValueTask<string> GetDisplayNameAsync(KvsScope scope, CancellationToken cancellationToken) { return ValueTask.FromResult(Get<string>(scope, "DisplayName")); }
    public ValueTask<ImmutableDictionary<CultureInfo, string>> GetDisplayNamesAsync(KvsScope scope, CancellationToken cancellationToken) { return ValueTask.FromResult(Get(scope, "DisplayNames", ImmutableDictionary<CultureInfo, string>.Empty)); }
    public ValueTask<string> GetIdAsync(KvsScope scope, CancellationToken cancellationToken) { return ValueTask.FromResult(scope.Id); }
    public ValueTask<string> GetNameAsync(KvsScope scope, CancellationToken cancellationToken) { return ValueTask.FromResult(Get<string>(scope, "Name")); }
    public ValueTask<ImmutableDictionary<string, JsonElement>> GetPropertiesAsync(KvsScope scope, CancellationToken cancellationToken) { return ValueTask.FromResult(Get(scope, "Properties", ImmutableDictionary<string, JsonElement>.Empty)); }
    public ValueTask<ImmutableArray<string>> GetResourcesAsync(KvsScope scope, CancellationToken cancellationToken) { return ValueTask.FromResult(Get(scope, "Resources", ImmutableArray<string>.Empty)); }
    public ValueTask SetDescriptionAsync(KvsScope scope, string description, CancellationToken cancellationToken) { Set(scope, "Description", description); return ValueTask.CompletedTask; }
    public ValueTask SetDescriptionsAsync(KvsScope scope, ImmutableDictionary<CultureInfo, string> descriptions, CancellationToken cancellationToken) { Set(scope, "Descriptions", descriptions); return ValueTask.CompletedTask; }
    public ValueTask SetDisplayNameAsync(KvsScope scope, string name, CancellationToken cancellationToken) { Set(scope, "DisplayName", name); return ValueTask.CompletedTask; }
    public ValueTask SetDisplayNamesAsync(KvsScope scope, ImmutableDictionary<CultureInfo, string> names, CancellationToken cancellationToken) { Set(scope, "DisplayNames", names); return ValueTask.CompletedTask; }
    public ValueTask SetNameAsync(KvsScope scope, string name, CancellationToken cancellationToken) { Set(scope, "Name", name); return ValueTask.CompletedTask; }
    public ValueTask SetPropertiesAsync(KvsScope scope, ImmutableDictionary<string, JsonElement> properties, CancellationToken cancellationToken) { Set(scope, "Properties", properties); return ValueTask.CompletedTask; }
    public ValueTask SetResourcesAsync(KvsScope scope, ImmutableArray<string> resources, CancellationToken cancellationToken) { Set(scope, "Resources", resources); return ValueTask.CompletedTask; }
}

public sealed class KvsTokenStore(KvsBackend backend) : KvsStore<KvsToken>(backend), IOpenIddictTokenStore<KvsToken>
{
    public IAsyncEnumerable<KvsToken> FindAsync(string subject, string client, string status, string type, CancellationToken cancellationToken) { return FilterAsync(x => Match(x, "Subject", subject) && Match(x, "ApplicationId", client) && Match(x, "Status", status) && Match(x, "Type", type), cancellationToken); }
    public IAsyncEnumerable<KvsToken> FindByApplicationIdAsync(string identifier, CancellationToken cancellationToken) { return FilterAsync(x => Match(x, "ApplicationId", identifier), cancellationToken); }
    public IAsyncEnumerable<KvsToken> FindByAuthorizationIdAsync(string identifier, CancellationToken cancellationToken) { return FilterAsync(x => Match(x, "AuthorizationId", identifier), cancellationToken); }
    public async ValueTask<KvsToken> FindByReferenceIdAsync(string identifier, CancellationToken cancellationToken) { return await Backend.IndexedAsync<KvsToken>(identifier, cancellationToken); }
    public IAsyncEnumerable<KvsToken> FindBySubjectAsync(string subject, CancellationToken cancellationToken) { return FilterAsync(x => Match(x, "Subject", subject), cancellationToken); }
    public ValueTask<string> GetApplicationIdAsync(KvsToken token, CancellationToken cancellationToken) { return ValueTask.FromResult(Get<string>(token, "ApplicationId")); }
    public ValueTask<string> GetAuthorizationIdAsync(KvsToken token, CancellationToken cancellationToken) { return ValueTask.FromResult(Get<string>(token, "AuthorizationId")); }
    public ValueTask<System.Nullable<DateTimeOffset>> GetCreationDateAsync(KvsToken token, CancellationToken cancellationToken) { return ValueTask.FromResult(Get<System.Nullable<DateTimeOffset>>(token, "CreationDate")); }
    public ValueTask<System.Nullable<DateTimeOffset>> GetExpirationDateAsync(KvsToken token, CancellationToken cancellationToken) { return ValueTask.FromResult(Get<System.Nullable<DateTimeOffset>>(token, "ExpirationDate")); }
    public ValueTask<string> GetIdAsync(KvsToken token, CancellationToken cancellationToken) { return ValueTask.FromResult(token.Id); }
    public ValueTask<string> GetPayloadAsync(KvsToken token, CancellationToken cancellationToken) { return ValueTask.FromResult(Get<string>(token, "Payload")); }
    public ValueTask<ImmutableDictionary<string, JsonElement>> GetPropertiesAsync(KvsToken token, CancellationToken cancellationToken) { return ValueTask.FromResult(Get(token, "Properties", ImmutableDictionary<string, JsonElement>.Empty)); }
    public ValueTask<System.Nullable<DateTimeOffset>> GetRedemptionDateAsync(KvsToken token, CancellationToken cancellationToken) { return ValueTask.FromResult(Get<System.Nullable<DateTimeOffset>>(token, "RedemptionDate")); }
    public ValueTask<string> GetReferenceIdAsync(KvsToken token, CancellationToken cancellationToken) { return ValueTask.FromResult(Get<string>(token, "ReferenceId")); }
    public ValueTask<string> GetStatusAsync(KvsToken token, CancellationToken cancellationToken) { return ValueTask.FromResult(Get<string>(token, "Status")); }
    public ValueTask<string> GetSubjectAsync(KvsToken token, CancellationToken cancellationToken) { return ValueTask.FromResult(Get<string>(token, "Subject")); }
    public ValueTask<string> GetTypeAsync(KvsToken token, CancellationToken cancellationToken) { return ValueTask.FromResult(Get<string>(token, "Type")); }
    public ValueTask<long> RevokeAsync(string subject, string client, string status, string type, CancellationToken cancellationToken) { return RevokeWhereAsync(x => Match(x, "Subject", subject) && Match(x, "ApplicationId", client) && Match(x, "Status", status) && Match(x, "Type", type) && Get<string>(x, "Status") != "revoked", cancellationToken); }
    public ValueTask<long> RevokeByApplicationIdAsync(string identifier, CancellationToken cancellationToken) { return RevokeWhereAsync(x => Match(x, "ApplicationId", identifier) && Get<string>(x, "Status") != "revoked", cancellationToken); }
    public ValueTask<long> RevokeByAuthorizationIdAsync(string identifier, CancellationToken cancellationToken) { return RevokeWhereAsync(x => Match(x, "AuthorizationId", identifier) && Get<string>(x, "Status") != "revoked", cancellationToken); }
    public ValueTask<long> RevokeBySubjectAsync(string subject, CancellationToken cancellationToken) { return RevokeWhereAsync(x => Match(x, "Subject", subject) && Get<string>(x, "Status") != "revoked", cancellationToken); }
    public ValueTask SetApplicationIdAsync(KvsToken token, string identifier, CancellationToken cancellationToken) { Set(token, "ApplicationId", identifier); return ValueTask.CompletedTask; }
    public ValueTask SetAuthorizationIdAsync(KvsToken token, string identifier, CancellationToken cancellationToken) { Set(token, "AuthorizationId", identifier); return ValueTask.CompletedTask; }
    public ValueTask SetCreationDateAsync(KvsToken token, System.Nullable<DateTimeOffset> date, CancellationToken cancellationToken) { Set(token, "CreationDate", date); return ValueTask.CompletedTask; }
    public ValueTask SetExpirationDateAsync(KvsToken token, System.Nullable<DateTimeOffset> date, CancellationToken cancellationToken) { Set(token, "ExpirationDate", date); return ValueTask.CompletedTask; }
    public ValueTask SetPayloadAsync(KvsToken token, string payload, CancellationToken cancellationToken) { Set(token, "Payload", payload); return ValueTask.CompletedTask; }
    public ValueTask SetPropertiesAsync(KvsToken token, ImmutableDictionary<string, JsonElement> properties, CancellationToken cancellationToken) { Set(token, "Properties", properties); return ValueTask.CompletedTask; }
    public ValueTask SetRedemptionDateAsync(KvsToken token, System.Nullable<DateTimeOffset> date, CancellationToken cancellationToken) { Set(token, "RedemptionDate", date); return ValueTask.CompletedTask; }
    public ValueTask SetReferenceIdAsync(KvsToken token, string identifier, CancellationToken cancellationToken) { Set(token, "ReferenceId", identifier); return ValueTask.CompletedTask; }
    public ValueTask SetStatusAsync(KvsToken token, string status, CancellationToken cancellationToken) { Set(token, "Status", status); return ValueTask.CompletedTask; }
    public ValueTask SetSubjectAsync(KvsToken token, string subject, CancellationToken cancellationToken) { Set(token, "Subject", subject); return ValueTask.CompletedTask; }
    public ValueTask SetTypeAsync(KvsToken token, string type, CancellationToken cancellationToken) { Set(token, "Type", type); return ValueTask.CompletedTask; }
}
