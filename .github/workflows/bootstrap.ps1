Write-Host "Entered bootstrap"
$root = $github.workspace
Write-Host "`$root: $root"
if(Test-Path $root){
    try{
        push-location
        set-location $root
        Get-Location

        Write-Host "Before build.ps1"
        ./build.ps1 PsModule Publish -Verbose
        $last = $LASTEXITCODE
        Write-Host "After build.ps1 ($last)"

        $found = $(Test-Path $root/publish/Workflow)
        Write-Host "`$found: $found"

        echo "::set-output name=workflow::./publish/Workflow"
    } finally {
        pop-location
    }
} else {
    Write-Error $_
    throw "`$root is invalid: $root"
}
Write-Host "Leaving bootstrap"
