/*
 * filename: SystemStorageCapabilityHandler.cs
 */

using System.Globalization;
using System.Text;

namespace NIRAAgent.Capabilities;

/// <summary>
/// Read-only observation of current local storage capacity.
///
/// This exists so routine machine-state questions do not need to go through
/// shell.execute. It uses System.IO.DriveInfo directly, changes no system state,
/// and requires no command execution or elevated authorization.
/// </summary>
public sealed class NIRASystemStorageListCapabilityHandler
    : INIRACapabilityHandler
{
    public NIRACapabilityDescriptor Descriptor
    {
        get;
    } =
        new NIRACapabilityDescriptor
        {
            Id =
                NIRACapabilityIds.SystemStorageList,

            Description =
                "Observe current ready local storage volumes and return total, used, free and available capacity plus free percentage. Read-only; no shell or PowerShell is used. No arguments.",

            DefaultRisk =
                NIRACapabilityRisk.Observe,

            Parameters =
                Array.Empty<NIRACapabilityParameterDescriptor>()
        };


    public NIRACapabilityRisk ResolveRisk(
        NIRACapabilityRequest request)
    {
        return NIRACapabilityRisk.Observe;
    }


    public Task<NIRACapabilityHandlerResult> ExecuteAsync(
        NIRACapabilityRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        DriveInfo[] drives =
            DriveInfo.GetDrives();

        StringBuilder output =
            new();

        output.AppendLine(
            "Drive\tType\tFormat\tTotalBytes\tUsedBytes\tFreeBytes\tAvailableBytes\tFreePercent");

        int observed =
            0;

        int skipped =
            0;

        foreach (
            DriveInfo drive
            in drives.OrderBy(
                value =>
                    value.Name,
                StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                // Keep this primitive about local storage. Network mappings and
                // optical media are different resources and can have very
                // different latency/availability semantics.
                if (drive.DriveType is
                    DriveType.Network or
                    DriveType.CDRom)
                {
                    skipped++;
                    continue;
                }

                if (!drive.IsReady)
                {
                    skipped++;
                    continue;
                }

                long total =
                    drive.TotalSize;

                long free =
                    drive.TotalFreeSpace;

                long available =
                    drive.AvailableFreeSpace;

                long used =
                    Math.Max(
                        0L,
                        total - free);

                double freePercent =
                    total > 0
                        ? (double)free / total * 100.0
                        : 0.0;

                string format;

                try
                {
                    format =
                        drive.DriveFormat;
                }
                catch
                {
                    format =
                        string.Empty;
                }

                output
                    .Append(drive.Name.TrimEnd('\\'))
                    .Append('\t')
                    .Append(drive.DriveType)
                    .Append('\t')
                    .Append(format)
                    .Append('\t')
                    .Append(total.ToString(CultureInfo.InvariantCulture))
                    .Append('\t')
                    .Append(used.ToString(CultureInfo.InvariantCulture))
                    .Append('\t')
                    .Append(free.ToString(CultureInfo.InvariantCulture))
                    .Append('\t')
                    .Append(available.ToString(CultureInfo.InvariantCulture))
                    .Append('\t')
                    .AppendLine(
                        freePercent.ToString(
                            "F2",
                            CultureInfo.InvariantCulture));

                observed++;
            }
            catch (
                IOException)
            {
                // A removable/local volume may disappear between enumeration
                // and observation. Skip it rather than turning the whole
                // machine observation into a failed capability.
                skipped++;
            }
            catch (
                UnauthorizedAccessException)
            {
                skipped++;
            }
        }

        if (observed == 0)
        {
            return Task.FromResult(
                new NIRACapabilityHandlerResult
                {
                    Succeeded =
                        false,

                    Summary =
                        "No ready local storage volumes could be observed.",

                    Output =
                        output.ToString().TrimEnd(),

                    ChangedSystemState =
                        false
                });
        }

        return Task.FromResult(
            new NIRACapabilityHandlerResult
            {
                Summary =
                    $"Observed {observed} ready local storage volume(s). Skipped={skipped}. Capacity values are current DriveInfo observations.",

                Output =
                    output.ToString().TrimEnd(),

                ChangedSystemState =
                    false
            });
    }
}
