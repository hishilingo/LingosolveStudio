using System.Windows;
using LingosolveStudio.Services;

namespace LingosolveStudio.Dialogs
{
    public partial class AITranslationDialog : Window
    {
        private AISettings settings;

        public AITranslationDialog()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            settings = AISettings.Load();

            cmbLanguage.Items.Clear();
            foreach (var lang in AISettings.AvailableLanguages)
            {
                cmbLanguage.Items.Add(lang);
            }

            chkEnableTranslation.IsChecked = settings.AutoTranslationEnabled;
            chkSaveToFile.IsChecked = settings.SaveOutputToFile;

            if (!string.IsNullOrEmpty(settings.TargetLanguage) && cmbLanguage.Items.Contains(settings.TargetLanguage))
                cmbLanguage.SelectedItem = settings.TargetLanguage;
            else
                cmbLanguage.SelectedIndex = 0;

            UpdateStatus();
            UpdateUI();
        }

        private void UpdateStatus()
        {
            if (settings.HasApiKey())
            {
                txtStatus.Text = $"Using model: {settings.ModelName}\nOpenRouter API key is configured.";
                txtStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Green);
            }
            else
            {
                txtStatus.Text = "No OpenRouter API key configured.\nPlease set up your API key in Settings > API Keys first.";
                txtStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Red);
            }
        }

        private void UpdateUI()
        {
            bool enabled = chkEnableTranslation.IsChecked == true;
            cmbLanguage.IsEnabled = enabled;
            chkSaveToFile.IsEnabled = enabled;
        }

        private void chkEnableTranslation_Changed(object sender, RoutedEventArgs e)
        {
            UpdateUI();
        }

        private void btnSave_Click(object sender, RoutedEventArgs e)
        {
            settings.AutoTranslationEnabled = chkEnableTranslation.IsChecked == true;
            settings.TargetLanguage = cmbLanguage.SelectedItem?.ToString() ?? "English";
            settings.SaveOutputToFile = chkSaveToFile.IsChecked == true;
            settings.Save();
            this.DialogResult = true;
            this.Close();
        }
    }
}
