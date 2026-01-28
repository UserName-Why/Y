using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using System.Text.Json;

namespace Windows_Optimization
{
    public partial class MainWindow : Window
    {
        #region Поля и свойства
        
        private const string APP_FOLDER_NAME = "Anime_Manga_Novell_Read";
        private const string ANIME_FILE = "Anime.txt";
        private const string MANGA_FILE = "Manga.txt";
        private const string NOVEL_FILE = "Novel.txt";
        private const string FAVORITES_FILE = "Favorites.txt";
        private const string SETTINGS_FILE = "Settings.json";
        private const string DEFAULT_COLOR_HEX = "#8A2BE2";
        
        private readonly string _appFolderPath;
        private readonly Dictionary<string, string> _filePaths;
        
        // Цвета для разных элементов
        private Color _borderColor;
        private Color _dividerColor;
        private Color _buttonHoverColor;
        
        // Настройки
        private bool _confirmDelete = true;
        private bool _autoRemoveOld = false;
        private double _backgroundBrightness = 0.5;
        private double _textBrightness = 0.8;
        
        private string _currentCategory = "";
        private List<string> _currentItems = new List<string>();
        private HashSet<string> _favorites = new HashSet<string>();
        private bool _isDeleteMode = false;
        private bool _isAddMode = false;
        
        // Таймер для поиска с задержкой
        private DispatcherTimer? _searchTimer;
        
        // Для отслеживания кликов на кнопки категорий
        private DateTime? _lastCategoryClickTime = null;
        private string? _lastCategoryClicked = null;
        
        // Для сортировки
        private string _currentSort = "date";
        
        #endregion
        
        #region Конструктор и инициализация
        
        public MainWindow()
        {
            InitializeComponent();
            
            // Инициализация путей
            _appFolderPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
                APP_FOLDER_NAME);
            
            _filePaths = new Dictionary<string, string>
            {
                { "Аниме", Path.Combine(_appFolderPath, ANIME_FILE) },
                { "Манга", Path.Combine(_appFolderPath, MANGA_FILE) },
                { "Новелла", Path.Combine(_appFolderPath, NOVEL_FILE) }
            };
            
            InitializeApplication();
        }
        
        private void InitializeApplication()
        {
            // Создаем папку при первом запуске
            if (!Directory.Exists(_appFolderPath))
            {
                Directory.CreateDirectory(_appFolderPath);
            }
            
            // Загружаем настройки
            LoadSettings();
            
            // Загружаем избранное
            LoadFavorites();
            
            // Создаем пустые файлы при первом запуске
            InitializeFiles();
            
            // Подключаем обработчики событий
            InitializeEventHandlers();
            
            // Применяем настройки
            ApplyAllSettings();
            
            // Устанавливаем начальный текст
            ContentTitle.Text = "Главное";
            ContentText.Text = "Выберите категорию слева";
            
            // Скрываем панели по умолчанию
            SearchPanel.Visibility = Visibility.Collapsed;
            
            // Инициализируем значения полей настроек
            UpdateSettingsValues();
            
            // Инициализируем размер окна
            this.Width = 1000;
            this.Height = 600;
        }
        
        private void InitializeEventHandlers()
        {
            // Подключаем обработчики слайдеров
            WindowWidthSlider.ValueChanged += WindowWidthSlider_ValueChanged;
            WindowHeightSlider.ValueChanged += WindowHeightSlider_ValueChanged;
            BackgroundBrightnessSlider.ValueChanged += BackgroundBrightnessSlider_ValueChanged;
            TextBrightnessSlider.ValueChanged += TextBrightnessSlider_ValueChanged;
            
            // Подключаем обработчики текстовых полей
            SearchTextBox.TextChanged += SearchTextBox_TextChanged;
            
            // Инициализация таймера поиска
            InitializeSearchTimer();
        }
        
        private void InitializeSearchTimer()
        {
            _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _searchTimer.Tick += (s, e) =>
            {
                _searchTimer?.Stop();
                PerformSearch();
            };
        }
        
        private void LoadFavorites()
        {
            var favoritesPath = Path.Combine(_appFolderPath, FAVORITES_FILE);
            if (File.Exists(favoritesPath))
            {
                _favorites = new HashSet<string>(File.ReadAllLines(favoritesPath)
                    .Where(line => !string.IsNullOrWhiteSpace(line))
                    .Select(line => line.Trim()));
            }
        }
        
        private void SaveFavorites()
        {
            var favoritesPath = Path.Combine(_appFolderPath, FAVORITES_FILE);
            File.WriteAllLines(favoritesPath, _favorites);
        }
        
        #endregion
        
        #region Работа с файлами
        
        private void InitializeFiles()
        {
            try
            {
                EnsureFileExists(ANIME_FILE, Array.Empty<string>());
                EnsureFileExists(MANGA_FILE, Array.Empty<string>());
                EnsureFileExists(NOVEL_FILE, Array.Empty<string>());
            }
            catch (Exception ex)
            {
                ShowNotification($"Ошибка создания файлов: {ex.Message}", true);
            }
        }
        
        private void EnsureFileExists(string fileName, string[] defaultContent)
        {
            var filePath = Path.Combine(_appFolderPath, fileName);
            if (!File.Exists(filePath))
            {
                File.WriteAllLines(filePath, defaultContent);
            }
        }
        
        private void LoadItemsForCategory(string category, bool showList = false, bool isDeleteMode = false, bool isAddMode = false)
        {
            _isDeleteMode = isDeleteMode;
            _isAddMode = isAddMode;
            
            if (!_filePaths.TryGetValue(category, out string? filePath))
                return;
                
            _currentCategory = category;
            _currentItems.Clear();
            ItemsListPanel.Children.Clear();
            
            // ОБНОВЛЯЕМ ШАПКУ
            ContentTitle.Text = category;
            
            // Показываем/скрываем кнопки сортировки
            SortAZButton.Visibility = showList ? Visibility.Visible : Visibility.Collapsed;
            SortZAButton.Visibility = showList ? Visibility.Visible : Visibility.Collapsed;
            SortDateButton.Visibility = showList ? Visibility.Visible : Visibility.Collapsed;
            
            try
            {
                if (File.Exists(filePath))
                {
                    var itemsWithTimestamp = File.ReadAllLines(filePath)
                        .Where(line => !string.IsNullOrWhiteSpace(line))
                        .Select(line => line.Trim())
                        .Select(line =>
                        {
                            var parts = line.Split('|');
                            if (parts.Length == 2 && long.TryParse(parts[0], out long timestamp))
                            {
                                return new { Timestamp = timestamp, Name = parts[1] };
                            }
                            return new { Timestamp = DateTime.Now.Ticks, Name = line };
                        })
                        .ToList();
                    
                    // Сортируем
                    itemsWithTimestamp = _currentSort switch
                    {
                        "az" => itemsWithTimestamp.OrderBy(x => x.Name).ToList(),
                        "za" => itemsWithTimestamp.OrderByDescending(x => x.Name).ToList(),
                        _ => itemsWithTimestamp.OrderByDescending(x => x.Timestamp).ToList()
                    };
                    
                    // Избранные в начале
                    itemsWithTimestamp = itemsWithTimestamp
                        .OrderByDescending(x => _favorites.Contains($"{category}:{x.Name}"))
                        .ThenBy(x => _currentSort == "az" ? x.Name : "")
                        .ThenByDescending(x => _currentSort == "za" ? x.Name : "")
                        .ThenByDescending(x => _currentSort == "date" ? x.Timestamp : 0)
                        .ToList();
                    
                    _currentItems = itemsWithTimestamp.Select(x => x.Name).ToList();
                    
                    DisplayItems();
                    
                    // Показываем список если есть элементы ИЛИ если нажали "Посмотреть", "Удалить" или "Добавить"
                    if (_currentItems.Count > 0 || showList || isDeleteMode || isAddMode)
                    {
                        ShowItemsList();
                        
                        // Показываем панель поиска (всегда в режимах списка)
                        SearchPanel.Visibility = Visibility.Visible;
                        
                        // Показываем кнопку + ТОЛЬКО в режиме добавления
                        AddMainButton.Visibility = isAddMode ? Visibility.Visible : Visibility.Collapsed;
                    }
                }
            }
            catch (Exception ex)
            {
                ShowNotification($"Ошибка загрузки файла: {ex.Message}", true);
            }
        }
        
        private void AddItemToCurrentCategory(string item)
        {
            item = item?.Trim();
            
            if (string.IsNullOrWhiteSpace(item))
            {
                ShowNotification("Введите название элемента", true);
                return;
            }
            
            if (item.Length > 100)
            {
                ShowNotification("Название слишком длинное (макс. 100 символов)", true);
                return;
            }
            
            if (string.IsNullOrEmpty(_currentCategory) || 
                !_filePaths.TryGetValue(_currentCategory, out string? filePath))
            {
                ShowNotification("Категория не выбрана", true);
                return;
            }
            
            try
            {
                var existingItems = File.ReadAllLines(filePath)
                    .Select(line => line.Trim())
                    .Where(line => !string.IsNullOrWhiteSpace(line))
                    .ToList();
                
                // Проверяем, существует ли уже точно такой же элемент
                var exactMatch = existingItems
                    .FirstOrDefault(existing => string.Equals(
                        existing.Contains("|") ? existing.Split('|')[1] : existing, 
                        item, StringComparison.OrdinalIgnoreCase));
                
                if (exactMatch != null)
                {
                    ShowNotification("Такой элемент уже существует", true);
                    return;
                }
                
                // Автоматическое удаление старых похожих записей
                if (_autoRemoveOld)
                {
                    RemoveOldSimilarItems(item, existingItems, filePath);
                }
                
                // Добавляем новый элемент с временной меткой
                var itemWithTimestamp = $"{DateTime.Now.Ticks}|{item}";
                File.AppendAllText(filePath, $"\n{itemWithTimestamp}");
                
                // Обновляем список и показываем его
                LoadItemsForCategory(_currentCategory, true, false, true);
                
                // Скрываем окно добавления
                HideAddPanel();
                
                ShowNotification($"Добавлено: {item}");
            }
            catch (Exception ex)
            {
                ShowNotification($"Ошибка добавления: {ex.Message}", true);
            }
        }
        
        private void RemoveOldSimilarItems(string newItem, List<string> existingItems, string filePath)
        {
            // Извлекаем базовое имя (например, "Наруто" из "Наруто серия 13")
            var baseName = ExtractBaseName(newItem);
            
            if (string.IsNullOrEmpty(baseName))
                return;
            
            // Ищем похожие элементы
            var similarItems = existingItems
                .Select(line =>
                {
                    var parts = line.Split('|');
                    var name = parts.Length == 2 ? parts[1] : line;
                    return new { Line = line, Name = name, Timestamp = parts.Length == 2 ? parts[0] : "" };
                })
                .Where(x => IsSimilarName(x.Name, baseName))
                .ToList();
            
            if (similarItems.Count >= 2)
            {
                // Оставляем только самый новый и удаляем остальные
                var itemsToKeep = similarItems
                    .OrderByDescending(x => x.Timestamp)
                    .Take(1)
                    .ToList();
                
                var itemsToRemove = similarItems.Except(itemsToKeep).ToList();
                
                foreach (var itemToRemove in itemsToRemove)
                {
                    existingItems.Remove(itemToRemove.Line);
                    ShowNotification($"Автоматически удалено: {itemToRemove.Name}");
                }
                
                // Сохраняем обновленный список
                File.WriteAllLines(filePath, existingItems);
            }
        }
        
        private string ExtractBaseName(string itemName)
        {
            // Удаляем числа и серии
            var cleaned = Regex.Replace(itemName, @"\s*(серия|сезон|эпизод|глава|том|part|chapter|episode|season|vol\.?)\s*\d+", "", 
                RegexOptions.IgnoreCase);
            
            // Удаляем числа в конце
            cleaned = Regex.Replace(cleaned, @"\s+\d+$", "");
            
            // Удаляем специальные символы
            cleaned = Regex.Replace(cleaned, @"[^\w\sа-яА-ЯёЁ]", " ");
            
            return cleaned.Trim();
        }
        
        private bool IsSimilarName(string item1, string item2)
        {
            // Приводим к нижнему регистру
            var name1 = item1.ToLower();
            var name2 = item2.ToLower();
            
            // Проверяем точное совпадение базовых имен
            var base1 = ExtractBaseName(name1);
            var base2 = ExtractBaseName(name2);
            
            if (string.Equals(base1, base2, StringComparison.OrdinalIgnoreCase))
                return true;
            
            // Проверяем частичное совпадение
            if (base1.Contains(base2) || base2.Contains(base1))
                return true;
            
            // Проверяем расстояние Левенштейна для похожих названий
            var distance = LevenshteinDistance(base1, base2);
            var maxLength = Math.Max(base1.Length, base2.Length);
            
            return maxLength > 0 && (double)distance / maxLength < 0.3;
        }
        
        private int LevenshteinDistance(string s, string t)
        {
            if (string.IsNullOrEmpty(s))
                return string.IsNullOrEmpty(t) ? 0 : t.Length;
            
            if (string.IsNullOrEmpty(t))
                return s.Length;
            
            var n = s.Length;
            var m = t.Length;
            var d = new int[n + 1, m + 1];
            
            for (var i = 0; i <= n; d[i, 0] = i++) { }
            for (var j = 0; j <= m; d[0, j] = j++) { }
            
            for (var i = 1; i <= n; i++)
            {
                for (var j = 1; j <= m; j++)
                {
                    var cost = (t[j - 1] == s[i - 1]) ? 0 : 1;
                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost);
                }
            }
            
            return d[n, m];
        }
        
        private void DeleteItem(string item)
        {
            if (string.IsNullOrEmpty(_currentCategory) || 
                !_filePaths.TryGetValue(_currentCategory, out string? filePath))
            {
                ShowNotification("Категория не выбрана", true);
                return;
            }
            
            // Проверяем, является ли элемент избранным
            var fullItemName = $"{_currentCategory}:{item}";
            bool isFavorite = _favorites.Contains(fullItemName);
            
            if (isFavorite)
            {
                var result = MessageBox.Show(
                    $"Элемент \"{item}\" находится в избранном.\nУдалить его из избранного и продолжить удаление?", 
                    "Элемент в избранном", 
                    MessageBoxButton.YesNo, 
                    MessageBoxImage.Question);
                
                if (result != MessageBoxResult.Yes)
                    return;
                
                // Удаляем из избранного
                _favorites.Remove(fullItemName);
                SaveFavorites();
            }
            
            // Проверяем настройку подтверждения удаления
            if (_confirmDelete)
            {
                var result = MessageBox.Show(
                    $"Вы уверены, что хотите удалить \"{item}\"?", 
                    "Подтверждение удаления", 
                    MessageBoxButton.YesNo, 
                    MessageBoxImage.Question);
                
                if (result != MessageBoxResult.Yes)
                    return;
            }
            
            try
            {
                var items = File.ReadAllLines(filePath)
                    .Where(line => !string.IsNullOrWhiteSpace(line))
                    .Select(line => line.Trim())
                    .ToList();
                
                // Ищем элемент для удаления (с временной меткой или без)
                var itemToRemove = items.FirstOrDefault(i => 
                    i.EndsWith($"|{item}") || i == item);
                
                if (itemToRemove != null)
                {
                    items.Remove(itemToRemove);
                    
                    File.WriteAllLines(filePath, items);
                    
                    LoadItemsForCategory(_currentCategory, true, true);
                    
                    ShowNotification($"Удалено: {item}");
                }
            }
            catch (Exception ex)
            {
                ShowNotification($"Ошибка удаления: {ex.Message}", true);
            }
        }
        
        private void ToggleFavorite(string item)
        {
            var fullItemName = $"{_currentCategory}:{item}";
            
            if (_favorites.Contains(fullItemName))
            {
                _favorites.Remove(fullItemName);
                ShowNotification($"Удалено из избранного: {item}");
            }
            else
            {
                _favorites.Add(fullItemName);
                ShowNotification($"Добавлено в избранное: {item}");
            }
            
            SaveFavorites();
            LoadItemsForCategory(_currentCategory, true, _isDeleteMode, _isAddMode);
        }
        
        #endregion
        
        #region Сохранение и загрузка настроек
        
        private void SaveSettings()
        {
            try
            {
                var settings = new
                {
                    BorderColor = $"#{_borderColor.R:X2}{_borderColor.G:X2}{_borderColor.B:X2}",
                    DividerColor = $"#{_dividerColor.R:X2}{_dividerColor.G:X2}{_dividerColor.B:X2}",
                    ButtonHoverColor = $"#{_buttonHoverColor.R:X2}{_buttonHoverColor.G:X2}{_buttonHoverColor.B:X2}",
                    ConfirmDelete = _confirmDelete,
                    AutoRemoveOld = _autoRemoveOld,
                    BackgroundBrightness = _backgroundBrightness,
                    TextBrightness = _textBrightness,
                    WindowWidth = this.Width,
                    WindowHeight = this.Height
                };
                
                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                
                File.WriteAllText(Path.Combine(_appFolderPath, SETTINGS_FILE), json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка сохранения настроек: {ex.Message}");
            }
        }
        
        private void LoadSettings()
        {
            var settingsPath = Path.Combine(_appFolderPath, SETTINGS_FILE);
            
            if (!File.Exists(settingsPath))
            {
                // Устанавливаем значения по умолчанию
                _borderColor = (Color)ColorConverter.ConvertFromString(DEFAULT_COLOR_HEX);
                _dividerColor = (Color)ColorConverter.ConvertFromString(DEFAULT_COLOR_HEX);
                _buttonHoverColor = (Color)ColorConverter.ConvertFromString(DEFAULT_COLOR_HEX);
                return;
            }
            
            try
            {
                var json = File.ReadAllText(settingsPath);
                var settings = JsonSerializer.Deserialize<Settings>(json);
                
                if (settings != null)
                {
                    _borderColor = ParseColor(settings.BorderColor ?? DEFAULT_COLOR_HEX);
                    _dividerColor = ParseColor(settings.DividerColor ?? DEFAULT_COLOR_HEX);
                    _buttonHoverColor = ParseColor(settings.ButtonHoverColor ?? DEFAULT_COLOR_HEX);
                    
                    _confirmDelete = settings.ConfirmDelete ?? true;
                    _autoRemoveOld = settings.AutoRemoveOld ?? false;
                    _backgroundBrightness = settings.BackgroundBrightness ?? 0.5;
                    _textBrightness = settings.TextBrightness ?? 0.8;
                    
                    // Применяем размер окна
                    if (settings.WindowWidth.HasValue && settings.WindowWidth.Value > 0)
                        this.Width = settings.WindowWidth.Value;
                    if (settings.WindowHeight.HasValue && settings.WindowHeight.Value > 0)
                        this.Height = settings.WindowHeight.Value;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка загрузки настроек: {ex.Message}");
                
                // Устанавливаем значения по умолчанию при ошибке
                _borderColor = (Color)ColorConverter.ConvertFromString(DEFAULT_COLOR_HEX);
                _dividerColor = (Color)ColorConverter.ConvertFromString(DEFAULT_COLOR_HEX);
                _buttonHoverColor = (Color)ColorConverter.ConvertFromString(DEFAULT_COLOR_HEX);
            }
        }
        
        private Color ParseColor(string colorString)
        {
            try
            {
                return (Color)ColorConverter.ConvertFromString(colorString);
            }
            catch
            {
                return (Color)ColorConverter.ConvertFromString(DEFAULT_COLOR_HEX);
            }
        }
        
        private class Settings
        {
            public string? BorderColor { get; set; }
            public string? DividerColor { get; set; }
            public string? ButtonHoverColor { get; set; }
            public bool? ConfirmDelete { get; set; }
            public bool? AutoRemoveOld { get; set; }
            public double? BackgroundBrightness { get; set; }
            public double? TextBrightness { get; set; }
            public double? WindowWidth { get; set; }
            public double? WindowHeight { get; set; }
        }
        
        #endregion
        
        #region Отображение элементов
        
        private void DisplayItems()
        {
            ItemsListPanel.Children.Clear();
            
            if (_currentItems.Count == 0)
            {
                var textBlock = new TextBlock
                {
                    Text = _isDeleteMode ? "Нет элементов для удаления" : "Список пуст",
                    Foreground = Brushes.Gray,
                    FontSize = 14,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 20, 0, 0)
                };
                ItemsListPanel.Children.Add(textBlock);
                return;
            }
            
            foreach (var item in _currentItems)
            {
                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                
                // Основная кнопка с текстом
                var textButton = new Button
                {
                    Content = item,
                    Style = (Style)FindResource("ListItemButtonStyle"),
                    Tag = item,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(0, 0, 5, 0)
                };
                
                // Кнопка избранного
                var favoriteButton = new Button
                {
                    Width = 35,
                    Height = 35,
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Tag = item
                };
                
                var fullItemName = $"{_currentCategory}:{item}";
                var isFavorite = _favorites.Contains(fullItemName);
                
                var starText = new TextBlock
                {
                    Text = isFavorite ? "⭐" : "☆",
                    FontSize = 14,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Foreground = isFavorite ? Brushes.Gold : Brushes.LightGray
                };
                
                favoriteButton.Content = starText;
                
                if (_isDeleteMode)
                {
                    textButton.Content = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Children =
                        {
                            new TextBlock 
                            { 
                                Text = "❌", 
                                VerticalAlignment = VerticalAlignment.Center,
                                Margin = new Thickness(0, 0, 10, 0)
                            },
                            new TextBlock 
                            { 
                                Text = item, 
                                VerticalAlignment = VerticalAlignment.Center 
                            }
                        }
                    };
                    
                    textButton.Click += (s, e) => DeleteItem(item);
                    
                    // В режиме удаления скрываем кнопку избранного
                    favoriteButton.Visibility = Visibility.Collapsed;
                }
                else
                {
                    textButton.Click += (s, e) =>
                    {
                        try
                        {
                            Clipboard.SetText(item);
                            ShowNotification("Скопировано в буфер обмена");
                        }
                        catch (Exception ex)
                        {
                            ShowNotification($"Ошибка копирования: {ex.Message}", true);
                        }
                    };
                    
                    favoriteButton.Click += (s, e) =>
                    {
                        ToggleFavorite(item);
                    };
                }
                
                Grid.SetColumn(textButton, 0);
                Grid.SetColumn(favoriteButton, 1);
                
                grid.Children.Add(textButton);
                grid.Children.Add(favoriteButton);
                
                ItemsListPanel.Children.Add(grid);
            }
        }
        
        private void ShowItemsList()
        {
            ItemsListScrollViewer.Visibility = Visibility.Visible;
            ContentText.Visibility = Visibility.Collapsed;
            AddPanel.Visibility = Visibility.Collapsed;
            SettingsPanel.Visibility = Visibility.Collapsed;
        }
        
        #endregion
        
        #region Управление интерфейсом
        
        private void BtnCategory1_Click(object sender, RoutedEventArgs e)
        {
            HandleCategoryClick("Аниме", SubMenu1);
        }
        
        private void BtnCategory2_Click(object sender, RoutedEventArgs e)
        {
            HandleCategoryClick("Манга", SubMenu2);
        }
        
        private void BtnCategory3_Click(object sender, RoutedEventArgs e)
        {
            HandleCategoryClick("Новелла", SubMenu3);
        }
        
        private void HandleCategoryClick(string category, StackPanel subMenu)
        {
            var now = DateTime.Now;
            
            // Если это двойной клик на ту же кнопку
            if (_lastCategoryClicked == category && _lastCategoryClickTime.HasValue &&
                (now - _lastCategoryClickTime.Value).TotalMilliseconds < 500)
            {
                // Закрываем все подменю
                SubMenu1.Visibility = Visibility.Collapsed;
                SubMenu2.Visibility = Visibility.Collapsed;
                SubMenu3.Visibility = Visibility.Collapsed;
                
                // Сбрасываем таймер
                _lastCategoryClickTime = null;
                _lastCategoryClicked = null;
                return;
            }
            
            // Запоминаем время клика
            _lastCategoryClickTime = now;
            _lastCategoryClicked = category;
            
            // Открываем/закрываем подменю
            if (subMenu.Visibility == Visibility.Visible)
            {
                subMenu.Visibility = Visibility.Collapsed;
            }
            else
            {
                SubMenu1.Visibility = Visibility.Collapsed;
                SubMenu2.Visibility = Visibility.Collapsed;
                SubMenu3.Visibility = Visibility.Collapsed;
                subMenu.Visibility = Visibility.Visible;
            }
        }
        
        private void AddCategory1Btn_Click(object sender, RoutedEventArgs e)
        {
            LoadItemsForCategory("Аниме", true, false, true);
        }
        
        private void AddCategory2Btn_Click(object sender, RoutedEventArgs e)
        {
            LoadItemsForCategory("Манга", true, false, true);
        }
        
        private void AddCategory3Btn_Click(object sender, RoutedEventArgs e)
        {
            LoadItemsForCategory("Новелла", true, false, true);
        }
        
        private void DeleteCategory1Btn_Click(object sender, RoutedEventArgs e)
        {
            LoadItemsForCategory("Аниме", true, true);
        }
        
        private void DeleteCategory2Btn_Click(object sender, RoutedEventArgs e)
        {
            LoadItemsForCategory("Манга", true, true);
        }
        
        private void DeleteCategory3Btn_Click(object sender, RoutedEventArgs e)
        {
            LoadItemsForCategory("Новелла", true, true);
        }
        
        private void ViewCategory1Btn_Click(object sender, RoutedEventArgs e)
        {
            LoadItemsForCategory("Аниме", true);
        }
        
        private void ViewCategory2Btn_Click(object sender, RoutedEventArgs e)
        {
            LoadItemsForCategory("Манга", true);
        }
        
        private void ViewCategory3Btn_Click(object sender, RoutedEventArgs e)
        {
            LoadItemsForCategory("Новелла", true);
        }
        
        private void ShowAddPanel()
        {
            if (string.IsNullOrEmpty(_currentCategory))
            {
                ShowNotification("Сначала выберите категорию", true);
                return;
            }
            
            AddPanelTitle.Text = $"Добавить в {_currentCategory}";
            AddItemTextBox.Text = "";
            AddPanel.Visibility = Visibility.Visible;
            AddItemTextBox.Focus();
        }
        
        private void HideAddPanel()
        {
            AddPanel.Visibility = Visibility.Collapsed;
        }
        
        private void AddMainButton_Click(object sender, RoutedEventArgs e)
        {
            ShowAddPanel();
        }
        
        private void CloseAddPanelButton_Click(object sender, RoutedEventArgs e)
        {
            HideAddPanel();
        }
        
        private void CancelAddButton_Click(object sender, RoutedEventArgs e)
        {
            HideAddPanel();
        }
        
        private void ConfirmAddButton_Click(object sender, RoutedEventArgs e)
        {
            AddItemToCurrentCategory(AddItemTextBox.Text.Trim());
        }
        
        private void ShowSettings()
        {
            SettingsPanel.Visibility = Visibility.Visible;
            ContentText.Visibility = Visibility.Collapsed;
            ItemsListScrollViewer.Visibility = Visibility.Collapsed;
            SearchPanel.Visibility = Visibility.Collapsed;
            SortAZButton.Visibility = Visibility.Collapsed;
            SortZAButton.Visibility = Visibility.Collapsed;
            SortDateButton.Visibility = Visibility.Collapsed;
            ContentTitle.Text = "Настройки";
            
            HideAllSettingsPanels();
        }
        
        private void HideSettings()
        {
            SettingsPanel.Visibility = Visibility.Collapsed;
        }
        
        private void CloseSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            HideSettings();
        }
        
        #endregion
        
        #region Настройки внешнего вида
        
        private void UpdateSettingsValues()
        {
            // Устанавливаем начальные значения для полей обводки
            BorderHexTextBox.Text = $"#{_borderColor.R:X2}{_borderColor.G:X2}{_borderColor.B:X2}";
            DividerHexTextBox.Text = $"#{_dividerColor.R:X2}{_dividerColor.G:X2}{_dividerColor.B:X2}";
            ButtonHoverHexTextBox.Text = $"#{_buttonHoverColor.R:X2}{_buttonHoverColor.G:X2}{_buttonHoverColor.B:X2}";
            
            // Устанавливаем начальные значения для слайдеров
            WindowWidthSlider.Value = this.Width;
            WindowHeightSlider.Value = this.Height;
            WindowWidthValue.Text = $"{this.Width}px";
            WindowHeightValue.Text = $"{this.Height}px";
            
            BackgroundBrightnessSlider.Value = _backgroundBrightness * 100;
            TextBrightnessSlider.Value = _textBrightness * 100;
            BackgroundBrightnessValue.Text = $"{_backgroundBrightness * 100:0}%";
            TextBrightnessValue.Text = $"{_textBrightness * 100:0}%";
            
            // Настройки подтверждения удаления
            ConfirmDeleteCheckBox.IsChecked = _confirmDelete;
            AutoRemoveOldCheckBox.IsChecked = _autoRemoveOld;
        }
        
        private void ApplyAllSettings()
        {
            try
            {
                // Применяем размер окна
                this.Width = WindowWidthSlider.Value;
                this.Height = WindowHeightSlider.Value;
                
                // Применяем яркость
                _backgroundBrightness = BackgroundBrightnessSlider.Value / 100.0;
                _textBrightness = TextBrightnessSlider.Value / 100.0;
                
                // Применяем цвета
                ApplyBorderSettings();
                ApplyDividerSettings();
                ApplyButtonHoverSettings();
                
                // Обновляем настройки
                if (ConfirmDeleteCheckBox.IsChecked.HasValue)
                {
                    _confirmDelete = ConfirmDeleteCheckBox.IsChecked.Value;
                }
                
                if (AutoRemoveOldCheckBox.IsChecked.HasValue)
                {
                    _autoRemoveOld = AutoRemoveOldCheckBox.IsChecked.Value;
                }
                
                // Сохраняем настройки
                SaveSettings();
                
                // Применяем яркость интерфейса
                ApplyBrightness();
                
                // Не показываем уведомление при запуске
                if (!_firstLoad)
                {
                    ShowNotification("Настройки применены");
                }
                _firstLoad = false;
            }
            catch (Exception ex)
            {
                ShowNotification($"Ошибка применения настроек: {ex.Message}", true);
            }
        }
        
        private bool _firstLoad = true;
        
        private void ApplyBorderSettings()
        {
            MainBorder.BorderBrush = new SolidColorBrush(_borderColor);
        }
        
        private void ApplyDividerSettings()
        {
            DividerBorder.Background = new SolidColorBrush(_dividerColor);
        }
        
        private void ApplyButtonHoverSettings()
        {
            Application.Current.Resources["ButtonHoverColor"] = new SolidColorBrush(_buttonHoverColor);
            
            byte hoverAlpha = 0x10; // 16 в hex - прозрачность
            byte pressedAlpha = 0x20; // 32 в hex - прозрачность
            
            Color hoverBackgroundColor = Color.FromArgb(hoverAlpha, 
                _buttonHoverColor.R, _buttonHoverColor.G, _buttonHoverColor.B);
            Color pressedColor = Color.FromArgb(pressedAlpha, 
                _buttonHoverColor.R, _buttonHoverColor.G, _buttonHoverColor.B);
            
            Application.Current.Resources["ButtonHoverBackground"] = new SolidColorBrush(hoverBackgroundColor);
            Application.Current.Resources["ButtonPressedColor"] = new SolidColorBrush(pressedColor);
        }
        
        private void ApplyBrightness()
        {
            // Применяем яркость фона
            var baseColor = (Color)ColorConverter.ConvertFromString("#1E1E1E");
            var brightnessFactor = _backgroundBrightness;
            
            var r = (byte)(baseColor.R * brightnessFactor);
            var g = (byte)(baseColor.G * brightnessFactor);
            var b = (byte)(baseColor.B * brightnessFactor);
            
            var backgroundColor = Color.FromRgb(r, g, b);
            
            // Обновляем основные фоны
            MainBorder.Background = new SolidColorBrush(backgroundColor);
            
            // Применяем яркость текста
            var textBrightness = _textBrightness;
            var textColor = Color.FromRgb(
                (byte)(255 * textBrightness),
                (byte)(255 * textBrightness),
                (byte)(255 * textBrightness));
            
            // Обновляем цвет текста в основных элементах
            ContentText.Foreground = new SolidColorBrush(textColor);
            ContentTitle.Foreground = new SolidColorBrush(Color.FromRgb(
                (byte)(128 * textBrightness),
                (byte)(128 * textBrightness),
                (byte)(128 * textBrightness)));
        }
        
        #endregion
        
        #region Обработчики настроек
        
        private void WindowWidthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            WindowWidthValue.Text = $"{e.NewValue:0}px";
        }
        
        private void WindowHeightSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            WindowHeightValue.Text = $"{e.NewValue:0}px";
        }
        
        private void BackgroundBrightnessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            BackgroundBrightnessValue.Text = $"{e.NewValue:0}%";
        }
        
        private void TextBrightnessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            TextBrightnessValue.Text = $"{e.NewValue:0}%";
        }
        
        private void WindowSizeButton_Click(object sender, RoutedEventArgs e)
        {
            ShowSettingsPanel(WindowSizePanel);
        }
        
        private void BrightnessButton_Click(object sender, RoutedEventArgs e)
        {
            ShowSettingsPanel(BrightnessPanel);
        }
        
        private void BorderSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            ShowSettingsPanel(BorderSettingsPanel);
        }
        
        private void DividerSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            ShowSettingsPanel(DividerSettingsPanel);
        }
        
        private void ButtonHoverSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            ShowSettingsPanel(ButtonHoverSettingsPanel);
        }
        
        private void ShowSettingsPanel(Border panelToShow)
        {
            HideAllSettingsPanels();
            panelToShow.Visibility = Visibility.Visible;
        }
        
        private void HideAllSettingsPanels()
        {
            WindowSizePanel.Visibility = Visibility.Collapsed;
            BrightnessPanel.Visibility = Visibility.Collapsed;
            BorderSettingsPanel.Visibility = Visibility.Collapsed;
            DividerSettingsPanel.Visibility = Visibility.Collapsed;
            ButtonHoverSettingsPanel.Visibility = Visibility.Collapsed;
        }
        
        private bool ValidateColorInput(string hex, out Color color)
        {
            try
            {
                hex = hex.Trim();
                if (string.IsNullOrEmpty(hex))
                {
                    color = Colors.Transparent;
                    return false;
                }
                
                if (!hex.StartsWith("#"))
                    hex = "#" + hex;
                    
                if (!Regex.IsMatch(hex, 
                    @"^#([A-Fa-f0-9]{6}|[A-Fa-f0-9]{3}|[A-Fa-f0-9]{8})$"))
                {
                    color = Colors.Transparent;
                    return false;
                }
                
                color = (Color)ColorConverter.ConvertFromString(hex);
                return true;
            }
            catch
            {
                color = Colors.Transparent;
                return false;
            }
        }
        
        private void ApplyBorderColorButton_Click(object sender, RoutedEventArgs e)
        {
            if (ValidateColorInput(BorderHexTextBox.Text, out Color newColor))
            {
                _borderColor = newColor;
                ApplyBorderSettings();
                ShowNotification("Цвет обводки применен");
            }
            else
            {
                ShowNotification("Неверный формат цвета", true);
            }
        }
        
        private void ApplyDividerColorButton_Click(object sender, RoutedEventArgs e)
        {
            if (ValidateColorInput(DividerHexTextBox.Text, out Color newColor))
            {
                _dividerColor = newColor;
                ApplyDividerSettings();
                ShowNotification("Цвет центральной линии применен");
            }
            else
            {
                ShowNotification("Неверный формат цвета", true);
            }
        }
        
        private void ApplyButtonHoverColorButton_Click(object sender, RoutedEventArgs e)
        {
            if (ValidateColorInput(ButtonHoverHexTextBox.Text, out Color newColor))
            {
                _buttonHoverColor = newColor;
                ApplyButtonHoverSettings();
                ShowNotification("Цвет кнопок применен");
            }
            else
            {
                ShowNotification("Неверный формат цвета", true);
            }
        }
        
        private void ApplyAllSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            ApplyAllSettings();
        }
        
        #endregion
        
        #region Поиск
        
        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _searchTimer?.Stop();
            _searchTimer?.Start();
        }
        
        private void PerformSearch()
        {
            var searchText = SearchTextBox.Text.ToLower().Trim();
            
            if (string.IsNullOrWhiteSpace(searchText))
            {
                DisplayItems();
                return;
            }
            
            if (_currentItems.Count == 0)
                return;
            
            var filteredItems = _currentItems
                .Where(item => item.ToLower().Contains(searchText))
                .ToList();
            
            ItemsListPanel.Children.Clear();
            
            if (filteredItems.Count == 0)
            {
                var textBlock = new TextBlock
                {
                    Text = "Ничего не найдено",
                    Foreground = Brushes.Gray,
                    FontSize = 14,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 20, 0, 0)
                };
                ItemsListPanel.Children.Add(textBlock);
                return;
            }
            
            foreach (var item in filteredItems)
            {
                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                
                // Основная кнопка с текстом
                var textButton = new Button
                {
                    Content = item,
                    Style = (Style)FindResource("ListItemButtonStyle"),
                    Tag = item,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(0, 0, 5, 0)
                };
                
                // Кнопка избранного
                var favoriteButton = new Button
                {
                    Width = 35,
                    Height = 35,
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Tag = item
                };
                
                var fullItemName = $"{_currentCategory}:{item}";
                var isFavorite = _favorites.Contains(fullItemName);
                
                var starText = new TextBlock
                {
                    Text = isFavorite ? "⭐" : "☆",
                    FontSize = 14,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Foreground = isFavorite ? Brushes.Gold : Brushes.LightGray
                };
                
                favoriteButton.Content = starText;
                
                if (_isDeleteMode)
                {
                    textButton.Content = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Children =
                        {
                            new TextBlock 
                            { 
                                Text = "❌", 
                                VerticalAlignment = VerticalAlignment.Center,
                                Margin = new Thickness(0, 0, 10, 0)
                            },
                            new TextBlock 
                            { 
                                Text = item, 
                                VerticalAlignment = VerticalAlignment.Center 
                            }
                        }
                    };
                    
                    textButton.Click += (s, e) => DeleteItem(item);
                    
                    // В режиме удаления скрываем кнопку избранного
                    favoriteButton.Visibility = Visibility.Collapsed;
                }
                else
                {
                    textButton.Click += (s, e) =>
                    {
                        try
                        {
                            Clipboard.SetText(item);
                            ShowNotification("Скопировано в буфер обмена");
                        }
                        catch (Exception ex)
                        {
                            ShowNotification($"Ошибка копирования: {ex.Message}", true);
                        }
                    };
                    
                    favoriteButton.Click += (s, e) =>
                    {
                        ToggleFavorite(item);
                    };
                }
                
                Grid.SetColumn(textButton, 0);
                Grid.SetColumn(favoriteButton, 1);
                
                grid.Children.Add(textButton);
                grid.Children.Add(favoriteButton);
                
                ItemsListPanel.Children.Add(grid);
            }
        }
        
        #endregion
        
        #region Сортировка
        
        private void SortAZButton_Click(object sender, RoutedEventArgs e)
        {
            _currentSort = "az";
            LoadItemsForCategory(_currentCategory, true, _isDeleteMode, _isAddMode);
        }
        
        private void SortZAButton_Click(object sender, RoutedEventArgs e)
        {
            _currentSort = "za";
            LoadItemsForCategory(_currentCategory, true, _isDeleteMode, _isAddMode);
        }
        
        private void SortDateButton_Click(object sender, RoutedEventArgs e)
        {
            _currentSort = "date";
            LoadItemsForCategory(_currentCategory, true, _isDeleteMode, _isAddMode);
        }
        
        #endregion
        
        #region Вспомогательные методы
        
        private void ShowNotification(string message, bool isError = false)
        {
            NotificationText.Text = message;
            
            var color = isError 
                ? Color.FromRgb(255, 68, 68) 
                : _borderColor;
                
            NotificationPanel.Background = new SolidColorBrush(color);
            NotificationPanel.Visibility = Visibility.Visible;
            
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            timer.Tick += (s, e) =>
            {
                NotificationPanel.Visibility = Visibility.Collapsed;
                timer.Stop();
            };
            timer.Start();
        }
        
        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.F11:
                    ToggleMaximize();
                    break;
                case Key.Escape when AddPanel.Visibility == Visibility.Visible:
                    HideAddPanel();
                    break;
                case Key.Escape when SettingsPanel.Visibility == Visibility.Visible:
                    HideSettings();
                    break;
                case Key.Enter when AddPanel.Visibility == Visibility.Visible:
                    AddItemToCurrentCategory(AddItemTextBox.Text.Trim());
                    break;
                case Key.Escape:
                    SubMenu1.Visibility = Visibility.Collapsed;
                    SubMenu2.Visibility = Visibility.Collapsed;
                    SubMenu3.Visibility = Visibility.Collapsed;
                    break;
            }
        }
        
        private void SettingsSidebarButton_Click(object sender, RoutedEventArgs e)
        {
            ShowSettings();
        }
        
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }
        
        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }
        
        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleMaximize();
        }
        
        private void ToggleMaximize()
        {
            if (WindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Normal;
                if (MaximizeButton.Content is TextBlock textBlock)
                {
                    textBlock.Text = "□";
                }
                MaximizeButton.ToolTip = "Развернуть";
            }
            else
            {
                WindowState = WindowState.Maximized;
                if (MaximizeButton.Content is TextBlock textBlock)
                {
                    textBlock.Text = "❐";
                }
                MaximizeButton.ToolTip = "Свернуть";
            }
        }
        
        #endregion
        
        #region Новые функции
        
        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var saveDialog = new SaveFileDialog
                {
                    FileName = "Anime_Manga_Novell_Read_Export",
                    DefaultExt = ".txt",
                    Filter = "Text files (*.txt)|*.txt"
                };
                
                if (saveDialog.ShowDialog() == true)
                {
                    var exportText = new StringBuilder();
                    exportText.AppendLine("=== Anime Manga Novell Read Export ===");
                    exportText.AppendLine($"Export Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    exportText.AppendLine($"Favorites: {_favorites.Count}");
                    exportText.AppendLine();
                    
                    foreach (var kvp in _filePaths)
                    {
                        exportText.AppendLine($"--- {kvp.Key} ---");
                        
                        if (File.Exists(kvp.Value))
                        {
                            var items = File.ReadAllLines(kvp.Value)
                                .Where(line => !string.IsNullOrWhiteSpace(line))
                                .Select(line => line.Trim())
                                .Select(line =>
                                {
                                    var parts = line.Split('|');
                                    return parts.Length == 2 ? parts[1] : line;
                                })
                                .ToList();
                            
                            if (items.Count > 0)
                            {
                                foreach (var item in items)
                                {
                                    var isFavorite = _favorites.Contains($"{kvp.Key}:{item}");
                                    exportText.AppendLine($"• {(isFavorite ? "⭐ " : "")}{item}");
                                }
                            }
                            else
                            {
                                exportText.AppendLine("(пусто)");
                            }
                        }
                        else
                        {
                            exportText.AppendLine("(файл не найден)");
                        }
                        exportText.AppendLine();
                    }
                    
                    File.WriteAllText(saveDialog.FileName, exportText.ToString());
                    
                    ShowNotification($"Данные экспортированы в {saveDialog.FileName}");
                }
            }
            catch (Exception ex)
            {
                ShowNotification($"Ошибка экспорта: {ex.Message}", true);
            }
        }
        
        private void ImportButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var openDialog = new OpenFileDialog
                {
                    Filter = "Text files (*.txt)|*.txt"
                };
                
                if (openDialog.ShowDialog() == true)
                {
                    var content = File.ReadAllText(openDialog.FileName);
                    var lines = content.Split('\n');
                    string currentCategory = "";
                    var categoryItems = new List<string>();
                    
                    foreach (var line in lines)
                    {
                        var trimmedLine = line.Trim();
                        
                        if (trimmedLine.StartsWith("--- ") && trimmedLine.EndsWith(" ---"))
                        {
                            if (!string.IsNullOrEmpty(currentCategory) && categoryItems.Count > 0)
                            {
                                if (_filePaths.TryGetValue(currentCategory, out string? filePath))
                                {
                                    var existingItems = File.ReadAllLines(filePath)
                                        .Where(l => !string.IsNullOrWhiteSpace(l))
                                        .Select(l => l.Trim())
                                        .ToList();
                                    
                                    // Добавляем новые элементы с временными метками
                                    foreach (var item in categoryItems)
                                    {
                                        var cleanItem = item.StartsWith("⭐ ") ? item.Substring(2) : item;
                                        var itemWithTimestamp = $"{DateTime.Now.Ticks}|{cleanItem}";
                                        
                                        if (!existingItems.Any(existing => 
                                            existing.EndsWith($"|{cleanItem}") || existing == cleanItem))
                                        {
                                            existingItems.Add(itemWithTimestamp);
                                        }
                                    }
                                    
                                    File.WriteAllLines(filePath, existingItems);
                                }
                            }
                            
                            currentCategory = trimmedLine.Substring(4, trimmedLine.Length - 8).Trim();
                            categoryItems.Clear();
                        }
                        else if (trimmedLine.StartsWith("• "))
                        {
                            categoryItems.Add(trimmedLine.Substring(2).Trim());
                        }
                    }
                    
                    if (!string.IsNullOrEmpty(currentCategory) && categoryItems.Count > 0)
                    {
                        if (_filePaths.TryGetValue(currentCategory, out string? filePath))
                        {
                            var existingItems = File.ReadAllLines(filePath)
                                .Where(l => !string.IsNullOrWhiteSpace(l))
                                .Select(l => l.Trim())
                                .ToList();
                            
                            foreach (var item in categoryItems)
                            {
                                var cleanItem = item.StartsWith("⭐ ") ? item.Substring(2) : item;
                                var itemWithTimestamp = $"{DateTime.Now.Ticks}|{cleanItem}";
                                
                                if (!existingItems.Any(existing => 
                                    existing.EndsWith($"|{cleanItem}") || existing == cleanItem))
                                {
                                    existingItems.Add(itemWithTimestamp);
                                }
                            }
                            
                            File.WriteAllLines(filePath, existingItems);
                        }
                    }
                    
                    ShowNotification($"Данные импортированы из {openDialog.FileName}");
                    
                    if (!string.IsNullOrEmpty(_currentCategory))
                    {
                        LoadItemsForCategory(_currentCategory, true, _isDeleteMode, _isAddMode);
                    }
                }
            }
            catch (Exception ex)
            {
                ShowNotification($"Ошибка импорта: {ex.Message}", true);
            }
        }
        
        private void StatsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var stats = new StringBuilder();
                int totalItems = 0;
                
                stats.AppendLine("📊 Статистика Anime Manga Novell Read");
                stats.AppendLine("=====================================");
                stats.AppendLine();
                
                foreach (var kvp in _filePaths)
                {
                    if (File.Exists(kvp.Value))
                    {
                        var items = File.ReadAllLines(kvp.Value)
                            .Count(line => !string.IsNullOrWhiteSpace(line));
                        
                        var favoritesInCategory = _favorites
                            .Count(f => f.StartsWith($"{kvp.Key}:"));
                        
                        stats.AppendLine($"• {kvp.Key}: {items} элементов ({favoritesInCategory} в избранном)");
                        totalItems += items;
                    }
                    else
                    {
                        stats.AppendLine($"• {kvp.Key}: файл не найден");
                    }
                }
                
                stats.AppendLine();
                stats.AppendLine($"📈 Всего: {totalItems} элементов");
                stats.AppendLine($"⭐ Избранное: {_favorites.Count} элементов");
                stats.AppendLine();
                stats.AppendLine($"🗓️ Дата: {DateTime.Now:dd.MM.yyyy HH:mm}");
                
                MessageBox.Show(stats.ToString(), "Статистика", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                ShowNotification($"Ошибка получения статистики: {ex.Message}", true);
            }
        }
        
        private void ClearAllDataButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Вы уверены, что хотите очистить ВСЕ данные?\nЭто действие нельзя отменить!", 
                "Очистка всех данных", 
                MessageBoxButton.YesNo, 
                MessageBoxImage.Warning);
            
            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    foreach (var kvp in _filePaths)
                    {
                        if (File.Exists(kvp.Value))
                        {
                            File.WriteAllText(kvp.Value, "");
                        }
                    }
                    
                    // Очищаем избранное
                    _favorites.Clear();
                    SaveFavorites();
                    
                    ShowNotification("Все данные очищены");
                    
                    if (!string.IsNullOrEmpty(_currentCategory))
                    {
                        LoadItemsForCategory(_currentCategory, true, _isDeleteMode, _isAddMode);
                    }
                }
                catch (Exception ex)
                {
                    ShowNotification($"Ошибка очистки данных: {ex.Message}", true);
                }
            }
        }
        
        #endregion
    }
}