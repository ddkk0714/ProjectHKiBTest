using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// 시각·물리 연출과 함께 선택적으로 재생하는 SO 기반 원샷 효과음입니다.
/// AudioData가 비어 있으면 기존 연출 동작을 바꾸지 않습니다.
/// </summary>
[Serializable]
public sealed class EffectAudioCue
{
    [Tooltip("연출과 함께 재생할 효과음 SO입니다. 비워 두면 무음으로 실행합니다.")]
    [SerializeField]
    private AudioDataSO _audioData;

    // AudioPlayer.Play는 AudioSource.volume과 PlayOneShot의 volume scale을 **둘 다** 적용해
    // 실효 게인이 volume²이 된다. 게다가 원샷은 AudioSource 하나를 공유하므로 여기서 볼륨을 낮추면
    // 아직 울리고 있는 다른 효과음까지 같이 줄어든다. 큐 사이의 균형은 음원 파일 자체에서 맞추고
    // 이 값은 1로 두는 것이 안전하다.
    [Tooltip("이 연출에서 사용할 효과음 볼륨입니다. 큐 사이의 균형은 음원 파일에서 맞추고 여기는 1로 두세요.")]
    [Range(0f, 1f)]
    [SerializeField]
    private float _volume = 1f;

    [Tooltip("연출 시작 후 이 시간(초)만큼 지난 뒤 재생합니다. 0이면 같은 프레임에 재생합니다.")]
    [Min(0f)]
    [SerializeField]
    private float _delay;

    public AudioDataSO AudioData => _audioData;
    public float Volume => _volume;
    public float Delay => _delay;
    public bool HasAudio => _audioData != null && _volume > 0f;

#if UNITY_EDITOR
    /// <summary>에디터 자동 배선 도구가 중첩 직렬화 값을 갱신할 때 사용합니다.</summary>
    public void Configure(AudioDataSO audioData, float volume = 1f, float delay = 0f)
    {
        _audioData = audioData;
        _volume = Mathf.Clamp01(volume);
        _delay = Mathf.Max(0f, delay);
    }
#endif

    /// <summary>
    /// 현재 프로젝트의 AudioManager 원샷 경로로 효과음을 재생합니다.
    /// 화면 연출 미리보기처럼 실행 주체가 없으면 2D AudioSource이므로 원점을 사용합니다.
    /// </summary>
    public void Play(StateController owner = null)
    {
        if (!HasAudio) return;

        if (_audioData.type == null)
        {
            Debug.LogWarning($"[EffectAudioCue] '{_audioData.name}'에 AudioTypeSO가 없습니다.", _audioData);
            return;
        }

        if (!_audioData.type.playOneShot)
        {
            Debug.LogWarning($"[EffectAudioCue] '{_audioData.name}'은 원샷 타입이 아니어서 재생하지 않습니다.", _audioData);
            return;
        }

        if (_audioData.audioClips == null || _audioData.audioClips.Length == 0)
        {
            Debug.LogWarning($"[EffectAudioCue] '{_audioData.name}'에 재생할 AudioClip이 없습니다.", _audioData);
            return;
        }

        GameManager gameManager = GameManager.instance;
        AudioManager audioManager = gameManager != null ? gameManager.audioManager : null;
        if (audioManager == null)
        {
            Debug.LogWarning($"[EffectAudioCue] '{_audioData.name}'을 재생할 AudioManager가 없습니다.");
            return;
        }

        Vector3 position = owner != null ? owner.transform.position : Vector3.zero;

        if (_delay > 0f)
        {
            // AudioManager가 파괴되면 코루틴도 함께 죽으므로 씬 전환 뒤에 뒤늦게 울릴 일이 없다.
            audioManager.StartCoroutine(PlayAfterDelay(audioManager, position));
            return;
        }

        audioManager.PlayAudioOneShot(_audioData, _volume, position);
    }

    // 컷신은 timeScale을 0으로 두는 경우가 있어 WaitForSeconds는 영원히 끝나지 않는다.
    // 화면 효과가 모두 unscaled time을 쓰는 것과 같은 기준으로 맞춘다.
    private IEnumerator PlayAfterDelay(AudioManager audioManager, Vector3 position)
    {
        yield return new WaitForSecondsRealtime(_delay);

        if (audioManager == null) yield break;
        audioManager.PlayAudioOneShot(_audioData, _volume, position);
    }
}
