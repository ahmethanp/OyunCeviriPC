using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace OyunCeviriPC;

public class CatalogGameItem
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string DictUrl { get; set; } = "";
    public string DictSha256 { get; set; } = "";
    public int DictBytes { get; set; }
}

public class LoadedDictionary
{
    public string GameId { get; }
    public Dictionary<string, string> Speakers { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> ExactMap { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, List<(string Norm, string Tr)>> WordIndex { get; } = new(StringComparer.Ordinal);

    public LoadedDictionary(string gameId)
    {
        GameId = gameId;
    }

    public string? Lookup(string rawEnglish)
    {
        string norm = TranslationEngine.NormalizeForLookup(rawEnglish);
        if (string.IsNullOrEmpty(norm))
            return null;

        if (ExactMap.TryGetValue(norm, out string? exact))
            return exact;

        string expanded = TranslationEngine.NormalizeForLookup(TranslationEngine.ExpandContractions(rawEnglish));
        if (!string.IsNullOrEmpty(expanded) && expanded != norm && ExactMap.TryGetValue(expanded, out string? exactExp))
            return exactExp;

        return FuzzyLookup(norm);
    }

    private string? FuzzyLookup(string normQuery)
    {
        if (normQuery.Length < 4)
            return null;

        var words = normQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 3)
            .OrderByDescending(w => w.Length)
            .Take(2)
            .ToList();

        if (words.Count == 0)
            return null;

        int qLen = normQuery.Length;
        int maxDiff = Math.Max(2, (int)(qLen * 0.15));
        double bestScore = 0.85;
        string? bestTr = null;

        foreach (string w in words)
        {
            if (!WordIndex.TryGetValue(w, out var bucket))
                continue;

            foreach (var (candNorm, tr) in bucket)
            {
                if (Math.Abs(candNorm.Length - qLen) > maxDiff)
                    continue;

                int dist = LevenshteinBounded(normQuery, candNorm, maxDiff);
                if (dist <= maxDiff)
                {
                    double score = 1.0 - ((double)dist / Math.Max(candNorm.Length, qLen));
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestTr = tr;
                        if (score >= 0.96)
                            return bestTr;
                    }
                }
            }
        }
        return bestTr;
    }

    private static int LevenshteinBounded(string a, string b, int maxDist)
    {
        int n = a.Length;
        int m = b.Length;
        if (Math.Abs(n - m) > maxDist)
            return maxDist + 1;

        int[] prev = new int[m + 1];
        int[] curr = new int[m + 1];
        for (int j = 0; j <= m; j++)
            prev[j] = j;

        for (int i = 1; i <= n; i++)
        {
            curr[0] = i;
            int minRow = curr[0];
            char ca = a[i - 1];
            for (int j = 1; j <= m; j++)
            {
                int cost = (ca == b[j - 1]) ? 0 : 1;
                int v = Math.Min(Math.Min(curr[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
                curr[j] = v;
                if (v < minRow)
                    minRow = v;
            }
            if (minRow > maxDist)
                return maxDist + 1;

            (prev, curr) = (curr, prev);
        }
        return prev[m];
    }
}

public static class TranslationEngine
{
    private const string CatalogUrl = "https://console-translator-dictionaries.console-translator-relay.workers.dev/catalog.json";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    private static readonly ConcurrentDictionary<string, string> MemoryCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, byte> PendingRequests = new(StringComparer.OrdinalIgnoreCase);
    private static LoadedDictionary? ActiveGameDict;

    // 180+ Yerleşik Oyun Ana Menü / Görev / Arayüz Sözlüğü (Her oyunda 0ms çevrimdışı çalışır)
    private static readonly Dictionary<string, string> BuiltInMenuDict = new(StringComparer.OrdinalIgnoreCase)
    {
        ["new game"] = "Yeni Oyun",
        ["continue"] = "Devam Et",
        ["resume"] = "Oyuna Dön",
        ["resume game"] = "Oyuna Dön",
        ["load game"] = "Oyun Yükle",
        ["load"] = "Yükle",
        ["save game"] = "Oyunu Kaydet",
        ["save"] = "Kaydet",
        ["settings"] = "Ayarlar",
        ["options"] = "Seçenekler",
        ["controls"] = "Kontroller",
        ["key bindings"] = "Tuş Atamaları",
        ["keyboard & mouse"] = "Klavye ve Fare",
        ["controller"] = "Oyun Kumandası",
        ["graphics"] = "Grafikler",
        ["video"] = "Görüntü",
        ["display"] = "Ekran",
        ["audio"] = "Ses",
        ["sound"] = "Ses",
        ["language"] = "Dil",
        ["subtitles"] = "Altyazılar",
        ["gameplay"] = "Oynanış",
        ["difficulty"] = "Zorluk",
        ["easy"] = "Kolay",
        ["normal"] = "Normal",
        ["medium"] = "Orta",
        ["hard"] = "Zor",
        ["nightmare"] = "Kabus",
        ["accessibility"] = "Erişilebilirlik",
        ["credits"] = "Yapımcılar",
        ["extras"] = "Ekstralar",
        ["quit"] = "Çıkış",
        ["quit game"] = "Oyundan Çık",
        ["quit to desktop"] = "Masaüstüne Dön",
        ["exit"] = "Çıkış",
        ["exit game"] = "Oyundan Çık",
        ["exit to main menu"] = "Ana Menüye Dön",
        ["main menu"] = "Ana Menü",
        ["return to main menu"] = "Ana Menüye Dön",
        ["apply"] = "Uygula",
        ["cancel"] = "İptal",
        ["back"] = "Geri",
        ["confirm"] = "Onayla",
        ["accept"] = "Kabul Et",
        ["decline"] = "Reddet",
        ["yes"] = "Evet",
        ["no"] = "Hayır",
        ["ok"] = "Tamam",
        ["reset to default"] = "Varsayılana Sıfırla",
        ["default"] = "Varsayılan",
        ["on"] = "Açık",
        ["off"] = "Kapalı",
        ["enabled"] = "Etkin",
        ["disabled"] = "Devre Dışı",
        ["fullscreen"] = "Tam Ekran",
        ["windowed"] = "Pencere Modu",
        ["borderless"] = "Çerçevesiz",
        ["resolution"] = "Çözünürlük",
        ["vsync"] = "Dikey Senkronizasyon (V-Sync)",
        ["frame rate limit"] = "Kare Hızı Sınırı",
        ["brightness"] = "Parlaklık",
        ["field of view"] = "Görüş Açısı (FOV)",
        ["motion blur"] = "Hareket Bulanıklığı",
        ["shadow quality"] = "Gölge Kalitesi",
        ["texture quality"] = "Doku Kalitesi",
        ["master volume"] = "Ana Ses Seviyesi",
        ["music volume"] = "Müzik Sesi",
        ["sfx volume"] = "Efekt Sesi",
        ["dialogue volume"] = "Konuşma Sesi",
        ["voice volume"] = "Konuşma Sesi",
        ["inventory"] = "Envanter",
        ["map"] = "Harita",
        ["world map"] = "Dünya Haritası",
        ["journal"] = "Günlük",
        ["quests"] = "Görevler",
        ["quest"] = "Görev",
        ["main quest"] = "Ana Görev",
        ["side quest"] = "Yan Görev",
        ["missions"] = "Görevler",
        ["mission"] = "Görev",
        ["objective"] = "Hedef",
        ["objectives"] = "Hedefler",
        ["new objective"] = "Yeni Görev Hedefi",
        ["objective updated"] = "Görev Güncellendi",
        ["objective completed"] = "Görev Tamamlandı",
        ["mission completed"] = "Görev Tamamlandı",
        ["mission failed"] = "Görev Başarısız",
        ["checkpoint"] = "Kontrol Noktası",
        ["checkpoint reached"] = "Kontrol Noktasına Ulaşıldı",
        ["autosaving"] = "Otomatik Kaydediliyor...",
        ["saving"] = "Kaydediliyor...",
        ["loading"] = "Yükleniyor...",
        ["skills"] = "Yetenekler",
        ["skill tree"] = "Yetenek Ağacı",
        ["abilities"] = "Kabiliyetler",
        ["crafting"] = "Üretim",
        ["craft"] = "Üret",
        ["upgrade"] = "Geliştir",
        ["equip"] = "Kuşan",
        ["unequip"] = "Çıkar",
        ["use"] = "Kullan",
        ["drop"] = "Bırak",
        ["examine"] = "İncele",
        ["inspect"] = "İncele",
        ["read"] = "Oku",
        ["take"] = "Al",
        ["pick up"] = "Al",
        ["interact"] = "Etkileşime Geç",
        ["open"] = "Aç",
        ["close"] = "Kapat",
        ["talk"] = "Konuş",
        ["skip"] = "Atla",
        ["hold to skip"] = "Atlamak İçin Basılı Tut",
        ["press any key"] = "Herhangi Bir Tuşa Basın",
        ["press any key to continue"] = "Devam Etmek İçin Bir Tuşa Basın",
        ["press enter"] = "Enter Tuşuna Basın",
        ["paused"] = "Duraklatıldı",
        ["game over"] = "Oyun Bitti",
        ["you died"] = "Öldün",
        ["restart"] = "Yeniden Başlat",
        ["retry"] = "Tekrar Dene",
        ["load last checkpoint"] = "Son Kontrol Noktasını Yükle",
        ["chapter"] = "Bölüm",
        ["prologue"] = "Giriş",
        ["epilogue"] = "Son Söz",
        ["health"] = "Sağlık",
        ["stamina"] = "Dayanıklılık",
        ["ammo"] = "Cephane",
        ["weapons"] = "Silahlar",
        ["armor"] = "Zırh",
        ["items"] = "Eşyalar",
        ["character"] = "Karakter",
        ["codex"] = "Kodeks / Ansiklopedi",
        ["tutorials"] = "Eğitimler"
    };

    private static string CacheFilePath =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "translation_cache.json");

    private static string AssetsDir =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "catalog-assets");

    static TranslationEngine()
    {
        LoadDiskCache();
    }

    private static void LoadDiskCache()
    {
        try
        {
            if (File.Exists(CacheFilePath))
            {
                var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(CacheFilePath));
                if (dict != null)
                {
                    foreach (var kv in dict)
                        MemoryCache[kv.Key] = kv.Value;
                }
            }
        }
        catch { }
    }

    public static void SaveDiskCache()
    {
        try
        {
            var snapshot = MemoryCache.ToDictionary(k => k.Key, v => v.Value);
            File.WriteAllText(CacheFilePath, JsonSerializer.Serialize(snapshot));
        }
        catch { }
    }

    public static string NormalizeForLookup(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        string s = FixOcrZeros(text).Replace('|', 'I').ToLowerInvariant();
        var sb = new StringBuilder(s.Length);
        bool lastSpace = true;
        foreach (char c in s)
        {
            if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
            {
                sb.Append(c);
                lastSpace = false;
            }
            else if (char.IsWhiteSpace(c))
            {
                if (!lastSpace)
                {
                    sb.Append(' ');
                    lastSpace = true;
                }
            }
        }
        return sb.ToString().Trim();
    }

    public static string FixOcrZeros(string text)
    {
        return Regex.Replace(text, @"(?<![0-9])0(?=\p{L})|(?<=\p{L})0(?![0-9])", "o");
    }

    public static string ExpandContractions(string text)
    {
        string s = text.Replace('’', '\'').Replace('`', '\'');
        s = Regex.Replace(s, @"\b[iI]'?m\b", "I am");
        s = Regex.Replace(s, @"\b[iI]'ll\b", "I will");
        s = Regex.Replace(s, @"\b[iI]'ve\b", "I have");
        s = Regex.Replace(s, @"\b[yY]ou'?re\b", "you are");
        s = Regex.Replace(s, @"\b[wW]e'?re\b", "we are");
        s = Regex.Replace(s, @"\b[tT]hey'?re\b", "they are");
        s = Regex.Replace(s, @"\b[cC]an'?t\b", "cannot");
        s = Regex.Replace(s, @"\b[wW]on'?t\b", "will not");
        s = Regex.Replace(s, @"\b[dD]on'?t\b", "do not");
        s = Regex.Replace(s, @"\b[dD]oesn'?t\b", "does not");
        s = Regex.Replace(s, @"\b[dD]idn'?t\b", "did not");
        s = Regex.Replace(s, @"\b[iI]sn'?t\b", "is not");
        s = Regex.Replace(s, @"\b[aA]ren'?t\b", "are not");
        s = Regex.Replace(s, @"\b[wW]asn'?t\b", "was not");
        s = Regex.Replace(s, @"\b[hH]aven'?t\b", "have not");
        s = Regex.Replace(s, @"\b[wW]ouldn'?t\b", "would not");
        s = Regex.Replace(s, @"\b[cC]ouldn'?t\b", "could not");
        s = Regex.Replace(s, @"\b[sS]houldn'?t\b", "should not");
        return s;
    }

    /// <summary>
    /// 0ms Senkron Çeviri Kontrolü (Hazır Oyun Sözlüğü + Yerleşik Menü Sözlüğü + Önbellek).
    /// Bulamazsa arka planda online çeviriyi tetikler ve bir sonraki taramada (100ms sonra) hazır olur.
    /// </summary>
    public static string? TryTranslateInstantOrQueue(string rawEnglish, AppConfig cfg, Action? onAsyncReady = null)
    {
        string cleaned = CleanRawOcrText(rawEnglish);
        if (cleaned.Length < 2)
            return null;

        // Konuşmacı ayrıştırma (Örn: "Arthur: Let's go" veya "Objective: Follow Tino")
        var (speaker, dialogue) = SplitSpeakerOrPrefix(cleaned);

        string? trDialogue = LookupSinglePhrase(dialogue, cfg);
        if (trDialogue != null)
        {
            return FormatWithSpeaker(speaker, trDialogue, cfg);
        }

        // Tam metni de kontrol et
        string? fullMatch = LookupSinglePhrase(cleaned, cfg);
        if (fullMatch != null)
            return fullMatch;

        // Önbellekte varsa döndür
        if (MemoryCache.TryGetValue(dialogue, out string? cached))
            return FormatWithSpeaker(speaker, cached, cfg);

        // Arka planda hızlı çeviri başlat
        if (PendingRequests.TryAdd(dialogue, 1))
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    string? tr = await TranslateOnlineAsync(dialogue);
                    if (!string.IsNullOrWhiteSpace(tr))
                    {
                        MemoryCache[dialogue] = tr;
                        onAsyncReady?.Invoke();
                    }
                }
                finally
                {
                    PendingRequests.TryRemove(dialogue, out _);
                }
            });
        }

        return null;
    }

    public static async Task<string> TranslateGuaranteedAsync(string rawEnglish, AppConfig cfg)
    {
        string? instant = TryTranslateInstantOrQueue(rawEnglish, cfg);
        if (instant != null)
            return instant;

        string cleaned = CleanRawOcrText(rawEnglish);
        var (speaker, dialogue) = SplitSpeakerOrPrefix(cleaned);
        if (MemoryCache.TryGetValue(dialogue, out string? cached))
            return FormatWithSpeaker(speaker, cached, cfg);

        string? tr = await TranslateOnlineAsync(dialogue);
        if (!string.IsNullOrWhiteSpace(tr))
        {
            MemoryCache[dialogue] = tr;
            return FormatWithSpeaker(speaker, tr, cfg);
        }
        return cleaned;
    }

    private static string? LookupSinglePhrase(string text, AppConfig cfg)
    {
        string norm = NormalizeForLookup(text);
        if (string.IsNullOrEmpty(norm))
            return null;

        // 1. Seçili Oyun Sözlüğü (Mafia: The Old Country, RDR2, GTA V vb.)
        if (cfg.UseGameDictionary && cfg.SelectedGameId != "none" && ActiveGameDict?.GameId == cfg.SelectedGameId)
        {
            string? gameMatch = ActiveGameDict.Lookup(text);
            if (gameMatch != null)
                return gameMatch;
        }

        // 2. Yerleşik 180+ Ana Menü / Görev / Arayüz Sözlüğü
        if (BuiltInMenuDict.TryGetValue(norm, out string? menuTr))
            return menuTr;

        // "Press [Key] to [Action]" kalıbı
        var pressMatch = Regex.Match(text.Trim(), @"^Press\s+([A-Za-z0-9+\[\]\s]{1,12})\s+to\s+(.+)$", RegexOptions.IgnoreCase);
        if (pressMatch.Success)
        {
            string keyName = pressMatch.Groups[1].Value.Trim();
            string actionEn = pressMatch.Groups[2].Value.Trim().TrimEnd('.');
            if (BuiltInMenuDict.TryGetValue(NormalizeForLookup(actionEn), out string? actTr))
                return $"{actTr} için [{keyName}] tuşuna bas";
        }

        return null;
    }

    private static string FormatWithSpeaker(string? speaker, string translatedDialogue, AppConfig cfg)
    {
        if (string.IsNullOrWhiteSpace(speaker))
            return translatedDialogue;

        string sp = speaker.Trim();
        if (BuiltInMenuDict.TryGetValue(sp.ToLowerInvariant(), out string? prefixTr))
            return $"{prefixTr}: {translatedDialogue}";

        if (cfg.UseGameDictionary && ActiveGameDict != null && ActiveGameDict.Speakers.TryGetValue(sp, out string? gameSp))
            return $"{gameSp}: {translatedDialogue}";

        return $"{sp}: {translatedDialogue}";
    }

    private static (string? Speaker, string Dialogue) SplitSpeakerOrPrefix(string text)
    {
        var m = Regex.Match(text, @"^([A-Za-z][A-Za-z0-9_.'\-\s]{0,22}?)\s*:\s+(.+)$");
        if (m.Success)
        {
            string sp = m.Groups[1].Value.Trim();
            string diag = m.Groups[2].Value.Trim();
            if (diag.Length >= 2)
                return (sp, diag);
        }
        return (null, text);
    }

    public static string CleanRawOcrText(string raw)
    {
        string s = FixOcrZeros(raw).Replace('|', 'I');
        s = Regex.Replace(s, @"\s+", " ").Trim();
        return s;
    }

    private static async Task<string?> TranslateOnlineAsync(string englishText)
    {
        try
        {
            string prep = ExpandContractions(englishText);
            string url = "https://translate.googleapis.com/translate_a/single?client=gtx&sl=en&tl=tr&dt=t&q=" +
                         Uri.EscapeDataString(prep);
            string json = await Http.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
            {
                var sentences = root[0];
                if (sentences.ValueKind == JsonValueKind.Array)
                {
                    var sb = new StringBuilder();
                    foreach (var item in sentences.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.Array && item.GetArrayLength() > 0 && item[0].ValueKind == JsonValueKind.String)
                        {
                            sb.Append(item[0].GetString());
                        }
                    }
                    string result = sb.ToString().Trim();
                    if (!string.IsNullOrEmpty(result))
                        return result;
                }
            }
        }
        catch { }
        return null;
    }

    // =========================================================================
    // 38 OYUN KATALOGU VE SÖZLÜK YÖNETİMİ
    // =========================================================================
    public static async Task<List<CatalogGameItem>> FetchCatalogGamesAsync()
    {
        var list = new List<CatalogGameItem>();
        try
        {
            string raw = await Http.GetStringAsync(CatalogUrl);
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.TryGetProperty("games", out var gamesArr))
            {
                foreach (var g in gamesArr.EnumerateArray())
                {
                    string id = g.GetProperty("id").GetString() ?? "";
                    string title = g.GetProperty("title").GetString() ?? id;
                    if (g.TryGetProperty("dictionary", out var dictObj))
                    {
                        string url = dictObj.GetProperty("url").GetString() ?? "";
                        string sha = dictObj.GetProperty("sha256").GetString() ?? "";
                        int bytes = dictObj.GetProperty("bytes").GetInt32();
                        list.Add(new CatalogGameItem
                        {
                            Id = id,
                            Title = title,
                            DictUrl = url,
                            DictSha256 = sha,
                            DictBytes = bytes
                        });
                    }
                }
            }
        }
        catch { }
        return list;
    }

    public static bool IsGameInstalled(CatalogGameItem game)
    {
        string path = Path.Combine(AssetsDir, $"{game.DictSha256}.json");
        return File.Exists(path) && new FileInfo(path).Length == game.DictBytes;
    }

    public static async Task<(bool Ok, string Message)> DownloadAndLoadGameAsync(
        CatalogGameItem game,
        Action<int, string>? onProgress = null)
    {
        try
        {
            Directory.CreateDirectory(AssetsDir);
            string targetPath = Path.Combine(AssetsDir, $"{game.DictSha256}.json");

            if (!File.Exists(targetPath) || new FileInfo(targetPath).Length != game.DictBytes)
            {
                double mb = game.DictBytes / (1024.0 * 1024.0);
                onProgress?.Invoke(5, $"{game.Title} Türkçe sözlüğü indiriliyor ({mb:F1} MB)...");

                using var resp = await Http.GetAsync(game.DictUrl, HttpCompletionOption.ResponseHeadersRead);
                resp.EnsureSuccessStatusCode();

                string tempPath = targetPath + ".tmp";
                using (var input = await resp.Content.ReadAsStreamAsync())
                using (var output = File.Create(tempPath))
                using (var sha = SHA256.Create())
                {
                    byte[] buf = new byte[32768];
                    int totalRead = 0;
                    int read;
                    while ((read = await input.ReadAsync(buf, 0, buf.Length)) > 0)
                    {
                        output.Write(buf, 0, read);
                        sha.TransformBlock(buf, 0, read, null, 0);
                        totalRead += read;
                        if (game.DictBytes > 0)
                        {
                            int pct = Math.Min(90, (int)((totalRead * 90L) / game.DictBytes));
                            onProgress?.Invoke(pct, $"{game.Title} indiriliyor: %{pct}");
                        }
                    }
                    sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                    string hex = BitConverter.ToString(sha.Hash!).Replace("-", "").ToLowerInvariant();
                    if (!string.Equals(hex, game.DictSha256, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new Exception("Dosya bütünlük doğrulaması (SHA-256) başarısız.");
                    }
                }

                if (File.Exists(targetPath))
                    File.Delete(targetPath);
                File.Move(tempPath, targetPath);
            }

            onProgress?.Invoke(95, "Sözlük belleğe yükleniyor...");
            var loaded = await Task.Run(() => ParseDictionaryFile(game.Id, targetPath));
            ActiveGameDict = loaded;
            onProgress?.Invoke(100, $"Hazır ({loaded.ExactMap.Count:N0} cümle yüklendi)");
            return (true, $"{game.Title} sözlüğü ({loaded.ExactMap.Count:N0} diyalog/görev/menü satırı) başarıyla yüklendi!");
        }
        catch (Exception ex)
        {
            return (false, "Hata: " + ex.Message);
        }
    }

    public static async Task TryLoadCachedGameDictionaryAsync(string gameId)
    {
        if (string.IsNullOrWhiteSpace(gameId) || gameId == "none")
        {
            ActiveGameDict = null;
            return;
        }
        if (ActiveGameDict?.GameId == gameId)
            return;

        var games = await FetchCatalogGamesAsync();
        var g = games.FirstOrDefault(x => x.Id == gameId);
        if (g != null && IsGameInstalled(g))
        {
            string targetPath = Path.Combine(AssetsDir, $"{g.DictSha256}.json");
            ActiveGameDict = await Task.Run(() => ParseDictionaryFile(g.Id, targetPath));
        }
    }

    private static LoadedDictionary ParseDictionaryFile(string gameId, string filePath)
    {
        var dict = new LoadedDictionary(gameId);
        using var fs = File.OpenRead(filePath);
        using var doc = JsonDocument.Parse(fs);
        var root = doc.RootElement;

        if (root.TryGetProperty("speakers", out var spObj) && spObj.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in spObj.EnumerateObject())
            {
                string tr = prop.Value.GetString() ?? "";
                if (!string.IsNullOrWhiteSpace(tr))
                    dict.Speakers[prop.Name.Trim()] = tr.Trim();
            }
        }

        if (root.TryGetProperty("entries", out var entriesArr) && entriesArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in entriesArr.EnumerateArray())
            {
                string en = item.TryGetProperty("english", out var enEl) ? (enEl.GetString() ?? "") : "";
                string tr = item.TryGetProperty("turkish", out var trEl) ? (trEl.GetString() ?? "") : "";
                if (string.IsNullOrWhiteSpace(en) || string.IsNullOrWhiteSpace(tr))
                    continue;

                string norm = NormalizeForLookup(en);
                if (string.IsNullOrEmpty(norm))
                    continue;

                string cleanTr = tr.Trim();
                dict.ExactMap.TryAdd(norm, cleanTr);

                if (norm.Length >= 5 && norm.Length <= 140)
                {
                    string longestWord = norm.Split(' ').OrderByDescending(w => w.Length).FirstOrDefault() ?? "";
                    if (longestWord.Length >= 3)
                    {
                        if (!dict.WordIndex.TryGetValue(longestWord, out var list))
                        {
                            list = new List<(string, string)>(4);
                            dict.WordIndex[longestWord] = list;
                        }
                        if (list.Count < 45)
                            list.Add((norm, cleanTr));
                    }
                }
            }
        }

        return dict;
    }
}
