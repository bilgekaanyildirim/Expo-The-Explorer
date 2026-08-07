# Day (Bölüm) Sistemi — Uygulama Roadmap'i

Bu doküman, sınırsız/sonsuz devam eden bilet üretimini **elle tasarlanmış, art arda gelen Day'ler** haline getirmek ve bu Day'leri düzenlemek için bir **Day Editor** (Unity Editor tooling) inşa etmek amacıyla PR-PR planı tanımlar. **Bu dosya sadece planlama amaçlıdır — henüz hiçbir koda dokunulmamıştır.**

Bu roadmap, 2026-08-07 tarihli kod tabanı incelemesine ve sonrasında yapılan tasarım oturumuna dayanıyor. CLAUDE.md Section 3/4/5 ile GDD ile çelişen hiçbir karar burada alınmadı; açık kalan noktalar ayrıca işaretlendi.

## Terminoloji Kararı

- **"Day"** kelimesi bu sistemde kullanılacak — "chapter/level/stage" değil. Gerekçe: kod tabanında zaten `DayLifecycleManager`, `DayCompleted`, `DayRetried`, `TicketsRequiredPerDay` gibi bir "gün" kavramı var; bu roadmap onu **tekil/sonsuz bir gün**den **sıralı, elle tasarlanmış Day'ler dizisi**ne genişletiyor.
- **"Level" kelimesi bu sistem için KULLANILMAYACAK.** Kod tabanında `LevelManager`, `LevelProgressionConfig`, `LevelView`, `state.Level`, `LevelUp` event'i zaten oyuncu XP-seviyesi anlamında var (bkz. `docs/LevelSystem_Roadmap.md`). Bu iki sistem birbirinden tamamen bağımsız — günlük konuşmada "level" kelimesi bölüm/Day anlamında geçebilir, ama kod/sınıf/alan isimlerinde geçmeyecek.

## İnceleme Bulguları (özet)

- `TicketSlotManager.DeliverTicket`/`CancelTicket`, boşalan her slotu koşulsuz olarak `AssignTicket` ile yeniden dolduruyor — bu döngüyü durduran hiçbir kod yok. Tek durma koşulu can bitmesi (`IsAwaitingContinue`).
- `DayLifecycleManager.RecordDelivery()`, `TicketsDeliveredToday == GameConfig.TicketsRequiredPerDay` olduğunda `DayCompleted` event'i atıyor ama **üretimi durdurmuyor** — teslimatlar hedefin üstünde saymaya devam ediyor (bunu doğrulayan bir EditMode testi var).
- Bilet içeriği tamamen prosedürel (`TicketFactory`) — elle yazılmış bilet yok. `TicketSlotManager`'ın `nextTicketProvider` delegate'i (constructor injection, `Func<Ticket>`) zaten var ve `GameManager.CreateNextTicket()`'a bağlı — bu, alternatif bir ticket-source enjekte etmek için doğal bir uzatma noktası; **`TicketSlotManager`'ın kendisi değişmeden** herhangi bir `Func<Ticket>` takılabilir.
- `BoardGrid.TryPlaceItem(item, x, y)` (belirli hücreye) ve `BoardGrid.RequestSpawn(item, random)` (herhangi boş hücreye/kuyruğa) zaten `BoardDistributor`'dan tamamen bağımsız, public primitifler — manuel board yerleştirme için **yeni bir engine primitive'i gerekmiyor**. `BoardDistributor.OnOrderPlaced` bunların üstünde sadece bir *politika* katmanı.
- Tüm balancing config'leri (`GameConfig`, `TicketGenerationConfig`, `BoardDistributionConfig`, `EconomyConfig`, `LivesConfig`) tekil, global, `GameManager`'a bir kere bağlanmış ScriptableObject asset'ler — Day başına farklı config seçimi yok.
- Zorluk düşürme (retry sonrası) kasıtlı olarak boş bırakılmış (`GameManager.RetryDay()` içinde açık yorum var) — CLAUDE.md Section 4 açık soru.
- `Assets/Editor/` altında sadece `[CustomEditor]` ile tek asset'in Inspector'ını genişleten scriptler var (`BoardDistributionConfigEditor`, `TicketGenerationConfigEditor`, `EconomyConfigEditor`, `FoodItemConfigEditor`). Day dizisini düzenleyecek bağımsız bir `EditorWindow` hiç yok.
- **Projede JSON tabanlı persistence zaten var:** `Assets/Scripts/Systems/ProgressionSystem/PlayerProfile.cs` + `PlayerProfileStore.cs`, `File.ReadAllText`/`WriteAllText` + `UnityEngine.JsonUtility.FromJson`/`ToJson` kalıbıyla. Projede Newtonsoft.Json **yok** (`Packages/manifest.json`'da sadece yerleşik `com.unity.modules.jsonserialize`) — yeni bağımlılık eklenmeyecek, `JsonUtility` kullanılacak.
- **Kritik `JsonUtility` kısıtı** (kod yorumunda açıkça yazılı, `PlayerProfile.cs`): `JsonUtility` sadece **public field**'ları (ya da `[SerializeField]` private field'ları) serileştirir — auto-property'ler sessizce `{}` olarak round-trip eder, hata vermez. Yeni Day JSON DTO'ları bu yüzden public field'lı plain class olacak.
- `FoodItemConfig`/`ModificationConfig`'in zaten bir `id` (`string`) alanı var ama **hiçbir id→asset lookup metodu yok** (`FoodCatalog` sadece ham `Items` listesi). JSON tabanlı Day verisi bu id'leri referans alacağı için bu lookup eklenmeli.

## Kapsam Dışı (bilinçli olarak hariç tutuldu)

- **Powerup System** — zaten deferred (CLAUDE.md Section 3).
- **Endless Mode** — GDD'den kaldırıldı, geri gelmiyor. Bu roadmap onu geri getirmiyor; mevcut "pratikte sonsuz davranan tek gün" durumunu kapatıp Daily Goal Mode'u **Day dizisi** olarak yapılandırıyor.
- **Oyuncu XP/Level sistemi** (`docs/LevelSystem_Roadmap.md`) — ayrı sistem, bu roadmap'te değiştirilmiyor. `DayCompleted`/`DayRetried` event imzaları/publish noktaları değişmeyecek, `LevelManager`'ın subscribe'ları bozulmayacak.
- **Zorluk düşürme sayılarının kesin tuning'i** — CLAUDE.md Açık Soru olarak kalıyor; bu roadmap sadece zorluk parametresinin **nerede** authored olacağını netleştirir (Day'in kendi retry variant'ı — bkz. Q3), sayıları kilitlemez.

## Açık Sorular — Cevaplandı

- **Q1 — Manuel bilet/board yapılandırma kapsamı → CEVAPLANDI: Hibrit model.** Manuel yapılandırma sadece bilet içeriğini değil, **board'a hangi item'ların spawn olacağını** da kapsar. Bir Day içinde **Auto** (mevcut random `TicketFactory`/`BoardDistributor` mantığı, Day'e özel config override'larıyla) ve **Authored** (tam elle yazılmış) girdiler **serbestçe iç içe geçebilir** — sıra kısıtı yok. Ayrıca bir Day'i önce otomatik **üretip**, sonucu elle **düzenleyip** ("bake" akışı) ince ayar yapılabilmesi gerekiyor — bkz. PR-6/PR-6.5.
- **Q2 — Son authored Day'den sonra ne olur → CEVAPLANDI.** Şu an ne main menu ne de gün-sonu popup'ı var. Kararlaştırılan davranış: gün-sonu popup'ındaki "Continue" butonu, son Day tamamlandığında **devre dışı** kalır — sadece "Main Menu" seçeneği aktif olur. Main menu normalde "Continue Day X" yazısı gösterir; tüm authored Day'ler bitmişse bu yazı **"End of Days"** olur. Bkz. yeni **PR-9**.
- **Q3 — Retry zorluk düşürme nereye oturacak → CEVAPLANDI.** Her Day'in kendi authored **"retry variant"ı** olacak — tasarımcı Day başına retry zorluğunu elle ayarlar (ayrı global bir `DifficultyScaler` değil).
- **Q4 — Day ilerlemesi kalıcı mı → CEVAPLANDI: Evet.** `CurrentDayIndex`, mevcut `PlayerProfile` JSON'una (XP/Level'ın yanına) eklenecek.

**Not — iki ayrı JSON kategorisi, birbirine karıştırılmayacak:**
- **(A) Oyuncu ilerleme kaydı** (`PlayerProfile`, mevcut dosya): `Xp`, `Level`, ve artık `CurrentDayIndex` — oyuncunun *hangi Day'de olduğunu* işaretleyen tek bir sayı.
- **(B) Day içerik verisi** (yeni, bu roadmap'in konusu): her Day'in biletleri/board spawn'ları/config override'ları — Day başına **ayrı bir `.json` dosyası**. (A) ile (B) hiçbir zaman aynı dosyada birleşmez; biri oyuncu kaydı, biri tasarımcı-authored içerik.

## Kilitli Kararlar

1. Day terminolojisi kullanılacak, "Level" kullanılmayacak.
2. **Day verisi ScriptableObject değil, JSON dosyaları olarak saklanacak** (kategori B, yukarıda) ve bir **Editor tab'i** (`EditorWindow`) üzerinden düzenlenecek — SO Inspector'ı yok, çünkü SO asset'i yok.
3. Bilet üretimi ve board spawn'ları Day başına **Auto + Authored hibrit** olacak, serbestçe interleave edilebilir.
4. Bir Day tamamlandığında bilet üretimi **gerçekten durmalı** — şu anki "hedefin üstünde saymaya devam et" davranışı kapatılacak.
5. Day dizisi sıralı ilerler: Day N tamamlanınca Day N+1'e geçilir. Retry, aynı Day'i (kendi authored retry variant'ıyla) tekrar başlatır — Day ilerlemesini geri almaz.
6. Mevcut mimari prensipler korunacak: Day çözümleme/hibrit içerik mantığı MonoBehaviour'dan bağımsız, unit-testable plain C# olacak; UI reaktif kalacak.

---

## JSON Veri Modeli (kategori B — Day içeriği)

### Depolama
- `Assets/Resources/Days/day_XX.json` — her Day kendi dosyasında. `Resources` altında olması, runtime'da platformdan bağımsız `Resources.LoadAll<TextAsset>("Days")` ile okunabilmesi için (Android'de `StreamingAssets` okuma sorunlarından bilerek kaçınılıyor).
- Day Editor, bu klasördeki dosyaları `File.ReadAllText`/`WriteAllText` ile doğrudan okuyup yazar, sonra `AssetDatabase.Refresh()` çağırır.

### DTO şekli (plain `[Serializable]` class, **public field**, enum'lar okunabilirlik için **string** — `JsonUtility`'nin varsayılan int-enum serileştirmesi yerine; dönüşüm resolver'da yapılır)

```csharp
[Serializable] public class DayJson
{
    public int dayIndex;
    public int ticketsRequiredForDay;
    public bool hasBoardDistributionOverride;
    public float noiseLeakCountLambdaOverride;
    public int guaranteedTicketCountOverride;
    public int leakDepthOverride;
    public int maxLeakCountOverride;
    public string boardSpawnMode;                 // "AutoOnly" | "AuthoredOnly" | "AuthoredThenAuto"
    public DayTicketEntryJson[] ticketSequence;    // ARRAY, List<T> değil (JsonUtility kısıtı)
    public AuthoredBoardSpawnJson[] authoredBoardSpawns;
    public DayJson retryVariant;                   // null olabilir — Day'in kendi retry zorluğu (Q3)
}
[Serializable] public class DayTicketEntryJson { public string mode; public AuthoredTicketEntryJson authored; }
[Serializable] public class AuthoredTicketEntryJson
{
    public string mainItemId; public string sideItemId; public string drinkItemId;
    public AuthoredModificationJson[] modifications;
    public string patienceType;                    // "Impatient" | "Normal" | "Patient"
    public string customerNameOverride;             // boş = random
    public float timeLimitSecondsOverride;          // <=0 = config default
}
[Serializable] public class AuthoredModificationJson { public string modificationId; public bool isAddition; }
[Serializable] public class AuthoredBoardSpawnJson
{
    public string itemId; public AuthoredModificationJson[] modifications;
    public bool useExactCell; public int x; public int y;
}
```

**Config override kararı:** `BoardDistributionConfig`/`TicketGenerationConfig` override'ları ayrı bir SO asset'e referans vermek yerine **doğrudan Day JSON'una gömülü sayısal alanlar** olarak tutulur — tamamen JSON-native, SO-referans-by-name çözümlemesi gibi ek bir dolaylılık gerekmiyor.

### Runtime çözümleme
- **`FoodCatalog.GetById(string id) : FoodItemConfig`** — yeni, basit LINQ lookup.
- **Modification id lookup** — `FoodCatalog.Items[*].AvailableModifications`'ı tarayıp `Dictionary<string, ModificationConfig>` inşa eden küçük bir yardımcı (yeni bir master `ModificationCatalog` asset'i icat etmeden).
- **`DayJsonSource`** (Unity'ye bağımlı ince katman): `Resources.LoadAll<TextAsset>("Days")` → ham JSON string listesi.
- **`DayCatalogParser`/`DayDefinitionResolver`** (plain C#, Unity'siz, **unit-testable**): ham JSON string'leri `JsonUtility.FromJson<DayJson>` ile parse eder, id'leri `FoodCatalog`/modification lookup üzerinden gerçek SO referanslarına çözer, `dayIndex`'e göre sıralar, runtime `DayDefinition` nesnelerini üretir. Testler, gerçek `Resources` klasörüne dokunmadan sabit JSON string fixture'larıyla çalışır.
- Çözülemeyen bir id (typo vb.) **sessizce yutulmaz** — yükleme sırasında `Debug.LogError` (dosya adı + hatalı id) ve Day Editor'da görsel uyarı.

### Hibrit ticket/board authoring mantığı
- `DayTicketSequenceProvider` — `ticketSequence`'i sırayla okur; `Auto` girdilerde `TicketFactory`'ye düşer (yeni 4-parametreli `Create(pool, name, patience, arrivalSequence)` overload'uyla — **tek arrival-sequence otoritesi kendisinde**, bkz. Risk 1); liste tükenince tamamen Auto fallback. `Authored` girdilerde `AuthoredTicketFactory.Create(resolved entry, ...)`.
- `DayBoardSpawnOrchestrator.ApplyAuthoredSpawns(board, spawns, random)` — mevcut `BoardGrid.TryPlaceItem`/`RequestSpawn` primitiflerini kullanır.
- `BoardSpawnMode`'a göre `GameManager.OnTicketAssigned`'da tek koşul: `AuthoredOnly` ise `boardDistributor.OnOrderPlaced` atlanır; `AutoOnly`/`AuthoredThenAuto`'da normal çalışır.
- **"Bake" akışı** (`DayContentGenerator`, plain C#, seedable `Random` parametresiyle): gerçek `TicketFactory`/`BoardDistributor` mantığını (izole bir `GameState`/`BoardGrid` üzerinde) çalıştırıp sonucu JSON DTO'ya (id'ler `.Id` ile çıkarılarak) donduran dönüştürücü. Day Editor'daki "Auto-Generate" butonu bunu çağırır — **gerçek runtime mantığını tekrar kullanır, kendi kopyasını çıkarmaz** (mevcut `FoodItemConfigEditor` preview deseniyle tutarlı).

---

## PR-1 — Day JSON Veri Modeli + Çözümleme (kategori B)

**Kapsam:**
- Yukarıdaki DTO'lar (`DayJson` ve alt tipleri), `FoodCatalog.GetById`, modification id lookup, `DayJsonSource`, `DayCatalogParser`/`DayDefinitionResolver`, `DayDefinition` runtime tipi.
- **Davranış değişikliği yok** — sadece veri okuma/çözümleme altyapısı, hiçbir sistem henüz bunu kullanmıyor.

**Bağımlılık:** Yok (paralel başlanabilir).

**Kabul kriteri:** Sabit JSON fixture string'i → doğru `DayDefinition` çözümlemesi; bilinmeyen id → `Debug.LogError` + dosya adı; EditMode testleriyle doğrulanmış.

---

## PR-2 — Day İlerleme Çekirdeği

**Kapsam:**
- `DayLifecycleManager` genişletilir (ya da yanına bir sıralama sınıfı eklenir): artık tek bir global hedef değil, **aktif `DayDefinition`**'dan `ticketsRequiredForDay` okuyor.
- `GameState`'e `CurrentDayIndex` alanı eklenir (custom setter → event publish, mevcut `Lives`/`SoftMoney` kalıbıyla aynı).
- `GameManager.Awake()`, PR-1'in `DayCatalogParser`'ından çözümlenmiş Day listesini alıp `CurrentDayIndex`'teki `DayDefinition`'ı seçer.

**Bağımlılık:** PR-1.

**Kabul kriteri:** Farklı `ticketsRequiredForDay` değerleriyle `DayCompleted`'in doğru sayıda teslimatta tetiklendiği unit testle doğrulanmış.

---

## PR-3 — Day Tamamlanınca Üretimi Gerçekten Durdur

**Neden ayrı PR:** Tespit edilen en kritik davranış boşluğu — şu an `DayCompleted` sadece XP commit'i tetikliyor, üretimi durdurmuyor.

**Kapsam:**
- `TicketSlotManager`'a bir "durdurulmuş" durumu eklenir (mevcut `IsAwaitingContinue` desenine benzer): `DayCompleted` geldiğinde `AssignTicket` çağrıları duraklar, aktif slotlardaki `Tick` sayaçları durur.
- `GameManager.OnDayCompleted` genişletilir: `LevelManager.CommitProgress()` çağrısına ek olarak üretimi durdurma + "Day tamamlandı" sinyalini UI'ya iletme (Day-Complete popup'ı PR-9'da).
- **Dikkat:** `LevelManager`'ın `DayCompleted`/`DayRetried` subscribe'ları bozulmaz — event imzaları/publish noktaları değişmez.

**Bağımlılık:** PR-2.

**Kabul kriteri:** Hedef teslimat sayısına ulaşıldıktan sonra `TicketSlotManager` yeni bilet atamıyor; `LevelManager` hâlâ doğru XP commit ediyor.

---

## PR-4 — Day Geçişi (Day N → Day N+1, Retry Davranışı)

**Kapsam:**
- PR-3'teki "durduruldu" durumundan çıkış: bir tetikleyici `CurrentDayIndex`'i artırır, sıradaki `DayDefinition`'ı yükler, board/tray/ticket slot'ları sıfırlar (can/XP'yi sıfırlamadan — Day başarıyla bitti, ceza yok).
- `GameManager.RetryDay()`: **aynı** `CurrentDayIndex`'i tekrar yükler ama Day'in `retryVariant` JSON verisi varsa onu kullanır (Q3 — Day'in kendi authored retry zorluğu).
- Son Day'den sonrasının davranışı (Q2) burada uygulanır: `CurrentDayIndex`, Day listesinin sonuna geldiğinde artık artmaz; "Continue" akışı PR-9'daki UI kararına göre devre dışı kalır.

**Risk — stale lookahead queue:** `TicketSlotManager.ResetSlotsForNewDay()` şu an `upcomingTickets` lookahead listesini temizlemiyor. Day geçişinde/retry'da önceki Day'in kuyruktaki biletleri yeni Day'e sızabilir — bu PR'a `TicketSlotManager.ClearUpcomingQueue()` (ya da `ResetSlotsForNewDay`'in bunu içermesi) eklenmesi gerekiyor.

**Bağımlılık:** PR-2, PR-3.

**Kabul kriteri:** Day 1 tamamlanınca Day 2'nin verisi devreye giriyor; retry, Day'in `retryVariant`'ını (varsa) yüklüyor; ilerleme geri gitmiyor; Day 2'nin ilk atanan biletleri Day 1'in kuyruğundan sızıntı içermiyor.

---

## PR-5 — Day Başına Config Override Enjeksiyonu

**Kapsam:**
- `GameManager`, `BoardDistributor`/`TicketFactory` kurulumunu artık sabit `[SerializeField]` asset yerine **aktif `DayDefinition`'ın gömülü JSON override alanları (varsa) + global default (yoksa)** ile yapar.
- `TicketGenerationConfig`'e `TimeLimitSecondsFor(PatienceType) : float` public helper'ı eklenir (şu an `TicketFactory.Create` içinde private switch olarak duran mantığın tek-kaynak haline getirilmesi) — authored ticket mapper'ın aynı GDD kuralına (Impatient < Normal < Patient) bağlı kalması için.

**Bağımlılık:** PR-1, PR-2.

**Kabul kriteri:** İki farklı Day JSON'u, farklı board-distribution override değerleriyle test edildiğinde board'a farklı noise/guaranteed davranışı yansıyor.

---

## PR-6 — Hibrit Day İçeriği (Authored Ticket + Authored Board Spawn)

**Kapsam:**
- `AuthoredTicketFactory.Create(entry, config, ticketFactory, arrivalSequence)` — authored JSON girdisini (id'leri çözülmüş haliyle) `Ticket`'a çevirir.
- `DayTicketSequenceProvider` — `TicketSlotManager`'ın `Func<Ticket> nextTicketProvider` seam'ine bağlanan, `ticketSequence`'i sırayla okuyan sağlayıcı (yukarıda detaylandırıldı). **`TicketSlotManager`'a hiç dokunulmuyor.**
- `DayBoardSpawnOrchestrator.ApplyAuthoredSpawns(...)` — authored board girdilerini `BoardGrid.TryPlaceItem`/`RequestSpawn` ile uygular; `State.Board.Clear()` sonrası, `TicketSlotManager` slot doldurmadan **önce** çağrılır (`BoardDistributor`'ın "required pool önce" kuralıyla çakışmaması için sıra önemli).
- `TicketFactory.Create`'e 4. parametre (`arrivalSequence`) alan ek overload — eski 3-parametreli çağrılar bozulmaz.

**Bağımlılık:** PR-1, PR-5.

**Kabul kriteri:** Authored + Auto karışık bir `ticketSequence` verilen bir Day'de, atanan biletlerin sırası/içeriği authored girdilerle tam eşleşiyor; `ArrivalSequence` değerleri hiç çakışmıyor (Risk 1).

---

## PR-6.5 — Day İçerik "Bake" (Üret-ve-Dondur) Akışı

**Kapsam:**
- `DayContentGenerator` (plain C#, Editor'a bağımlı değil, EditMode test edilebilir, seedable `Random`): `GenerateAuthoredTickets(...)` gerçek `TicketFactory.Create` mantığını çalıştırıp sonucu `AuthoredTicketEntryJson`'a dondurur; `GenerateAuthoredBoardSpawns(...)` izole bir `GameState`/`BoardGrid` üzerinde gerçek `BoardDistributor` mantığını çalıştırıp sonucu `AuthoredBoardSpawnJson`'a okur.
- **Kritik prensip:** gerçek runtime mantığı tekrar kullanılır, kopyalanmaz — Day Editor'daki "Auto-Generate" butonu asla gerçek oyun davranışından sapmaz.

**Bağımlılık:** PR-6 (authored veri tipleri/mapper'lar olmalı).

**Kabul kriteri:** Sabit seed ile `DayContentGenerator` üretimi, aynı seed'le `TicketFactory.Create`/`BoardDistributor` üretimiyle aynı sonucu veriyor (testle doğrulanmış).

---

## PR-7 — Day Editor (`EditorWindow`) — JSON'un biricik düzenleme arayüzü

**Kapsam:**
- Yeni `Assets/Editor/DayEditorWindow.cs`: `Assets/Resources/Days/*.json` dosyalarını listeler, oluşturur/kopyalar/siler, `dayIndex`'e göre sıralar (SO Inspector'ı yok, çünkü artık SO asset'i yok — bu pencere biricik düzenleme arayüzü).
- Seçili Day için: `ticketsRequiredForDay`, config override alanları (PR-5), `ticketSequence` editörü (her girdi için Auto/Authored toggle + authored alanlarında `FoodItemConfig`/`ModificationConfig` için Unity `ObjectField`'lar — dahili olarak seçilen asset'in `.Id`'si JSON'a yazılır), board-spawn listesi editörü (basit grid hücre seçici, `GameConfig.BoardWidth/Height`'a göre), `retryVariant` alt-editörü.
- "Auto-Generate Tickets/Board" butonları PR-6.5'i çağırır, sonucu ilgili alanlara yazar (kullanıcı sonra elle düzenleyebilir).
- Kaydet: `File.WriteAllText` (ilgili `Assets/Resources/Days/day_XX.json`) + `AssetDatabase.Refresh()`.
- Doğrulama/uyarı paneli: çözülemeyen id'ler ve `AuthoredOnly` board modunda Day'in en az bir biletinin tamamlanabilir olup olmadığı (gerçek eşleşme mantığıyla) kontrol edilip `HelpBox` ile gösterilir — GDD'nin "en az bir bilet her zaman tamamlanabilir olmalı" kuralı için oynanamaz Day riskine karşı.

**Bağımlılık:** PR-1 (minimum). PR-5/PR-6/PR-6.5 tamamlanmışsa editör onları da kullanabilir; erken başlayıp diğer PR'larla paralel/iteratif genişleyebilir.

**Kabul kriteri:** Bir tasarımcı, koda dokunmadan yeni bir Day JSON'u oluşturup sırasını/parametrelerini ayarlayabiliyor, otomatik üretip elle düzenleyebiliyor, kaydettiğinde dosya diskte doğru JSON olarak duruyor.

---

## PR-8 — Day İlerlemesinin Kalıcılığı (kategori A)

**Kapsam:**
- `PlayerProfile`'a `public int CurrentDayIndex;` eklenir (mevcut sınıfa doğrudan ek — Day'lerin kendi içeriğiyle, kategori B/PR-1, hiçbir dosya paylaşmaz).
- `PlayerProfileStore.Save` için şu an gerçek bir çağıran yok — bu PR aynı zamanda "gün başarıyla tamamlandı → kaydet" tetikleyicisini bağlar.
- Oyun açılışında `CurrentDayIndex` yüklenir, ilgili `DayDefinition` seçilir.

**Bağımlılık:** PR-2, PR-4.

**Kabul kriteri:** Oyunu kapat-aç → oyuncu son tamamladığı Day'in bir sonrasından başlıyor.

---

## PR-9 — Main Menu + Day-Complete / End-of-Days UI

**Kapsam (Q2 kararının uygulanması):**
- Minimal bir Day-Complete popup: "Continue" (bir sonraki Day'e geç) + "Main Menu" butonları. Son Day tamamlandığında "Continue" devre dışı, sadece "Main Menu" aktif.
- Minimal bir Main Menu ekranı: normalde "Continue Day X" metni; tüm authored Day'ler bitmişse "End of Days" metni.
- Mevcut `GameOverPopupView` deseni takip edilir (`Start()`'ta subscribe, `OnDestroy()`'da unsubscribe, `ValidateReferences()`).
- **Not:** proje şu an hiçbir Main Menu içermiyor — bu PR yeni bir UI katmanı ekliyor.

**Bağımlılık:** PR-4 (Day geçişi/son-Day tespiti), PR-8 (kalıcı `CurrentDayIndex` — "Continue Day X" hangi Day'i göstereceğini bilmek için).

**Kabul kriteri:** Son Day tamamlandığında popup'ta sadece "Main Menu" aktif; main menude "End of Days" metni görünüyor; ara bir Day'de ise "Continue Day X" doğru numarayla görünüyor.

---

## Sıra Özeti

```
PR-1 (JSON DTO + parser/resolver + FoodCatalog.GetById) ─┬─→ PR-2 → PR-3 → PR-4 (+ retry variant, + lookahead-queue fix)
                                                          ├─→ PR-5 (gömülü override enjeksiyonu) → PR-6 (hibrit içerik) → PR-6.5 (bake)
                                                          └─→ PR-7 (Day Editor — JSON'un biricik düzenleme arayüzü, erken başlar, paralel genişler)

PR-4 → PR-8 (Day ilerlemesi kalıcılığı, kategori A) → PR-9 (Main Menu + End of Days UI)
```

PR-1 diğer her şeyi bloke ediyor. PR-2/3/4 sıralı (Day döngüsünün çekirdeği). PR-5/6/6.5 (config/içerik) PR-2'den sonra bağımsız ilerleyebilir. PR-7 (Day Editor) PR-1'den sonra erken başlayıp diğer PR'larla birlikte genişletilebilir — tasarımcının elinde çalışan bir araç olması için önceliklendirilebilir. PR-9, PR-4 ve PR-8'i bekler.

## Risk/Not Bölümü

1. **Arrival-sequence çakışması** — Auto/Authored karışık girdilerde tek sayaç otoritesi şart; `DayTicketSequenceProvider` bu otoriteyi kendinde tutmalı, `TicketFactory`'nin kendi private sayacına asla bağımsız düşülmemeli (PR-6 sert gereksinim).
2. **Stale lookahead queue** — Day geçişinde önceki Day'in kuyruğu sızabilir (PR-4'e not, `TicketSlotManager.ClearUpcomingQueue()` gerekli).
3. **`AuthoredOnly` board modunda oynanamaz Day riski** — Day Editor'da doğrulama uyarısı (PR-7).
4. **`JsonUtility` kısıtları** — public field zorunlu (property çalışmaz, sessizce boş döner); iç içe class array'leri (`T[]`) sorunsuz ama `List<T>` yerine array kullanılmalı; enum'lar okunabilirlik için string olarak tutulup resolver'da çevrilecek.
5. **Id çözümleme hataları sessiz kalmamalı** — typo/eksik id, Day yüklemesinde açık hata logu + Editor'da görsel uyarı üretmeli.
6. **Bake sonrası "dondurulmuş" veri config rebalance'larını yansıtmaz** — Day Editor'da Authored/Auto rozeti ile görsel ayrım önerilir; bu bilinçli bir tasarım, hata değil.
