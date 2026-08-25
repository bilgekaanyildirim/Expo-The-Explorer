# Anahtar ekonomisi — yol haritası

<!-- 2026-08-25. Kullanıcının "eski kalp UI'ın oraya anahtar gelicek..." isteği
     üzerine çıkarıldı. Her adım tek bir preflight + APPROVE döngüsüdür;
     adımlar sırayla yürütülür ve bir adım bitmeden sonrakine geçilmez. Bir
     adım bittiğinde buradaki kutusu işaretlenir.

     Bu dosya plan tutanağıdır, mimari otorite DEĞİL — kalıcı kararlar
     Adım 1'de `.claude/decisions.md`'ye D-065 olarak yazılacak.

     Bağlam: D-064 canları GÜNLÜK bir kaynağa çevirdi (her gün 3 kalp, kayıt
     yok). Anahtar onun yerine geçen META kaynaktır: kaydedilir, gerçek
     zamanda yenilenir ve oynamayı kapılar. İkisi birbirini okumaz. -->

## 1. Ne inşa ediyoruz

Kalpler bir günün içindeki hata payı. **Anahtar** ise o günü oynama hakkı:

| | Kalp (D-064) | Anahtar (bu plan) |
|---|---|---|
| Kapsam | Tek gün | Oyuncu, oturumlar arası |
| Kaynak | Her gün 3 ile başlar | 5 tavan, 30 dakikada +1 |
| Kaybı | Yanlış teslim / süre aşımı | Kaybedilen günden çıkarken |
| Kaydedilir mi | Hayır | Evet (save v7) |
| Bitince | Gün biter, Continue teklif edilir | Yeni gün başlatılamaz / retry edilemez |

**Kullanıcının kilitlediği dört kural** (bu turda soruldu, cevaplandı):

1. **Tavan 5.** Yeni oyuncu 5 anahtarla başlar; doluyken sayaç durur.
2. **40 Gem tavana doldurur** (üstüne eklemez) — tavan hiç aşılmaz, kural tek.
3. **Anahtar yalnızca kaybedilen günden çıkarken gider.** Retry veya Ana Menü
   → −1. Gem/para ile Continue **bedava** (günden çıkmıyorsun). Güne başlamak
   bir **kapı**, bir bedel değil: >0 anahtar şartı aranır ama harcanmaz.
4. **30 dakikalık sayaç oyun kapalıyken de işler**, geri-alma korumasıyla.

## 2. Ölçülen gerçekler (varsayım değil)

- **Projede bugün hiçbir yerde `DateTime` kullanılmıyor** (`Scripts/` altında
  `DateTime.UtcNow`/`Now` için sıfır sonuç). Bu sistem gerçek saate ilk
  bağımlılığı getiriyor, o yüzden saat tek bir dikişin arkasında tutulur.
- Eski can göstergesi `HUDCanvas.prefab` içinde `UpperPanel/HealthUI`
  (`HeartImage` + `HealthText`) ve iki sahnede birden görünüyor — anahtarın
  gideceği yer tam olarak burası. **Bu obje SİLİNMEYECEK**, Adım 3'te yeniden
  adlandırılıp sprite'ı değiştirilecek. (D-064 raporunda "sil" denmişti; o
  talimat bu planla geri alındı.)
- `Wallet.TrySpendGems(int)` zaten var ve Gem'in tek yazarı — 40 Gem oradan
  geçecek, yeni bir yazma yolu açılmayacak.
- `LivesSystem → ProgressionSystem` oku zaten mevcut; `KeySystem` birebir aynı
  şekli alır, ters ok yok.

## 3. Mimari kararlar (Adım 1'de D-065 olarak yazılacak)

- **Anahtar sayısı `GameState`'e KONMAZ.** `KeyManager` kendi `Keys` alanını ve
  kendi `KeysChanged` olayını tutar; alan `private`, yazan yalnızca bu sınıf.
  Bu, `Lives`'ın durumundan daha güçlü: `GameState.Lives`'ın public setter'ı
  var ve tek-yazar kuralı yoruma dayanıyor, burada **derleyici** tutuyor.
  Ayrıca anahtar bir GÜN durumu değil — `GameState` günün merkezi durumu.
- **Saat enjekte edilir**: `Func<DateTime>`, varsayılanı `() => DateTime.UtcNow`.
  Süsleme değil — doğrudan saat okuyan bir yenilenme kuralının hiçbir vakası
  test edilemez, ve buradaki vakaların hepsi zaman vakası.
- **`Refresh(now)` idempotent ve ucuzdur**, her kapı kontrolünden önce çağrılır.
  Böylece doğruluk bir tick'in var olmasına ASLA bağlı olmaz; tick yalnızca
  ekranı tazeler.
- **Yenilenme artığı korunur**: `steps = geçen / aralık` kadar anahtar verilir
  ve çapa `steps * aralık` kadar ilerler — 29 dakikalık birikim çöpe gitmez.
  Tavana vurulduğunda çapa `now`'a çekilir (dolu sayaç işlemez).
- **Geri-alma koruması**: `now < çapa` ise çapa `now`'a çekilir. Bedava anahtar
  yok, ceza da yok. İleri alma istemci tarafında çözülemez ve kabul edilir.
- **Kayıtta `-1` "yok" işaretidir.** `Keys` için 0 güvenli varsayılan değil
  (0 = kilitli oyuncu, v3'teki `Lives` ile aynı problem) ama tavanı
  `PlayerProfileStore` bilemez — config referansı yok ve olmamalı. Store `-1`
  yazar, `KeyManager.ApplyPersisted` onu tavana çevirir. Store payload'ın
  anlamına bugünkü kadar cahil kalır ve `System.DateTime` de oraya girmez.

## 4. Adımlar

Her adım oyunu **çalışır ve tutarlı** bir durumda bırakır; hiçbir adım yarım
kapı bırakmaz.

- [x] **Adım 1 — Model.** ✅ 2026-08-25 (D-065). `KeyConfig` (5 / 30 dk / 40 gem, `[CreateAssetMenu]`)
      + `KeySystem` asmdef + `KeyManager` + `KeySystemTests`. Saf C#, oyunda
      hiçbir şey bunu referans etmez.
      *Sonuç:* davranış değişmez, sıfır risk. Yenilenme matematiği, tavan,
      geri-alma, harcama ve Gem doldurma tamamen test altına girer.
      *Yan iş:* `CLAUDE.md`'nin D-064'ten kalan yanlış "canlar kalıcıdır"
      metni düzeltilir (bekleyen borç).

- [x] **Adım 2 — Kalıcılık ve oturuma bağlama.** ✅ 2026-08-25 (D-066). `PlayerProfile` v7
      (`Keys`, `LastKeyRegenUtcTicks`) + store migration + `GameSession`
      `KeyManager`'ı kurar/yükler/kaydeder + `GameManager` ve `MainScreenRoot`
      `keyConfig` alanı + `MainScreenSessionSetup` onu doldurur.
      *Sonuç:* anahtar gerçekten var, kaydediliyor ve kapalıyken bile
      yenileniyor — ama hâlâ görünmez ve hiçbir kapı kapalı değil.
      *El adımı:* `KeyConfig.asset`'i Unity'de menüden oluştur.

- [x] **Adım 3 — HUD göstergesi.** ✅ 2026-08-25 (D-067). `HudWalletSource` anahtar iletimi kazanır,
      `KeysView` yazılır, `HudCanvasPrefabSetup` onu bağlar.
      *Sonuç:* iki ekranda da "3/5" okunur ve dakikalar geçtikçe kendiliğinden
      artar. Hâlâ hiçbir şey engellenmiyor.
      *El adımı:* `HealthUI` → `KeysUI` olarak yeniden adlandır, sprite'ı
      anahtara çevir, `KeysView` ekle.

- [x] **Adım 4 — Harcama.** ✅ 2026-08-25 (D-068). Kaybedilen günden çıkarken −1: `GameManager`'ın
      abandon yolu ve retry yolu. **Henüz engelleme yok** — 0'da kalır, altına
      inmez.
      *Sonuç:* oyuncu sayacın düştüğünü ve geri dolduğunu görür. Kilitlenme
      riski olmadan gerçek davranış izlenebilir.

- [x] **Adım 5 — Kontroller + popup.** ✅ 2026-08-25 (D-069). `NoKeysPopupView` (geri sayım + 40 Gem
      butonu), `MainScreenView`'ın Play kontrolü, `GameOverPopupView`'ın Retry
      kontrolü. Popup ve kontroller **aynı adımda** gelir, çünkü biri
      diğerinden önce girerse oyuncu sıkışır.
      **HİÇBİR BUTON DEVRE DIŞI BIRAKILMAZ** (kullanıcı, 2026-08-25): kontrol
      `interactable = false` değil, TIKLAMA ANINDA yapılır — anahtar varsa
      devam, yoksa popup açılır. Gri bir buton oyuncuya nedenini söylemez;
      popup hem sebebi söyler hem de iki çıkış yolu sunar (bekle ya da 40 Gem).
      Ana Menü hiçbir koşulda engellenmez, yoksa 0 anahtarda softlock olur.
      *Sonuç:* özellik tamam. `CLAUDE.md` Bölüm 3'e anahtar kuralı yazılır.
      *El adımı:* popup'ı iki sahneye de kur ve referanslarını sürükle.

## 5. Kapsam dışı (bilerek)

- Sunucu tarafı zaman doğrulaması. İleri alınan cihaz saati istemcide
  çözülemez; oyuncu yalnızca kendi temposunu bozar, para kazancı yoktur.
- Anahtar için ayrı bir mağaza/paket ekranı. Tek satın alma yolu popup'taki
  40 Gem butonudur.
- Reklam izleyerek anahtar. Tasarlanmadı; istenirse Adım 5'in üstüne oturur.
- Tavanın yükseltilebilir olması. Bugün `KeyConfig`'de sabit bir sayı; meta
  bir yükseltme haline gelirse kaydedilen bir alan ister (D-064'ün `MaxLives`
  için yazdığı notun aynısı).
