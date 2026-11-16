using SKECS.GA;

namespace SpaceBattle.Web.Ga;

public struct ParticleExpiryFrame
{
    public int Tick;
}

public struct ParticleExpiryRow
{
    public int ExpiresTick;
    public bool Expired;
}

[SecsSystem(SystemId = "particle-expiry")]
public partial struct ParticleExpiry
{
    [GaTickResource(typeof(ParticleExpiryFrame))]
    public void Execute(in SystemContext context, ref ParticleExpiryRow row)
    {
        row.Expired = row.ExpiresTick <= context.Resource<ParticleExpiryFrame>().Tick;
    }
}
