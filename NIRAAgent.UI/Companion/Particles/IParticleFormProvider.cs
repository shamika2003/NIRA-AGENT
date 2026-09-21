/*
 * filename: IParticleFormProvider.cs
 */

using NIRAAgent.Embodiment;

namespace NIRAAgent.UI.Companion.Particles;

public interface IParticleFormProvider
{
    string FormId
    {
        get;
    }


    void BuildTargets(
        ParticleTarget[] targets,
        NIRAVisualIntent intent,
        double time);
}
