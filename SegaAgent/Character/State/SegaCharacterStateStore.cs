/*
 * filename: SegaCharacterStateStore.cs
 */

using System.Diagnostics;
using System.Text.Json;

namespace SegaAgent.Character.State;

public sealed class SegaCharacterStateStore
{
    private const int SchemaVersion =
        1;


    private readonly object _sync =
        new();


    private readonly string _filePath;


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


    public SegaCharacterStateStore()
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
                "character-state.json");
    }


    public SegaCharacterSnapshot Load()
    {
        lock (_sync)
        {
            if (!File.Exists(
                    _filePath))
            {
                return SegaCharacterSnapshot.Initial;
            }


            try
            {
                string json =
                    File.ReadAllText(
                        _filePath);


                CharacterStateDocument?
                    document =
                        JsonSerializer
                            .Deserialize<
                                CharacterStateDocument>(
                                    json,
                                    _jsonOptions);


                if (
                    document ==
                    null
                    ||
                    document.SchemaVersion !=
                    SchemaVersion)
                {
                    return SegaCharacterSnapshot.Initial;
                }


                return document
                    .Character
                    .Normalize();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"[CharacterStore] LOAD ERROR: {ex}");


                TryPreserveCorruptFile();


                return SegaCharacterSnapshot.Initial;
            }
        }
    }


    public void Save(
        SegaCharacterSnapshot snapshot)
    {
        lock (_sync)
        {
            string temporaryPath =
                _filePath +
                ".tmp";


            try
            {
                CharacterStateDocument
                    document =
                        new()
                        {
                            SchemaVersion =
                                SchemaVersion,

                            Character =
                                snapshot.Normalize()
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


    private void TryPreserveCorruptFile()
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


    private sealed class
        CharacterStateDocument
    {
        public int SchemaVersion
        {
            get;
            init;
        }


        public SegaCharacterSnapshot Character
        {
            get;
            init;
        }
    }
}