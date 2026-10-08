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
                "Observe ready local storage volumes. Table size fields are exact BYTES (TotalBytes, UsedBytes, FreeBytes, AvailableBytes); FreePercent is FreeBytes/TotalBytes*100. For user-facing sizes, label decimal GB (bytes/1,000,000,000) versus binary GiB (bytes/1,073,741,824) correctly. Read-only; no arguments.",

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

        // Preserve the original eight fields in order for existing consumers.
        // Additional precomputed displays stop the model from silently treating
        // bytes / 2^30 as decimal GB. All fields are read-only measurements.
        output.AppendLine(
            "Drive\tType\tFormat\tTotalBytes\tUsedBytes\tFreeBytes\tAvailableBytes\tFreePercent\tFreeGB\tFreeGiB");

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
                    .Append(
                        freePercent.ToString(
                            "F2",
                            CultureInfo.InvariantCulture))
                    .Append('\t')
                    .Append(
                        (free / 1_000_000_000.0).ToString(
                            "F2",
                            CultureInfo.InvariantCulture))
                    .Append('\t')
                    .AppendLine(
                        (free / 1_073_741_824.0).ToString(
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
                    $"Observed {observed} ready local storage volume(s). Skipped={skipped}. " +
                    "Raw capacity fields are exact bytes. FreeGB and FreeGiB are precomputed " +
                    "from the SAME FreeBytes observation using decimal and binary divisors. " +
                    "FreePercent=FreeBytes/TotalBytes*100. Use FreeGB with GB or FreeGiB " +
                    "with GiB; do not mix these figures or units.",

                Output =
                    output.ToString().TrimEnd(),

                ChangedSystemState =
                    false
            });
    }
}
