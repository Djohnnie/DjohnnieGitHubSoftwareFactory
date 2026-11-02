#Requires -Version 7
<#
.SYNOPSIS
    Connects this GitHub repository to an Azure subscription for the "Build and release" workflow.

.DESCRIPTION
    Uses existing Azure resources (resource group, Web App, Azure SQL server and database) and:
      1. Creates (or reuses) a Microsoft Entra app registration + service principal for GitHub Actions.
      2. Adds a federated credential so the repository's "production" environment can log in with OIDC
         (no Azure client secret is stored in GitHub).
      3. Grants the service principal "Website Contributor" on the Web App and
         "SQL Server Contributor" on the SQL server (needed to open a temporary firewall rule).
      4. Creates the GitHub environment and sets its variables and the SQL connection string secret.
      5. Optionally stores the same connection string on the Web App as ConnectionStrings:Database.

    Requires the Azure CLI (az login) and GitHub CLI (gh auth login) with admin rights on the repository.

.EXAMPLE
    ./scripts/setup-azure-github.ps1 -SubscriptionId <guid> -ResourceGroup rg-dghsf `
        -WebAppName app-dghsf -SqlServerName sql-dghsf -SqlDatabaseName sqldb-dghsf -ConfigureWebApp
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $SubscriptionId,
    [Parameter(Mandatory)] [string] $ResourceGroup,
    [Parameter(Mandatory)] [string] $WebAppName,
    [Parameter(Mandatory)] [string] $SqlServerName,
    [Parameter(Mandatory)] [string] $SqlDatabaseName,
    [string] $Repository = 'Djohnnie/DjohnnieGitHubSoftwareFactory',
    [string] $Environment = 'production',
    [string] $AppRegistrationName = 'github-DjohnnieGitHubSoftwareFactory',
    [switch] $ConfigureWebApp
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

function Invoke-Az { $out = az @args; if ($LASTEXITCODE) { throw "az $args failed" }; $out }

Write-Host "Selecting subscription $SubscriptionId"
Invoke-Az account set --subscription $SubscriptionId | Out-Null
$tenantId = Invoke-Az account show --query tenantId -o tsv

$webAppId = Invoke-Az webapp show -g $ResourceGroup -n $WebAppName --query id -o tsv
$sqlServerId = Invoke-Az sql server show -g $ResourceGroup -n $SqlServerName --query id -o tsv
$sqlServerFqdn = Invoke-Az sql server show -g $ResourceGroup -n $SqlServerName --query fullyQualifiedDomainName -o tsv
Invoke-Az sql db show -g $ResourceGroup -s $SqlServerName -n $SqlDatabaseName --query id -o tsv | Out-Null

Write-Host "Ensuring app registration '$AppRegistrationName'"
$clientId = Invoke-Az ad app list --display-name $AppRegistrationName --query '[0].appId' -o tsv
if (-not $clientId) {
    $clientId = Invoke-Az ad app create --display-name $AppRegistrationName --query appId -o tsv
}
$principalId = Invoke-Az ad sp list --filter "appId eq '$clientId'" --query '[0].id' -o tsv
if (-not $principalId) {
    $principalId = Invoke-Az ad sp create --id $clientId --query id -o tsv
}

$subject = "repo:${Repository}:environment:$Environment"
$existingSubjects = Invoke-Az ad app federated-credential list --id $clientId --query '[].subject' -o tsv
if ($existingSubjects -notcontains $subject) {
    Write-Host "Adding federated credential for $subject"
    $credentialFile = New-TemporaryFile
    @{
        name      = "github-$Environment"
        issuer    = 'https://token.actions.githubusercontent.com'
        subject   = $subject
        audiences = @('api://AzureADTokenExchange')
    } | ConvertTo-Json | Set-Content $credentialFile
    try { Invoke-Az ad app federated-credential create --id $clientId --parameters "@$credentialFile" | Out-Null }
    finally { Remove-Item $credentialFile }
}

Write-Host 'Assigning roles'
foreach ($assignment in @(
        @{ Role = 'Website Contributor'; Scope = $webAppId },
        @{ Role = 'SQL Server Contributor'; Scope = $sqlServerId })) {
    $exists = Invoke-Az role assignment list --assignee $principalId --role $assignment.Role --scope $assignment.Scope --query '[0].id' -o tsv
    if (-not $exists) {
        Invoke-Az role assignment create --assignee-object-id $principalId --assignee-principal-type ServicePrincipal `
            --role $assignment.Role --scope $assignment.Scope | Out-Null
    }
}

Write-Host "Configuring GitHub environment '$Environment' on $Repository"
gh api --method PUT "repos/$Repository/environments/$Environment" --silent
$variables = [ordered]@{
    AZURE_CLIENT_ID       = $clientId
    AZURE_TENANT_ID       = $tenantId
    AZURE_SUBSCRIPTION_ID = $SubscriptionId
    AZURE_RESOURCE_GROUP  = $ResourceGroup
    AZURE_WEBAPP_NAME     = $WebAppName
    AZURE_SQL_SERVER_NAME = $SqlServerName
}
foreach ($name in $variables.Keys) {
    gh variable set $name --repo $Repository --env $Environment --body $variables[$name]
}

Write-Host ''
Write-Host "Enter the ADO.NET connection string for database '$SqlDatabaseName' (input hidden)."
Write-Host "Example: Server=tcp:$sqlServerFqdn,1433;Database=$SqlDatabaseName;User ID=<user>;Password=<password>;Encrypt=True;TrustServerCertificate=False;"
$secure = Read-Host 'Connection string' -AsSecureString
$connectionString = [System.Net.NetworkCredential]::new('', $secure).Password
if ([string]::IsNullOrWhiteSpace($connectionString)) { throw 'A connection string is required.' }

$connectionString | gh secret set AZURE_SQL_CONNECTION_STRING --repo $Repository --env $Environment

if ($ConfigureWebApp) {
    Write-Host "Storing ConnectionStrings:Database on Web App '$WebAppName'"
    Invoke-Az webapp config connection-string set -g $ResourceGroup -n $WebAppName `
        --connection-string-type SQLAzure --settings "Database=$connectionString" -o none
}

Write-Host ''
Write-Host 'Done. Remaining manual steps:' -ForegroundColor Green
Write-Host "  - Configure Web App app settings GitHub__ClientId, GitHub__ClientSecret, OpenAI__* (see docs/azure-deployment.md)."
Write-Host "  - Push to main (or run the workflow manually) to migrate the database and deploy."
