# Pre-boot check. Exit 1 if anything is wrong.
$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$Game = "C:\Program Files (x86)\Steam\steamapps\common\7 Days To Die"
$fail = 0

function Fail($msg) {
    Write-Host "FAIL  $msg" -ForegroundColor Red
    $script:fail++
}
function Ok($msg) { Write-Host "ok    $msg" -ForegroundColor Green }

$vanillaItems = [regex]::Matches((Get-Content (Join-Path $Game "Data\Config\items.xml") -Raw), '<item name="([^"]+)"') | ForEach-Object { $_.Groups[1].Value }
$known = @{}
foreach ($n in $vanillaItems) { $known[$n] = $true }

$itemsXml = Get-Content (Join-Path $Root "Config\items.xml") -Raw
$recipeXml = Get-Content (Join-Path $Root "Config\recipes.xml") -Raw
$blocksPath = Join-Path $Root "Config\blocks.xml"
$locXml = Get-Content (Join-Path $Root "Config\Localization.txt") -Raw
try { [xml]$itemsXml | Out-Null; Ok "items.xml" } catch { Fail "items.xml: $_" }
try { [xml]$recipeXml | Out-Null; Ok "recipes.xml" } catch { Fail "recipes.xml: $_" }
if (-not (Test-Path $blocksPath)) { Fail "blocks.xml missing" }
else {
    $blocksXml = Get-Content $blocksPath -Raw
    try { [xml]$blocksXml | Out-Null; Ok "blocks.xml" } catch { Fail "blocks.xml: $_" }
    foreach ($anchor in @("HSVehicleDecoAnchorBicycle", "HSVehicleDecoAnchorMinibike", "HSVehicleDecoAnchorMotorcycle", "HSVehicleDecoAnchorTruck4x4", "HSVehicleDecoAnchorGyrocopter")) {
        if ($blocksXml -notmatch [regex]::Escape("block name=`"$anchor`"")) { Fail "blocks.xml missing $anchor" }
    }
    if ($blocksXml -notmatch "HSVehicleDeco, HSVehicleAsDecoration") { Fail "anchor class missing" } else { Ok "anchor class" }
    if ($blocksXml -match "CanPickup`" value=`"true`"") { Fail "anchor can be picked up" } else { Ok "anchors cannot be picked up" }
}

$expected = @{
    HSVehicleDecoBicycle = 2
    HSVehicleDecoMinibike = 2
    HSVehicleDecoMotorcycle = 2
    HSVehicleDecoTruck4x4 = 4
    HSVehicleDecoGyrocopter = 3
}
$parents = @{
    HSVehicleDecoBicycle = "vehicleBicyclePlaceable"
    HSVehicleDecoMinibike = "vehicleMinibikePlaceable"
    HSVehicleDecoMotorcycle = "vehicleMotorcyclePlaceable"
    HSVehicleDecoTruck4x4 = "vehicleTruck4x4Placeable"
    HSVehicleDecoGyrocopter = "vehicleGyrocopterPlaceable"
}
$slots = @{
    HSVehicleDecoBicycle = 2
    HSVehicleDecoMinibike = 3
    HSVehicleDecoMotorcycle = 4
    HSVehicleDecoTruck4x4 = 5
    HSVehicleDecoGyrocopter = 5
}

if ($recipeXml -match '<ingredient name="(smallEngine|carBattery)"') { Fail "recipe still has an engine or battery" } else { Ok "no engine or battery" }

$recipes = [regex]::Matches($recipeXml, '<recipe name="([^"]+)"') | ForEach-Object { $_.Groups[1].Value }
if ($recipes.Count -ne 5) { Fail "expected 5 recipes, found $($recipes.Count)" } else { Ok "5 recipes" }

foreach ($name in $expected.Keys) {
    if ($itemsXml -notmatch [regex]::Escape("item name=`"$name`"")) { Fail "items.xml missing $name" }
    if ($recipeXml -notmatch [regex]::Escape("recipe name=`"$name`"")) { Fail "recipes.xml missing $name" }
    if ($locXml -notmatch [regex]::Escape($name + ",")) { Fail "localization missing $name" }
    $parent = $parents[$name]
    if (-not $known.ContainsKey($parent)) { Fail "parent item missing $parent" }
    if ($itemsXml -notmatch [regex]::Escape("Extends`" value=`"$parent`"")) { Fail "$name does not extend $parent" } else { Ok "extends $parent" }
    if ($itemsXml -notmatch [regex]::Escape("CustomIcon`" value=`"$parent`"")) { Fail "$name missing icon $parent" } else { Ok "icon $parent" }
    $item = [regex]::Match($itemsXml, "(?s)<item name=`"$name`".*?</item>").Value
    if ($item -notmatch "ModSlots[\s\S]*?value=`"$($slots[$name])`"") { Fail "$name mod slots" } else { Ok "$name mod slots $($slots[$name])" }
    $chunk = [regex]::Match($recipeXml, "(?s)<recipe name=`"$name`".*?</recipe>").Value
    $wheels = [regex]::Match($chunk, 'name="vehicleWheels" count="(\d+)"')
    if (-not $wheels.Success -or [int]$wheels.Groups[1].Value -ne $expected[$name]) {
        Fail "$name wheels expected $($expected[$name])"
    } else { Ok "$name wheels $($expected[$name])" }
    foreach ($ing in ([regex]::Matches($chunk, '<ingredient name="([^"]+)"') | ForEach-Object { $_.Groups[1].Value })) {
        if (-not $known.ContainsKey($ing)) { Fail "ingredient unknown '$ing'" }
    }
}

if ($locXml -notmatch "HSVehicleDecoDesc") { Fail "localization missing description" } else { Ok "description" }

$dll = Join-Path $Root "HSVehicleAsDecoration.dll"
if (-not (Test-Path $dll)) { Fail "HSVehicleAsDecoration.dll missing" }
else {
    $ascii = [Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($dll))
    foreach ($t in @("HSVehicleAsDecorationMod", "HSVehicleDecoration", "IsShell", "isDriveable", "AddFuelFromInventory", "KeepUpright", "BlockHSVehicleDeco", "AddTrackedVehicle")) {
        if ($ascii.IndexOf($t) -lt 0) { Fail "DLL missing '$t'" } else { Ok "dll $t" }
    }
}

if ($fail -gt 0) {
    Write-Host ("{0} check(s) failed." -f $fail) -ForegroundColor Red
    exit 1
}
Write-Host "All checks passed." -ForegroundColor Green
exit 0
