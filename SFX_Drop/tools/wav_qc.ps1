# 후보 WAV의 큐 시트 5장 "필수 통과" 항목 중 기계로 잴 수 있는 것만 측정한다.
# 길이 / 선행 무음 / 피크 / 클리핑 / RMS / 종료 잔향(끝 tail) — 질감 판정은 청감 몫이다.
param(
    [string]$StageDir = "C:/Users/a/AppData/Local/Temp/claude/D--Unity-Project-ProjectHKiBTest-ProjectHKiB-Re/85c87e2c-0836-4a5b-8519-d0a9cc914027/scratchpad/sn_candidates"
)

$ErrorActionPreference = 'Stop'

function Read-Wav([string]$path) {
    $b = [IO.File]::ReadAllBytes($path)
    if ($b.Length -lt 44) { throw "too short: $path" }
    if ([Text.Encoding]::ASCII.GetString($b, 0, 4) -ne 'RIFF') { throw "not RIFF: $path" }

    # fmt / data 청크를 순회해 찾는다(LIST 등 부가 청크가 끼어 있을 수 있다).
    $pos = 12; $fmt = $null; $dataOff = -1; $dataLen = 0
    while ($pos + 8 -le $b.Length) {
        $id = [Text.Encoding]::ASCII.GetString($b, $pos, 4)
        $sz = [BitConverter]::ToInt32($b, $pos + 4)
        if ($id -eq 'fmt ') {
            $fmt = @{
                Channels = [BitConverter]::ToInt16($b, $pos + 10)
                Rate     = [BitConverter]::ToInt32($b, $pos + 12)
                Bits     = [BitConverter]::ToInt16($b, $pos + 22)
            }
        }
        elseif ($id -eq 'data') { $dataOff = $pos + 8; $dataLen = $sz }
        $pos += 8 + $sz + ($sz % 2)
    }
    if ($null -eq $fmt -or $dataOff -lt 0) { throw "missing fmt/data: $path" }
    if ($fmt.Bits -ne 16) { throw "only 16-bit supported ($($fmt.Bits)-bit): $path" }
    if ($dataOff + $dataLen -gt $b.Length) { $dataLen = $b.Length - $dataOff }

    $frames = [int]($dataLen / (2 * $fmt.Channels))
    $mono = New-Object 'double[]' $frames
    for ($i = 0; $i -lt $frames; $i++) {
        $sum = 0.0
        for ($c = 0; $c -lt $fmt.Channels; $c++) {
            $sum += [BitConverter]::ToInt16($b, $dataOff + ($i * $fmt.Channels + $c) * 2)
        }
        $mono[$i] = ($sum / $fmt.Channels) / 32768.0
    }
    return @{ Samples = $mono; Rate = $fmt.Rate; Channels = $fmt.Channels }
}

function To-Db([double]$lin) {
    if ($lin -le 1e-9) { return -999.0 }
    return [Math]::Round(20 * [Math]::Log10($lin), 2)
}

$rows = @()
foreach ($f in Get-ChildItem $StageDir -Filter *.wav -File | Sort-Object Name) {
    try { $w = Read-Wav $f.FullName } catch { Write-Output "SKIP $($f.Name): $_"; continue }

    $s = $w.Samples; $n = $s.Length; $rate = $w.Rate
    $dur = [Math]::Round($n / [double]$rate, 3)

    $peak = 0.0; $sq = 0.0
    for ($i = 0; $i -lt $n; $i++) { $a = [Math]::Abs($s[$i]); if ($a -gt $peak) { $peak = $a }; $sq += $s[$i] * $s[$i] }
    $rms = if ($n -gt 0) { [Math]::Sqrt($sq / $n) } else { 0 }

    # 연속 3샘플 이상 풀스케일이면 클리핑으로 본다.
    $clip = 0; $run = 0
    for ($i = 0; $i -lt $n; $i++) {
        if ([Math]::Abs($s[$i]) -ge 0.9995) { $run++; if ($run -eq 3) { $clip++ } } else { $run = 0 }
    }

    # 선행 무음: 피크의 -40 dB 를 처음 넘는 지점.
    $thr = $peak * 0.01
    $lead = 0
    while ($lead -lt $n -and [Math]::Abs($s[$lead]) -lt $thr) { $lead++ }
    $leadMs = [Math]::Round(1000.0 * $lead / $rate, 1)

    # 후행 잔향: 마지막으로 -40 dB 를 넘은 뒤 남은 꼬리.
    $tail = $n - 1
    while ($tail -ge 0 -and [Math]::Abs($s[$tail]) -lt $thr) { $tail-- }
    $tailMs = [Math]::Round(1000.0 * ($n - 1 - $tail) / $rate, 1)

    $rows += [pscustomobject]@{
        File     = $f.Name
        Sec      = $dur
        Rate     = $rate
        Ch       = $w.Channels
        PeakDb   = To-Db $peak
        RmsDb    = To-Db $rms
        Clips    = $clip
        LeadMs   = $leadMs
        TailMs   = $tailMs
    }
}

$rows | Format-Table -AutoSize | Out-String -Width 200
