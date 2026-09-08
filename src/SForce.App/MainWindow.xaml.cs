using Microsoft.Win32;
using SForce.App.Services;
using System.IO;
using System.Windows.Media;

namespace SForce.App;

public partial class MainWindow : Window
{
    private readonly BackupStore _backupStore = new();
    private readonly FontCatalogService _fontCatalog = new();
    private readonly BrowserForceService _browserForce = new();
    private readonly WindhawkIntegrationService _windhawk = new();
    private readonly WindowsFontManager _windowsFonts;
    private IReadOnlyList<FontFaceInfo> _fontFaces = [];

    public MainWindow()
    {
        InitializeComponent();
        _windowsFonts = new WindowsFontManager(_backupStore);
        DeepForceCheck.IsEnabled = _windhawk.IsAvailable;
        DeepForceCheck.IsChecked = _windhawk.IsAvailable;
        Loaded += async (_, _) => await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        SetBusy(true, "در حال پیدا کردن فونت‌های SF نصب‌شده…");
        try
        {
            _fontFaces = await _fontCatalog.DiscoverSfFontsAsync();
            PopulateFontLists();
            RefreshStatus();
            OpenExtensionButton.IsEnabled = _browserForce.Exists();
            FooterText.Text = _fontFaces.Count == 0
                ? "برای شروع، فایل‌های رسمی SF Pro و SF Arabic را اضافه کن."
                : $"{_fontFaces.Count} چهرهٔ فونت SF پیدا شد.";
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void PopulateFontLists()
    {
        var previousLatin = LatinFamilyCombo.SelectedItem as string;
        var previousArabic = ArabicFamilyCombo.SelectedItem as string;
        var latin = _fontFaces.Where(x => !x.IsArabic).Select(x => x.Family).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var arabic = _fontFaces.Where(x => x.IsArabic).Select(x => x.Family).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        LatinFamilyCombo.ItemsSource = latin;
        ArabicFamilyCombo.ItemsSource = arabic;
        LatinFamilyCombo.SelectedItem = latin.FirstOrDefault(x => x.Equals(previousLatin, StringComparison.OrdinalIgnoreCase))
                                        ?? latin.FirstOrDefault(x => x.Equals("SF Pro Text", StringComparison.OrdinalIgnoreCase))
                                        ?? latin.FirstOrDefault();
        ArabicFamilyCombo.SelectedItem = arabic.FirstOrDefault(x => x.Equals(previousArabic, StringComparison.OrdinalIgnoreCase))
                                         ?? arabic.FirstOrDefault(x => x.Equals("SF Arabic", StringComparison.OrdinalIgnoreCase))
                                         ?? arabic.FirstOrDefault();
    }

    private void RefreshStatus()
    {
        var status = _windowsFonts.GetStatus();
        AdminStatus.Text = status.IsAdministrator ? "فعال" : "غیرفعال";
        AdminStatus.Foreground = status.IsAdministrator ? FindBrush("AccentBrush") : FindBrush("DangerBrush");
        RestoreButton.IsEnabled = _backupStore.LoadActiveBackup() is not null;
        BackupStatus.Text = status.LastBackupAt?.ToString("yyyy/MM/dd  HH:mm") ?? "ندارد";
        DeepForceStatus.Text = !_windhawk.IsAvailable ? "Windhawk پیدا نشد" : _windhawk.IsEnabled ? "فعال" : "آماده";
        DeepForceStatus.Foreground = _windhawk.IsEnabled ? FindBrush("AccentBrush") : FindBrush("MutedBrush");

        if (status.IsApplied)
        {
            StatusText.Text = $"فعال: {status.ActiveLatinFamily} + {status.ActiveArabicFamily}";
            StatusPill.Background = FindBrush("AccentBrush");
            StatusPillText.Foreground = new SolidColorBrush(Color.FromRgb(16, 19, 14));
            StatusPillText.Text = "ACTIVE";
        }
        else
        {
            StatusText.Text = "ویندوز هنوز روی فونت اصلی خودش است.";
            StatusPill.Background = new SolidColorBrush(Color.FromRgb(40, 44, 52));
            StatusPillText.Foreground = FindBrush("MutedBrush");
            StatusPillText.Text = "READY";
        }
    }

    private async void ImportFonts_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "فایل‌های رسمی SF Pro و SF Arabic را انتخاب کن",
            Filter = "OpenType / TrueType fonts (*.otf;*.ttf)|*.otf;*.ttf|All files (*.*)|*.*",
            Multiselect = true,
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        SetBusy(true, "در حال نصب فونت‌ها…");
        try
        {
            var installed = await _fontCatalog.InstallAsync(dialog.FileNames);
            FooterText.Text = $"{installed.Count} فایل فونت با موفقیت نصب شد.";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        var profile = TryBuildProfile();
        if (profile is null)
        {
            return;
        }

        SetBusy(true, "در حال نصب سیستمی، ساخت بکاپ و اعمال SF…");
        try
        {
            var selectedFiles = _fontFaces
                .Where(x => !string.IsNullOrWhiteSpace(x.FilePath) &&
                            (x.Family.Equals(profile.LatinFamily, StringComparison.OrdinalIgnoreCase) ||
                             x.Family.Equals(profile.ArabicFamily, StringComparison.OrdinalIgnoreCase)))
                .Select(x => x.FilePath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (selectedFiles.Length > 0)
            {
                var installed = await _fontCatalog.InstallAsync(selectedFiles);
                profile = profile with
                {
                    ArabicFontFile = installed
                        .Where(x => x.IsArabic && x.Family.Equals(profile.ArabicFamily, StringComparison.OrdinalIgnoreCase))
                        .OrderBy(x => x.Face.Contains("Regular", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                        .FirstOrDefault()?.FilePath ?? profile.ArabicFontFile
                };
            }

            var result = _windowsFonts.Apply(profile);
            if (!result.Success)
            {
                ShowError(result.Message);
                return;
            }

            if (DeepForceCheck.IsChecked == true)
            {
                var deepResult = _windhawk.Enable(profile);
                if (!deepResult.Success)
                {
                    ShowError(result.Message + Environment.NewLine + deepResult.Message);
                    RefreshStatus();
                    return;
                }
                result = result with { Message = result.Message + Environment.NewLine + deepResult.Message };
            }
            else if (_windhawk.IsEnabled)
            {
                _windhawk.Disable();
            }

            FooterText.Text = result.Message.Replace(Environment.NewLine, "  ");
            RefreshStatus();
            MessageBox.Show(result.Message, "SFORCE", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(
            "تمام aliasها و فونت‌های رابط ویندوز به snapshot قبل از اولین Apply برگردند؟",
            "بازگردانی SFORCE",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        SetBusy(true, "در حال بازگردانی snapshot اصلی…");
        try
        {
            var deepResult = _windhawk.Disable();
            var result = _windowsFonts.Restore();
            if (!result.Success)
            {
                ShowError(result.Message + (deepResult.Success ? string.Empty : Environment.NewLine + deepResult.Message));
                return;
            }

            FooterText.Text = result.Message;
            RefreshStatus();
            MessageBox.Show(result.Message, "SFORCE", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void BuildBrowserPack_Click(object sender, RoutedEventArgs e)
    {
        var profile = TryBuildProfile();
        if (profile is null)
        {
            return;
        }

        try
        {
            var path = _browserForce.Build(profile);
            OpenExtensionButton.IsEnabled = true;
            _browserForce.OpenDirectory();
            var browser = _browserForce.OpenExtensionsPage();
            BrowserHint.Text = browser is null
                ? $"Force Pack ساخته شد: {path}\nصفحهٔ Extensions مرورگرت را باز و Load unpacked را انتخاب کن."
                : $"Force Pack ساخته شد. در {browser}: Developer mode → Load unpacked → همین پوشه.";
            FooterText.Text = "Force Pack مرورگر آماده است.";
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private void OpenExtension_Click(object sender, RoutedEventArgs e)
    {
        if (_browserForce.Exists())
        {
            _browserForce.OpenDirectory();
        }
    }

    private ForceProfile? TryBuildProfile()
    {
        if (LatinFamilyCombo.SelectedItem is not string latin || ArabicFamilyCombo.SelectedItem is not string arabic)
        {
            ShowError("اول هر دو خانوادهٔ SF Pro و SF Arabic را اضافه و انتخاب کن.");
            return null;
        }

        var arabicFile = _fontFaces
            .Where(x => x.IsArabic && x.Family.Equals(arabic, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(x.FilePath))
            .OrderBy(x => x.Face.Contains("Regular", StringComparison.OrdinalIgnoreCase) ||
                          x.Face.Contains("Roman", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(x => x.Face, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault()?.FilePath;
        var minimumWeight = WeightCombo.SelectedItem is ComboBoxItem item &&
                            int.TryParse(item.Tag?.ToString(), out var parsed)
            ? parsed
            : 500;
        return new ForceProfile(latin, arabic, arabicFile, AggressiveCheck.IsChecked == true)
        {
            MinimumWeight = minimumWeight
        };
    }

    private void FontSelection_Changed(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (LatinFamilyCombo.SelectedItem is string latin)
            {
                PreviewLatin.FontFamily = new FontFamily(latin);
            }

            if (ArabicFamilyCombo.SelectedItem is string arabic)
            {
                PreviewArabic.FontFamily = new FontFamily(arabic);
                if (LatinFamilyCombo.SelectedItem is string selectedLatin)
                {
                    FontFamily = new FontFamily($"{selectedLatin}, {arabic}");
                }
            }
        }
        catch (ArgumentException)
        {
            // A malformed family name should not prevent selecting another font.
        }
    }

    private void SetBusy(bool busy, string? message = null)
    {
        ApplyButton.IsEnabled = !busy;
        BrowserButton.IsEnabled = !busy;
        LatinFamilyCombo.IsEnabled = !busy;
        ArabicFamilyCombo.IsEnabled = !busy;
        WeightCombo.IsEnabled = !busy;
        DeepForceCheck.IsEnabled = !busy && _windhawk.IsAvailable;
        Mouse.OverrideCursor = busy ? Cursors.Wait : null;
        if (message is not null)
        {
            FooterText.Text = message;
        }
    }

    private void ShowError(string message)
    {
        FooterText.Text = message.Replace(Environment.NewLine, "  ");
        MessageBox.Show(message, "SFORCE", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private SolidColorBrush FindBrush(string key) => (SolidColorBrush)FindResource(key);

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }
        else
        {
            DragMove();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
}
