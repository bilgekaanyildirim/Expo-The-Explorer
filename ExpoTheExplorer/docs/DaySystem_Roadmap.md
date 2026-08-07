# Day (Bölüm) Sistemi — Uygulama Roadmap'i

Bu doküman, sınırsız/sonsuz devam eden bilet üretimini **elle tasarlanmış, art arda gelen Day'ler** haline getirmek ve bu Day'leri düzenlemek için bir **Day Editor** (Unity Editor tooling) inşa etmek amacıyla PR-PR planı tanımlar. **Bu dosya sadece planlama amaçlıdır — henüz hiçbir koda dokunulmamıştır.**

Bu roadmap, 2026-08-07 tarihli kod tabanı incelemesine ve sonrasında yapılan birkaç tasarım oturumuna dayanıyor. CLAUDE.md Section 3/4/5 ile GDD ile çelişen hiçbir karar burada alınmadı; açık kalan noktalar ayrıca işaretlendi.

## Terminoloji Kararı

- **"Day"** kelimesi bu sistemde kullanılacak — "chapter/level/stage" değil. Gerekçe: kod tabanında zaten `DayLifecycleManager`, `DayCompleted`, `DayRetried`, `TicketsRequiredPerDay` gibi bir "gün" kavramı var; bu roadmap onu **tekil/sonsuz bir gün**den **sıralı, elle tasarlanmış Day'ler dizisi**ne genişletiyor.
- **"Level" kelimesi bu sistem için KULLANILMAYACAK.** Kod tabanında `LevelManager`, `LevelProgressionConfig`, `LevelView`, `state.Level`, `LevelUp` event'i zaten oyuncu XP-seviyesi anlamında var (bkz. `docs/LevelSystem_Roadmap.md`). Bu iki sistem birbirinden tamamen bağımsız — günlük konuşmada "level" kelimesi bölüm/Day anlamında geçebilir, ama kod/sınıf/alan isimlerinde geçmeyecek.

## İnceleme Bulguları (özet)

- `TicketSlotManager.DeliverTicket`/`CancelTicket`, boşalan her slotu koşulsuz olarak `AssignTicket` ile yeniden dolduruyor — bu döngüyü durduran hiçbir kod yok. Tek durma koşulu can bitmesi (`IsAwaitingContinue`).
- `DayLifecycleManager.RecordDelivery()`, `TicketsDeliveredToday == GameConfig.TicketsRequiredPerDay` olduğunda `DayCompleted` event'i atıyor ama **üretimi durdurmuyor** — teslimatlar hedefin üstünde saymaya devam ediyor (bunu doğrulayan bir EditMode testi var).
- Bilet içeriği tamamen prosedürel (`TicketFactory`) — elle yazılmış bilet yok. `TicketSlotManager`'ın `nextTicketProvider` delegate'i (constructor injection, `Func<Ticket>`) zaten var ve `GameManager.CreateNextTicket()`'a bağlı — bu, alternatif bir ticket-source enjekte etmek için doğal bir uzatma noktası; **`TicketSlotManager`'ın kendisi değişmeden** herhangi bir `Func<Ticket>` takılabilir.
- `BoardGrid.TryPlaceItem(item, x, y)` (belirli hücreye) ve `BoardGrid.RequestSpawn(item, random)` (herhangi boş hücreye/kuyruğa) zaten `BoardDistributor`'dan tamamen bağımsız, public primitifler — manuel board yerleştirme için **yeni bir engine primitive'i gerekmiyor**. `BoardDistributor.OnOrderPlaced` bunların üstünde sadece bir *politika* katmanı — bu ayrım sayesinde `BoardDistributor`'ı runtime'dan çıkarıp yerine deterministik bir oynatıcı koymak mümkün (bkz. Q1).
- Tüm balancing config'leri (`GameConfig`, `TicketGenerationConfig`, `BoardDistributionConfig`, `EconomyConfig`, `LivesConfig`) tekil, global, `GameManager`'a bir kere bağlanmış ScriptableObject asset'ler — Day başına farklı config seçimi yok.
- Zorluk düşürme (retry sonrası) kasıtlı olarak boş bırakılmış (`GameManager.RetryDay()` içinde açık yorum var) — CLAUDE.md Section 4 açık soru.
- `Assets/Editor/` altında sadece `[CustomEditor]` ile tek asset'in Inspector'ını genişleten scriptler var (`BoardDistributionConfigEditor`, `TicketGenerationConfigEditor`, `EconomyConfigEditor`, `FoodItemConfigEditor`). Day dizisini düzenleyecek bağımsız bir `EditorWindow` hiç yok.
- **Projede JSON tabanlı persistence zaten var:** `Assets/Scripts/Systems/ProgressionSystem/PlayerProfile.cs` + `PlayerProfileStore.cs`, `File.ReadAllText`/`WriteAllText` + `UnityEngine.JsonUtility.FromJson`/`ToJson` kalıbıyla. Projede Newtonsoft.Json **yok** (`Packages/manifest.json`'da sadece yerleşik `com.unity.modules.jsonserialize`) — **Newtonsoft eklenmeyecek**, `JsonUtility` kullanılacak (kullanıcı onayı: şema tamamen düz array'lerden oluştuğu için `JsonUtility`'nin kısıtlarına takılmıyoruz).
- **Kritik `JsonUtility` kısıtı** (kod yorumunda açıkça yazılı, `PlayerProfile.cs`): `JsonUtility` sadece **public field**'ları (ya da `[SerializeField]` private field'ları) serileştirir — auto-property'ler sessizce `{}` olarak round-trip eder, hata vermez. Yeni Day JSON DTO'ları bu yüzden public field'lı plain class olacak.
- `FoodItemConfig`/`ModificationConfig`'in zaten bir `id` (`string`) alanı var ama **hiçbir id→asset lookup metodu yok** (`FoodCatalog` sadece ham `Items` listesi). JSON tabanlı Day verisi bu id'leri referans alacağı için bu lookup eklenmeli.

## Kapsam Dışı (bilinçli olarak hariç tutuldu)

- **Powerup System** — zaten deferred (CLAUDE.md Section 3).
- **Endless Mode** — GDD'den kaldırıldı, geri gelmiyor. Bu roadmap onu geri getirmiyor; mevcut "pratikte sonsuz davranan tek gün" durumunu kapatıp Daily Goal Mode'u **Day dizisi** olarak yapılandırıyor.
- **Oyuncu XP/Level sistemi** (`docs/LevelSystem_Roadmap.md`) — ayrı sistem, bu roadmap'te değiştirilmiyor. `DayCompleted`/`DayRetried` event imzaları/publish noktaları değişmeyecek, `LevelManager`'ın subscribe'ları bozulmayacak.
- **Zorluk düşürme sayılarının kesin tuning'i** — CLAUDE.md Açık Soru olarak kalıyor; bu roadmap sadece zorluk parametresinin **nerede** authored olacağını netleştirir (Day'in kendi retry variant'ı — bkz. Q3), sayıları kilitlemez.

## Açık Sorular — Cevaplandı

- **Q1 — Manuel bilet/board yapılandırma kapsamı → CEVAPLANDI: "Generate → elle düzenle" tek akışı, tamamen deterministik.** Hibrit/dual-mode bir sistem **değil**. Akış: (1) tasarımcı Day için ticket-generation/board-distribution benzeri ayarları girer, (2) **"Generate"** butonuna basar — gerçek `TicketFactory`/`BoardDistributor` mantığı, authored bilet dizisi boyunca **adım adım simüle edilerek** çalışır ve Day'in somut bilet dizisini **ve** somut, adıma-bağlı board spawn zaman çizelgesini üretir, (3) tasarımcı bunun üzerine **istediği kadar elle oynar**: ekstra bilet ekler, üretilmiş biletleri değiştirir/siler, board spawn'larının hangi adımda/hangi hücrede olacağını değiştirir — sıfırdan da elle oluşturabilir.
  - Üretilmiş ile elle yazılmış girdi arasında **hiçbir veri/tip farkı yok** — bilet için `TicketEntryJson`, board için `BoardSpawnEntryJson`, ikisi de tek şekil.
  - **Runtime'da hem bilet hem board tamamen deterministik oynatılır — rastgelelik yok.** `TicketFactory` ve `BoardDistributor` (Poisson noise leak, guaranteed-ticket seçimi) runtime'da **hiç çağrılmaz**; sadece Editor'daki "Generate" (`DayContentGenerator`) bunları kullanır. Bu sayede board override alanları da (`noiseLeakCountLambda`, `guaranteedTicketCount`, `leakDepth`, `maxLeakCount`) **tamamen editor-only** oluyor — tıpkı ticket-generation override'ları gibi, runtime hiçbirini okumuyor.
  - Her `BoardSpawnEntryJson`'a bir **`triggerStepIndex`** eklendi: "bu spawn, `ticketSequence`'in N. bileti bir slota atandığı anda uygulanır" (`-1` = Day başlangıcında, ilk bilet atanmadan önce). Runtime'da `GameManager.OnTicketAssigned`, `BoardDistributor.OnOrderPlaced` yerine deterministik `DayBoardTimelinePlayer`'ı çağırır.
  - Oynanamaz Day riski artık rastgelelik simülasyonu gerektirmeyen, **tam deterministik** bir `DayValidator` ile yakalanır (bkz. aşağıda) — runtime'la %100 aynı sonucu verir, sapma riski yok.
- **Q2 — Son authored Day'den sonra ne olur → CEVAPLANDI.** Şu an ne main menu ne de gün-sonu popup'ı var. Kararlaştırılan davranış: gün-sonu popup'ındaki "Continue" butonu, son Day tamamlandığında **devre dışı** kalır — sadece "Main Menu" seçeneği aktif olur. Main menu normalde "Continue Day X" yazısı gösterir; tüm authored Day'ler bitmişse bu yazı **"End of Days"** olur. Bkz. yeni **PR-9**.
- **Q3 — Retry zorluk düşürme nereye oturacak → CEVAPLANDI.** Her Day'in kendi authored **"retry variant"ı** olacak — tasarımcı Day başına retry zorluğunu elle ayarlar (ayrı global bir `DifficultyScaler` değil).
- **Q4 — Day ilerlemesi kalıcı mı → CEVAPLANDI: Evet.** `CurrentDayIndex`, mevcut `PlayerProfile` JSON'una (XP/Level'ın yanına) eklenecek.

**Not — iki ayrı JSON kategorisi, birbirine karıştırılmayacak:**
- **(A) Oyuncu ilerleme kaydı** (`PlayerProfile`, mevcut dosya): `Xp`, `Level`, ve artık `CurrentDayIndex` — oyuncunun *hangi Day'de olduğunu* işaretleyen tek bir sayı.
- **(B) Day içerik verisi** (yeni, bu roadmap'in konusu): her Day'in biletleri/board spawn timeline'ı — Day başına **ayrı bir `.json` dosyası**. (A) ile (B) hiçbir zaman aynı dosyada birleşmez; biri oyuncu kaydı, biri tasarımcı-authored içerik.

## Kilitli Kararlar

1. Day terminolojisi kullanılacak, "Level" kullanılmayacak.
2. **Day verisi ScriptableObject değil, JSON dosyaları olarak saklanacak** (kategori B, yukarıda) ve bir **Editor tab'i** (`EditorWindow`) üzerinden düzenlenecek — SO Inspector'ı yok, çünkü SO asset'i yok.
3. Bilet üretimi **ve** board spawn'ları **"Generate → elle düzenle" tek akışıyla, tamamen deterministik olarak** çalışacak (Q1). Runtime'da `TicketFactory`/`BoardDistributor` hiç çağrılmaz.
4. Bir Day tamamlandığında bilet üretimi **gerçekten durmalı** — şu anki "hedefin üstünde saymaya devam et" davranışı kapatılacak.
5. Day dizisi sıralı ilerler: Day N tamamlanınca Day N+1'e geçilir. Retry, aynı Day'i (kendi authored retry variant'ıyla) tekrar başlatır — Day ilerlemesini geri almaz.
6. Mevcut mimari prensipler korunacak: Day çözümleme/içerik mantığı MonoBehaviour'dan bağımsız, unit-testable plain C# olacak; UI reaktif kalacak.
7. **Oynanamaz Day riski, tam deterministik bir doğrulama sistemiyle (`DayValidator`) kapatılıyor** — tasarımcı elle her şeyi değiştirebilir, ama Editor kaydetmeyi engelleyerek bunu güvenli tutar; doğrulama rastgelelik içermediği için runtime davranışıyla her zaman bire bir örtüşür.

---

## JSON Veri Modeli (kategori B — Day içeriği)

### Depolama
- `Assets/Resources/Days/day_XX.json` — her Day kendi dosyasında. `Resources` altında olması, runtime'da platformdan bağımsız `Resources.LoadAll<TextAsset>("Days")` ile okunabilmesi için (Android'de `StreamingAssets` okuma sorunlarından bilerek kaçınılıyor).
- Day Editor, bu klasördeki dosyaları `File.ReadAllText`/`WriteAllText` ile doğrudan okuyup yazar, sonra `AssetDatabase.Refresh()` çağırır.

### Runtime/Editor ayrımı — tek dosya, iki bölüm
Hem ticket-generation override'ları (`sideInclusionChance` vb.) hem de board-distribution override'ları (`noiseLeakCountLambda` vb.) **sadece Day Editor'daki "Generate" butonunu besler** — runtime'da `TicketFactory` de `BoardDistributor` de hiç çağrılmadığı için oyun bu override'ların **hiçbirini** okumaz. Runtime sadece **sonucu** (authored `ticketSequence` + `authoredBoardSpawns` zaman çizelgesi) okur. Bu yüzden `DayJson` **iki bölüme** ayrılır: `runtime` (oyunun okuduğu her şey — artık sadece sonuç verisi) ve `editorMeta` (sadece Day Editor'ın "Generate" için kullandığı, oyunun asla bakmadığı üretim ayarları). Day başına **tek dosya** kalır.

### DTO şekli (plain `[Serializable]` class, **public field**, enum'lar okunabilirlik için **string** — dönüşüm resolver'da yapılır)

```csharp
[Serializable] public class DayJson
{
    public DayRuntimeJson runtime;
    public DayEditorMetaJson editorMeta;    // DayCatalogParser/runtime bunu HİÇ okumaz — sadece Day Editor kullanır
}
[Serializable] public class DayRuntimeJson
{
    public int dayIndex;
    public int ticketsRequiredForDay;
    public TicketEntryJson[] ticketSequence;          // tam olarak ticketsRequiredForDay uzunluğunda olmalı (DayValidator zorlar)
    public BoardSpawnEntryJson[] boardTimeline;        // triggerStepIndex'e göre sıralı, deterministik oynatım listesi
    public bool hasRetryVariant;                        // JsonUtility null'u nested class için asla gerçek null olarak round-trip etmiyor (bkz. Risk 8) — bu yüzden "var mı" sorusu bu sentinel ile sorulur
    public DayJson retryVariant;                        // hasRetryVariant=false ise içeriği anlamsız/yok sayılır; true ise Day'in kendi retry zorluğu (Q3), kendi runtime+editorMeta çiftini taşır
}
[Serializable] public class DayEditorMetaJson
{
    // Sadece "Generate" butonunu besler — runtime hiç okumaz.
    public bool hasTicketGenerationOverride;
    public float sideInclusionChanceOverride;
    public float drinkInclusionChanceOverride;
    public float modificationCountLambdaOverride;
    public bool hasBoardDistributionOverride;
    public float noiseLeakCountLambdaOverride;
    public int guaranteedTicketCountOverride;
    public int leakDepthOverride;
    public int maxLeakCountOverride;
}
[Serializable] public class TicketEntryJson
{
    public string mainItemId; public string sideItemId; public string drinkItemId;
    public ModificationEntryJson[] modifications;
    public string patienceType;                   // "Impatient" | "Normal" | "Patient"
    public string customerNameOverride;           // boş = random
    public float timeLimitSecondsOverride;         // <=0 = global TicketGenerationConfig default
}
[Serializable] public class ModificationEntryJson { public string modificationId; public bool isAddition; }
[Serializable] public class BoardSpawnEntryJson
{
    public int triggerStepIndex;   // -1 = Day başlangıcı (ilk bilet atanmadan önce); N = ticketSequence[N] bir slota atandığında
    public string itemId; public ModificationEntryJson[] modifications;
    public bool useExactCell; public int x; public int y;
}
```

**İsimlendirme notu:** "Authored" ön eki kullanılmıyor — generate edilmiş de elle yazılmış da girdi aynı şekli kullanır, hiçbir tip/alan farkı yok.

**Config override kararı:** Tüm override'lar (`editorMeta`) ayrı bir SO asset'e referans vermek yerine **doğrudan Day JSON'una gömülü sayısal alanlar** olarak tutulur — tamamen JSON-native.

### Runtime çözümleme
- **`FoodCatalog.GetById(string id) : FoodItemConfig`** — yeni, basit LINQ lookup.
- **Modification id lookup** — `FoodCatalog.Items[*].AvailableModifications`'ı tarayıp `Dictionary<string, ModificationConfig>` inşa eden küçük bir yardımcı (yeni bir master `ModificationCatalog` asset'i icat etmeden).
- **`DayJsonSource`** (Unity'ye bağımlı ince katman): `Resources.LoadAll<TextAsset>("Days")` → ham JSON string listesi.
- **`DayCatalogParser`/`DayDefinitionResolver`** (plain C#, Unity'siz, **unit-testable**): ham JSON string'leri `JsonUtility.FromJson<DayJson>` ile parse eder, **sadece `.runtime`'ı** okur, id'leri `FoodCatalog`/modification lookup üzerinden gerçek SO referanslarına çözer, `dayIndex`'e göre sıralar, runtime `DayDefinition` nesnelerini üretir. Testler, gerçek `Resources` klasörüne dokunmadan sabit JSON string fixture'larıyla çalışır.
- Çözülemeyen bir id (typo vb.) **sessizce yutulmaz** — yükleme sırasında `Debug.LogError` (dosya adı + hatalı id) ve Day Editor'da görsel uyarı (aynı `DayValidator` akışına dahil).

### İçerik oynatım mantığı — tamamen deterministik, runtime'da rastgelelik yok
- **`DayTicketSequenceProvider`** — `ticketSequence`'i sırayla `Ticket`'a çevirip verir. **Hiçbir fallback/otomatik üretim dalı yok** — `ticketSequence.Length == ticketsRequiredForDay` bir **veri garantisi** olarak kabul edilir (Editor save-time'da zorlanır). Tek, basit bir sayaç (liste index'i) — çakışma riski yok.
- **`DayBoardTimelinePlayer`** — `boardTimeline`'ı `triggerStepIndex`'e göre gruplar; `GameManager.OnTicketAssigned` her tetiklendiğinde "şu ana kadar kaç bilet atandı" sayacını kontrol eder ve eşleşen `triggerStepIndex`'li girdileri `BoardGrid.TryPlaceItem`/`RequestSpawn` ile uygular. Day başlangıcında (`Clear()` sonrası, ilk atamadan önce) `triggerStepIndex == -1` olan girdiler uygulanır. **`BoardDistributor.OnOrderPlaced` runtime'dan tamamen kaldırılır** — `GameManager` artık onu hiç çağırmaz.
- **`DayContentGenerator`** (plain C#, seedable `Random`, **sadece Editor-time çağrılır**, runtime bu sınıfı hiç bilmez): `editorMeta`'daki override'ları kullanarak (a) `TicketFactory.Create` ile `ticketSequence`'i üretir, (b) izole bir `GameState`/`BoardGrid` üzerinde, üretilen `ticketSequence`'i adım adım simüle ederek her adımda gerçek `BoardDistributor.OnOrderPlaced`'i çalıştırır ve ortaya çıkan her yeni board item'ını o adımın `triggerStepIndex`'iyle damgalayıp `boardTimeline`'a yazar. Yani "Generate", eskiden runtime'da canlı çalışan `BoardDistributor` davranışının **tek seferlik, offline bir simülasyonudur** — sonucu donmuş, deterministik bir zaman çizelgesi olarak kaydeder.
- İki çağıran: Editor'daki "Generate" (Day'i ilk kez doldurmak) ve "bir tane daha ekle" butonları.

### Day Doğrulama Sistemi (`DayValidator`) — tam deterministik
Tasarımcı elle düzenlerken Day'i oynanamaz hale getirebilir — bu **canlı** (her değişiklikte), save-time'ı beklemeden yakalanmalı.
- **`DayValidator`** (plain C#, Unity'siz, unit-testable) iki kontrol yapar:
  1. **Bilet sayısı kontrolü:** `ticketSequence.Length == ticketsRequiredForDay` mi? Değilse **hata** ("12 bilet authored ama 15 gerekiyor").
  2. **Oynanabilirlik simülasyonu — artık rastgelelik yok:** `boardTimeline`, `DayBoardTimelinePlayer`'ın runtime'da yapacağı **aynı deterministik oynatımla** adım adım "tekrar oynatılır" (gerçek `BoardDistributor` çağrılmaz, sadece authored veriler `triggerStepIndex` sırasına göre uygulanır) — sekansın **her noktasında** en az bir aktif biletin tamamlanabilir olduğu doğrulanır. Bozulursa **hata**, hangi adımda bozulduğu belirtilerek. Bu simülasyon artık `Random` gerektirmez ve **runtime'la bit-bit aynı sonucu verir** — önceki tasarımdaki "rastgelelik sapması" riski tamamen ortadan kalkar.
- Day Editor bu doğrulamayı her değişiklikte (debounce'lu) çalıştırır, durum çubuğunda gösterir (✓/✗ + hata listesi), **doğrulama başarısız olduğunda "Save" butonunu devre dışı bırakır.**

---

## PR-1 — Day JSON Veri Modeli + Çözümleme (kategori B)

**Kapsam:**
- Yukarıdaki DTO'lar (`DayJson`, `DayRuntimeJson`, `DayEditorMetaJson`, `TicketEntryJson`, `ModificationEntryJson`, `BoardSpawnEntryJson` — `triggerStepIndex` dahil), `FoodCatalog.GetById`, modification id lookup, `DayJsonSource`, `DayCatalogParser`/`DayDefinitionResolver` (sadece `.runtime`'ı okur), `DayDefinition` runtime tipi.
- **Davranış değişikliği yok** — sadece veri okuma/çözümleme altyapısı, hiçbir sistem henüz bunu kullanmıyor.

**Bağımlılık:** Yok (paralel başlanabilir).

**Kabul kriteri:** Sabit JSON fixture string'i → doğru `DayDefinition` çözümlemesi; bilinmeyen id → `Debug.LogError` + dosya adı; `editorMeta` alanları çözümlemeyi etkilemiyor; EditMode testleriyle doğrulanmış.

---

## PR-2 — Day İlerleme Çekirdeği ✅ Uygulandı

**Kapsam (gerçekleşen implementasyon):**
- `GameState`'e `CurrentDayIndex` eklendi — `Lives`/`Level` kalıbının birebir kopyası (backing field + guard'lı setter + `CurrentDayIndexChanged` event). `TicketsDeliveredToday`/`DayCompleted`'in yanına, "gün" state'iyle gruplandı. Varsayılan `0` (kalıcılık PR-8'de).
- `DayLifecycleManager`, `GameConfig` yerine **`Func<int> getTicketsRequiredForDay` enjeksiyonu** kullanacak şekilde değişti — `TicketSlotManager`'ın `Func<Ticket> nextTicketProvider` deseniyle birebir aynı yaklaşım. Bu, `DayLifecycleManager`'ın `DaySystem` tipini hiç bilmesine gerek kalmadan (asmdef referansı eklemeden) Day-özel değeri okuyabilmesini sağlıyor.
- Yeni `DayCatalogNavigator` (plain C#, `DaySystem` modülünde, `Assets/Scripts/Systems/DaySystem/DayCatalogNavigator.cs`): `GetDayAt(catalog, index)` — sınır-güvenli Day seçimi. PR-4'ün Day geçişi mantığı buraya eklenecek.
- `GameManager.Awake()`: `dayCatalog = DayCatalogParser.ParseAll(new DayJsonSource().LoadAll(), foodCatalog)` çağrılıyor, `CurrentDay` private property'si `DayCatalogNavigator.GetDayAt(dayCatalog, State.CurrentDayIndex)` ile hesaplanıyor. **Not:** `Assets/Scripts/Bootstrap/`'ın hiç asmdef'i yok (`Assembly-CSharp`'a derleniyor) — `DaySystem`'i kullanmak için hiçbir asmdef düzenlemesi gerekmedi.
- **Bootstrapping fallback (roadmap'te yazılı olmayan ama zorunlu bir karar):** `dayLifecycleManager = new DayLifecycleManager(State, () => CurrentDay?.TicketsRequiredForDay ?? gameConfig.TicketsRequiredPerDay);` — Day Editor (PR-7) henüz yokken `Assets/Resources/Days/` boş olacağı için `CurrentDay` `null` olur; bu durumda eski global `GameConfig.TicketsRequiredPerDay` davranışına düşülüyor. Bu sayede oyun PR-7'den önce de tamamen çalışır durumda kalıyor — PR-2/3/4/5/6 art arda test edilebilir.

**Bağımlılık:** PR-1.

**Kabul kriteri:** ✅ `DayLifecycleManagerTests.RecordDelivery_UsesInjectedDelegate_IndependentOfGameConfig` — `GameConfig`'in varsayılan `10`'unu görmezden gelen `() => 2` delegate'iyle `DayCompleted`'in tam 2. teslimatta tetiklendiği doğrulandı. `DayCatalogNavigatorTests` (5 test: geçerli/negatif/sınır/boş/null index) eklendi. Unity EditMode test suite'i (161 test) çalıştırıldı — sadece önceden var olan bağımsız flaky `TraySystemTests` testi hariç hepsi geçti.

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

## PR-5 — `TimeLimitSecondsFor` Helper

**Kapsam:**
- `TicketGenerationConfig`'e `TimeLimitSecondsFor(PatienceType) : float` public helper'ı eklenir (şu an `TicketFactory.Create` içinde private switch olarak duran mantığın tek-kaynak haline getirilmesi) — `TicketEntryFactory`'nin (PR-6) `timeLimitSecondsOverride <= 0` fallback'i bu global helper'ı çağırır, GDD'nin Impatient < Normal < Patient kuralına bağlı kalır.

**Not:** Bu PR'ın kapsamı daralmıştır — board-override enjeksiyonu artık gerekmiyor, çünkü `BoardDistributor` runtime'da hiç çalışmıyor (Q1 revizyonu). Küçüklüğü nedeniyle PR-6'ya katılabilir; ayrı tutuluyorsa bağımsız/paralel başlanabilir.

**Bağımlılık:** Yok.

**Kabul kriteri:** `TimeLimitSecondsFor` her `PatienceType` için config'teki doğru değeri döndürüyor; `TicketFactory.Create` bu helper'ı kullanacak şekilde refactor edilmiş (davranış değişmedi).

---

## PR-6 — Day İçerik Oynatımı (tamamen deterministik)

**Kapsam:**
- `TicketEntryFactory.Create(entry, arrivalSequence)` — JSON girdisini (id'leri çözülmüş haliyle) `Ticket`'a çevirir; `timeLimitSecondsOverride <= 0` ise PR-5'in `TimeLimitSecondsFor` helper'ına düşer. Generate edilmiş de elle yazılmış da aynı yoldan geçer.
- `DayTicketSequenceProvider` — `TicketSlotManager`'ın `Func<Ticket> nextTicketProvider` seam'ine bağlanan, `ticketSequence`'i sırayla okuyan sağlayıcı. **Fallback/otomatik üretim dalı yok.** **`TicketSlotManager`'a hiç dokunulmuyor.**
- `DayBoardTimelinePlayer.ApplyForStep(board, boardTimeline, stepIndex)` — `boardTimeline`'daki `triggerStepIndex == stepIndex` olan girdileri `BoardGrid.TryPlaceItem`/`RequestSpawn` ile uygular.
- **`GameManager.OnTicketAssigned` artık `boardDistributor.OnOrderPlaced(...)` çağırmıyor** — bunun yerine `DayBoardTimelinePlayer.ApplyForStep(...)` çağrılıyor, "kaçıncı bilet atandı" sayacıyla. Day başlangıcında (`State.Board.Clear()` sonrası, ilk atamadan önce) `triggerStepIndex == -1` grubu uygulanır.
- `BoardDistributor` sınıfı **koddan silinmiyor** — sadece runtime çağrı zincirinden çıkarılıyor; tek çağıranı PR-6.5'teki `DayContentGenerator` oluyor.

**Bağımlılık:** PR-1, PR-5.

**Kabul kriteri:** Bir Day'de atanan biletlerin sırası/içeriği `ticketSequence`'le tam eşleşiyor; board'daki spawn'lar `boardTimeline`'daki `triggerStepIndex`'lerle tam eşleşiyor; `BoardDistributor.OnOrderPlaced`'in artık `GameManager`'dan çağrılmadığı testle doğrulanmış.

---

## PR-6.5 — Day İçerik Üretici (`DayContentGenerator`)

**Kapsam:**
- `DayContentGenerator` (plain C#, Editor'a bağımlı değil, EditMode test edilebilir, seedable `Random`): `editorMeta`'daki override'ları kullanarak (a) `TicketFactory.Create` ile bilet dizisini üretir, (b) izole bir `GameState`/`BoardGrid` üzerinde üretilen bilet dizisini adım adım simüle edip her adımda gerçek `BoardDistributor.OnOrderPlaced`'i çalıştırarak, ortaya çıkan yeni item'ları o adımın `triggerStepIndex`'iyle damgalayıp `boardTimeline`'a yazar.
- **Kritik prensip:** gerçek runtime mantığı (bir zamanlar runtime'da canlı çalışan `BoardDistributor`) tekrar kullanılır, kopyalanmaz — sadece artık offline/tek seferlik simülasyon olarak çalışır.
- **Sadece Editor-time çağrılır** — runtime bu sınıfı hiç bilmez.

**Bağımlılık:** PR-6 (veri tipleri/mapper'lar olmalı).

**Kabul kriteri:** Sabit seed ile üretilen `boardTimeline`, aynı seed'le adım adım çalıştırılan gerçek `TicketFactory`/`BoardDistributor` çağrılarıyla üretilen sonuçla birebir eşleşiyor (testle doğrulanmış).

---

## PR-6.6 — Day Doğrulayıcı (`DayValidator`)

**Kapsam:**
- `DayValidator` (plain C#, Unity'siz, unit-testable, **rastgelelik gerektirmez**): (1) bilet-sayısı kontrolü (`ticketSequence.Length == ticketsRequiredForDay`), (2) oynanabilirlik simülasyonu — `boardTimeline`'ı `DayBoardTimelinePlayer` ile aynı deterministik mantıkla adım adım oynatıp (gerçek `BoardDistributor` çağrılmaz) sekansın her noktasında en az bir aktif biletin tamamlanabilir olduğunu doğrular.
- Runtime'la bit-bit aynı sonucu verir — sapma riski yok.

**Bağımlılık:** PR-6 (veri şekli + `DayBoardTimelinePlayer`).

**Kabul kriteri:** Kasıtlı olarak bozuk bir Day (eksik bilet sayısı ya da tamamlanamaz bir adım) testte doğru hata mesajıyla yakalanıyor; geçerli bir Day sorunsuz geçiyor.

---

## PR-7 — Day Editor (`EditorWindow`) — JSON'un biricik düzenleme arayüzü

**Kapsam:**
- Yeni `Assets/Editor/DayEditorWindow.cs`: `Assets/Resources/Days/*.json` dosyalarını listeler, oluşturur/kopyalar/siler, `dayIndex`'e göre sıralar.
- Seçili Day için `runtime` alanları (`ticketsRequiredForDay`, `ticketSequence` editörü, `boardTimeline` editörü — her girdi için bir `triggerStepIndex` seçici: "Day Start" ya da belirli bir bilet adımı, `retryVariant` alt-editörü) **ve** `editorMeta` alanları (ticket-generation + board-distribution override'ları) aynı pencerede düzenlenir — tasarımcı için tek arayüz, dosyadaki iki-bölüm ayrımı UI'da görünmez.
- "Generate" (config'e göre N bilet + tam board timeline üret) ve "Add Manually" (main/side/drink/mod `ObjectField`'larıyla boş bilet girdisi / board spawn girdisi ekle) butonları PR-6.5'i çağırır.
- **`DayValidator`'ı (PR-6.6) her değişiklikte (debounce'lu) canlı çalıştırır**, sonucu bir durum çubuğunda gösterir (✓/✗ + hata listesi), **doğrulama başarısız olduğunda "Save" butonunu devre dışı bırakır.**
- Kaydet: `File.WriteAllText` (ilgili `Assets/Resources/Days/day_XX.json`) + `AssetDatabase.Refresh()`.

**Bağımlılık:** PR-1 (minimum). PR-5/PR-6/PR-6.5/PR-6.6 tamamlanmışsa editör onları da kullanabilir; erken başlayıp diğer PR'larla paralel/iteratif genişleyebilir.

**Kabul kriteri:** Bir tasarımcı, koda dokunmadan yeni bir Day JSON'u oluşturup sırasını/parametrelerini ayarlayabiliyor, otomatik üretip elle düzenleyebiliyor (bilet + board timeline dahil), geçersiz bir Day'i kaydedemiyor, geçerli olduğunda dosya diskte doğru JSON olarak duruyor.

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
PR-1 (JSON DTO [runtime+editorMeta] + parser/resolver + FoodCatalog.GetById) ─┬─→ PR-2 → PR-3 → PR-4 (+ retry variant, + lookahead-queue fix)
                                                                              ├─→ PR-5 (TimeLimitSecondsFor) → PR-6 (deterministik oynatım) → PR-6.5 (generator) → PR-6.6 (validator)
                                                                              └─→ PR-7 (Day Editor — Generate + Add Manually + canlı doğrulama, PR-6.5/6.6'ya bağlı)

PR-4 → PR-8 (Day ilerlemesi kalıcılığı, kategori A) → PR-9 (Main Menu + End of Days UI)
```

PR-1 diğer her şeyi bloke ediyor. PR-2/3/4 sıralı (Day döngüsünün çekirdeği). PR-5/6/6.5/6.6 (içerik/doğrulama) PR-2'den sonra bağımsız ilerleyebilir. PR-7 (Day Editor) PR-1'den sonra erken başlayıp diğer PR'larla birlikte genişletilebilir. PR-9, PR-4 ve PR-8'i bekler.

## Risk/Not Bölümü

1. **Arrival-sequence çakışması — risk değil.** Tek liste + tek sayaç (`DayTicketSequenceProvider`), fallback dalı yok — çakışma imkânsız.
2. **Stale lookahead queue** — hâlâ geçerli risk (PR-4): Day geçişinde önceki Day'in kuyruğu sızabilir, `TicketSlotManager.ClearUpcomingQueue()` gerekli.
3. **Oynanamaz Day riski — `DayValidator` (PR-6.6) ile canlı ve tam deterministik yakalanıyor.** `BoardDistributor` artık runtime'da hiç çalışmadığı için (Q1 revizyonu) bu risk tamamen authoring-time bir doğrulama konusu; rastgelelik sapması riski de yok — validator'ın simülasyonu runtime'la birebir aynı.
4. **`JsonUtility` kısıtları** — public field zorunlu (property çalışmaz, sessizce boş döner); iç içe class array'leri (`T[]`) sorunsuz ama `List<T>` yerine array kullanılmalı; enum'lar okunabilirlik için string olarak tutulup resolver'da çevrilecek. **Newtonsoft.Json eklenmeyecek** (kullanıcı onayı).
5. **Id çözümleme hataları sessiz kalmamalı** — typo/eksik id, Day yüklemesinde açık hata logu + Editor'da görsel uyarı (aynı `DayValidator` akışına dahil edilebilir).
6. **`triggerStepIndex` tutarlılığı** — bir `TicketEntryJson` silinir/reorder edilirse, ona bağlı `triggerStepIndex`'li board girdileri de kayabilir/anlamsızlaşabilir. Day Editor (PR-7), bilet silme/reorder işlemlerinde bağlı `boardTimeline` girdilerini otomatik güncellemeli ya da en azından `DayValidator` üzerinden bunu yakalamalı.
7. **`DayContentGenerator`'ın simülasyonu potansiyel olarak ağır bir işlem** (tüm ticket dizisini adım adım simüle ediyor) — büyük Day'lerde (uzun `ticketSequence`) performans PR-6.5 sırasında ölçülmeli. `DayValidator`'ın canlı çalışması da benzer şekilde debounce'lu/odak-kaybında tetiklenmeli (PR-7).
8. **`JsonUtility` nested-class null kısıtı — PR-1'de gerçek testle doğrulandı.** `JsonUtility`, bir nested `[Serializable]` class field'ının `null` değerini asla gerçek `null` olarak round-trip etmiyor — her zaman default-constructed bir instance'a dönüştürüyor. Bu, `DayRuntimeJson.retryVariant`'a saf bir null-check koymayı imkânsız kılıyor (her zaman "var" gibi görünür). Çözüm: `hasRetryVariant` (bool) sentinel eklendi — mevcut `hasBoardDistributionOverride`/`hasTicketGenerationOverride` desenine tutarlı. **Bundan sonra eklenecek her "opsiyonel nested object" alanı için aynı sentinel deseni kullanılmalı**, saf null-check'e güvenilmemeli.
