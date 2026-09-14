param(
    [string]$DllPath = (Join-Path $PSScriptRoot "..\bin\Debug\Rxdk.MsBuild.dll"),
    [string]$Task
)

Add-Type -AssemblyName $DllPath
$base = "Rxdk.MsBuild.Tasks.RxdkToolTask"
$baseType = "[$base]"
$taskType = "[Rxdk.MsBuild.Tasks.$Task]"

Invoke-Expression "$baseType::DumpLangScaffold$taskType([$base+LangFragmentSettings]::new())"
