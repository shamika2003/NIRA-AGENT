using System.Text.Json;

namespace NIRAAgent.Settings;

public enum NIRAVoiceEngineMode
{
    Auto,
    Groq,
    Piper
}

public sealed record NIRARuntimeSettings
{
    // Voice
    public bool VoiceEnabled { get; init; } = true;
    public bool SpeakBackgroundUpdates { get; init; } = true;
    public NIRAVoiceEngineMode VoiceEngine { get; init; } = NIRAVoiceEngineMode.Auto;
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

public sealed class NIRARuntimeSettingsService
{
    private readonly object _sync = new();
    private readonly string _filePath;
    private NIRARuntimeSettings _current;

    public NIRARuntimeSettingsService()
    {
        string root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NIRAAgent");

        Directory.CreateDirectory(root);
        _filePath = Path.Combine(root, "settings.json");
        _current = Normalize(LoadOrDefault());
    }

    public NIRARuntimeSettings Current
    {
        get
        {
            lock (_sync)
            {
                return _current;
            }
        }
    }

    public event Action<NIRARuntimeSettings, NIRARuntimeSettings>? Changed;

    public void SetVoiceEnabled(bool value) =>
        Update(current => current with { VoiceEnabled = value });

    public void SetSpeakBackgroundUpdates(bool value) =>
        Update(current => current with { SpeakBackgroundUpdates = value });

    public void SetVoiceEngine(NIRAVoiceEngineMode value) =>
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

    private void Update(Func<NIRARuntimeSettings, NIRARuntimeSettings> change)
    {
        ArgumentNullException.ThrowIfNull(change);

        NIRARuntimeSettings before;
        NIRARuntimeSettings after;

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

    private void Replace(NIRARuntimeSettings replacement)
    {
        NIRARuntimeSettings before;
        NIRARuntimeSettings normalized = Normalize(replacement);

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

    private NIRARuntimeSettings LoadOrDefault()
    {
        if (!File.Exists(_filePath))
        {
            return CreateDefaults();
        }

        try
        {
            string json = File.ReadAllText(_filePath);
            NIRARuntimeSettings? loaded = JsonSerializer.Deserialize<NIRARuntimeSettings>(json);
            return loaded ?? CreateDefaults();
        }
        catch
        {
            return CreateDefaults();
        }
    }

    private static NIRARuntimeSettings Normalize(NIRARuntimeSettings settings) =>
        settings with
        {
            VoiceVolume = Math.Clamp(settings.VoiceVolume, 0.0, 1.0),
            IdleFadeDelaySeconds = Math.Clamp(settings.IdleFadeDelaySeconds, 15, 1800)
        };

    private static NIRARuntimeSettings CreateDefaults()
    {
        NIRAVoiceEngineMode engine = NIRAVoiceEngineMode.Auto;
        string configured = Environment.GetEnvironmentVariable("NIRA_VOICE_ENGINE")?.Trim() ?? string.Empty;

        if (configured.Equals("groq", StringComparison.OrdinalIgnoreCase))
        {
            engine = NIRAVoiceEngineMode.Groq;
        }
        else if (configured.Equals("piper", StringComparison.OrdinalIgnoreCase))
        {
            engine = NIRAVoiceEngineMode.Piper;
        }

        return new NIRARuntimeSettings
        {
            VoiceEngine = engine
        };
    }

    private void SaveLocked(NIRARuntimeSettings settings)
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
        NIRARuntimeSettings before,
        NIRARuntimeSettings after)
    {
        Action<NIRARuntimeSettings, NIRARuntimeSettings>? changed = Changed;

        if (changed == null)
        {
            return;
        }

        foreach (Action<NIRARuntimeSettings, NIRARuntimeSettings> subscriber
                 in changed.GetInvocationList().Cast<Action<NIRARuntimeSettings, NIRARuntimeSettings>>())
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

