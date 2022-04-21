
$root = $github.workspace
if(Test-Path $root){
    try{
        push-location
        set-location $root

        . ./build.ps1 PsModule Publish

        echo "::set-output name=workflow::./publish/workflow"
    } finally {
        pop-location
    }
} else {
    Write-Error $_
    throw "`$root is invalid: $root"
}