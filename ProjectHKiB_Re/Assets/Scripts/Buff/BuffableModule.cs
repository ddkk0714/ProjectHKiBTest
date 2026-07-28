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
 *   Buff(buffSO)                        // 1스택, 현재 장비 소스로 적용
 *   Buff(buffSO, stack)                 // N스택 한 번에 적용
 *   Buff(buffSO, gear, stack)           // 소스 Gear를 직접 지정
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
 * [주의] Buff() 와 UnBuff() 는 sourceGear 가 일치해야 같은 버프로 인식함.
 *        Gear 인자 없이 호출하면 내부에서 GetCurrentSourceGear() 를 자동 사용함.
 * ────────────────────────────────────────────────────────────────────────────
 */
public class BuffableModule : InterfaceModule, IBuffable
{
    public InterfaceRegister entityToBuff;
    [field: SerializeField] public List<BuffInfo> CurrentBuffs { get; set; } = new();

    [Header("Gear Source")]
    [SerializeField] private GearManager gearManager;
    [SerializeField] private int weaponSlotIndex = 0;

    public override void Register(IInterfaceRegistable interfaceRegistable)
    {
        interfaceRegistable.RegisterInterface<IBuffable>(this);
    }

    public void Initialize()
    {
        if (gearManager == null)
            gearManager = FindObjectOfType<GearManager>(true);
    }

    public BuffInfo FindBuff(StatBuffSO buff)
        => CurrentBuffs.Find(b => b.Buff == buff);

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
            CurrentBuffs.Remove(buffInfo);
        }

        if (!isForceRemove && buff.BuffRemoveType == StatBuffSO.BuffRemoveTypeEnum.Unstack)
        {
            buffInfo.RemoveBuff(entityToBuff, buffStack, false);
            buffInfo.BuffStack -= buffStack;
            if (buffInfo.BuffStack <= 0)
                CurrentBuffs.Remove(buffInfo);
        }
    }
}