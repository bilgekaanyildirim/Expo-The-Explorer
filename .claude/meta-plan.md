# Meta oyun (Expo Alanı) — yol haritası

<!-- 2026-08-19. Kullanıcının "meta oyunu implement etmeye başlayalım" isteği
     üzerine, Art/Meta1 sprite seti okunarak çıkarıldı. Her adım tek bir
     preflight + APPROVE döngüsüdür; adımlar sırayla yürütülür ve bir adım
     bitmeden sonrakine geçilmez. Bir adım bittiğinde buradaki kutusu
     işaretlenir.

     Bu dosya plan tutanağıdır, mimari otorite DEĞİL — kalıcı kararlar
     Adım 0'da `.claude/decisions.md`'ye D-015 olarak yazılacak.

     GDD dayanağı: ExpoTheExplorer/CLAUDE.md "Main Screen / Scene Flow":
     "The main screen is a navigation shell on purpose... Meta content
     (day-select map, upgrades, shop) is not designed yet — build the design
     first, then hang it here." Bu plan tam olarak o tasarımdır. -->

## 1. Ne inşa ediyoruz

Ana ekran bugün bir navigasyon kabuğu: gün numarası, cüzdan HUD'ı, Play. Meta
oyun, o kabuğun arkasına **oyuncunun büyüttüğü bir expo alanı** koyuyor. Gün
oynanır → SoftMoney kazanılır → ana ekranda o parayla alan dekore edilir →
oyuncu ilerlemesini bir yer olarak görür.

Sanat seti `Assets/Art/Meta1/` (20 PNG: 1 arka plan + 16 satın alınabilir + 3
required). Main.png **853×1844**, yani en-boy oranı **0.463** — modern uzun bir
telefon (~9:19.5) için çizilmiş, bir ekran dolusu.

> **Ama sahnenin Canvas'ı 1080×1920 (0.5625) referansla kurulmuş.** Yani sanat
> ile ekran aynı orana sahip DEĞİL; kısa/geniş ekranlarda bir şeyin verilmesi
> gerekiyor. Ölçüldü ve K5'e yazıldı — Adım 7'nin kararı.

> **Klasör adı tesadüf değil: bu ilk lokasyon.** Belli bir Gün'den sonra ikinci
> bir restoran gelecek (`Art/Meta2/`, sanatı henüz yok). Sistem baştan **çok
> lokasyonlu** kurulur — bkz. K6. Aşağıdaki tablo Meta1'in içeriğidir, sistemin
> tamamı değil.

| Sprite | Rol | Nasıl açılır |
|---|---|---|
| `Background.png` (853×1844) | Sahnenin arka planı. **Harabe bina içine gömülü** — oyunun başlangıç hâli. | Her zaman görünür, satılık değil |
| `Building.png` (494×428) | Bitmiş stand. Harabenin üstüne çizilir. | SoftMoney — oyunun ilk hedefi |
| `Square.png` (853×819) | Taşlı meydan — **alan genişletme** | SoftMoney; ayrıca kendi bölgesindeki dekor yuvalarını açar |
| Fences, Fountain, FrontStand, HotdogSign, Lamp1, Lamp2, Plant, Sign, Table1–4, Tent, TrashBin (14 adet) | Kozmetik dekor | SoftMoney |
| `Required/DrinkFridge.png`, `Required/SaucesStand.png`, `Required/Fritöz.png` | Oynanışa bağlı proplar | **Satın alınmaz.** Gün içeriği tetikler |

> **Dosya adı notu:** `Fritöz.png` ASCII dışı karakter taşıyor. Unity sorun
> çıkarmaz ama git, macOS (NFD) ile Windows/Linux (NFC) arasında bu adı farklı
> kodlayabiliyor; proje ileride başka bir makinede açılırsa dosya "kayıp"
> görünebilir. Adım 1'de `Fryer.png` olarak yeniden adlandırmak bugün bedava,
> sonra referanslar bağlandıktan sonra değil.

### Kullanıcının kilitlediği kararlar (2026-08-19)

1. **Sabit yuvalar.** Her dekorun Main üzerinde önceden yazarlanmış tek bir yeri
   var. Serbest sürükleme yok, oyuncu pozisyon seçmiyor.
2. **Sadece SoftMoney, fiyat kilidi.** Gem meta tarafına hiç girmiyor (Gem'in tek
   harcama yeri Continue olarak kalıyor). Gün kilidi de yok — bir ürünü açan tek
   şey fiyatı.
3. **Required proplar oynanışa bağlı.** "Sonraki bölümde sos vardır, o zaman o
   bölümden **önce** açılması gerekir." Yani sabit bir gün numarası değil, Day
   içeriğinin kendisi tetikler.
4. **Square alan genişletmedir.** Kendi sprite'ı var ve ayrıca başka yuvaların
   kilidini açar.
5. **Lokasyonlar birikir, değiştirilmez.** Yeni restoran açılınca eskisi
   ziyaret edilebilir kalır ve dekore edilmeye devam edebilir. Cüzdan ortak.
6. **Yeni lokasyonu Gün numarası açar** — lokasyon asset'inde yazarlanmış
   `unlockAtDayIndex`, oyun içi bir tamamlama koşulu değil.
7. **Sistemin tamamı iki prop tipinden ibaret:** satın alınan (kozmetik + alan
   genişletme) ve gün gelince açılan. Üçüncü bir tip yok — K1'in tek
   mekanizması bu yüzden yeterli.
8. **Karışım lokasyondan lokasyona değişir.** Meta1'de kozmetik olan bir şey
   Meta2'de gün-açılımlı olabilir, ya da hiç bulunmayabilir. Bu **yapısal olarak
   bedava**: `unlock` alanı item başına, item'lar da lokasyonun içinde (K6).
   Meta2 geldiğinde yazılacak kod yok, doldurulacak asset var.

---

## 2. Mimari kararlar

### K1 — Tek mekanizma: bir yuva = bir Image, tek sprite

> **2026-08-20 düzeltmesi (D-016).** Bu bölüm başta *iki* sprite taşıyordu
> (`unownedSprite` / `ownedSprite`) ki bir prop satın alınınca **kaybolabilsin**.
> Kullanıcı bunu kaldırdı: bir prop sadece alınınca görünür, öncesinde yok.

Katalogdaki her kayıt **tek bir sprite** taşır ve prop aktif olana kadar hiç
yoktur. Görünüm kodunun tamamı:

```
image.sprite  = item.Sprite;
image.enabled = active;
```

Enum yok, switch yok, ikinci sprite'ın hesabı yok. Dekor, alan genişletmesi ve
gün-açılımlı prop üçü de aynı yoldan geçer; aralarındaki tek fark **aktifliğin
neyle kararlaştırıldığı**: `Purchase(price)` ya da `DayUnlock(trigger)`.
`abstraction-level.md` gerekçesi: soyutlama tek bir alan (`unlock`), tip başına
bir sınıf değil.

**İkinci sprite tek bir prop için vardı** — Main.png'nin içine gömülü bitmiş
standı gizleyen harabe. Artık bu **sanatta** çözülüyor: arka plan harabeyi
içeriyor, stand ise üstüne çizilen sıradan bir satın alma propu. Ekranda aynı
sonuç, bir mekanizma eksik.

**Vazgeçilen şey açıkça şu:** sistem artık "alınca kaybolan prop"u *hiç* ifade
edemiyor. Bunu yapabilen tek alan oydu, yani ileride öyle bir prop gerekirse yeni
bir mekanizma gerekir — bir alan değil. Birinin ona uzanıp hiçbir şey bulmaması
için burada yazılı.

**Sanat borcu: KAPANDI (kullanıcı, 2026-08-20).** `Main.png` → `Background.png`
(harabe gömülü) ve `StartBuilding.png` → `Building.png` (bitmiş stand, satın alınınca
üstüne çizilir). D-016'nın istediği tam olarak buydu: "alınca kaybolan prop" kavramı
kodda yok, sanatta çözüldü.

### K2 — Gün-açılımlı proplar: tek yazarlanmış gün numarası

> **2026-08-20 düzeltmesi (D-017).** Bu bölüm başta propun açılmasını **Day
> içeriğinden türetiyordu**: dört alanlı bir tetikleyici (yiyecekler /
> kategoriler / modifikasyonlar / minimum gün) ve oyuncunun geldiği güne kadarki
> bütün günlerde bu içeriği arayan bir tarama. Kullanıcı bunu kaldırdı:
> *"sadece hangi günden önce açılacağını seçebilelim, böyle çok gereksiz karmaşa
> var ve gereksiz arama tarama var."*

Kural artık tek satır:

```
prop aktif  ⟺  CurrentDayIndex >= item.UnlockAtDayIndex
```

Lokasyonun kendi kilidiyle **birebir aynı** kural (K6-2), yani sistemde
açılma diye tek bir kavram var, iki tane değil.

Sayı bir **katalog pozisyonu**, 0 tabanlı — oyuncuya gösterilen gün numarası
`index + 1` olduğu için "oyuncunun beşinci gününden önce hazır olsun" katalogda
**4** olarak yazılır. Bu, `fingerprint.md`'nin zaten kaydettiği
`CurrentDayIndex` ↔ Day dosyasının kendi `dayIndex`'i karışıklığının aynısı, o
yüzden tooltip'te açıkça yazıyor.

**Neyin gittiği (üç şey, bir değil):**

- `DayUnlockTrigger` sınıfının tamamı ve dört alanı
- **Adım 6'nın kendisi** — `DayContentFacts`, gün içeriği taraması, hepsi
- **`MetaSystem → DaySystem` bağımlılık oku.** Meta tarafı Day kataloğunu hiç
  okumuyor artık; ihtiyacı olan tek şey oyuncunun bulunduğu gün numarası.
- M3/M4/M4b açık soruları — hangi modifikasyon sos sayılır, buzdolabı kategoriye
  mi altı id'ye mi bakar, fritöz `Food_Fries`'a mı bütün `Side`'lara mı. Üçü de
  **yalnızca** açılma içerik okuduğu için soruydu.

**Vazgeçilen şey:** iki sayı birbirinden kopabilir. Sosu Gün 5'ten Gün 9'a
taşırsan sos standı yine Gün 5'te belirir, ta ki katalogdaki sayıyı da
güncelleyene kadar. Ve bunu **hiçbir doğrulayıcı yakalayamaz** — `Data`
assembly'si Day kataloğunu tasarım gereği göremiyor, yani kontrol yazılmamış
değil, *yazılamaz*. Azaltıcı etken: prop başına tek bir sayı, Inspector'da
görünür.

`MetaUnlockKind` enum'u **kalıyor**: iki int alan var (`price`,
`unlockAtDayIndex`) ve hangisinin geçerli olduğunu söyleyen şey o. "Fiyatı 0'dan
büyükse satılıktır" gibi bir çıkarım, yanlış yazarlanmış bir 0'ın propun türünü
sessizce değiştirmesi demek olurdu.

### K3 — Cüzdanın tek yazıcısı ana ekranda da korunmalı — bu planın en riskli yeri

Bugünkü durum:

- `Wallet` her bakiye değişiminin tek yazıcısı; `GameState`'in setter'ları
  `internal` (D-010, derleyici zorluyor).
- `player_profile.json`'a yazan tek yer `GameManager` (4 çağrı noktası).
- **Ana ekranda `GameManager` yok, `GameState` yok.** `MainScreenView` ve
  `HudWalletSource` saf okuyucu (D-012, D-013).

Meta dükkânı ana ekranda para harcayacak. Yani ana ekranın yazma yeteneği
kazanması gerekiyor — invariant'ı kırmadan.

#### Neden "GameManager'ı da ana ekrana koyalım" değil (kullanıcının sorusu, 2026-08-19)

Doğru içgüdü, ama olduğu gibi taşımak çalışmıyor. `GameManager.Awake`'in sonu:

```
ApplyDayStartBoardPreSeed();
TicketSlotManager.FillEmptySlots();   ← 3 ticket atar ve sayaçlarını BAŞLATIR
```
```
Update() → TicketSlotManager.Tick(Time.deltaTime);   ← gün menünün arkasında işler
```

Ana ekranda dükkâna bakarken ticket'lar zaman aşımına uğrar, can gider,
`HandleLifeLoss` tetiklenir. Ayrıca `EnsurePhysics2DRaycaster()` (board
sürükleme) anlamsız ve `OnTicketAssigned`, `CurrentDay == null` iken fırlatıyor.

Kalıcı (`DontDestroyOnLoad`) bir GameManager ayrı bir duvara çarpıyor:
SampleScene'deki view'ların hepsi `[SerializeField] GameManager` tutuyor ve başka
sahneden gelen kalıcı bir nesne Inspector'da atanamaz — hepsi runtime aramaya
dönerdi, ki bu D-013'teki açık kullanıcı talimatını geri alır. Üstüne, güne
tekrar girerken sahne yıkımının bugün bedava verdiği board/tray/slot temizliğini
elle yazmak gerekirdi (D-012'nin ayrı-sahne gerekçesi).

#### Ama sorunun asıl noktası doğru: kurulum kopyalanmamalı

`Awake`'in **ilk beş adımı tek bitişik blok** ve tam olarak ana ekranın ihtiyacı:

| # | Satır | Ana ekran | Gün sahnesi |
|---|---|---|---|
| 1 | `State = new GameState(gameConfig)` | ✅ | ✅ |
| 2 | `wallet` + `profileStore.Load()` + `ApplyPersistedBalances` | ✅ | ✅ |
| 3 | `LivesManager` + `ApplyPersistedLives` | ✅ | ✅ |
| 4 | `dayCatalog = DayCatalogParser.ParseAll(...)` | ❌ (D-017'den sonra meta buna muhtaç değil) | ✅ |
| 5 | `State.CurrentDayIndex = ResolveStartingDayIndex(...)` | ✅ | ✅ |
| 6+ | ticketFactory, DayLifecycle, TicketSlot, Tray, Economy, provider, abonelikler, pre-seed, `FillEmptySlots` | ❌ | ✅ |

Bu beş adımı ana ekranda **yeniden yazmak**, dört tane yük taşıyan sıralama
kuralını iki dosyaya kopyalamak demek:

1. `wallet`, `ApplyPersistedBalances`'tan önce kurulmalı — `GameState`'in
   setter'ları `internal`, sınıf bakiyeyi kendi atayamaz
2. `LivesManager`, can götürebilecek her şeyden önce kurulmalı
3. katalog, `ResolveStartingDayIndex`'ten **önce** parse edilmeli — clamp
   `Count`'a muhtaç
4. `ApplyPersistedBalances` gün-başı anlık görüntüsünü yeniden alıyor

Kopyalanmış kurulum, invariant'ların engellemek için var olduğu şey.

**Şekil: `GameSession`.** 1–5 dışarı çıkarılır; `GameManager` = `GameSession` +
gün yarısı; ana ekran yalnızca `GameSession` kurar. Tek kurulum yolu.

`GameSession` **saf C# olabilir** — bağımlılıkları (Data, ProgressionSystem,
LivesSystem, DaySystem) hepsi asmdef. Yani **EditMode'dan test edilebilir**, ki
`ResolveStartingDayIndex`'in clamp'i bugün testsiz: GameManager predefined
assembly'de sıkıştığı için D-012 bunu bilinen bir boşluk olarak kaydetmişti. Bu
çıkarma o boşluğu kapatıyor — planın yan kazancı değil, en somut kazancı.

#### Kalan iki parça

1. Dosyaya yazma **tek bir sınıfa** toplanır (`ProfileSaver`, ProgressionSystem
   içinde): tam bir `PlayerProfile`'ı alır ve `PlayerProfileStore.Save` çağırır.
   `GameManager` ve meta ekranı ikisi de bunu çağırır — tıpkı `Wallet`'ın birden
   çok çağıranı olması gibi. Yazıcı **sınıf** tek kalır.
   - Bu olmadan ana ekran kaydederken gün indeksini ve canları ezme riski var;
     tam profili round-trip eden tek bir yazıcı bunu yapısal olarak kapatır.
2. `HudWalletSource` bugün canlı moda yalnızca bir **`GameManager`**
   `[SerializeField]` varsa geçiyor. Bunun yerine `GameSession` sağlayan bir
   arayüz: `GameManager` ve ana ekranın kökü ikisi de uygular.
   HudWalletSource'un canlı yolu **değişmeden** çalışır, dosya-okuma yolu
   "sağlayıcı yok" durumuna düşer.
   - Kazanç: satın alma sonrası ana ekran HUD'ı **canlı güncellenir**. Bugünkü
     "dosyayı bir kez oku" modu satın almadan sonra yanlış rakam gösterirdi.
   - D-013/D-014'ün kendi kuralına uyar: *"Do not add a mode branch back into a
     view; extend the source instead."*

Bu adım K1 kritiklikte dosyalara dokunuyor (`GameManager`, `HudWalletSource` ve
üç HUD view'ı). Kendi başına bir adım olarak, testleriyle yürütülmeli.

### K4 — Sahne: ayrı bir sahne değil, MainScreen'in kendisi

GDD zaten "hang it here" diyor ve `MainScreen` build index 0. Üçüncü bir sahne
save dosyası üzerinden ikinci bir el değiştirme daha yaratırdı. Meta içerik
mevcut `MainScreen.unity` Canvas'ına eklenir; `MainScreenView` (gün numarası +
Play) ve `HudCanvas` prefabı olduğu gibi kalır.

> `MainScreenSceneBuilder` mevcut sahneyi **üzerine yazmayı reddediyor**, yani
> sahne serbestçe yeniden düzenlenebilir; ama builder yaptığın değişiklikleri
> üretmez. Meta yuvalarını sahneye elle yerleştirmek yerine **katalogdan runtime
> spawn** etmek bu yüzden tercih edilmeli: tek otorite katalog olur, sahne
> kayması olmaz.

### K5 — Yuva pozisyonları normalize edilir

Slot pozisyonları arka plan Image'ının rect'ine göre **normalize** (0..1)
saklanır, piksel değil. Sebep: Main.png tam telefon oranında, ama gerçek
cihazlar 0.42–0.56 arası değişiyor; arka plan aspect-fill edilince piksel
pozisyonlar kayar, normalize olanlar kaymaz.

19 yuva pozisyonunu (16 satın alınabilir + 3 required; Main arka plan, yuva değil) elle yazmak makul değil → bir **Editor yerleştirme aracı**: yuvaları
sahnede sürükle, araç normalize pozisyonları katalog asset'ine geri yazsın.
Projenin bu konuda geçmişi var (DayEditor, MainScreenSceneBuilder,
HudCanvasPrefabSetup).

Normalize pozisyon çok lokasyonlulukta ikinci bir kazanç veriyor (K6): her
lokasyonun arka planı farklı boyutta olabilir, yuva verisi aynı biçimde kalır.

#### Ölçülen en-boy uyumsuzluğu (2026-08-20) — Adım 7'nin kararı

`MainScreen.unity`'nin CanvasScaler'ı: **Scale With Screen Size**, referans
**1080×1920**, Match Width Or Height **0.5**. CanvasScaler ölçeği değiştirir,
**oranı değiştirmez** — yani Canvas rect'inin oranı her zaman cihazın oranıdır ve
arka plan 0.46–0.56 aralığını karşılamak zorunda.

Arka planı **genişliğe oturtursak** (`height = width / 0.4626`):

| Cihaz | Oran | Canvas yüksekliği | Sanatın yüksekliği | Taşma |
|---|---|---|---|---|
| 1080×2340 (9:19.5) | 0.461 | ~2120 birim | ~2118 | **~0 — tam oturur** |
| 1080×1920 (9:16) | 0.5625 | 1920 | 2334 | **414 birim (~%18)** |

Yani sanat, kısa ekranlarda dikeyde %18'e kadar taşıyor. Üç seçenek:

- **(a) Genişliğe oturt + dikey kaydırma — SEÇİLDİ (kullanıcı, 2026-08-20).** Taşma
  varsa clamp'li bir `ScrollRect`; uzun telefonlarda kaydıracak bir şey olmaz. Oyuncu
  her cihazda sanatın tamamını görebilir, hiçbir yuva erişilemez kalmaz.
- **(b) Genişliğe oturt + kırp.** Bedava, ama kırpılan bantta kalan yuvalar
  geniş ekranlarda görünmez — yazarlama kuralı gerektirir ("önemli yuvalar
  güvenli bantta").
- **(c) Referans çözünürlüğü 1080×2340'a çek.** Sanata birebir uyar ama HUD ve
  `MainScreenView` 1080×1920 için yazarlandı; ikisini de yeniden düzenlemek
  gerekir. Bu adımın kapsamını aşar.

**K5'in "yuvaları ekrana değil arka plan rect'ine normalize et" kuralı üç
seçenekte de load-bearing:** yuvalar sanatla birlikte hareket eder, yoksa
kaydırma/kırpma anında pozisyonlar sanattan kopar.

### K6 — Sistem baştan çok lokasyonlu kurulur

Kullanıcı: *"belli bir bölüm sonra yeni bir restorana geçeceğiz, şu an o daha
elimde yok ama buna göre planla."* `Art/Meta1/` klasör adı da bunu söylüyor.

**Şekil: lokasyon birinci sınıf bir varlık, dekor ona ait.**

```
MetaCatalog (kök asset)
  locations: MetaLocation[]          ← sıralı

MetaLocation
  id                 "Meta1"
  displayName        "Expo Park"
  backgroundSprite   Main.png
  unlockAtDayIndex   int             ← Meta1 için 0
  items              MetaItemDefinition[]
```

Yani K1–K5'in tamamı **lokasyon içinde** geçerli: yuvalar, iki sprite'lı
mekanizma, alan genişletme, required proplar — hepsi bir lokasyonun kendi
listesinde. Meta2 geldiğinde yazılacak kod yok, doldurulacak bir asset var.

Dört sonuç:

1. **Sahiplik anahtarı lokasyonla nitelenir.** `PlayerProfile` düz bir
   `List<string>` tutar, ama içindeki değer `"Meta1.Fountain"` — anahtarı
   `$"{location.Id}.{item.Id}"` olarak **resolver hesaplar**, yazar elle yazmaz.
   Böylece asset'te id'ler yerel ve temiz kalır (`Fountain`), benzersizlik
   yapısal olur, ve iki lokasyonda aynı adlı dekor çakışmaz. Kalıcılık tarafında
   hâlâ tek alan, tek versiyon artışı.
2. **Lokasyon kilidi türetilir, kaydedilmez:**
   `açık ⟺ CurrentDayIndex >= unlockAtDayIndex`. Required propların kuralıyla
   aynı şekil (K2) — tek otorite, ikinci bir kayıt alanı yok.
3. **Cüzdan ortak, dekor değil.** Tek SoftMoney havuzu bütün lokasyonlar için.
   Bu, "eskisi ziyaret edilebilir kalır" kararıyla birlikte batık maliyet
   sorununu kapatıyor: Meta1'e harcanan para görünür kalıyor ve oyuncu Meta2
   açıldıktan sonra da Meta1'i bitirebiliyor.
4. **Hangi lokasyona bakıldığı UI durumudur, kayıt değil.** Ekran, açık olan
   **en yeni** lokasyonla açılır; oyuncu oklarla gezer. Gerekçe: bunu kaydetmek
   sırf görüntüleme tercihi için bir profil alanı ve bir versiyon artışı demek.
   Rahatsız ederse geri dönüşü tek alan — ucuz ve tersine çevrilebilir.
5. **Her lokasyon kendi içinde kapalıdır.** Bir item'ın `requiresAreaId`'si
   **aynı** lokasyondaki bir alanı göstermek zorunda; lokasyonlar arası referans
   doğrulamada hata. Karışım lokasyondan lokasyona değiştiği için (kilitli karar
   8) bu kural, Meta2 yazarlanırken sessizce Meta1'in meydanına bağlanmayı
   engelliyor.

**Bir nüans, bilerek böyle:** gün-açılımlı proplar **global** gün sayacına bakar,
lokasyonun kendi açılışına göre değil. Meta2 Gün 12'de açılıyorsa ve içindeki
buzdolabı Gün 4'e yazarlanmışsa, buzdolabı Meta2 **açılır açılmaz oradadır**.
Doğrusu bu: yeni restoran, hâlihazırda sattığın her şeyle birlikte kurulur.
Pratik sonuç: bir lokasyonun proplarını kendi açılış gününden ÖNCEKİ günlere
yazarlamak, "bu lokasyon hazır gelsin" demenin yolu.

**Bunun bugüne maliyeti:** katalogda bir sarmalama katmanı, resolver'ın lokasyon
parametresi alması, ve dükkânın "bakılan lokasyon" kavramı. Hepsi Adım 2–3–8'in
içinde, ayrı bir adım değil. Sonradan eklemek ise sahiplik anahtarlarının
şemasını değiştirmek olurdu — yani bir save migrasyonu. **Şimdi yapmak bedava,
sonra yapmak pahalı**; bu yüzden K6 baştan planda.

---

## 3. Sistem sınırları

**Yeni sistem: `MetaSystem`** — `Assets/Scripts/Systems/MetaSystem/`, kendi
asmdef'i ile (projenin diğer sistemleri gibi, EditMode'dan test edilebilsin).

```
MetaSystem  →  ProgressionSystem   (Wallet ile harcama, profil sınırı)
MetaSystem  →  Data                (MetaCatalog, FoodItemConfig, ModificationConfig)
MainScreen  →  MetaSystem          (ekran onu barındırır)
```

Hepsi tek yönlü, döngü yok. `MetaSystem`'e hiçbir sistem referans vermez.

**`GameSession` MetaSystem'e ait değil** (K3). O, `GameManager`'dan çıkarılan
paylaşılan oturum kurulumu — sahibi **Bootstrap**, ve her iki ekran da onu
kullanır. Meta tarafı onun bir *tüketicisi*; ok `MetaSystem → Bootstrap` değil,
ana ekranın kökü `GameSession`'ı kurar ve `MetaSystem`'e besler, yani
MetaSystem'in kendisi Bootstrap'i tanımaz.

Yeni klasörler (blueprint.md'nin folder layout'una eklenecek):
`Scripts/Systems/MetaSystem/`, `Data/Meta/`, `Prefabs/Meta/` (gerekirse).

---

## 4. Adımlar

Her kutu tek bir preflight + APPROVE döngüsü.

### [x] Adım 0 — Harita onarımı + karar kaydı (kod yok) — **bitti 2026-08-20**

Oturum başı rapor haritaların bozuk olduğunu söylüyor ve skill'in 8. ilkesi
"görevin dokunduğu satırdaki bozukluğu onarmak görevin parçasıdır" diyor:

- `codemap-core`: **DEGRADED** — 9 stale, 36 missing-role
- `codemap-editor`: **DEGRADED** — 3 stale, 2 missing-role
- `codemap-ui`: **DEGRADED** — 4 missing-role
- `index.md`: 44 satırda `sys: ?`

Bu planın dokunacağı yollardaki satırlar onarılır (hepsi değil — dokunulanlar).
Ayrıca:

- `blueprint.md`: `MetaSystem` sistem satırı + 4 ok + yeni klasör satırları
- `shards.json`: `Scripts/Systems/MetaSystem/**` hangi shard'a düşüyor, kontrol
- `decisions.md`: **D-015** — bu bölümün K1–K6 kararları

**Sonuç (2026-08-20).** Yazılanlar: `blueprint.md` (`MetaSystem` sistem satırı +
2 ok, `MainScreen`'e 3. ok, ve daha önce hiç bulunmayan `Scripts/Bootstrap/` +
`Scripts/Systems/<System>/` klasör satırları), `decisions.md` (**D-015**, projenin
ilk tasarım-önce kaydı), `index.md` (yeniden üretildi — 13 sistem).
`check_blueprint.py`: **0 hata**, 3 uyarı. Üçüncü uyarı yeni ve *doğru*:
"blueprint system with no code behind it: MetaSystem" — Adım 3'te kapanır.
`shards.json` değişmedi (catch-all `core` yeterli). Codemap onarımı **gerekmedi**:
12 işaretli satırın hiçbiri bu planın yollarında değil; o satırlar kendi
yollarına dokunan adımlarda onarılacak. Ayrıca `check_blueprint.py` klasör
yerleşimini hiç DENETLEMİYOR ("no Assets/ directory yet") — iç içe `Assets`
klasörü hatası, D-013'te kaydedilmiş, hâlâ açık; yeni klasör satırları elle
doğrulandı.

### [x] Adım 1 — Yeniden adlandırma (ithal ayarları GEREKMEDİ) — **bitti 2026-08-20**

> **2026-08-20 düzeltmesi.** Bu adım planlandığında "20 PNG'nin pivot/PPU/
> sıkıştırma ayarları düzeltilecek" yazıyordu. 20 `.meta` dosyası okundu:
> **hepsi zaten doğru ve birbirinin aynısı** — `textureType: 8` (Sprite 2D and
> UI), `spriteMode: 1` (Single), mipmap kapalı, `alphaIsTransparency: 1`,
> `nPOTScale: 0`, `maxTextureSize: 2048` (en büyük kenar 1844, yani hiçbir şey
> küçültülmüyor). Platform override'ı yok, gerekmiyor.
>
> **Pivot maddesi de yanlıştı:** UI `Image` bileşeni sprite'ın pivot'unu
> **kullanmaz** — konumu RectTransform'un pivot'u belirler. K4/K5 meta ekranını
> Canvas/UI olarak kurduğu için sprite pivot'u tamamen ilgisiz; "temas noktası"
> kararı yuva verisidir (Adım 2/7), ithal ayarı değil.
>
> Atlas da gerekmiyor: projede hiç Sprite Atlas yok, ve tek bir menü ekranında
> ~19 UI Image'ın draw call maliyeti maliyet modelinde önemsiz. Atlas eklemek
> ölçülmemiş bir optimizasyon olurdu.

Geriye tek gerçek iş kalıyor: **`Required/Fritöz.png` → `Required/Fryer.png`**
(ASCII dışı karakter, §1 notu). `.png` ve `.png.meta` birlikte taşınır ki GUID
korunsun. Katalog asset'i ona referans vermeden yapılırsa bedava.

Bu adım o kadar küçüldüğü için **Adım 2 ile aynı preflight'ta** yürütülür.

### [x] Adım 2 — Veri katmanı: `MetaCatalog` + `MetaLocation` — **bitti 2026-08-20**

`Assets/Data/DataScripts/MetaCatalog.cs` + `Assets/Data/Meta/MetaCatalog.asset`

Yapı çok lokasyonlu (K6) — Meta2 geldiğinde kod değil, asset doldurulur:

```
MetaCatalog (kök)
  locations           MetaLocation[]   ← sıralı

MetaLocation
  id                  "Meta1"          ← sahiplik anahtarının öneki
  displayName         "Expo Park"      ← lokasyon geçiş çubuğunda görünen
  backgroundSprite    Main.png
  unlockAtDayIndex    int              ← Meta1 = 0
  items               MetaItemDefinition[]

MetaItemDefinition
  id                  string, lokasyon İÇİNDE benzersiz ("Fountain")
  displayName         string (dükkânda görünen)
  unownedSprite       Sprite (opsiyonel)
  ownedSprite         Sprite (opsiyonel)
  unlock              Purchase(price) | DayContent(trigger)   ← trigger: bkz. K2
  normalizedPosition  Vector2 (0..1, arka plan rect'ine göre)
  sortOrder           int (çizim sırası — derinlik)
  requiresAreaId      string (boş = her zaman açık; "Square" = meydan alınmadan alınamaz)
  unlocksArea         bool (Square'de true)
```

Fiyatlar **veriden** okunur, kodda sabit yok (root invariant). Doğrulama:
lokasyon içinde tekrarlanan id, tekrarlanan lokasyon id'si, iki sprite'ı da boş
kayıt, satın alınabilir ama fiyatı 0 olan kayıt, var olmayan bir alana işaret
eden `requiresAreaId`, **başka bir lokasyonun alanını gösteren `requiresAreaId`**
(K6-5), arka planı boş lokasyon, ve `unlockAtDayIndex`'i azalan sırada giden
lokasyon listesi.

**Sonuç (2026-08-20).** Yazılanlar: `MetaCatalog.cs` (şema: `MetaCatalog` SO +
`MetaLocation` / `MetaItemDefinition` / `DayUnlockTrigger` düz sınıfları +
`MetaUnlockKind` + `OwnershipKey`), `MetaCatalogValidator.cs` (hata/uyarı
ayrımı, `DayValidationResult`'ın şeklini izliyor), `MetaCatalogValidatorTests.cs`
(31 test). `Fritöz.png` → `Fryer.png`, GUID korunarak; `.meta` içindeki
`Fritöz_0` sprite adı da `Fryer_0` yapıldı, internalID'ye dokunmadan.

**Derleme:** `dotnet build` ile `ExpoTheExplorer.Data` ve
`ExpoTheExplorer.Tests.EditMode` — **ikisi de 0 hata**. Yeni dosyalar hiç uyarı
üretmedi (mevcut 4 uyarı `TicketGenerationConfig`'de, önceden var).
**Testler KOŞTURULAMADI:** Unity projeyi açık tutuyor, batchmode kilide çarpıyor.
Koşturma Test Runner'dan yapılmalı.

Yazarken çıkan iki tasarım düzeltmesi:
1. **`DayUnlockTrigger.IsEmpty` null girdileri saymaz.** Yalnızca silinmiş bir
   asset referansı tutan liste Inspector'da dolu görünür ama hiçbir zaman
   eşleşemez; düz bir `Count` kontrolü onu geçerli tetikleyici sayardı.
   Yanında canlı girdi de varsa ayrı bir uyarı çıkıyor (`HasMissingReferences`).
2. **Boyut/ölçek alanı YOK, bilerek.** Bir lokasyonun bütün sprite'ları aynı
   çözünürlükte yazarlandığı için propun canvas boyutu kendi piksel boyutu ×
   arka planın ölçek katsayısı olarak *türetiliyor* — boyutu ayrıca yazarlamak
   sanatla çelişebilecek ikinci bir otorite olurdu.

`MetaCatalog.asset` bilerek yazılmadı — Unity'de `Create >
ExpoTheExplorer/Data/Meta Catalog` ile üretilecek (Adım 7/9 onu dolduracak).

### [x] Adım 2.5 — Meta Editor penceresi — **bitti 2026-08-20**

Kullanıcının isteği: kataloğu iç içe Inspector listelerinden tıklamak yerine Day
Editor gibi bir pencereden yazarlamak. **Depolama SO olarak kaldı**, JSON'a
gidilmedi — kararın tamamı ve JSON'un neden reddedildiği `decisions.md` D-018'de.

Yazılanlar: `Assets/Editor/MetaEditorWindow.cs` (OdinMenuEditorWindow — sol
lokasyon ağacı, araç çubuğunda katalog alanı + doğrulama sayıları) ve
`MetaLocationLayoutGUI.cs` (asıl iş: arka planı gerçek oranında çizip propları
**sürükleyerek** yerleştirme). `DayEditorSpriteGUI.DrawTexCoords` yeniden
kullanıldı, üçüncü tüketicisi olarak. **Hiçbir asmdef değişmedi** —
`ExpoTheExplorer.Editor` zaten `ExpoTheExplorer.Data`'ya referans veriyordu.

Menü: **ExpoTheExplorer > Meta Editor**.

Üç tasarım notu: bütün mutasyonlar `SerializedObject` üzerinden (doğrudan yazmak
asset'i işaretlemez, düzenleme sonraki domain reload'da sessizce kaybolur);
alanları Odin değil Unity'nin kendi drawer'ı çiziyor (şema değişince pencere
tahmin yapmasın); ve "Depth from Y" otomatik değil **buton** (otomatik olsa elle
verilmiş istisnaları her sürüklemede silerdi).

Derleme: `ExpoTheExplorer.Editor` **0 hata**; Data ve Tests.EditMode bozulmadı.
Pencere Unity'de **çalıştırılarak denenmedi** — proje açık olduğu için batchmode
kilide çarpıyor, ve IMGUI davranışı derlemeyle kanıtlanmaz.

Bu adım Adım 7'nin ayrı "Editor yerleştirme aracı" maddesini **yuttu**.

### [x] Adım 3 — Çekirdek mantık + testler (asmdef, saf C#) — **bitti 2026-08-20**

Bu adım planın değer merkezi — Unity'siz, tamamen test edilebilir.

- `MetaResolver` — `(lokasyon, sahip olunan anahtarlar, CurrentDayIndex)` → aktif id
  kümesi + satın alınabilir id kümesi (alan kilidini uygulayarak). Üçüncü girdi
  D-017'den sonra tek bir int; eskiden taranmış gün içeriğiydi
- **Sahiplik anahtarı** `$"{location.Id}.{item.Id}"` burada hesaplanır; katalog
  yerel id tutar, profil nitelenmiş anahtar tutar (K6-1)
- **Lokasyon kilidi:** `açık ⟺ CurrentDayIndex >= unlockAtDayIndex`, ve açık
  lokasyonların listesi + varsayılan görüntülenen (en yeni açık olan)
- `MetaPurchase` — "X alınabilir mi": zaten sahip mi, lokasyonu açık mı, alanı
  açık mı, parası yetiyor mu; her red için ayrı bir sebep
- EditMode testleri: `MetaSystemTests.cs` — iki sahte lokasyonla, tek lokasyonla
  yakalanamayacak anahtar çakışması ve kilit sızıntısı senaryoları dahil

Henüz ne kalıcılık, ne UI, ne sahne.

**Sonuç (2026-08-20).** `MetaSystem` assembly'si + `MetaResolver` + `MetaPurchase` +
25 test. Kalıcı kayıt `decisions.md` D-019.

**Blueprint düzeltildi:** D-015'in planladığı `MetaSystem → ProgressionSystem` oku
**kaldırıldı** — kodu yazarken gereksiz olduğu görüldü. Kurallar sahip olunan
anahtarları ve bakiyeyi parametre alıyor, yani para ve sahiplik listesi
ProgressionSystem'deki tek yazıcılarını koruyor ve ekranın kompozisyon kökü ikisini
birleştiriyor. MetaSystem artık **hiçbir sisteme bağımlı değil**; kazanç temizlik
değil erişim: sıfır bağımlılık + sıfır MonoBehaviour = her kural testten çağrılabilir.

Kurallardaki dört karar: verdict (bool değil) ve kontrol sırası
(`NotForSale` → ... → `NotEnoughMoney`); alan geçidi **aktifliğe** de uygulanıyor;
`ActiveItems` `List.Sort` değil insertion sort kullanıyor (kararsızlık üst üste binen
proplarda titremeye yol açardı); `ShopItems` parası yetmeyen ve alan-kilitli propları
**listede tutuyor**.

**Doğrulama sınırı:** yalnızca **derleme** (assembly + testler, ikisi de 0 hata;
Unity yeni asmdef için csproj üretmemişti, geçici csproj'larla yapıldı). Suite
koşturulmadı — Unity projeyi kilitliyor.

### [x] Adım 4 — Kalıcılık: profil şeması v3 → v4 — **bitti 2026-08-20**

- `PlayerProfile.OwnedMetaItemIds` (`List<string>`) — nitelenmiş anahtarlar
  (`"Meta1.Fountain"`), düz tek liste. Lokasyon başına iç içe yapı **değil**:
  JsonUtility'de daha kırılgan ve tek alan tek versiyon artışı demek (K6-1).
- `PlayerProfileStore.CurrentVersion = 4`
- **Migrasyon gerekmez:** eski dosyada alan yok → boş/null okunur → "hiçbir şeye
  sahip değil", ki yeni bir oyuncu için doğru varsayılan. (v3'ün `Lives`
  durumunun aksine — orada 0 ölü oyuncu demekti, `UpgradeToCurrent` gerekmişti.)
  Yine de `Defaults()` ve `UpgradeToCurrent` gözden geçirilir.
- `PlayerProfileStoreTests.cs`'e v4 round-trip + "v3 dosyası boş liste okur"
  testleri.

**Sonuç (2026-08-20).** `OwnedMetaItemIds` (`List<string>`, nitelenmiş anahtarlar),
`CurrentVersion = 4`, ve `PlayerProfileStore.Normalize`. Kayıt: D-020.

**Preflight'ta "migrasyon gerekmez" demiştim, yarı yanlıştı.** Semantik migrasyon
gerçekten gerekmiyor (boş liste = hiçbir şeye sahip değil, doğru varsayılan). Ama bu
**ilk referans tipli alan**, ve `UpgradeToCurrent` dosya zaten güncelse erken dönüyor —
yani elle bozulmuş bir v4 dosyası normalize edilmeden geçerdi ve çağırana
`NullReferenceException` olarak varırdı. `Normalize` bu yüzden versiyon kapısının
**dışında** ve her yüklemede çalışıyor, `fallback` yollarını da sarıyor.

Testlerden biri özellikle bu boşluğu pinliyor: `"Version":4` + `"OwnedMetaItemIds":null`
payload'u. Bariz olan v3 testi bu hatayı yakalamıyor, iki durumda da geçiyor.

Derleme: ProgressionSystem ve Tests.EditMode **0 hata**. Suite koşturulmadı (Unity kilidi).

### [x] Adım 5 — `GameSession` çıkarımı + tek yazıcı sınırı (K3) — **en riskli adım**

> **İKİYE BÖLÜNDÜ (2026-08-20).** İki yarısı farklı K1 yüzeylerine dokunuyor ve tek
> adımda yapılsa Play-mode'da bir şey bozulduğunda hangisinden geldiği belirsiz olurdu.
> **5a — `GameSession` çıkarımı: BİTTİ.** **5b — HUD bağlaması + ana ekranın kompozisyon
> kökü: BİTTİ.**

Bu adım artık meta için bir yardımcı sınıf yazmak değil, **mevcut kurulumu
paylaşılabilir hale getirmek** (K3'teki gerekçe):

- **`GameSession`** — saf C#, kendi asmdef'i: `GameManager.Awake`'in 1–5.
  adımları (GameState, Wallet + `ApplyPersistedBalances`, LivesManager +
  `ApplyPersistedLives`, Day kataloğu parse, `ResolveStartingDayIndex` clamp).
  Dört sıralama kuralı burada tek yerde yaşar.
- **`GameManager` bunu kullanır** — `Awake` = `session = new GameSession(...)` +
  gün yarısı (ticket/tray/board/economy/pre-seed/tick). Davranış değişmez.
- **Ana ekranın kökü** yalnızca `GameSession` kurar; gün yarısı hiç çalışmaz.
- ProgressionSystem'de **`ProfileSaver`**: dosyaya yazan tek sınıf; hem
  `GameManager` hem ana ekran onu çağırır, tam profili round-trip ederek.
- **`HudWalletSource`** somut `GameManager` yerine `GameSession` sağlayan arayüze
  bağlanır → ana ekran HUD'ı satın alma sonrası canlı güncellenir.

**Test kazancı:** `GameSession` saf C# olduğu için `ResolveStartingDayIndex`'in
clamp'i ilk kez EditMode'dan test edilebilir — D-012'nin açıkça kaydettiği
kapsam boşluğu. `GameSessionTests.cs` bu adımın çıktısı.

K1 kritiklikte dosyalara dokunuyor ve `GameManager`'ın kendisi hâlâ EditMode'dan
test edilemez (predefined assembly vs asmdef test assembly), o yüzden adım ayrıca
bir Play-mode geçişi ister: gün sahnesi eskisi gibi açılıyor mu, ilk 3 ticket
geliyor mu, HUD doğru mu.

**5a sonucu (2026-08-20).** `Scripts/Session/` + `GameSession` + 12 test. `GameManager`
oturum yarısını tek satıra indirdi; **public yüzeyi birebir korundu**, o yüzden
`SampleScene`'deki hiçbir view rewire edilmedi. Kayıt: D-021.

**`ProfileSaver` iptal edildi.** `GameSession` zaten profili yükleyip cüzdanı/canları/gün
indeksini tuttuğu için yazılanı derleyecek olan da o; üçüncü bir sınıf her alanın bir
KOPYASINA ihtiyaç duyardı ve o kopyada atlanan bir alan hata vermez, oyuncunun parasının
üstüne sessizce sıfır yazar. `PlayerProfileStore` dosya sınırı olarak kaldı. Bu, Adım 4'te
bilerek açık bırakılan "sahip olunan kümenin yazıcısı" boşluğunu da kapattı.

**Klasör zorunluydu:** `Scripts/Core/` döngü olurdu (ProgressionSystem, LivesSystem ve
DaySystem üçü de Core'a referans veriyor), `Scripts/Bootstrap/` ise asmdef'siz olmak
zorunda (asmdef eklemek `GameManager`'ı içine alıp sahnedeki view referanslarını riske
atardı).

**Kazanılan kapsam:** `ResolveStartingDayIndex`'in clamp'i D-012'den beri gerçek bir
çökmeyi (son günün ötesindeki indeks → `CurrentDay` null → ilk ticket'ta exception)
koruyordu ve **hiç testi yoktu**. Artık dört testi var.

**Doğrulama sınırı:** yalnızca derleme (Session + Assembly-CSharp + testler, üçü de 0
hata). **Play-mode geçişi YAPILMADI** — senden gereken kontrol: gün sahnesi açılıyor mu,
ilk 3 ticket geliyor mu, HUD doğru mu, gün bitince kayıt oluyor mu.

**5b sonucu (2026-08-20).** `SessionHost` (soyut taban), `MainScreenRoot`,
`HudWalletSource`'un yeniden bağlanması ve `ExpoTheExplorer > Meta > Wire MainScreen
Session` menü adımı. Kayıt: D-022. **Ana ekran artık bir `GameState` ve `Wallet`
taşıyor** — dükkânın para harcayabilmesinin ön koşulu.

**Planda "arayüze bağlanır" yazıyordu, olmuyor.** `[SerializeField]` bir arayüzü
serileştiremez, yani Inspector'dan sürüklenebilir tip-güvenli bir alan somut bir tipe
bakmak zorunda. Soyut MonoBehaviour tabanı (`SessionHost`) seçildi; alternatifleri
`MonoBehaviour` alan + runtime cast (her şey sürüklenebilir, hata play-time'da çıkar)
ya da D-013'te kullanıcının reddettiği runtime arama.

**`[FormerlySerializedAs("gameManager")]` kozmetik değil, zorunlu.** Alan adını
değiştirmek `SampleScene`'deki prefab override'ını düşürür ve HUD gün sahnesinde
sessizce save-dosyası moduna geçer — dosyanın kendi yorumunda "KNOWN FAILURE MODE"
diye yazılı olan şeyin aynısı.

**Yeni test yok, bilerek:** üç tip de MonoBehaviour ve `HudWalletSource` predefined
assembly'de, yani asmdef test assembly'si onu göremiyor. Doğrulama yalnızca derleme
(4 assembly, 0 hata).

**Senden gereken ilk kontrol:** `HudWalletSource`'un konsol satırı **iki sahnede de**
"live GameState" demeli. "save file" derse referans düşmüş.

---

#### Doğrulama yönteminde bulunan kusur (2026-08-20)

Adım 3 ve 5a'da yazdığım "0 hata" kanıtı **eksikti** ve iki derleme hatası oyuncuya
—yani kullanıcıya— kadar gitti (`CS0121`, `CS0104`). Sebep:

> Unity'nin ürettiği `.csproj` dosyaları derlenecek dosyaları `<Compile Include>`
> satırlarıyla **tek tek** listeliyor. Yeni yazılmış bir dosya, Unity onu içe alıp
> csproj'u yeniden üretene kadar o listede **yok** — derleme onu hiç görmüyor ve
> gönül rahatlığıyla "0 hata" diyor.

İkinci bir kusur da vardı: 5a'da MetaSystem'i güncel kaynaktan derledim ama testleri
**overload'ları içermeyen eski DLL'e** karşı derledim, o yüzden belirsizlik o anda
gerçekten yoktu.

**Bundan sonraki kural:** yeni dosya yazdıktan sonraki derleme, o dosyanın csproj'un
Compile listesinde göründüğü teyit edilmeden kanıt sayılmaz; ve bağımlılık sırası
değişince ALT assembly'ler de yeniden derlenir.

> **Sıra notu:** D-017'den sonra bu adımın ikinci tüketicisi kalmadı (Adım 6
> silindi). Gerekçesi değişmedi — cüzdanın tek yazıcısı ana ekranda da korunmalı
> ve kurulum kopyalanmamalı — ama `GameSession`'ın Day kataloğunu parse etmesi
> artık yalnızca `GameManager`'ın kendi ihtiyacı.

### ~~Adım 6~~ — SİLİNDİ (D-017)

Gün içeriği taraması. Açılma tek bir yazarlanmış gün numarasına indiği için bu
adımın konusu kalmadı — bkz. K2. Sonraki adımlar **yeniden numaralanmadı**:
`decisions.md` D-015 ve D-016 "Adım 7/9"a atıf yapıyor, numaraları kaydırmak o
atıfları yanlış yapardı. Numaralar etiket, sıra sayacı değil.

### [x] Adım 7 — Görünüm: arka plan + yuvalar + lokasyon geçişi + yerleştirme aracı

- `MetaBoardView` — **bakılan lokasyonun** arka planını basar, o lokasyonun her
  yuvası için bir Image spawn eder, `sortOrder` ile sıralar, aktifliğe göre
  sprite seçer
- Arka plan aspect-fill + normalize yuva çapalama (K5) — normalize pozisyon,
  farklı boyuttaki lokasyon arka planlarında da aynı veriyi geçerli kılar
- **Lokasyon geçişi** (K6): `< Expo Park >` çubuğu; yalnızca açık lokasyonlar
  arasında gezinir, ekran en yeni açık olanla açılır. Geçiş tahtayı yeniden kurar
- Kilitli bir sonraki lokasyon için "Gün N'de açılıyor" göstergesi
- ~~Editor yerleştirme aracı~~ → **Adım 2.5'te yapıldı** (Meta Editor penceresi)
- Meta1'in 19 pozisyonunun gerçekten yazarlanması (pencereden, sürükleyerek)

**Sonuç (2026-08-20).** `MetaGroundsView` (görünüm) + `MetaGroundsSetup`
(`ExpoTheExplorer > Meta > Build Meta Grounds`). Kayıt: D-023. **M10 kapandı:**
genişliğe oturt + clamp'li dikey kaydırma.

Dört karar: proplar katalogdan **runtime'da üretiliyor** (sahnede 19 elle konmuş nesne
katalogla sessizce çelişebilirdi); ölçmeden önce `Canvas.ForceUpdateCanvases()` —
`Start` karesinde stretched bir rect'in genişliği hâlâ **sıfır**, ölçsem bütün proplar
sıfır boyutta çıkar ve ekran boş görünür (hata vermez); proplar arka planın **çocuğu**
ve `normalizedPosition` doğrudan anchor'a yazılıyor, K5'in karşılığı bu — kaydırma için
prop başına hiçbir kod gerekmedi; ve geçiş yalnızca **açık** lokasyonlar arasında,
kilitli olan "Gün N'de açılıyor" ipucu olarak görünüyor.

**Doğrulama:** derleme, ve bu kez yeni dosyaların csproj'un Compile listesinde olduğu
**teyit edilerek** — bu oturumda iki hatanın kullanıcıya kadar gitmesine yol açan boşluk
tam olarak buydu.

Kalan: yuva pozisyonlarının yazarlanması (Meta Editor'den) ve sanat işi.

### [ ] Adım 8 — Dükkân UI'ı — **GERİ ALINDI 2026-08-21**

> **Yapıldı ve geri alındı (D-024 → D-027).** Kullanıcı farklı bir tasarım istiyor;
> dükkânda bir hata bulunmadı. `MetaShopView` ve `MetaShopSetup` silindi,
> `MetaGroundsView`'ın yalnızca onlar için büyüttüğü parçalar da (hayalet önizleme,
> `ViewedLocation`, `ViewedLocationChanged`, public `Refresh`) çıkarıldı.
>
> **Duran şeyler:** `MetaResolver`/`MetaPurchase` ve 25 testi (kurallar ekrandan
> bağımsız), `PlayerProfile.OwnedMetaItemIds` ve şema v4 (geri almak mevcut kayıtları
> okunamaz yapardı). Yani küme D-020'nin kaydettiği duruma döndü: var, ve onu yazan
> hiçbir şey yok.
>
> **`git reset` kullanılmadı:** dükkândan sonraki işleri (Continue Day X, reset
> düğmesi + 1000 coin) de silerdi.
>
> **Yeni tasarımın planı ayrı bir dosyada: `.claude/meta-shop-plan.md`** (2026-08-21).
> Kullanıcının tarif ettiği akış iki aşamalı — market düğmesi → liste → satır BUY ile
> hayalet + onay popup'ı → popup BUY ile satın alma — ve asıl işi satırları çizmek
> değil dört durumlu bir makineyi doğru kurmak. Aşağıdaki notlar o plana girdi olarak
> duruyor.

- `MetaShopView` — **bakılan lokasyonun** satın alınabilir ürünleri, fiyat,
  Satın Al butonu. Oyuncu neye bakıyorsa onu satın alır; kapalı bir lokasyona
  uzaktan alışveriş yok
- Durumlar: alınabilir / para yetmiyor / alan kilitli / zaten sahip
- Satın alma akışı: `MetaPurchase` → `Wallet.TrySpendSoftMoney` → `ProfileSaver`
  → tahtayı ve HUD'ı tazele
- ~~Açık soru: 16 ürünlük düz liste mi, kategorili mi (M5)~~ → **düz yatay şerit**,
  kaydırmalı, kategori yok. 16 ürün için sekme yapmak sürtünmeden başka bir şey değil.

**Sonuç (2026-08-20).** `MetaShopView` + `MetaShopSetup`
(`ExpoTheExplorer > Meta > Build Meta Shop`), artı `MetaGroundsView`'a hayalet
önizleme ve `ViewedLocationChanged` olayı. Kayıt: D-024. **Meta sistemi ilk kez
uçtan uca çalışıyor:** katalog → kurallar → cüzdan → kayıt dosyası → haritanın
yeniden çizilmesi.

Satın alma sırası: `Evaluate` → `Wallet.TrySpendSoftMoney` → `OwnedMetaItemIds.Add`
→ `Save()` → `Refresh()`. `Evaluate` zaten "Ok" dedi ama cüzdana **yine de**
soruluyor: bakiyenin tek yazıcısı o ve kendi cevabı otorite; ikisi ayrışırsa oyuncu
ödemediği bir şeye sahip olmasın.

**Akış yapısal olarak atomik, ve bu emekten değil önceki kararlardan çıktı:** para ve
sahiplik listesi aynı `GameSession`'da duruyor, `Save()` ikisini birlikte yazıyor.
Çökme olursa ikisi de kaybolur, kayıt olursa ikisi de yazılır — "parası gitti ama eşya
gelmedi" ifade edilemiyor.

Altı verdict'ten **üçü** ekrana çıkıyor; `AreaLocked` satırı ölü bir düğme yerine
"Needs Square" yazıyor, ki verdict'in bool olmamasının sebebi tam olarak bu.

**Kalan:** fiyatlar (M1) ve yuva pozisyonlarının yazarlanması.

### [ ] Adım 9 — Fiyat dengesi + içerik doldurma

- Meta1'in 16 ürününün gerçek fiyatları — **günlük tipik gelir rakamı
  gerektirir** (§5)
- Hangi yuvalar `Square` gerektiriyor
- StartBuilding'in fiyatı (ilk satın alma, oyunun ilk hedefi)
- Meta1'in `unlockAtDayIndex = 0`; Meta2'ninki sanatı geldiğinde (§5 M8)

### [ ] Adım 10 — Belgeler

- `ExpoTheExplorer/CLAUDE.md` Section 3'e "Meta / Expo Alanı" bölümü
- `fingerprint.md` veri otoriteleri: sahip olunan meta ürünler → profil;
  required prop açıklığı → Day içeriği (türetilmiş, kalıcı değil)
- `decisions.md` D-015 nihai hali
- `index.md` yeniden üretilir

---

## 5. Açık sorular — cevaplanmadan ilgili adım başlamaz

| # | Soru | Bloke ettiği adım |
|---|---|---|
| M1 | **Fiyatlar.** Tipik bir gün kaç SoftMoney kazandırıyor? Dekorlar kaç günde bir alınacak hissi vermeli? (Fiyat, D-011'in 20/10/5 tip oranlarıyla ve yiyecek `basePrice`'larıyla ölçeklenmeli.) | Adım 9 |
| M2 | **StartBuilding gerçekten satın alınıyor mu**, yoksa ilk gün bitince mi kayboluyor? Plan "satın alınır" varsayıyor — oyunun ilk hedefi olur. | Adım 2 |
| M5 | **Dükkân şekli:** 16 ürün düz liste mi, yoksa kategori sekmeleri / kaydırma mı? | Adım 8 |
| M6 | **Her şey alındığında ne olur?** Şu an bir bitiş durumu tasarlanmadı. | Adım 8 |
| M7 | Meydan (`Square`) hangi dekor yuvalarını açıyor — Main.png'nin alt yarısındaki çim alan mı? | Adım 9 |
| M8 | **Meta2 hangi Gün'de açılıyor?** Sanat henüz yok; sistem baştan hazır (K6), yalnızca sayı ve asset eksik. Sistemi bloke etmez — Meta1 tek lokasyonlu çalışır. | Meta2 asset'i |
| M9 | **Yeni lokasyon açıldığı an oyuncuya nasıl duyurulur?** (banner / animasyon / sadece çubukta belirir). Cila işi, sistemi bloke etmez. | Adım 7 |
| ~~M10~~ | **CEVAPLANDI (2026-08-20):** genişliğe oturt + clamp'li dikey kaydırma. Sanat 0.463, Canvas referansı 0.5625; kısa ekranlarda %18 taşma kaydırılarak gezilir, uzun telefonlarda kaydıracak bir şey olmaz. | ~~Adım 7~~ |

---

## 6. Kapsam dışı (bilerek)

- **Oynanış etkisi yok.** Dekorlar tamamen kozmetik — gelir çarpanı, hız bonusu,
  can yok. Kullanıcının kararı.
- **Gem meta tarafına girmiyor.** Gem yalnızca Continue'da kalıyor.
- **Serbest yerleştirme yok.** Sabit yuvalar (kullanıcının kararı).
- **Day-select haritası yok.** Play hâlâ kaldığın günü açıyor.
- **Powerup sistemi yok** — GDD'de zaten DEFERRED.
- **Meta için ikinci bir sahne yok** (K4).
- **Lokasyona özel ekonomi yok.** Tek cüzdan, tek fiyat ekseni; Meta2'nin
  "kendi parası" ya da lokasyon başına gelir çarpanı tasarlanmadı (K6-3).
- **Lokasyonlar arası taşınabilir dekor yok.** Her dekor ait olduğu lokasyonda
  kalır; Meta1'de alınan çeşme Meta2'ye taşınamaz.
- **Meta2'nin kendisi bu planın teslimatı değil.** Bu plan Meta2'yi *taşıyabilen*
  sistemi kuruyor; sanat gelince yazılacak kod değil, doldurulacak asset var.

---

## Ek: sıradaki Day Unlock propunun önizlemesi (D-040, 2026-08-21)

Kullanıcının isteği: açılmamış Day Unlock proplarından **sıradaki** haritada hayalet
olarak duruyor, gün ilerledikçe alttan yukarı katı hâline doğru doluyor, üstünde
tamamlanan yüzde yazıyor.

**Kural:** `MetaResolver.NextDayUnlock` → `MetaDayUnlockPreview` (prop + 0..1 ilerleme).
İlerleme = (bugün − geçilmiş son Day Unlock günü) / (hedef gün − aynı nokta); hiç
geçilmiş nokta yoksa 0'dan. **Yeni yazarlanan alan YOK.**

**Üç çatal kullanıcıya soruldu:** dolum başlangıcı (→ önceki dönüm noktası), kaç prop
(→ sadece sıradaki), yüzde yönü (→ tamamlanan).

**Sanat hazırlığı gerekmiyor:** tek sprite yeterli. İki katman — arkada soluk siluet,
üstünde `Image.Type.Filled` ile alttan yukarı dolan katı kısım. Tek `Image` ile hem
soluk hem kısmen dolu ifade edilemiyor, çünkü `fillAmount` kesiyor, saydamlaştırmıyor.

**Alan geçidi önizlemeye de uygulanıyor:** alanı alınmamış bir Day Unlock propu hiç
görünmüyor, çünkü onu bekleten zaman değil bir satın alma.

**Bilinen boşluk:** oyuncuya propun AÇILDIĞINI söyleyen hâlâ bir şey yok — önizleme
"geliyor"u anlatıyor, "geldi"yi anlatmıyor.

## Ek 2: açılış bir OLAY (D-041, 2026-08-21) — birinci yarı

Kullanıcının isteği iki parça: (a) açılış kutlaması, (b) açılış olduğunda ana menüye
zorunlu dönüş. **İkiye bölündü ve sıra tersine çevrildi**, çünkü (b) tek başına
kötüleşme olurdu: menüye zorla yollanıp karşılığında hiçbir şey görmemek.

**Bu adım (a):** ana ekrana nasıl dönüldüğünden bağımsız çalışıyor. Zoom in → prop
siluetten katıya yükseliyor → zoom out. Sıradaki propa geçmek için dokunulabilir
(atlanabilir).

- Kural: `MetaResolver.DayUnlocksBetween(location, owned, since, current)` — alt sınır
  **dışlayıcı**, yoksa her menü ziyaretinde aynı kutlama tekrar oynar.
- Şema **v5**: `LastCelebratedDayIndex`. **0 güvenli değil**, o yüzden gerçek bir göç
  var (eski dosyalarda `CurrentDayIndex`'e ayarlanıyor). Projenin v3'ten sonraki ikinci
  göçü.
- Tek yazıcı: `MetaGroundsView` — "gösterildi mi"yi ancak gösteren bilir.
- İşaret **bütün kuyruk bitince** yazılıyor; yarıda kesilirse bir dahaki açılışta
  tekrar oynar (kaybolmaz).
- Statik cross-scene alan kullanılmadı: devir teslim aracı kayıt dosyası, ve statik alan
  oyun arada kapanırsa kutlamayı sessizce yok ederdi.

**Sıradaki adım (b):** "Next Day" bir prop açacaksa gün sahnesinde kalmak yerine ana
ekrana yönlendirmek — böylece kutlama atlanamaz.

## Ek 3: zorunlu ana ekran dönüşü (D-042) — ikinci yarı, tamamlandı

"Next Day", girilecek gün bir Day Unlock propu açıyorsa gün sahnesinde kalmak yerine
**ana ekrana** yönlendiriyor. Böylece D-041'in kutlaması atlanamıyor.

- Davranış yeniden yazılmadı: `ReturnToMainScreenFromCompletedDay` zaten tam olarak
  bunu yapıyordu, ve `AdvanceToNextDay`'i bilerek çağırmıyor (sahne kapanırken tahta
  temizlemek boşa gider ya da yıkılan görünümlere olay kaskadı yollar). Yeni dal da
  ona uğramıyor.
- Karar `GameManager.TryContinueIntoNextDay`'de; popup tek soru soruyor ve yalnızca
  gizlenip gizlenmeyeceğine bakıyor.
- Sorulan lokasyon, meta ekranın AÇILACAĞI lokasyon — katalog düzeyindeki aşırı yükleme
  ikisini tek fonksiyona bağlıyor. "Zorla döndüm ama kutlama oynamadı" ifade edilemiyor.
- `GameManager`'a **opsiyonel** bir `MetaCatalog` alanı geldi; boşsa yönlendirme olmuyor
  ve `Awake` bunu bir kez logluyor.
- Asmdef değişikliği yok: `Bootstrap` ve `UI` `Assembly-CSharp`'ta, o da `MetaSystem`'e
  zaten erişiyor.

**Kullanıcı adımı:** `SampleScene`'de `GameManager`'a `Meta Catalog`'u sürüklemek.

**Test edilmeyen ve edilemeyen:** kararın kendisi (hangi sahnenin yükleneceği) — sahne
yükleyen bir MonoBehaviour. Test edilen şey kural.

## Ek 4: kutlamadan sonra sıradakine bakış (D-043)

Kutlama dizisi artık: açılan propa zoom → prop yükseliyor → **sıradakinin hayaletine
zoom + kısa bekleme** → zoom out.

- Yeni kural yok, yeni test yok — bakılan prop `NextDayUnlock`'un zaten döndürdüğü şey.
- Hayalet yeniden çizilmiyor: `Refresh` kutlamadan önce ve yeni gün indeksiyle koştuğu
  için ekrandaki hayalet zaten "açılan propun SONRAKİSİ" ve doğru oranda.
- **Zamanlama bedavaya anlamlı:** D-040 dolumu önceki açılıştan ölçtüğü için, bir prop
  açıldığı anda sıradaki tam **%0** okuyor. "Bunun sayacı şimdi başladı" mesajı ekstra
  metin olmadan çıkıyor.
- Bakışın kendi süresi var (`Celebration Next Peek Seconds`), kutlama beklemesine
  bindirilmedi: iki farklı beat.
- Atlama bayrağı bakış öncesi sıfırlanıyor — açılışı atlayan bakışı otomatik atlamıyor.

