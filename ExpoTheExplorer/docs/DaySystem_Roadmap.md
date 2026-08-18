# Day (Bölüm) Sistemi — Uygulama Roadmap'i

Bu doküman, sınırsız/sonsuz devam eden bilet üretimini **elle tasarlanmış, art arda gelen Day'ler** haline getirmek ve bu Day'leri düzenlemek için bir **Day Editor** (Unity Editor tooling) inşa etmek amacıyla PR-PR planı tanımlar. **Bu dosya sadece planlama amaçlıdır — henüz hiçbir koda dokunulmamıştır.**

Bu roadmap, 2026-08-07 tarihli kod tabanı incelemesine ve sonrasında yapılan birkaç tasarım oturumuna dayanıyor. CLAUDE.md Section 3/4/5 ile GDD ile çelişen hiçbir karar burada alınmadı; açık kalan noktalar ayrıca işaretlendi.

## Terminoloji Kararı

- **"Day"** kelimesi bu sistemde kullanılacak — "chapter/level/stage" değil. Gerekçe: kod tabanında zaten `DayLifecycleManager`, `DayCompleted`, `DayRetried`, `TicketsRequiredPerDay` gibi bir "gün" kavramı var; bu roadmap onu **tekil/sonsuz bir gün**den **sıralı, elle tasarlanmış Day'ler dizisi**ne genişletiyor.
- **"Level" kelimesi bu sistem için KULLANILMAYACAK.** Bu kural yazıldığında gerekçe, kod tabanında `LevelManager`/`LevelView`/`state.Level`'in oyuncu XP-seviyesi anlamında zaten var olmasıydı. **2026-08-18: o sistem tamamen silindi** (`.claude/decisions.md` D-009), yani isim çakışması artık yok — ama kural aynen duruyor: bölüm/aşama kavramının adı **Day**'dir, kod/sınıf/alan isimlerinde "level" geçmeyecek.

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
- ~~**Oyuncu XP/Level sistemi**~~ — **2026-08-18: bu sistem oyundan tamamen kaldırıldı** (`.claude/decisions.md` D-009); `LevelSystem_Roadmap.md`, `LevelManager` ve `GameManager`'daki `DayCompleted`/`DayRetried` handler'ları silindi. `DayCompleted` hâlâ publish ediliyor ve `DayCompletePopupView` onu dinliyor; `DayRetried`'ın şu an hiç abonesi yok.
- **Zorluk düşürme sayılarının kesin tuning'i** — CLAUDE.md Açık Soru olarak kalıyor; bu roadmap sadece zorluk parametresinin **nerede** authored olacağını netleştirir (Day'in kendi retry variant'ı — bkz. Q3), sayıları kilitlemez.

## Açık Sorular — Cevaplandı

- **Q1 — Manuel bilet/board yapılandırma kapsamı → CEVAPLANDI: "Generate → elle düzenle" tek akışı, tamamen deterministik.** Hibrit/dual-mode bir sistem **değil**. Akış: (1) tasarımcı Day için ticket-generation/board-distribution benzeri ayarları girer, (2) **"Generate"** butonuna basar — gerçek `TicketFactory`/`BoardDistributor` mantığı, authored bilet dizisi boyunca **adım adım simüle edilerek** çalışır ve Day'in somut bilet dizisini **ve** somut, adıma-bağlı board spawn zaman çizelgesini üretir, (3) tasarımcı bunun üzerine **istediği kadar elle oynar**: ekstra bilet ekler, üretilmiş biletleri değiştirir/siler, board spawn'larının hangi adımda/hangi hücrede olacağını değiştirir — sıfırdan da elle oluşturabilir.
  - Üretilmiş ile elle yazılmış girdi arasında **hiçbir veri/tip farkı yok** — bilet için `TicketEntryJson`, board için `BoardSpawnEntryJson`, ikisi de tek şekil.
  - **Runtime'da hem bilet hem board tamamen deterministik oynatılır — rastgelelik yok.** `TicketFactory` ve `BoardDistributor` (Poisson noise leak, guaranteed-ticket seçimi) runtime'da **hiç çağrılmaz**; sadece Editor'daki "Generate" (`DayContentGenerator`) bunları kullanır. Bu sayede board override alanları da (`noiseLeakCountLambda`, `guaranteedTicketCount`, `leakDepth`, `maxLeakCount`) **tamamen editor-only** oluyor — tıpkı ticket-generation override'ları gibi, runtime hiçbirini okumuyor.
  - Her `BoardSpawnEntryJson`'a bir **`triggerStepIndex`** eklendi: "bu spawn, `ticketSequence`'in N. bileti bir slota atandığı anda uygulanır" (`-1` = Day başlangıcında, ilk bilet atanmadan önce). Runtime'da `GameManager.OnTicketAssigned`, `BoardDistributor.OnOrderPlaced` yerine deterministik `DayBoardTimelinePlayer`'ı çağırır.
  - Oynanamaz Day riski artık rastgelelik simülasyonu gerektirmeyen, **tam deterministik** bir `DayValidator` ile yakalanır (bkz. aşağıda) — runtime'la %100 aynı sonucu verir, sapma riski yok.
- **Q2 — Son authored Day'den sonra ne olur → CEVAPLANDI.** Şu an ne main menu ne de gün-sonu popup'ı var. Kararlaştırılan davranış: gün-sonu popup'ındaki "Continue" butonu, son Day tamamlandığında **devre dışı** kalır — sadece "Main Menu" seçeneği aktif olur. Main menu normalde "Continue Day X" yazısı gösterir; tüm authored Day'ler bitmişse bu yazı **"End of Days"** olur. Bkz. yeni **PR-9**.
- **Q3 — Retry zorluk düşürme nereye oturacak → CEVAPLANDI.** Her Day'in kendi authored **"retry variant"ı** olacak — tasarımcı Day başına retry zorluğunu elle ayarlar (ayrı global bir `DifficultyScaler` değil).
- **Q4 — Day ilerlemesi kalıcı mı → CEVAPLANDI: Evet, ama HENÜZ YAPILMADI.** `CurrentDayIndex`, `PlayerProfile` JSON'una eklenecek. *(2026-08-18 güncellemesi: XP/Level kaldırılınca `PlayerProfile` tamamen boşaldı ve `GameManager` artık onu hiç yüklemiyor — yani bu alan eklendiğinde kalıcılığın bağlantısı da sıfırdan kurulacak ve root `CLAUDE.md` invariantı gereği yanına bir `Version` alanı gerekiyor. Bkz. `.claude/economy-plan.md` Adım 4.)*

**Not — iki ayrı JSON kategorisi, birbirine karıştırılmayacak:**
- **(A) Oyuncu ilerleme kaydı** (`PlayerProfile`, mevcut dosya): bugün **boş**; içine girecek ilk alan `CurrentDayIndex` — oyuncunun *hangi Day'de olduğunu* işaretleyen tek bir sayı. (Eskiden `Xp`/`Level` taşıyordu, D-009'da silindi.)
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

## PR-3 — Day Tamamlanınca Üretimi Gerçekten Durdur ✅ Uygulandı

**Neden ayrı PR:** Tespit edilen en kritik davranış boşluğu — şu an `DayCompleted` sadece XP commit'i tetikliyor, üretimi durdurmuyor.

**Kapsam (gerçekleşen implementasyon):**
- `TicketSlotManager`'a `IsDayComplete` (bool, `private set`) + `PauseForDayComplete()` eklendi. `AssignTicket` (private, `FillEmptySlots`/`DeliverTicket`/`CancelTicket`/`ResetSlotsForNewDay`'in **hepsinin** ortak darboğazı) ve `Tick`'in en başına `if (IsDayComplete) return;` guard'ı eklendi — tek noktadan tüm üretim yolları kapatılıyor.
- `ResetSlotsForNewDay()` artık en başta `IsDayComplete = false;` set ediyor — yeni bir "Resume" metodu icat edilmedi, mevcut retry akışı (`GameManager.RetryDay()`) otomatik olarak "resume" oluyor.
- `GameManager.OnDayCompleted`'e tek satır eklendi: `TicketSlotManager.PauseForDayComplete();`. Yeni bir UI event'i eklenmedi — `State.DayCompleted` zaten sinyalin kendisi, PR-9'daki popup ona subscribe olacak.
- **Önemli davranış notu (testle keşfedildi):** Guard, `AssignTicket`'i durdurduğunda slot **`null` olmaz** — `DeliverTicket`, slotu asla açıkça `null`'lamıyor, yeni bilet atanmasına güveniyordu. Yani Day tamamlandığında son teslim edilen bilet, `TicketState.Delivered` işaretiyle slotta **görsel olarak kalıyor** (yeni atama olmuyor, ama eski referans duruyor). Bu, roadmap'in "yeni bilet atanmıyor" kriterini karşılıyor; slotun görsel temizliği (varsa) PR-9'un Day-Complete popup'ının kapsamı.
- `LevelManager`'ın `DayCompleted`/`DayRetried` subscribe'larına dokunulmadı.

**Bağımlılık:** PR-2.

**Kabul kriteri:** ✅ 4 yeni test (`TicketSystemTests.cs`): `AssignTicket_AfterPauseForDayComplete_DoesNotRefillSlot`, `Tick_AfterPauseForDayComplete_DoesNotDecrementRemainingSecondsOrLoseLife`, `ResetSlotsForNewDay_ClearsIsDayComplete_SubsequentAssignTicketWorksAgain`, ve gerçek event cascade'ini kuran `DeliverTicket_WhenDayCompletedEventFires_StopsRefillingThatSlot` (tek `DeliverTicket` çağrısının `TicketDelivered → RecordDelivery → DayCompleted → PauseForDayComplete` zincirini tetikleyip slotu doğru şekilde durdurduğunu doğruluyor). Unity EditMode suite (165 test) çalıştırıldı — sadece bilinen bağımsız flaky `TraySystemTests` testi hariç hepsi geçti.

---

## PR-4 — Day Geçişi (Day N → Day N+1, Retry Davranışı) ✅ Uygulandı

**Kapsam (gerçekleşen implementasyon):**
- Yeni `GameManager.AdvanceToNextDay() : bool` — `State.CurrentDayIndex`'i artırır, `isRetryAttempt`'i temizler, board/tray/ticket slot'ları `RetryDay()`'le aynı sırayla sıfırlar (`Board.Clear() → TrayManager.DiscardAllForNewDay() → TicketSlotManager.ResetSlotsForNewDay()`) **ama `LivesManager`'ı hiç çağırmaz** — can/XP Day ilerlerken sıfırlanmıyor, sadece başarısız retry'de sıfırlanıyor. Son Day'de (`nextIndex >= dayCatalog.Count`) `false` döner, ilerlemez (Q2) — henüz hiçbir UI bunu tüketmiyor, `bool` dönüşü PR-9'un "Continue butonu son Day'de devre dışı" kararının üzerine ineceği sinyal.
- `GameManager.RetryDay()`'e `isRetryAttempt = true;` eklendi. Yeni `DayCatalogNavigator.GetEffectiveDay(baseDay, isRetryAttempt)`: retry değilse `baseDay`, retry'deyse `baseDay.RetryVariant ?? baseDay`. `CurrentDay` artık `GetEffectiveDay` üzerinden çözülüyor. `isRetryAttempt`, sadece ilk retry'de değil, **`AdvanceToNextDay()` çağrılana kadar** true kalıyor — yani o Day'i art arda retry etmek her seferinde `RetryVariant`'ı kullanıyor.
- **Scope notu (önceden belirtilmişti, implementasyonda doğrulandı):** `DayDefinition.TicketSequence`/`BoardTimeline` PR-6'ya kadar hiçbir runtime sisteme bağlı değil — `RetryVariant`'ın şu an tek somut etkisi `TicketsRequiredForDay` (PR-2'de zaten bağlıydı). Authored içerik farkı PR-6'dan sonra görünür olacak.
- **Stale lookahead queue düzeltildi:** Yeni `TicketSlotManager.ClearUpcomingQueue()`, `ResetSlotsForNewDay()`'in en başına eklendi — hem `RetryDay()` hem `AdvanceToNextDay()` bunu otomatik miras alıyor.
- **Manuel test için debug tuşları:** `DebugTicketDeliveryController.cs`'ye `N` (→ `AdvanceToNextDay()`) ve `R` (→ `RetryDay()`) eklendi — PR-9'dan önce Play mode'da elle test edilebilmesi için (dosyanın kendi "dev-only, silinebilir" açıklamasıyla tutarlı).

**Bağımlılık:** PR-2, PR-3.

**Kabul kriteri:** ✅ `DayCatalogNavigatorTests`'e 3 yeni test (`GetEffectiveDay_NotRetrying_ReturnsBaseDay`, `GetEffectiveDay_RetryingWithVariant_ReturnsVariant`, `GetEffectiveDay_RetryingWithoutVariant_FallsBackToBaseDay`), `TicketSystemTests`'e 2 yeni test (`ClearUpcomingQueue_EmptiesTheQueue`, `ResetSlotsForNewDay_ClearsUpcomingQueue_OldQueuedTicketsDoNotReappear` — düzeltme olmadan gerçekten fail ettiği izole testte doğrulandı). Unity EditMode suite (170 test) çalıştırıldı — sadece bilinen bağımsız flaky `TraySystemTests` testi hariç hepsi geçti.

---

## PR-5 — `TimeLimitSecondsFor` Helper ✅ Uygulandı

**Kapsam (gerçekleşen implementasyon):**
- `TimeLimitSecondsFor(PatienceType) : float` eklendi — ama roadmap'in yazdığı gibi `TicketGenerationConfig`'e literal bir instance metodu **olarak değil**. **Mimari bulgu:** `TicketGenerationConfig`, `ExpoTheExplorer.Data` assembly'sinde, ve `Data.asmdef`'in `references` dizisi tamamen boş — `PatienceType` ise `ExpoTheExplorer.Core`'da, ve `Core.asmdef` zaten `Data`'ya referans veriyor (`Core → Data`). Helper'ı `TicketGenerationConfig`'e literal eklemek `Data`'nın da `Core`'a referans vermesini gerektirirdi — bu döngüsel assembly referansı (`Core → Data → Core`) yaratır, Unity derlemez.
- **Çözüm:** Yeni `Assets/Scripts/Core/TicketGenerationConfigExtensions.cs` — `Core`'da tanımlı bir **extension method** (`public static float TimeLimitSecondsFor(this TicketGenerationConfig config, PatienceType patienceType)`). `Core` zaten hem `PatienceType`'ı hem `Data.TicketGenerationConfig`'i görebildiği için döngü oluşmuyor. Çağıran kod tarafında (`config.TimeLimitSecondsFor(patienceType)`) **hiçbir fark yok** — extension method sözdizimi instance metoduyla birebir aynı görünüyor, sadece fiziksel olarak nerede tanımlandığı değişti.
- `TicketFactory.Create`'teki private switch tek satıra indi: `var timeLimitSeconds = config.TimeLimitSecondsFor(patienceType);`

**Not:** Bu PR'ın kapsamı daralmıştı — board-override enjeksiyonu artık gerekmiyor, çünkü `BoardDistributor` runtime'da hiç çalışmıyor (Q1 revizyonu).

**Bağımlılık:** Yok.

**Kabul kriteri:** ✅ `TicketGenerationConfigExtensionsTests.cs` — 3 yeni test, her `PatienceType` için doğru değeri doğruluyor. Mevcut `TicketSystemTests.TicketFactory_Create_OnlyUsesItemsFromSuppliedPool_AndProducesPlausibleItemCount` (Normal süresini doğrudan kontrol ediyor) yeşil kaldı — `TicketFactory.Create`'in davranışı değişmedi. Unity EditMode suite (173 test) çalıştırıldı — sadece bilinen bağımsız flaky `TraySystemTests` testi hariç hepsi geçti.

---

## PR-6 — Day İçerik Oynatımı (tamamen deterministik) ✅ Uygulandı

**Kapsam:**
- `TicketEntryFactory.Create(entry, arrivalSequence)` — JSON girdisini (id'leri çözülmüş haliyle) `Ticket`'a çevirir; `timeLimitSecondsOverride <= 0` ise PR-5'in `TimeLimitSecondsFor` helper'ına düşer. Generate edilmiş de elle yazılmış da aynı yoldan geçer.
- `DayTicketSequenceProvider` — `TicketSlotManager`'ın `Func<Ticket> nextTicketProvider` seam'ine bağlanan, `ticketSequence`'i sırayla okuyan sağlayıcı. **Fallback/otomatik üretim dalı yok.** **`TicketSlotManager`'a hiç dokunulmuyor.**
- `DayBoardTimelinePlayer.ApplyForStep(board, boardTimeline, stepIndex)` — `boardTimeline`'daki `triggerStepIndex == stepIndex` olan girdileri `BoardGrid.TryPlaceItem`/`RequestSpawn` ile uygular.
- **`GameManager.OnTicketAssigned` artık `boardDistributor.OnOrderPlaced(...)` çağırmıyor** — bunun yerine `DayBoardTimelinePlayer.ApplyForStep(...)` çağrılıyor, "kaçıncı bilet atandı" sayacıyla. Day başlangıcında (`State.Board.Clear()` sonrası, ilk atamadan önce) `triggerStepIndex == -1` grubu uygulanır.
- `BoardDistributor` sınıfı **koddan silinmiyor** — sadece runtime çağrı zincirinden çıkarılıyor; tek çağıranı PR-6.5'teki `DayContentGenerator` oluyor.

**Bağımlılık:** PR-1, PR-5.

**Kabul kriteri:** Bir Day'de atanan biletlerin sırası/içeriği `ticketSequence`'le tam eşleşiyor; board'daki spawn'lar `boardTimeline`'daki `triggerStepIndex`'lerle tam eşleşiyor; `BoardDistributor.OnOrderPlaced`'in artık `GameManager`'dan çağrılmadığı testle doğrulanmış.

**Implementasyon notları:**
- **Kullanıcı onaylı karar: temiz kesim, fallback yok.** PR-7 (Day Editor) henüz yok, `Assets/Resources/Days/` boş, `CurrentDay` her zaman `null`. `CreateNextTicket()`/`OnTicketAssigned` artık `CurrentDay == null` olduğunda sessizce eski `TicketFactory`/`BoardDistributor` davranışına düşmüyor, açık `InvalidOperationException` fırlatıyor. Bu, PR-1-5'teki "her PR sonrası oynanabilir kalsın" alışkanlığından bilinçli bir sapma — oyun PR-7'ye kadar Play mode'da çalışmaz durumda kalıyor, doğrulama tamamen EditMode testleriyle yapıldı.
- **`ExpoTheExplorer.Systems.TicketSystem` referansı bir PR erken eklendi.** Roadmap bunu PR-6.5/6.6'ya bırakmıştı, ama `TicketEntryFactory`'nin kozmetik isim-seçimi (`TicketFactory.PickRandomCustomerName()`) için `DaySystem` asmdef'inin `TicketSystem`'i görmesi PR-6'da zaten gerekti.
- **Yan fayda — Risk 9 bu PR'la yapısal olarak kapandı** (aşağıdaki Risk/Not Bölümü'nde detaylandırıldı): board spawn'ları artık "aktif biletleri sürekli yeniden garantiye bağla" değil, `triggerStepIndex`'e bağlı tek seferlik olaylar; `ticketSequence.Count == ticketsRequiredForDay` kilidi sayesinde day'in son teslimatına kadar tüm board timeline adımları zaten uygulanmış oluyor.
- ~~Açık risk (kabul edilerek not düşüldü, bu PR'da çözülmedi): TicketSlotManager'ın lookahead kuyruğu hâlâ ileri bilet üretmeye çalışıyor...~~ **Bu not yanlış teşhis içeriyordu — gerçek sorun "Day'ler en az `UpcomingQueueSize` kadar bilet içermeli" değildi, `TicketSlotManager`'ın kendisindeki lookahead buffer mekanizmasıydı (Day uzunluğundan tamamen bağımsız, HER Day'de tetiklenirdi). 2026-08'de `TicketSlotManager`'dan buffer tamamen kaldırılarak düzeltildi — bkz. PR-6.6 sonrasındaki 🔧 Bugfix notu.**
- Test doğrulaması: `TicketEntryFactoryTests` (6), `DayTicketSequenceProviderTests` (2), `DayBoardTimelinePlayerTests` (5) — tümü `DayDefinition`/`ResolvedTicketEntry`/`ResolvedBoardSpawnEntry` fixture'larını doğrudan C#'ta kurarak, gerçek JSON dosyasına ihtiyaç duymadan. Tam EditMode suite (211 test) yeşil, tek istisna bilinen bağımsız flaky `TraySystemTests.TryAddItem_WrongItem_...` testi (Day sistemiyle ilgisiz, board-scatter testi). `BoardDistributionTests` (20 test) değişmeden geçiyor — `BoardDistributor` koddan silinmedi, sadece `GameManager`'ın çağrı zincirinden çıkarıldı.

---

## PR-6.5 — Day İçerik Üretici (`DayContentGenerator`) ✅ Uygulandı

**Kapsam:**
- `DayContentGenerator` (plain C#, Editor'a bağımlı değil, EditMode test edilebilir, seedable `Random`): `editorMeta`'daki override'ları kullanarak (a) `TicketFactory.Create` ile bilet dizisini üretir, (b) izole bir `GameState`/`BoardGrid` üzerinde üretilen bilet dizisini adım adım simüle edip her adımda gerçek `BoardDistributor.OnOrderPlaced`'i çalıştırarak, ortaya çıkan yeni item'ları o adımın `triggerStepIndex`'iyle damgalayıp `boardTimeline`'a yazar.
- **Kritik prensip:** gerçek runtime mantığı (bir zamanlar runtime'da canlı çalışan `BoardDistributor`) tekrar kullanılır, kopyalanmaz — sadece artık offline/tek seferlik simülasyon olarak çalışır.
- **Sadece Editor-time çağrılır** — runtime bu sınıfı hiç bilmez.

**Bağımlılık:** PR-6 (veri tipleri/mapper'lar olmalı).

**Kabul kriteri:** Sabit seed ile üretilen `boardTimeline`, aynı seed'le adım adım çalıştırılan gerçek `TicketFactory`/`BoardDistributor` çağrılarıyla üretilen sonuçla birebir eşleşiyor (testle doğrulanmış).

**Implementasyon notları:**
- **Kullanıcı onaylı karar: teslimat/silme simülasyonu eklendi (roadmap'in yazılı kapsamının ötesinde).** Sadece `OnOrderPlaced`'i çalıştıran en basit model, hiçbir şeyi board'dan silmediği için uzun Day'lerde `BoardGrid`'in pending-spawn kuyruğunda required item'ların sonsuza dek beklemesine (kapasite açlığı) yol açabiliyordu. Bunun yerine generator, 3 aktif slotu round-robin döndürüp "bu slottaki önceki bilet artık teslim edildi" varsayımıyla o biletin required item'larını (`RequiredItemKey` eşleşmesiyle, `BoardDistributor`'ın kendi kuralıyla birebir) board'dan kaldırıyor (`BoardGrid.RemoveItem`), `BoardGrid`'in kendi pending-queue-backfill mekanizmasını tetikleyerek. Bu sadece **üretim zamanı** kapasite gerçekçiliği için var — runtime'da hiç karşılığı yok (`DayBoardTimelinePlayer` hâlâ hiçbir şeyi silmiyor, sadece ekliyor; gerçek silme oyuncunun sürükle-bırakıyla olur) ve replay doğruluğunu etkilemiyor (`triggerStepIndex` her zaman `ticketSequence` sırasıyla ateşlenir, oyuncu hızından bağımsız).
- **`TicketGenerationConfig.CloneWithOverrides`/`BoardDistributionConfig.CloneWithOverrides` eklendi** (Data assembly) — `editorMeta` override'larını `Object.Instantiate` ile klonlanan bir kopyaya uygular, base asset'i hiç mutasyona uğratmaz, `UnityEditor`/`SerializedObject` kullanmaz (bu sınıflar Editor'a bağımlı kalmıyor). Nullable-primitive parametreler sayesinde `DayEditorMetaJson`'ı hiç bilmiyorlar.
- **`useExactCell = true`, her üretilen board spawn'ı için, istisnasız** — donmuş `(x,y)`, generator'ın önizlemesiyle gerçek oynanıştaki yerleşimin birebir örtüşmesi için.
- **Simülasyon-içi müşteri adı sabit bir placeholder** (`ticketFactory.PickRandomCustomerName()` hiç çağrılmıyor), `customerNameOverride`/`timeLimitSecondsOverride` üretilen JSON'da boş bırakılıyor — gerçek oynanış bunları kendi rastgele/config-varsayılan mantığıyla dolduruyor.
- **`DayContentGenerationResult.Warnings`** (best-effort, throw yok) — teslimat-silme sırasında beklenen bir required item board'da bulunamazsa (teorik olarak nadir) burada birikir; sert doğrulama `DayValidator`'ın (PR-6.6) işi.
- `ExpoTheExplorer.Systems.BoardDistribution` referansı `DaySystem` asmdef'ine eklendi (`BoardDistributor` sınıfı orada yaşıyor).
- Test doğrulaması: `TicketGenerationConfigCloneWithOverridesTests` (3), `BoardDistributionConfigCloneWithOverridesTests` (3), `DayContentGeneratorTests` (6 — determinism, override wiring, kapasite-açlığı-önleme ve tam round-trip: generate → `JsonUtility.ToJson` → `DayCatalogParser.ParseAll` → `DayTicketSequenceProvider`/`DayBoardTimelinePlayer` ile playback → board/bilet içeriği karşılaştırması). Tam EditMode suite (198 test) yeşil, tek istisna bilinen bağımsız flaky `TraySystemTests` testi.

---

## PR-6.6 — Day Doğrulayıcı (`DayValidator`) ✅ Uygulandı

**Kapsam:**
- `DayValidator` (plain C#, Unity'siz, unit-testable, **rastgelelik gerektirmez**): (1) bilet-sayısı kontrolü (`ticketSequence.Length == ticketsRequiredForDay`), (2) oynanabilirlik simülasyonu — `boardTimeline`'ı `DayBoardTimelinePlayer` ile aynı deterministik mantıkla adım adım oynatıp (gerçek `BoardDistributor` çağrılmaz) sekansın her noktasında en az bir aktif biletin tamamlanabilir olduğunu doğrular.
- Runtime'la bit-bit aynı sonucu verir — sapma riski yok.

**Bağımlılık:** PR-6 (veri şekli + `DayBoardTimelinePlayer`).

**Kabul kriteri:** Kasıtlı olarak bozuk bir Day (eksik bilet sayısı ya da tamamlanamaz bir adım) testte doğru hata mesajıyla yakalanıyor; geçerli bir Day sorunsuz geçiyor.

**Implementasyon notları:**
- ~~Üçüncü bir kontrol de eklendi... `ticketSequence.Length`, `TicketSlotManager`'ın lookahead kuyruk boyutundan kısa olamaz...~~ **Bu kontrol 2026-08'de kaldırıldı — bkz. aşağıdaki 🔧 Bugfix notu.** Kontrolün kendisi yanlış bir teşhise dayanıyordu: gerçek sorun Day içeriğinin kısa olması değil, `TicketSlotManager`'ın artık amaçsız lookahead buffer'ı yüzünden hiç oynanmayacak fazladan bilet istemesiydi. `DayValidator.Validate`'in imzası da bu kontrolle birlikte `TicketGenerationConfig` parametresini kaybetti: `Validate(DayDefinition day, GameConfig gameConfig)`.
- **Hiçbir yeni asmdef referansı gerekmedi** — `DayValidator.cs` `BoardDistributor`/`TicketFactory`'yi hiç çağırmıyor, sadece `Core`+`Data`'ya bakıyor (zaten `DaySystem`'in referanslarıydı). `UnityEngine`'e bile bağımlı değil — `BoardDistributor.cs`'in kendi "plain C#" konvansiyonuyla aynı.
- **Board hiç küçülmüyor (silme simülasyonu yok), round-robin aktif-slot penceresi `DayContentGenerator`'la (PR-6.5) aynı desen** — `DayBoardTimelinePlayer`'ın runtime'daki gerçek (katkısal-only) davranışıyla birebir örtüşüyor; bu, en sıkı/en az affedici zamanlama senaryosu olduğu için (round-robin'de oynanabilirse her zaman oynanabilir) doğru bir yaklaşım. `DayContentGenerator`'ın kendi üretim-zamanı teslimat/silme simülasyonu validator'a taşınmadı (kasıtlı asimetri, gerekçesi ayrı).
- **`RetryVariant` recursive doğrulanıyor**, hata mesajları `"RetryVariant: ..."` önekiyle ayırt ediliyor; tüm kontroller ilk hatada durmadan bağımsız çalışıp `Errors` listesinde birikiyor.
- **`DayCatalogParser`'a hiç entegre edilmedi** — tamamen PR-7'nin Editor-time "Save" gate'i, çalışma zamanı davranışı/yükleme akışı değişmedi.
- Test doğrulaması (2026-08 bugfix'inden önceki hâl): `DayValidatorTests` (7) — bilet-sayısı, lookahead-açlığı, oynanamaz adım, geçerli Day, `RetryVariant` önekleme, bağımsız çoklu hata testlerinin yanında **çapraz-tutarlılık testi** (`Validate_GeneratedDayIsAlwaysValid`). Tam EditMode suite (205 test) yeşil, tek istisna bilinen bağımsız flaky `TraySystemTests` testi. (Bugfix sonrası güncel test sayısı ve kapsamı aşağıdaki 🔧 Bugfix notunda.)

---

## 🔧 Bugfix (2026-08) — `TicketSlotManager`'ın artık amaçsız lookahead buffer'ı `DayTicketSequenceProvider`'ı açlıktan öldürüyordu

**Belirti:** Day Editor'da oluşturulan ilk gerçek Day (`ticketsRequiredForDay = 10`) Play'de denendiğinde, `GameManager.Awake()` aşamasında (oyun hiç başlamadan) şu hata fırlıyordu: `"Day's authored ticketSequence only has 10 entries but ticket #11 was requested."`

**Yanlış teşhis (PR-6/PR-6.6'da yapıldı, düzeltildi):** Sorunun "Day içeriği `UpcomingQueueSize`'dan kısa" olduğu düşünülmüştü (PR-6'nın "Açık risk" notu, PR-6.6'nın 3. kontrolü) — **bu yanlıştı**. `ticketSequence.Count`'u ne kadar büyütülse büyütülsün, sorun devam ederdi.

**Gerçek kök neden:** `TicketSlotManager.DequeueNextTicket()`, her çağrıldığında `EnsureQueueFilled()`'i **iki kez** çalıştırıyordu — bir kere dequeue'dan önce (ilk çağrıda kuyruğu `lookaheadCount`'a dolduran asıl işlem), bir kere de dequeue'dan sonra (buffer'ı geri doldurmak için). Bu, artık runtime'da hiç çağrılmayan eski `BoardDistributor.OnOrderPlaced`'in "henüz gelmemiş biletlerden gürültü sızdır" ihtiyacı içindi (GDD Section 4) — PR-6'dan beri tamamen **vestigial**. Sonuç: bir Day'in tamamı boyunca `nextTicketProvider()` (= `DayTicketSequenceProvider.NextTicket()`) `ticketsRequiredForDay` değil, **`ticketsRequiredForDay + lookaheadCount`** kez çağrılıyordu — `ticketSequence.Count == ticketsRequiredForDay` (PR-1'den beri kilitli invariant) bunu hiçbir zaman karşılayamazdı, Day'in uzunluğu ne olursa olsun.

**Bu bug'ın PR-6.5/PR-6.6'nın testlerinde hiç yakalanamamasının nedeni:** Hiçbir test gerçek `TicketSlotManager`'ı gerçek `DayTicketSequenceProvider`'a uçtan uca bağlamadı — `DayContentGenerator`/`DayValidator`'ın playback testleri `NextTicket()`'i sadece elle, tam `ticketsRequiredForDay` kez çağırdı, `TicketSlotManager`'ın kendi çift-`EnsureQueueFilled()` deseni hiç devreye girmedi. Generator ve validator birbirleriyle tutarlıydı ama ikisi de gerçek runtime davranışını yanlış modelliyordu.

**Düzeltme:**
- **`TicketSlotManager.cs`**: lookahead buffer tamamen kaldırıldı (`upcomingTickets`, `UpcomingTickets`, `lookaheadCount`, `EnsureQueueFilled()`, `ClearUpcomingQueue()`, constructor'ın 4. parametresi). `AssignTicket` artık `nextTicketProvider()`'ı doğrudan çağırıyor — buffer yok, her slot ataması tam olarak bir bilet istiyor. Sonuç: `ticketsRequiredForDay = N` olan bir Day için `nextTicketProvider()` bir gün boyunca tam olarak **N** kez çağrılıyor.
- **`GameManager.cs`**: `TicketSlotManager` constructor çağrısı 4. argümanı (`ticketGenerationConfig.UpcomingQueueSize`) kaybetti.
- **`DayValidator.cs`**: artık yanlış olan lookahead-açlığı kontrolü kaldırıldı, `Validate`'in imzası `TicketGenerationConfig` parametresini kaybetti (2 kontrole geri döndü: bilet-sayısı + oynanabilirlik — roadmap'in orijinal PR-6.6 kapsamı).
- **`DayContentGenerator.cs`**: **hiç değişmedi** — zaten sadece `ticketsRequiredForDay` adet girdi üretiyordu; kendi iç `upcomingTickets` simülasyonu (offline `BoardDistributor.OnOrderPlaced` simülasyonu için) `TicketSlotManager`'ın (artık kaldırılan) buffer'ından tamamen bağımsız bir kavram, dokunulmadı.
- **`TicketGenerationConfig.UpcomingQueueSize` alanı kaldırılmadı** — `DayContentGenerator`'ın kendi offline simülasyonu için hâlâ anlamlı bir parametre, sadece `TicketSlotManager`/`DayValidator`'daki (artık yanlış olan) kullanımı kaldırıldı.
- **Yeni kritik test — `DayTicketSlotManagerIntegrationTests.cs`**: bu bug'ın gözden kaçmasına yol açan tam da eksik olan kapsam — gerçek `TicketSlotManager`'ı gerçek `DayTicketSequenceProvider`'a bağlayıp tam bir Day'i uçtan uca simüle ediyor, provider'ın tam olarak N kez çağrıldığını doğruluyor.
- `TicketSystemTests.cs`'teki 5 buffer-özel test (artık var olmayan davranışı test ediyorlardı) + `DayValidatorTests.cs`'teki 1 test (artık geçerli olmayan kontrolü test ediyordu) kaldırıldı; kalan testlerin `DayValidator.Validate(...)` çağrıları yeni 2-parametreli imzaya güncellendi.
- Tam EditMode suite (208 test) yeşil, tek istisna bilinen bağımsız flaky `TraySystemTests` testi.

---

## 🔧 Bugfix (2026-08) — Fungible (modifikasyonsuz) itemlar bir Day'in tamamı için yetersiz üretiliyordu

**Belirti:** Day Editor'da "Generate" ile üretilen bir Day (yukarıdaki lookahead-buffer fix'inden sonra artık crash olmadan) oynanabilir hâle geldi, ama ilerleyen bir noktada tıkanıyordu — aktif 3 biletin hiçbiri board'daki mevcut itemlarla tamamlanamıyordu (örn. bütün biletler `extra_patty`+side istiyor ama board'da hiç fries, hiç extra-pattyli burger yok).

**Kök neden:** `DayContentGenerator`'ın ana üretim döngüsü her adımda `BoardDistributor.OnOrderPlaced`'i çağırıyor, bu da sadece **o anda garanti edilen** bilet(ler)in (`GuaranteedTicketCount`, varsayılan 1) ihtiyacını karşılıyor — günün **tamamı** boyunca aynı `RequiredItemKey`'i (aynı item + aynı modifikasyon kombinasyonu — modifikasyonsuz side/drink'ler için bu her zaman aynı key) isteyecek **kaç bilet daha** geleceğinden habersiz. Board'da o key'den zaten bir tane duruyorsa, `presentCount` per-call kontrolünü karşılıyor ve yenisi eklenmiyor — ama gerçek oynanışta o item çoktan başka bir bilet tarafından teslim edilmiş/tüketilmiş oluyor. Sonuç: aynı key'i isteyen ardışık biletler arttıkça arz asla yetişmiyor.

**Bu bug'ın `DayValidator`'da (PR-6.6) hiç yakalanamamasının nedeni:** O zamanki oynanabilirlik kontrolü "board hiç küçülmez" varsayımına dayanıyordu — bu, **en cömert** arz senaryosuydu (gerçek oynanıştaki tüketimi hiç modellemiyordu), en sıkı senaryo değil. Bu yanlış çıkarım yüzünden kontrol, kümülatif talebin arzı aştığı hiçbir durumu tespit edemiyordu.

**Düzeltme:**
- **Yeni `DaySolvabilityChecker.cs`**: board/hücre simülasyonu gerektirmeyen, sayım-tabanlı bir çözülebilirlik kontrolü. Round-robin teslimat sırası kesinlikle sıralı olduğundan (bilet `i` en geç adım `min(i + TicketSlotCount, N-1)`'de teslim edilir), her `RequiredItemKey`'i isteyen j'inci bilet (varış sırasına göre) için, kendi deadline'ına kadar en az `j+1` birim o key'in üretilmiş olması gerekiyor. `FindShortfalls(ticketSequence, boardTimeline)` bunu ihlal eden her (bilet, key) çiftini döndürüyor.
- **`DayContentGenerator.cs`**: ana üretim döngüsü (zorluk ayarlarını etkileyen kısım) hiç değişmedi. Döngü bittikten sonra yeni bir onarım geçişi (`EnsureSolvable`) eklendi: `DaySolvabilityChecker.FindShortfalls` boş dönene kadar, en erken shortfall'ı, ilgili biletin kendi varış adımında (`DayBoardTimelinePlayer`/`BoardGrid` primitiflerini kullanarak bulunan ilk boş hücreye) bir `boardTimeline` girdisi ekleyerek kapatıyor. `ticketSequence.Length == ticketsRequiredForDay` invariantı değişmedi — sadece `boardTimeline` büyüyebiliyor. Bu onarım geçişinin hücre bulma mantığı, gerçek teslimat sırasında item'ların kaldırılmasını simüle ETMİYOR (additive-only replay) — bu yüzden çok küçük bir board'da çok uzun bir Day'in sonlarına doğru bir shortfall'ı onaramayabilir; bu durumda sessizce atlamak yerine `Warnings`'e "board is full at that point" mesajı düşüyor (bilinen, kabul edilmiş bir sınır — otomatik büyütme/yer açma bu PR'ın kapsamında değil).
- **`DayValidator.cs`**: eski "board hiç küçülmez" simülasyonu (`ValidatePlayability`/`IsCompletable`/`BoardHasMatch`) tamamen kaldırıldı, yerine `DaySolvabilityChecker.FindShortfalls` kullanılıyor. `Validate`'in imzası artık sadece `Validate(DayDefinition day)` — `GameConfig` parametresine hiç ihtiyaç kalmadı (board simülasyonu tamamen kalktığı için).
- Yeni testler: `DaySolvabilityCheckerTests.cs` (4 test — yeterli arz, fungible-undersupply, deadline sınırı, modifikasyonlu Main çakışması) + `DayContentGeneratorTests.Generate_ManyTicketsNeedingSameSideItem_NeverUndersuppliesFungibleItems` + `DayValidatorTests.Validate_FungibleItemUndersupply_ReportsShortfallForLaterTickets`.
- Tam EditMode suite (214 test) yeşil, tek istisna bilinen bağımsız flaky `TraySystemTests` testi.

**Not:** Bu fix'ten önce üretilmiş herhangi bir Day JSON'u (örn. kullanıcının kendi elle üretip test ettiği `day_00.json`) bu düzeltmeden faydalanmıyor — Day Editor'da tekrar **"Generate"** ile yeniden üretilmesi gerekiyor.

---

## 🔧 Bugfix (2026-08) — Bilet dizisi, gün "tamamlandı" sinyalinden önce tükeniyor ve oyun crash oluyordu

**Belirti:** Yukarıdaki fungible-item fix'inden sonra yeniden generate edilen bir Day (`ticketsRequiredForDay = 11`) oynanırken şu hata fırlıyordu: `"Day's authored ticketSequence only has 11 entries but ticket #12 was requested."` — 11. bilet gelmeden, oyun daha bitmeden.

**Kök neden:** `TicketSlotManager.AssignTicket`, `nextTicketProvider()`'ı (`DayTicketSequenceProvider.NextTicket()`) her **slot doldurma/yeniden doldurma** olayında çekiyor: gün başında 3 slot için 3 çekiliş (hiçbiri "teslimat" sayılmıyor), sonra her teslimat/timeout için 1 çekiliş daha. Ama `IsDayComplete` bayrağı — bu çekilişleri durduran tek kontrol — `DayLifecycleManager.RecordDelivery()` içinde `TicketsDeliveredToday == ticketsRequiredForDay` (N) olduğunda set ediliyordu, yani **teslimat sayısına** dayalı bir sinyal. N=11, slot sayısı=3 için: 3 başlangıç çekilişi + ilk 8 teslimattan sonraki 8 refill çekilişi = 11 çekiliş (sıra tam bitiyor) — ama henüz sadece 8 teslimat yapılmış, N (11) değil. 9. teslimat anında `AssignTicket` yine çekmeye çalışıyor, sıra boş, crash. **Bu matematiksel olarak `ticketsRequiredForDay > TicketSlotCount` olan HER Day'de oluyordu** (yani pratikte her zaman) — mevcut kodla bir Day'i gerçekten baştan sona bitirmek zaten mümkün değildi. Ayrıca: bir bilet timeout ile iptal olursa hiçbir zaman "teslim edilmiş" sayılmıyordu, yani `TicketsDeliveredToday` hiçbir timeout'lu günde N'e ulaşamazdı — sıra tükendikten sonra kalan biletler teslim edilse bile gün hiçbir zaman "bitti" sinyalini vermeyecek, oyun boş slotlarla askıda kalacaktı (crash'in kendisi çözülse bile).

**Düzeltme — "gün tamamlandı" sinyali teslimat sayısından bağımsızlaştırıldı, sıra tükenmesine bağlandı:**
- **`DayTicketSequenceProvider.cs`**: yeni `HasNext` property (`cursor < ticketSequence.Count`) — çağıranlar `NextTicket()`'i çağırmadan önce sıranın bitip bitmediğini sorabiliyor. `NextTicket()`'in kendisi değişmedi, hâlâ throw ediyor (artık sadece "çağıran `HasNext`'i kontrol etmedi" durumunda tetiklenecek bir savunma invariant'ı).
- **`GameManager.cs`**: `CreateNextTicket()` artık sıra tükendiyse `throw` etmek yerine `null` döndürüyor. `OnTicketAssigned(...)` artık `null` bilet için board timeline oynatımını atlıyor (`TrayManager.OnTicketAssigned` zaten sadece `slotIndex`'e bakıyor, ticket'ı okumuyor — null-safe, değişmedi). `OnDayCompleted`'in `TicketSlotManager.PauseForDayComplete()` çağrısı kalktı — artık `TicketSlotManager` bunu kendi kendine set ediyor.
- **`TicketSlotManager.cs` — asıl düzeltme**: `AssignTicket`, provider'dan `null` gelirse slotu boş bırakıyor (atama zaten otomatik yapıyor bunu) ve **o an tüm 3 slot da boşsa** günü tamamlanmış ilan ediyor: `IsDayComplete = true` + `state.DayCompleted.Publish(state.TicketsDeliveredToday)`. Bu, hangi sebeple (teslim ya da timeout) slotların boşaldığından bağımsız — sadece "sıra bitti + kimse aktif değil" diye bakıyor.
- **`DayLifecycleManager.cs`**: artık anlamsızlaşan `getTicketsRequiredForDay` delegate'i ve onu kullanan tamamlanma kontrolü tamamen kaldırıldı (Bug 1'deki presedente göre — vestigial kodu silmek, etrafında dolaşmamak). Sınıf artık sadece `TicketsDeliveredToday` sayacını tutuyor (`RecordDelivery`/`ResetForNewDay`), tamamlanma sinyali vermiyor.
- Yeni/güncellenen testler: `TicketSystemTests.AssignTicket_ProviderReturnsNull_LeavesSlotEmptyWithoutThrowing`, `DeliverTicket_SequenceExhaustedAndAllSlotsEmpty_SetsIsDayCompleteAndPublishesDayCompleted`, `DeliverTicket_OneTicketTimesOutInsteadOfDelivered_DayStillCompletesOnceSequenceExhausted` (artık geçersiz öncüle dayanan `DeliverTicket_WhenDayCompletedEventFires_StopsRefillingThatSlot` kaldırıldı); `DayTicketSlotManagerIntegrationTests.FullDayLifecycle_AllTicketsDeliveredIncludingFinalSlots_CompletesDayWithoutThrowing` (eski entegrasyon testi sadece `N - TicketSlotCount` teslimat yapıp duruyordu — tam günü hiç bitirmiyordu, bu yanlış güven veriyordu; yeni test gerçek `DayContentGenerator`+`DayLifecycleManager` ile tam N teslimatı uçtan uca sürüyor); `DayLifecycleManagerTests.cs` artık var olmayan goal-tabanlı davranışı test eden 3 testi kaybetti, yerine `RecordDelivery_IncrementsCounter` + `RecordDelivery_NeverPublishesDayCompleted` geldi.
- Tam EditMode suite (215 test) yeşil, tek istisna bilinen bağımsız flaky `TraySystemTests` testi.

**Not:** Bu fix, `day_00.json`'ın kendisine dokunmuyor (JSON içeriği hâlâ geçerli) — sadece runtime davranışını düzeltiyor. Fungible-item fix'i sonrası regenerate edilmiş olan `day_00.json` artık baştan sona oynanabilir olmalı.

---

## PR-7 — Day Editor (`EditorWindow`) — JSON'un biricik düzenleme arayüzü ✅ Uygulandı

**Kapsam:**
- Yeni `Assets/Editor/DayEditorWindow.cs`: `Assets/Resources/Days/*.json` dosyalarını listeler, oluşturur/kopyalar/siler, `dayIndex`'e göre sıralar.
- Seçili Day için `runtime` alanları (`ticketsRequiredForDay`, `ticketSequence` editörü, `boardTimeline` editörü — her girdi için bir `triggerStepIndex` seçici: "Day Start" ya da belirli bir bilet adımı, `retryVariant` alt-editörü) **ve** `editorMeta` alanları (ticket-generation + board-distribution override'ları) aynı pencerede düzenlenir — tasarımcı için tek arayüz, dosyadaki iki-bölüm ayrımı UI'da görünmez.
- "Generate" (config'e göre N bilet + tam board timeline üret) ve "Add Manually" (main/side/drink/mod `ObjectField`'larıyla boş bilet girdisi / board spawn girdisi ekle) butonları PR-6.5'i çağırır.
- **`DayValidator`'ı (PR-6.6) her değişiklikte (debounce'lu) canlı çalıştırır**, sonucu bir durum çubuğunda gösterir (✓/✗ + hata listesi), **doğrulama başarısız olduğunda "Save" butonunu devre dışı bırakır.**
- Kaydet: `File.WriteAllText` (ilgili `Assets/Resources/Days/day_XX.json`) + `AssetDatabase.Refresh()`.

**Bağımlılık:** PR-1 (minimum). PR-5/PR-6/PR-6.5/PR-6.6 tamamlanmışsa editör onları da kullanabilir; erken başlayıp diğer PR'larla paralel/iteratif genişleyebilir.

**Kabul kriteri:** Bir tasarımcı, koda dokunmadan yeni bir Day JSON'u oluşturup sırasını/parametrelerini ayarlayabiliyor, otomatik üretip elle düzenleyebiliyor (bilet + board timeline dahil), geçersiz bir Day'i kaydedemiyor, geçerli olduğunda dosya diskte doğru JSON olarak duruyor.

**Implementasyon notları:**
- **Kullanıcı onaylı karar: Odin Inspector sadece bu PR için kullanıldı** (proje zaten `Assets/Plugins/Sirenix` altında kuruluydu, precompiled DLL olarak otomatik referanslanıyor) — `OdinMenuEditorWindow` (sol ağaç = Day listesi, sağ panel = seçilenin Odin-çizilen içeriği), `[TableList]`/`[FoldoutGroup]`/`[Button]`/`[ToggleGroup]`/`[ShowIf]`/`[EnableIf]`/`[InfoBox]` kullanıldı. Mevcut config Inspector'ları (`TicketGenerationConfigEditor` vb.) **değiştirilmedi** — kapsam dışı, kullanıcı açıkça sınırladı.
- **3 yeni dosya, mimari katmanlı:** `DayFileIO.cs` (saf dosya I/O, `UnityEditor`'a bağımlı değil, klasör yolu parametrik — testler `Path.GetTempPath()` kullanıyor, gerçek `Assets/` hiç dokunulmuyor), `DayEditorModel.cs` (editlenebilir, `FoodItemConfig`/`ModificationConfig`'e doğrudan referans tutan Odin-attributed veri modeli + `FromDayJson`/`ToDayJson`/`ToDayDefinition`/`Clone` dönüşümleri + Generate/Add/Save/Duplicate/Delete aksiyonları), `DayEditorWindow.cs` (`OdinMenuEditorWindow`, menü ağacı, config auto-discovery, dosya-seviyesi callback'ler).
- **`[Button]` aksiyonlarının shared config'leri (catalog/gameConfig/ticketConfig/boardConfig) parametre olarak almaması bilinçli** — Odin, parametreli bir `[Button]` metodunun argümanları için Inspector'da ayrı giriş alanları çiziyor; bunun yerine `DayEditorModel.Configure(...)` ile enjekte edilip private field'larda tutuluyor, `Generate()` parametresiz kalıyor.
- **Id çözülemezse sessizce `null`/boş `ObjectField`** — `DayCatalogParser`'ın "tüm Day'i düşür" davranışı Editor'da tekrarlanmıyor, `DayEditorModel.FromDayJson` kendi hoşgörülü çözümlemesini yapıyor (`DayCatalogParser`'a hiç bağlanmadan).
- **`Clone()`, `ToDayJson()`→`FromDayJson()` round-trip'i üzerinden** — ikinci bir deep-clone algoritması yok.
- **Canlı doğrulama debounce timer'ı olmadan her GUI çiziminde çalışır** — `DayValidator`'ın maliyeti ihmal edilebilir düzeyde (Random yok, `BoardDistributor` yok).
- **Save, `dayIndex` değişikliğini rename olarak ele alıyor** — `DayEditorModel.LastSavedDayIndex` (yüklendiği/son kaydedildiği index) ile karşılaştırılıp eski dosya silinip yeni `day_XX.json`'a yazılıyor, orphan dosya kalmıyor. İki Day'in aynı `dayIndex`'i paylaşmasına Save aşamasında izin verilmiyor (bu, `DayValidator`'ın tek-Day kapsamının dışında bir kontrol, `DayValidator.cs`'e dokunulmadı).
- **`RetryVariant` tek seviyeyle sınırlı** — `IsTopLevel=false` olan bir model kendi `RetryVariant` foldout'unu göstermiyor (retry'nin retry'si yok), ama `Generate`/`Add Ticket`/`Add Board Spawn` (içerik-seviyesi aksiyonlar) her seviyede kullanılabilir.
- Test doğrulaması: `DayFileIOTests` (4 — saf dosya I/O, geçici klasörde), `DayEditorModelConverterTests` (4 — tam alan round-trip dahil `RetryVariant`/override'lar, `DayValidator`'a besleme, `Clone` bağımsızlığı, çözülemeyen id'nin Day'i düşürmemesi). Tam EditMode suite (213 test) yeşil, tek istisna bilinen bağımsız flaky `TraySystemTests` testi. **Pencerenin kendisi (menü ağacı, buton render'ı) otomatik test kapsamına alınmadı** — Unity Editor'da elle açılıp denenmesi gerekiyor (bkz. plan'ın Verification bölümündeki adımlar).

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
2. ~~Stale lookahead queue — hâlâ geçerli risk (PR-4): Day geçişinde önceki Day'in kuyruğu sızabilir, TicketSlotManager.ClearUpcomingQueue() gerekli.~~ **2026-08 bugfix'i sonrası moot** — lookahead kuyruğunun kendisi `TicketSlotManager`'dan tamamen kaldırıldı (`ClearUpcomingQueue()` de dahil), sızabilecek bir kuyruk artık yok.
3. **Oynanamaz Day riski — `DayValidator` (PR-6.6) ile canlı ve tam deterministik yakalanıyor.** `BoardDistributor` artık runtime'da hiç çalışmadığı için (Q1 revizyonu) bu risk tamamen authoring-time bir doğrulama konusu; rastgelelik sapması riski de yok — validator'ın simülasyonu runtime'la birebir aynı.
4. **`JsonUtility` kısıtları** — public field zorunlu (property çalışmaz, sessizce boş döner); iç içe class array'leri (`T[]`) sorunsuz ama `List<T>` yerine array kullanılmalı; enum'lar okunabilirlik için string olarak tutulup resolver'da çevrilecek. **Newtonsoft.Json eklenmeyecek** (kullanıcı onayı).
5. **Id çözümleme hataları sessiz kalmamalı** — typo/eksik id, Day yüklemesinde açık hata logu + Editor'da görsel uyarı (aynı `DayValidator` akışına dahil edilebilir).
6. **`triggerStepIndex` tutarlılığı** — bir `TicketEntryJson` silinir/reorder edilirse, ona bağlı `triggerStepIndex`'li board girdileri de kayabilir/anlamsızlaşabilir. Day Editor (PR-7), bilet silme/reorder işlemlerinde bağlı `boardTimeline` girdilerini otomatik güncellemeli ya da en azından `DayValidator` üzerinden bunu yakalamalı.
7. **`DayContentGenerator`'ın simülasyonu potansiyel olarak ağır bir işlem** (tüm ticket dizisini adım adım simüle ediyor) — büyük Day'lerde (uzun `ticketSequence`) performans PR-6.5 sırasında ölçülmeli. `DayValidator`'ın canlı çalışması da benzer şekilde debounce'lu/odak-kaybında tetiklenmeli (PR-7).
8. **`JsonUtility` nested-class null kısıtı — PR-1'de gerçek testle doğrulandı.** `JsonUtility`, bir nested `[Serializable]` class field'ının `null` değerini asla gerçek `null` olarak round-trip etmiyor — her zaman default-constructed bir instance'a dönüştürüyor. Bu, `DayRuntimeJson.retryVariant`'a saf bir null-check koymayı imkânsız kılıyor (her zaman "var" gibi görünür). Çözüm: `hasRetryVariant` (bool) sentinel eklendi — mevcut `hasBoardDistributionOverride`/`hasTicketGenerationOverride` desenine tutarlı. **Bundan sonra eklenecek her "opsiyonel nested object" alanı için aynı sentinel deseni kullanılmalı**, saf null-check'e güvenilmemeli. **2026-08-14 notu:** retry variant özelliği tamamen kaldırıldığı için (bkz. aşağıdaki kaldırma notu, `decisions.md` D-003) `hasRetryVariant` sentinel'i de gitti — ama buradaki `JsonUtility` **kısıtı hâlâ geçerli ve genel bir kuraldır**: ileride eklenecek her opsiyonel nested object alanı için aynı sentinel deseni gerekli.
9. **PR-3 regresyonu — day tamamlanınca board üretimi kalıcı olarak duruyordu — PR-6 ile yapısal olarak kapandı, ayrı bir fix gerekmedi.** Eski analiz: `GameManager.OnTicketAssigned`, `boardDistributor.OnOrderPlaced(...)`'in **tek çağrı noktasıydı** — `State.TicketAssigned` event'ine bağlıydı, ve `TicketSlotManager.IsDayComplete=true` olduğunda (PR-3'ün `AssignTicket` guard'ı) bu event'in tek publisher'ı olan `AssignTicket` erken `return` ettiği için `TicketAssigned` bir daha hiç ateşlenmiyordu. PR-6, `boardDistributor.OnOrderPlaced`'i (aktif biletleri sürekli yeniden garantiye bağlayan, sürekli-tetiklenmesi-gereken mantık) `GameManager`'dan tamamen çıkardı; yerine gelen `DayBoardTimelinePlayer.ApplyForStep`, `triggerStepIndex`'e bağlı **tek seferlik** olaylardan oluşuyor. `ticketSequence.Count == ticketsRequiredForDay` kilitli kuralı sayesinde, day'in son (N.) teslimatı gerçekleştiğinde o day'in tüm N biletinin ataması (dolayısıyla board timeline adımları) zaten çok önceden tamamlanmış olur — atama her zaman teslimattan önce gelir, day'de asla N'den fazla bilet olmaz. "Teslimatı bekleyen ama board item'ı hiç gelmeyen bilet" senaryosu artık yapısal olarak imkânsız.

## 🗑️ Retry Variant kaldırma notu (2026-08-14)

Bu roadmap'te tasarlanan ve PR-1/PR-2/PR-6.6/PR-7 boyunca uygulanan **retry variant** özelliği tamamen kaldırıldı (kullanıcı talebi; `decisions.md` D-003). Yukarıdaki satırlar **tarihsel kayıt olarak bırakıldı** — artık kodda karşılıkları yok:

- `DayDefinition.RetryVariant` (property + ctor parametresi) — silindi.
- `DayRuntimeJson.hasRetryVariant` / `retryVariant` — silindi; authored Day dosyalarındaki (`day_00.json`, `day_01.json`) 5 seviye derinliğindeki boş `retryVariant` blobları da temizlendi.
- `DayCatalogParser`'ın özyinelemeli variant çözümlemesi — silindi.
- `DayValidator`'ın `"RetryVariant: "` önekli özyinelemeli doğrulaması — silindi; validator'da artık sadece `ticketSequence.Count == ticketsRequiredForDay` kontrolü var.
- `DayCatalogNavigator.GetEffectiveDay(baseDay, isRetryAttempt)` — **metodun tamamı** silindi (var olma sebebi buydu); `GetDayAt` duruyor.
- `GameManager.isRetryAttempt` — silindi; `CurrentDay` yeniden düz bir `GetDayAt` çağrısı.
- Day Editor'ın "Retry Variant" foldout'u, `DayEditorModel.IsTopLevel` ve `FromDayJson`'ın `isTopLevel` parametresi — silindi (yalnızca iç içe variant modelini desteklemek için vardı); Save/Duplicate/Delete butonları artık koşulsuz görünür.

**Yukarıdaki satır 189-190 ve 273 ve 342 ve 359'daki retry-variant açıklamaları geçersizdir.** Retry *akışı* duruyor: `GameManager.RetryDay()` (canı bitince ücretsiz yol) ve `RetryCompletedDay()` (yıldız için gönüllü tekrar) ikisi de Day'in kendisini aynı zorlukta yeniden oynatıyor. CLAUDE.md Section 4'teki **"canı bitince zorluk azaltma"** açık sorusu hâlâ açık — bu kaldırma, o sorunun cevabını değil, cevaplamak için en doğal olan *mekanizmayı* siliyor; ileride cevaplanırsa sıfırdan tasarlanacak.
