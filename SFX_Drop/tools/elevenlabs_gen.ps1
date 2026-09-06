# 화면 노이즈 큐 3종을 ElevenLabs Sound Effects로 생성해 스테이징 폴더에 내려받는다.
# 키는 $env:ELEVENLABS_API_KEY 에서만 읽고 절대 출력하지 않는다.
# 프로젝트 Assets 에는 쓰지 않는다 — 후보 선정이 끝난 파일만 나중에 복사한다.
param(
    [int]$Variants = 3,
    [int]$StartIndex = 1,
    [string[]]$Only = @(),                          # 예: -Only Cut_1500,Death_0600
    [ValidateSet('analog', '8bit', 'snap', 'impact', 'impact2')]
    [string]$Style = 'analog',                      # analog = 큐 시트 §4, 8bit = 레트로 노이즈 채널, snap = 종료 "픽!", impact = 흔들림/섬광/넉백
    [string]$PromptSuffix = '',                     # 재생성 시 프롬프트 끝에 덧붙일 지시
    # 주의: PowerShell 변수는 대소문자를 구분하지 않는다. 파라미터를 $Tag 로 두면
    # 아래 $tag = ... 한 줄이 파라미터 자신을 덮어써 -Tag 가 무시된 것처럼 보인다.
    [ValidateSet('dry', 'crt')]
    [string]$SnapPrompt = 'dry',                    # snap 스타일 프롬프트 선택 (파일명 태그로도 쓴다)
    [double]$Influence = -1,                        # prompt influence override (-1 = 큐 기본값)
    [string]$StageDir = "C:/Users/a/AppData/Local/Temp/claude/D--Unity-Project-ProjectHKiBTest-ProjectHKiB-Re/85c87e2c-0836-4a5b-8519-d0a9cc914027/scratchpad/sn_candidates"
)

$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$key = $env:ELEVENLABS_API_KEY
if ([string]::IsNullOrWhiteSpace($key)) {
    Write-Output "MISSING_KEY: set ELEVENLABS_API_KEY in the environment first."
    exit 2
}

if (-not (Test-Path $StageDir)) { New-Item -ItemType Directory -Path $StageDir | Out-Null }

# 큐 시트 Docs/Screen_Noise_Audio_Cue_Sheet.md 4장 그대로.
$cues = @(
    @{
        Id       = 'Cut_1500'
        Duration = 1.5
        Influence= 0.45
        Text     = 'A single 1.5-second burst of analog television static for a dark surreal video game. It cuts in instantly with dense dry crackle, holds with unstable fine noise, then ends cleanly. Medium intensity, close and centered, no glitch stutter, no impact, no whoosh, no music, no voice, no UI beep, minimal reverb.'
    },
    @{
        Id       = 'Eye_2400'
        Duration = 2.4
        Influence= 0.45
        Text     = 'A single 2.4-second bed of fine-grain CRT television static for an unsettling eye close-up in a dark surreal video game. Thin high-frequency hiss and delicate electrical crackle slowly become slightly more tense, then stop cleanly. Controlled medium intensity, dry and centered, no impact, no glitch cuts, no melody, no voice, no UI beep, no long reverb.'
    },
    @{
        Id       = 'Death_0600'
        Duration = 0.6
        Influence= 0.55
        Text     = 'A single 0.6-second violent analog TV static burst as a video signal abruptly dies in a dark horror game. Immediate harsh broadband crackle, dense electrical noise, and a very fast dry cutoff. Strong intensity but no bass boom, no digital glitch rhythm, no power-down tone, no music, no voice, no UI beep, no reverb tail.'
    }
)

# ElevenLabs 의 pcm_44100 은 16-bit **스테레오** 인터리브다.
# (실측: 0.6초 요청 -> 105840 바이트 = 44100 * 2ch * 2byte * 0.6)
# 채널 수를 1로 적으면 2배 느리게, 좌우가 뒤섞여 재생된다.
function Write-WavFromPcm16([byte[]]$pcm, [int]$rate, [int]$channels, [string]$path) {
    $blockAlign = $channels * 2
    $fs = [IO.File]::Create($path)
    $bw = New-Object IO.BinaryWriter($fs)
    $bw.Write([char[]]'RIFF'); $bw.Write([int](36 + $pcm.Length))
    $bw.Write([char[]]'WAVE'); $bw.Write([char[]]'fmt ')
    $bw.Write([int]16); $bw.Write([int16]1); $bw.Write([int16]$channels)
    $bw.Write([int]$rate); $bw.Write([int]($rate * $blockAlign))
    $bw.Write([int16]$blockAlign); $bw.Write([int16]16)
    $bw.Write([char[]]'data'); $bw.Write([int]$pcm.Length); $bw.Write($pcm)
    $bw.Close(); $fs.Close()
}

# 8비트 대체 세트. 길이·용도는 같고 질감만 레트로 사운드칩 노이즈 채널로 바꾼다.
# 8비트 소재는 UI 비프/멜로디로 새기 쉬워(큐 시트 §5 탈락 조건) 금지 항목을 더 강하게 적었고,
# prompt influence 도 아날로그 세트보다 올려 프롬프트 준수를 강제한다.
$cues8bit = @(
    @{
        Id       = 'Cut_1500'
        Duration = 1.5
        Influence= 0.60
        # 프롬프트 상한(450자)에 -PromptSuffix 를 얹을 여유를 두려고
        # "no chiptune tune" / "no coin or jump sound" 를 뺐다 —
        # "no melody, no musical notes, no arpeggio" 와 의미가 겹친다.
        # 8bit01~03 은 그 두 구절이 있던 긴 버전으로 생성됐다.
        Text     = 'A single 1.5-second burst of 8-bit game console noise-channel static, like a corrupted NES screen. Harsh bit-crushed digital hiss from a low-resolution LFSR noise generator, coarse and grainy at a low sample rate, cutting in instantly and stopping cleanly. Medium intensity, dry and centered, no melody, no musical notes, no arpeggio, no voice, no reverb.'
    },
    @{
        Id       = 'Eye_2400'
        Duration = 2.4
        Influence= 0.60
        Text     = 'A single 2.4-second bed of 8-bit console noise-channel hiss for an unsettling eye close-up. Thin high-pitched bit-crushed digital grain from a retro sound chip, quantized and lo-fi, slowly growing slightly more tense, then stopping cleanly. Controlled medium intensity, dry and centered, no melody, no musical notes, no arpeggio, no chiptune tune, no UI confirm beep, no voice, no reverb.'
    },
    @{
        Id       = 'Death_0600'
        Duration = 0.6
        Influence= 0.60
        Text     = 'A single 0.6-second violent 8-bit console noise-channel burst as a retro game screen dies. Immediate harsh bit-crushed digital static from a low-resolution noise generator, dense and coarse, with a very fast hard cutoff. Strong intensity, no bass boom, no descending power-down melody, no musical notes, no arpeggio, no explosion, no voice, no reverb tail.'
    }
)

# 노이즈가 끊기는 순간의 "픽!". 화면은 NoiseRoutine 이 duration 에서 페이드 없이 곧장 끄므로
# 어택이 즉시 서고 잔향 없이 무음으로 떨어져야 영상과 붙는다.
# ElevenLabs duration 하한이 0.5초라 0.5초로 받고 후처리에서 잘라 쓴다.
$cuesSnap = @(
    @{
        Id       = 'Snap_0500'
        Duration = 0.5
        Influence= 0.50
        Text     = 'A single short dry snap as an old CRT television is switched off and the video signal dies instantly. One tight electrical pop with a very fast click transient collapsing straight into silence. No music, no voice, no UI beep, no bass boom, no reverb tail, no power-down melody.'
    }
)

# CRT 전원 차단음을 더 정확히 지시한 2차 프롬프트.
# 1차는 "마른 팝"만 요청해 브라운관 특유의 수축 휘잉과 낮은 툭이 빠졌다.
# 저역을 완전히 금지하지 않는다 — 실제 CRT 차단음에는 플라이백이 멎는 낮은 툭이 있다.
$cuesSnapCrt = @(
    @{
        Id       = 'Snap_0500'
        Duration = 0.5
        Influence= 0.55
        Text     = 'An analog CRT television powering off: one sharp electrostatic pop as the picture collapses to a point, with a brief high-frequency whine dropping away and a soft low thunk, then silence. Dry, close and centered. No music, no voice, no UI beep, no melody, no explosion, no long reverb tail.'
    }
)

# @(...) 를 반드시 씌운다 — 스크립트블록에서 나온 단일 원소 배열은 PowerShell 이 언롤해
# $cues 가 배열이 아니라 해시테이블 하나가 돼 버린다.
# 흔들림 / 섬광 / 넉백.
# Shake 와 Flash 는 EVT-002 보스화에서 같은 프레임에 겹치므로 주파수 대역을 갈라 둔다 —
# Shake 는 저역 몸통, Flash 는 고역 반짝임. 둘 다 저역을 채우면 어택이 뭉개진다.
$cuesImpact = @(
    @{
        Id       = 'Shake_0600'
        Duration = 0.6
        Influence= 0.50
        Text     = 'A single deep dry thud as something heavy strikes once. Fast low-frequency impact with a short tight decay and no ringing afterwards. Close and centered, moderate weight, not an explosion. No music, no voice, no UI beep, no metallic ring, no debris, no reverb tail.'
    },
    @{
        Id       = 'Flash_0500'
        Duration = 0.5
        Influence= 0.50
        Text     = 'A single bright sharp sting as a burst of white light erupts. Fast shimmering high-frequency attack with a short clean decay into silence. Bright and airy, dry and centered. No bass boom, no low rumble, no music, no melody, no voice, no UI beep, no long reverb tail.'
    },
    @{
        Id       = 'Knockback_0900'
        Duration = 0.9
        Influence= 0.50
        Text     = 'A single heavy body impact as a large figure is thrown backward and slides across the ground. Deep weighty thud on contact, then a short dragging scrape that fades out. Close, dry and centered. No music, no voice, no UI beep, no metallic clang, no explosion, no long reverb tail.'
    }
)

# impact 1차 실패 교훈: "deep low-frequency impact" 라고 쓰면 저역 100% 순수 서브가 나온다.
# CRT 프롬프트가 "soft low thunk" 하나만 잡아 저역 100% 가 됐던 것과 같은 실패다.
# 대역을 형용사로 지정하지 말고 **물체와 재질**로 묘사해야 중역 노크가 함께 나온다.
# 넉백의 미끄러짐도 1차에서는 통째로 빠져(뒤 500ms 무음) 지속 시간을 명시했다.
$cuesImpact2 = @(
    @{
        Id       = 'Shake_0600'
        Duration = 0.6
        Influence= 0.50
        Text     = 'A single solid dry thud, like a heavy wooden crate dropped once onto a hard floor. Clear percussive knock with woody mid-range body and a short weighty low end, decaying fast into silence. Close and centered. No music, no voice, no UI beep, no metallic ring, no explosion, no reverb tail.'
    },
    @{
        Id       = 'Flash_0500'
        Duration = 0.5
        Influence= 0.50
        Text     = 'A single bright sharp sting as a burst of white light erupts. Fast shimmering high-frequency attack with a short clean decay into silence. Bright and airy, dry and centered. No bass boom, no low rumble, no music, no melody, no voice, no UI beep, no long reverb tail.'
    },
    @{
        Id       = 'Knockback_0900'
        Duration = 0.9
        Influence= 0.50
        Text     = 'A heavy body hits the ground and drags. One solid muffled impact of cloth and flesh, then immediately a long gritty scraping slide across rough concrete that keeps rasping for half a second before fading. Close and dry. No music, no voice, no UI beep, no metallic clang, no explosion, no reverb tail.'
    }
)

if ($Style -eq '8bit') { $cues = @($cues8bit) }
if ($Style -eq 'impact') { $cues = @($cuesImpact) }
if ($Style -eq 'impact2') { $cues = @($cuesImpact2) }
if ($Style -eq 'snap') {
    if ($SnapPrompt -eq 'crt') { $cues = @($cuesSnapCrt) } else { $cues = @($cuesSnap) }
}

$fileTag = switch ($Style) {
    'impact'  { 'imp' }
    'impact2' { 'imp' }
    '8bit' { '8bit' }
    'snap' { $SnapPrompt }
    default { 'cand' }
}

Write-Output "STYLE=$Style SNAPPROMPT=[$SnapPrompt] TAG=[$fileTag] PROMPT_HEAD=[$($cues[0].Text.Substring(0, 40))]"

$made = 0
foreach ($cue in $cues) {
    if ($Only.Count -gt 0 -and $Only -notcontains $cue.Id) { continue }
    for ($i = $StartIndex; $i -lt ($StartIndex + $Variants); $i++) {
        $prefix = if ($Style -like "impact*") { "SFX_EVT_" } else { "SFX_EVT_ScreenNoise_" }
        $stem = "$prefix$($cue.Id)_$fileTag$('{0:D2}' -f $i)"
        $prompt = if ($PromptSuffix) { $cue.Text + ' ' + $PromptSuffix } else { $cue.Text }
        # 실측: 437자는 통과, 477자는 400 Bad Request(본문 없음). 상한은 450자 근처다.
        if ($prompt.Length -gt 437) {
            Write-Output "PROMPT_TOO_LONG $($cue.Id): $($prompt.Length)자 — 437자 이하로 줄이세요."
            continue
        }

        $body = @{
            text             = $prompt
            duration_seconds = $cue.Duration
            prompt_influence = if ($Influence -ge 0) { $Influence } else { $cue.Influence }
        } | ConvertTo-Json -Compress

        # pcm_44100 은 유료 등급이 필요할 수 있어 실패하면 mp3 로 내려받는다.
        $ok = $false
        foreach ($fmt in @('pcm_44100', 'mp3_44100_128')) {
            $uri = "https://api.elevenlabs.io/v1/sound-generation?output_format=$fmt"
            $tmp = Join-Path $StageDir "$stem.bin"
            try {
                Invoke-WebRequest -Uri $uri -Method Post `
                    -Headers @{ 'xi-api-key' = $key; 'Content-Type' = 'application/json' } `
                    -Body ([Text.Encoding]::UTF8.GetBytes($body)) `
                    -OutFile $tmp -UseBasicParsing -TimeoutSec 180 | Out-Null
            }
            catch {
                $msg = $_.Exception.Message
                if ($msg -match '40[13]') { Write-Output "AUTH_OR_TIER_ERROR on $stem ($fmt)" }
                else { Write-Output "REQUEST_FAILED $stem ($fmt): $msg" }
                if (Test-Path $tmp) { Remove-Item $tmp -Force }
                continue
            }

            if ($fmt -eq 'pcm_44100') {
                $bytes = [IO.File]::ReadAllBytes($tmp)
                Write-WavFromPcm16 $bytes 44100 2 (Join-Path $StageDir "$stem.wav")
                Remove-Item $tmp -Force
                $sec = [Math]::Round($bytes.Length / (44100.0 * 4), 3)
                Write-Output "OK $stem.wav (pcm_44100 stereo -> wav, ${sec}s)"
            }
            else {
                Move-Item $tmp (Join-Path $StageDir "$stem.mp3") -Force
                Write-Output "OK $stem.mp3 (mp3 fallback - needs wav conversion before import)"
            }
            $ok = $true; $made++
            break
        }
        if (-not $ok) { Write-Output "FAILED $stem" }
    }
}

Write-Output "DONE files=$made stage=$StageDir"
