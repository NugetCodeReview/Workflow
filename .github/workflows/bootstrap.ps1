Write-Host "bootstrap: Entered"
$root = $env:root ?? (Get-Location)
Write-Host "`$env:root: $env:root"
Write-Host "`$root: $root"
push-location
if(Test-Path $root){
    try{
        $result=Set-Location -Path $root
        Write-Host "`$result: $result"

        $at = Get-Location
        Write-Host "At: $at"
        dir $at

        if(Test-Path ./build.ps1){
            $build = get-item ./build.ps1
            Write-Host "Before build.ps1"
            Write-Host "[$build] PsModule Publish -Verbose"
            . $build PsModule Publish -Verbose
            $last = $LASTEXITCODE
            Write-Host "After build.ps1 ($last)"
            if(Test-Path artifacts){
                dir artifacts
            }
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
