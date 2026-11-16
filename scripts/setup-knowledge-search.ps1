<#
.SYNOPSIS
    Creates or updates the Azure AI Search objects that index the DGHSF knowledge base for the work item chat agents.

.DESCRIPTION
    Fills in the templates in docs/knowledge-base-search and PUTs them to the search service (REST API 2024-07-01), in
    order: the blob data source, the index, the skillset (split into passages, embed with Azure OpenAI, one search
    document per passage) and the indexer. Running it again updates the objects in place; see
    docs/azure-deployment.md (Knowledge base search) for what to configure in DGHSF afterwards.

    Changing -Dimensions or -EmbeddingModel later changes the index's vector field, which Azure AI Search can't update:
    delete the index (and reset the indexer) first.

.EXAMPLE
    ./scripts/setup-knowledge-search.ps1 -SearchServiceName my-search -SearchAdminKey <admin key> `
        -StorageConnectionString "<connection string>" `
        -OpenAIEndpoint https://my-openai.openai.azure.com -OpenAIKey <key> -EmbeddingDeployment text-embedding-3-small
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $SearchServiceName,
    [Parameter(Mandatory)] [string] $SearchAdminKey,
    [Parameter(Mandatory)] [string] $StorageConnectionString,
    [Parameter(Mandatory)] [string] $OpenAIEndpoint,
    [Parameter(Mandatory)] [string] $OpenAIKey,
    [Parameter(Mandatory)] [string] $EmbeddingDeployment,
    [ValidateSet("text-embedding-3-small", "text-embedding-3-large", "text-embedding-ada-002")]
    [string] $EmbeddingModel = "text-embedding-3-small",
    [int] $Dimensions = 1536,
    [string] $ContainerName = "dghsf-knowledge-base",
    [string] $IndexName = "dghsf-knowledge-base",
    # How often the indexer picks up new, changed and deleted files (ISO 8601, at least 5 minutes).
    [string] $Schedule = "PT15M"
)

$ErrorActionPreference = "Stop"
$apiVersion = "2024-07-01"
$templates = Join-Path $PSScriptRoot "..\docs\knowledge-base-search"
$baseUrl = "https://$SearchServiceName.search.windows.net"

# Every value is put into a JSON string (except the dimensions), so escape it as JSON.
function ConvertTo-JsonText([string] $value) {
    return (ConvertTo-Json $value -Compress).Trim('"')
}

$values = @{
    IndexName               = ConvertTo-JsonText $IndexName
    ContainerName           = ConvertTo-JsonText $ContainerName
    StorageConnectionString = ConvertTo-JsonText $StorageConnectionString
    OpenAIEndpoint          = ConvertTo-JsonText $OpenAIEndpoint.TrimEnd('/')
    OpenAIKey               = ConvertTo-JsonText $OpenAIKey
    EmbeddingDeployment     = ConvertTo-JsonText $EmbeddingDeployment
    EmbeddingModel          = ConvertTo-JsonText $EmbeddingModel
    Dimensions              = $Dimensions.ToString([Globalization.CultureInfo]::InvariantCulture)
    Schedule                = ConvertTo-JsonText $Schedule
}

function Publish-SearchObject([string] $collection, [string] $template) {
    $json = Get-Content (Join-Path $templates $template) -Raw
    foreach ($key in $values.Keys) {
        $json = $json.Replace("{{$key}}", $values[$key])
    }
    if ($json -match '\{\{\w+\}\}') {
        throw "$template still has a placeholder: $($Matches[0])"
    }

    $name = ($json | ConvertFrom-Json).name
    Write-Host "Publishing $collection/$name ..."
    Invoke-RestMethod -Method Put -Uri "$baseUrl/$collection/$($name)?api-version=$apiVersion" `
        -Headers @{ "api-key" = $SearchAdminKey; "Prefer" = "return=minimal" } `
        -ContentType "application/json; charset=utf-8" -Body ([Text.Encoding]::UTF8.GetBytes($json)) | Out-Null
}

Publish-SearchObject "datasources" "datasource.json"
Publish-SearchObject "indexes" "index.json"
Publish-SearchObject "skillsets" "skillset.json"
Publish-SearchObject "indexers" "indexer.json"

Write-Host ""
Write-Host "Done. The indexer runs every $Schedule; run it now with:"
Write-Host "  Invoke-RestMethod -Method Post -Uri '$baseUrl/indexers/$IndexName-indexer/run?api-version=$apiVersion' -Headers @{ 'api-key' = '<admin key>' }"
Write-Host "Then set KnowledgeSearch__Endpoint=$baseUrl, KnowledgeSearch__ApiKey=<a query key> and KnowledgeSearch__IndexName=$IndexName in DGHSF."
