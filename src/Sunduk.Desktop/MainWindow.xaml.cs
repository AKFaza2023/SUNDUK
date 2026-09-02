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

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

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
            PageSubtitle.Text = "Обзор ваших данных и быстрый доступ к важному";
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
            "Объявления" => "Объявления, публикации и внешние источники.",
            "Закладки" => "Сохранённые ссылки и быстрый доступ.",
            "Контакты" => "Контакты, организации и связанные записи.",
            "Заметки" => "Личные и рабочие заметки.",
            "Ежедневник" => "Календарь и события.",
            "Задачи" => "Задачи, статусы и контроль выполнения.",
            "Напоминания" => "Напоминания и локальные уведомления.",
            "Файлы" => "Файлы и вложения карточек.",
            "Избранное" => "Избранные записи SUNDUK.",
            "Настройки" => $"Активная тема: {_currentTheme}.",
            "Поддержка" => "SUNDUK — разработка СтавДок. ставдок.рф",
            _ => $"Раздел «{page}» подключён к SUNDUK."
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

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshCatalog();

    private void RefreshAll()
    {
        try
        {
            ProductsCountText.Text = DatabaseService.CountByKind("Товар").ToString();
            ContactsCountText.Text = DatabaseService.CountByKind("Контакт").ToString();
            LatestList.ItemsSource = DatabaseService.GetItems(limit: 5);
        }
        catch
        {
        }
    }

    private void RefreshCatalog()
    {
        try
        {
            CatalogList.ItemsSource = DatabaseService.GetItems(SearchBox?.Text);
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
        var effectiveTheme = theme == "System" ? (IsWindowsLightTheme() ? "Light" : "Dark") : theme;

        // The approved main reference keeps the dark navy shell in the default theme.
        if (effectiveTheme == "Light")
        {
            SetBrush("ShellBrush", "#F3F6F8");
            SetBrush("ShellTextBrush", "#173042");
        }
        else
        {
            SetBrush("ShellBrush", "#00162E");
            SetBrush("ShellTextBrush", "#F7FBFF");
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
