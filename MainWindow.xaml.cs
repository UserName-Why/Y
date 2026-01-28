using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Anime_Manga_Novell_Read
{
    public partial class MainWindow
    {
        private const string AppFolderName = "Anime_Manga_Novell_Read";
        private const string AnimeFile = "Anime.txt";
        private const string MangaFile = "Manga.txt";
        private const string NovelFile = "Novel.txt";
        private const string FavoritesFile = "Favorites.txt";
        private const string SettingsFile = "Settings.json";
        private const string DefaultColorHex = "#8A2BE2";
        
        private readonly string _appFolderPath;
        private readonly Dictionary<string, string> _filePaths;
        
        private Color _borderColor;
        private Color _dividerColor;
        private Color _buttonHoverColor;
        
        private bool _confirmDelete = true;
        private bool _autoRemoveOld;
        private double _backgroundBrightness = 0.5;
        private double _textBrightness = 0.8;
        
        private string _currentCategory = string.Empty;
        private readonly List<string> _currentItems = [];
        private readonly HashSet<string> _favorites = [];
        private bool _isDeleteMode;
        private bool _isAddMode;
        
        private DispatcherTimer? _searchTimer;
        private DateTime? _lastCategoryClickTime;
        private string? _lastCategoryClicked;
        private string _currentSort = "date";
        private bool _firstLoad = true;

        public MainWindow()
        {
            InitializeComponent();
            
            _appFolderPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
                AppFolderName);
            
            _filePaths = new Dictionary<string, string>
            {
                { "Аниме", Path.Combine(_appFolderPath, AnimeFile) },
                { "Манга", Path.Combine(_appFolderPath, MangaFile) },
                { "Новелла", Path.Combine(_appFolderPath, NovelFile) }
            };
            
            InitializeApplication();
        }
        
        private void InitializeApplication()
        {
            if (!Directory.Exists(_appFolderPath))
                Directory.CreateDirectory(_appFolderPath);
            
            LoadSettings();
            LoadFavorites();
            InitializeFiles();
            InitializeEventHandlers();
            ApplyAllSettings();
            
            ContentTitle.Text = "Anime Manga Novell Read";
            ContentText.Text = "Выберите категорию слева";
            
            SearchPanel.Visibility = Visibility.Collapsed;
            AddMainButton.Visibility = Visibility.Collapsed;
            UpdateSettingsValues();
            
            Width = 1000;
            Height = 600;
        }
        
        private void InitializeEventHandlers()
        {
            WindowWidthSlider.ValueChanged += WindowWidthSlider_ValueChanged;
            WindowHeightSlider.ValueChanged += WindowHeightSlider_ValueChanged;
            BackgroundBrightnessSlider.ValueChanged += BackgroundBrightnessSlider_ValueChanged;
            TextBrightnessSlider.ValueChanged += TextBrightnessSlider_ValueChanged;
            SearchTextBox.TextChanged += SearchTextBox_TextChanged;
            
            InitializeSearchTimer();
        }
        
        private void InitializeSearchTimer()
        {
            _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _searchTimer.Tick += (_, _) =>
            {
                _searchTimer?.Stop();
                PerformSearch();
            };
        }
        
        private void LoadFavorites()
        {
            var favoritesPath = Path.Combine(_appFolderPath, FavoritesFile);
            if (!File.Exists(favoritesPath)) return;
            
            _favorites.UnionWith(File.ReadAllLines(favoritesPath)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => line.Trim()));
        }
        
        private void SaveFavorites()
        {
            var favoritesPath = Path.Combine(_appFolderPath, FavoritesFile);
            File.WriteAllLines(favoritesPath, _favorites);
        }
        
        private void InitializeFiles()
        {
            try
            {
                EnsureFileExists(AnimeFile, []);
                EnsureFileExists(MangaFile, []);
                EnsureFileExists(NovelFile, []);
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
                File.WriteAllLines(filePath, defaultContent);
        }
        
        private void LoadItemsForCategory(string category, bool showList = false, bool isDeleteMode = false, bool isAddMode = false)
        {
            try
            {
                _isDeleteMode = isDeleteMode;
                _isAddMode = isAddMode;
                
                if (!_filePaths.TryGetValue(category, out var filePath))
                {
                    ShowNotification($"Категория '{category}' не найдена", true);
                    return;
                }
                    
                _currentCategory = category;
                _currentItems.Clear();
                ItemsListPanel.Children.Clear();
                
                ContentTitle.Text = category;
                
                var visibility = showList ? Visibility.Visible : Visibility.Collapsed;
                SortAzButton.Visibility = visibility;
                SortZaButton.Visibility = visibility;
                SortDateButton.Visibility = visibility;
                
                if (File.Exists(filePath))
                {
                    var itemsWithTimestamp = File.ReadAllLines(filePath)
                        .Where(line => !string.IsNullOrWhiteSpace(line))
                        .Select(line => line.Trim())
                        .Select(line =>
                        {
                            var parts = line.Split('|');
                            return parts.Length == 2 && long.TryParse(parts[0], out var timestamp)
                                ? new { Timestamp = timestamp, Name = parts[1] }
                                : new { Timestamp = DateTime.Now.Ticks, Name = line };
                        })
                        .ToList();
                    
                    itemsWithTimestamp = _currentSort switch
                    {
                        "az" => [.. itemsWithTimestamp.OrderBy(x => x.Name)],
                        "za" => [.. itemsWithTimestamp.OrderByDescending(x => x.Name)],
                        _ => [.. itemsWithTimestamp.OrderByDescending(x => x.Timestamp)]
                    };
                    
                    itemsWithTimestamp = [.. itemsWithTimestamp
                        .OrderByDescending(x => _favorites.Contains($"{category}:{x.Name}"))
                        .ThenBy(x => _currentSort == "az" ? x.Name : "")
                        .ThenByDescending(x => _currentSort == "za" ? x.Name : "")
                        .ThenByDescending(x => _currentSort == "date" ? x.Timestamp : 0)];
                    
                    _currentItems.AddRange(itemsWithTimestamp.Select(x => x.Name));
                    
                    DisplayItems();
                    
                    if (showList || isDeleteMode || isAddMode)
                    {
                        ShowItemsList();
                        SearchPanel.Visibility = Visibility.Visible;
                        AddMainButton.Visibility = isAddMode ? Visibility.Visible : Visibility.Collapsed;
                    }
                }
                else
                {
                    if (showList || isDeleteMode || isAddMode)
                    {
                        ShowItemsList();
                        SearchPanel.Visibility = Visibility.Visible;
                        AddMainButton.Visibility = isAddMode ? Visibility.Visible : Visibility.Collapsed;
                        DisplayItems();
                    }
                }
            }
            catch (Exception ex)
            {
                ShowNotification($"Ошибка загрузки файла: {ex.Message}", true);
            }
        }
        
        private void AddItemToCurrentCategory(string? item)
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
                !_filePaths.TryGetValue(_currentCategory, out var filePath))
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
                
                var exactMatch = existingItems
                    .FirstOrDefault(existing => string.Equals(
                        existing.Contains('|') ? existing.Split('|')[1] : existing, 
                        item, StringComparison.OrdinalIgnoreCase));
                
                if (exactMatch != null)
                {
                    ShowNotification("Такой элемент уже существует", true);
                    return;
                }
                
                if (_autoRemoveOld)
                    RemoveOldSimilarItems(item, existingItems, filePath);
                
                File.AppendAllText(filePath, $"\n{DateTime.Now.Ticks}|{item}");
                
                LoadItemsForCategory(_currentCategory, true, false, true);
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
            var baseName = ExtractBaseName(newItem);
            if (string.IsNullOrEmpty(baseName)) return;
            
            var similarItems = existingItems
                .Select(line =>
                {
                    var parts = line.Split('|');
                    return new { Line = line, Name = parts.Length == 2 ? parts[1] : line, Timestamp = parts.Length == 2 ? parts[0] : "" };
                })
                .Where(x => IsSimilarName(x.Name, baseName))
                .ToList();
            
            if (similarItems.Count < 2) return;
            
            var itemsToRemove = similarItems.OrderByDescending(x => x.Timestamp).Skip(1).ToList();
            
            foreach (var itemToRemove in itemsToRemove)
            {
                existingItems.Remove(itemToRemove.Line);
                ShowNotification($"Автоматически удалено: {itemToRemove.Name}");
            }
            
            File.WriteAllLines(filePath, existingItems);
        }
        
        private static string ExtractBaseName(string itemName)
        {
            var cleaned = Regex.Replace(itemName, 
                @"\s*(серия|сезон|эпизод|глава|том|part|chapter|episode|season|vol\.?)\s*\d+", "", 
                RegexOptions.IgnoreCase);
            
            cleaned = Regex.Replace(cleaned, @"\s+\d+$", "");
            cleaned = Regex.Replace(cleaned, @"[^\w\sа-яА-ЯёЁ]", " ");
            
            return cleaned.Trim();
        }
        
        private static bool IsSimilarName(string item1, string item2)
        {
            var name1 = item1.ToLower();
            var name2 = item2.ToLower();
            
            var base1 = ExtractBaseName(name1);
            var base2 = ExtractBaseName(name2);
            
            if (string.Equals(base1, base2, StringComparison.OrdinalIgnoreCase))
                return true;
            
            if (base1.Contains(base2) || base2.Contains(base1))
                return true;
            
            var distance = LevenshteinDistance(base1, base2);
            var maxLength = Math.Max(base1.Length, base2.Length);
            
            return maxLength > 0 && (double)distance / maxLength < 0.3;
        }
        
        private static int LevenshteinDistance(string s, string t)
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
                    var cost = t[j - 1] == s[i - 1] ? 0 : 1;
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
                !_filePaths.TryGetValue(_currentCategory, out var filePath))
            {
                ShowNotification("Категория не выбрана", true);
                return;
            }
            
            var fullItemName = $"{_currentCategory}:{item}";
            
            if (_favorites.Contains(fullItemName))
            {
                if (MessageBox.Show(
                    $"Элемент \"{item}\" находится в избранном.\nУдалить его из избранного и продолжить удаление?", 
                    "Элемент в избранном", 
                    MessageBoxButton.YesNo, 
                    MessageBoxImage.Question) != MessageBoxResult.Yes)
                    return;
                
                _favorites.Remove(fullItemName);
                SaveFavorites();
            }
            
            if (_confirmDelete && MessageBox.Show(
                $"Вы уверены, что хотите удалить \"{item}\"?", 
                "Подтверждение удаления", 
                MessageBoxButton.YesNo, 
                MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
            
            try
            {
                var items = File.ReadAllLines(filePath)
                    .Where(line => !string.IsNullOrWhiteSpace(line))
                    .Select(line => line.Trim())
                    .ToList();
                
                var itemToRemove = items.FirstOrDefault(i => 
                    i.EndsWith($"|{item}") || i == item);
                
                if (itemToRemove == null) return;
                
                items.Remove(itemToRemove);
                File.WriteAllLines(filePath, items);
                LoadItemsForCategory(_currentCategory, true, true);
                ShowNotification($"Удалено: {item}");
            }
            catch (Exception ex)
            {
                ShowNotification($"Ошибка удаления: {ex.Message}", true);
            }
        }
        
        private void ToggleFavorite(string item)
        {
            var fullItemName = $"{_currentCategory}:{item}";
            
            if (_favorites.Remove(fullItemName))
                ShowNotification($"Удалено из избранного: {item}");
            else
            {
                _favorites.Add(fullItemName);
                ShowNotification($"Добавлено в избранное: {item}");
            }
            
            SaveFavorites();
            LoadItemsForCategory(_currentCategory, true, _isDeleteMode, _isAddMode);
        }
        
        private void SaveSettings()
        {
            try
            {
                var settings = new Settings
                {
                    BorderColor = $"#{_borderColor.R:X2}{_borderColor.G:X2}{_borderColor.B:X2}",
                    DividerColor = $"#{_dividerColor.R:X2}{_dividerColor.G:X2}{_dividerColor.B:X2}",
                    ButtonHoverColor = $"#{_buttonHoverColor.R:X2}{_buttonHoverColor.G:X2}{_buttonHoverColor.B:X2}",
                    ConfirmDelete = _confirmDelete,
                    AutoRemoveOld = _autoRemoveOld,
                    BackgroundBrightness = _backgroundBrightness,
                    TextBrightness = _textBrightness,
                    WindowWidth = Width,
                    WindowHeight = Height
                };
                
                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                
                File.WriteAllText(Path.Combine(_appFolderPath, SettingsFile), json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка сохранения настроек: {ex.Message}");
            }
        }
        
        private void LoadSettings()
        {
            var settingsPath = Path.Combine(_appFolderPath, SettingsFile);
            
            if (!File.Exists(settingsPath))
            {
                SetDefaultColors();
                return;
            }
            
            try
            {
                var json = File.ReadAllText(settingsPath);
                var settings = JsonSerializer.Deserialize<Settings>(json);
                
                if (settings == null)
                {
                    SetDefaultColors();
                    return;
                }
                
                _borderColor = ParseColor(settings.BorderColor ?? DefaultColorHex);
                _dividerColor = ParseColor(settings.DividerColor ?? DefaultColorHex);
                _buttonHoverColor = ParseColor(settings.ButtonHoverColor ?? DefaultColorHex);
                
                _confirmDelete = settings.ConfirmDelete ?? true;
                _autoRemoveOld = settings.AutoRemoveOld ?? false;
                _backgroundBrightness = settings.BackgroundBrightness ?? 0.5;
                _textBrightness = settings.TextBrightness ?? 0.8;
                
                if (settings.WindowWidth > 0)
                    Width = settings.WindowWidth.Value;
                if (settings.WindowHeight > 0)
                    Height = settings.WindowHeight.Value;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка загрузки настроек: {ex.Message}");
                SetDefaultColors();
            }
        }
        
        private void SetDefaultColors()
        {
            var color = (Color)ColorConverter.ConvertFromString(DefaultColorHex);
            _borderColor = _dividerColor = _buttonHoverColor = color;
        }
        
        private static Color ParseColor(string colorString)
        {
            try
            {
                return (Color)ColorConverter.ConvertFromString(colorString);
            }
            catch
            {
                return (Color)ColorConverter.ConvertFromString(DefaultColorHex);
            }
        }
        
        private class Settings
        {
            public string? BorderColor { get; init; }
            public string? DividerColor { get; init; }
            public string? ButtonHoverColor { get; init; }
            public bool? ConfirmDelete { get; init; }
            public bool? AutoRemoveOld { get; init; }
            public double? BackgroundBrightness { get; init; }
            public double? TextBrightness { get; init; }
            public double? WindowWidth { get; init; }
            public double? WindowHeight { get; init; }
        }
        
        private void DisplayItems()
        {
            ItemsListPanel.Children.Clear();
            
            if (_currentItems.Count == 0)
            {
                AddEmptyListText(_isDeleteMode ? "Нет элементов для удаления" : "Список пуст");
                return;
            }
            
            foreach (var item in _currentItems)
                ItemsListPanel.Children.Add(CreateItemGrid(item));
        }
        
        private Grid CreateItemGrid(string item)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            
            var textButton = CreateTextButton(item);
            var favoriteButton = CreateFavoriteButton(item);
            
            Grid.SetColumn(textButton, 0);
            Grid.SetColumn(favoriteButton, 1);
            
            grid.Children.Add(textButton);
            grid.Children.Add(favoriteButton);
            
            return grid;
        }
        
        private Button CreateTextButton(string item)
        {
            var button = new Button
            {
                Content = item,
                Style = (Style)FindResource("ListItemButtonStyle"),
                Tag = item,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 5, 0)
            };
            
            if (_isDeleteMode)
            {
                button.Content = new StackPanel
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
                button.Click += (_, _) => DeleteItem(item);
            }
            else
            {
                button.Click += (_, _) => CopyToClipboard(item);
            }
            
            return button;
        }
        
        private Button CreateFavoriteButton(string item)
        {
            var fullItemName = $"{_currentCategory}:{item}";
            var isFavorite = _favorites.Contains(fullItemName);
            
            var button = new Button
            {
                Width = 35,
                Height = 35,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Right,
                Tag = item,
                Visibility = _isDeleteMode ? Visibility.Collapsed : Visibility.Visible,
                Content = new TextBlock
                {
                    Text = isFavorite ? "⭐" : "☆",
                    FontSize = 14,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Foreground = isFavorite ? Brushes.Gold : Brushes.LightGray
                }
            };
            
            if (!_isDeleteMode)
                button.Click += (_, _) => ToggleFavorite(item);
            
            return button;
        }
        
        private void AddEmptyListText(string text)
        {
            ItemsListPanel.Children.Add(new TextBlock
            {
                Text = text,
                Foreground = Brushes.Gray,
                FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 20, 0, 0)
            });
        }
        
        private void CopyToClipboard(string text)
        {
            try
            {
                Clipboard.SetText(text);
                ShowNotification("Скопировано в буфер обмена");
            }
            catch (Exception ex)
            {
                ShowNotification($"Ошибка копирования: {ex.Message}", true);
            }
        }
        
        private void ShowItemsList()
        {
            ItemsListScrollViewer.Visibility = Visibility.Visible;
            ContentText.Visibility = Visibility.Collapsed;
            AddPanel.Visibility = Visibility.Collapsed;
            SettingsPanel.Visibility = Visibility.Collapsed;
        }
        
        private void BtnCategory1_Click(object sender, RoutedEventArgs e) => HandleCategoryClick("Аниме", SubMenu1);
        private void BtnCategory2_Click(object sender, RoutedEventArgs e) => HandleCategoryClick("Манга", SubMenu2);
        private void BtnCategory3_Click(object sender, RoutedEventArgs e) => HandleCategoryClick("Новелла", SubMenu3);
        
        private void HandleCategoryClick(string category, StackPanel subMenu)
        {
            var now = DateTime.Now;
            
            if (_lastCategoryClicked == category && _lastCategoryClickTime.HasValue &&
                (now - _lastCategoryClickTime.Value).TotalMilliseconds < 500)
            {
                SubMenu1.Visibility = SubMenu2.Visibility = SubMenu3.Visibility = Visibility.Collapsed;
                _lastCategoryClickTime = null;
                _lastCategoryClicked = null;
                return;
            }
            
            _lastCategoryClickTime = now;
            _lastCategoryClicked = category;
            
            subMenu.Visibility = subMenu.Visibility == Visibility.Visible 
                ? Visibility.Collapsed 
                : Visibility.Visible;
            
            if (subMenu.Visibility == Visibility.Visible)
                SubMenu1.Visibility = SubMenu2.Visibility = SubMenu3.Visibility = Visibility.Collapsed;
            
            subMenu.Visibility = Visibility.Visible;
        }
        
        private void AddCategory1Btn_Click(object sender, RoutedEventArgs e) => LoadItemsForCategory("Аниме", true, false, true);
        private void AddCategory2Btn_Click(object sender, RoutedEventArgs e) => LoadItemsForCategory("Манга", true, false, true);
        private void AddCategory3Btn_Click(object sender, RoutedEventArgs e) => LoadItemsForCategory("Новелла", true, false, true);
        
        private void DeleteCategory1Btn_Click(object sender, RoutedEventArgs e) => LoadItemsForCategory("Аниме", true, true);
        private void DeleteCategory2Btn_Click(object sender, RoutedEventArgs e) => LoadItemsForCategory("Манга", true, true);
        private void DeleteCategory3Btn_Click(object sender, RoutedEventArgs e) => LoadItemsForCategory("Новелла", true, true);
        
        private void ViewCategory1Btn_Click(object sender, RoutedEventArgs e) => LoadItemsForCategory("Аниме", true);
        private void ViewCategory2Btn_Click(object sender, RoutedEventArgs e) => LoadItemsForCategory("Манга", true);
        private void ViewCategory3Btn_Click(object sender, RoutedEventArgs e) => LoadItemsForCategory("Новелла", true);
        
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
        
        private void HideAddPanel() => AddPanel.Visibility = Visibility.Collapsed;
        
        private void AddMainButton_Click(object sender, RoutedEventArgs e) => ShowAddPanel();
        private void CloseAddPanelButton_Click(object sender, RoutedEventArgs e) => HideAddPanel();
        private void CancelAddButton_Click(object sender, RoutedEventArgs e) => HideAddPanel();
        private void ConfirmAddButton_Click(object sender, RoutedEventArgs e) => AddItemToCurrentCategory(AddItemTextBox.Text.Trim());
        
        private void ShowSettings()
        {
            SettingsPanel.Visibility = Visibility.Visible;
            ContentText.Visibility = Visibility.Collapsed;
            ItemsListScrollViewer.Visibility = Visibility.Collapsed;
            SearchPanel.Visibility = Visibility.Collapsed;
            SortAzButton.Visibility = SortZaButton.Visibility = SortDateButton.Visibility = Visibility.Collapsed;
            ContentTitle.Text = "Настройки";
            
            HideAllSettingsPanels();
        }
        
        private void HideSettings() => SettingsPanel.Visibility = Visibility.Collapsed;
        private void CloseSettingsButton_Click(object sender, RoutedEventArgs e) => HideSettings();
        
        private void UpdateSettingsValues()
        {
            BorderHexTextBox.Text = $"#{_borderColor.R:X2}{_borderColor.G:X2}{_borderColor.B:X2}";
            DividerHexTextBox.Text = $"#{_dividerColor.R:X2}{_dividerColor.G:X2}{_dividerColor.B:X2}";
            ButtonHoverHexTextBox.Text = $"#{_buttonHoverColor.R:X2}{_buttonHoverColor.G:X2}{_buttonHoverColor.B:X2}";
            
            WindowWidthSlider.Value = Width;
            WindowHeightSlider.Value = Height;
            WindowWidthValue.Text = $"{Width}px";
            WindowHeightValue.Text = $"{Height}px";
            
            BackgroundBrightnessSlider.Value = _backgroundBrightness * 100;
            TextBrightnessSlider.Value = _textBrightness * 100;
            BackgroundBrightnessValue.Text = $"{_backgroundBrightness * 100:0}%";
            TextBrightnessValue.Text = $"{_textBrightness * 100:0}%";
            
            ConfirmDeleteCheckBox.IsChecked = _confirmDelete;
            AutoRemoveOldCheckBox.IsChecked = _autoRemoveOld;
        }
        
        private void ApplyAllSettings()
        {
            try
            {
                Width = WindowWidthSlider.Value;
                Height = WindowHeightSlider.Value;
                
                _backgroundBrightness = BackgroundBrightnessSlider.Value / 100.0;
                _textBrightness = TextBrightnessSlider.Value / 100.0;
                
                ApplyBorderSettings();
                ApplyDividerSettings();
                ApplyButtonHoverSettings();
                
                if (ConfirmDeleteCheckBox.IsChecked.HasValue)
                    _confirmDelete = ConfirmDeleteCheckBox.IsChecked.Value;
                
                if (AutoRemoveOldCheckBox.IsChecked.HasValue)
                    _autoRemoveOld = AutoRemoveOldCheckBox.IsChecked.Value;
                
                SaveSettings();
                ApplyBrightness();
                
                if (!_firstLoad)
                    ShowNotification("Настройки применены");
                    
                _firstLoad = false;
            }
            catch (Exception ex)
            {
                ShowNotification($"Ошибка применения настроек: {ex.Message}", true);
            }
        }
        
        private void ApplyBorderSettings() => MainBorder.BorderBrush = new SolidColorBrush(_borderColor);
        private void ApplyDividerSettings() => DividerBorder.Background = new SolidColorBrush(_dividerColor);
        
        private void ApplyButtonHoverSettings()
        {
            Application.Current.Resources["ButtonHoverColor"] = new SolidColorBrush(_buttonHoverColor);
            
            const byte hoverAlpha = 0x10;
            const byte pressedAlpha = 0x20;
            
            var hoverBackgroundColor = Color.FromArgb(hoverAlpha, _buttonHoverColor.R, _buttonHoverColor.G, _buttonHoverColor.B);
            var pressedColor = Color.FromArgb(pressedAlpha, _buttonHoverColor.R, _buttonHoverColor.G, _buttonHoverColor.B);
            
            Application.Current.Resources["ButtonHoverBackground"] = new SolidColorBrush(hoverBackgroundColor);
            Application.Current.Resources["ButtonPressedColor"] = new SolidColorBrush(pressedColor);
        }
        
        private void ApplyBrightness()
        {
            var baseColor = (Color)ColorConverter.ConvertFromString("#1E1E1E");
            var r = (byte)(baseColor.R * _backgroundBrightness);
            var g = (byte)(baseColor.G * _backgroundBrightness);
            var b = (byte)(baseColor.B * _backgroundBrightness);
            
            MainBorder.Background = new SolidColorBrush(Color.FromRgb(r, g, b));
            
            var textColor = Color.FromRgb(
                (byte)(255 * _textBrightness),
                (byte)(255 * _textBrightness),
                (byte)(255 * _textBrightness));
            
            ContentText.Foreground = new SolidColorBrush(textColor);
            
            var titleColor = Color.FromRgb(
                (byte)(128 * _textBrightness),
                (byte)(128 * _textBrightness),
                (byte)(128 * _textBrightness));
            
            ContentTitle.Foreground = new SolidColorBrush(titleColor);
        }
        
        private void WindowWidthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => WindowWidthValue.Text = $"{e.NewValue:0}px";
        private void WindowHeightSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => WindowHeightValue.Text = $"{e.NewValue:0}px";
        private void BackgroundBrightnessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => BackgroundBrightnessValue.Text = $"{e.NewValue:0}%";
        private void TextBrightnessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => TextBrightnessValue.Text = $"{e.NewValue:0}%";
        
        private void WindowSizeButton_Click(object sender, RoutedEventArgs e) => ShowSettingsPanel(WindowSizePanel);
        private void BrightnessButton_Click(object sender, RoutedEventArgs e) => ShowSettingsPanel(BrightnessPanel);
        private void BorderSettingsButton_Click(object sender, RoutedEventArgs e) => ShowSettingsPanel(BorderSettingsPanel);
        private void DividerSettingsButton_Click(object sender, RoutedEventArgs e) => ShowSettingsPanel(DividerSettingsPanel);
        private void ButtonHoverSettingsButton_Click(object sender, RoutedEventArgs e) => ShowSettingsPanel(ButtonHoverSettingsPanel);
        
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
        
        private static bool ValidateColorInput(string hex, out Color color)
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
                    
                if (!Regex.IsMatch(hex, @"^#([A-Fa-f0-9]{6}|[A-Fa-f0-9]{3}|[A-Fa-f0-9]{8})$"))
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
            if (ValidateColorInput(BorderHexTextBox.Text, out var newColor))
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
            if (ValidateColorInput(DividerHexTextBox.Text, out var newColor))
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
            if (ValidateColorInput(ButtonHoverHexTextBox.Text, out var newColor))
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
        
        private void ApplyAllSettingsButton_Click(object sender, RoutedEventArgs e) => ApplyAllSettings();
        
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
            
            var filteredItems = _currentItems.Where(item => item.ToLower().Contains(searchText)).ToList();
            
            ItemsListPanel.Children.Clear();
            
            if (filteredItems.Count == 0)
            {
                AddEmptyListText("Ничего не найдено");
                return;
            }
            
            foreach (var item in filteredItems)
                ItemsListPanel.Children.Add(CreateItemGrid(item));
        }
        
        private void SortAzButton_Click(object sender, RoutedEventArgs e)
        {
            _currentSort = "az";
            LoadItemsForCategory(_currentCategory, true, _isDeleteMode, _isAddMode);
        }
        
        private void SortZaButton_Click(object sender, RoutedEventArgs e)
        {
            _currentSort = "za";
            LoadItemsForCategory(_currentCategory, true, _isDeleteMode, _isAddMode);
        }
        
        private void SortDateButton_Click(object sender, RoutedEventArgs e)
        {
            _currentSort = "date";
            LoadItemsForCategory(_currentCategory, true, _isDeleteMode, _isAddMode);
        }
        
        private void ShowNotification(string message, bool isError = false)
        {
            NotificationText.Text = message;
            
            var color = isError ? Color.FromRgb(255, 68, 68) : _borderColor;
            NotificationPanel.Background = new SolidColorBrush(color);
            NotificationPanel.Visibility = Visibility.Visible;
            
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            timer.Tick += (_, _) =>
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
                    SubMenu1.Visibility = SubMenu2.Visibility = SubMenu3.Visibility = Visibility.Collapsed;
                    break;
            }
        }
        
        private void SettingsSidebarButton_Click(object sender, RoutedEventArgs e) => ShowSettings();
        private void CloseButton_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();
        private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        
        private void MaximizeButton_Click(object sender, RoutedEventArgs e) => ToggleMaximize();
        
        private void ToggleMaximize()
        {
            if (WindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Normal;
                if (MaximizeButton.Content is TextBlock textBlock)
                    textBlock.Text = "□";
                MaximizeButton.ToolTip = "Развернуть";
            }
            else
            {
                WindowState = WindowState.Maximized;
                if (MaximizeButton.Content is TextBlock textBlock)
                    textBlock.Text = "❐";
                MaximizeButton.ToolTip = "Свернуть";
            }
        }
        
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
                
                if (saveDialog.ShowDialog() != true) return;
                
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
                            .Select(line => line.Split('|')[^1])
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
            catch (Exception ex)
            {
                ShowNotification($"Ошибка экспорта: {ex.Message}", true);
            }
        }
        
        private void ImportButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var openDialog = new OpenFileDialog { Filter = "Text files (*.txt)|*.txt" };
                
                if (openDialog.ShowDialog() != true) return;
                
                var content = File.ReadAllText(openDialog.FileName);
                var lines = content.Split('\n');
                string currentCategory = "";
                var categoryItems = new List<string>();
                
                foreach (var line in lines)
                {
                    var trimmedLine = line.Trim();
                    
                    if (trimmedLine.StartsWith("--- ") && trimmedLine.EndsWith(" ---"))
                    {
                        SaveCategoryItems(currentCategory, categoryItems);
                        currentCategory = trimmedLine.Substring(4, trimmedLine.Length - 8).Trim();
                        categoryItems.Clear();
                    }
                    else if (trimmedLine.StartsWith("• "))
                    {
                        categoryItems.Add(trimmedLine.Substring(2).Trim());
                    }
                }
                
                SaveCategoryItems(currentCategory, categoryItems);
                ShowNotification($"Данные импортированы из {openDialog.FileName}");
                
                if (!string.IsNullOrEmpty(_currentCategory))
                    LoadItemsForCategory(_currentCategory, true, _isDeleteMode, _isAddMode);
            }
            catch (Exception ex)
            {
                ShowNotification($"Ошибка импорта: {ex.Message}", true);
            }
        }
        
        private void SaveCategoryItems(string category, List<string> items)
        {
            if (string.IsNullOrEmpty(category) || items.Count == 0) return;
            
            if (!_filePaths.TryGetValue(category, out var filePath)) return;
            
            var existingItems = File.ReadAllLines(filePath)
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Select(l => l.Trim())
                .ToList();
            
            foreach (var item in items)
            {
                var cleanItem = item.StartsWith("⭐ ") ? item.Substring(2) : item;
                if (!existingItems.Any(existing => existing.EndsWith($"|{cleanItem}") || existing == cleanItem))
                    existingItems.Add($"{DateTime.Now.Ticks}|{cleanItem}");
            }
            
            File.WriteAllLines(filePath, existingItems);
        }
        
        private void StatsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var stats = new StringBuilder();
                var totalItems = 0;
                
                stats.AppendLine("📊 Статистика Anime Manga Novell Read");
                stats.AppendLine("=====================================");
                stats.AppendLine();
                
                foreach (var kvp in _filePaths)
                {
                    var items = 0;
                    if (File.Exists(kvp.Value))
                    {
                        items = File.ReadAllLines(kvp.Value)
                            .Count(line => !string.IsNullOrWhiteSpace(line));
                    }
                    
                    var favoritesInCategory = _favorites.Count(f => f.StartsWith($"{kvp.Key}:"));
                    
                    stats.AppendLine($"• {kvp.Key}: {items} элементов ({favoritesInCategory} в избранном)");
                    totalItems += items;
                }
                
                stats.AppendLine();
                stats.AppendLine($"📈 Всего: {totalItems} элементов");
                stats.AppendLine($"⭐ Избранное: {_favorites.Count} элементов");
                stats.AppendLine();
                stats.AppendLine($"🗓️ Дата: {DateTime.Now:dd.MM.yyyy HH:mm}");
                
                MessageBox.Show(stats.ToString(), "Статистика Anime Manga Novell Read", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                ShowNotification($"Ошибка получения статистики: {ex.Message}", true);
            }
        }
        
        private void ClearAllDataButton_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show(
                "Вы уверены, что хотите очистить ВСЕ данные?\nЭто действие нельзя отменить!", 
                "Очистка всех данных - Anime Manga Novell Read", 
                MessageBoxButton.YesNo, 
                MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            
            try
            {
                foreach (var kvp in _filePaths)
                {
                    if (File.Exists(kvp.Value))
                        File.WriteAllText(kvp.Value, "");
                }
                
                _favorites.Clear();
                SaveFavorites();
                ShowNotification("Все данные очищены");
                
                if (!string.IsNullOrEmpty(_currentCategory))
                    LoadItemsForCategory(_currentCategory, true, _isDeleteMode, _isAddMode);
            }
            catch (Exception ex)
            {
                ShowNotification($"Ошибка очистки данных: {ex.Message}", true);
            }
        }
    }
}
