$proj = "D:/Unity/Project/ProjectHKiBTest/ProjectHKiB_Re"
$out  = "C:/Users/a/AppData/Local/Temp/claude/D--Unity-Project-ProjectHKiBTest-ProjectHKiB-Re/85c87e2c-0836-4a5b-8519-d0a9cc914027/scratchpad"
$bs = [string][char]92
$fs = [string][char]47

$excludeThirdParty = @(
  ($bs + 'NodeGraphProcessor-1.3.0' + $bs),
  ($bs + 'NaughtyAttributes' + $bs),
  ($bs + 'SerializedCollections' + $bs),
  ($bs + 'Assets' + $bs + 'Packages' + $bs)
)

$all = Get-ChildItem -Path "$proj/Assets" -Recurse -Filter *.cs -File | ForEach-Object { $_.FullName }
$editorSrc = @($all | Where-Object { $p = $_; -not ($excludeThirdParty | Where-Object { $p.Contains($_) }) })

$hints = @()
foreach ($cs in @("$proj/Assembly-CSharp.csproj", "$proj/Assembly-CSharp-Editor.csproj")) {
  $txt = Get-Content $cs -Raw
  [regex]::Matches($txt, '<HintPath>(.*?)</HintPath>') | ForEach-Object { $hints += $_.Groups[1].Value }
}
$refs = @{}
foreach ($h in $hints) {
  $p = $h.Replace($bs, $fs)
  $p = $p.Replace('C:/Program Files/Unity/Hub/Editor/2021.3.45f2/', 'D:/Unity/Editor/2021.3.45f2/')
  $name = Split-Path $p -Leaf
  if (-not $refs.ContainsKey($name)) { $refs[$name] = $p }
}
Get-ChildItem "$proj/Library/ScriptAssemblies" -Filter *.dll -File | ForEach-Object {
  if ($_.Name -like 'Assembly-CSharp*') { return }
  if (-not $refs.ContainsKey($_.Name)) { $refs[$_.Name] = $_.FullName.Replace($bs, $fs) }
}

$defTxt = Get-Content "$proj/Assembly-CSharp-Editor.csproj" -Raw
$defines = ([regex]::Match($defTxt, '<DefineConstants>(.*?)</DefineConstants>')).Groups[1].Value

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add('-target:library')
$lines.Add('-out:"' + $out + '/AsmEditorCheck.dll"')
$lines.Add('-nologo')
$lines.Add('-langversion:9.0')
$lines.Add('-nostdlib+')
$lines.Add('-noconfig')
$lines.Add('-define:' + $defines)
foreach ($k in $refs.Keys) { $lines.Add('-r:"' + $refs[$k] + '"') }
foreach ($s in $editorSrc) { $lines.Add('"' + $s.Replace($bs, $fs) + '"') }
Set-Content -Path "$out/editor.rsp" -Value $lines -Encoding utf8
Write-Output ("sources=" + $editorSrc.Count + " refs=" + $refs.Count)
Write-Output ("defines=" + $defines)
