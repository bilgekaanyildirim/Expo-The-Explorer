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

Sanat seti `Assets/Art/Meta1/` (20 PNG: 1 arka plan + 16 satın alınabilir + 3 required). Main.png **853×1844** — bu tam olarak
telefon dikey en-boy oranı (0.463), yani arka plan kaydırmaya gerek yok, bir
ekran dolusu.

> **Klasör adı tesadüf değil: bu ilk lokasyon.** Belli bir Gün'den sonra ikinci
> bir restoran gelecek (`Art/Meta2/`, sanatı henüz yok). Sistem baştan **çok
> lokasyonlu** kurulur — bkz. K6. Aşağıdaki tablo Meta1'in içeriğidir, sistemin
> tamamı değil.

| Sprite | Rol | Nasıl açılır |
|---|---|---|
| `Main.png` (853×1844) | Sahnenin arka planı. Bitmiş hotdog standı **zaten içine gömülü**. | Her zaman görünür, satılık değil |
| `StartBuilding.png` (516×458) | Standın üstünü kapatan **harabe bina**. Oyun bununla başlar. | Satın alınınca **silinir** (ters görünürlük) |
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

### K1 — Tek mekanizma: bir yuva = bir Image, iki sprite

Dört farklı ürün tipi (dekor / harabe / alan / required) için dört kod yolu
yazmak yerine, katalogdaki her kayıt **iki sprite** taşır:

```
MetaItemDefinition
  unownedSprite   ← aktif DEĞİLKEN gösterilen (çoğunda boş)
  ownedSprite     ← aktifKEN gösterilen (StartBuilding'de boş)
```

Görünüm kodunun tamamı: `image.sprite = active ? owned : unowned;
image.enabled = image.sprite != null;` — enum yok, switch yok.

| Ürün | unowned | owned |
|---|---|---|
| Fountain (dekor) | — | Fountain.png |
| StartBuilding (harabe) | StartBuilding.png | — |
| Square (alan) | — | Square.png |
| DrinkFridge (required) | — | DrinkFridge.png |

Geriye tek bir gerçek dallanma kalıyor: **aktiflik neyle kararlaştırılır.** İki
seçenek: `Purchase(price)` veya `DayContent(trigger)`. `abstraction-level.md`
gerekçesi: soyutlama tek bir alan (`unlock`), dört tip için dört sınıf değil.

### K2 — Required propların otoritesi Day içeriğidir, save dosyası değil

Bir Required propun açık olup olmadığı **kaydedilmez, türetilir**:

```
aktif  ⟺  0..CurrentDayIndex arasındaki herhangi bir Day'in ticket'larında
          o propun tetikleyici içeriğinden EN AZ BİRİ geçiyor
```

Üç prop var ve **üçü de farklı bir içerik kavramına bakıyor** — bu, tetikleyicinin
şeklini belirleyen esas gözlem:

| Prop | Kavram | Gerçek asset(ler) |
|---|---|---|
| **Fritöz** | tek bir yiyecek | `FoodData/Sides/Food_Fries.asset` |
| **DrinkFridge** | bir yiyecek *kategorisi* | `FoodData/Beverages/` — Cola, Fanta, Sprite + 3 milkshake |
| **SaucesStand** | bir *modifikasyon kümesi* | `Mod_ExtraKetchup`, `Mod_ExtraMayonnaise`, `Mod_ExtraMustard` |

> **Dikkat:** `FoodCategory` yalnızca `Main | Side | Drink` — **`Sauce` yok.**
> Soslar `ModificationConfig` olarak modellenmiş, yani sos standının tetikleyicisi
> bir yiyecek değil. Hotdog klasöründeki üç `Mod_Extra*` sos; Burger'in dördü
> (`ExtraCheese`, `ExtraPatty`, `NoLettuce`, `NoTomato`) değil.

**Tek mekanizma, üç isteğe bağlı alan.** Prop `DayContent` unlock'u şu üçünü
taşır ve **herhangi biri** eşleşirse açılır:

```
DayUnlockTrigger
  foods           FoodItemConfig[]      ← Fritöz: [Food_Fries]
  foodCategories  FoodCategory[]        ← DrinkFridge: [Drink]
  modifications   ModificationConfig[]  ← SaucesStand: [Ketchup, Mayo, Mustard]
  minDayIndex     int (-1 = kullanılmıyor)   ← düz "Gün N'de açılır"
```

Hepsi **asset referansı**, string değil — kod içine gömülü bir id ya da kategori
adı "content data is never embedded in code" invariant'ını ihlal ederdi, ve asset
referansı yeniden adlandırmaya dayanıklı.

`minDayIndex` neden var: kullanıcının sistemi özetleyişi ("bölüm gelince açılan
proplar") düz **gün numarası** okumasını da içeriyor, ve lokasyonun kendisi zaten
`unlockAtDayIndex` kullanıyor — kavram sistemde mevcut. Bir prop içerik
tetikleyicisi olmadan da "Gün 12'de belirir" diyebilmeli. Maliyeti bir alan ve
bir OR koşulu; boş bırakılırsa hiçbir şey değişmez. Meta1'in üç propu bunu
kullanmıyor.

`foodCategories` alanı neden var (tek başına `foods` yetmiyor mu): DrinkFridge'i
6 içeceği tek tek listeleyerek de yazarlayabilirdik, ama o zaman 7. içecek
eklendiğinde buzdolabı sessizce tetiklenmez — listeyi güncellemeyi unutmak bir
hata sınıfı. Buzdolabı sprite'ı zaten "bir sürü çeşit içecek" demek, yani
semantik olarak kategori doğru. Fritöz'ün tersi: kullanıcı özellikle *patates*
side'ı için istedi, tüm `Side` kategorisi için değil — o yüzden tek asset. Alan
başına bu seçim yazarındır; resolver'a maliyeti üç satır.

`CurrentDayIndex` oynanacak günü gösterdiği için, `≤ CurrentDayIndex` taraması
propu tam da o gün oynanmadan **önce** açar — kullanıcının istediği davranış,
`+1` gerekmeden.

Alternatif (reddedildi): açılan propları profile yazmak. İkinci otorite yaratır;
Day içeriği değişince kayıt yalan söyler.

**Maliyeti:** ana ekranın Day kataloğunu parse etmesi gerekir
(`DayJsonSource().LoadAll()` + `DayCatalogParser.ParseAll(files, foodCatalog)`).
İkisi de sahneye bağımlı değil, GameManager'ın zaten her gün sahnesinde yaptığı
iş. Frekans = **load** (×0.01), yani maliyet modeline göre önemsiz. K3'teki
`GameSession` çıkarımından sonra bu parse ana ekranda **zaten yapılıyor** olacak,
yani bu tetikleyici ek bir yükleme maliyeti getirmiyor — yalnızca yeni bir
`MetaSystem → DaySystem` oku ekliyor. Ok tek yönlü ve döngü yaratmıyor
(DaySystem → TicketSystem → EconomySystem).

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
| 4 | `dayCatalog = DayCatalogParser.ParseAll(...)` | ✅ (Adım 6 buna muhtaç) | ✅ |
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

**Bir nüans, bilerek böyle:** gün-açılımlı propların tetikleyicisi **global**
içeriğe bakar, lokasyonun kendi zaman çizgisine değil. Yani Meta2 Gün 12'de
açılırsa ve içecekler Gün 3'te girmişse, Meta2'nin buzdolabı **açılır açılmaz
oradadır**. Doğrusu bu: yeni restoran, hâlihazırda sattığın her şeyle birlikte
kurulur — açıldıktan sonra buzdolabının "gelmesini" beklemek anlamsız olurdu.

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
MetaSystem  →  DaySystem           (required prop tetikleyicisi: gün içeriği taraması)
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

### [ ] Adım 1 — Sanat ithal ayarları

20 PNG'nin import ayarları: Sprite (2D and UI), pivot (dekorların çoğu **bottom
center** olmalı — zeminle temas noktası orası), Pixels Per Unit, mipmap kapalı,
max size, sıkıştırma. Yanlış pivot bütün yuva pozisyonlarını yanlış yapar, o
yüzden yerleştirmeden **önce**.

`Main.png` ve `Square.png` büyük (853×1844 / 853×819) — atlas'a girmemeli, tek
tek kalmalı.

Ayrıca bu adımda: **`Required/Fritöz.png` → `Required/Fryer.png`** yeniden
adlandırma (ASCII dışı karakter, §1 notu). Şimdi bedava; katalog asset'i ona
referans verdikten sonra değil.

### [ ] Adım 2 — Veri katmanı: `MetaCatalog` + `MetaLocation`

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

### [ ] Adım 3 — Çekirdek mantık + testler (asmdef, saf C#)

Bu adım planın değer merkezi — Unity'siz, tamamen test edilebilir.

- `MetaResolver` — `(lokasyon, sahip olunan anahtarlar, gün içeriği gerçekleri)`
  → aktif id kümesi + satın alınabilir id kümesi (alan kilidini uygulayarak)
- **Sahiplik anahtarı** `$"{location.Id}.{item.Id}"` burada hesaplanır; katalog
  yerel id tutar, profil nitelenmiş anahtar tutar (K6-1)
- **Lokasyon kilidi:** `açık ⟺ CurrentDayIndex >= unlockAtDayIndex`, ve açık
  lokasyonların listesi + varsayılan görüntülenen (en yeni açık olan)
- `MetaPurchase` — "X alınabilir mi": zaten sahip mi, lokasyonu açık mı, alanı
  açık mı, parası yetiyor mu; her red için ayrı bir sebep
- EditMode testleri: `MetaSystemTests.cs` — iki sahte lokasyonla, tek lokasyonla
  yakalanamayacak anahtar çakışması ve kilit sızıntısı senaryoları dahil

Henüz ne kalıcılık, ne UI, ne sahne.

### [ ] Adım 4 — Kalıcılık: profil şeması v3 → v4

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

### [ ] Adım 5 — `GameSession` çıkarımı + tek yazıcı sınırı (K3) — **en riskli adım**

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

> **Sıra notu:** Adım 6 (gün içeriği tetikleyicisi) Day kataloğunu ana ekranda
> ister; `GameSession` onu zaten parse ettiği için Adım 6 bu adımın üstüne bedava
> oturuyor. Bu yüzden Adım 5, Adım 6'dan önce.

### [ ] Adım 6 — Gün içeriği tetikleyicisi (Required proplar)

- `DayContentFacts` — 0..CurrentDayIndex günlerindeki ticket'larda geçen food
  id'leri, **yiyecek kategorileri** ve modification id'leri (küçük, türetilmiş
  üç küme). Üç küme, K2'deki `DayUnlockTrigger`'ın üç içerik alanına birebir
  karşılık gelir; eşleşme "herhangi biri kesişiyor mu" sorusu. Dördüncü alan
  `minDayIndex` içerik taraması gerektirmez, doğrudan `CurrentDayIndex`'e bakar
- Girdisi **Adım 5'in `GameSession`'ının zaten parse ettiği katalog** — ana ekran
  ikinci bir parse yapmaz, `FoodCatalog` referansı da `GameSession` üzerinden
  gelir
- `MetaResolver`'a beslenir; `DayContent` unlock'lu kayıtlar buradan aktifleşir
- Meta1'in üç propunun tetikleyicileri yazarlanır (K2 tablosu): Fritöz →
  `Food_Fries`; DrinkFridge → `FoodCategory.Drink`; SaucesStand → üç
  `Mod_Extra{Ketchup,Mayonnaise,Mustard}`
- Testler: saf C#, sahte katalogla — üç tetikleyici şeklinin her biri için ayrı
  vaka, artı "tetiklenmemesi gereken içerik tetiklemiyor" (ör. `Mod_ExtraCheese`
  sos standını açmamalı)

### [ ] Adım 7 — Görünüm: arka plan + yuvalar + lokasyon geçişi + yerleştirme aracı

- `MetaBoardView` — **bakılan lokasyonun** arka planını basar, o lokasyonun her
  yuvası için bir Image spawn eder, `sortOrder` ile sıralar, aktifliğe göre
  sprite seçer
- Arka plan aspect-fill + normalize yuva çapalama (K5) — normalize pozisyon,
  farklı boyuttaki lokasyon arka planlarında da aynı veriyi geçerli kılar
- **Lokasyon geçişi** (K6): `< Expo Park >` çubuğu; yalnızca açık lokasyonlar
  arasında gezinir, ekran en yeni açık olanla açılır. Geçiş tahtayı yeniden kurar
- Kilitli bir sonraki lokasyon için "Gün N'de açılıyor" göstergesi
- **Editor yerleştirme aracı**: yuvaları sahnede sürükle → normalize pozisyonları
  ilgili `MetaLocation` asset'ine geri yaz
- Meta1'in 19 pozisyonunun gerçekten yazarlanması

### [ ] Adım 8 — Dükkân UI'ı

- `MetaShopView` — **bakılan lokasyonun** satın alınabilir ürünleri, fiyat,
  Satın Al butonu. Oyuncu neye bakıyorsa onu satın alır; kapalı bir lokasyona
  uzaktan alışveriş yok
- Durumlar: alınabilir / para yetmiyor / alan kilitli / zaten sahip
- Satın alma akışı: `MetaPurchase` → `Wallet.TrySpendSoftMoney` → `ProfileSaver`
  → tahtayı ve HUD'ı tazele
- Açık soru: 16 ürünlük düz bir liste mi, kategorili/kaydırmalı mı (§5)

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
| M3 | ~~SaucesStand'i hangi modifikasyon tetikliyor?~~ **Fiilen cevaplandı:** `Mod_ExtraKetchup` + `Mod_ExtraMayonnaise` + `Mod_ExtraMustard` (Hotdog klasörü). Burger'in dört modifikasyonu sos değil. Onay yeterli. | Adım 6 |
| M4 | ~~DrinkFridge tetikleyicisi?~~ **Önerilen:** `FoodCategory.Drink` kategorisi (6 içeceği tek tek listelemek yerine) — 7. içecek eklendiğinde sessizce tetiklenmeme hatasını kapatır. Onay yeterli. | Adım 6 |
| M4b | **Fritöz `Food_Fries`'a mı bağlı, tüm `Side` kategorisine mi?** Plan tek asset varsayıyor ("patates side'ını açsın diye"). İleride ikinci bir kızartma side'ı gelirse kategori daha doğru olabilir. | Adım 6 |
| M5 | **Dükkân şekli:** 16 ürün düz liste mi, yoksa kategori sekmeleri / kaydırma mı? | Adım 8 |
| M6 | **Her şey alındığında ne olur?** Şu an bir bitiş durumu tasarlanmadı. | Adım 8 |
| M7 | Meydan (`Square`) hangi dekor yuvalarını açıyor — Main.png'nin alt yarısındaki çim alan mı? | Adım 9 |
| M8 | **Meta2 hangi Gün'de açılıyor?** Sanat henüz yok; sistem baştan hazır (K6), yalnızca sayı ve asset eksik. Sistemi bloke etmez — Meta1 tek lokasyonlu çalışır. | Meta2 asset'i |
| M9 | **Yeni lokasyon açıldığı an oyuncuya nasıl duyurulur?** (banner / animasyon / sadece çubukta belirir). Cila işi, sistemi bloke etmez. | Adım 7 |

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
