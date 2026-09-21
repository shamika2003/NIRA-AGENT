/*
 * filename: ParticleFormRegistry.cs
 */

using NIRAAgent.Embodiment;

namespace NIRAAgent.UI.Companion.Particles;

public sealed class ParticleFormRegistry
{
    // =========================================================
    // PROVIDERS
    // =========================================================

    private readonly Dictionary<
        string,
        IParticleFormProvider>
        _providers =
            new(
                StringComparer.OrdinalIgnoreCase);


    // =========================================================
    // FALLBACK
    // =========================================================

    private readonly IParticleFormProvider
        _fallback;


    // =========================================================
    // CONSTRUCTOR
    // =========================================================

    public ParticleFormRegistry()
    {
        _fallback =
            new OrbParticleFormProvider();


        Register(
            _fallback);
    }


    // =========================================================
    // REGISTER
    // =========================================================

    public void Register(
        IParticleFormProvider provider)
    {
        ArgumentNullException.ThrowIfNull(
            provider);


        if (string.IsNullOrWhiteSpace(
                provider.FormId))
        {
            throw new ArgumentException(
                "Particle form provider requires a form ID.",
                nameof(provider));
        }


        _providers[
            provider.FormId.Trim()] =
                provider;
    }


    // =========================================================
    // RESOLVE
    // =========================================================

    public IParticleFormProvider Resolve(
        NIRAVisualIntent intent)
    {
        ArgumentNullException.ThrowIfNull(
            intent);


        string formId =
            intent
                .Normalize()
                .FormId;


        if (_providers.TryGetValue(
                formId,
                out IParticleFormProvider?
                    provider))
        {
            return provider;
        }


        return _fallback;
    }
}
