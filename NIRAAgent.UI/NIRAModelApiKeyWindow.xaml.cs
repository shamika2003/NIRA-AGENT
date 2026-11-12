using System.Diagnostics;
using System.Windows;
using System.Windows.Input;

using NIRAAgent.AI.Models;
using NIRAAgent.UI.Theming;

namespace NIRAAgent.UI;

public partial class NIRAModelApiKeyWindow : Window
{
    private readonly NIRAModelCredentialRequest _request;

    public NIRAModelCredentialResolution? Resolution { get; private set; }

    public NIRAModelApiKeyWindow(
        NIRAModelCredentialRequest request)
    {
        InitializeComponent();

        _request = request ?? throw new ArgumentNullException(nameof(request));

        ApplyProblemText();
        Loaded += NIRAModelApiKeyWindow_Loaded;
    }


    private void NIRAModelApiKeyWindow_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        NIRAMotion.AnimateEntrance(
            RootShell,
            distance: 12.0,
            durationMs: 280);

        ApiKeyBox.Focus();
    }

    private void ApplyProblemText()
    {
        switch (_request.Problem)
        {
            case NIRAModelCredentialProblem.Missing:
                HeadingText.Text = "Connect NIRA to NVIDIA";
                DescriptionText.Text =
                    "NIRA needs an NVIDIA API key before her reasoning service can start. " +
                    "Paste a key from your NVIDIA account.";
                WarningText.Visibility = Visibility.Collapsed;
                RetryCurrentButton.Visibility = Visibility.Collapsed;
                break;

            case NIRAModelCredentialProblem.InvalidOrRevoked:
                HeadingText.Text = "NVIDIA API key needs attention";
                DescriptionText.Text =
                    "NVIDIA rejected the current credential. Paste a valid replacement key and NIRA will retry the failed reasoning request once.";
                WarningText.Text =
                    "The previous API key is not revoked at NVIDIA. This replacement is for the current NIRA process only.";
                WarningText.Visibility = Visibility.Visible;
                RetryCurrentButton.Visibility = Visibility.Collapsed;
                break;

            case NIRAModelCredentialProblem.UsageUnavailable:
                HeadingText.Text = "NVIDIA API access is unavailable";
                DescriptionText.Text =
                    "NVIDIA reported that the current account/key cannot continue cloud inference right now. " +
                    "You can add usage, retry the same key, or paste another valid key you are authorized to use.";
                WarningText.Text =
                    "Creating another key on the same exhausted NVIDIA account does not create new credits. " +
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
            ValidationText.Text = "That value does not look like a valid NVIDIA API key.";
            ValidationText.Visibility = Visibility.Visible;
            return;
        }

        Resolution = new NIRAModelCredentialResolution
        {
            Kind = NIRAModelCredentialResolutionKind.ReplaceKey,
            ApiKey = key
        };

        DialogResult = true;
        Close();
    }

    private void RetryCurrent_Click(object sender, RoutedEventArgs e)
    {
        Resolution = new NIRAModelCredentialResolution
        {
            Kind = NIRAModelCredentialResolutionKind.UseCurrent
        };

        DialogResult = true;
        Close();
    }

    private void OpenProvider_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(
                new ProcessStartInfo("https://build.nvidia.com/")
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
