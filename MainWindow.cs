using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace OyunCeviriPC
{
    public class MainWindow : Window
    {
        private const int WM_HOTKEY = 0x0312;
        private const int HOTKEY_TOGGLE_LIVE = 9001; // F9
        private const int HOTKEY_INSTANT_SCAN = 9002; // F8
        private const int HOTKEY_CLEAR = 9003; // F10
        private const uint VK_F8 = 0x77;
        private const uint VK_F9 = 0x78;
        private const uint VK_F10 = 0x79;
        private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint dwAffinity);

        private readonly AppConfig _cfg;
        private readonly ScreenOcrService _ocrService;
        private readonly OverlayWindow _overlay;
        private readonly DispatcherTimer _timer;
        private bool _isRunning;
        private bool _isBusy;

        // UI Kontrolleri
        private Button _btnStartStop = null!;
        private TextBlock _txtStatus = null!;
        private ComboBox _cmbScanMode = null!;
        private CheckBox _chkInPlace = null!;
        private CheckBox _chkChameleon = null!;
        private CheckBox _chkGuide = null!;
        private ComboBox _cmbGames = null!;
        private TextBlock _txtDictStatus = null!;
        private Button _btnDownloadDict = null!;
        private ListBox _lstLog = null!;

        private Slider _sldInterval = null!;
        private TextBlock _lblIntervalVal = null!;
        private Slider _sldFontScale = null!;
        private TextBlock _lblFontScaleVal = null!;
        private Slider _sldBgOpacity = null!;
        private TextBlock _lblBgOpacityVal = null!;
        private Slider _sldTextOpacity = null!;
        private TextBlock _lblTextOpacityVal = null!;

        private Slider _sldRegX = null!;
        private Slider _sldRegY = null!;
        private Slider _sldRegW = null!;
        private Slider _sldRegH = null!;
        private TextBlock _lblRegSummary = null!;

        private List<CatalogGameItem> _catalogItems = new();

        public MainWindow()
        {
            _cfg = AppConfig.Load();
            _ocrService = new ScreenOcrService();
            _overlay = new OverlayWindow();
            _overlay.Show();

            Title = "OyunCeviriPC — Tam Ekran Menü, Görev ve Altyazı Çevirici";
            Width = 920;
            Height = 760;
            MinWidth = 780;
            MinHeight = 640;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new SolidColorBrush(Color.FromRgb(15, 23, 42));
            Foreground = Brushes.White;

            BuildUi();

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(Math.Clamp(_cfg.ScanIntervalMs, 250, 3000))
            };
            _timer.Tick += async (_, _) => await RunCaptureCycleAsync(forceFullScreen: false);

            Loaded += async (_, _) => await InitializeCatalogAndDictAsync();
            Closed += OnWindowClosed;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var hwnd = new WindowInteropHelper(this).Handle;
            var source = HwndSource.FromHwnd(hwnd);
            source?.AddHook(HwndHook);

            // Kontrol panelinin kendisini de ekran yakalamadan gizle ki test ederken kendi butonlarını çevirmesin
            SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE);

            RegisterHotKey(hwnd, HOTKEY_INSTANT_SCAN, 0, VK_F8);
            RegisterHotKey(hwnd, HOTKEY_TOGGLE_LIVE, 0, VK_F9);
            RegisterHotKey(hwnd, HOTKEY_CLEAR, 0, VK_F10);
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                if (id == HOTKEY_TOGGLE_LIVE)
                {
                    ToggleLiveTranslation();
                    handled = true;
                }
                else if (id == HOTKEY_INSTANT_SCAN)
                {
                    _ = RunCaptureCycleAsync(forceFullScreen: true);
                    handled = true;
                }
                else if (id == HOTKEY_CLEAR)
                {
                    _overlay.ClearOverlays();
                    _txtStatus.Text = "Ekran üzerindeki çeviri kutuları temizlendi (F10).";
                    handled = true;
                }
            }
            return IntPtr.Zero;
        }

        private void BuildUi()
        {
            var rootScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(18)
            };

            var mainStack = new StackPanel();
            rootScroll.Content = mainStack;
            Content = rootScroll;

            // 1. ÜST BAŞLIK VE KONTROL BUTONLARI
            var headerBorder = CreateCardBorder();
            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titleStack = new StackPanel();
            titleStack.Children.Add(new TextBlock
            {
                Text = "🎮 OyunCeviriPC — Canlı Tam Ekran & Menü Çevirici",
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(56, 189, 248))
            });
            _txtStatus = new TextBlock
            {
                Text = "Hazır. Oyuna geçip F9 ile canlı çeviriyi başlatabilir veya F8 ile anlık menü/görev ekranını çevirebilirsiniz.",
                FontSize = 12.5,
                Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                Margin = new Thickness(0, 4, 0, 0),
                TextWrapping = TextWrapping.Wrap
            };
            titleStack.Children.Add(_txtStatus);
            Grid.SetColumn(titleStack, 0);
            headerGrid.Children.Add(titleStack);

            var btnStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            _btnStartStop = CreateActionButton("▶ CANLI ÇEVİRİYİ BAŞLAT (F9)", Color.FromRgb(16, 185, 129), 210);
            _btnStartStop.Click += (_, _) => ToggleLiveTranslation();
            btnStack.Children.Add(_btnStartStop);

            var btnInstant = CreateActionButton("⚡ ANLIK MENÜ / EKRAN ÇEVİR (F8)", Color.FromRgb(59, 130, 246), 220);
            btnInstant.Margin = new Thickness(10, 0, 0, 0);
            btnInstant.Click += async (_, _) => await RunCaptureCycleAsync(forceFullScreen: true);
            btnStack.Children.Add(btnInstant);

            Grid.SetColumn(btnStack, 1);
            headerGrid.Children.Add(btnStack);
            headerBorder.Child = headerGrid;
            mainStack.Children.Add(headerBorder);

            // 2. İKİ SÜTUNLU ANA AYARLAR BÖLÜMÜ
            var twoColGrid = new Grid { Margin = new Thickness(0, 12, 0, 0) };
            twoColGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            twoColGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14, GridUnitType.Pixel) });
            twoColGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // SOL SÜTUN: Çeviri Modu + 38 Oyun Sözlüğü + Okuma Alanı
            var leftStack = new StackPanel();
            Grid.SetColumn(leftStack, 0);
            twoColGrid.Children.Add(leftStack);

            // Kart A: Tarama ve Yerleşim Modu
            var modeCard = CreateCardBorder();
            var modeStack = new StackPanel();
            modeStack.Children.Add(CreateSectionHeader("1. Ekran Tarama ve Konumlandırma Modu"));

            modeStack.Children.Add(CreateLabel("Tarama Kapsamı:"));
            _cmbScanMode = new ComboBox
            {
                Margin = new Thickness(0, 4, 0, 10),
                Padding = new Thickness(8, 6, 8, 6),
                FontSize = 13
            };
            _cmbScanMode.Items.Add("Tam Ekran Her Yeri Çevir (Ana Menü + Görevler + Envanter + Altyazı)");
            _cmbScanMode.Items.Add("Altyazı + Üst Görev Bölgeleri (Oyun İçi Hızlı Mod)");
            _cmbScanMode.Items.Add("Sadece Özel Seçili Bölge");
            _cmbScanMode.SelectedIndex = Math.Clamp(_cfg.ScanMode, 0, 2);
            _cmbScanMode.SelectionChanged += (_, _) =>
            {
                _cfg.ScanMode = _cmbScanMode.SelectedIndex;
                _cfg.Save();
                RefreshPreviewOverlay();
            };
            modeStack.Children.Add(_cmbScanMode);

            _chkInPlace = new CheckBox
            {
                Content = "Her Yazıyı Kendi Yerinde Çevir (İngilizce metnin tam üstüne Türkçe kutu yerleştir)",
                IsChecked = _cfg.InPlaceOverlay,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 4, 0, 6),
                FontSize = 13
            };
            _chkInPlace.Checked += (_, _) => { _cfg.InPlaceOverlay = true; _cfg.Save(); RefreshPreviewOverlay(); };
            _chkInPlace.Unchecked += (_, _) => { _cfg.InPlaceOverlay = false; _cfg.Save(); RefreshPreviewOverlay(); };
            modeStack.Children.Add(_chkInPlace);

            _chkChameleon = new CheckBox
            {
                Content = "Bukalemun Modu (Menü/HUD arka plan rengini otomatik algıla ve uydur)",
                IsChecked = _cfg.AutoSampleBackground,
                Foreground = new SolidColorBrush(Color.FromRgb(167, 243, 208)),
                Margin = new Thickness(0, 4, 0, 6),
                FontSize = 13
            };
            _chkChameleon.Checked += (_, _) => { _cfg.AutoSampleBackground = true; _cfg.Save(); };
            _chkChameleon.Unchecked += (_, _) => { _cfg.AutoSampleBackground = false; _cfg.Save(); };
            modeStack.Children.Add(_chkChameleon);

            _chkGuide = new CheckBox
            {
                Content = "Yeşil Okuma Alanı Kılavuz Çerçevesini Ekranda Göster",
                IsChecked = _cfg.ShowRegionGuide,
                Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                Margin = new Thickness(0, 4, 0, 4),
                FontSize = 13
            };
            _chkGuide.Checked += (_, _) => { _cfg.ShowRegionGuide = true; _cfg.Save(); RefreshPreviewOverlay(); };
            _chkGuide.Unchecked += (_, _) => { _cfg.ShowRegionGuide = false; _cfg.Save(); RefreshPreviewOverlay(); };
            modeStack.Children.Add(_chkGuide);

            modeCard.Child = modeStack;
            leftStack.Children.Add(modeCard);

            // Kart B: 38 Hazır Oyun Sözlüğü (Mafia: The Old Country, RDR2 vb.)
            var dictCard = CreateCardBorder();
            dictCard.Margin = new Thickness(0, 12, 0, 0);
            var dictStack = new StackPanel();
            dictStack.Children.Add(CreateSectionHeader("2. Hazır Oyun Sözlüğü (38 Oyun + Yerleşik Menü Sözlüğü)"));

            _txtDictStatus = new TextBlock
            {
                Text = "Yerleşik Oyun Menü/Görev Sözlüğü aktif. Katalog yükleniyor...",
                Foreground = new SolidColorBrush(Color.FromRgb(56, 189, 248)),
                FontSize = 12.5,
                Margin = new Thickness(0, 0, 0, 8),
                TextWrapping = TextWrapping.Wrap
            };
            dictStack.Children.Add(_txtDictStatus);

            _cmbGames = new ComboBox
            {
                Margin = new Thickness(0, 0, 0, 8),
                Padding = new Thickness(8, 6, 8, 6),
                FontSize = 13
            };
            dictStack.Children.Add(_cmbGames);

            var dictBtnRow = new StackPanel { Orientation = Orientation.Horizontal };
            _btnDownloadDict = CreateActionButton("📥 Seçili Oyun Sözlüğünü İndir / Etkinleştir", Color.FromRgb(14, 165, 233), 270);
            _btnDownloadDict.Click += async (_, _) => await DownloadSelectedGameDictAsync();
            dictBtnRow.Children.Add(_btnDownloadDict);

            var btnClearDict = CreateActionButton("Sadece Genel Çeviri", Color.FromRgb(71, 85, 105), 140);
            btnClearDict.Margin = new Thickness(8, 0, 0, 0);
            btnClearDict.Click += (_, _) =>
            {
                _cfg.SelectedGameId = "none";
                _cfg.UseGameDictionary = false;
                _cfg.Save();
                _txtDictStatus.Text = "Özel oyun sözlüğü kapatıldı (Yerleşik Menü Sözlüğü + Online Çeviri aktif).";
            };
            dictBtnRow.Children.Add(btnClearDict);

            dictStack.Children.Add(dictBtnRow);
            dictCard.Child = dictStack;
            leftStack.Children.Add(dictCard);

            // Kart C: Özel Bölge Koordinatları
            var regCard = CreateCardBorder();
            regCard.Margin = new Thickness(0, 12, 0, 0);
            var regStack = new StackPanel();
            regStack.Children.Add(CreateSectionHeader("3. Okuma Bölgesi Koordinatları (Altyazı / Özel Bölge)"));

            _lblRegSummary = new TextBlock
            {
                Text = $"X: %{_cfg.RegionX:0} | Y: %{_cfg.RegionY:0} | Genişlik: %{_cfg.RegionW:0} | Yükseklik: %{_cfg.RegionH:0}",
                Foreground = new SolidColorBrush(Color.FromRgb(16, 185, 129)),
                FontSize = 12.5,
                Margin = new Thickness(0, 0, 0, 6)
            };
            regStack.Children.Add(_lblRegSummary);

            _sldRegX = CreateSliderRow(regStack, "Yatay Konum (X %):", 0, 90, _cfg.RegionX, v => { _cfg.RegionX = v; UpdateRegionSummary(); });
            _sldRegY = CreateSliderRow(regStack, "Dikey Konum (Y %):", 0, 92, _cfg.RegionY, v => { _cfg.RegionY = v; UpdateRegionSummary(); });
            _sldRegW = CreateSliderRow(regStack, "Genişlik (W %):", 10, 100, _cfg.RegionW, v => { _cfg.RegionW = v; UpdateRegionSummary(); });
            _sldRegH = CreateSliderRow(regStack, "Yükseklik (H %):", 5, 100, _cfg.RegionH, v => { _cfg.RegionH = v; UpdateRegionSummary(); });

            regCard.Child = regStack;
            leftStack.Children.Add(regCard);

            // SAĞ SÜTUN: Görünüm Ayarları + Canlı Çeviri Günlüğü
            var rightStack = new StackPanel();
            Grid.SetColumn(rightStack, 2);
            twoColGrid.Children.Add(rightStack);

            // Kart D: Görünüm, Font Boyutu, Renk ve Opaklık
            var styleCard = CreateCardBorder();
            var styleStack = new StackPanel();
            styleStack.Children.Add(CreateSectionHeader("4. Yazı Boyutu, Renk ve Arka Plan Opaklığı"));

            (_sldInterval, _lblIntervalVal) = CreateLabeledSlider(styleStack, "Tarama Hızı (ms):", 250, 2000, _cfg.ScanIntervalMs, "ms", v =>
            {
                _cfg.ScanIntervalMs = (int)v;
                _timer.Interval = TimeSpan.FromMilliseconds(_cfg.ScanIntervalMs);
                _cfg.Save();
            });

            (_sldFontScale, _lblFontScaleVal) = CreateLabeledSlider(styleStack, "Yazı Boyutu Ölçeği (%):", 60, 180, _cfg.FontScalePercent, "%", v =>
            {
                _cfg.FontScalePercent = v;
                _cfg.Save();
            });

            (_sldBgOpacity, _lblBgOpacityVal) = CreateLabeledSlider(styleStack, "Arka Plan Kutu Opaklığı (%):", 0, 100, _cfg.BgOpacity, "%", v =>
            {
                _cfg.BgOpacity = v;
                _cfg.Save();
            });

            (_sldTextOpacity, _lblTextOpacityVal) = CreateLabeledSlider(styleStack, "Yazı Opaklığı (%):", 25, 100, _cfg.TextOpacity, "%", v =>
            {
                _cfg.TextOpacity = v;
                _cfg.Save();
            });

            // Yazı Rengi Butonları
            styleStack.Children.Add(CreateLabel("Yazı Rengi:"));
            var textColorRow = new WrapPanel { Margin = new Thickness(0, 4, 0, 8) };
            AddColorPresetButton(textColorRow, "Beyaz", "#FFFFFF", isText: true);
            AddColorPresetButton(textColorRow, "Altın Sarı", "#FACC15", isText: true);
            AddColorPresetButton(textColorRow, "Camgöbeği", "#38BDF8", isText: true);
            AddColorPresetButton(textColorRow, "Açık Yeşil", "#4ADE80", isText: true);
            AddColorPresetButton(textColorRow, "Turuncu", "#FB923C", isText: true);
            styleStack.Children.Add(textColorRow);

            // Arka Plan Rengi Butonları
            styleStack.Children.Add(CreateLabel("Sabit Arka Plan Rengi (Bukalemun Modu kapalıyken):"));
            var bgColorRow = new WrapPanel { Margin = new Thickness(0, 4, 0, 4) };
            AddColorPresetButton(bgColorRow, "Koyu Lacivert", "#10141C", isText: false);
            AddColorPresetButton(bgColorRow, "Tam Siyah", "#000000", isText: false);
            AddColorPresetButton(bgColorRow, "Koyu Gri", "#1F2937", isText: false);
            AddColorPresetButton(bgColorRow, "Koyu Kahve", "#271C19", isText: false);
            styleStack.Children.Add(bgColorRow);

            styleCard.Child = styleStack;
            rightStack.Children.Add(styleCard);

            // Kart E: Canlı Çeviri Günlüğü
            var logCard = CreateCardBorder();
            logCard.Margin = new Thickness(0, 12, 0, 0);
            var logStack = new StackPanel();
            logStack.Children.Add(CreateSectionHeader("5. Canlı Yakalanan & Çevrilen Metinler"));

            _lstLog = new ListBox
            {
                Height = 250,
                Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
                Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
                FontSize = 12
            };
            logStack.Children.Add(_lstLog);

            logCard.Child = logStack;
            rightStack.Children.Add(logCard);

            mainStack.Children.Add(twoColGrid);
        }

        private async Task InitializeCatalogAndDictAsync()
        {
            _catalogItems = await TranslationEngine.FetchCatalogGamesAsync();
            _cmbGames.Items.Clear();

            int selectedIdx = -1;
            for (int i = 0; i < _catalogItems.Count; i++)
            {
                var g = _catalogItems[i];
                bool cached = TranslationEngine.IsGameInstalled(g);
                double mb = g.DictBytes / (1024.0 * 1024.0);
                string badge = cached ? " [İNDİRİLDİ]" : "";
                _cmbGames.Items.Add($"{g.Title} ({mb:F1} MB){badge}");
                if (g.Id == _cfg.SelectedGameId)
                    selectedIdx = i;
            }

            if (selectedIdx == -1)
            {
                selectedIdx = _catalogItems.FindIndex(x => x.Id.Contains("mafia", StringComparison.OrdinalIgnoreCase));
                if (selectedIdx == -1 && _catalogItems.Count > 0) selectedIdx = 0;
            }

            if (selectedIdx >= 0 && selectedIdx < _cmbGames.Items.Count)
                _cmbGames.SelectedIndex = selectedIdx;

            if (!string.IsNullOrWhiteSpace(_cfg.SelectedGameId) && _cfg.SelectedGameId != "none")
            {
                var item = _catalogItems.FirstOrDefault(x => x.Id == _cfg.SelectedGameId);
                if (item != null && TranslationEngine.IsGameInstalled(item))
                {
                    var (_, msg) = await TranslationEngine.DownloadAndLoadGameAsync(item, (_, status) =>
                    {
                        Dispatcher.Invoke(() => _txtDictStatus.Text = status);
                    });
                    _txtDictStatus.Text = msg;
                    return;
                }
            }

            _txtDictStatus.Text = $"Hazır: {_catalogItems.Count} oyun sözlüğü katalogda mevcut. İstediğiniz oyunu seçip indirebilirsiniz.";
        }

        private async Task DownloadSelectedGameDictAsync()
        {
            int idx = _cmbGames.SelectedIndex;
            if (idx < 0 || idx >= _catalogItems.Count) return;

            var game = _catalogItems[idx];
            _btnDownloadDict.IsEnabled = false;

            var (ok, msg) = await TranslationEngine.DownloadAndLoadGameAsync(game, (_, status) =>
            {
                Dispatcher.Invoke(() => _txtDictStatus.Text = status);
            });

            _txtDictStatus.Text = msg;
            if (ok)
            {
                _cfg.SelectedGameId = game.Id;
                _cfg.SelectedGameTitle = game.Title;
                _cfg.UseGameDictionary = true;
                _cfg.Save();
                double mb = game.DictBytes / (1024.0 * 1024.0);
                _cmbGames.Items[idx] = $"{game.Title} ({mb:F1} MB) [AKTİF]";
            }

            _btnDownloadDict.IsEnabled = true;
        }

        private void ToggleLiveTranslation()
        {
            _isRunning = !_isRunning;
            if (_isRunning)
            {
                _btnStartStop.Content = "⏹ CANLI ÇEVİRİYİ DURDUR (F9)";
                _btnStartStop.Background = new SolidColorBrush(Color.FromRgb(239, 68, 68));
                _txtStatus.Text = "Canlı çeviri AKTİF. Ekrandaki menü, görev ve altyazılar otomatik çevriliyor...";
                _timer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(_cfg.ScanIntervalMs, 250, 3000));
                _timer.Start();
                _ = RunCaptureCycleAsync(forceFullScreen: false);
            }
            else
            {
                _timer.Stop();
                _btnStartStop.Content = "▶ CANLI ÇEVİRİYİ BAŞLAT (F9)";
                _btnStartStop.Background = new SolidColorBrush(Color.FromRgb(16, 185, 129));
                _txtStatus.Text = "Canlı çeviri durduruldu.";
                _overlay.ClearOverlays();
            }
        }

        private async Task RunCaptureCycleAsync(bool forceFullScreen)
        {
            if (_isBusy) return;
            _isBusy = true;

            try
            {
                int prevMode = _cfg.ScanMode;
                if (forceFullScreen)
                    _cfg.ScanMode = 0;

                var regions = _ocrService.GetScanRegions(_cfg);
                var blocks = await _ocrService.CaptureAndReadAsync(_cfg);

                if (forceFullScreen)
                    _cfg.ScanMode = prevMode;

                var translatedBlocks = new List<DetectedTextBlock>();
                foreach (var b in blocks)
                {
                    string? instant = TranslationEngine.TryTranslateInstantOrQueue(b.OriginalText, _cfg);
                    string tr;
                    string src;

                    if (!string.IsNullOrWhiteSpace(instant))
                    {
                        tr = instant;
                        src = "Sözlük/Önbellek";
                    }
                    else
                    {
                        tr = await TranslationEngine.TranslateGuaranteedAsync(b.OriginalText, _cfg);
                        src = "Online Çeviri";
                    }

                    if (!string.IsNullOrWhiteSpace(tr))
                    {
                        b.TranslatedText = tr;
                        b.SourceTag = src;
                        translatedBlocks.Add(b);
                    }
                }

                _overlay.RenderOverlays(translatedBlocks, regions, _cfg);

                if (translatedBlocks.Count > 0)
                {
                    _txtStatus.Text = $"Son taramada {translatedBlocks.Count} metin bloğu çevrildi ({DateTime.Now:HH:mm:ss}).";
                    UpdateLogList(translatedBlocks);
                }
            }
            catch (Exception ex)
            {
                _txtStatus.Text = $"Tarama hatası: {ex.Message}";
            }
            finally
            {
                _isBusy = false;
            }
        }

        private void UpdateLogList(List<DetectedTextBlock> blocks)
        {
            foreach (var b in blocks.Take(8))
            {
                string entry = $"[{b.SourceTag}] {b.OriginalText}  ➔  {b.TranslatedText}";
                if (_lstLog.Items.Count == 0 || _lstLog.Items[0]?.ToString() != entry)
                {
                    _lstLog.Items.Insert(0, entry);
                    while (_lstLog.Items.Count > 60)
                        _lstLog.Items.RemoveAt(_lstLog.Items.Count - 1);
                }
            }
        }

        private void UpdateRegionSummary()
        {
            if (_lblRegSummary != null)
            {
                _lblRegSummary.Text = $"X: %{_cfg.RegionX:0} | Y: %{_cfg.RegionY:0} | Genişlik: %{_cfg.RegionW:0} | Yükseklik: %{_cfg.RegionH:0}";
            }
            _cfg.Save();
            RefreshPreviewOverlay();
        }

        private void RefreshPreviewOverlay()
        {
            if (!_isRunning && _cfg.ShowRegionGuide)
            {
                var regions = _ocrService.GetScanRegions(_cfg);
                _overlay.RenderOverlays(new List<DetectedTextBlock>(), regions, _cfg);
            }
            else if (!_isRunning && !_cfg.ShowRegionGuide)
            {
                _overlay.ClearOverlays();
            }
        }

        private void OnWindowClosed(object? sender, EventArgs e)
        {
            _timer.Stop();
            var hwnd = new WindowInteropHelper(this).Handle;
            UnregisterHotKey(hwnd, HOTKEY_INSTANT_SCAN);
            UnregisterHotKey(hwnd, HOTKEY_TOGGLE_LIVE);
            UnregisterHotKey(hwnd, HOTKEY_CLEAR);
            _overlay.Close();
        }

        // UI Yardımcı Metotlar
        private static Border CreateCardBorder()
        {
            return new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14),
                BorderBrush = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
                BorderThickness = new Thickness(1)
            };
        }

        private static TextBlock CreateSectionHeader(string title)
        {
            return new TextBlock
            {
                Text = title,
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
                Margin = new Thickness(0, 0, 0, 10)
            };
        }

        private static TextBlock CreateLabel(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 12.5,
                Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                Margin = new Thickness(0, 4, 0, 2)
            };
        }

        private static Button CreateActionButton(string text, Color bg, double width)
        {
            return new Button
            {
                Content = text,
                Width = width,
                Height = 38,
                Background = new SolidColorBrush(bg),
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 12.5,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand
            };
        }

        private static Slider CreateSliderRow(StackPanel parent, string label, double min, double max, double val, Action<double> onChanged)
        {
            var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(135) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var tb = new TextBlock
            {
                Text = label,
                Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(tb, 0);
            grid.Children.Add(tb);

            var slider = new Slider
            {
                Minimum = min,
                Maximum = max,
                Value = val,
                VerticalAlignment = VerticalAlignment.Center
            };
            slider.ValueChanged += (_, e) => onChanged(Math.Round(e.NewValue, 1));
            Grid.SetColumn(slider, 1);
            grid.Children.Add(slider);

            parent.Children.Add(grid);
            return slider;
        }

        private static (Slider, TextBlock) CreateLabeledSlider(StackPanel parent, string label, double min, double max, double val, string unit, Action<double> onChanged)
        {
            var headerGrid = new Grid { Margin = new Thickness(0, 4, 0, 2) };
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var lbl = new TextBlock
            {
                Text = label,
                Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                FontSize = 12.5
            };
            var valTb = new TextBlock
            {
                Text = $"{val:0} {unit}",
                Foreground = new SolidColorBrush(Color.FromRgb(56, 189, 248)),
                FontWeight = FontWeights.Bold,
                FontSize = 12.5
            };
            Grid.SetColumn(lbl, 0);
            Grid.SetColumn(valTb, 1);
            headerGrid.Children.Add(lbl);
            headerGrid.Children.Add(valTb);
            parent.Children.Add(headerGrid);

            var sld = new Slider
            {
                Minimum = min,
                Maximum = max,
                Value = val,
                Margin = new Thickness(0, 0, 0, 6)
            };
            sld.ValueChanged += (_, e) =>
            {
                double rounded = Math.Round(e.NewValue);
                valTb.Text = $"{rounded:0} {unit}";
                onChanged(rounded);
            };
            parent.Children.Add(sld);
            return (sld, valTb);
        }

        private void AddColorPresetButton(WrapPanel panel, string name, string hex, bool isText)
        {
            var c = AppConfig.ParseHexColor(hex, Colors.White);
            var btn = new Button
            {
                Content = name,
                Margin = new Thickness(0, 0, 6, 6),
                Padding = new Thickness(10, 4, 10, 4),
                Background = new SolidColorBrush(c),
                Foreground = (c.R * 0.299 + c.G * 0.587 + c.B * 0.114) > 150 ? Brushes.Black : Brushes.White,
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            btn.Click += (_, _) =>
            {
                if (isText) _cfg.TextColorHex = hex;
                else _cfg.BgColorHex = hex;
                _cfg.Save();
            };
            panel.Children.Add(btn);
        }
    }
}
