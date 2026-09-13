using System.Windows;
using System.Windows.Input;
using PrayTime.App.Services;
using PrayTime.App.ViewModels;
using PrayTime.Core.Security;

namespace PrayTime.App.Views;

public partial class PinDialog : Window
{
    private readonly AppShell _shell;
    private readonly bool _isCreateMode;

    private PinDialog(AppShell shell, bool createMode)
    {
        _shell = shell;
        _isCreateMode = createMode;
        InitializeComponent();

        if (createMode)
        {
            TitleText.Text = "تعيين رمز الخروج";
            HintText.Text = $"اختر رمزًا من {Numerals.Int(PinHasher.MinPinLength)} إلى " +
                            $"{Numerals.Int(PinHasher.MaxPinLength)} أرقام. سيُطلب منك كلما أردت " +
                            "إنهاء التطبيق. إن نسيته، احذف ملف settings.json لإعادة الضبط.";
            Pin2.Visibility = Visibility.Visible;
        }

        Loaded += (_, _) => Pin1.Focus();
    }

    /// <summary>يطلب من المستخدم إنشاء رمز جديد. يُرجع true إن أُنشئ.</summary>
    public static bool PromptCreate(AppShell shell) =>
        new PinDialog(shell, createMode: true).ShowDialog() == true;

    /// <summary>يطلب الرمز للتحقق. يُرجع true عند النجاح.</summary>
    public static bool PromptVerify(AppShell shell) =>
        new PinDialog(shell, createMode: false).ShowDialog() == true;

    private void OnDigitsOnly(object sender, TextCompositionEventArgs e) =>
        e.Handled = !e.Text.All(char.IsAsciiDigit);

    private void OnPinKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) OnOkClick(sender, new RoutedEventArgs());
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        var pin = Pin1.Password;

        if (_isCreateMode)
        {
            if (!PinHasher.IsAcceptablePin(pin))
            {
                ShowError($"الرمز يجب أن يكون بين {Numerals.Int(PinHasher.MinPinLength)} " +
                          $"و{Numerals.Int(PinHasher.MaxPinLength)} أرقام.");
                return;
            }

            if (pin != Pin2.Password)
            {
                ShowError("الرمزان غير متطابقين.");
                Pin2.Clear();
                Pin2.Focus();
                return;
            }

            _shell.Pin.SetPin(pin);
            DialogResult = true;
            return;
        }

        if (_shell.Pin.IsLockedOut)
        {
            ShowError($"محاولات كثيرة خاطئة. حاول بعد {Numerals.HumanDuration(_shell.Pin.LockRemaining)}.");
            return;
        }

        switch (_shell.Pin.Verify(pin))
        {
            case PinVerifyResult.Ok:
                DialogResult = true;
                break;

            case PinVerifyResult.Wrong:
                ShowError("رمز غير صحيح.");
                Pin1.Clear();
                Pin1.Focus();
                break;

            case PinVerifyResult.LockedOut:
                ShowError($"محاولات كثيرة خاطئة. حاول بعد {Numerals.HumanDuration(_shell.Pin.LockRemaining)}.");
                Pin1.Clear();
                break;

            case PinVerifyResult.NotConfigured:
                DialogResult = true; // لا رمز مضبوطًا أصلًا
                break;

            case PinVerifyResult.UnreadableOnThisMachine:
                ShowError("تعذّرت قراءة الرمز على هذا الجهاز (نُقلت الإعدادات من حساب آخر). " +
                          "احذف ملف settings.json لإعادة الضبط.");
                break;
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
