using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace OyunCeviriPC
{
    public class OverlayWindow : Window
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_LAYERED = 0x00080000;
        private const int WS_EX_TOPMOST = 0x00000008;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint dwAffinity);

        private readonly Canvas _rootCanvas;
        private double _dpiScaleX = 1.0;
        private double _dpiScaleY = 1.0;

        public OverlayWindow()
        {
            Title = "OyunCeviriPC Overlay";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;

            _rootCanvas = new Canvas
            {
                Background = Brushes.Transparent,
                IsHitTestVisible = false
            };

            Content = _rootCanvas;
            UpdateBoundsToPrimaryScreen();
        }

        public void UpdateBoundsToPrimaryScreen()
        {
            var screen = System.Windows.Forms.Screen.PrimaryScreen?.Bounds ?? new System.Drawing.Rectangle(0, 0, 1920, 1080);
            var dpi = VisualTreeHelper.GetDpi(this);
            _dpiScaleX = dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1.0;
            _dpiScaleY = dpi.DpiScaleY > 0 ? dpi.DpiScaleY : 1.0;

            Left = screen.Left / _dpiScaleX;
            Top = screen.Top / _dpiScaleY;
            Width = screen.Width / _dpiScaleX;
            Height = screen.Height / _dpiScaleY;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var hwnd = new WindowInteropHelper(this).Handle;

            // Mouse tıklamalarının oyuna geçmesini sağla (Click-Through) ve Alt+Tab'da gizle
            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOPMOST | WS_EX_TOOLWINDOW);

            // Windows 10/11: Ekran görüntüsü alınırken bu pencereyi gizle (Sıfır titreme / kendi çevirisini tekrar okumama!)
            SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE);

            UpdateBoundsToPrimaryScreen();
        }

        public void ClearOverlays()
        {
            _rootCanvas.Children.Clear();
        }

        public void RenderOverlays(
            List<DetectedTextBlock> blocks,
            List<System.Drawing.Rectangle> scanRegions,
            AppConfig cfg)
        {
            UpdateBoundsToPrimaryScreen();
            _rootCanvas.Children.Clear();

            // 1. Yeşil Okuma Alanı Kılavuz Çerçeveleri (Açık ise)
            if (cfg.ShowRegionGuide && scanRegions != null)
            {
                foreach (var reg in scanRegions)
                {
                    double rx = reg.X / _dpiScaleX;
                    double ry = reg.Y / _dpiScaleY;
                    double rw = reg.Width / _dpiScaleX;
                    double rh = reg.Height / _dpiScaleY;

                    var guideRect = new Rectangle
                    {
                        Width = Math.Max(10, rw - 4),
                        Height = Math.Max(10, rh - 4),
                        Stroke = new SolidColorBrush(Color.FromArgb(210, 16, 185, 129)),
                        StrokeThickness = 2.0,
                        StrokeDashArray = new DoubleCollection { 6, 4 },
                        Fill = new SolidColorBrush(Color.FromArgb(18, 16, 185, 129)),
                        RadiusX = 6,
                        RadiusY = 6
                    };
                    Canvas.SetLeft(guideRect, rx + 2);
                    Canvas.SetTop(guideRect, ry + 2);
                    _rootCanvas.Children.Add(guideRect);

                    var badge = new Border
                    {
                        Background = new SolidColorBrush(Color.FromArgb(210, 16, 185, 129)),
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(6, 2, 6, 2),
                        Child = new TextBlock
                        {
                            Text = $"OKUMA ALANI ({reg.Width}x{reg.Height})",
                            Foreground = Brushes.Black,
                            FontSize = 11,
                            FontWeight = FontWeights.Bold
                        }
                    };
                    Canvas.SetLeft(badge, rx + 8);
                    Canvas.SetTop(badge, Math.Max(4, ry + 6));
                    _rootCanvas.Children.Add(badge);
                }
            }

            if (blocks == null || blocks.Count == 0)
                return;

            var textColor = cfg.GetResolvedTextColor();
            var textBrush = new SolidColorBrush(textColor);

            // 2. Mod A: Her Yazıyı Kendi Yerinde Çevir (Menü + Görev + Envanter + Altyazı)
            if (cfg.InPlaceOverlay)
            {
                foreach (var b in blocks)
                {
                    if (string.IsNullOrWhiteSpace(b.TranslatedText)) continue;

                    double x = (b.ScreenBounds.X / _dpiScaleX) - 4;
                    double y = (b.ScreenBounds.Y / _dpiScaleY) - 3;
                    double w = (b.ScreenBounds.Width / _dpiScaleX) + 12;
                    double h = (b.ScreenBounds.Height / _dpiScaleY) + 6;

                    // Orijinal İngilizce yazının yüksekliğine göre Türkçe font boyutunu otomatik hesapla
                    double singleLineHeightDip = (b.ScreenBounds.Height / _dpiScaleY) / Math.Max(1, b.LineCount);
                    double baseFontSize = Math.Clamp(singleLineHeightDip * 0.78, 11.0, 42.0);
                    double scaledFontSize = Math.Clamp(baseFontSize * (cfg.FontScalePercent / 100.0), 10.0, 54.0);

                    // Eğer Türkçe metin İngilizceden biraz daha uzunsa fontu hafifçe küçült ki kutuya tam sığsın
                    if (b.TranslatedText.Length > b.OriginalText.Length * 1.25 && b.LineCount == 1)
                    {
                        double ratio = (double)b.OriginalText.Length / b.TranslatedText.Length;
                        scaledFontSize = Math.Max(10.0, scaledFontSize * Math.Clamp(ratio * 1.15, 0.72, 1.0));
                    }

                    Color bgColor;
                    if (cfg.AutoSampleBackground)
                    {
                        byte alpha = (byte)Math.Clamp((int)(cfg.BgOpacity * 2.55), 0, 255);
                        bgColor = Color.FromArgb(alpha, b.SampledBackColor.R, b.SampledBackColor.G, b.SampledBackColor.B);
                    }
                    else
                    {
                        bgColor = cfg.GetResolvedBgColor();
                    }

                    var tb = new TextBlock
                    {
                        Text = b.TranslatedText,
                        Foreground = textBrush,
                        FontFamily = new FontFamily(cfg.FontFamilyName),
                        FontSize = scaledFontSize,
                        FontWeight = FontWeights.SemiBold,
                        TextWrapping = b.LineCount > 1 || b.ScreenBounds.Width > 260 ? TextWrapping.Wrap : TextWrapping.NoWrap,
                        VerticalAlignment = VerticalAlignment.Center,
                        Effect = new DropShadowEffect
                        {
                            Color = Colors.Black,
                            BlurRadius = 4,
                            ShadowDepth = 1,
                            Opacity = 0.85
                        }
                    };

                    var border = new Border
                    {
                        Background = new SolidColorBrush(bgColor),
                        CornerRadius = new CornerRadius(cfg.CornerRadius),
                        Padding = new Thickness(6, 2, 6, 2),
                        MinWidth = w,
                        MinHeight = h,
                        MaxWidth = Math.Max(w * 1.35, 220),
                        BorderBrush = new SolidColorBrush(Color.FromArgb(65, 255, 255, 255)),
                        BorderThickness = new Thickness(0.8),
                        Child = tb
                    };

                    Canvas.SetLeft(border, Math.Max(2, x));
                    Canvas.SetTop(border, Math.Max(2, y));
                    _rootCanvas.Children.Add(border);
                }
            }
            else
            {
                // 3. Mod B: Sabit Altyazı Kutusu Modu
                string combined = string.Join("\n", blocks.ConvertAll(b => b.TranslatedText));
                if (string.IsNullOrWhiteSpace(combined)) return;

                double boxX = Width * (cfg.FixedBoxX / 100.0);
                double boxY = Height * (cfg.FixedBoxY / 100.0);
                double boxW = Width * (cfg.FixedBoxW / 100.0);

                var tb = new TextBlock
                {
                    Text = combined,
                    Foreground = textBrush,
                    FontFamily = new FontFamily(cfg.FontFamilyName),
                    FontSize = cfg.FixedFontSize * (cfg.FontScalePercent / 100.0),
                    FontWeight = FontWeights.Bold,
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    HorizontalAlignment = HorizontalAlignment.Center
                };

                var border = new Border
                {
                    Width = boxW,
                    Background = new SolidColorBrush(cfg.GetResolvedBgColor()),
                    CornerRadius = new CornerRadius(cfg.CornerRadius),
                    Padding = new Thickness(18, 10, 18, 10),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(90, 56, 189, 248)),
                    BorderThickness = new Thickness(1.2),
                    Child = tb
                };

                Canvas.SetLeft(border, boxX);
                Canvas.SetTop(border, boxY);
                _rootCanvas.Children.Add(border);
            }
        }
    }
}
