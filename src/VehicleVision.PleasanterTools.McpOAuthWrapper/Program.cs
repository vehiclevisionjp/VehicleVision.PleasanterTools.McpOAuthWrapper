var builder = WebApplication.CreateBuilder(args);
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseExceptionHandler();
app.MapHealthChecks("/health/live");

// OAuth と中継の実装前に、MCP の操作が成功したように見せない。
app.MapMethods("/mcp", ["GET", "POST", "DELETE"], () => Results.Problem(
    statusCode: StatusCodes.Status501NotImplemented,
    title: "MCP 認証ラッパーは準備中です。",
    detail: "OAuth 認証と Pleasanter への中継はまだ実装されていません。"));

app.Run();

// WebApplicationFactory から起動して HTTP 経由で検証するために公開する。
public partial class Program;
