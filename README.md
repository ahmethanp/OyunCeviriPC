# 🎮 OyunCeviriPC — Canlı Tam Ekran Oyun, Menü, Görev ve Altyazı Çevirici (Windows)

**OyunCeviriPC**, Windows 10/11 üzerinde çalışan herhangi bir PC oyununda ekranda görünen **ana menü maddelerini, görev hedeflerini, envanter/eşya açıklamalarını, etkileşim ipuçlarını ve diyalog altyazılarını** gerçek zamanlı olarak okuyup, **İngilizce metinlerin tam kendi koordinatları üzerine (In-Place Overlay)** Türkçe çevirilerini yerleştiren açık kaynaklı bir masaüstü uygulamasıdır.

---

## ✨ Öne Çıkan Özellikler

- **📍 Her Yazıyı Kendi Yerinde Çevirme (In-Place Overlay):** Sadece ekranın altındaki altyazıları değil; ana menüdeki (`New Game`, `Continue`, `Options`), ekranın köşelerindeki görev hedeflerini (`Objective: Follow Tino`) ve envanter ekranındaki açıklamaları ekranda bulundukları **tam koordinatın üstüne** Türkçe kutu olarak yazar.
- **⚡ Sıfır Titreme & Kendi Çevirisini Tekrar Okumama (`WDA_EXCLUDEFROMCAPTURE`):** Windows 10/11 `SetWindowDisplayAffinity` teknolojisi sayesinde çeviri kutularını siz görürsünüz ancak ekran yakalama motoru arkadaki orijinal oyun görüntüsünü görür. Kutular yanıp sönmez ve uygulama kendi yazdığı Türkçeyi tekrar okumaz.
- **🖱️ Click-Through (Tıklama Geçirgenliği):** Ekran üzerindeki Türkçe çeviri kutuları mouse tıklamalarını engellemez; tüm tıklamalar doğrudan arkadaki oyuna geçer.
- **🦎 Bukalemun Modu (Otomatik Arka Plan Rengi Algılama):** Her metnin çevresindeki pikselleri analiz ederek oyunun menü veya HUD arka plan rengini otomatik kopyalar.
- **📚 3 Katmanlı Hızlı Çeviri Motoru:**
  1. **38 Hazır Oyun Sözlüğü (`0 ms` Gecikme):** *Mafia: The Old Country* (35.902 satır), *Red Dead Redemption 2*, *GTA V*, *Silent Hill 2 Remake*, *Alan Wake 2* dahil 38 oyunun hazır Türkçe sözlüğünü uygulama içinden tek tıkla indirip kullanabilme.
  2. **Yerleşik 180+ Oyun Menüsü & Görev Sözlüğü (`0 ms` Çevrimdışı):** Tüm oyunlarda ortak olan menü, ayar, kontrol ve `Press [Key] to [Action]` kalıplarını anında çevirir.
  3. **Online Çeviri + Önbellek:** Sözlükte bulunmayan cümleleri anında çevirip yerel önbelleğe kaydeder.
- **🎨 Tam Görünüm Özelleştirme:** Okuma bölgesi (X, Y, Genişlik, Yükseklik), yazı boyutu ölçeği, yazı rengi/opaklığı ve arka plan rengi/opaklığı ayarı.

---

## ⌨️ Oyun İçi Kısayol Tuşları

| Kısayol | İşlev |
| :--- | :--- |
| **`F9`** | Canlı sürekli çeviriyi **Başlat / Durdur** |
| **`F8`** | **Anlık Tam Ekran Tarama** (Ana menüde, envanterde veya görev ekranında tüm ekranı çevirir) |
| **`F10`** | Ekran üzerindeki çeviri kutularını **Temizle / Gizle** |

---

## 🚀 Kurulum ve Derleme

### Gereksinimler
- Windows 10 (19041+) veya Windows 11
- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)

### Derleme ve Çalıştırma
```powershell
git clone https://github.com/ahmethanp/OyunCeviriPC.git
cd OyunCeviriPC
dotnet build -c Release
.\Baslat.bat
```

> **💡 Önemli İpucu:** Oyunlarda çeviri katmanının (Overlay) tam üstte görünebilmesi için oyunun görüntü ayarlarından ekran modunu **Çerçevesiz Tam Ekran (Borderless Windowed / Windowed Fullscreen)** olarak ayarlayın.
