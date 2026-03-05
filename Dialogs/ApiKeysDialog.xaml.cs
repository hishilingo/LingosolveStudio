using System.Windows;
using System.Windows.Controls.Primitives;
using LingosolveStudio.Services;

namespace LingosolveStudio.Dialogs
{
    public partial class ApiKeysDialog : Window
    {
        private AISettings settings;

        public ApiKeysDialog()
        {
            InitializeComponent();
            LoadSettings();
        }

        private void LoadSettings()
        {
            settings = AISettings.Load();
            pwdApiKey.Password = settings.OpenRouterApiKey ?? string.Empty;
            txtApiKey.Text = settings.OpenRouterApiKey ?? string.Empty;
            txtModelName.Text = settings.ModelName ?? "google/gemini-2.0-flash-001";
        }

        private void btnShowKey_Click(object sender, RoutedEventArgs e)
        {
            if (btnShowKey.IsChecked == true)
            {
                txtApiKey.Text = pwdApiKey.Password;
                pwdApiKey.Visibility = Visibility.Collapsed;
                txtApiKey.Visibility = Visibility.Visible;
                txtApiKey.Focus();
            }
            else
            {
                pwdApiKey.Password = txtApiKey.Text;
                txtApiKey.Visibility = Visibility.Collapsed;
                pwdApiKey.Visibility = Visibility.Visible;
                pwdApiKey.Focus();
            }
        }

        private void btnSave_Click(object sender, RoutedEventArgs e)
        {
            settings.OpenRouterApiKey = txtApiKey.Visibility == Visibility.Visible ? txtApiKey.Text : pwdApiKey.Password;
            settings.ModelName = !string.IsNullOrWhiteSpace(txtModelName.Text) ? txtModelName.Text : "google/gemini-2.0-flash-001";
            settings.Save();
            this.DialogResult = true;
            this.Close();
        }
    }
}
