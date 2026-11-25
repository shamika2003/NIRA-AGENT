using System.Text;
using Nira.Core;
using Nira.Infrastructure;

NiraRuntime runtime;
try { runtime = NiraRuntime.CreateDefault(); }
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
{
    Console.Error.WriteLine("NIRA initialization failed: " + ex.Message);
    Environment.ExitCode = 2;
    return;
}
using (runtime)
{
    Guid conversation = Guid.NewGuid();
    Console.WriteLine("NIRA by ELVARA — diagnostic CLI");
    Console.WriteLine("Workspace: " + runtime.Workspace);
    Console.WriteLine("Commands: :new, :resume, :id, :status, :abandon (twice), :model-key, :model-key-clear, :image-key, :image-key-clear, :retry, :exit");
    var abandonGate = new TaskAbandonConfirmation();
    var broker = new CliApprovalBroker();
    var progress = new Progress<AgentProgress>(p =>
        Console.WriteLine($"  [{p.Kind}] {p.Summary} {(p.ToolId ?? "")}"));

    while (true)
    {
        Console.Write("You > ");
        string? line = Console.ReadLine();
        if (line is null || line.Trim().Equals(":exit", StringComparison.OrdinalIgnoreCase)) break;
        if (line.Trim().Equals(":abandon", StringComparison.OrdinalIgnoreCase))
        {
            if (!abandonGate.ConfirmOrArm(DateTimeOffset.UtcNow))
            {
                Console.WriteLine("Press :abandon a SECOND time within 15 seconds to abandon this task. No files are deleted.");
                continue;
            }
            var task = await runtime.Conversations.LoadAsync(conversation, default);
            if (task is not null)
            {
                TaskAbandonConfirmation.MarkAbandoned(task);
                await runtime.Conversations.SaveAsync(task, default);
            }
            conversation = Guid.NewGuid();
            Console.WriteLine("Task abandoned; history retained. New conversation: " + conversation);
            continue;
        }
        abandonGate.Reset();
        if (line.Trim().Equals(":new", StringComparison.OrdinalIgnoreCase))
        {
            conversation = Guid.NewGuid();
            Console.WriteLine("New conversation: " + conversation);
            continue;
        }
        if (line.Trim().Equals(":resume", StringComparison.OrdinalIgnoreCase))
        {
            Guid? previous = await runtime.Conversations.FindRecentUnfinishedAsync(default);
            if (previous is null) Console.WriteLine("No unfinished conversation found.");
            else { conversation = previous.Value; Console.WriteLine("Resuming: " + conversation); }
            continue;
        }
        if (line.Trim().Equals(":id", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine(conversation);
            continue;
        }
        if (line.Trim().Equals(":status", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                ConversationState? task = await runtime.Conversations.LoadAsync(conversation, default);
                if (task is null) Console.WriteLine("No task has been saved in this conversation.");
                else
                {
                    Console.WriteLine("Task: " + task.Objective);
                    Console.WriteLine("State: " + task.Status);
                    Console.WriteLine("Automatic planning: " + task.AutomaticPlanningOutcome);
                    Console.WriteLine($"Steps: {task.Plan.Count(step => step.Status == "completed")}/{task.Plan.Count} completed");
                    for (int i = 0; i < task.Plan.Count; i++)
                    {
                        PlanStep step = task.Plan[i];
                        Console.WriteLine($"  {i + 1}. [{step.Status}] [{step.Kind}] {step.Description}");
                        Console.WriteLine("     Success: " + step.SuccessCriterion);
                        if (!string.IsNullOrWhiteSpace(step.ToolName))
                            Console.WriteLine("     Expected tool: " + step.ToolName);
                        if (!string.IsNullOrWhiteSpace(step.EvidenceId))
                            Console.WriteLine("     Receipt: " + step.EvidenceId);
                    }
                }
            }
            catch (Exception error)
            { Console.WriteLine("[status] " + error.GetType().Name + ": " + error.Message); }
            continue;
        }
        if (line.Trim().Equals(":image-key", StringComparison.OrdinalIgnoreCase) ||
            line.Trim().Equals(":image-key-clear", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                string home = Path.GetDirectoryName(runtime.DatabasePath)!;
                if (line.Trim().Equals(":image-key-clear", StringComparison.OrdinalIgnoreCase))
                    ImageGenerationProvider.Clear(home);
                else
                {
                    Console.Write("Cloudflare Account ID: ");
                    string account = Console.ReadLine()?.Trim() ?? "";
                    Console.WriteLine("Enter the Workers AI API token at the hidden prompt.");
                    string? token = ReadHiddenKey();
                    if (string.IsNullOrWhiteSpace(token)) { Console.WriteLine("Cancelled."); continue; }
                    ImageGenerationProvider.Save(home, account, token);
                }
                Console.WriteLine("Image credential updated. Available immediately. No request sent.");
            }
            catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
            { Console.WriteLine("Could not update image credentials. Check the account ID, token and local permissions."); }
            continue;
        }
        if (line.Trim().Equals(":model-key", StringComparison.OrdinalIgnoreCase))
        {
            try { StoreNewKey(runtime); }
            catch (Exception error) { Console.WriteLine("[auth] Unable to store credential: " + error.Message); }
            continue;
        }
        if (line.Trim().Equals(":model-key-clear", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                runtime.ModelCredentials.ClearStored();
                Console.WriteLine("[auth] Saved model credential cleared. An environment key may still be present.");
            }
            catch (Exception error) { Console.WriteLine("[auth] Unable to clear credential: " + error.Message); }
            continue;
        }
        if (line.Trim().Equals(":retry", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                AgentRunResult resumed = await runtime.Engine.RunAsync(conversation, null, broker, progress);
                Console.WriteLine("NIRA > " + resumed.Message);
                Console.WriteLine("[" + resumed.Status + "]");
                if (resumed.Status == ConversationStatus.Completed) conversation = Guid.NewGuid();
            }
            catch (Exception error)
            { Console.WriteLine("[error] " + error.GetType().Name + ": " + error.Message); }
            continue;
        }
        if (string.IsNullOrWhiteSpace(line)) continue;

        try
        {
            AgentRunResult answer;
            try { answer = await runtime.Engine.RunAsync(conversation, line, broker, progress); }
            catch (ModelAuthenticationException auth)
            {
                Console.WriteLine("[auth] " + auth.Message);
                Console.WriteLine("Enter an API key to retry this same task, or press Enter to cancel.");
                if (!StoreNewKey(runtime)) continue;
                // The original user message was persisted before the HTTP 401.
                // Resume with null input, not the original text, to avoid duplicating it.
                answer = await runtime.Engine.RunAsync(conversation, null, broker, progress);
            }
            Console.WriteLine("NIRA > " + answer.Message);
            Console.WriteLine("[" + answer.Status + "]");
            if (answer.Status == ConversationStatus.Completed)
                conversation = Guid.NewGuid();
        }
        catch (ModelAuthenticationException auth)
        {
            Console.WriteLine("[auth] " + auth.Message);
            Console.WriteLine("Use :model-key to replace the credential, then :retry to resume the same task.");
        }
        catch (Exception error)
        {
            Console.WriteLine("[error] " + error.GetType().Name + ": " + error.Message);
        }
    }
}

static bool StoreNewKey(NiraRuntime runtime)
{
    string? key = ReadHiddenKey();
    if (string.IsNullOrWhiteSpace(key))
    {
        Console.WriteLine("[auth] Credential entry cancelled. No existing key changed.");
        return false;
    }
    runtime.ModelCredentials.Save(key);
    Console.WriteLine("[auth] API key saved for this Windows user and model endpoint (DPAPI encrypted).");
    return true;
}

static string? ReadHiddenKey()
{
    if (Console.IsInputRedirected)
    {
        Console.WriteLine("[auth] Cannot safely prompt for a credential with redirected input.");
        return null;
    }
    Console.Write("Model API key (hidden, Esc to cancel): ");
    var entered = new StringBuilder();
    while (true)
    {
        ConsoleKeyInfo character = Console.ReadKey(intercept: true);
        if (character.Key == ConsoleKey.Escape) { Console.WriteLine(); return null; }
        if (character.Key == ConsoleKey.Enter) { Console.WriteLine(); return entered.ToString(); }
        if (character.Key == ConsoleKey.Backspace)
        {
            if (entered.Length > 0) { entered.Length--; Console.Write("\b \b"); }
            continue;
        }
        if (!char.IsControl(character.KeyChar) && entered.Length < 8192)
        {
            entered.Append(character.KeyChar);
            Console.Write('*');
        }
    }
}

internal sealed class CliApprovalBroker : IApprovalBroker
{
    public Task<bool> RequestApprovalAsync(ApprovalRequest request, CancellationToken ct)
    {
        Console.WriteLine("ACTION APPROVAL REQUIRED: " + request.Description);
        if (request.ToolName.StartsWith("memory.", StringComparison.Ordinal) || request.ToolName is "desktop.window_activate" or "desktop.control_invoke" or "desktop.control_sequence" or
            "desktop.control_set_text" or "desktop.control_type_text")
            Console.WriteLine("Exact approved action arguments: " + request.ArgumentsJson);
        if (request.ToolName == "memory.search_history")
            Console.WriteLine("Matching private excerpts will be sent to the configured reasoning model.");
        if (request.ToolName == "desktop.control_type_text")
            Console.WriteLine("NIRA will activate the approved observed window itself after YES. " +
                "Windows still verifies the exact window and focused editor before typing. " +
                "No manual Alt+Tab is required.");
        Console.Write("Approve this operation? Type YES (case-insensitive; anything else denies): ");
        return Task.FromResult(ApprovalInput.IsExplicitYes(Console.ReadLine()));
    }
}
