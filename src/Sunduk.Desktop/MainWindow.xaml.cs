using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
            PageSubtitle.Text = "Ваше рабочее пространство и быстрый доступ к важному";
            DashboardView.Visibility = Visibility.Visible;
            RefreshAll();
            return;
        }

        if (page == "Каталог")
        {
            PageSubtitle.Text = "Карточки, категории, фильтры и быстрый поиск";
            CatalogView.Visibility = Visibility.Visible;
            RefreshCatalog();
            return;
        }

        PageSubtitle.Text = "Модуль SUNDUK";
        PlaceholderTitle.Text = page;

        PlaceholderText.Text = page switch
        {
            "Объявления" => "Единое рабочее место для объявлений и внешних источников. Модуль подключён к утверждённой оболочке SUNDUK.",
            "Закладки" => "Сохранённые ссылки и быстрый доступ к важным интернет-ресурсам.",
            "Контакты" => "Контакты, организации и связанные записи в единой локальной базе.",
            "Заметки" => "Личные и рабочие заметки с привязкой к другим данным SUNDUK.",
            "Ежедневник" => "Ежедневник и календарное представление рабочих событий.",
            "Задачи" => "Задачи, статусы и контроль выполнения в едином рабочем пространстве.",
            "Напоминания" => "Локальные напоминания и уведомления SUNDUK.",
            "Файлы" => "Локальные файлы и вложения карточек с дальнейшим развитием синхронизации.",
            "Избранное" => "Избранные карточки, записи и быстрые ссылки.",
            "Настройки" => $"Активная тема: {_currentTheme}. Настройки применяются без перезапуска приложения.",
            "Поддержка" => "SUNDUK — разработка СтавДок. Сайт разработчика доступен по ссылке «ставдок.рф» в нижней части боковой панели.",
            _ => $"Раздел «{page}» подключён к общей навигации SUNDUK."
        };

        PlaceholderView.Visibility = Visibility.Visible;
    }

    private void TopSearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        ShowPage("Каталог");
        SearchBox.Text = TopSearchBox.Text;
        RefreshCatalog();
        StatusText.Text = string.IsNullOrWhiteSpace(TopSearchBox.Text)
            ? "Открыт каталог"
            : $"Поиск: {TopSearchBox.Text.Trim()}";
        e.Handled = true;
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
            SetBrush("ShellBrush", "#F3F6F8");
            SetBrush("ShellDeepBrush", "#E8EEF2");
            SetBrush("ShellHoverBrush", "#E1E9EE");
            SetBrush("ShellTextBrush", "#173042");
            SetBrush("ShellMutedBrush", "#6D7D89");
            SetBrush("WorkspaceBrush", "#F7F9FB");
            SetBrush("CardBrush", "#FFFFFF");
            SetBrush("InputBrush", "#F8FAFC");
            SetBrush("TextBrush", "#17212B");
            SetBrush("MutedBrush", "#697887");
            SetBrush("BorderBrush", "#E1E7ED");
            SetBrush("AccentBrush", "#1FA35B");
            SetBrush("AccentHoverBrush", "#188A4D");
            SetBrush("AccentSoftBrush", "#EAF7F0");
            SetBrush("AccentTextBrush", "#167A45");
        }
        else
        {
            // Approved default: dark-blue shell, white/light workspace and green accents.
            SetBrush("ShellBrush", "#11283A");
            SetBrush("ShellDeepBrush", "#0D2030");
            SetBrush("ShellHoverBrush", "#1A354A");
            SetBrush("ShellTextBrush", "#F7FBFF");
            SetBrush("ShellMutedBrush", "#A8B9C6");
            SetBrush("WorkspaceBrush", "#F5F7FA");
            SetBrush("CardBrush", "#FFFFFF");
            SetBrush("InputBrush", "#F8FAFC");
            SetBrush("TextBrush", "#17212B");
            SetBrush("MutedBrush", "#697887");
            SetBrush("BorderBrush", "#E1E7ED");
            SetBrush("AccentBrush", "#1FA35B");
            SetBrush("AccentHoverBrush", "#188A4D");
            SetBrush("AccentSoftBrush", "#EAF7F0");
            SetBrush("AccentTextBrush", "#167A45");
        }

        StatusText.Text = theme switch
        {
            "Dark" => "Тема: Тёмная — утверждённая оболочка SUNDUK",
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
