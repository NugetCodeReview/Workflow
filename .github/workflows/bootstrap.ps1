param([string]$Configuration="Debug")

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
            . $build PsModule Publish -Configuration $Configuration -RootDirectory $env:artifacts
            $last = $LASTEXITCODE
            Write-Host "After build.ps1 ($last)"
            if($last -ne 0) { throw "$build returned exit code $last"; }
            if(Test-Path artifacts){
                dir artifacts
            }
        } else {
            Write-Host "Cannot find ./build.ps1"
        }

        $found = $(Test-Path $root/artifacts/workflow/Workflow)
        Write-Host "`$found: $found"

        $workflowPath = (Get-ChildItem Workflow -Path $root/artifacts/workflow).FullName
        echo "::set-output name=workflow::$workflowPath"
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
