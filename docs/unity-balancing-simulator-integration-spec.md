# Unity Balancing Simulator — Integration Specification

**Expo the Explorer · rev 2026-08-30**

*"Mevcut Unity Editor'e, üretim gameplay kodunu yeniden kullanarak deterministik/batch bir balancing simulator eklemek için tam olarak neyi bilmemiz veya değiştirmemiz gerekiyor?"*

İlgili ve **tekrar edilmeyen** dökümanlar: `day-editor-el-kitabi.md` (UI kılavuzu) · `day-runtime-spec.md` (algoritma sözleşmesi) · `external-tool-project-data-spec.md` (config verisi).

---

## §0 · Başlıca sonuç

Bu projeye simulator eklemek **beklenenden çok daha ucuz**, çünkü mimari zaten bunun için kurulmuş:

| Bulgu | Kanıt |
|---|---|
| **`DayEditorModel.ToDayDefinition()` zaten `public`** | `Assets/Editor/DayEditorModel.cs:1226` — kaydedilmemiş editör durumu sıfır refactor ile okunabilir |
| **Tüm asmdef'li assembly'lerde tek MonoBehaviour var** | `Scripts/Session/SessionHost.cs:24`. Core ve 13 Systems assembly'sinde başka yok |
| **`Time.deltaTime` / `Time.time` / coroutine kullanımı sıfır** | `Core/`, `Session/`, `Systems/` altında hiç geçmiyor |
| **`UnityEngine.Random` kullanımı sıfır** | Tüm rastgelelik `System.Random` ve **hepsi enjekte edilebilir** |
| **BoardDistributor sahnesiz koşuyor — kanıtlı** | `Tests/EditMode/BoardDistributionTests.cs` bunu zaten yapıyor |
| **Tam gün yaşam döngüsü sahnesiz koşuyor — kanıtlı** | `Tests/EditMode/DayTicketSlotManagerIntegrationTests.cs` gerçek `TicketSlotManager` + `DayTicketSequenceProvider` ile bir günü baştan sona sürüyor |

**Zorunlu üretim kodu değişikliği: bir tane.** `ExpoTheExplorer.Editor.asmdef`'e beş assembly referansı eklemek. Davranış değişikliği yok, risk yok.

Tek gerçek mimari kısıt şu: **`GameManager` (`Scripts/Bootstrap/`) Assembly-CSharp'ta ve hiçbir asmdef onu referans edemez.** Yani simulator, GameManager'ın yaptığı **kurulum/kablolama** işini kendi yapmak zorunda — ama tek bir **algoritmayı** bile yeniden yazmak zorunda değil.

---

## §1 · Mevcut Day Editor mimarisi

### 1.1 Sınıflar

| Dosya | Satır | Rol |
|---|---|---|
| `Assets/Editor/DayEditorWindow.cs` | 745 | `OdinMenuEditorWindow` — gün listesi, dosya işlemleri, toolbar, kaydetme koruması |
| `Assets/Editor/DayEditorModel.cs` | 1799 | Bir günün bellek içi hâli + inspector çizimi + dönüşümler |
| `Assets/Editor/DayEditorTicketCardPreview.cs` | 284 | Bilet şeridi çizimi (statik) |
| `Assets/Editor/DayEditorDayStartPreview.cs` | 220 | Açılış tahtası ızgarası (statik) |
| `Assets/Editor/DayEditorSettingsPreviews.cs` | 194 | Olasılık önizlemeleri (statik) |
| `Assets/Editor/DayEditorSpriteGUI.cs` | 80 | Sprite çizim yardımcısı (statik) |
| `Assets/Editor/DayFileIO.cs` | — | Disk okuma/yazma/taşıma |

**UI teknolojisi: IMGUI + Odin Inspector.** UI Toolkit hiç kullanılmıyor. `DayEditorWindow : OdinMenuEditorWindow` (Sirenix).

### 1.2 Seçili gün bellekte nasıl duruyor

`DayEditorWindow.BuildMenuTree()` (satır 81) diskteki her günü bir `DayEditorModel`'e çevirir ve Odin menü ağacına ekler:

```csharp
loadedDays = DayFileIO.LoadAll(daysFolderPath)
    .Select(f => DayEditorModel.FromDayJson(f.Json, catalog))
    .OrderBy(d => d.DayIndex)
    .ToList();
```

Seçili gün her zaman şuradan okunur (`DayEditorWindow.cs:340`):

```csharp
var selected = MenuTree.Selection?.SelectedValue as DayEditorModel;
```

`DayEditorModel` **düz bir `[Serializable]` C# sınıfıdır** — `ScriptableObject` değil, `SerializedObject` yok. Odin onu doğrudan çizer. Bu, simulator için iyi haber: nesneyi elde ettiğinde tam da kullanıcının o an gördüğü hâlidir.

### 1.3 Dirty state / Save / Revert / Duplicate / Delete / Generate

| İşlem | Uygulayan | Not |
|---|---|---|
| **Dirty** | `DayEditorModel.RefreshUnsavedState()` (`:1012`) | Bayrak değil **karşılaştırma**: `savedSnapshot != SerializeState()`. `SerializeState()` = `JsonUtility.ToJson(ToDayJson())`. 0.1 sn'de bir yenilenir |
| **Save** | `DayEditorModel.Save()` (`:1149`) → `onSaveRequested` → `DayEditorWindow.OnSaveRequested` | `[EnableIf(nameof(IsValid))]` ile kapılı |
| **Revert** | `DayEditorModel.RevertToSaved()` (`:1027`) | `savedSnapshot`'tan `RestoreFrom` ile yerinde geri yükler |
| **Duplicate** | `DayEditorModel.Clone(catalog)` (`:1261`) | `FromDayJson(ToDayJson(), catalog)` — JSON üzerinden deep copy |
| **Delete** | `onDeleteRequested` → window | Satırdaki × ve alttaki buton aynı yolu kullanır |
| **Generate** | `DayEditorModel.Generate()` (`:1088`) | `UnityEngine.Random.Range` ile seed üretip `DayContentGenerator.Generate`'e verir |
| **Validation** | `DayEditorModel.Validate()` (`:1140`) | `DayValidator.Validate(ToDayDefinition(), AllowedFoodPool)` — **her çizim geçişinde** |

### 1.4 Play From Day

`Assets/Editor/DayJumpWindow.cs` (278 satır) — ayrı bir pencere. Profildeki `CurrentDayIndex`'i yazıp sahne yükler. **Play Mode'a girer**, yani simulator ile ilgisi yoktur; ama §14'teki parity testinin doğal barınağıdır.

### 1.5 KRİTİK SORU: Simulator kaydedilmemiş `DayEditorModel`'i doğrudan okuyabilir mi?

## **EVET — refactor gerekmez.**

**Hangi metot:** `DayEditorModel.ToDayDefinition()` — `Assets/Editor/DayEditorModel.cs:1226`, zaten `public`:

```csharp
public DayDefinition ToDayDefinition()
{
    var ticketSequence = TicketSequence.Select(e => e.ToResolved()).ToList();
    var boardTimeline = BoardTimeline.Select(e => e.ToResolved()).ToList();
    return new DayDefinition(DayIndex, TicketsRequiredForDay, ticketSequence, boardTimeline,
        BoardDistribution.ToResolved(), TicketRuntime.ToResolved(),
        DayCatalogParser.ResolveTutorial(Tutorial));
}
```

**Gereken dönüşüm: hiçbiri.** `DayDefinition` runtime'ın tükettiği tam tiptir — `DayCatalogParser.ParseAll` de aynı tipi üretir. Yani:

```
Kaydedilmemiş DayEditorModel
    ↓  ToDayDefinition()          ← zaten public, diske hiç dokunmaz
DayDefinition
    ↓  DayTicketSequenceProvider + BoardDistributor + TicketSlotManager
Simülasyon
```

Bu yolun doğruluğu **zaten kanıtlı**: `DayValidator` tam da bu metodun çıktısı üzerinde çalışıyor (`DayEditorModel.cs:1145`) ve `DayEditorModelConverterTests.cs` (23.8 KB) dönüşümü test ediyor.

Üç ince nokta:

1. **`ToResolved()` çağrıları `FoodItemConfig` referanslarını taşır**, id'leri değil — yani katalog çözümlemesi zaten yapılmıştır. `RequiredItemKey` referans eşitliği kullandığı için bu doğru davranıştır.
2. **`ToDayDefinition()` parser'ın katılığını uygulamaz.** Editör toleranslıdır (bkz. `day-runtime-spec.md` §1.5): boş bir Main slotu `null` olarak geçer ve `DayValidator` bunu yakalar ama `ToDayDefinition` engellemez. **Simulator çalışmadan önce `IsValid` kontrolü yapmalıdır.**
3. **`sharedGameConfig` `DayEditorModel` içinde `private`** (`:1180` civarı). Simülasyon paneli `DayEditorModel` içinden çizilirse (önerilen desen, §2) ona erişimi vardır; ayrı bir sınıftan çizilirse `AssetDatabase` ile kendi bulmalıdır.

---

## §2 · Day Editor UI genişletme noktası

### 2.1 Mevcut çizim yapısı

`DayEditorWindow` iki Odin kancası kullanır:

| Kanca | Satır | Ne çizer |
|---|---|---|
| `OnBeginDrawEditors()` | 544 | Üst toolbar (+ New Day, 5 config alanı) |
| `OnImGUI()` | 318 | `GuardDayChange()` + `RefreshSaveStateLabels()` + `base.OnImGUI()` |

Seçili günün içeriğini **Odin, `DayEditorModel`'in attribute'larından** çizer. Bölümler `[BoxGroup]` / `[FoldoutGroup]` + `[PropertyOrder]` ile sıralanır:

| PropertyOrder | Bölüm | Çizen |
|---|---|---|
| −100 | Kayıt durumu barı | `DrawSaveStateBar()` |
| −5 | Food Selection | `DrawFoodSelection()` |
| −4 | Day (index, ticket sayısı) | Odin, alanlardan |
| −3 … −2.85 | Generation Settings | `EditorMeta` + önizlemeler |
| −2.4 | Ticket Runtime | Odin |
| −2 | Generate butonu | `[Button]` |
| −1 … −0.5 | Ticket Sequence + Start Board | `DrawTicketCardPreview()`, `DrawDayStartPreview()` |
| −0.2 … −0.15 | Board Distribution + önizlemeler | `DrawBoardDistributionPreviews()` |
| 0 (varsayılan) | Save / Revert / Duplicate / Delete | `[Button]` |

### 2.2 Zaten var olan genişletme deseni

Proje **ağır çizim kodunu ayrı statik sınıflara çıkarma** desenini üç kez uygulamış:

```csharp
[FoldoutGroup("Ticket Sequence"), OnInspectorGUI, PropertyOrder(-1)]
private void DrawTicketCardPreview() => DayEditorTicketCardPreview.DrawStrip(...);

[FoldoutGroup("Start Board"), OnInspectorGUI, PropertyOrder(-1)]
private void DrawDayStartPreview() { ... DayEditorDayStartPreview.DrawGrid(...); }

[BoxGroup("Board Distribution"), OnInspectorGUI, PropertyOrder(-0.15f)]
private void DrawBoardDistributionPreviews() { ... DayEditorSettingsPreviews.DrawLeakPreview(...); }
```

### 2.3 Öneri: aynı deseni izle, sekme sistemi kurma

**Sekme (AUTHORING | SIMULATION | ANALYTICS) önerilmez.** Gerekçe:

- Odin'in `[TabGroup]`'u mevcut `[BoxGroup]`/`[FoldoutGroup]`/`[PropertyOrder]` düzenini yeniden yazmayı gerektirir — bu, 1799 satırlık dosyada geniş ve riskli bir değişikliktir.
- Mevcut düzen zaten **katlanabilir bölümler**dir. Bir `[FoldoutGroup("Simulation")]` tam olarak aynı işi görür ve hiçbir şeyi bozmaz.
- Kullanıcı akışı buna uygun: simülasyon, içerik yazıldıktan **sonra** yapılan bir pastır — tıpkı Board Distribution gibi. Aynı yere, en alta ait.

**Önerilen ekleme — `DayEditorModel.cs`'e 5 satır:**

```csharp
// Board Distribution'ın (-0.2) hemen altına, butonların (0) hemen üstüne.
[FoldoutGroup("Simulation"), OnInspectorGUI, PropertyOrder(-0.1f)]
private void DrawSimulation() =>
    DayEditorSimulationPanel.Draw(this, sharedGameConfig, sharedTicketConfig, ref simulationUiState);

// Diğer UI durumları gibi: serialize edilmez, JSON'a girmez.
private DayEditorSimulationPanel.UiState simulationUiState;
```

`-0.1f` seçimi kasıtlı: `DrawBoardDistributionPreviews` (`-0.15f`) ile Save butonu (`0`) arasındaki boşluk.

### 2.4 Ön refactor gerekli mi?

**Hayır.** `DayEditorModel.cs` 1799 satırla büyük ama **zaten doğru şekilde bölünmüş**: ağır çizim kodu dört ayrı dosyada. Simülasyon paneli aynı deseni izlerse dosyaya 5 satır ekler.

`DayEditorWindow.cs` (745 satır) hiç dokunulmaz.

> **NOT:** İleride ANALYTICS gerçekten ayrı bir sekme isterse, o zaman **ayrı bir EditorWindow** daha uygun olur (`DayJumpWindow` gibi) — gün başına değil, gün *kümesine* bakan bir araç zaten farklı bir pencereye aittir.

---

## §3 · Runtime kod yeniden kullanım denetimi

Sınıflandırma: **A)** saf/doğrudan · **B)** küçük adaptör · **C)** MonoBehaviour/sahne bağımlı · **D)** simülasyona uygun değil.

| Sınıf | Assembly | Sınıf. | Kanıt / gerekçe |
|---|---|---|---|
| `TicketFactory` | Systems.TicketSystem | **A** | ctor `(TicketGenerationConfig, System.Random = null)`. Tek Unity bağı: `UnityEngine.Sprite` döndüren `PickRandomCustomerPortrait` (simülasyonda çağrılmaz) |
| `DayContentGenerator` | Systems.DaySystem | **A** | `static`, `Generate(..., int seed)`. `UnityEngine.Object.DestroyImmediate` çağırır (config klonu) — Editor'de sorunsuz |
| `RequiredItemKey` | Core | **A** | `readonly struct`, saf |
| `BoardDistributor` | Systems.BoardDistribution | **A** | ctor `(GameState, BoardDistributionSettings, Random = null)`. **`BoardDistributionTests` zaten sahnesiz kuruyor** |
| `BoardGrid` | Core | **A** | ctor `(GameConfig)`. `GameConfig` bir `ScriptableObject` — Editor'de `AssetDatabase.LoadAssetAtPath` ile gerçeği yüklenir |
| `Ticket` | Core | **A** | Düz sınıf. Opsiyonel `Sprite customerPortrait` parametresi var, `null` geçilir |
| `TicketSlotManager` | Systems.TicketSystem | **A** | ctor `(GameState, Func<Ticket>, Action<int>)`. `Tick(float deltaSeconds)` — **delta parametre olarak alınır**, `Time.deltaTime` yok |
| `DayTicketSequenceProvider` | Systems.DaySystem | **A** | ctor `(IReadOnlyList<ResolvedTicketEntry>, TicketRuntimeSettings, TicketFactory)` |
| `TraySlot` | Systems.TraySystem | **A** | Düz sınıf, `List<BoardItem>` + `Matches()` |
| `TrayManager` | Systems.TraySystem | **A** | ctor `(GameState, Action<int>, Action<int>, System.Random = null)`. `using UnityEngine` var ama yalnız `Debug.Log` için |
| `EconomyCalculator` | Systems.EconomySystem | **A** | ctor `(EconomyConfig)`, `CalculatePayout(Ticket)` saf |
| `TruncatedPoisson` | Core | **A** | `static`, saf matematik |
| `GameState` | Core | **A*** | ctor `(GameConfig)`. **Yıldız:** `SoftMoney`/`Gems` setter'ları `internal` — bkz. §15.3 |
| `TicketRuntimeSettings` | Data | **A** | Değişmez değer nesnesi. `Mathf.Max` kullanır (saf) |
| `BoardDistributionSettings` | Data | **A** | Değişmez değer nesnesi. `Mathf.Clamp` kullanır (saf) |
| `TicketRequirements` | Core | **A** | `static`, saf |
| `PowerupEffects` | Systems.PowerupSystem | **A** | `static`. `PlanAutoCollect` saf — bkz. §9 |
| `LivesManager` | Systems.LivesSystem | **A** | ctor `(GameState, LivesConfig, Wallet)`. `Wallet` gerekiyor |
| `DayLifecycleManager` | Systems.DayLifecycle | **A** | ctor `(GameState, StarScoreConfig)`. `Mathf.Clamp01` kullanır (saf) |
| `DayBoardTimelinePlayer` | Systems.DaySystem | **A** | `static`, saf, kasten randomsuz |
| `DayValidator` | Systems.DaySystem | **A** | `static`, saf |
| `TutorialDirector` | Systems.Tutorial | **A** | asmdef'i `references: []` — tamamen bağımsız. Tutorial günleri simülasyondan çıkarılıyor (§5.7) |
| `SessionHost` | Session | **C** | **Tek MonoBehaviour.** Gerekmiyor |
| `GameManager` | **Assembly-CSharp** | **D** | MonoBehaviour **ve** hiçbir asmdef referans edemez. Bkz. §3.2 |
| `AutoCollectRunner` | **Assembly-CSharp** | **D** | MonoBehaviour + `BoardView`/`WorldTrayView` sahne referansları. Karar yarısı (`PlanAutoCollect`) zaten ayrık |
| `WorldTrayView`, `BoardItemDragHandler`, `BoardView` | **Assembly-CSharp** | **D** | UI, `Transform`/tween/pooling |

### 3.1 Bağımlılık denetimi — sonuç

`Core/`, `Session/`, `Systems/` altında yapılan tarama:

| Aranan | Bulunan |
|---|---|
| `MonoBehaviour` | **1** — `Session/SessionHost.cs:24` |
| `Time.deltaTime` / `Time.time` | **0** |
| `Coroutine` / `WaitForSeconds` | **0** |
| `UnityEngine.Random` | **0** |
| `static ... = new` (değişebilir global durum) | **0** |
| `GameObject` / `Transform` | **0** |
| Serialized inspector referansları | **0** (asmdef'li assembly'lerde) |
| Audio / VFX | **0** |

Kalan Unity bağları yalnızca: `ScriptableObject` config'leri (Editor'de yüklenebilir), `Sprite` alanları (okunmaz), `Debug.Log` (zararsız), `Mathf` (saf).

### 3.2 Assembly-CSharp duvarı

`Scripts/Bootstrap/`, `Scripts/UI/`, `Scripts/Debug/` klasörlerinde **asmdef yoktur** → hepsi Unity'nin predefined `Assembly-CSharp` assembly'sindedir.

Unity'de **hiçbir asmdef `Assembly-CSharp`'ı referans edemez.** Bu, projenin kendi kodunda açıkça kayıtlı (`UI/AutoCollectRunner.cs` sınıf yorumu: *"Assembly-CSharp is a predefined assembly and no asmdef — including the test assembly — can reference it"*).

**Sonuç:** simulator `GameManager`'ı ne çağırabilir ne de örnekleyebilir. `GameManager`'ın üç işi vardır ve simulator bunları kendi yapmalıdır:

1. **Kompozisyon** — hangi nesnenin hangi nesneye bağlandığı (`Awake`)
2. **Olay kablolaması** — `TicketAssigned` → `BoardDistributor.OnOrderPlaced` + `TrayManager.OnTicketAssigned` (`OnTicketAssigned`, `:630`)
3. **Gün başlangıcı** — `ApplyDayStartBoardPreSeed` + `openingAssignmentsWithoutDistribution` sayacı (`:1193`)

Bunların hepsi **kablolamadır, algoritma değil** — toplam ~60 satır. Hiçbir algoritma kopyalanmaz.

---

## §4 · BoardDistributor simülasyon uygunluğu

### 4.1 Sahnesiz örneklenebilir mi?

## **EVET — değiştirilmeden.**

Kanıt tek başına yeterli: `Assets/Tests/EditMode/BoardDistributionTests.cs` (46.6 KB) bunu **zaten yapıyor**, EditMode'da, Play Mode'a hiç girmeden:

```csharp
gameConfig = ScriptableObject.CreateInstance<GameConfig>();
var settings = new BoardDistributionSettings(noiseLeakCountLambda, ...);
// → new BoardDistributor(state, settings, random)
```

Test dosyası `UnityEditor` ve `UnityEngine` kullanır ama yalnız `ScriptableObject.CreateInstance` ve `SerializedObject` ile sahte config kurmak için. Simulator gerçek asset'leri `AssetDatabase.LoadAssetAtPath` ile yükleyeceği için bu bile gerekmez.

### 4.2 Sahip olduğu durum

`Assets/Scripts/Systems/BoardDistribution/BoardDistributor.cs:22-26`:

```csharp
private readonly GameState state;                      // dışarıdan
private readonly BoardDistributionSettings settings;   // dışarıdan
private readonly Random random;                        // dışarıdan (null ise new Random())
private readonly HashSet<Ticket> leakedTickets = new();      // KENDİ DURUMU
private readonly HashSet<Ticket> guaranteedTickets = new();  // KENDİ DURUMU
```

İki `HashSet` **gün boyunca yaşar** (sticky seçim — `day-runtime-spec.md` §3.4). Bu kasıtlıdır ve simülasyonda korunmalıdır. `GameManager` bunu her gün/retry değişiminde distributor'ı yeniden inşa ederek çözer (`RefreshDayTicketSequenceProvider`, `:1180`) — simulator de her koşu için yeni bir `BoardDistributor` kurmalıdır.

### 4.3 Dış bağımlılıklar

Üç tane, hepsi ctor'dan: `GameState`, `BoardDistributionSettings`, `System.Random`. **Sahne nesnesi, MonoBehaviour, serialized referans, statik durum yok.**

### 4.4 Neyi mutasyona uğratıyor?

| Hedef | Mutasyon var mı | Nasıl |
|---|---|---|
| `BoardGrid` | **EVET** | `state.Board.RequestSpawn(item, random)` — hücreye yerleştirir veya `pendingSpawns`'a kuyruklar |
| `pendingSpawns` | **EVET (dolaylı)** | `RequestSpawn` boş hücre bulamazsa |
| `guaranteedTickets` | **EVET** | Kendi alanı — budar ve ekler |
| `leakedTickets` | **EVET** | Kendi alanı — budar ve ekler |
| `Ticket` durumu | **HAYIR** | Yalnız `RemainingSeconds` ve `State`'i *okur* |
| Upcoming queue | **HAYIR** | `IReadOnlyList<Ticket>` olarak alır, dokunmaz |
| `GameState`'in geri kalanı | **HAYIR** | Yalnız `state.Board` |

### 4.5 Extraction gerekli mi?

## **HAYIR. Sıfır extraction.**

İstenen mimari zaten mevcut:

```
        BoardDistributor  (Systems.BoardDistribution, saf C#)
                  ↑
        ┌─────────┴─────────┐
   GameManager          Simulator
  (Assembly-CSharp)     (yeni asmdef)
```

`BoardDistributor` zaten **hiçbir şeye bağlı değil** — `GameManager`'a da, sahneye de. `GameManager` onu yalnızca *çağırır*. Simulator de aynısını yapar. **Kod duplikasyonu riski sıfırdır** çünkü ikisi de aynı derlenmiş sınıfı kullanır.

Bu, D-004 kararının (`BoardDistributionConfig` ScriptableObject'inin silinip düz `BoardDistributionSettings`'e geçilmesi) beklenmedik bir kazancıdır: o değişiklik `BoardDistributor`'dan son Unity bağını da kopardı.

---

## §5 · Ticket lifecycle simülasyonu

Her geçiş için üretim metodu ve öneri. **A = üretim metodunu çağır**, **B = hafif simülasyonda yeniden üret**.

| # | Geçiş | Üretim metodu | Öneri |
|---|---|---|---|
| 1 | Başlangıç biletleri | `TicketSlotManager.FillEmptySlots()` | **A** |
| 2 | Aktif slot sayısı | `GameState.TicketSlotCount` (`const 3`) | **A** — sabiti oku |
| 3 | Upcoming queue | `DayTicketSequenceProvider.PeekUpcoming(count)` | **A** |
| 4 | Bilet atama | `TicketSlotManager.AssignTicket` (private) → `FillEmptySlots`/`DeliverTicket`/`CancelTicket` üzerinden | **A** — public sarmalayıcılar yeterli |
| 5 | Timer tick | `TicketSlotManager.Tick(float deltaSeconds)` | **A** — delta parametre, bkz. §6 |
| 6 | Timeout | `Tick` içinde, `RemainingSeconds <= 0` → `loseLife(i)` → `CancelTicket(i)` | **A** |
| 7 | Teslimat | `TrayManager.TryAddItem(slot, item, onAccepted)` → `slot.Matches` → `deliverTicket(slot)` | **A** |
| 8 | İptal | `TicketSlotManager.CancelTicket(slotIndex)` | **A** |
| 9 | Yeni bilet | `AssignTicket` cascade'i (7 ve 8'in içinde) | **A** — otomatik |
| 10 | Gün sonu | `AssignTicket`: `ticket == null && AllSlotsEmpty()` → `IsDayComplete` + `DayCompleted.Publish` | **A** |
| 11 | Canlar | `LivesManager.LoseLife()` | **A** veya **B** — bkz. 5.5 |
| 12 | Continue | `LivesManager.TryContinueWithGems()` | **B** — v1'de modellenmiyor |
| 13 | Gün istatistikleri | `DayLifecycleManager.RecordDelivery/RecordFailure` | **A** |

**13 geçişin 11'i doğrudan üretim metodudur.** Bu, "üretim kodunu yeniden kullan" hedefinin neredeyse tamamen karşılandığı anlamına gelir.

### 5.1 Simulator'ın kendi yazması gereken tek şey: kablolama

`GameManager.OnTicketAssigned` (`Bootstrap/GameManager.cs:630`) erişilemez olduğu için eşdeğeri simulator'da kurulmalı:

```csharp
// SimulationHost içinde — GameManager.OnTicketAssigned'ın birebir eşdeğeri
state.TicketAssigned.Subscribe(assignment =>
{
    if (openingAssignmentsWithoutDistribution > 0)
    {
        openingAssignmentsWithoutDistribution--;      // açılış bastırma
    }
    else
    {
        var active = state.TicketSlots.Where(t => t != null).ToList();
        var lookahead = Math.Max(GameState.TicketSlotCount, day.TicketRuntime.UpcomingQueueSize);
        boardDistributor.OnOrderPlaced(active, provider.PeekUpcoming(lookahead));
    }
    trayManager.OnTicketAssigned(assignment.SlotIndex);
});
```

**Bu kablolamanın üretimle aynı kalması `day-runtime-spec.md` §3.1'de tam olarak belgelenmiştir** ve §14'teki parity testinin asıl hedefi budur.

### 5.2 Gün başlangıcı sırası

`GameManager.ApplyDayStartBoardPreSeed()` (`:1193`) eşdeğeri, **`FillEmptySlots()`'tan önce**:

```csharp
DayBoardTimelinePlayer.ApplyForStep(state.Board, day.BoardTimeline, -1);
openingAssignmentsWithoutDistribution =
    DayBoardTimelinePlayer.HasEntriesForStep(day.BoardTimeline, -1)
        ? GameState.TicketSlotCount : 0;
```

### 5.3 Teslimat yolu — kritik incelik

`TrayManager.TryAddItem`'in üçüncü parametresi `onAccepted`'dır ve **batch check'ten önce** çağrılır. Üretimde bu `BoardItemDragHandler.DetachFromBoard`'dır (item'ı tahtadan siler).

Simulator'da bu bir lambda olmalıdır:

```csharp
trayManager.TryAddItem(slot, item, () => state.Board.RemoveItem(x, y));
```

**Bu sıra atlanamaz.** Atlanırsa, teslimatın senkron cascade'i `OnOrderPlaced`'e indiğinde item hâlâ eski hücresindedir ve paylaşılan bir kombinasyon "zaten tahtada var" diye okunur — `TrayManager.TryAddItem`'in kendi yorumunda kayıtlı gerçek bir bug.

### 5.4 Bilet örneği kimliği

`DayTicketSequenceProvider.PeekUpcoming` örnekleri **cache'ler** ve `NextTicket()` aynı örneği döndürür. `BoardDistributor.leakedTickets` referans kimliğine dayandığı için bu zorunludur. Simulator tek bir provider örneği kullandığı sürece bedava gelir.

### 5.5 Canlar — tek gerçek karar noktası

`LivesManager` ctor'u bir `Wallet` ister (`Systems/LivesSystem/LivesManager.cs:30`), `Wallet` de `PlayerProfileStore`'a bağlıdır. v1 için iki seçenek:

| Seçenek | Artı | Eksi |
|---|---|---|
| **A: gerçek `LivesManager`** | Continue mantığı bedava | `Wallet` + profil kurulumu gerekir; gerçek profil dosyasına yazma riski |
| **B: sayaç** `Action<int> loseLife = _ => livesLost++;` | Sıfır bağımlılık, sıfır risk | Continue modellenmez |

**v1 için B önerilir.** `TicketSlotManager` ve `TrayManager` zaten `Action<int> loseLife` delegesi alır — yani bu bir hack değil, mimarinin sunduğu resmi genişletme noktasıdır. Gün "3 can bitti" durumunda sonlanır; Continue v2'ye kalır.

### 5.6 `IsAwaitingContinue` / `IsPaused` / tutorial durakları

`GameManager.Update` üç durak kontrol eder (`:522-552`). v1'de üçü de `false` kalır → `Tick` her zaman çalışır. `deferredTimeouts` mekanizması (son can) yalnız `IsAwaitingContinue` true iken devreye girer, yani v1'de hiç tetiklenmez.

### 5.7 Tutorial günleri

`day_00` tek tutorial günüdür ve `TutorialDirector.IsArmed` iken `Tick` hiç çalışmaz. **`external-tool-project-data-spec.md` §7.3'teki karar geçerli: day_00 simülasyondan çıkarılmalı.**

---

## §6 · Zaman modeli

### 6.1 Denetim sonucu: sanal saat **gerekmiyor**

| Aranan | asmdef'li assembly'lerde |
|---|---|
| `Time.deltaTime` | **0 kullanım** |
| `Time.time` | **0 kullanım** |
| `WaitForSeconds` / coroutine | **0 kullanım** |
| `Update()` | **0** (SessionHost hariç, o da timer tutmaz) |

Tek zaman tüketicisi:

```csharp
// Systems/TicketSystem/TicketSlotManager.cs
public void Tick(float deltaSeconds)
{
    ...
    ticket.RemainingSeconds = Math.Max(0f, ticket.RemainingSeconds - deltaSeconds);
```

**`deltaSeconds` bir parametredir.** `Time.deltaTime`'ı ona veren tek yer `GameManager.Update:573` — ve o Assembly-CSharp'ta, yani simulator zaten kullanamaz.

### 6.2 Öneri: `ISimulationClock` YAZMAYIN

Bir saat arayüzü **gereksiz soyutlamadır**. Simulator sadece şunu yapar:

```csharp
const float Dt = 0.1f;
float virtualTime = 0f;
while (!slotManager.IsDayComplete && virtualTime < maxSeconds)
{
    policy.Act(simState, Dt);        // oyuncu hamleleri
    slotManager.Tick(Dt);            // üretim metodu, değiştirilmemiş
    virtualTime += Dt;
    metrics.Sample(state, virtualTime);
}
```

`urgentTimeThresholdSeconds` karşılaştırması (`BoardDistributor.SelectGuaranteedTickets`) `ticket.RemainingSeconds` okur — o da yukarıdaki `Tick` tarafından güncellenir. **Zaman modeli tek bir yerden akar ve o yer zaten parametrikti.**

### 6.3 Discrete-event alternatifi

Teorik olarak mümkün: olaylar `{oyuncu hamlesi, bilet timeout'u, bilet ataması}`. Ama:

- `Tick` zaten O(3) — kazanç yok
- Timeout anını önceden hesaplamak `RemainingSeconds`'ı okumayı gerektirir ki bu da tick'e eşdeğerdir
- Powerup'lar (Time Reset) zamanı geriye alabilir → olay kuyruğu geçersizleşir

**Sabit adım öner.** `Dt = 0.1f` üretim davranışına yakınlık ile hız arasında iyi bir denge; `Dt = 0.25f` de kabul edilebilir (bkz. §11 performans).

> **NEEDS DECISION:** `Dt` seçimi timeout anını ±Dt kaydırır. Bahşiş kademesi eşiğine çok yakın teslimatlar farklı kademeye düşebilir. Ölçüm hassasiyeti isteniyorsa `Dt = 0.05f`.

---

## §7 · RNG / determinizm

### 7.1 Denetim: **her sistem zaten `System.Random` kabul ediyor**

| Sistem | İmza | Enjeksiyon |
|---|---|---|
| `TicketFactory` | `TicketFactory(config, System.Random random = null)` | ✓ |
| `DayContentGenerator` | `Generate(..., int seed)` → `new Random(seed)` | ✓ |
| `BoardDistributor` | `BoardDistributor(state, settings, Random random = null)` | ✓ |
| `TrayManager` | `TrayManager(state, deliver, loseLife, System.Random random = null)` | ✓ |
| `BoardGrid.RequestSpawn` | `RequestSpawn(BoardItem item, Random random = null)` | ✓ |
| `TruncatedPoisson.Sample` | `Sample(int n, float lambda, Random random)` | ✓ **zorunlu parametre** |

**`UnityEngine.Random` kullanımı asmdef'li assembly'lerde sıfırdır.** Tek istisna editör tarafında:

```csharp
// Assets/Editor/DayEditorModel.cs:1108 — Generate butonu
var seed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
```

Bu, simülasyon yolunda çağrılmaz (simulator zaten yazılmış `ticketSequence`'i oynar).

### 7.2 RNG enjeksiyonu gereken yer: **hiçbiri**

`RunSimulation(day, seed)` bugünkü kodla yazılabilir. Örnek:

```csharp
var master = new System.Random(seed);
var distributorRandom = new System.Random(master.Next());
var trayRandom        = new System.Random(master.Next());
var factoryRandom     = new System.Random(master.Next());
```

Alt akışları master'dan türetmek, bir sistemin çekim sayısı değiştiğinde diğerlerini kaydırmaz.

### 7.3 Önemli: üretim deterministik DEĞİL

`GameManager.RefreshDayTicketSequenceProvider` (`:1180`):

```csharp
boardDistributor = new BoardDistributor(State, CurrentDay.BoardDistribution);  // random = null
```

`random = null` → `new Random()` → zamana bağlı seed. **Gerçek oyun her koşuda farklı davranır.** Simulator deterministik olacak, üretim olmayacak. Bu bir sorun değil, tam da simulator'ın var olma sebebidir — ama §14'teki parity testini etkiler.

### 7.4 Batch tekrarlanabilirliği

`seed 1000, 1001, 1002...` doğrudan çalışır. Tek şart: her koşu **taze nesnelerle** başlamalı (`BoardDistributor`, `DayTicketSequenceProvider`, `GameState`, `TrayManager` yeniden kurulmalı) — çünkü `guaranteedTickets`/`leakedTickets`/`peekCache` durum taşır. `GameManager` de gün değişiminde tam olarak bunu yapar.

---

## §8 · Player policy

### 8.1 Yeniden kontrol: hâlâ tam bir oyuncu politikası yok

`external-tool-project-data-spec.md` §5'teki tespit geçerli. `Scripts/` ve `Tests/` yeniden tarandı: bot, autoplay, solver, hint, ticket-seçim sezgiseli **yok**. Oyuncunun her hamlesi `BoardItemDragHandler` (Assembly-CSharp) üzerinden gerçek dokunuşla başlar.

**Ama `PowerupEffects.PlanAutoCollect` kullanılabilir** — detay §9.

### 8.2 v1 politika arayüzü

Üretim mimarisi bir "action" soyutlaması **sunmuyor** — `TryAddItem` doğrudan çağrılır. Bu yüzden `ChooseNextAction(SimulationState)` yerine, mevcut şekle daha yakın bir arayüz öneriliyor:

```csharp
// Assets/Scripts/Simulation/IPlayerPolicy.cs
public interface IPlayerPolicy
{
    // Bir simülasyon adımında yapılacak hamleleri planlar.
    // Boş liste = "bekle". Zamanı politika değil runner harcar.
    IReadOnlyList<PlannedMove> Plan(SimulationView view);
}

public readonly struct PlannedMove
{
    public int SlotIndex { get; }   // hedef tepsi
    public int X { get; }           // kaynak hücre
    public int Y { get; }
    public BoardItem Item { get; }  // planlandığı anda o hücrede duran örnek
}
```

`PlannedMove`, `PowerupSystem.AutoCollectMove` ile **birebir aynı şekildedir** — kasten, çünkü v1 politikası doğrudan onu üretecek.

> `AutoCollectMove` `Systems.PowerupSystem`'de. Simülasyon assembly'si onu referans edebilir; ayrı bir tip tanımlamak yerine **doğrudan `AutoCollectMove` kullanmak** daha az kod ve sıfır dönüşümdür. Öneri: `PlannedMove` yazma, `AutoCollectMove` kullan.

### 8.3 v1 politikası: `AutoCollectPolicy`

```
Her simülasyon adımında:
  1. Elde bekleyen plan yoksa:
       PowerupEffects.UnwantedTrayItems ile çöp tepsileri boşalt
       plan = PowerupEffects.PlanAutoCollect(state, trayContents)
  2. Plandan bir hamle al (hamle başına actionDelaySeconds sanal zaman harca)
  3. Hamleyi doğrula (hücrede hâlâ aynı örnek mi? slot aynı bilet mi?)
  4. trayManager.TryAddItem(slot, item, () => board.RemoveItem(x, y))
  5. Plan bittiyse ve tahtada iş yoksa → bekle
```

Hedef, gerçekçi insan modellemesi değil: **"tahta üretimi ve zamanlayıcılardan kaynaklanan yapısal zorluğu ölçebilecek, yetkin ve deterministik bir oyuncu."** `PlanAutoCollect` tam olarak budur.

### 8.4 Sonradan eklenecek beceri parametreleri

| Parametre | Nasıl uygulanır | Etkisi |
|---|---|---|
| `actionDelaySeconds` | Hamle başına sanal zaman | **v1'de zaten gerekli** — 0 olursa oyuncu sonsuz hızlı olur |
| `mistakeChance` | Hamleyi rastgele yanlış slota yönlendir | Can kaybı ve yıldız puanı gerçekçiliği |
| `ticketPriority` | `PlanAutoCollect`'in slot sırası yerine aciliyet sırası | Politika karşılaştırması |
| `recognitionDelaySeconds` | Yeni bilet/item geldikten sonra ek gecikme | Bilişsel gecikme |

**v1'de yalnız `actionDelaySeconds` uygulanmalı.** Diğer üçü v3.

> **NEEDS DECISION:** `actionDelaySeconds` için başlangıç değeri projede yok. Gerçek oynanıştan ölçülmeli. Ölçülene kadar `0.5f` gibi bir yer tutucu ve sonuçların ona duyarlılığı raporlanmalı.

---

## §9 · Auto Collect yeniden kullanımı

### 9.1 İnceleme

| Konu | Cevap |
|---|---|
| **Sınıf** | `ExpoTheExplorer.Systems.PowerupSystem.PowerupEffects` (`Assets/Scripts/Systems/PowerupSystem/PowerupEffects.cs`) |
| **Planlama metodu** | `public static List<AutoCollectMove> PlanAutoCollect(GameState state, IReadOnlyList<IReadOnlyList<BoardItem>> trayContents)` |
| **Yardımcılar** | `UnwantedTrayItems`, `OutstandingFor` (private), `MaxMovesFor` (private), `BuildBudget` (private) |
| **Girdi** | `GameState` (tahta + slotlar) + slot başına tepsi içeriği (düz `BoardItem` listeleri) |
| **Çıktı** | `List<AutoCollectMove>` — her biri `(SlotIndex, X, Y, Item)` |
| **Saf mı?** | **Evet.** `GameState`'i okur, hiçbir şeyi mutasyona uğratmaz. `PowerupEffects` sınıf yorumu bunu açıkça belirtir: *"the CHOOSING here is what makes the powerup's actual rules testable at all"* |
| **Sahne bağımlılığı** | **Yok.** `Systems.PowerupSystem` asmdef'i yalnız Core, Data, ProgressionSystem referans eder. `PowerupEffectsTests.cs` (40.4 KB) sahnesiz test ediyor |
| **Keyfi tahta/tepsi durumundan plan yapabilir mi?** | **Evet** — girdisi `GameState` + tepsi listeleri; nereden geldikleri önemsiz |

### 9.2 Algoritma (kısaca — detay `external-tool-project-data-spec.md` §5.1'de)

İki geçişli, **paylaşılan bütçe** üzerinden açgözlü atama:
- **Geçiş 1:** tam tamamlanabilen biletleri tamamla (slot sırasında)
- **Geçiş 2:** kalanlara kısmi doldurma, **bir boşluk kasten bırakarak**

### 9.3 Cevap: **PARTIALLY**

## `PlanAutoCollect` PlayerPolicyV1 olarak kullanılabilir — bir sarmalayıcı ile.

**Doğrudan devralınanlar:**
- Item→tepsi atama kararı (asıl zor kısım)
- Paylaşılan bütçe mantığı — "tek burger'ı yanlış slota verip kilitleme" bug'ının düzeltmesi dahil
- Tray-aware `OutstandingFor` — yarı dolu tepsiyi doğru okur
- Saflık ve test edilebilirlik

**Sarmalayıcının eklemesi gerekenler:**

| Eksik | Neden | Çözüm |
|---|---|---|
| **Zaman yok** | Tek seferlik toplu plan — powerup basışı anlıktır | Runner hamle başına `actionDelaySeconds` harcar |
| **Yeniden planlama yok** | Plan kapalı liste; koşu sırasında gelen item'lar giremez | Plan tükenince yeniden çağır |
| **Geçiş 2 boşluk bırakır** | Powerup'ın "asla can kaybettirme" güvenlik kuralı | v1'de **koru** — bkz. aşağıdaki yanlılık notu |
| **Bekleme kararı yok** | Boş plan = yapacak iş yok | Runner boş planı "bekle" olarak yorumlar |
| **Aciliyet önceliği yok** | Slot sırasında gider | v1'de kabul et; v3'te alternatif politika |

### 9.4 Bilinen yanlılık — raporlanmalı

Geçiş 2'nin "bir boşluk bırak" kuralı, politikanın **asla yanlış teslimat yapamayacağı** anlamına gelir. Gerçek oyuncu yapar.

**Sonuç:** v1 simülasyonu **can kaybını olduğundan az gösterir** (yalnız timeout kaynaklı kayıplar görünür). Bu, "yapısal zorluğu ölç" hedefi için **doğru taraftır** — çünkü kalan her can kaybı oyuncu hatasından değil, sistemden gelir. Ama rapor bunu açıkça yazmalıdır.

---

## §10 · Simülasyon durum modeli

### 10.1 Temel karar: **yeni bir durum modeli yazmayın**

`GameState` zaten simülasyon durumudur. Sahne durumu içermez (§3.1 denetimi: sıfır `GameObject`/`Transform`). Kopyalamak, `day-runtime-spec.md`'nin uyardığı duplikasyonu geri getirir.

Öneri: `GameState`'i **sahiplenen** ince bir kompozisyon sınıfı.

### 10.2 `DaySimulation` — alanlar

| Alan | Tip | Üretim kaynağı | Sahiplik | Not |
|---|---|---|---|---|
| `state` | `GameState` | `Core/GameState.cs` | **Üretim nesnesi** | Tahta, slotlar, canlar, olaylar |
| `state.Board` | `BoardGrid` | `Core/BoardGrid.cs` | Üretim | `OccupiedCellCount`, `PendingSpawnCount`, `IsFull` hazır |
| `state.TicketSlots` | `Ticket[3]` | `Core/GameState.cs` | Üretim | Aktif biletler |
| `provider` | `DayTicketSequenceProvider` | `Systems.DaySystem` | Üretim | Upcoming queue **buradan** — ayrı liste tutma |
| `slotManager` | `TicketSlotManager` | `Systems.TicketSystem` | Üretim | Atama + tick + gün sonu |
| `trayManager` | `TrayManager` | `Systems.TraySystem` | Üretim | 3 tepsi + batch check |
| `distributor` | `BoardDistributor` | `Systems.BoardDistribution` | Üretim | Sticky durumu taşır |
| `economy` | `EconomyCalculator` | `Systems.EconomySystem` | Üretim | Payout |
| `lifecycle` | `DayLifecycleManager` | `Systems.DayLifecycle` | Üretim | **İstatistiklerin çoğu zaten burada** |
| `virtualTime` | `float` | — | **Simulator** | Yeni |
| `livesLost` | `int` | — | **Simulator** | `loseLife` delegesinden (§5.5 seçenek B) |
| `openingSuppressionCounter` | `int` | `GameManager` eşdeğeri | **Simulator** | Yeni — kablolama |
| `metrics` | `SimulationMetrics` | — | **Simulator** | Yeni, bkz. §12 |

### 10.3 DTO gerekmez

Her alan üretim nesnesine **doğrudan referans** verebilir. Gerekçe: hiçbiri sahneye, MonoBehaviour'a veya statik duruma bağlı değil (§3.1). Hafif DTO yalnız **sonuç raporu** için gerekir (`SimulationResult`), çünkü koşu bittikten sonra nesneler atılır.

### 10.4 `SimulationView` — politikaya verilen okuma yüzeyi

Politika `DaySimulation`'ın tamamını görmemeli (yanlışlıkla mutasyon riski). İnce bir okuma sarmalayıcısı:

```csharp
public readonly struct SimulationView
{
    public GameState State { get; }                              // okuma
    public IReadOnlyList<IReadOnlyList<BoardItem>> TrayContents { get; }
    public float VirtualTime { get; }
}
```

`PlanAutoCollect` tam olarak bu iki alanı ister.

---

## §11 · Olay modeli — sabit adım vs discrete event

### 11.1 Öneri: **sabit adım**

Gerekçeler, üretim koduna dayalı:

1. **`TicketSlotManager.Tick(float)` zaten sabit adım için tasarlanmış** — delta parametre.
2. **Tick maliyeti O(3)** — 3 slot, birer float çıkarma. Discrete event'in kurulum maliyeti bundan yüksek.
3. **Time Reset powerup'ı zamanı geri alır** → önceden hesaplanmış timeout olayları geçersizleşir. Sabit adımda böyle bir sorun yok.
4. **Board dağıtımı zaten olay tetiklemeli** — `TicketAssigned`. Yani sistemin pahalı yarısı zaten discrete; tick yalnız sayaç düşürür.

### 11.2 Performans tahmini

10.000 koşu için kaba hesap (`Dt = 0.1`):

| Büyüklük | Hesap |
|---|---|
| Gün başına sanal süre | ~150–250 sn (N=10-15 bilet, 3 paralel slot) |
| Koşu başına tick | ~2.000 |
| 10k koşu için toplam tick | **~20M** |
| Tick maliyeti | 3 float işlemi + döngü — ihmal edilebilir |
| Koşu başına `OnOrderPlaced` | ~N+3 ≈ 15 |
| 10k koşu için `OnOrderPlaced` | **~150k** |

**Darboğaz `OnOrderPlaced`'dır**, tick değil. Her çağrı `neededCounts`, `presentCounts`, `activeSet`, aday listeleri gibi birkaç `Dictionary`/`List` allocate eder ve 20 hücreyi tarar. 150k çağrı Editor'de saniyeler mertebesindedir — kabul edilebilir, ama GC baskısı yaratır.

`Dt = 0.25` tick sayısını 4'e böler ve darboğazı değiştirmez; **hassasiyet gerekmiyorsa tercih edilebilir.**

> **UNKNOWN:** Gerçek allocation profili ölçülmedi. 10k koşu hedefine yaklaşıldığında Unity Profiler ile bakılmalı. Gerekirse `BoardDistributor`'a dictionary havuzu eklemek bir optimizasyon olur — ama **davranış değiştirmeyen** bir optimizasyon olduğundan emin olunmalı.

---

## §12 · Metrikler

### 12.1 Zaten var olan olaylar ve sayaçlar

Projenin **çoğu metriği zaten ürettiği** ortaya çıktı:

| Kaynak | Üye | Verdiği |
|---|---|---|
| `GameState` | `TicketDelivered` (EventBus) | `(SlotIndex, Ticket)` — teslimat anı |
| `GameState` | `TicketCancelled` (EventBus) | Timeout/iptal anı |
| `GameState` | `TicketAssigned` (EventBus) | Yeni bilet anı |
| `GameState` | `DayCompleted` (EventBus) | Gün sonu + teslimat sayısı |
| `GameState` | `LivesChanged`, `LivesDepleted` | Can olayları |
| `GameState` | `TicketsDeliveredToday` | Teslimat sayacı |
| `BoardGrid` | `CellChanged` (EventBus) | Hücre değişimi |
| `BoardGrid` | `OccupiedCellCount`, `CellCount`, `IsFull` | Doluluk — **anlık okunabilir** |
| `BoardGrid` | `PendingSpawnCount` | Kuyruk uzunluğu |
| `DayLifecycleManager` | `OrdersDeliveredValue`, `TipsValue`, `Total` | **Gelir dökümü hazır** |
| `DayLifecycleManager` | `WrongDeliveryCount`, `TimeoutCount`, `OrdersFailedCount` | **Hata dökümü hazır** |
| `DayLifecycleManager` | `SavedSeconds`, `TotalTicketSeconds`, `StarScore`, `StarCount` | **Yıldız puanı hazır** |
| `DeliveryPayoutResult` | `Tier`, `TipRate`, `OrderValue`, `Tip`, `Total`, `RemainingSeconds` | Teslimat başına tam döküm |

### 12.2 Metrik → örnekleme noktası

| Metrik | Nerede örneklenir | Mevcut olay/üye | Yeni kod? |
|---|---|---|---|
| Tamamlandı / başarısız | Döngü sonu | `slotManager.IsDayComplete` + `livesLost >= 3` | Hayır |
| Kaybedilen can | `loseLife` delegesi | `Action<int>` — ctor'dan | Hayır |
| Yanlış teslimat | `lifecycle.WrongDeliveryCount` | `RecordFailure(WrongDelivery)` | Hayır |
| Timeout | `lifecycle.TimeoutCount` | `RecordFailure(Timeout)` | Hayır |
| Ort. kalan süre oranı | `TicketDelivered` aboneliği | `payout.RemainingSeconds / ticket.TimeLimitSeconds` | Küçük — abone |
| Bahşiş kademesi sayımı | `TicketDelivered` aboneliği | `economy.CalculatePayout(ticket).Tier` | Küçük — abone |
| Toplam gelir | Döngü sonu | `lifecycle.Total` | Hayır |
| Ort. tahta doluluğu | **Her tick** | `state.Board.OccupiedCellCount` | Küçük — toplayıcı |
| Tepe tahta doluluğu | **Her tick** | Aynı, `Math.Max` | Küçük |
| Board-full olayları | Her tick veya spawn sonrası | `state.Board.IsFull` | Küçük |
| Pending spawn olayları | Her tick | `state.Board.PendingSpawnCount` | Küçük |
| **Sıfır-tamamlanabilir tur** | `OnOrderPlaced` sonrası | **YOK — hesaplanmalı** | Bkz. 12.3 |
| Süre | Döngü sonu | `virtualTime` | Hayır |
| Teslim edilen bilet sayısı | Döngü sonu | `state.TicketsDeliveredToday` | Hayır |

### 12.3 Tek gerçek yeni hesap: sıfır-tamamlanabilir tur

`day-runtime-spec.md` §10.3'te "sistemin sağlık göstergesi" olarak işaretlenen metrik. Üretimde karşılığı yok, ama **mevcut saf yardımcılarla** yazılabilir:

```csharp
// Simülasyon tarafında, saf
static bool AnyActiveTicketCompletable(GameState state)
{
    var boardCounts = CountBoard(state.Board);   // RequiredItemKey → adet
    for (var i = 0; i < GameState.TicketSlotCount; i++)
    {
        var t = state.TicketSlots[i];
        if (t == null || t.State != TicketState.Active) continue;
        if (IsCompletableFrom(TicketRequirements.RequiredCounts(t), boardCounts)) return true;
    }
    return false;
}
```

`DayValidator.IsCompletableFrom` (private) tam olarak bu mantığı taşıyor. **İki seçenek:**

- **A:** `DayValidator`'daki private yardımcıları `public static` yap → duplikasyon yok, ama üretim sınıfının yüzeyi genişler
- **B:** Simülasyon assembly'sinde `TicketRequirements` üzerine 15 satırlık kendi kopyasını yaz

**B önerilir.** `TicketRequirements.RequiredCounts` zaten paylaşılan kural; kalan yalnız iki sözlüğü karşılaştırmaktır ve orada "kural" yoktur. `DayValidator`'ın yüzeyini simülasyon için genişletmek, o sınıfın tek işi (authoring gate) ile çelişir.

> Bu, dökümanın **kod duplikasyonunu kabul ettiği tek yerdir** ve kopyalanan şey bir algoritma değil, iki `Dictionary`'nin karşılaştırılmasıdır.

---

## §13 · Batch runner

### 13.1 Öneri: saf senkron çekirdek + `EditorApplication.update` ile parçalama

```
SimulationRunner.RunOne(day, options, seed) → SimulationResult
    saf, senkron, UnityEditor bağımlılığı YOK, ~50 ms
              ↓
SimulationBatchRunner  (Editor)
    EditorApplication.update'e abone olur,
    her frame'de N koşu yapar (N ~ 20),
    ilerlemeyi çizer, iptal edilebilir
```

### 13.2 Seçenek değerlendirmesi

| Seçenek | Değerlendirme |
|---|---|
| **Düz senkron** | 1–100 koşu için doğru. 10k'da Editor donar (dakikalar) |
| **`EditorApplication.update` parçalama** | **ÖNERİLEN.** Editor donmaz, iptal edilebilir, ilerleme çubuğu mümkün. Basit |
| **`Task` / `Thread`** | **ÖNERİLMEZ.** `ScriptableObject` erişimi (`FoodItemConfig.BasePrice`, `GameConfig.BoardWidth`) main thread dışında güvenli değil. Ayrıca `Debug.Log` thread-safe değil |
| **Job System / Burst** | **UYGUN DEĞİL.** Kod `class`, `Dictionary`, `List`, `HashSet` kullanıyor — Burst uyumlu değil. Yeniden yazım gerektirir ki bu tam da kaçınmak istenen şey |

### 13.3 `EditorUtility.DisplayProgressBar` notu

Kullanılabilir ama **her koşuda değil** — kendisi pahalıdır. Frame başına bir kez güncellensin. İptal için `DisplayCancelableProgressBar` yerine paneldeki kendi "Cancel" butonu tercih edilmeli (modal bar Editor'ü bloke eder).

### 13.4 Determinizm ve parçalama

Parçalama determinizmi **bozmaz**, çünkü her koşu bağımsız ve kendi seed'ini alır. Koşuların tamamlanma sırası önemli değildir; sonuçlar `seed`'e göre indekslenir.

---

## §14 · Play Mode parity testi

### 14.1 Temel gözlem: algoritma sapması yapısal olarak imkânsız

Simulator, üretim sınıflarının **aynı derlenmiş kopyalarını** kullanır. `BoardDistributor.SelectGuaranteedTickets` değişirse simulator de anında değişir. Yani şu risk **yoktur**: "üretim algoritması değişti, simülatör eski davranışta kaldı."

### 14.2 Gerçek risk: kablolama sapması

Sapabilecek tek şey, simulator'ın **`GameManager`'dan kopyaladığı kablolamadır** (§5.1):

- `OnOrderPlaced`'in çağrılma anı ve sırası
- `openingAssignmentsWithoutDistribution` sayacı
- `lookaheadCount = Math.Max(3, UpcomingQueueSize)`
- `onAccepted` → `RemoveItem` sırası
- Gün başlangıcı sırası (`ApplyDayStartBoardPreSeed` → `FillEmptySlots`)

`GameManager` Assembly-CSharp'ta olduğu için bir test onu **doğrudan çağıramaz**.

### 14.3 Önerilen strateji: iki katmanlı

**Katman 1 — EditMode kablolama testi (ucuz, hemen yapılabilir)**

`Tests/EditMode/SimulationWiringTests.cs`: simülasyonu bilinen bir gün + seed ile çalıştır, olay sırasını kaydet, **`day-runtime-spec.md` §3.1 ve §6.3'te belgelenen sıraya** karşı doğrula. Mevcut `DayTicketSlotManagerIntegrationTests.cs` bunun şablonudur.

Bu, kablolamanın *dökümante edilmiş sözleşmeye* uygunluğunu kilitler.

**Katman 2 — Play Mode parity (pahalı, v2+)**

Gerçek karşılaştırma için üretimin deterministik olması gerekir ve **şu an değil** (§7.3). Gereken enstrümantasyon:

| İhtiyaç | Değişiklik | Davranış değişikliği |
|---|---|---|
| `GameManager` seed alabilmeli | `BoardDistributor`/`TrayManager` ctor'larına seed geçir | **EVET** — üretim deterministik olur |
| Olay izi kaydı | `#if UNITY_EDITOR` bir trace listener | Hayır |
| Karşılaştırma | Trace dosyası diff | Hayır |

Karşılaştırılacaklar: spawn edilen `RequiredItemKey` dizisi, bilet atama sırası, timeout anları, teslimat sırası, payout, son durum.

> **NEEDS DESIGN DECISION:** Üretime seed enjeksiyonu eklemek, oyunun tekrarlanabilir olmasını sağlar (test için iyi) ama `random = null` varsayılanını kullanan mevcut davranışı değiştirir. Debug menüsüne bir "deterministic mode" bayrağı olarak eklenmesi en az riskli yol olabilir. **v1 için gerekmiyor.**

---

## §15 · Assembly / Editor sınırları

### 15.1 Mevcut harita

```
ExpoTheExplorer.Data              (references: [])
        ↑
ExpoTheExplorer.Core              (→ Data)
        ↑
13 × ExpoTheExplorer.Systems.*    (→ Core, Data, birbirlerine)
        ↑
ExpoTheExplorer.Session           (→ Core, Data, Progression, Lives, Key, Powerup, DaySystem)

ExpoTheExplorer.Editor            [Editor-only]
    → Data, Core, DaySystem, TicketSystem, ProgressionSystem

ExpoTheExplorer.Tests.EditMode    [Editor-only]
    → hepsi + Editor

Assembly-CSharp  (asmdef YOK — Scripts/Bootstrap, Scripts/UI, Scripts/Debug)
    ⚠ hiçbir asmdef tarafından referans EDİLEMEZ
```

**Hiçbir runtime assembly `UnityEditor`'a bağlı değil.** Yön zaten doğru.

### 15.2 ZORUNLU DEĞİŞİKLİK: Editor asmdef eksik referanslar

`Assets/Editor/ExpoTheExplorer.Editor.asmdef` şu an **beş assembly'yi referans etmiyor**:

| Eksik | Neden gerekli |
|---|---|
| `ExpoTheExplorer.Systems.BoardDistribution` | `BoardDistributor` |
| `ExpoTheExplorer.Systems.TraySystem` | `TrayManager`, `TraySlot` |
| `ExpoTheExplorer.Systems.EconomySystem` | `EconomyCalculator` |
| `ExpoTheExplorer.Systems.DayLifecycle` | `DayLifecycleManager` |
| `ExpoTheExplorer.Systems.PowerupSystem` | `PowerupEffects.PlanAutoCollect` |

> **Unity'de assembly referansları geçişli değildir.** `Editor → DaySystem → BoardDistribution` zinciri Editor'e `BoardDistributor`'ı **vermez**; doğrudan referans şarttır.

**Ancak:** §16'daki öneri simülasyon çekirdeğini ayrı bir `ExpoTheExplorer.Simulation` assembly'sine koyar. O durumda bu beş referans **Simulation asmdef'ine** gider ve `Editor` yalnız `Simulation`'ı ekler. Sonuç aynı, ayrım daha temiz.

### 15.3 Erişilebilirlik engelleri

| Üye | Erişim | Etki |
|---|---|---|
| `GameState.SoftMoney` / `Gems` setter | **`internal`** | `Core/AssemblyInfo.cs` yalnız `ProgressionSystem` ve `Tests.EditMode`'a açıyor. **Simülasyon assembly'si para yazamaz** |
| `TicketSlotManager.AssignTicket` | `private` | Sorun değil — `FillEmptySlots`/`DeliverTicket`/`CancelTicket` yeterli |
| `PowerupEffects.OutstandingFor`, `MaxMovesFor`, `BuildBudget` | `private` | Sorun değil — `PlanAutoCollect` ve `UnwantedTrayItems` public |
| `DayValidator.IsCompletableFrom` | `private` | §12.3'te ele alındı — kopyalanacak |
| `DayEditorModel.sharedGameConfig` | `private` | Panel model içinden çizilirse sorun yok (§2.3) |

**Para yazma engeli aslında iyi haber:** simulator geliri `DayLifecycleManager.Total`'dan okumalıdır (zaten orada birikiyor), `GameState.SoftMoney`'den değil. `AssemblyInfo.cs`'e yeni bir isim eklemek **yapılmamalı** — o dosyanın kendi yorumu bunu "gerçek bir mimari karar, build düzeltmesi değil" diye işaretliyor.

---

## §16 · Önerilen dosya yapısı

Mevcut yapıya uydurulmuş, minimal:

```
Assets/Scripts/Simulation/                          ← YENİ assembly
    ExpoTheExplorer.Simulation.asmdef               [Editor-only, bkz. not]
    DaySimulation.cs            // GameState + manager'ları sahiplenen kompozisyon
    SimulationOptions.cs        // seed, Dt, actionDelaySeconds, maxVirtualSeconds
    SimulationResult.cs         // koşu sonucu DTO'su
    SimulationRunner.cs         // static RunOne(DayDefinition, options) → result
    SimulationMetrics.cs        // örnekleme + toplama
    IPlayerPolicy.cs
    AutoCollectPolicy.cs        // PlanAutoCollect sarmalayıcısı

Assets/Editor/
    DayEditorSimulationPanel.cs // IMGUI çizimi (mevcut *Preview.cs deseniyle aynı)
    SimulationBatchRunner.cs    // EditorApplication.update parçalama (v2)
```

**Neden `Scripts/Simulation/` ve `Editor/` değil:**
- EditMode testleri asmdef adıyla referans verebilir → simülasyon çekirdeği test edilebilir olur
- `UnityEditor` bağımlılığı çekirdekten uzak kalır → §15'teki yön kuralı korunur
- Mevcut `Systems/*` desenine uyar

**`ExpoTheExplorer.Simulation.asmdef` içeriği:**

```json
{
  "name": "ExpoTheExplorer.Simulation",
  "rootNamespace": "ExpoTheExplorer.Simulation",
  "references": [
    "ExpoTheExplorer.Core",
    "ExpoTheExplorer.Data",
    "ExpoTheExplorer.Systems.DaySystem",
    "ExpoTheExplorer.Systems.TicketSystem",
    "ExpoTheExplorer.Systems.BoardDistribution",
    "ExpoTheExplorer.Systems.TraySystem",
    "ExpoTheExplorer.Systems.EconomySystem",
    "ExpoTheExplorer.Systems.DayLifecycle",
    "ExpoTheExplorer.Systems.PowerupSystem"
  ],
  "includePlatforms": ["Editor"],
  "autoReferenced": true,
  "noEngineReferences": false
}
```

> **NEEDS DECISION — `includePlatforms: ["Editor"]`?**
> **Evet önerilir:** simulator bir araçtır, oyuna girmemeli. EditMode testleri zaten Editor-only olduğu için erişimini kaybetmez. Eğer ileride runtime içi otomatik denge testi istenirse bu kaldırılır.

`Assets/Editor/ExpoTheExplorer.Editor.asmdef`'e tek ekleme: `"ExpoTheExplorer.Simulation"`.

---

## §17 · Minimum refactor planı

### 17.1 ZORUNLU değişiklikler

| # | Dosya | Sebep | Kapsam | Risk | Davranış değişikliği |
|---|---|---|---|---|---|
| **R1** | `Assets/Editor/ExpoTheExplorer.Editor.asmdef` | Simülasyon panelinin `ExpoTheExplorer.Simulation`'a erişmesi için | 1 satır (`references` dizisine ekleme) | **Yok** | **HAYIR** |
| **R2** | `Assets/Editor/DayEditorModel.cs` | Simülasyon panelini çizmek için kanca | ~5 satır (`[OnInspectorGUI]` metodu + bir UI state alanı) | **Çok düşük** — mevcut desenin dördüncü tekrarı | **HAYIR** — yeni bir foldout, mevcut hiçbir şey taşınmıyor |

**Zorunlu üretim kodu değişikliği toplamı: 6 satır.**

### 17.2 Gerekmeyenler — açıkça

| Beklenen refactor | Neden gerekmiyor |
|---|---|
| RNG enjeksiyonu | Her sistem zaten `System.Random` kabul ediyor (§7.1) |
| `ISimulationClock` | `Tick(float)` zaten parametrik (§6.1) |
| `BoardDistributor` extraction | Zaten saf ve sahnesiz koşuyor (§4.5) |
| `TicketSlotManager` adaptörü | Ctor delegeleri zaten resmi genişletme noktası (§5.5) |
| `GameState` kopyası / DTO | Sahne durumu içermiyor (§10.1) |
| `DayEditorModel` dönüşümü | `ToDayDefinition()` zaten public (§1.5) |
| `PowerupEffects` extraction | `PlanAutoCollect` zaten saf ve public (§9.1) |
| `DayEditorWindow` bölünmesi | Dokunulmuyor (§2.4) |
| `AssemblyInfo.cs`'e yeni `InternalsVisibleTo` | Gelir `DayLifecycleManager`'dan okunur (§15.3) |

### 17.3 Opsiyonel — v1'de gerekmez

| # | Değişiklik | Ne zaman | Davranış değişikliği |
|---|---|---|---|
| O1 | `DayValidator`'ın `IsCompletableFrom`'unu public yapmak | §12.3'te B seçilirse gerekmez | HAYIR |
| O2 | `BoardDistributor`'a dictionary havuzu | 10k koşuda GC sorun olursa | HAYIR (dikkatli yapılırsa) |
| O3 | Üretime seed enjeksiyonu | Play Mode parity (§14.2) | **EVET** — dikkat |

---

## §18 · V1 uygulama planı

**Hedef:** Day Editor'de Simulation foldout'u · kaydedilmemiş günü simüle et · deterministik seed · tek yetkin politika · bir tam gün · sonuç gösterimi.

**Kapsam dışı:** grafikler, Monte Carlo UI, zorluk skoru, karşılaştırma modu, powerup stratejisi, gerçekçi insan modeli.

| Adım | Yapılacak | Dosyalar |
|---|---|---|
| **1** | Simulation assembly'sini kur | **YENİ:** `Assets/Scripts/Simulation/ExpoTheExplorer.Simulation.asmdef` (§16) |
| **2** | Editor'e referans ekle | **DÜZENLE:** `Assets/Editor/ExpoTheExplorer.Editor.asmdef` — R1 |
| **3** | Seçenek/sonuç tiplerini yaz | **YENİ:** `SimulationOptions.cs` (seed, Dt=0.1, actionDelaySeconds, maxVirtualSeconds), `SimulationResult.cs` |
| **4** | Kompozisyonu kur — **en kritik adım** | **YENİ:** `DaySimulation.cs`. `GameManager.Awake` + `OnTicketAssigned` + `ApplyDayStartBoardPreSeed` eşdeğerleri (§5.1, §5.2). Üretim nesnelerini kurar, hiçbir algoritma yazmaz |
| **5** | Politikayı yaz | **YENİ:** `IPlayerPolicy.cs`, `AutoCollectPolicy.cs` — `PowerupEffects.PlanAutoCollect` + `UnwantedTrayItems` sarmalayıcısı (§8.3, §9.3) |
| **6** | Metrikleri topla | **YENİ:** `SimulationMetrics.cs`. `TicketDelivered`/`TicketCancelled` abonelikleri + tick başına doluluk örneklemesi (§12.2) |
| **7** | Runner | **YENİ:** `SimulationRunner.cs` — `static SimulationResult RunOne(DayDefinition, SimulationOptions)`. Saf, senkron |
| **8** | Doğruluk testi | **YENİ:** `Assets/Tests/EditMode/SimulationRunnerTests.cs` — aynı seed iki kez aynı sonuç; bilinen bir gün tamamlanıyor; teslimat sayısı ≤ N |
| **9** | Editor paneli | **YENİ:** `Assets/Editor/DayEditorSimulationPanel.cs` — seed alanı, Run butonu, sonuç tablosu |
| **10** | Panele kanca | **DÜZENLE:** `Assets/Editor/DayEditorModel.cs` — R2, `[FoldoutGroup("Simulation"), PropertyOrder(-0.1f)]` |
| **11** | Kablolama testi | **YENİ:** `Assets/Tests/EditMode/SimulationWiringTests.cs` — §14.3 katman 1 |

### Panelin göstereceği (v1)

```
Simulation                                    [Seed: 1000] [Run]
─────────────────────────────────────────────────────────────
Completion            COMPLETED / FAILED (lives)
Delivered             12 / 12
Lives lost            1
Timeouts              1
Wrong deliveries      0
Duration              184.3 s (virtual)
Revenue               248  (orders 210 + tips 38)
Board occupancy       avg 11.2 / 20   peak 17 / 20
Board full events     0
Pending spawns (max)  0
```

### Uyarılar panelde gösterilmeli

- Gün `IsValid` değilse **çalıştırma** — `ValidationSummary`'yi göster (§1.5 nokta 2)
- `DayIndex == 0` ise "tutorial day — timing metrics invalid" uyarısı (§5.7)
- Politikanın yanlış teslimat yapamadığı notu (§9.4)

---

## §19 · V2 / V3 yol haritası

### V2 — batch

| İş | Not |
|---|---|
| `SimulationBatchRunner` | `EditorApplication.update` parçalama (§13.1) |
| 100 / 1k / 10k koşu | Seed aralığı: `baseSeed + i` |
| Toplu metrikler | Ortalama, medyan, p5/p95, tamamlanma oranı |
| Histogramlar | Can kaybı, süre, gelir dağılımları |
| **Tahta baskısı metrikleri** | Sıfır-tamamlanabilir tur oranı (§12.3), doluluk eğrisi, pending spawn olayları — `day-runtime-spec.md` §10.3'ün sağlık göstergeleri |
| GC profili | 10k koşuda allocation ölçümü (§11.2 UNKNOWN) |

### V3 — analiz

| İş | Not |
|---|---|
| Çoklu oyuncu profili | `actionDelaySeconds`, `mistakeChance`, `ticketPriority` (§8.4) |
| Mevcut vs taslak karşılaştırma | Kaydedilmiş gün ile kaydedilmemiş modeli yan yana çalıştır — `ToDayDefinition()` ikisini de verir |
| Balancing dashboard | Ayrı EditorWindow (§2.4 notu) |
| Parametre taraması | `NoiseLeakCountLambda` × `GuaranteedTicketCount` ızgarası |
| Powerup politikaları | §6'daki eşik kararı gerekir |
| Play Mode parity | §14.3 katman 2 — üretime seed enjeksiyonu (O3) |

---

## §20 · Blockers / Questions

### ✅ SAFE TO IMPLEMENT NOW

Doğrulanmış, engel yok:

1. **Kaydedilmemiş günü okumak** — `DayEditorModel.ToDayDefinition()` zaten public (`DayEditorModel.cs:1226`)
2. **`BoardDistributor`'ı sahnesiz çalıştırmak** — `BoardDistributionTests.cs` kanıtı
3. **Tam gün yaşam döngüsü** — `DayTicketSlotManagerIntegrationTests.cs` kanıtı
4. **Deterministik RNG** — her sistem `System.Random` kabul ediyor, enjeksiyon gerekmez
5. **Sanal zaman** — `TicketSlotManager.Tick(float)` zaten parametrik
6. **`PlanAutoCollect`'i politika olarak kullanmak** — saf, public, sahnesiz test edilmiş
7. **Metriklerin çoğu** — `DayLifecycleManager` + `BoardGrid` + `GameState` olayları hazır
8. **Simülasyon panelini eklemek** — mevcut `[OnInspectorGUI]` deseninin dördüncü tekrarı
9. **Sabit adım döngüsü** — üretim koduna en uygun model
10. **Editor-safe batch** — `EditorApplication.update` parçalama

### ⚠️ NEEDS SMALL REFACTOR

Toplam **6 satır üretim kodu**, ikisi de davranış değiştirmez:

| # | Ne | Kapsam | Risk |
|---|---|---|---|
| R1 | `ExpoTheExplorer.Editor.asmdef` → `"ExpoTheExplorer.Simulation"` referansı | 1 satır | Yok |
| R2 | `DayEditorModel.cs` → `[FoldoutGroup("Simulation")]` kancası + UI state alanı | ~5 satır | Çok düşük |

Ayrıca **yeni dosyalar** (üretim kodunu değiştirmez): §16'daki 8 dosya + 2 test dosyası.

Küçük kopyalama kabul edilen tek yer: **sıfır-tamamlanabilir kontrolü** (§12.3) — `TicketRequirements` üzerine ~15 satır sözlük karşılaştırması. Algoritma değil.

### ❓ NEEDS DESIGN DECISION

| # | Soru | Bağlam | Öneri |
|---|---|---|---|
| Q1 | **`actionDelaySeconds` kaç olmalı?** | Projede oyuncu hamle hızı hiçbir yerde tanımlı değil. Tüm zaman baskısı buna bağlı | Gerçek oynanıştan ölç. O zamana kadar `0.5f` yer tutucu + duyarlılık raporu |
| Q2 | **`Dt` = 0.1 mi 0.25 mi?** | Timeout anını ±Dt kaydırır; bahşiş kademesi eşiğine yakın teslimatlar etkilenebilir | v1'de `0.1f`; 10k batch'te hız sorun olursa `0.25f` |
| Q3 | **`Simulation` asmdef Editor-only mi?** | Editor-only ise oyuna girmez ama runtime içi test imkânsız olur | **Editor-only** — simulator bir araçtır |
| Q4 | **Canlar: gerçek `LivesManager` mi sayaç mı?** | `LivesManager` bir `Wallet` ister → profil dosyası riski | v1'de **sayaç** (`Action<int>` delegesi). Continue v2 |
| Q5 | **`PlanAutoCollect`'in "bir boşluk bırak" kuralı korunsun mu?** | Politikanın asla yanlış teslimat yapamamasına yol açar → can kaybı olduğundan az görünür | v1'de **koru** ve raporda belirt. Yapısal zorluk ölçümü için doğru taraf |
| Q6 | **Play Mode parity için üretime seed enjekte edilsin mi?** | Oyunu deterministik yapar — test için iyi, ama mevcut davranışı değiştirir | v1'de **hayır**. Gerekirse debug menüsünde bayrak olarak |
| Q7 | **day_00 (tutorial) simülasyondan çıkarılsın mı?** | Tutorial armed'ken `Tick` hiç çalışmıyor → zaman metrikleri geçersiz | **Evet**, varsayılan olarak atla; zorlanırsa "invalid timing" işaretiyle çalıştır |

### ❔ UNKNOWN

| # | Ne | Neden doğrulanamadı |
|---|---|---|
| U1 | **10k koşuda gerçek allocation/GC profili** | Ölçüm yapılmadı. `OnOrderPlaced`'in dictionary allocation'ları darboğaz adayı (§11.2) |
| U2 | **`Dt` seçiminin sonuçlara duyarlılığı** | Simulator henüz yok; ölçülemez. İlk işlerden biri: `Dt=0.05` vs `0.1` vs `0.25` karşılaştırması |
| U3 | **`TutorialDirector`'ın adımlar arası geçiş mantığı** | day_00 çıkarıldığı için incelenmedi (`NotifyTrayAccepted`, `NotifyTicketPatienceRatio`, `Abort`) |
| U4 | **Odin'in `[FoldoutGroup]` + `[OnInspectorGUI]` kombinasyonunun uzun süren işlemlerle davranışı** | Panel çizimi sırasında saniyeler süren bir Run'ın Odin'in çizim döngüsünü nasıl etkileyeceği test edilmedi. Muhtemelen `EditorApplication.delayCall` ile ertelemek gerekir — `DayEditorWindow` bu deseni zaten iki yerde kullanıyor (`OnDeleteRequested`, `MoveDay`) |

---

*Expo the Explorer · Unity Balancing Simulator Integration Specification · 2026-08-30*
*İlgili: `day-editor-el-kitabi.md` · `day-runtime-spec.md` · `external-tool-project-data-spec.md`*
