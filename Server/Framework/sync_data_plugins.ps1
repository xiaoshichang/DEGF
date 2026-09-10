param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$generatorProject = Join-Path $PSScriptRoot 'DE.Share.DataTableSG/DE.Share.DataTableSG.csproj'
$dataProject = Join-Path $PSScriptRoot 'DE.Share.Data/DE.Share.Data.csproj'

& dotnet build $generatorProject -c $Configuration -v minimal
if ($LASTEXITCODE -ne 0)
{
    throw 'DataTableSG build failed.'
}
& dotnet restore $dataProject
if ($LASTEXITCODE -ne 0)
{
    throw 'Data project restore failed.'
}

$assets = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'DE.Share.Data/obj/project.assets.json') -Raw | ConvertFrom-Json
$library = @($assets.libraries.PSObject.Properties | Where-Object { $_.Name -like 'ExcelDataReader/*' })
if ($library.Count -ne 1)
{
    throw 'Expected exactly one restored ExcelDataReader package.'
}
$excelAssembly = $null
foreach ($folder in $assets.packageFolders.PSObject.Properties.Name)
{
    $candidate = Join-Path $folder ($library[0].Value.path + '/lib/netstandard2.0/ExcelDataReader.dll')
    if (Test-Path -LiteralPath $candidate)
    {
        $excelAssembly = $candidate
        break
    }
}
if ($null -eq $excelAssembly)
{
    throw 'Restored ExcelDataReader netstandard2.0 assembly was not found.'
}

$pluginsDirectory = Join-Path $repositoryRoot 'Client/Demo/Assets/Plugins'
$generatorAssembly = Join-Path $PSScriptRoot "DE.Share.DataTableSG/bin/$Configuration/netstandard2.0/DE.Share.DataTableSG.dll"
Copy-Item -LiteralPath $generatorAssembly -Destination (Join-Path $pluginsDirectory 'DE.Share.DataTableSG.dll') -Force
Copy-Item -LiteralPath $excelAssembly -Destination (Join-Path $pluginsDirectory 'ExcelDataReader.dll') -Force
Write-Output "Updated Unity data plugins from $($library[0].Name) and DataTableSG ($Configuration)."
