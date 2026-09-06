using System.Diagnostics;
using System.Windows;
using System.Windows.Input;

using SegaAgent.AI.Ollama;

namespace SegaAgent.UI;

public partial class OllamaApiKeyWindow : Window
{
    private readonly SegaOllamaCredentialRequest _request;

    public SegaOllamaCredentialResolution? Resolution { get; private set; }

    public OllamaApiKeyWindow(
        SegaOllamaCredentialRequest request)
    {
        InitializeComponent();

        _request = request ?? throw new ArgumentNullException(nameof(request));

        ApplyProblemText();
        Loaded += (_, _) => ApiKeyBox.Focus();
    }

    private void ApplyProblemText()
    {
        switch (_request.Problem)
        {
            case SegaOllamaCredentialProblem.Missing:
                HeadingText.Text = "Connect Sega to Ollama";
                DescriptionText.Text =
                    "Sega needs an Ollama cloud API key before her reasoning service can start. " +
                    "Paste a key from your Ollama account.";
                WarningText.Visibility = Visibility.Collapsed;
                RetryCurrentButton.Visibility = Visibility.Collapsed;
                break;

            case SegaOllamaCredentialProblem.InvalidOrRevoked:
                HeadingText.Text = "Ollama API key needs attention";
                DescriptionText.Text =
                    "Ollama rejected the current credential. Paste a valid replacement key and Sega will retry the failed reasoning request once.";
                WarningText.Text =
                    "The old key is not deleted from Ollama itself; Sega only replaces the local environment value.";
                WarningText.Visibility = Visibility.Visible;
                RetryCurrentButton.Visibility = Visibility.Collapsed;
                break;

            case SegaOllamaCredentialProblem.UsageUnavailable:
                HeadingText.Text = "Ollama cloud usage is unavailable";
                DescriptionText.Text =
                    "Ollama reported that the current account/key cannot continue cloud inference right now. " +
                    "You can add usage, retry the same key, or paste another valid key you are authorized to use.";
                WarningText.Text =
                    "Creating another key on the same exhausted Ollama account does not create new credits. " +
                    "Use Retry current after adding usage, or replace the key only when you genuinely have another valid credential.";
                WarningText.Visibility = Visibility.Visible;
                RetryCurrentButton.Visibility = _request.HasCurrentKey
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                break;
        }
    }

    private void ApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        ValidationText.Visibility = Visibility.Collapsed;
        SaveButton.IsEnabled = !string.IsNullOrWhiteSpace(ApiKeyBox.Password);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        string key = ApiKeyBox.Password.Trim();

        if (key.Length < 16 || key.Any(char.IsWhiteSpace))
        {
            ValidationText.Text = "That value does not look like a valid Ollama API key.";
            ValidationText.Visibility = Visibility.Visible;
            return;
        }

        Resolution = new SegaOllamaCredentialResolution
        {
            Kind = SegaOllamaCredentialResolutionKind.ReplaceKey,
            ApiKey = key
        };

        DialogResult = true;
        Close();
    }

    private void RetryCurrent_Click(object sender, RoutedEventArgs e)
    {
        Resolution = new SegaOllamaCredentialResolution
        {
            Kind = SegaOllamaCredentialResolutionKind.UseCurrent
        };

        DialogResult = true;
        Close();
    }

    private void OpenOllama_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(
                new ProcessStartInfo("https://ollama.com/")
                {
                    UseShellExecute = true
                });
        }
        catch (Exception ex)
        {
            ValidationText.Text = $"Could not open the browser: {ex.Message}";
            ValidationText.Visibility = Visibility.Visible;
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
}
