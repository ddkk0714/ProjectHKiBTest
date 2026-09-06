# EventChain.asset 은 events 구조와 references(SerializeReference) 블록이 분리돼 있어
# 행 위치로는 액션을 이벤트에 매핑할 수 없다. rid 로 이어 붙인다.
# TargetEntityManipulateAction 처럼 data 안에 다시 rid 를 품는 액션도 재귀로 따라간다.
param(
    [string]$Path = "D:\Unity\Project\ProjectHKiBTest\ProjectHKiB_Re\Assets\Scripts\Event\Test\Generated\EventChain.asset",
    [string[]]$Classes = @('CameraShakeAction', 'KnockBackAction', 'ScreenFlashAction', 'ScreenTearAction', 'ScreenGlitchAction', 'ScreenNoiseAction')
)

$lines = Get-Content $Path

# references 블록 시작 위치
$refStart = -1
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match '^\s*RefIds:\s*$') { $refStart = $i; break }
}
if ($refStart -lt 0) { throw "RefIds 블록을 찾지 못했습니다." }

# ── references: rid -> class, data, 자식 rid ───────────────────────────
$cls = @{}; $data = @{}; $kids = @{}
$cur = $null
for ($i = $refStart; $i -lt $lines.Count; $i++) {
    $l = $lines[$i]
    if ($l -match '^\s*- rid:\s*(-?\d+)\s*$') { $cur = $Matches[1]; $data[$cur] = @(); $kids[$cur] = @(); continue }
    if ($null -eq $cur) { continue }
    if ($l -match 'type:\s*\{class:\s*([A-Za-z0-9_]+)') { $cls[$cur] = $Matches[1]; continue }
    if ($l -match 'rid:\s*(-?\d+)') { $kids[$cur] += $Matches[1]; continue }
    if ($l -match '^\s{6,}([A-Za-z_][A-Za-z0-9_]*):\s*(.*)$') { $data[$cur] += "$($Matches[1])=$($Matches[2])" }
}

# ── events: eventId / step label 별 최상위 rid ─────────────────────────
$use = @()   # {Event, Step, Rid}
$ev = '?'; $step = '?'
for ($i = 0; $i -lt $refStart; $i++) {
    $l = $lines[$i]
    if ($l -match '^\s*- eventId:\s*(\S+)') { $ev = $Matches[1]; $step = '(이벤트 직속)'; continue }
    if ($l -match '^\s*- label:\s*(.+)$') {
        $step = $Matches[1].Trim('"')
        # 유니코드 이스케이프 복원
        $step = [regex]::Replace($step, '\\u([0-9A-Fa-f]{4})', { param($m) [char][Convert]::ToInt32($m.Groups[1].Value, 16) })
        continue
    }
    if ($l -match 'rid:\s*(-?\d+)') { $use += [pscustomobject]@{ Event = $ev; Step = $step; Rid = $Matches[1] } }
}

function Expand-Rid([string]$rid, [int]$depth = 0) {
    if ($depth -gt 4) { return @() }
    $out = @([pscustomobject]@{ Rid = $rid; Depth = $depth })
    foreach ($k in $kids[$rid]) { $out += Expand-Rid $k ($depth + 1) }
    return $out
}

$rows = @()
foreach ($u in $use) {
    foreach ($e in Expand-Rid $u.Rid) {
        $c = $cls[$e.Rid]
        if (-not $c -or $Classes -notcontains $c) { continue }
        $keys = @('strength', 'acceleration', 'accelDuration', 'knockbackFriction', 'directionMode',
                  'intensity', 'duration', 'tiling', 'alpha', 'stop', 'shakeStrength', 'shakeDuration', 'shakeCount')
        $kv = ($data[$e.Rid] | Where-Object { $k = ($_ -split '=')[0]; $keys -contains $k }) -join ' '
        $rows += [pscustomobject]@{
            Event  = $u.Event
            Step   = $u.Step
            Action = $c + $(if ($e.Depth -gt 0) { ' (중첩)' } else { '' })
            Params = $kv
        }
    }
}

$rows | Where-Object { $_.Event -ne '?' } | Format-Table -AutoSize -Wrap | Out-String -Width 190
