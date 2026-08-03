[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string] $Command = "help",

    [Parameter(Position = 1)]
    [string] $Provider = "all",

    [Parameter(Position = 2)]
    [string] $Argument,

    [switch] $Force,

    [Alias("h")]
    [switch] $Help
)

$ErrorActionPreference = "Stop"
$Framework = if ($env:SPINDLE_EF_FRAMEWORK) {
    $env:SPINDLE_EF_FRAMEWORK
}
else {
    "net10.0"
}
$RepositoryRoot = Split-Path -Parent $PSScriptRoot

function Write-Usage {
    @"
Manage migrations for the Spindle EF Core provider projects.

Usage:
  ./scripts/migrations.ps1 add <provider|all> <migration-name>
  ./scripts/migrations.ps1 remove [provider|all] [-Force]
  ./scripts/migrations.ps1 list [provider|all]
  ./scripts/migrations.ps1 check [provider|all]

Providers:
  sqlite, postgresql (or postgres), mysql, sqlserver (or mssql), all

Environment:
  SPINDLE_EF_FRAMEWORK  Target framework used by dotnet ef (default: net10.0)

Examples:
  ./scripts/migrations.ps1 add all AddFlowPriority
  ./scripts/migrations.ps1 check postgresql
  ./scripts/migrations.ps1 remove sqlite
"@
}

function Resolve-Provider {
    param([string] $Name)

    switch ($Name.ToLowerInvariant()) {
        "sqlite" { return "Sqlite" }
        { $_ -in "postgresql", "postgres" } { return "PostgreSQL" }
        "mysql" { return "MySql" }
        { $_ -in "sqlserver", "mssql" } { return "SqlServer" }
        default { throw "Unknown provider: $Name" }
    }
}

function Invoke-DotNetEf {
    param([string[]] $Arguments)

    & dotnet ef @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet ef exited with code $LASTEXITCODE."
    }
}

function Invoke-ProviderCommand {
    param(
        [string] $ProviderName,
        [string] $MigrationCommand,
        [string] $CommandArgument
    )

    $Project = "src/Spindle.Persistence.EFCore.$ProviderName/Spindle.Persistence.EFCore.$ProviderName.csproj"
    $CommonArguments = @(
        "--project", $Project,
        "--startup-project", $Project,
        "--framework", $Framework
    )

    Write-Host "`n==> ${ProviderName}: $MigrationCommand"

    switch ($MigrationCommand) {
        "add" {
            $EfArguments = @("migrations", "add", $CommandArgument) +
                $CommonArguments +
                @("--output-dir", "Migrations")
            Invoke-DotNetEf -Arguments $EfArguments
        }
        "remove" {
            $RemoveArguments = @("migrations", "remove")
            if ($CommandArgument -eq "--force") {
                $RemoveArguments += "--force"
            }

            Invoke-DotNetEf -Arguments ($RemoveArguments + $CommonArguments)
        }
        "list" {
            Invoke-DotNetEf -Arguments (@("migrations", "list") + $CommonArguments)
        }
        "check" {
            Invoke-DotNetEf -Arguments (@("migrations", "has-pending-model-changes") + $CommonArguments)
        }
    }
}

$Command = $Command.ToLowerInvariant()

if ($Help) {
    Write-Usage
    exit 0
}

switch ($Command) {
    { $_ -in "help", "-h", "--help" } {
        Write-Usage
        exit 0
    }
    "add" {
        if ([string]::IsNullOrWhiteSpace($Argument)) {
            throw "A migration name is required for the add command."
        }
    }
    "remove" {
        if ($Argument) {
            throw "The remove command does not accept a positional argument; use -Force if needed."
        }
    }
    { $_ -in "list", "check" } { }
    default { throw "Unknown command: $Command" }
}

if ($Force -and $Command -ne "remove") {
    throw "-Force can only be used with the remove command."
}

Push-Location $RepositoryRoot
try {
    $Providers = if ($Provider.ToLowerInvariant() -eq "all") {
        @("Sqlite", "PostgreSQL", "MySql", "SqlServer")
    }
    else {
        @(Resolve-Provider $Provider)
    }

    foreach ($ProviderName in $Providers) {
        $CommandArgument = if ($Force) { "--force" } else { $Argument }
        Invoke-ProviderCommand $ProviderName $Command $CommandArgument
    }
}
finally {
    Pop-Location
}
