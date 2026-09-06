/*
 * filename: SegaCapabilityRegistry.cs
 */

namespace SegaAgent.Capabilities;

public sealed class SegaCapabilityRegistry
{
    private readonly IReadOnlyDictionary<
        string,
        ISegaCapabilityHandler>
        _handlers;


    public SegaCapabilityRegistry(
        IEnumerable<ISegaCapabilityHandler> handlers)
    {
        ArgumentNullException.ThrowIfNull(
            handlers);


        Dictionary<string, ISegaCapabilityHandler> map =
            new(
                StringComparer.OrdinalIgnoreCase);


        foreach (
            ISegaCapabilityHandler handler
            in handlers)
        {
            ArgumentNullException.ThrowIfNull(
                handler);


            SegaCapabilityDescriptor descriptor =
                handler.Descriptor.Normalize();


            if (!map.TryAdd(
                    descriptor.Id,
                    handler))
            {
                throw new InvalidOperationException(
                    $"Duplicate Sega capability ID '{descriptor.Id}'.");
            }
        }


        _handlers =
            map;
    }


    public IReadOnlyList<SegaCapabilityDescriptor> Descriptors =>
        _handlers.Values
            .Select(
                handler =>
                    handler.Descriptor.Normalize())
            .OrderBy(
                descriptor =>
                    descriptor.Id,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();


    public bool TryResolve(
        string capabilityId,
        out ISegaCapabilityHandler? handler)
    {
        handler =
            null;


        if (string.IsNullOrWhiteSpace(
                capabilityId))
        {
            return false;
        }


        return _handlers.TryGetValue(
            capabilityId.Trim(),
            out handler);
    }
}
