using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DG.Tweening;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Audio;

public enum AudioLoopChannel { BGM, Ambience }

public class AudioPlayData
{
    public AudioDataSO audioData;
    public float volume;
    public Vector3 position;

    public AudioPlayData(AudioDataSO _audioData, float _volume, Vector3 _position)
    {
        audioData = _audioData;
        volume = _volume;
        position = _position;
    }
}


public class AudioManager : PoolManager<AudioPlayer>
{
    public GameObject prefab;
    [SerializeField] private AudioDataSO[] allDatas;
    [SerializeField] private AudioTypeSO[] allTypes;
    public AudioMixer audioMixer;

    private Queue<AudioPlayData> _oneShotPlayQueue;
    private const int ONESHOTPLAYQUEUEMAX = 10;
    private AudioPlayer _oneShotPlayer;
    // 원샷 플레이어 하나를 여러 그룹이 돌려 쓰면 울리던 소리가 다음 소리의 그룹으로 옮겨 간다.
    private readonly Dictionary<AudioMixerGroup, AudioPlayer> _groupOneShotPlayers = new();

    private bool _oneShotPlayerDequeueInProgress;

    [Header("BGM / 환경음")]
    [Tooltip("맵의 bgmID·ambienceID와 PlayLoopAudioAction이 에셋 이름으로 찾는 루프 음원.")]
    [SerializeField] private AudioDataSO[] loopDatas;
    [SerializeField] private AudioMixerGroup bgmGroup;
    [SerializeField] private AudioMixerGroup ambienceGroup;
    [Tooltip("맵이 바뀔 때 BGM·환경음을 넘기는 시간(초).")]
    [SerializeField, Min(0f)] private float mapLoopFadeSec = 1f;
    private LoopChannel[] _loopChannels;

    public void Start()
    {
        Initialize();
        _loopChannels = new[] { new LoopChannel(gameObject, bgmGroup), new LoopChannel(gameObject, ambienceGroup) };
        MapManager mapManager = GameManager.instance != null ? GameManager.instance.mapManager : null;
        if (mapManager == null) return;
        mapManager.OnMapLoaded += HandleMapLoaded;
        // 에디터에 열어 둔 맵은 MapManager.Start에서 바로 로드 완료되므로 위 구독보다 먼저 끝났을 수 있다.
        if (mapManager.CurrentMapData != null) HandleMapLoaded(mapManager.CurrentMapData);
    }

    private void OnDestroy()
    {
        MapManager mapManager = GameManager.instance != null ? GameManager.instance.mapManager : null;
        if (mapManager != null) mapManager.OnMapLoaded -= HandleMapLoaded;
    }

    // 맵에 적힌 값이 지금 것과 같으면 끊지 않고 이어서 튼다. 비어 있으면 꺼진다.
    private void HandleMapLoaded(MapDataSO mapData)
    {
        PlayLoop(AudioLoopChannel.BGM, mapData.bgmID, 1f, mapLoopFadeSec);
        PlayLoop(AudioLoopChannel.Ambience, mapData.ambienceID, 1f, mapLoopFadeSec);
    }

    /// <summary>
    /// 채널의 루프 음원을 audioID(loopDatas의 에셋 이름)로 크로스페이드해 바꾼다. 빈 값이면 페이드아웃한다.
    /// </summary>
    public void PlayLoop(AudioLoopChannel channel, string audioID, float volume, float fadeSec)
    {
        AudioDataSO data = null;
        if (!string.IsNullOrWhiteSpace(audioID))
        {
            data = loopDatas.FirstOrDefault(d => d && d.name == audioID);
            if (!data)
            {
                Debug.LogWarning($"[AudioManager] loopDatas에 '{audioID}'가 없어 {channel}을 바꾸지 않습니다.");
                return;
            }
        }
        _loopChannels[(int)channel].Play(data, volume, fadeSec);
    }

    /// <summary>
    /// audioID가 sequence에서 지금 곡보다 뒤일 때만 바꾼다. 퍼즐 스위치를 순서와 다르게 다시 밟아도
    /// 앞 단계 곡으로 되돌아가지 않게 하려는 것. 지금 곡이 목록에 없으면 첫 단계 전으로 본다.
    /// 진행 상태를 따로 두지 않으므로 맵을 다시 불러 원래 곡이 나오면 처음부터 다시 진행된다.
    /// </summary>
    public void PlayLoopForward(AudioLoopChannel channel, string[] sequence, string audioID, float volume, float fadeSec)
    {
        AudioDataSO current = _loopChannels[(int)channel].Data;
        int now = current ? Array.IndexOf(sequence, current.name) : -1;
        if (Array.IndexOf(sequence, audioID) <= now) return;
        PlayLoop(channel, audioID, volume, fadeSec);
    }

    // AudioSource 두 개를 번갈아 써서 이전 곡이 빠지는 동안 새 곡이 들어온다.
    private class LoopChannel
    {
        private readonly AudioSource[] _sources = new AudioSource[2];
        private int _current;
        private AudioDataSO _data;
        public AudioDataSO Data => _data;

        public LoopChannel(GameObject host, AudioMixerGroup group)
        {
            for (int i = 0; i < _sources.Length; i++)
            {
                _sources[i] = host.AddComponent<AudioSource>();
                _sources[i].playOnAwake = false;
                _sources[i].loop = true;
                _sources[i].outputAudioMixerGroup = group;
            }
        }

        public void Play(AudioDataSO data, float volume, float fadeSec)
        {
            if (data == _data)
            {
                if (data) Fade(_sources[_current], volume, fadeSec);
                return;
            }
            _data = data;
            Fade(_sources[_current], 0f, fadeSec);
            if (!data || data.audioClips.Length == 0) return;

            _current = 1 - _current;
            AudioSource source = _sources[_current];
            source.DOKill();
            source.clip = data.audioClips[UnityEngine.Random.Range(0, data.audioClips.Length)];
            source.volume = 0f;
            source.Play();
            Fade(source, volume, fadeSec);
        }

        // SetUpdate(true): AudioPlayer.FadeCoroutine과 같은 이유로 timeScale과 무관하게 페이드한다.
        private static void Fade(AudioSource source, float volume, float fadeSec)
        {
            source.DOKill();
            if (fadeSec <= 0f)
            {
                source.volume = volume;
                if (volume <= 0f) source.Stop();
                return;
            }
            source.DOFade(volume, fadeSec).SetUpdate(true).OnComplete(() =>
            {
                if (volume <= 0f) source.Stop();
            });
        }
    }

    public override void Initialize()
    {
        _oneShotPlayQueue = new(ONESHOTPLAYQUEUEMAX);
        base.Initialize();
    }

    public override void InitializePool()
    {
        objects = new();
        inactiveObjectSet = new(allDatas.Length);
        activeObjectSet = new(allDatas.Length);

        var clone = Instantiate(prefab, this.transform);
        if (clone.TryGetComponent(out AudioPlayer oneShotAudioPlayer))
            _oneShotPlayer = oneShotAudioPlayer;
        else
            Debug.LogError("ERROR: Failed to create oneShotPlayer(audioPlayer prefab is invalid)!!!");

        // allTypes에 등록된 타입의 그룹마다 원샷 플레이어를 따로 둔다. 등록 안 된 그룹은 기본 플레이어를 쓴다.
        _groupOneShotPlayers.Clear();
        foreach (AudioTypeSO type in allTypes)
        {
            if (!type || !type.mixerGroup || _groupOneShotPlayers.ContainsKey(type.mixerGroup)) continue;
            if (Instantiate(prefab, this.transform).TryGetComponent(out AudioPlayer groupPlayer))
                _groupOneShotPlayers.Add(type.mixerGroup, groupPlayer);
        }

        for (int i = 0; i < allDatas.Length; i++)
        {
            if (allDatas[i].type.playOneShot) continue;
            CreatePool(allDatas[i].GetInstanceID(), allDatas[i].type.PoolSize);
            for (int j = 0; j < allDatas[i].type.PoolSize; j++)
            {
                clone = Instantiate(prefab, this.transform);
                if (clone.TryGetComponent(out AudioPlayer audioPlayer))
                {
                    AddObjectToPool(allDatas[i].GetInstanceID(), audioPlayer);
                    audioPlayer.InitializeWhenReused(allDatas[i]);
                }
                else
                {
                    Debug.LogError("ERROR: Failed to create pool(audioPlayer prefab is invalid)!!!");
                }
            }
        }
    }

    public override void InitObjectOnReuse(AudioPlayer audioPlayer, Transform transform, Quaternion rotation, bool attatchToTransform)
    {
        if (!audioPlayer.gameObject.activeSelf)
            audioPlayer.gameObject.SetActive(true);
        audioPlayer.transform.SetPositionAndRotation(transform.position, rotation);
        if (attatchToTransform) audioPlayer.transform.parent = transform;
    }

    public AudioPlayer PlayAudio(int ID, float volume, float fadeTimeSec, Transform transform, bool attatchToTransform)
    {
        AudioPlayer clone = ReuseObject(ID, transform, quaternion.identity, attatchToTransform, false);
        if (!clone)
        {
            Debug.LogError("ERROR: Failed to play audio(audioPlayer prefab is invalid)!!! ID: " + ID);
            return null;
        }
        clone.Play(volume, fadeTimeSec);
        return clone;
    }

    public void PlayAudioOneShot(AudioDataSO audioData, float volume, Vector3 pos)
    {
        if (!audioData) return;
        if (_oneShotPlayQueue.Count >= ONESHOTPLAYQUEUEMAX) return;
        _oneShotPlayQueue.Enqueue(new AudioPlayData(audioData, volume, pos));
        if (!_oneShotPlayerDequeueInProgress)
        {
            _oneShotPlayerDequeueInProgress = true;
            StartCoroutine(OneShotPlayDequeueCoroutine());
        }
    }

    private void PlayAudioOneShotDequeue(AudioDataSO audioData, float volume, Vector3 pos)
    {
        AudioMixerGroup group = audioData.type ? audioData.type.mixerGroup : null;
        AudioPlayer player = group && _groupOneShotPlayers.TryGetValue(group, out AudioPlayer groupPlayer) ? groupPlayer : _oneShotPlayer;
        player.InitializeWhenReused(audioData);
        player.transform.position = pos;
        player.Play(volume, 0);
    }

    private IEnumerator OneShotPlayDequeueCoroutine()
    {
        while (_oneShotPlayQueue.Count > 0)
        {
            AudioPlayData playData = _oneShotPlayQueue.Dequeue();
            PlayAudioOneShotDequeue(playData.audioData, playData.volume, playData.position);
            yield return null;
        }
        _oneShotPlayerDequeueInProgress = false;
    }

    public void ChangeVolume(AudioPlayer audioPlayer, float volume, float fadeTimeSec)
    => audioPlayer.Fade(volume, fadeTimeSec);

    public void StopPlaying(AudioPlayer audioPlayer, float fadeTimeSec)
    => ChangeVolume(audioPlayer, 0, fadeTimeSec);

    public override void ResetPool()
    {
        if (objects != null && objects.Count > 0)
        {
            int[] keys = objects.Keys.ToArray();
            for (int i = 0; i < keys.Length; i++)
                Destroy(objects[keys[i]].gameObject);
        }
        base.ResetPool();
        if (_oneShotPlayer != null)
        {
            Destroy(_oneShotPlayer.gameObject);
            _oneShotPlayer = null;
        }
        foreach (AudioPlayer groupPlayer in _groupOneShotPlayers.Values)
            if (groupPlayer != null) Destroy(groupPlayer.gameObject);
        _groupOneShotPlayers.Clear();
    }
}