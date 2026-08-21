using System.Collections.Generic;
using AYellowpaper.SerializedCollections;
using UnityEngine;
using UnityEngine.U2D.Animation;
[CreateAssetMenu(fileName = "Gear Data", menuName = "Scriptable Objects/Data/Gear Data", order = 2)]
public class GearDataSO : ItemDataSO
{
    public enum GearType { Damage, Transform, Util }
    [Header("Base Data")]
    //public GearTypeSO gearType;
    [Tooltip("Where this gear's AttackDatas come from. Leave empty for gears that cannot attack.")]
    public PlayerBaseDataSO playerBaseData;
    //public GameObject tutorialPrefab;
    public GearType gearType;
    public StateMachineSO stateMachine;
    public string startStateName;
    public float transformTime;

    [Header("Base Stat Buff")]
    public StatBuffSO baseBuff;
    public StatBuffSO stackableBuff;

    [Header("Base Animation")]
    public SimpleAnimationDataSO mainAnimationData;
    public SpriteLibraryAsset mainSpriteLibrary;
    public SimpleAnimationDataSO effectAnimationData;
    public SpriteLibraryAsset effectSpriteLibrary;

    [Header("Skin Data")]
    public SkinDataSO skinData;
    public SpriteLibraryAsset standingCGData;

    [Header("Merge Setting")]
    public GearDataSO[] mergeSet;
    [Tooltip("smaller the value is, priority is higher")]
    public int mergePriority;
    public SerializedDictionary<GearDataSO, SkinDataSO> SkinMixList;

    [Header("Gear Effect")]
    public string mainGearEffectDiscription;
    public GearEffectSO[] mainGearEffects;

    [Header("Graffiti")]
    public List<GraffitiCode> graffitiCodes;
    public List<GraffitiCode> graffitiAllCases;

    public void CalculateAllCases()
    {
        graffitiAllCases.Clear();
        foreach (GraffitiCode graffitiCode in graffitiCodes)
        {
            foreach (Vector2Int center in graffitiCode.code)
            {
                GraffitiCode skillCase = new() { code = new(graffitiCode.code.Count) };
                foreach (Vector2Int point in graffitiCode.code)
                {
                    skillCase.code.Add(point - center);
                }
                graffitiAllCases.Add(skillCase);
            }
        }
    }

    public void ApplyMainGearEffect(MergedPlayerBaseData realGear)
    {
        for (int i = 0; i < mainGearEffects.Length; i++)
            mainGearEffects[i].ApplyEffect(realGear);
    }

    public void Activate(StateController player, GearDataSO recentGear)
    {
        if (gearType != GearType.Damage)
        {
            if (stateMachine) player.Initialize(stateMachine);
            // 플레이어는 IDirAnimatable만 등록한다 — DirAnimatableModule.Register가 부모 것에
            // 더하는 게 아니라 덮어써 버리기 때문이다. InterfaceRegister는 정확한 타입으로
            // 찾으므로 IAnimatable로만 조회하면 플레이어에서는 절대 안 잡힌다.
            // PlayAnimationAction이 쓰는 것과 같은 폴백이다.
            if (!player.TryGetInterface(out IAnimatable animatable)
                && player.TryGetInterface(out IDirAnimatable dirAnimatable))
                animatable = dirAnimatable;
            if (animatable != null)
            {
                animatable.MainAnimationData = mainAnimationData;
                animatable.MainSpriteLibrary = mainSpriteLibrary;
                // 값만 넣는 건 아무 효과가 없다 — 둘 다 그냥 자동 프로퍼티고, 이걸 실제
                // SimpleAnimationPlayer로 밀어 넣는 건 AnimatableModule.Initialize뿐이다.
                // Player.Initialize와 같은 순서다: 데이터를 넣고, 그 다음 모듈을 초기화한다.
                animatable.Initialize();
            }
            if (player.TryGetInterface(out IAttackable attackable))
            {
                attackable.EffectAnimationData = effectAnimationData;
                attackable.EffectSpriteLibrary = effectSpriteLibrary;
                // 위 MainAnimationData와 같은 사정이다 — damager의 이펙트 플레이어로 밀어 넣기
                // 전까지 프로퍼티는 아무 효과가 없고, 안 밀면 이전 기어의 이펙트가 그대로 나온다.
                attackable.ApplyEffectAnimationData();

                // AttackDatas는 캐릭터의 PlayerBaseDataSO에 있는데, 기어를 바꿀 때 이걸 옮겨 주는
                // 곳이 아무 데도 없다 — 예전엔 GearMergeManagerSO가 했지만 GearManager가 더 이상
                // 그걸 부르지 않는다. 이 줄이 없으면 플레이어가 기본형 캐릭터의 (비어 있는) 배열을
                // 그대로 들고 있어서 모든 공격이 "AttackData[n] is missing"으로 실패한다.
                // 별칭이 아니라 복사로 넘긴다(DatabaseManagerSO.SetIAttackable과 같은 방식) —
                // 그래야 플레이 중에 에셋의 배열이 바뀌지 않는다.
                AttackDataSO[] gearAttackDatas = playerBaseData != null ? playerBaseData.AttackDatas : null;
                if (gearAttackDatas != null && gearAttackDatas.Length > 0)
                    attackable.AttackDatas = (AttackDataSO[])gearAttackDatas.Clone();
            }

            // 회피 데이터도 같은 구멍이었다. Delta/Hadaka/Default의 PlayerData는 KeepDodgeParticle이
            // 비어 있어서, 기어만 Lily로 바꾸고 회피하면 DodgeableModule.StartKeepDodge가
            // NullReferenceException을 낸다. 회피 속도·거리·무적시간도 기본형 값 그대로 남는다.
            if (playerBaseData != null && player.TryGetInterface(out IDodgeable dodgeable))
                DatabaseManagerSO.CopyDodgeableData(dodgeable, playerBaseData);

            SkinDataSO skin = SkinMixList.ContainsKey(recentGear) ? SkinMixList[recentGear] : skinData;
            if (player.TryGetInterface(out ISkinable skinable)) skinable.SetSkinData(skin);
        }

        player.ChangeState(startStateName);

        if (player.TryGetInterface(out IBuffable buffable))
        {
            if (baseBuff) buffable.Buff(baseBuff);
            if (stackableBuff) buffable.Buff(stackableBuff);
        }
    }

    public void Deactivate(StateController player)
    {
        if (player.TryGetInterface(out IBuffable buffable))
        {
            if (baseBuff) buffable.UnBuff(baseBuff);
        }
    }
}
