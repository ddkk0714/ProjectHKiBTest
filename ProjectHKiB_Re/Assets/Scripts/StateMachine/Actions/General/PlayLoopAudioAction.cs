using UnityEngine;
namespace StateMachine
{
    // 이벤트 중 BGM·환경음을 바꾸거나 끈다. 다음 맵 로드 때는 그 맵의 bgmID·ambienceID로 다시 덮인다.
    [System.Serializable]
    public class PlayLoopAudioAction : StateAction
    {
        [SerializeField] private AudioLoopChannel channel;
        [Tooltip("AudioManager.loopDatas에 등록한 AudioDataSO의 에셋 이름. 비우면 그 채널을 끈다.")]
        [SerializeField] private string audioID;
        [Tooltip("단계 순서. 채우면 Audio ID가 이 목록에서 지금 곡보다 뒤일 때만 바꾼다(앞 단계로 되돌아가지 않음).")]
        [SerializeField] private string[] sequence = System.Array.Empty<string>();
        [Range(0, 1)][SerializeField] private float volume = 1f;
        [Tooltip("지금 곡에서 넘어가는 시간(초). 0이면 바로 바뀐다.")]
        [Min(0f)][SerializeField] private float fadeSec = 1f;

        public override void Act(StateController stateController)
        {
            AudioManager audioManager = GameManager.instance.audioManager;
            if (sequence.Length > 0) audioManager.PlayLoopForward(channel, sequence, audioID, volume, fadeSec);
            else audioManager.PlayLoop(channel, audioID, volume, fadeSec);
        }
    }
}
