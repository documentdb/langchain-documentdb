[CmdletBinding()]
param(
    [int]$Port = 10260,
    [string]$Image = "ghcr.io/documentdb/documentdb/documentdb-local:latest",
    [switch]$KeepContainer
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$container = "langchain-documentdb-e2e-$PID"
$username = "langchain_e2e"
$password = "local_$([Guid]::NewGuid().ToString('N'))"
$database = "langchain_e2e"
$certificate = Join-Path ([IO.Path]::GetTempPath()) "$container-cert.pem"
$trustStore = Join-Path ([IO.Path]::GetTempPath()) "$container-truststore.p12"
$savedJavaToolOptions = $env:JAVA_TOOL_OPTIONS
$testEnvironmentNames = @(
    "DOCUMENTDB_RUN_INTEGRATION_TESTS",
    "DOCUMENTDB_CONNECTION_STRING",
    "DOCUMENTDB_DATABASE",
    "DOCUMENTDB_COLLECTION"
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
        [Parameter(Mandatory)] [string]$ConnectionString,
        [Parameter(Mandatory)] [string]$CollectionPrefix
    )

    $env:DOCUMENTDB_RUN_INTEGRATION_TESTS = "1"
    $env:DOCUMENTDB_CONNECTION_STRING = $ConnectionString
    $env:DOCUMENTDB_DATABASE = $database
    $env:DOCUMENTDB_COLLECTION = $CollectionPrefix
}

try {
    Invoke-Checked docker @("version")
    Invoke-Checked docker @("run", "-d", "--name", $container, "-p", "127.0.0.1:${Port}:10260", $Image, "--username", $username, "--password", $password, "--init-data", "false")

    $readyJob = Start-Job -ScriptBlock {
        docker logs --follow $using:container 2>&1 |
            Select-String -SimpleMatch "=== DocumentDB is ready ===" |
            Select-Object -First 1
    }
    if (-not (Wait-Job $readyJob -Timeout 180)) {
        throw "DocumentDB Local did not become ready within 180 seconds."
    }
    Receive-Job $readyJob | Out-Host
    Remove-Job $readyJob -Force

    $escapedUsername = [Uri]::EscapeDataString($username)
    $escapedPassword = [Uri]::EscapeDataString($password)
    $baseUri = "mongodb://${escapedUsername}:${escapedPassword}@localhost:$Port/"
    $allowInvalidUri = "${baseUri}?tls=true&tlsAllowInvalidCertificates=true&authMechanism=SCRAM-SHA-256&directConnection=true"
    $tlsInsecureUri = "${baseUri}?tls=true&tlsInsecure=true&authMechanism=SCRAM-SHA-256&directConnection=true"

    Write-Host "Running Python live E2E tests"
    Set-TestEnvironment $allowInvalidUri "python"
    Push-Location (Join-Path $root "python")
    try {
        Invoke-Checked ".\.venv\Scripts\python.exe" @("-m", "pytest", "tests\integration_tests\test_standard.py", "-q")
    }
    finally {
        Pop-Location
    }

    Write-Host "Running JavaScript live E2E tests"
    Set-TestEnvironment $allowInvalidUri "javascript"
    Push-Location (Join-Path $root "javascript")
    try {
        Invoke-Checked npm @("test", "--", "--run", "tests/integration.test.ts")
    }
    finally {
        Pop-Location
    }

    Write-Host "Running Java live E2E tests"
    Invoke-Checked docker @("cp", "${container}:/home/documentdb/.local/state/documentdb-gateway/tls/cert.pem", $certificate)
    Invoke-Checked keytool @("-importcert", "-noprompt", "-alias", "documentdb-local", "-file", $certificate, "-keystore", $trustStore, "-storetype", "PKCS12", "-storepass", "local_test_only")
    $env:JAVA_TOOL_OPTIONS = "-Djavax.net.ssl.trustStore=$trustStore -Djavax.net.ssl.trustStorePassword=local_test_only"
    Set-TestEnvironment "${baseUri}?tls=true&authMechanism=SCRAM-SHA-256&directConnection=true" "java"
    Push-Location (Join-Path $root "java")
    try {
        Invoke-Checked ".\mvnw.cmd" @("-Dtest=DocumentDBIntegrationTest", "test")
    }
    finally {
        Pop-Location
    }

    Write-Host "Running Go live E2E tests"
    $env:JAVA_TOOL_OPTIONS = $savedJavaToolOptions
    Set-TestEnvironment $tlsInsecureUri "go"
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

    Write-Host "Running .NET live E2E tests"
    Set-TestEnvironment $tlsInsecureUri "csharp"
    Invoke-Checked dotnet @("test", (Join-Path $root "csharp\DocumentDB.LangChain.sln"), "--no-restore", "--filter", "Category=Integration")

    Write-Host "All DocumentDB Local E2E tests passed."
}
finally {
    $env:JAVA_TOOL_OPTIONS = $savedJavaToolOptions
    Remove-Item $certificate, $trustStore -Force -ErrorAction SilentlyContinue
    foreach ($name in $testEnvironmentNames) {
        [Environment]::SetEnvironmentVariable($name, $savedTestEnvironment[$name], "Process")
    }
    if (-not $KeepContainer) {
        docker rm -fv $container 2>$null | Out-Null
    }
    else {
        Write-Host "DocumentDB Local is still running in container $container on port $Port."
    }
}