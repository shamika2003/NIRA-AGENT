using System.Text.Json;

namespace SegaAgent.Settings;

public enum SegaVoiceEngineMode
{
    Auto,
    Groq,
    Piper
}

public sealed record SegaRuntimeSettings
{
    // Voice
    public bool VoiceEnabled { get; init; } = true;
    public bool SpeakBackgroundUpdates { get; init; } = true;
    public SegaVoiceEngineMode VoiceEngine { get; init; } = SegaVoiceEngineMode.Auto;
    public double VoiceVolume { get; init; } = 1.0;

    // Presence / idle
    public bool DesktopPresenceEnabled { get; init; } = true;
    public bool IdleBehaviorEnabled { get; init; } = true;
    public bool UserIdleAwarenessEnabled { get; init; } = true;
    public bool FadeDesktopPresenceWhenIdle { get; init; } = true;
    public int IdleFadeDelaySeconds { get; init; } = 60;

    // Attention / autonomy
    public bool ProactiveCompanionEnabled { get; init; } = true;
    public bool PcContextReactionsEnabled { get; init; } = true;

    // Notifications
    public bool PermissionNotificationsEnabled { get; init; } = true;

    // Interface
    public bool WorkCardParticleTransitionsEnabled { get; init; } = true;
    public bool ShowBranchActivityInSidebar { get; init; } = true;
    public bool ShowCommitmentActivityInSidebar { get; init; } = true;
}

public sealed class SegaRuntimeSettingsService
{
    private readonly object _sync = new();
    private readonly string _filePath;
    private SegaRuntimeSettings _current;

    public SegaRuntimeSettingsService()
    {
        string root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SegaAgent");

        Directory.CreateDirectory(root);
        _filePath = Path.Combine(root, "settings.json");
        _current = Normalize(LoadOrDefault());
    }

    public SegaRuntimeSettings Current
    {
        get
        {
            lock (_sync)
            {
                return _current;
            }
        }
    }

    public event Action<SegaRuntimeSettings, SegaRuntimeSettings>? Changed;

    public void SetVoiceEnabled(bool value) =>
        Update(current => current with { VoiceEnabled = value });

    public void SetSpeakBackgroundUpdates(bool value) =>
        Update(current => current with { SpeakBackgroundUpdates = value });

    public void SetVoiceEngine(SegaVoiceEngineMode value) =>
        Update(current => current with { VoiceEngine = value });

    public void SetVoiceVolume(double value) =>
        Update(current => current with { VoiceVolume = Math.Clamp(value, 0.0, 1.0) });

    public void SetDesktopPresenceEnabled(bool value) =>
        Update(current => current with { DesktopPresenceEnabled = value });

    public void SetIdleBehaviorEnabled(bool value) =>
        Update(current => current with { IdleBehaviorEnabled = value });

    public void SetUserIdleAwarenessEnabled(bool value) =>
        Update(current => current with { UserIdleAwarenessEnabled = value });

    public void SetFadeDesktopPresenceWhenIdle(bool value) =>
        Update(current => current with { FadeDesktopPresenceWhenIdle = value });

    public void SetIdleFadeDelaySeconds(int value) =>
        Update(current => current with
        {
            IdleFadeDelaySeconds = Math.Clamp(value, 15, 1800)
        });

    public void SetProactiveCompanionEnabled(bool value) =>
        Update(current => current with { ProactiveCompanionEnabled = value });

    public void SetPcContextReactionsEnabled(bool value) =>
        Update(current => current with { PcContextReactionsEnabled = value });

    public void SetPermissionNotificationsEnabled(bool value) =>
        Update(current => current with { PermissionNotificationsEnabled = value });

    public void SetWorkCardParticleTransitionsEnabled(bool value) =>
        Update(current => current with { WorkCardParticleTransitionsEnabled = value });

    public void SetShowBranchActivityInSidebar(bool value) =>
        Update(current => current with { ShowBranchActivityInSidebar = value });

    public void SetShowCommitmentActivityInSidebar(bool value) =>
        Update(current => current with { ShowCommitmentActivityInSidebar = value });

    public void ResetToDefaults() =>
        Replace(CreateDefaults());

    private void Update(Func<SegaRuntimeSettings, SegaRuntimeSettings> change)
    {
        ArgumentNullException.ThrowIfNull(change);

        SegaRuntimeSettings before;
        SegaRuntimeSettings after;

        lock (_sync)
        {
            before = _current;
            after = Normalize(change(before));

            if (after == before)
            {
                return;
            }

            SaveLocked(after);
            _current = after;
        }

        RaiseChanged(before, after);
    }

    private void Replace(SegaRuntimeSettings replacement)
    {
        SegaRuntimeSettings before;
        SegaRuntimeSettings normalized = Normalize(replacement);

        lock (_sync)
        {
            before = _current;

            if (normalized == before)
            {
                return;
            }

            SaveLocked(normalized);
            _current = normalized;
        }

        RaiseChanged(before, normalized);
    }

    private SegaRuntimeSettings LoadOrDefault()
    {
        if (!File.Exists(_filePath))
        {
            return CreateDefaults();
        }

        try
        {
            string json = File.ReadAllText(_filePath);
            SegaRuntimeSettings? loaded = JsonSerializer.Deserialize<SegaRuntimeSettings>(json);
            return loaded ?? CreateDefaults();
        }
        catch
        {
            return CreateDefaults();
        }
    }

    private static SegaRuntimeSettings Normalize(SegaRuntimeSettings settings) =>
        settings with
        {
            VoiceVolume = Math.Clamp(settings.VoiceVolume, 0.0, 1.0),
            IdleFadeDelaySeconds = Math.Clamp(settings.IdleFadeDelaySeconds, 15, 1800)
        };

    private static SegaRuntimeSettings CreateDefaults()
    {
        SegaVoiceEngineMode engine = SegaVoiceEngineMode.Auto;
        string configured = Environment.GetEnvironmentVariable("SEGA_VOICE_ENGINE")?.Trim() ?? string.Empty;

        if (configured.Equals("groq", StringComparison.OrdinalIgnoreCase))
        {
            engine = SegaVoiceEngineMode.Groq;
        }
        else if (configured.Equals("piper", StringComparison.OrdinalIgnoreCase))
        {
            engine = SegaVoiceEngineMode.Piper;
        }

        return new SegaRuntimeSettings
        {
            VoiceEngine = engine
        };
    }

    private void SaveLocked(SegaRuntimeSettings settings)
    {
        string json = JsonSerializer.Serialize(
            settings,
            new JsonSerializerOptions
            {
                WriteIndented = true
            });

        string temporary = _filePath + ".tmp";
        File.WriteAllText(temporary, json);
        File.Move(temporary, _filePath, overwrite: true);
    }

    private void RaiseChanged(
        SegaRuntimeSettings before,
        SegaRuntimeSettings after)
    {
        Action<SegaRuntimeSettings, SegaRuntimeSettings>? changed = Changed;

        if (changed == null)
        {
            return;
        }

        foreach (Action<SegaRuntimeSettings, SegaRuntimeSettings> subscriber
                 in changed.GetInvocationList().Cast<Action<SegaRuntimeSettings, SegaRuntimeSettings>>())
        {
            try
            {
                subscriber(before, after);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[Settings] Subscriber failed: {ex.GetType().Name}");
            }
        }
    }
}
