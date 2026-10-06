# 専用の空コンテナだけに検証データを作成し、必ず破棄する。
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$project = Join-Path $repoRoot '_development/tests/database-smoke/DatabaseSmoke.csproj'
$taskRoot = Join-Path $repoRoot (('temp/' + (Get-Date -Format yyyyMMdd) + '_database-') + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskRoot -Force | Out-Null
$cases = @(
    @{Dbms='SQLServer';Image='mcr.microsoft.com/mssql/server:2025-CU8-ubuntu-24.04';Port=1433;UserVariable='MSSQL_SA_PASSWORD';Extra=@('ACCEPT_EULA=Y','MSSQL_PID=Developer')},
    @{Dbms='PostgreSQL';Image='postgres:17';Port=5432;UserVariable='POSTGRES_PASSWORD';Extra=@('POSTGRES_DB=smoke')},
    @{Dbms='MySQL';Image='mysql:8.4';Port=3306;UserVariable='MYSQL_ROOT_PASSWORD';Extra=@('MYSQL_DATABASE=smoke','MYSQL_ROOT_HOST=%')}
)
$names = @()
try {
    dotnet build $project -c Release
    if ($LASTEXITCODE -ne 0) { throw '検証プログラムのビルドに失敗しました。' }
    foreach($case in $cases) {
        $name = 'mcp-select-' + [Guid]::NewGuid().ToString('N')
        $names += $name
        $env:SMOKE_ADMIN_PASSWORD = 'Vv1!' + [Guid]::NewGuid().ToString('N')
        $env:SMOKE_READER_PASSWORD = 'Vv2!' + [Guid]::NewGuid().ToString('N')
        $envFile = Join-Path $taskRoot ($case.Dbms + '.env')
        @($case.UserVariable + '=' + $env:SMOKE_ADMIN_PASSWORD) + $case.Extra | Set-Content -LiteralPath $envFile -Encoding utf8NoBOM
        docker run -d --name $name --env-file $envFile -p "127.0.0.1::$($case.Port)" $case.Image | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "$($case.Dbms) の起動に失敗しました。" }
        $env:SMOKE_PORT = (docker port $name "$($case.Port)/tcp").Split(':')[-1]
        dotnet (Join-Path $repoRoot '_development/tests/database-smoke/bin/Release/net10.0/DatabaseSmoke.dll') $case.Dbms
        if ($LASTEXITCODE -ne 0) { throw "$($case.Dbms) の検証に失敗しました。" }
        docker rm -f $name | Out-Null
        $names = @($names | Where-Object { $_ -ne $name })
    }
} finally {
    foreach($name in $names) { docker rm -f $name 2>$null | Out-Null }
    Remove-Item Env:SMOKE_ADMIN_PASSWORD,Env:SMOKE_READER_PASSWORD,Env:SMOKE_PORT -ErrorAction SilentlyContinue
    $resolved = [IO.Path]::GetFullPath($taskRoot)
    if(!$resolved.StartsWith([IO.Path]::GetFullPath((Join-Path $repoRoot 'temp')) + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw '削除対象が一時領域の外です。' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
