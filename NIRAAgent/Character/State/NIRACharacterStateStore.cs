/*
 * filename: NIRACharacterStateStore.cs
 */

using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace NIRAAgent.Character.State;

public sealed class NIRACharacterStateStore
{
    private const int SchemaVersion =
        1;


    // Separate from JSON shape/version. This tracks the semantics of the
    // character-dynamics algorithm so a known-bad historical state can be
    // migrated exactly once without deleting future valid continuity.
    private const int CharacterDynamicsVersion =
        2;


    private readonly object _sync =
        new();


    private readonly string _filePath;

    private readonly string _backupFilePath;


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


    public NIRACharacterStateStore()
    {
        string directory =
            Path.Combine(
                Environment.GetFolderPath(
                    Environment
                        .SpecialFolder
                        .LocalApplicationData),
                "NIRAAgent");


        Directory.CreateDirectory(
            directory);


        _filePath =
            Path.Combine(
                directory,
                "character-state.json");


        _backupFilePath =
            _filePath +
            ".bak";
    }


    public NIRACharacterSnapshot Load()
    {
        lock (_sync)
        {
            if (TryLoadSnapshot(
                    _filePath,
                    out NIRACharacterSnapshot primary,
                    out bool primaryCorrupt))
            {
                return primary;
            }


            if (primaryCorrupt)
            {
                TryPreserveCorruptFile(
                    _filePath);
            }


            if (TryLoadSnapshot(
                    _backupFilePath,
                    out NIRACharacterSnapshot backup,
                    out bool backupCorrupt))
            {
                Debug.WriteLine(
                    "[CharacterStore] PRIMARY UNAVAILABLE | " +
                    "Recovered character continuity from backup.");

                return backup;
            }


            if (backupCorrupt)
            {
                TryPreserveCorruptFile(
                    _backupFilePath);
            }


            return NIRACharacterSnapshot.Initial;
        }
    }


    public void Save(
        NIRACharacterSnapshot snapshot)
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

                            DynamicsVersion =
                                CharacterDynamicsVersion,

                            Character =
                                snapshot.Normalize()
                        };


                string json =
                    JsonSerializer.Serialize(
                        document,
                        _jsonOptions);


                WriteDurableText(
                    temporaryPath,
                    json);


                // Commit the new primary first. The previous backup remains intact
                // until this succeeds, so an interrupted primary replacement still
                // leaves a known-good recovery snapshot.
                File.Move(
                    temporaryPath,
                    _filePath,
                    true);


                // Refresh the recovery copy only after the primary is valid.
                // If this copy fails, the primary is still authoritative and the
                // previous backup remains available.
                try
                {
                    File.Copy(
                        _filePath,
                        _backupFilePath,
                        true);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(
                        $"[CharacterStore] BACKUP WARNING: {ex.Message}");
                }
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


    private bool TryLoadSnapshot(
        string path,
        out NIRACharacterSnapshot snapshot,
        out bool corrupt)
    {
        snapshot =
            NIRACharacterSnapshot.Initial;

        corrupt =
            false;


        if (!File.Exists(
                path))
        {
            return false;
        }


        try
        {
            string json =
                File.ReadAllText(
                    path);


            CharacterStateDocument?
                document =
                    JsonSerializer
                        .Deserialize<
                            CharacterStateDocument>(
                                json,
                                _jsonOptions);


            if (document ==
                null)
            {
                corrupt =
                    true;

                return false;
            }


            if (document.SchemaVersion !=
                SchemaVersion)
            {
                Debug.WriteLine(
                    $"[CharacterStore] SCHEMA MISMATCH | " +
                    $"Path='{path}' | Stored={document.SchemaVersion} | " +
                    $"Expected={SchemaVersion}");

                return false;
            }


            if (document.DynamicsVersion >
                CharacterDynamicsVersion)
            {
                Debug.WriteLine(
                    $"[CharacterStore] DYNAMICS VERSION TOO NEW | " +
                    $"Path='{path}' | Stored={document.DynamicsVersion} | " +
                    $"Supported={CharacterDynamicsVersion}");

                return false;
            }


            NIRACharacterSnapshot loaded =
                document
                    .Character
                    .Normalize();


            snapshot =
                MigrateCharacterDynamics(
                    loaded,
                    document.DynamicsVersion);


            return true;
        }
        catch (Exception ex)
        {
            corrupt =
                true;


            Debug.WriteLine(
                $"[CharacterStore] LOAD ERROR | Path='{path}': {ex}");


            return false;
        }
    }


    private static NIRACharacterSnapshot MigrateCharacterDynamics(
        NIRACharacterSnapshot source,
        int storedDynamicsVersion)
    {
        // Existing files created before DynamicsVersion existed deserialize as 0.
        // Versions 0/1 used the old positive-social accumulation behavior that could
        // saturate warmth/trust/affection from routine friendliness. Preserve negative
        // history, concern, irritation, friction and situation exactly; only compress
        // the dimensions known to have been inflated by that algorithm.
        if (storedDynamicsVersion >=
            CharacterDynamicsVersion)
        {
            return source.Normalize();
        }


        NIRARelationshipState relationship =
            source.Relationship.Normalize();


        NIRAMoodState mood =
            source.Mood.Normalize();


        NIRARelationshipState relationshipDefault =
            NIRARelationshipState.Default;


        NIRAMoodState moodDefault =
            NIRAMoodState.Default;


        NIRARelationshipState migratedRelationship =
            relationship with
            {
                Familiarity =
                    CompressLegacyPositive(
                        relationship.Familiarity,
                        relationshipDefault.Familiarity,
                        0.45),

                Trust =
                    CompressLegacyPositive(
                        relationship.Trust,
                        relationshipDefault.Trust,
                        0.35),

                Warmth =
                    CompressLegacyPositive(
                        relationship.Warmth,
                        relationshipDefault.Warmth,
                        0.35),

                Respect =
                    CompressLegacyPositive(
                        relationship.Respect,
                        relationshipDefault.Respect,
                        0.50),

                Attachment =
                    CompressLegacyPositive(
                        relationship.Attachment,
                        relationshipDefault.Attachment,
                        0.30),

                Openness =
                    CompressLegacyPositive(
                        relationship.Openness,
                        relationshipDefault.Openness,
                        0.40),

                Playfulness =
                    CompressLegacyPositive(
                        relationship.Playfulness,
                        relationshipDefault.Playfulness,
                        0.50)

                // Friction is intentionally preserved exactly.
            };


        NIRAMoodState migratedMood =
            mood with
            {
                Valence =
                    CompressLegacyPositive(
                        mood.Valence,
                        moodDefault.Valence,
                        0.35),

                Amusement =
                    CompressLegacyPositive(
                        mood.Amusement,
                        moodDefault.Amusement,
                        0.55),

                Affection =
                    CompressLegacyPositive(
                        mood.Affection,
                        moodDefault.Affection,
                        0.30)

                // Energy, curiosity, irritation and concern are preserved exactly.
            };


        NIRACharacterSnapshot migrated =
            source with
            {
                Relationship =
                    migratedRelationship.Normalize(),

                Mood =
                    migratedMood.Normalize()
            };


        Debug.WriteLine(
            $"[CharacterStore] DYNAMICS MIGRATION | " +
            $"Stored={storedDynamicsVersion} -> {CharacterDynamicsVersion} | " +
            $"Warmth {relationship.Warmth:F3}->{migrated.Relationship.Warmth:F3} | " +
            $"Trust {relationship.Trust:F3}->{migrated.Relationship.Trust:F3} | " +
            $"Affection {mood.Affection:F3}->{migrated.Mood.Affection:F3} | " +
            $"Friction={migrated.Relationship.Friction:F3} | " +
            $"Irritation={migrated.Mood.Irritation:F3}");


        return migrated.Normalize();
    }


    private static double CompressLegacyPositive(
        double value,
        double baseline,
        double retention)
    {
        if (value <=
            baseline)
        {
            return value;
        }


        return baseline +
            (
                value -
                baseline
            )
            *
            Math.Clamp(
                retention,
                0.0,
                1.0);
    }


    private static void WriteDurableText(
        string path,
        string content)
    {
        using FileStream stream =
            new(
                path,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize:
                    4096,
                options:
                    FileOptions.WriteThrough);


        using StreamWriter writer =
            new(
                stream,
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier:
                        false));


        writer.Write(
            content);


        writer.Flush();


        stream.Flush(
            flushToDisk:
                true);
    }


    private static void TryPreserveCorruptFile(
        string path)
    {
        try
        {
            if (!File.Exists(
                    path))
            {
                return;
            }


            string destination =
                path +
                ".corrupt-" +
                DateTimeOffset.UtcNow
                    .ToString(
                        "yyyyMMdd-HHmmss");


            File.Move(
                path,
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


        public int DynamicsVersion
        {
            get;
            init;
        }


        public NIRACharacterSnapshot Character
        {
            get;
            init;
        }
    }
}
