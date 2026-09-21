using System.Windows;

using NIRAAgent.Authorization;

namespace NIRAAgent.UI;

public partial class CredentialPromptWindow : Window
{
    private readonly NIRACredentialPromptRequest _request;

    public NIRACredentialPromptResponse? Resolution { get; private set; }

    public CredentialPromptWindow(NIRACredentialPromptRequest request)
    {
        InitializeComponent();

        _request = request ?? throw new ArgumentNullException(nameof(request));

        OriginText.Text = $"Website: {_request.Origin}";
        ReasonText.Text = string.IsNullOrWhiteSpace(_request.Reason)
            ? "NIRA reached an authenticated step that requires an account credential."
            : _request.Reason;

        ExistingCredentialCombo.ItemsSource = _request.ExistingCredentials;
        ExistingCredentialCombo.SelectedItem =
            ResolvePreferredStoredCredential();

        UseStoredButton.IsEnabled =
            !_request.ForceReplacement && _request.ExistingCredentials.Count > 0;
        if (_request.ForceReplacement)
        {
            ReasonText.Text = "The last saved credential was not accepted. Enter the corrected login below and choose Save & use to replace it for future requests. The existing secret will not be tried again.";
            UseStoredButton.Content = "Previous saved login unavailable";
        }

        NIRACredentialMetadata? preferred = ResolvePreferredStoredCredential();
        AccountLabelBox.Text = !string.IsNullOrWhiteSpace(_request.RequestedAccountLabel)
            ? _request.RequestedAccountLabel
            : _request.ForceReplacement ? preferred?.AccountLabel ?? string.Empty : string.Empty;
        if (_request.ForceReplacement && preferred != null)
            UsernameBox.Text = preferred.Username;
    }

    private NIRACredentialMetadata? ResolvePreferredStoredCredential()
    {
        if (_request.ExistingCredentials.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(_request.RequestedAccountLabel))
        {
            NIRACredentialMetadata? exact =
                _request.ExistingCredentials.FirstOrDefault(
                    value =>
                        string.Equals(
                            value.AccountLabel,
                            _request.RequestedAccountLabel,
                            StringComparison.OrdinalIgnoreCase));

            if (exact != null)
            {
                return exact;
            }
        }

        return _request.ExistingCredentials[0];
    }

    private void UseStored_Click(object sender, RoutedEventArgs e)
    {
        if (_request.ForceReplacement) return;
        if (ExistingCredentialCombo.SelectedItem is not NIRACredentialMetadata selected)
        {
            MessageBox.Show(
                this,
                "Choose a saved login first.",
                "NIRA Secure Login",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        Resolution = new NIRACredentialPromptResponse
        {
            Choice = NIRACredentialPromptChoice.UseStored,
            CredentialId = selected.Id
        };

        DialogResult = true;
        Close();
    }

    private void UseOnce_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadNewCredential(out string label, out string username, out string secret))
        {
            return;
        }

        Resolution = new NIRACredentialPromptResponse
        {
            Choice = NIRACredentialPromptChoice.UseOnce,
            AccountLabel = label,
            Username = username,
            Secret = secret
        };

        DialogResult = true;
        Close();
    }

    private void SaveAndUse_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadNewCredential(out string label, out string username, out string secret))
        {
            return;
        }

        Resolution = new NIRACredentialPromptResponse
        {
            Choice = NIRACredentialPromptChoice.SaveAndUse,
            AccountLabel = label,
            Username = username,
            Secret = secret
        };

        DialogResult = true;
        Close();
    }

    private bool TryReadNewCredential(
        out string label,
        out string username,
        out string secret)
    {
        label = AccountLabelBox.Text.Trim();
        username = UsernameBox.Text.Trim();
        secret = SecretBox.Password;

        if (string.IsNullOrWhiteSpace(username) &&
            string.IsNullOrEmpty(secret))
        {
            MessageBox.Show(
                this,
                "Enter the account username/email and password/secret, or choose a saved login.",
                "NIRA Secure Login",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return false;
        }

        if (string.IsNullOrWhiteSpace(label))
        {
            label = string.IsNullOrWhiteSpace(username)
                ? "default"
                : username;
        }

        return true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Resolution = new NIRACredentialPromptResponse
        {
            Choice = NIRACredentialPromptChoice.Cancel
        };

        DialogResult = false;
        Close();
    }
}


