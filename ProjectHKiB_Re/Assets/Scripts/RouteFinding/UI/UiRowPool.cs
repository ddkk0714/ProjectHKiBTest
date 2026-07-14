using System;
using System.Collections.Generic;
using UnityEngine;

// 반복되는 UI 행/카드(도감 트리 행, 노트 카드, 코멘트 등)를 매 리프레시마다 Destroy+Instantiate하지
// 않고 재사용하는 범용 풀. [SerializeField]로 지정한 "행 템플릿" 프리팹이 있으면 그걸 복제하고,
// 없으면 fallback 팩토리로 런타임에 기본 템플릿을 한 번만 만들어 그걸 계속 복제한다.
//
// 템플릿 프리팹은 Prefab Mode에서 자유롭게 커스터마이징(이미지 추가, 레이아웃 조정 등)할 수 있다 —
// 색상/크기 [SerializeField] 값만 노출하던 기존 방식보다 자유도가 높다. 다만 템플릿 구조 안의
// 핵심 자식(예: "Text", "BtnDelete")은 이름이 유지되어야 Populate 코드가 찾아서 채울 수 있다
// (다른 Bind() 계열 코드와 동일한 이름 기반 규칙).
//
// 사용법: 매 리프레시마다 필요한 순서대로 Get(parent)를 호출해 행을 하나씩 받고(풀에 남은 게 있으면
// 재사용해 활성화, 없으면 새로 Instantiate), 다 쓴 뒤 EndPass()를 호출해 이번에 못 받은 나머지를
// 비활성화한다(파괴하지 않고 풀에 남겨 다음 리프레시에 재사용).
public class UiRowPool
{
    private readonly GameObject _template; // 프리팹(할당됨) 또는 런타임 기본 템플릿(비활성 상태로 보관)
    private readonly List<GameObject> _pool = new();
    private int _usedThisPass;

    public UiRowPool(GameObject templatePrefab, Func<GameObject> fallbackFactory)
    {
        _template = templatePrefab != null ? templatePrefab : fallbackFactory();
    }

    // 이번 리프레시에서 다음으로 필요한 행을 parent 밑에서 반환 — 풀에 남은 비활성 항목이 있으면
    // 재사용(활성화 + 재부모 + 맨 뒤로 정렬)하고, 없으면 템플릿을 복제해 새로 만든다.
    public GameObject Get(Transform parent)
    {
        GameObject go = _usedThisPass < _pool.Count ? _pool[_usedThisPass] : null;
        if (go == null)
        {
            go = UnityEngine.Object.Instantiate(_template);
            _pool.Add(go);
        }
        go.transform.SetParent(parent, false);
        go.transform.SetAsLastSibling();
        go.SetActive(true);
        _usedThisPass++;
        return go;
    }

    // 이번 리프레시가 끝났음을 알린다 — 못 쓴 나머지 풀 항목은 비활성화(파괴하지 않음)하고,
    // 다음 리프레시를 위해 사용 카운터를 0으로 되돌린다.
    public void EndPass()
    {
        for (int i = _usedThisPass; i < _pool.Count; i++)
            _pool[i].SetActive(false);
        _usedThisPass = 0;
    }
}
