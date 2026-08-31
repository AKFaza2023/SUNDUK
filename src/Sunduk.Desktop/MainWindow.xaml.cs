using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Navigation;
using Microsoft.Win32;
using Sunduk.Desktop.Services;

namespace Sunduk.Desktop;

public partial class MainWindow : Window
{
    private string _currentTheme = "Dark";

    public MainWindow()
    {
        InitializeComponent();

        try
        {
            DatabaseService.Initialize();
            StatusText.Text = "Локальная база данных подключена";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Ошибка базы данных: {ex.Message}";
        }

        ApplyTheme("Dark");
        ShowPage("Главная");
        RefreshAll();
    }

    private void NavButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string page)
        {
            ShowPage(page);
        }
    }

    private void ShowPage(string page)
    {
        PageTitle.Text = page;
        DashboardView.Visibility = Visibility.Collapsed;
        CatalogView.Visibility = Visibility.Collapsed;
        PlaceholderView.Visibility = Visibility.Collapsed;

        if (page == "Главная")
        {
            PageSubtitle.Text = "Рабочее пространство SUNDUK";
            DashboardView.Visibility = Visibility.Visible;
            RefreshAll();
            return;
        }

        if (page == "Каталог")
        {
            PageSubtitle.Text = "Карточки, категории и быстрый поиск";
            CatalogView.Visibility = Visibility.Visible;
            RefreshCatalog();
            return;
        }

        PageSubtitle.Text = "Модуль SUNDUK";
        PlaceholderTitle.Text = page;

        PlaceholderText.Text = page switch
        {
            "Объявления" => "Модуль объявлений подготовлен под Avito, ЦИАН и другие источники. Парсеры и WebView2 подключаются на следующем этапе.",
            "Ежедневник" => "Ежедневник присутствует в структуре интерфейса и будет подключён к задачам и напоминаниям.",
            "Напоминания" => "Напоминания запланированы как отдельный рабочий модуль с локальными уведомлениями.",
            "Файлы" => "Раздел файлов подготовлен для локальных вложений карточек и дальнейшей синхронизации.",
            "Настройки" => $"Активная тема: {_currentTheme}. Дополнительные параметры будут добавляться по мере развития проекта.",
            "Поддержка" => "SUNDUK — разработка СтавДок. Сайт разработчика доступен по ссылке в нижней части боковой панели.",
            _ => $"Раздел «{page}» уже включён в навигацию SUNDUK и будет развиваться по дорожной карте проекта."
        };

        PlaceholderView.Visibility = Visibility.Visible;
    }

    private void AddCard_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var title = $"Новая карточка {DateTime.Now:dd.MM HH:mm}";
            DatabaseService.AddItem("Товар", title, "Без категории", "Создано из интерфейса SUNDUK.");
            StatusText.Text = $"Создана карточка: {title}";
            RefreshAll();
            RefreshCatalog();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Не удалось создать карточку.\n\n{ex.Message}", "SUNDUK", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void QuickAdd_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string tag)
        {
            return;
        }

        var parts = tag.Split('|');
        if (parts.Length < 3)
        {
            return;
        }

        try
        {
            var title = $"{parts[1]} {DateTime.Now:dd.MM HH:mm}";
            DatabaseService.AddItem(parts[0], title, parts[2], "Создано быстрым действием.");
            StatusText.Text = $"Добавлено: {title}";
            RefreshAll();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Не удалось сохранить запись.\n\n{ex.Message}", "SUNDUK", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        RefreshCatalog();
    }

    private void RefreshAll()
    {
        try
        {
            ProductsCountText.Text = DatabaseService.CountByKind("Товар").ToString();
            ContactsCountText.Text = DatabaseService.CountByKind("Контакт").ToString();
            NotesCountText.Text = DatabaseService.CountByKind("Заметка").ToString();
            BookmarksCountText.Text = DatabaseService.CountByKind("Закладка").ToString();
            LatestList.ItemsSource = DatabaseService.GetItems(limit: 8);
        }
        catch
        {
        }
    }

    private void RefreshCatalog()
    {
        try
        {
            var search = SearchBox?.Text;
            CatalogList.ItemsSource = DatabaseService.GetItems(search);
        }
        catch
        {
        }
    }

    private void DarkTheme_Click(object sender, RoutedEventArgs e) => ApplyTheme("Dark");
    private void LightTheme_Click(object sender, RoutedEventArgs e) => ApplyTheme("Light");
    private void SystemTheme_Click(object sender, RoutedEventArgs e) => ApplyTheme("System");

    private void ApplyTheme(string theme)
    {
        _currentTheme = theme;

        var effectiveTheme = theme;
        if (theme == "System")
        {
            effectiveTheme = IsWindowsLightTheme() ? "Light" : "Dark";
        }

        if (effectiveTheme == "Light")
        {
            SetBrush("BgBrush", "#F4F7FA");
            SetBrush("PanelBrush", "#FFFFFF");
            SetBrush("PanelAltBrush", "#EEF2F6");
            SetBrush("TextBrush", "#16202A");
            SetBrush("MutedBrush", "#647383");
            SetBrush("BorderBrush", "#DCE3EA");
            SetBrush("AccentBrush", "#20945B");
            SetBrush("AccentHoverBrush", "#27A968");
        }
        else
        {
            SetBrush("BgBrush", "#0E131A");
            SetBrush("PanelBrush", "#151C25");
            SetBrush("PanelAltBrush", "#1C2530");
            SetBrush("TextBrush", "#F7FAFC");
            SetBrush("MutedBrush", "#9AA8B8");
            SetBrush("BorderBrush", "#2B3745");
            SetBrush("AccentBrush", "#24A866");
            SetBrush("AccentHoverBrush", "#2DBE76");
        }

        StatusText.Text = theme switch
        {
            "Dark" => "Тема: Тёмная",
            "Light" => "Тема: Светлая",
            _ => $"Тема: Как в системе ({effectiveTheme})"
        };

        if (PageTitle.Text == "Настройки")
        {
            ShowPage("Настройки");
        }
    }

    private static bool IsWindowsLightTheme()
    {
        try
        {
            var value = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 0);
            return value is int i && i > 0;
        }
        catch
        {
            return false;
        }
    }

    private static void SetBrush(string key, string hexColor)
    {
        Application.Current.Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hexColor));
    }

    private void DeveloperSite_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Не удалось открыть сайт.\n\n{ex.Message}", "SUNDUK", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
