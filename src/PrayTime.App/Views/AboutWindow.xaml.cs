using System.Diagnostics;
using System.Windows;
using PrayTime.App.Infrastructure;
using PrayTime.App.ViewModels;

namespace PrayTime.App.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();

        VersionText.Text = $"الإصدار {Numerals.Convert(AppInfo.Version)}";
        DeveloperText.Text = AppInfo.Developer;
        PhoneText.Text = AppInfo.Phone;
        CopyrightText.Text = AppInfo.Copyright;
    }

    private void OnOpenProject(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(AppInfo.ProjectUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warn($"تعذّر فتح صفحة المشروع: {ex.Message}");
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
