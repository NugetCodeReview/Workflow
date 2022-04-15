push-location
.\build.ps1 Clean PsModule
pop-location

$path="src\Workflow.Commands\bin\Debug\netstandard2.0"

if(Test-Path $path){
    Set-Location $path

    $assembly = ".\Workflow-Commands.dll"
    if(Test-Path $assembly){
        Import-Module $assembly

       Get-TopPackages -IncludeDetails -Force `
        | where -Property IsWhitelisted -not `
        | Format-Table -Property Rank, PackageName, OwnerNames, Repository;
    }
}