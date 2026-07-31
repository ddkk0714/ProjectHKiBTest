using UnityEngine;

public interface ITargetableBase
{
    public LayerMask[] TargetLayers { get; set; }
}

public interface ITargetable : ITargetableBase, IInitializable
{
    public Transform CurrentTarget { get; set; }
}

public class TargetableModule : InterfaceModule, ITargetable
{
    [field: SerializeField][field: NaughtyAttributes.ReadOnly] public Transform CurrentTarget { get; set; }
    [field: SerializeField] public LayerMask[] TargetLayers { get; set; }

    public override void Register(IInterfaceRegistable interfaceRegistable)
    {
        interfaceRegistable.RegisterInterface<ITargetable>(this);
    }

    public void Initialize()
    {
        CurrentTarget = null;
    }
}
