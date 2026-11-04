using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace OyunCeviriPC
{
    public class DetectedTextBlock
    {
        public string OriginalText { get; set; } = "";
        public string TranslatedText { get; set; } = "";
        public string SourceTag { get; set; } = "";
        public Rectangle ScreenBounds { get; set; }
        public System.Windows.Media.Color SampledBackColor { get; set; } = System.Windows.Media.Color.FromRgb(16, 20, 28);
        public System.Windows.Media.Color SampledTextColor { get; set; } = System.Windows.Media.Color.FromRgb(255, 255, 255);
        public int LineCount { get; set; } = 1;
    }

    public class ScreenOcrService
    {
        private OcrEngine? _ocrEngine;

        public ScreenOcrService()
        {
            InitializeOcrEngine();
        }

        private void InitializeOcrEngine()
        {
            try
            {
                // Önce İngilizce dil paketi varsa onu dene, yoksa sistemdeki mevcut Latin motorunu (tr vb.) kullan
                var enLang = new Windows.Globalization.Language("en-US");
                if (OcrEngine.IsLanguageSupported(enLang))
                {
                    _ocrEngine = OcrEngine.TryCreateFromLanguage(enLang);
                }

                if (_ocrEngine == null)
                {
                    _ocrEngine = OcrEngine.TryCreateFromUserProfileLanguages();
                }

                if (_ocrEngine == null && OcrEngine.AvailableRecognizerLanguages.Count > 0)
                {
                    _ocrEngine = OcrEngine.TryCreateFromLanguage(OcrEngine.AvailableRecognizerLanguages[0]);
                }
            }
            catch
            {
                // Fallback
            }
        }

        public List<Rectangle> GetScanRegions(AppConfig cfg)
        {
            var screen = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
            var list = new List<Rectangle>();

            if (cfg.ScanMode == 0)
            {
                // Tam Ekran (Menü + Görev + Altyazı + HUD)
                list.Add(screen);
            }
            else if (cfg.ScanMode == 1)
            {
                // Altyazı Bölgesi + Üst/Yan Görev Bölgesi
                int subX = (int)(screen.Width * (cfg.RegionX / 100.0));
                int subY = (int)(screen.Height * (cfg.RegionY / 100.0));
                int subW = Math.Max(100, (int)(screen.Width * (cfg.RegionW / 100.0)));
                int subH = Math.Max(50, (int)(screen.Height * (cfg.RegionH / 100.0)));
                list.Add(ClampRect(new Rectangle(screen.Left + subX, screen.Top + subY, subW, subH), screen));

                int qX = (int)(screen.Width * (cfg.QuestRegionX / 100.0));
                int qY = (int)(screen.Height * (cfg.QuestRegionY / 100.0));
                int qW = Math.Max(100, (int)(screen.Width * (cfg.QuestRegionW / 100.0)));
                int qH = Math.Max(50, (int)(screen.Height * (cfg.QuestRegionH / 100.0)));
                list.Add(ClampRect(new Rectangle(screen.Left + qX, screen.Top + qY, qW, qH), screen));
            }
            else
            {
                // Sadece Özel Bölge
                int rx = (int)(screen.Width * (cfg.RegionX / 100.0));
                int ry = (int)(screen.Height * (cfg.RegionY / 100.0));
                int rw = Math.Max(80, (int)(screen.Width * (cfg.RegionW / 100.0)));
                int rh = Math.Max(40, (int)(screen.Height * (cfg.RegionH / 100.0)));
                list.Add(ClampRect(new Rectangle(screen.Left + rx, screen.Top + ry, rw, rh), screen));
            }

            return list;
        }

        private static Rectangle ClampRect(Rectangle r, Rectangle bounds)
        {
            int x = Math.Clamp(r.X, bounds.Left, bounds.Right - 40);
            int y = Math.Clamp(r.Y, bounds.Top, bounds.Bottom - 30);
            int w = Math.Clamp(r.Width, 40, bounds.Right - x);
            int h = Math.Clamp(r.Height, 30, bounds.Bottom - y);
            return new Rectangle(x, y, w, h);
        }

        public async Task<List<DetectedTextBlock>> CaptureAndReadAsync(AppConfig cfg)
        {
            var results = new List<DetectedTextBlock>();
            if (_ocrEngine == null) return results;

            var regions = GetScanRegions(cfg);
            foreach (var region in regions)
            {
                var regionBlocks = await CaptureRegionAndOcrAsync(region);
                results.AddRange(regionBlocks);
            }

            return results;
        }

        private async Task<List<DetectedTextBlock>> CaptureRegionAndOcrAsync(Rectangle region)
        {
            var detected = new List<DetectedTextBlock>();
            if (region.Width <= 10 || region.Height <= 10) return detected;

            using var bmp = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(region.Left, region.Top, 0, 0, region.Size, CopyPixelOperation.SourceCopy);
            }

            SoftwareBitmap? softBmp = null;
            try
            {
                using var ms = new MemoryStream();
                bmp.Save(ms, ImageFormat.Bmp);
                ms.Position = 0;

                var decoder = await BitmapDecoder.CreateAsync(ms.AsRandomAccessStream());
                softBmp = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);

                var ocrResult = await _ocrEngine!.RecognizeAsync(softBmp);
                if (ocrResult?.Lines == null || ocrResult.Lines.Count == 0)
                    return detected;

                // 1. Adım: Aynı satırda birbirinden çok uzak sütunları (örn. sol menü vs sağ açıklama) ayır
                var rawSegments = new List<WordSegment>();
                foreach (var line in ocrResult.Lines)
                {
                    if (line.Words == null || line.Words.Count == 0) continue;

                    WordSegment? currentSeg = null;
                    foreach (var w in line.Words)
                    {
                        var wr = new Rectangle((int)w.BoundingRect.X, (int)w.BoundingRect.Y, (int)Math.Ceiling(w.BoundingRect.Width), (int)Math.Ceiling(w.BoundingRect.Height));
                        if (currentSeg == null)
                        {
                            currentSeg = new WordSegment
                            {
                                Text = w.Text,
                                Bounds = wr
                            };
                        }
                        else
                        {
                            int horizontalGap = wr.Left - currentSeg.Bounds.Right;
                            int maxExpectedGap = Math.Max(currentSeg.Bounds.Height, wr.Height) * 2;
                            if (horizontalGap > Math.Max(36, maxExpectedGap))
                            {
                                rawSegments.Add(currentSeg);
                                currentSeg = new WordSegment
                                {
                                    Text = w.Text,
                                    Bounds = wr
                                };
                            }
                            else
                            {
                                currentSeg.Text += " " + w.Text;
                                currentSeg.Bounds = Rectangle.Union(currentSeg.Bounds, wr);
                            }
                        }
                    }

                    if (currentSeg != null)
                        rawSegments.Add(currentSeg);
                }

                // 2. Adım: Alt alta devam eden cümleleri / altyazı veya görev paragraflarını birleştir
                // Ancak dikey menü maddelerini (New Game / Continue / Options gibi kısa ve aralıklı satırları) ayrı tut!
                var mergedSegments = MergeParagraphLines(rawSegments);

                foreach (var seg in mergedSegments)
                {
                    string cleaned = CleanOcrText(seg.Text);
                    if (!IsValidTranslatableText(cleaned)) continue;

                    var sampleColor = SampleBackgroundAroundRect(bmp, seg.Bounds);
                    var screenRect = new Rectangle(
                        region.Left + seg.Bounds.X,
                        region.Top + seg.Bounds.Y,
                        seg.Bounds.Width,
                        seg.Bounds.Height
                    );

                    detected.Add(new DetectedTextBlock
                    {
                        OriginalText = cleaned,
                        ScreenBounds = screenRect,
                        SampledBackColor = sampleColor,
                        LineCount = seg.LineCount
                    });
                }
            }
            catch
            {
                // Ekran yakalama veya OCR sırasında geçici hata olursa yut
            }
            finally
            {
                softBmp?.Dispose();
            }

            return detected;
        }

        private class WordSegment
        {
            public string Text { get; set; } = "";
            public Rectangle Bounds { get; set; }
            public int LineCount { get; set; } = 1;
            public bool Merged { get; set; }
        }

        private static List<WordSegment> MergeParagraphLines(List<WordSegment> segments)
        {
            var ordered = segments.OrderBy(s => s.Bounds.Top).ThenBy(s => s.Bounds.Left).ToList();
            var result = new List<WordSegment>();

            for (int i = 0; i < ordered.Count; i++)
            {
                if (ordered[i].Merged) continue;
                var current = ordered[i];

                for (int j = i + 1; j < ordered.Count; j++)
                {
                    var next = ordered[j];
                    if (next.Merged) continue;

                    int vGap = next.Bounds.Top - current.Bounds.Bottom;
                    int avgLineH = Math.Max(12, current.Bounds.Height / Math.Max(1, current.LineCount));

                    // Eğer dikey mesafe çok fazlaysa artık daha aşağıdakilere bakmaya gerek yok
                    if (vGap > avgLineH * 1.1) break;
                    if (vGap < -avgLineH / 2) continue;

                    // Yatay hizalama kontrolü (Sol hizalı paragraf veya ortalanmış altyazı)
                    bool leftAligned = Math.Abs(current.Bounds.Left - next.Bounds.Left) < avgLineH * 1.8;
                    int currentCenter = current.Bounds.Left + current.Bounds.Width / 2;
                    int nextCenter = next.Bounds.Left + next.Bounds.Width / 2;
                    bool centerAligned = Math.Abs(currentCenter - nextCenter) < avgLineH * 2.5;

                    // Kısa menü öğelerini (örn: "Continue", "Load Game", "Options") alt alta diye birleştirme!
                    // Yalnızca üst satır nispeten uzunsa (> 26 karakter veya >= 5 kelime) VEYA noktalama/küçük harfle devam ediyorsa birleştir.
                    int wordCountCurr = current.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
                    bool looksLikeSentenceContinuation =
                        wordCountCurr >= 5 ||
                        current.Text.Length >= 28 ||
                        current.Text.EndsWith(",") ||
                        (next.Text.Length > 0 && char.IsLower(next.Text[0]));

                    if ((leftAligned || centerAligned) && looksLikeSentenceContinuation && vGap <= avgLineH * 0.75)
                    {
                        current.Text = current.Text.TrimEnd('-') + " " + next.Text;
                        current.Bounds = Rectangle.Union(current.Bounds, next.Bounds);
                        current.LineCount++;
                        next.Merged = true;
                    }
                }

                result.Add(current);
            }

            return result;
        }

        private static string CleanOcrText(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            string s = raw
                .Replace("|", "I")
                .Replace("—", "-")
                .Replace("…", "...")
                .Trim();

            while (s.Contains("  "))
                s = s.Replace("  ", " ");

            return s;
        }

        private static bool IsValidTranslatableText(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Length < 2) return false;

            int letters = 0;
            int digits = 0;
            foreach (char c in text)
            {
                if (char.IsLetter(c)) letters++;
                else if (char.IsDigit(c)) digits++;
            }

            // En az 2 harf içermeli ve harf sayısı rakam sayısından anlamlı olmalı (FPS sayacı, saat, mermi sayısı vb. elenir)
            if (letters < 2) return false;
            if (digits > letters * 2) return false;

            return true;
        }

        private static System.Windows.Media.Color SampleBackgroundAroundRect(Bitmap bmp, Rectangle rect)
        {
            try
            {
                int rSum = 0, gSum = 0, bSum = 0, count = 0;

                // Kutunun kenar piksellerini (dış/iç sınırını) örnekleyerek arka plan rengini bul
                int left = Math.Clamp(rect.Left - 2, 0, bmp.Width - 1);
                int right = Math.Clamp(rect.Right + 2, 0, bmp.Width - 1);
                int top = Math.Clamp(rect.Top - 2, 0, bmp.Height - 1);
                int bottom = Math.Clamp(rect.Bottom + 2, 0, bmp.Height - 1);

                int stepX = Math.Max(2, (right - left) / 10);
                for (int x = left; x <= right; x += stepX)
                {
                    var c1 = bmp.GetPixel(x, top);
                    var c2 = bmp.GetPixel(x, bottom);
                    rSum += c1.R + c2.R;
                    gSum += c1.G + c2.G;
                    bSum += c1.B + c2.B;
                    count += 2;
                }

                if (count > 0)
                {
                    byte r = (byte)Math.Clamp((rSum / count) * 0.75, 8, 220);
                    byte g = (byte)Math.Clamp((gSum / count) * 0.75, 8, 220);
                    byte b = (byte)Math.Clamp((bSum / count) * 0.75, 8, 220);
                    return System.Windows.Media.Color.FromRgb(r, g, b);
                }
            }
            catch
            {
            }

            return System.Windows.Media.Color.FromRgb(16, 20, 28);
        }
    }
}
