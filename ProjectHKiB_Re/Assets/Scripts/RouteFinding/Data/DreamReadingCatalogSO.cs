using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 해몽 레시피 전체 목록.
///
/// 레시피마다 에셋을 따로 두지 않고 한 에셋에 모은 이유는, 런타임에 "프로젝트에 있는 레시피 전부"를
/// 모으는 경로가 필요한데 그러려면 어차피 레지스트리 에셋이 하나 있어야 하기 때문이다
/// (MapDataRegistrySO가 MapDataSO에 대해 하는 일과 같은 문제). 레시피는 수가 적고 서로 참조할
/// 일도 없어서 목록 하나로 충분하다.
///
/// [배치] Resources 폴더에 <c>DreamReadings</c>라는 이름으로 두면 DreamReadingModule이 알아서
/// 찾는다. RouteFinding이 이미 clues.json/map_database.json을 Resources로 읽고 있어 같은 방식이다.
/// </summary>
[CreateAssetMenu(fileName = "DreamReadings", menuName = "Event/DreamReadingCatalog")]
public class DreamReadingCatalogSO : ScriptableObject
{
    [SerializeField] private List<DreamReading> readings = new();

    public IReadOnlyList<DreamReading> Readings => readings;
}
