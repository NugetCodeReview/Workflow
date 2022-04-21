Write-Host "bootstrap: Entered"
$root = $github.workspace
Write-Host "`$root: $root"
if(Test-Path $root){
    try{
        push-location -Verbose
        set-location $root -Verbose
        Get-Location -Verbose
        dir -Verbose

        if(Test-Path ./build.ps1){
            $build = get-item ./build.ps1
            Write-Host "Before build.ps1"
            Write-Host "./build.ps1 PsModule Publish -Verbose"
            & $build PsModule Publish -Verbose
            $last = $LASTEXITCODE
            Write-Host "After build.ps1 ($last)"
        } else {
            Write-Host "Cannot find ./build.ps1"
        }

        $found = $(Test-Path $root/publish/Workflow)
        Write-Host "`$found: $found"

        echo "::set-output name=workflow::./publish/Workflow"
    } finally {
        Write-Host "bootstrap: Finally"
        pop-location
        Exit
    }
} else {
    Write-Error $_
    throw "`$root is invalid: $root"
}
Write-Host "bootstrap: Exiting"
