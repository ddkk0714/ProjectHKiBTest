using System;
using System.Collections.Generic;
using UnityEngine;

public interface IBuffable : IInitializable
{
    public List<BuffInfo> CurrentBuffs { get; set; }

    public BuffInfo FindBuff(StatBuffSO buff);
    public BuffInfo Buff(StatBuffSO buff, int buffStack = 1, int timeStack = 1, float overrideTime = -1);
    public void UnBuff(StatBuffSO buff, int buffStack = 1, int reduceTime = 0, bool byTimer = false);
}

[Serializable]
public class BuffInfo
{
    [field: SerializeField] public StatBuffSO Buff { get; set; }
    [field: SerializeField] public int BuffStack { get; set; }
    public Timer Cooltime { get; set; }

    public BuffInfo(StatBuffSO buff)
    {
        Buff = buff;
        Cooltime = new();
    }

    public void AddBuff(InterfaceRegister interfaceReg, int multiplyer, bool stack)
    {
        if (stack) BuffStack += multiplyer;
        else BuffStack = multiplyer;

        Buff.AddBuff(interfaceReg, multiplyer, stack);
    }

    public void RemoveBuff(InterfaceRegister interfaceReg, int multiplyer, bool remove)
        => Buff.RemoveBuff(interfaceReg, multiplyer, remove);
}

/*
 * ── BuffableModule 사용 가이드 ──────────────────────────────────────────────
 *
 * [1] 초기화
 *   Initialize() 를 반드시 먼저 호출할 것.
 *   entityToBuff 에 버프를 받을 대상의 InterfaceRegister 를 할당해야 실제 스탯에 반영됨.
 *
 * [2] 버프 적용
 *   Buff(buffSO)                        // 1스택 적용
 *   Buff(buffSO, stack)                 // N스택 한 번에 적용
 *   Buff(buffSO, stack, 1, overrideTime)// 지속시간 덮어쓰기 (-1이면 SO의 BuffTime 사용)
 *
 * [3] 버프 제거
 *   UnBuff(buffSO)                      // BuffRemoveType 에 따라 제거
 *   UnBuff(buffSO, stack)               // Unstack 타입일 때 N스택 감소
 *   UnBuff(buffSO, 1, reduceTime)       // 남은 시간을 N초 단축
 *   UnBuff(buffSO, 1, 0, true)          // ignorePermanent=true: Permanent도 강제 제거
 *
 * [4] 조회
 *   FindBuff(buffSO)                    // BuffInfo 반환. 없으면 null
 *   CurrentBuffs                        // 현재 활성 버프 목록 (읽기용)
 *
 * [5] SO 설정 항목 요약
 *   BuffStackType  : Ignore(중첩 무시) / Stack(누적) / Overwrite(덮어씀) / Independant(항상 별도 생성)
 *   BuffRemoveType : Remove(즉시 전체 제거) / Unstack(스택 단위 감소) / Permanent(UnBuff 무시)
 *   TimeStackType  : Ignore(시간 무시) / Stack(남은 시간 + 새 시간) / Overwrite(시간 초기화)
 *   IsBuffTimeInfinite = true → 쿨타임 시작 안 함 (수동 제거 필요)
 *
 * [주의] 버프의 동일성은 StatBuffSO 하나로만 판단함(FindBuff 참고).
 *        예외는 BuffStackType.Independant — 이 타입은 매번 별도 BuffInfo 를 새로 만듦.
 *        예전에는 (buff, sourceGear) 조합으로 구분했으나 2026-07-28 버프 시스템 단순화 때
 *        sourceGear 개념이 이 모듈에서 제거됨.
 * ────────────────────────────────────────────────────────────────────────────
 */
public class BuffableModule : InterfaceModule, IBuffable
{
    public InterfaceRegister entityToBuff;
    [field: SerializeField] public List<BuffInfo> CurrentBuffs { get; set; } = new();

    public override void Register(IInterfaceRegistable interfaceRegistable)
    {
        interfaceRegistable.RegisterInterface<IBuffable>(this);
    }

    // 지금은 할 일이 없다. 예전엔 GetCurrentSourceGear()에 쓰던 gearManager를 여기서 찾아뒀는데,
    // 2026-07-28 버프 시스템 단순화로 sourceGear 개념이 사라지면서 그 탐색도 불필요해졌다.
    // 메서드 자체는 IBuffable → IInitializable 계약이라 남겨둔다.
    public void Initialize() { }

    // StatBuffSO -> 그 SO의 첫 BuffInfo. FindBuff를 O(1)로 만들기 위한 색인이다.
    //
    // 예전 구현은 CurrentBuffs.Find(b => b.Buff == buff) 한 줄이었는데 두 가지가 겹쳐 비쌌다.
    //   - 람다가 buff를 캡처하므로 호출할 때마다 클로저와 Predicate 델리게이트를 새로 할당한다.
    //   - b.Buff == buff는 UnityEngine.Object의 == 오버로드라 네이티브 생존 검사까지 탄다.
    // 둘 다 버프 개수에 비례해 늘어나는데, EmotionVectorModule이 이걸 매 프레임 부른다 —
    // Update의 색 폴링만 14회, 조합 판정(ApplyCombinations)까지 돌면 프레임당 100회를 넘는다.
    // 그래서 "버프를 많이 바르면 프레임이 떨어지는" 증상이 났다.
    private readonly Dictionary<StatBuffSO, BuffInfo> _buffIndex = new();

    // 색인을 갱신한 시점의 CurrentBuffs.Count. CurrentBuffs는 public set이라 외부에서 통째로
    // 갈아끼울 수 있어서, 개수가 어긋나면 색인을 다시 만든다(O(1) 검사로 desync를 잡는다).
    private int _indexedCount = -1;

    private void RebuildIndex()
    {
        _buffIndex.Clear();

        for (int i = 0; i < CurrentBuffs.Count; i++)
        {
            BuffInfo info = CurrentBuffs[i];
            if (info?.Buff == null) continue;

            // Independant는 같은 SO로 BuffInfo가 여러 개 생긴다. 예전 Find와 같이 "첫 번째"를 준다.
            if (!_buffIndex.ContainsKey(info.Buff)) _buffIndex[info.Buff] = info;
        }

        _indexedCount = CurrentBuffs.Count;
    }

    public BuffInfo FindBuff(StatBuffSO buff)
    {
        if (buff == null) return null;
        if (_indexedCount != CurrentBuffs.Count) RebuildIndex();

        return _buffIndex.TryGetValue(buff, out BuffInfo info) ? info : null;
    }

    public BuffInfo Buff(StatBuffSO buff, int buffStack = 1, int timeStack = 1, float overrideTime = -1)
    {
        float cooltime = overrideTime > 0 ? overrideTime : buff.BuffTime;
        BuffInfo buffInfo = FindBuff(buff);

        if (buffInfo == null || buff.BuffStackType == StatBuffSO.BuffStackTypeEnum.Independant)
        {
            buffInfo = new(buff);
            buffInfo.AddBuff(entityToBuff, buffStack, false);

            if (!buffInfo.Buff.IsBuffTimeInfinite)
                buffInfo.Cooltime.StartTimer(cooltime, () => UnBuff(buff));

            CurrentBuffs.Add(buffInfo);
            if (!_buffIndex.ContainsKey(buff)) _buffIndex[buff] = buffInfo;
            _indexedCount = CurrentBuffs.Count;
        }
        else
        {
            if (buff.BuffStackType == StatBuffSO.BuffStackTypeEnum.Stack)
                buffInfo.AddBuff(entityToBuff, buffStack, true);
            else if (buff.BuffStackType == StatBuffSO.BuffStackTypeEnum.Overwrite)
                buffInfo.AddBuff(entityToBuff, buffStack, false);

            if (!buffInfo.Buff.IsBuffTimeInfinite)
            {
                if (buff.TimeStackType == StatBuffSO.TimeStackTypeEnum.Stack)
                {
                    float remain = buffInfo.Cooltime.RemainTime;
                    buffInfo.Cooltime.CancelTimer();
                    buffInfo.Cooltime.StartTimer(cooltime + remain, () => UnBuff(buff));
                }

                if (buff.TimeStackType == StatBuffSO.TimeStackTypeEnum.Overwrite)
                {
                    buffInfo.Cooltime.CancelTimer();
                    buffInfo.Cooltime.StartTimer(cooltime, () => UnBuff(buff));
                }
            }
        }

        return buffInfo;
    }

    public void UnBuff(StatBuffSO buff, int buffStack = 1, int reduceTime = 0, bool ignorePermanent = false)
    {
        BuffInfo buffInfo = FindBuff(buff);
        if (buffInfo == null) return;

        if (!ignorePermanent && buff.BuffRemoveType == StatBuffSO.BuffRemoveTypeEnum.Permanent)
            return;

        if (reduceTime > 0 && !buffInfo.Cooltime.IsCooltimeEnded)
        {
            float remain = buffInfo.Cooltime.RemainTime - reduceTime;
            buffInfo.Cooltime.CancelTimer();
            if (remain > 0)
                buffInfo.Cooltime.StartTimer(remain, () => UnBuff(buff));
        }

        bool isForceRemove = ignorePermanent && buff.BuffRemoveType == StatBuffSO.BuffRemoveTypeEnum.Permanent;

        if (buff.BuffRemoveType == StatBuffSO.BuffRemoveTypeEnum.Remove
            || buff.BuffStackType == StatBuffSO.BuffStackTypeEnum.Independant
            || isForceRemove)
        {
            buffInfo.RemoveBuff(entityToBuff, 1, true);
            RemoveFromCurrentBuffs(buffInfo);
        }

        if (!isForceRemove && buff.BuffRemoveType == StatBuffSO.BuffRemoveTypeEnum.Unstack)
        {
            buffInfo.RemoveBuff(entityToBuff, buffStack, false);
            buffInfo.BuffStack -= buffStack;
            if (buffInfo.BuffStack <= 0)
                RemoveFromCurrentBuffs(buffInfo);
        }
    }

    // 목록에서 빼면서 그 버프의 타이머도 반드시 같이 끈다.
    // 안 끄면 이미 제거된 BuffInfo의 DOTween 시퀀스가 살아남아 나중에 UnBuff(buff)를 한 번 더
    // 쏘는데, 그 사이에 같은 버프가 새로 걸려 있으면(예: 세이브 로드로 복원된 버프) 엉뚱하게
    // 그쪽이 걷혀버린다.
    private void RemoveFromCurrentBuffs(BuffInfo buffInfo)
    {
        buffInfo.Cooltime?.CancelTimer();
        CurrentBuffs.Remove(buffInfo);

        // 색인이 가리키던 항목이 빠졌으면 같은 SO의 다른 BuffInfo로 넘긴다(Independant면 남아 있다).
        if (buffInfo.Buff != null && _buffIndex.TryGetValue(buffInfo.Buff, out BuffInfo indexed) && indexed == buffInfo)
        {
            _buffIndex.Remove(buffInfo.Buff);

            for (int i = 0; i < CurrentBuffs.Count; i++)
            {
                if (CurrentBuffs[i]?.Buff != buffInfo.Buff) continue;
                _buffIndex[buffInfo.Buff] = CurrentBuffs[i];
                break;
            }
        }

        _indexedCount = CurrentBuffs.Count;
    }
}