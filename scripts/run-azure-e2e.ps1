[CmdletBinding()]
param(
    [switch]$ConfirmLiveService,
    [ValidateSet("ConnectionString", "Entra")]
    [string]$AuthMode = "ConnectionString",
    [string]$ClusterName
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$connectionString = $env:DOCUMENTDB_CONNECTION_STRING
$database = $env:DOCUMENTDB_DATABASE
$collectionPrefix = $env:DOCUMENTDB_COLLECTION
$testEnvironmentNames = @(
    "DOCUMENTDB_RUN_INTEGRATION_TESTS",
    "DOCUMENTDB_COLLECTION",
    "DOCUMENTDB_AUTH_MODE",
    "DOCUMENTDB_ENDPOINT",
    "DOCUMENTDB_OIDC_TOKEN"
)
$savedTestEnvironment = @{}
foreach ($name in $testEnvironmentNames) {
    $savedTestEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, "Process")
}

function Invoke-Checked {
    param(
        [Parameter(Mandatory)] [string]$Command,
        [string[]]$CommandArguments = @()
    )

    & $Command @CommandArguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Command failed with exit code $LASTEXITCODE"
    }
}

function Set-TestEnvironment {
    param(
        [Parameter(Mandatory)] [string]$Language
    )

    $env:DOCUMENTDB_RUN_INTEGRATION_TESTS = "1"
    $env:DOCUMENTDB_COLLECTION = "${collectionPrefix}_${Language}"
}

if (-not $ConfirmLiveService) {
    throw "Pass -ConfirmLiveService to allow temporary collections to be created and deleted."
}
if ([string]::IsNullOrWhiteSpace($database)) {
    throw "Set DOCUMENTDB_DATABASE to a dedicated integration-test database."
}
if ([string]::IsNullOrWhiteSpace($collectionPrefix)) {
    $collectionPrefix = "langchain_azure_e2e"
}

if ($AuthMode -eq "Entra") {
    if ([string]::IsNullOrWhiteSpace($ClusterName)) {
        throw "Pass -ClusterName when AuthMode is Entra."
    }
    if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
        throw "Azure CLI is required for Entra validation. Install it and run az login."
    }
    $tokenResponse = & az account get-access-token --scope "https://ossrdbms-aad.database.windows.net/.default" --output json | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($tokenResponse.accessToken)) {
        throw "Azure CLI could not acquire an Azure DocumentDB access token."
    }
    $env:DOCUMENTDB_AUTH_MODE = "entra"
    $env:DOCUMENTDB_ENDPOINT = "mongodb+srv://${ClusterName}.global.mongocluster.cosmos.azure.com/?tls=true&retryWrites=false&maxIdleTimeMS=120000"
    $env:DOCUMENTDB_OIDC_TOKEN = $tokenResponse.accessToken
    Write-Host "Azure E2E validation is using the active Azure CLI identity through MongoDB OIDC."
}
else {
    if ([string]::IsNullOrWhiteSpace($connectionString)) {
        throw "Set DOCUMENTDB_CONNECTION_STRING in the current terminal before running this script."
    }
    if ($connectionString -match "(?i)(tlsAllowInvalidCertificates|tlsInsecure)=true") {
        throw "Azure DocumentDB validation requires normal TLS certificate validation."
    }
    Write-Warning "Azure E2E validation is using connection-string authentication. Production applications must use managed or workload identity."
}

try {
    Write-Host "Running Python live E2E tests against Azure DocumentDB"
    Set-TestEnvironment "python"
    Push-Location (Join-Path $root "python")
    try {
        Invoke-Checked ".\.venv\Scripts\python.exe" @("-m", "pytest", "tests\integration_tests\test_standard.py", "-q")
    }
    finally {
        Pop-Location
    }

    Write-Host "Running JavaScript live E2E tests against Azure DocumentDB"
    Set-TestEnvironment "javascript"
    Push-Location (Join-Path $root "javascript")
    try {
        Invoke-Checked npm @("test", "--", "--run", "tests/integration.test.ts")
    }
    finally {
        Pop-Location
    }

    Write-Host "Running Java live E2E tests against Azure DocumentDB"
    Set-TestEnvironment "java"
    Push-Location (Join-Path $root "java")
    try {
        Invoke-Checked ".\mvnw.cmd" @("-Dtest=DocumentDBIntegrationTest", "test")
    }
    finally {
        Pop-Location
    }

    Write-Host "Running Go live E2E tests against Azure DocumentDB"
    Set-TestEnvironment "go"
    $goCommand = (Get-Command go -ErrorAction SilentlyContinue).Source
    if (-not $goCommand) {
        $goCommand = Join-Path $env:LOCALAPPDATA "Programs\Go\bin\go.exe"
    }
    if (-not (Test-Path $goCommand)) {
        throw "Go was not found on PATH or in the standard user-local installation."
    }
    Push-Location (Join-Path $root "go")
    try {
        Invoke-Checked $goCommand @("test", "-v", "./tests")
    }
    finally {
        Pop-Location
    }

    Write-Host "Running .NET live E2E tests against Azure DocumentDB"
    Set-TestEnvironment "csharp"
    Invoke-Checked dotnet @("test", (Join-Path $root "csharp\DocumentDB.LangChain.sln"), "--no-restore", "--filter", "Category=Integration")

    Write-Host "All Azure DocumentDB E2E tests passed."
}
finally {
    $tokenResponse = $null
    foreach ($name in $testEnvironmentNames) {
        $savedValue = $savedTestEnvironment[$name]
        if ([string]::IsNullOrEmpty($savedValue)) {
            Remove-Item "Env:$name" -ErrorAction SilentlyContinue
        }
        else {
            [Environment]::SetEnvironmentVariable($name, $savedValue, "Process")
        }
    }
}