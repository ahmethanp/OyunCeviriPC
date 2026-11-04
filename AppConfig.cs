using System;
using System.IO;
using System.Text.Json;
using System.Windows.Media;

namespace OyunCeviriPC
{
    public class AppConfig
    {
        // 0 = Tam Ekran (Menü + Görev + Altyazı), 1 = Altyazı + Üst Görev Bölgeleri, 2 = Sadece Özel Bölge
        public int ScanMode { get; set; } = 0;

        // Oyun Sözlüğü
        public string SelectedGameId { get; set; } = "mafia-old-country";
        public string SelectedGameTitle { get; set; } = "Mafia: The Old Country";
        public bool UseGameDictionary { get; set; } = true;

        // Özel / Altyazı Okuma Bölgesi (%0 .. %100 ekran yüzdesi)
        public double RegionX { get; set; } = 12.0;
        public double RegionY { get; set; } = 72.0;
        public double RegionW { get; set; } = 76.0;
        public double RegionH { get; set; } = 22.0;

        // Üst/Yan Görev Okuma Bölgesi (%0 .. %100 ekran yüzdesi)
        public double QuestRegionX { get; set; } = 2.0;
        public double QuestRegionY { get; set; } = 6.0;
        public double QuestRegionW { get; set; } = 38.0;
        public double QuestRegionH { get; set; } = 32.0;

        // Her yazıyı ekranda bulunduğu tam koordinatın üstüne mi yazsın (true), yoksa sabit kutuda mı toplasın (false)?
        public bool InPlaceOverlay { get; set; } = true;

        // Sabit Altyazı Kutusu Konumu (InPlaceOverlay == false ise kullanılır, %0..100)
        public double FixedBoxX { get; set; } = 14.0;
        public double FixedBoxY { get; set; } = 80.0;
        public double FixedBoxW { get; set; } = 72.0;

        // Görünüm Ayarları
        public string FontFamilyName { get; set; } = "Segoe UI";
        public double FontScalePercent { get; set; } = 100.0; // %60 .. %180
        public double FixedFontSize { get; set; } = 24.0;
        public string TextColorHex { get; set; } = "#FFFFFF";
        public double TextOpacity { get; set; } = 100.0; // %20 .. %100

        public string BgColorHex { get; set; } = "#10141C";
        public double BgOpacity { get; set; } = 90.0; // %0 .. %100
        public bool AutoSampleBackground { get; set; } = true; // Bukalemun modu: Menünün kendi arka plan rengini alır
        public double CornerRadius { get; set; } = 6.0;

        // Tarama Hızı (ms)
        public int ScanIntervalMs { get; set; } = 450;

        // Yeşil Okuma Alanı Rehber Çerçevesini Göster
        public bool ShowRegionGuide { get; set; } = false;

        public static string ConfigPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");

        public static AppConfig Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    string json = File.ReadAllText(ConfigPath);
                    var cfg = JsonSerializer.Deserialize<AppConfig>(json);
                    if (cfg != null)
                        return cfg;
                }
            }
            catch { }
            return new AppConfig();
        }

        public void Save()
        {
            try
            {
                string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigPath, json);
            }
            catch { }
        }

        public Color GetResolvedTextColor()
        {
            var baseCol = ParseHexColor(TextColorHex, Colors.White);
            byte a = (byte)Math.Clamp((int)Math.Round(TextOpacity * 2.55), 40, 255);
            return Color.FromArgb(a, baseCol.R, baseCol.G, baseCol.B);
        }

        public Color GetResolvedBgColor()
        {
            var baseCol = ParseHexColor(BgColorHex, Color.FromRgb(16, 20, 28));
            byte a = (byte)Math.Clamp((int)Math.Round(BgOpacity * 2.55), 0, 255);
            return Color.FromArgb(a, baseCol.R, baseCol.G, baseCol.B);
        }

        public static Color ParseHexColor(string hex, Color fallback)
        {
            try
            {
                return (Color)ColorConverter.ConvertFromString(hex);
            }
            catch
            {
                return fallback;
            }
        }
    }
}
