[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[A-Za-z0-9._-]+$')]
    [string]$Name,
    [string]$TestFilter = 'FullyQualifiedName~AI'
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$repoRoot = (& git rev-parse --show-toplevel).Trim()
if (-not $repoRoot) { throw '无法解析 Git 仓库根目录。' }

& git diff --cached --quiet
if ($LASTEXITCODE -eq 0) { throw 'Git 暂存区为空，无法验证候选切片。' }

$tempRoot = Join-Path ([IO.Path]::GetPathRoot($repoRoot)) 'bsw'
$shortName = $Name.Substring(0, [Math]::Min(16, $Name.Length))
$runId = $shortName + '_' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
$worktreePath = Join-Path $tempRoot $runId
$patchPath = Join-Path $tempRoot ($runId + '.patch')
New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null

& git diff --cached --binary --output=$patchPath
if ($LASTEXITCODE -ne 0) { throw '无法生成 staged patch。' }

try {
    & git worktree add --detach $worktreePath HEAD
    if ($LASTEXITCODE -ne 0) { throw '无法创建临时工作树。' }

    & git -C $worktreePath apply --index --binary $patchPath
    if ($LASTEXITCODE -ne 0) { throw '候选补丁无法应用到当前 HEAD。' }

    & dotnet restore (Join-Path $worktreePath 'BlueSapphire.slnx') -v:minimal
    if ($LASTEXITCODE -ne 0) { throw '临时工作树 restore 失败。' }

    $testProject = Join-Path $worktreePath 'BlueSapphire.Tests\BlueSapphire.Tests.csproj'
    if ([string]::IsNullOrWhiteSpace($TestFilter)) {
        & dotnet test $testProject --no-restore -v:minimal
    }
    else {
        & dotnet test $testProject --no-restore -v:minimal --filter $TestFilter
    }
    if ($LASTEXITCODE -ne 0) { throw '临时工作树测试失败。' }

    & dotnet build (Join-Path $worktreePath 'BlueSapphire.slnx') --no-incremental --no-restore -v:minimal
    if ($LASTEXITCODE -ne 0) { throw '临时工作树无增量构建失败。' }
}
finally {
    if (Test-Path -LiteralPath $worktreePath) {
        & git worktree remove --force $worktreePath
        if ($LASTEXITCODE -ne 0) {
            throw "无法清理临时工作树: $worktreePath"
        }
    }
    & git worktree prune
    if (Test-Path -LiteralPath $patchPath) {
        Remove-Item -LiteralPath $patchPath -Force
    }
}
