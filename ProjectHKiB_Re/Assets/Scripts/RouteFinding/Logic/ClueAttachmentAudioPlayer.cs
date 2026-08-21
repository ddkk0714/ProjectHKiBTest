using System;
using System.Collections;
using UnityEngine;

// 단서 첨부(ClueAttachmentKind.Audio) 재생 담당 — 도감 카드(CodexCardView)와 노트 그래프
// (NoteRouteGraphView)가 같이 쓴다. 붙은 GameObject에 AudioSource 하나를 만들어 재사용하므로
// 한 화면에서 소리가 여러 개 겹쳐 나오지 않는다.
//
// AudioManager를 쓰지 않는 이유: 그쪽은 AudioDataSO + 풀 기반이라 "clues.json이 가리키는 임의
// 경로의 AudioClip"을 재생하는 용도에 맞지 않는다. UI 효과음이라 2D(spatialBlend 0)로 고정한다.
//
// 재생 상태는 onPlayingChanged 콜백으로 알려준다 — 호출부가 버튼 라벨을 "▶ 재생"/"■ 정지"로
// 바꾸는 데 쓴다. 재생이 끝나거나(길이만큼 기다림) 패널이 닫혀 비활성화되면 false로 되돌아온다.
public class ClueAttachmentAudioPlayer : MonoBehaviour
{
    private AudioSource _source;
    private Action<bool> _onPlayingChanged;
    private Coroutine _watch;

    public static ClueAttachmentAudioPlayer AttachTo(GameObject host)
    {
        var existing = host.GetComponent<ClueAttachmentAudioPlayer>();
        return existing != null ? existing : host.AddComponent<ClueAttachmentAudioPlayer>();
    }

    // 같은 소리를 다시 누르면 정지, 다른 소리를 누르면 이전 것을 멈추고 새로 재생한다.
    public void Toggle(AudioClip clip, Action<bool> onPlayingChanged)
    {
        if (clip == null) return;

        EnsureSource();
        bool sameClipPlaying = _source.isPlaying && _source.clip == clip;
        Stop();
        if (sameClipPlaying) return;

        _source.clip = clip;
        _source.Play();
        _onPlayingChanged = onPlayingChanged;
        onPlayingChanged?.Invoke(true);
        _watch = StartCoroutine(WatchEnd(clip.length));
    }

    public void Stop()
    {
        if (_watch != null)
        {
            StopCoroutine(_watch);
            _watch = null;
        }
        if (_source != null && _source.isPlaying) _source.Stop();

        var callback = _onPlayingChanged;
        _onPlayingChanged = null;
        callback?.Invoke(false);
    }

    // 창이 닫히면(부모가 비활성화되면) 유니티가 AudioSource를 알아서 멈추지만, 그것만으로는
    // "■ 정지"로 바꿔둔 버튼 라벨이 그대로 굳는다 — 상태 콜백까지 확실히 되돌린다.
    private void OnDisable() => Stop();

    // WaitForSeconds가 아니라 Realtime인 이유: 메뉴를 여는 동안 timeScale이 0이 될 수 있는데,
    // 소리는 timeScale과 무관하게 실시간으로 흐른다.
    private IEnumerator WatchEnd(float seconds)
    {
        yield return new WaitForSecondsRealtime(seconds);
        _watch = null;
        Stop();
    }

    private void EnsureSource()
    {
        if (_source != null) return;
        _source = gameObject.GetComponent<AudioSource>();
        if (_source == null) _source = gameObject.AddComponent<AudioSource>();
        _source.playOnAwake = false;
        _source.loop = false;
        _source.spatialBlend = 0f;
    }
}
