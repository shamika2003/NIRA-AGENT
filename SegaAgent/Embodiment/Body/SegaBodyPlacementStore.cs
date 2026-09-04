/*
 * filename: SegaBodyPlacementStore.cs
 */

using System.Diagnostics;
using System.Text.Json;

using SegaAgent.PC.Awareness;

namespace SegaAgent.Embodiment.Body;

public sealed class SegaBodyPlacementStore
{
    private const int SchemaVersion =
        1;


    private readonly object
        _sync =
            new();


    private readonly string
        _filePath;


    private readonly JsonSerializerOptions
        _jsonOptions =
            new()
            {
                WriteIndented =
                    true,

                PropertyNameCaseInsensitive =
                    true,

                PropertyNamingPolicy =
                    JsonNamingPolicy.CamelCase
            };


    public SegaBodyPlacementStore()
    {
        string directory =
            Path.Combine(
                Environment.GetFolderPath(
                    Environment
                        .SpecialFolder
                        .LocalApplicationData),
                "SegaAgent");


        Directory.CreateDirectory(
            directory);


        _filePath =
            Path.Combine(
                directory,
                "body-placement.json");
    }


    // =========================================================
    // LOAD
    // =========================================================

    public PcRectangle? LoadPreferred()
    {
        lock (_sync)
        {
            if (!File.Exists(
                    _filePath))
            {
                return null;
            }


            try
            {
                string json =
                    File.ReadAllText(
                        _filePath);


                BodyPlacementDocument?
                    document =
                        JsonSerializer.Deserialize<
                            BodyPlacementDocument>(
                                json,
                                _jsonOptions);


                if (
                    document ==
                        null
                    ||
                    document.SchemaVersion !=
                        SchemaVersion
                    ||
                    document.PreferredWindowBounds
                        .IsEmpty)
                {
                    return null;
                }


                return document
                    .PreferredWindowBounds;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"[BodyPlacementStore] " +
                    $"LOAD ERROR: {ex}");


                PreserveCorruptFile();


                return null;
            }
        }
    }


    // =========================================================
    // SAVE
    // =========================================================

    public void SavePreferred(
        PcRectangle bounds)
    {
        if (bounds.IsEmpty)
        {
            return;
        }


        lock (_sync)
        {
            string temporaryPath =
                _filePath +
                ".tmp";


            try
            {
                BodyPlacementDocument document =
                    new()
                    {
                        SchemaVersion =
                            SchemaVersion,

                        PreferredWindowBounds =
                            bounds,

                        UpdatedAt =
                            DateTimeOffset.UtcNow
                    };


                string json =
                    JsonSerializer.Serialize(
                        document,
                        _jsonOptions);


                File.WriteAllText(
                    temporaryPath,
                    json);


                File.Move(
                    temporaryPath,
                    _filePath,
                    true);
            }
            finally
            {
                if (File.Exists(
                        temporaryPath))
                {
                    try
                    {
                        File.Delete(
                            temporaryPath);
                    }
                    catch
                    {
                    }
                }
            }
        }
    }


    // =========================================================
    // CORRUPT FILE
    // =========================================================

    private void PreserveCorruptFile()
    {
        try
        {
            if (!File.Exists(
                    _filePath))
            {
                return;
            }


            string destination =
                _filePath +
                ".corrupt-" +
                DateTimeOffset.UtcNow
                    .ToString(
                        "yyyyMMdd-HHmmss");


            File.Move(
                _filePath,
                destination,
                true);
        }
        catch
        {
        }
    }


    // =========================================================
    // DOCUMENT
    // =========================================================

    private sealed record BodyPlacementDocument
    {
        public int SchemaVersion
        {
            get;
            init;
        }


        public PcRectangle PreferredWindowBounds
        {
            get;
            init;
        }


        public DateTimeOffset UpdatedAt
        {
            get;
            init;
        }
    }
}
