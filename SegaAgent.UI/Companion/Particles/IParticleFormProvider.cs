/*
 * filename: IParticleFormProvider.cs
 */

using SegaAgent.Embodiment;

namespace SegaAgent.UI.Companion.Particles;

public interface IParticleFormProvider
{
    string FormId
    {
        get;
    }


    void BuildTargets(
        ParticleTarget[] targets,
        SegaVisualIntent intent,
        double time);
}