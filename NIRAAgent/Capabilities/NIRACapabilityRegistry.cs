/*
 * filename: NIRACapabilityRegistry.cs
 */

namespace NIRAAgent.Capabilities;

public sealed class NIRACapabilityRegistry
{
    private readonly IReadOnlyDictionary<
        string,
        INIRACapabilityHandler>
        _handlers;


    public NIRACapabilityRegistry(
        IEnumerable<INIRACapabilityHandler> handlers)
    {
        ArgumentNullException.ThrowIfNull(
            handlers);


        Dictionary<string, INIRACapabilityHandler> map =
            new(
                StringComparer.OrdinalIgnoreCase);


        foreach (
            INIRACapabilityHandler handler
            in handlers)
        {
            ArgumentNullException.ThrowIfNull(
                handler);


            NIRACapabilityDescriptor descriptor =
                handler.Descriptor.Normalize();


            if (!map.TryAdd(
                    descriptor.Id,
                    handler))
            {
                throw new InvalidOperationException(
                    $"Duplicate NIRA capability ID '{descriptor.Id}'.");
            }
        }


        _handlers =
            map;
    }


    public IReadOnlyList<NIRACapabilityDescriptor> Descriptors =>
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
        out INIRACapabilityHandler? handler)
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

