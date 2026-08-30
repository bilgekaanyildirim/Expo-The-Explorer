# External Tool — Project Data Specification

**Expo the Explorer · rev 2026-08-30**

Harici web tabanlı Day Editor ve balancing simulator'ın Unity projesinden ihtiyaç duyduğu, **`day_XX.json`'dan elde edilemeyen** her şey.

Bu döküman `day-runtime-spec.md`'yi tekrar etmez. Orada *algoritmalar* var; burada o algoritmaların çalışması için gereken **referans veri** var.

> **Tüm değerler gerçek `.asset` dosyalarından okunmuştur, C# initializer'larından değil.** Kaynak default'unun asset tarafından ezildiği yerler açıkça işaretlidir — üç tanesi var ve üçü de simülasyonu etkiler.

---

## §0 · Yönetici özeti — üç kritik uyuşmazlık

Bu üçü, C# kaynağına bakarak simülatör yazarsan **yanlış sonuç üretir**:

| # | Değer | C# default | **Gerçek asset** | Etkisi |
|---|---|---|---|---|
| D1 | `GameConfig.boardWidth × boardHeight` | 6 × 5 = 30 hücre | **5 × 4 = 20 hücre** | Board kapasitesi %33 daha küçük. Doygunluk ve `pendingSpawns` davranışı tamamen farklı. |
| D2 | `EconomyConfig.tipRateWarning` | 0.15 | **0.10** | Orta kademe bahşiş 1/3 daha düşük. |
| D3 | `EconomyConfig.tipRateCritical` | 0.1 | **0.05** | Alt kademe bahşiş yarı yarıya. |

**D1 ayrıca proje dökümantasyonuyla da çelişiyor:** `ExpoTheExplorer/CLAUDE.md` "Board is a fixed grid, starting point 6x5 = 30 cells" diyor. Asset 5×4. Kod parametrik olduğu için oyun doğru çalışıyor — yanlış olan yalnız dökümandaki sayı.

> **NEEDS DECISION (D1):** 5×4 kasıtlı bir balans kararı mı, yoksa test amaçlı bırakılmış bir değer mi? Simülatör 20 hücreyle mi kalibre edilecek? CLAUDE.md güncellenmeli mi?

---

## §1 · Food Catalog

### 1.1 Kaynak zinciri

| Ne | Nerede | Tip |
|---|---|---|
| Katalog | `Assets/Data/FoodData/FoodCatalog.asset` | `FoodCatalog : ScriptableObject` |
| Katalog sınıfı | `Assets/Data/DataScripts/FoodCatalog.cs` | `List<FoodItemConfig> items` |
| Yemek sınıfı | `Assets/Data/DataScripts/FoodItemConfig.cs` | `FoodItemConfig : ScriptableObject` |
| Yemek asset'leri | `Assets/Data/FoodData/{Burger,Hotdog,Sides,Beverages,Desserts}/Food_*.asset` | 21 dosya |

**Katalog sırası anlamlıdır.** `DayContentGenerator.ResolveFoodPool` havuzu `catalog.Items` üzerinden filtreler, yani havuz sırası = katalog sırası. Bu sıra `TicketFactory.PickWeightedMain`'in rulet taramasını ve `TryAddRandomItem`'in uniform seçimini etkiler. **Export sırayı korumalıdır.**

`FoodCatalog.GetById` bir `FirstOrDefault` linear taramadır — aynı id'ye sahip iki item olsaydı ilki kazanırdı. Şu an yok.

### 1.2 Kritik davranış: modifikasyonlar katalogda değil, yemeklerin içinde

```csharp
public ModificationConfig GetModificationById(string id) =>
    items.SelectMany(item => item.AvailableModifications).FirstOrDefault(mod => mod.Id == id);
```

`FoodCatalog`'un ayrı bir modifikasyon listesi **yoktur**. Bir modifikasyon yalnızca **en az bir yemeğin `availableModifications` listesinde göründüğü için** çözümlenebilir.

**Sonuç:** hiçbir yemeğe bağlı olmayan bir `Mod_*.asset` runtime'da erişilemez; onu referans veren bir Day JSON `unknown modificationId` ile **günü düşürür**. Harici araç modifikasyon listesini yemeklerden türetmeli, klasördeki dosyalardan değil.

### 1.3 Alanlar — simülasyon için gerekli mi?

`FoodItemConfig` serialized alanları:

| Alan | Tip | Gerekli mi | Kullanan |
|---|---|---|---|
| `id` | string | **ZORUNLU** | Day JSON referansları, `RequiredItemKey`, tüm eşleştirme |
| `category` | `FoodCategory` (0/1/2) | **ZORUNLU** | Main/Side/Drink havuz filtresi; "modifikasyonlar yalnız Main'e ait" kuralı |
| `basePrice` | int | **ZORUNLU** | `EconomyCalculator.ResolveOrderValue` — Order Value |
| `availableModifications` | `ModificationConfig[]` | **ZORUNLU** | Poisson tavanı (`min(count, maxMod)`), modifikasyon seçimi |
| `displayName` | string | Editör UI | Yalnız gösterim. Simülasyon okumaz. |
| `sprite` | Sprite ref | Editör UI | Kart/tahta görseli. Simülasyon okumaz. |
| `spriteLayers[]` | `SpriteLayer[]` | **KOZMETİK** | `BoardItem.ResolvedLayers` — yalnız render. Boşsa `sprite`'a düşer. |
| `overallScale` | float 0.1–1.5 | **KOZMETİK** | Hücre içi görsel boyut. |

**`spriteLayers` ve `overallScale` simülasyona hiç girmez.** Bunlar `BoardItem`'ın nasıl çizildiğini belirler; `RequiredItemKey` eşitliği yalnız `(Config, Modifications)` üzerinden hesaplanır. Web editörü tahtayı görsel olarak çizecekse `sprite` yeterlidir — katmanlı render (burger'ın 7 katmanı) ciddi bir iş ve v1 için gereksiz.

### 1.4 Gerçek katalog verisi (21 item, katalog sırasında)

| # | id | displayName | category | basePrice | availableModifications |
|---|---|---|---|---|---|
| 0 | `burger` | Burger | **Main** | 14 | no_lettuce, extra_cheese, no_tomato, extra_patty |
| 1 | `fries` | Fries | Side | 4 | — |
| 2 | `cola` | Cola | Drink | 3 | — |
| 3 | `hotdog` | Hotdog | **Main** | 8 | extraketchup, extramustard, extramayonnaise |
| 4 | `bluberrymilkshake` | Bluberry Milkshake | Drink | 8 | — |
| 5 | `vanillamilkshake` | Vanilla Milkshake | Drink | 8 | — |
| 6 | `strawberrymilkshake` | Strawberry Milkshake | Drink | 8 | — |
| 7 | `fanta` | Fanta | Drink | 3 | — |
| 8 | `sprite` | Sprite | Drink | 3 | — |
| 9 | `beer` | Beer | Drink | 5 | — |
| 10 | `chocolatecookie` | Chocolate Cookie | Side | 7 | — |
| 11 | `doublechocolatecookie` | Double Chocolate Cookie | Side | 7 | — |
| 12 | `vanillaicecream` | Vanilla Ice Cream | Side | 3 | — |
| 13 | `chocolateicecream` | Chocolate Ice Cream | Side | 3 | — |
| 14 | `crispyonion` | Crispy Onion | Side | 6 | — |
| 15 | `mozerellasticks` | Mozerella Sticks | Side | 6 | — |
| 16 | `sufle` | Sufle | Side | 7 | — |
| 17 | `matchacake` | Matcha Cake | Side | 9 | — |
| 18 | `matchadonut` | MatchaDonut | Side | 7 | — |
| 19 | `matchaicecream` | Matcha Ice Cream | Side | 5 | — |
| 20 | `strawberrymatcha` | Strawberry Matcha | Drink | 10 | — |

**Dağılım:** 2 Main · 10 Side · 9 Drink.

Gözlemler:

- **Yalnızca iki Main var.** Bütün modifikasyon sistemi bu ikisine bağlı. Bir günün Main seçimi pratikte "burger mi, hotdog mu, ikisi mi" sorusudur.
- **`basePrice == 0` olan yemek yok** — proje kuralı "fiyatsız yemek content bug'ıdır" der; şu an ihlal yok.
- **Sipariş değeri aralığı:** en ucuz teorik sipariş `hotdog(8)` = 8; en pahalı `burger(14) + matchacake(9) + strawberrymatcha(10)` = 33.
- İki id'de yazım hatası var (`bluberrymilkshake`, `mozerellasticks`) ve bir displayName boşluksuz (`MatchaDonut`). **Bunlar Day JSON'larda kullanılan gerçek id'lerdir — düzeltmek şemayı kırar.** Export olduğu gibi taşımalı.

> **UNKNOWN:** Yemek id'lerinde tutarlı bir adlandırma kuralı yok (`extra_cheese` snake_case ama `extraketchup` bitişik). Yeni içerik eklerken hangi kuralın izleneceği koddan anlaşılmıyor.

### 1.5 Hangi tüketici hangi alanı okur

| Tüketici | id | category | basePrice | availableModifications | sprite | layers |
|---|---|---|---|---|---|---|
| Day Editor (food selection) | ✓ | ✓ | — | ✓ | ✓ | — |
| Ticket generation (`TicketFactory`) | ✓ | ✓ | — | ✓ | — | — |
| Ticket matching (`TraySlot.Matches`) | ✓ | ✓ | — | — | — | — |
| Board simulation (`BoardDistributor`) | ✓ | ✓ | — | — | — | — |
| Economy (`EconomyCalculator`) | — | — | ✓ | — | — | — |
| Board render (`BoardItem`) | — | — | — | — | ✓ | ✓ |

---

## §2 · Modification Catalog

### 2.1 Kaynak

`Assets/Data/DataScripts/ModificationConfig.cs` · asset'ler `Assets/Data/FoodData/{Burger,Hotdog}/Mod_*.asset` (7 dosya).

Serialized alanlar: `id`, `displayName`, `icon` (Sprite), `allowedDirection` (`ModificationDirection`).

### 2.2 Gerçek modifikasyon verisi

| id | displayName | allowedDirection | Hangi yemekler sunuyor | icon |
|---|---|---|---|---|
| `no_lettuce` | No Lettuce | **RemovalOnly** | burger | Lettuce.png |
| `extra_cheese` | Extra Cheese | **Both** | burger | Cheese.png |
| `no_tomato` | No Tomato | **RemovalOnly** | burger | Tomato.png |
| `extra_patty` | Extra Patty | **AdditionOnly** | burger | Patty.png |
| `extraketchup` | Extra Ketchup | **AdditionOnly** | hotdog | Ketchup.png |
| `extramustard` | Extra Mustard | **AdditionOnly** | hotdog | Mustard.png |
| `extramayonnaise` | Extra Mayonnaise | **AdditionOnly** | hotdog | Mayonnaise.png |

**`extra_cheese` projedeki tek `Both` yönlü modifikasyondur.** `TicketFactory.CreateModification`'daki `ModificationAdditionChance` çekimi *yalnız onun için* yapılır — diğer altısı yönünü kendi tanımından alır ve hiç çekim tüketmez. Bu, üretim akışındaki çekim sırasını doğrudan etkiler.

**Hiçbir modifikasyon iki yemek tarafından paylaşılmıyor.** Burger'ın dördü ve hotdog'un üçü ayrık kümeler.

### 2.3 Doğrulama: matching dışında runtime davranışı var mı?

`ModificationConfig`'in tüm proje referansları (`Scripts/` altında):

| Dosya | Kullanım |
|---|---|
| `Core/Modification.cs` | `Config` alanı — `(config, isAddition)` çifti |
| `Core/RequiredItemKey.cs` | Eşitlik imzası: `HashSet<(ModificationConfig, bool)>` |
| `Core/BoardItem.cs` | `SourceModification` — **yalnız sprite katmanı görünürlüğü** |
| `Systems/TicketSystem/TicketFactory.cs` | Seçim + `AllowedDirection` ile yön |
| `Systems/BoardDistribution/BoardDistributor.cs` | Yalnız yorum satırı |

**Sonuç — DOĞRULANDI:**

- **Fiyata etkisi YOK.** `EconomyCalculator.ResolveOrderValue` yalnız `item.BasePrice` toplar; `Modification` hiç okunmaz. "Extra patty" siparişi pahalılaştırmaz. (Proje CLAUDE.md'si bunu açık bir tasarım kararı olarak kaydediyor: *"Do modifications change an order's price? Current decision: **no**"* — hâlâ Open Question listesinde.)
- **Süreye etkisi YOK.** Hiçbir zamanlayıcı kodu `Modification` okumaz.
- **Zorluğa dolaylı etkisi VAR ama yalnız `RequiredItemKey` üzerinden:** modifikasyonlu bir ana yemek farklı bir anahtardır, yani tahtadaki "düz burger" onu karşılamaz. Bu, gerekli-havuz spawn'ını ve gürültü çakışmasını etkiler — ama mekanizma tamamen eşleştirmedir, ayrı bir kural değil.
- **Tek ek runtime rolü kozmetiktir:** `BoardItem.SourceModification` bir sprite katmanının görünüp görünmeyeceğini belirler (`LayerVisibility` + `direction`). Simülasyonu etkilemez.

**Yani: `allowedDirection` üretim aşamasında (yön çekimi), `id` eşleştirmede kullanılır. Başka hiçbir gameplay etkisi yoktur.**

---

## §3 · GameConfig

### 3.1 Kaynak

`Assets/Data/GameConfig.asset` · sınıf `Assets/Data/DataScripts/GameConfig.cs`

### 3.2 Gerçek serialized değerler

```yaml
m_Name: GameConfig
boardWidth: 5           # C# default 6  ← EZİLMİŞ
boardHeight: 4          # C# default 5  ← EZİLMİŞ
startingSoftMoney: 1000
gemsPerStar: 1
```

| Alan | Gerçek değer | C# default | Simülasyon için |
|---|---|---|---|
| `boardWidth` | **5** | 6 | **ZORUNLU** — `BoardGrid` genişliği |
| `boardHeight` | **4** | 5 | **ZORUNLU** — `BoardGrid` yüksekliği |
| `startingSoftMoney` | 1000 | 1000 | Opsiyonel — yalnız yeni oyuncu cüzdanı |
| `gemsPerStar` | 1 | 1 | Opsiyonel — gün sonu gem ödülü |

**Board kapasitesi = 5 × 4 = 20 hücre.** Bu, simülatörün en önemli tek sayısıdır: gerekli havuz + gürültü birikimi bu kapasiteye karşı yarışır ve item'lar kendiliğinden eksilmez.

### 3.3 GameConfig'de OLMAYAN, ama simülasyonun ihtiyaç duyduğu sabitler

| Değer | Nerede | Gerçek değer | Not |
|---|---|---|---|
| Ticket slot sayısı | `Core/GameState.cs` → `public const int TicketSlotCount` | **3** | **Config'te değil, `const`.** Kilitli tasarım kuralı. Poisson bütçe formülü (`Sample(TicketSlotCount-1, ...)`) ve tutorial tray doğrulaması bunu okur. |
| Başlangıç can sayısı | `Core/GameState.cs` → `public const int DefaultStartingLives` | **3** | Yine `const`. Her gün 3 canla açılır; canlar persist etmez. |
| Continue gem ücreti | `Assets/Data/LivesConfig.asset` → `continueGemCost` | **10** | `LivesConfig` bu tek alandan ibaret. |

> **NEEDS DECISION:** `TicketSlotCount` ve `DefaultStartingLives` config'te olmadığı için export'a **elle sabit** olarak yazılmaları gerekir. Kod değişirse export sessizce yanlış kalır. Exporter bunları C# sabitinden okumalı (aşağıda §9'da öneriliyor), literal yazmamalı.

### 3.4 Tray ile ilgili değerler

Tepsi kapasitesi **config'te yoktur ve türetilmiştir**:

```csharp
// TraySlot.IsFull
public bool IsFull(Ticket ticket) => ticket != null && items.Count >= ticket.RequiredItems.Count;
```

Yani tepsi kapasitesi = o slottaki biletin item sayısı (1–3). Ayarlanabilir bir değer değildir. Export'a girmez.

---

## §4 · EconomyConfig

### 4.1 Kaynak

`Assets/Data/EconomyConfig.asset` · sınıf `Assets/Data/DataScripts/EconomyConfig.cs`

### 4.2 Gerçek serialized değerler

```yaml
m_Name: EconomyConfig
warningRatio: 0.666
criticalRatio: 0.333
tipRateFull: 0.2
tipRateWarning: 0.1     # C# default 0.15 ← EZİLMİŞ
tipRateCritical: 0.05   # C# default 0.1  ← EZİLMİŞ
```

| Alan | Gerçek | C# default | Rol |
|---|---|---|---|
| `warningRatio` | 0.666 | 0.666 | Full → Warning eşiği (kalan süre oranı) |
| `criticalRatio` | 0.333 | 0.333 | Warning → Critical eşiği |
| `tipRateFull` | 0.20 | 0.2 | Yeşil bar bahşiş oranı |
| `tipRateWarning` | **0.10** | 0.15 | Turuncu bar bahşiş oranı |
| `tipRateCritical` | **0.05** | 0.1 | Kırmızı bar bahşiş oranı |

**Beşi de payout hesabı için ZORUNLUDUR.** `EconomyCalculator` başka hiçbir değer okumaz.

### 4.3 Payout'u yeniden üretmek için gereken tam küme

`EconomyCalculator.CalculatePayout` girdileri:

1. `ticket.RequiredItems[].BasePrice` → **§1 food catalog'dan**
2. `ticket.RemainingSeconds` ve `ticket.TimeLimitSeconds` → runtime state
3. Yukarıdaki beş `EconomyConfig` değeri

Başka hiçbir şey. Patience formüle girmez, item sayısı girmez.

### 4.4 Sayısal örnek (doğrulama için)

Burger(14) + Fries(4) + Cola(3) = **Order Value 23**

| Teslimat anı | ratio | Kademe | Bahşiş | Toplam |
|---|---|---|---|---|
| Süre %80 kalmış | 0.80 | Full | 23 × 0.20 = 4.6 | **27.6** |
| Süre %50 kalmış | 0.50 | Warning | 23 × 0.10 = 2.3 | **25.3** |
| Süre %10 kalmış | 0.10 | Critical | 23 × 0.05 = 1.15 | **24.15** |

Tam kademe farkı yalnız **%12.5** (27.6 → 24.15). Hızlı oynamanın parasal getirisi şu anki değerlerle çok düşük.

> **Bu bir balans gözlemi, bulgu değil:** kaynak yorumları bu üç oranı açıkça *"placeholders, not balanced"* diye işaretliyor. Simülatörün ilk işlerinden biri muhtemelen bunları taramak olacak.

### 4.5 İlgili ama ayrı: yıldız puanı

`Assets/Data/StarScoreConfig.asset` — payout'a girmez ama gün sonu değerlendirmesi simüle edilecekse gerekir:

```yaml
threeStarScore: 0.55
twoStarScore: 0.3
wrongDeliveryPenalty: 0.2
timeoutPenalty: 0.05
```

Formül (`DayLifecycleManager.StarCount`, `day-runtime-spec.md` kapsamı dışında):
`StarScore = clamp01(SavedSeconds / TotalTicketSeconds − wrongDeliveries×0.2 − timeouts×0.05)`

`TotalTicketSeconds` = günün her biletinin kendi limitinin toplamı (`DayDefinition.TotalTicketSeconds`) — wall clock değil.

### 4.6 Kozmetik — asla payout'a bağlanmamalı

`TicketCardVisualsConfig.TimerSegmentSeconds` yalnız timer barındaki ayırıcı çizgileri konumlandırır. Kaynak yorumu açıkça uyarıyor: *"purely cosmetic — do not wire money to it."* Export'a girmesine gerek yok.

---

## §5 · Player action model

### NO PLAYER POLICY EXISTS

Projede simüle edilmiş bir oyuncuyu sürecek **hiçbir bot, autoplay, solver, hint sistemi, AI veya ticket-seçim sezgiseli yoktur.**

Arama kapsamı: `Scripts/` ve `Tests/` altında `bot`, `autoplay`, `solver`, `heuristic`, `simulate`, `policy`, `agent`, `strategy` kelime araması; ayrıca tüm `Systems/` ve `UI/` dizinlerinin manuel taraması. Tek eşleşme `PowerupEffects.cs` (aşağıda) ve o da bir oyuncu politikası değil, bir powerup'ın iç planlayıcısı.

Oyuncunun her hamlesi `BoardItemDragHandler` → `WorldTrayView.TryAcceptDrop` → `TrayManager.TryAddItem` zincirinden gelir; zinciri başlatan tek şey **gerçek bir dokunuş olayıdır**.

### 5.1 Ama kullanılabilir bir referans politika VAR

`Systems/PowerupSystem/PowerupEffects.PlanAutoCollect` — Auto-Collect powerup'ının karar yarısı. **Saf, deterministik ve test edilebilir**, çünkü kasten UI'dan ayrılmış. Bu, simülatör için hazır bir "yetkin oyuncu" politikası olarak doğrudan portlanabilir.

Algoritma:

```
PlanAutoCollect(state, trayContents):
  budget = BuildBudget(board)          // key → [hücreler], tarama sırası Y dış / X iç
  for slot in 0..2:
      outstanding[slot] = OutstandingFor(state, slot, tray[slot])
                          // biletin gerekli çokluğu EKSİ tepsidekiler
      maxMoves[slot]    = ticket.RequiredItems.Count - tray.Count

  // GEÇİŞ 1 — tamamlanabilenleri tamamla, slot sırasında
  for slot in 0..2:
      if TotalCount(owed) == 0: continue
      if TotalCount(owed) != maxMoves[slot]: continue   // tepside çöp var → atla
      if !CanPayInFull(owed, budget): continue          // bütçe yetmiyor → atla
      Take(owed, budget, slot, TotalCount(owed))        // tam doldur → teslimat
      completed[slot] = true

  // GEÇİŞ 2 — kalanlara kısmi doldurma
  for slot in 0..2:
      if completed[slot]: continue
      limit = maxMoves[slot] - 1        // BİR BOŞLUK KASTEN BIRAKILIR
      if limit <= 0: continue
      Take(owed, budget, slot, limit)
```

Tasarım özellikleri (yorumlardan doğrulandı):

- **Paylaşılan bütçe.** Tahta bir kez okunur; bir slota verilen item sonraki slotlar için havuzdan düşer. Açgözlü-kör bir "her slot ne bulursa alsın" yaklaşımının yarattığı bug bu yüzden düzeltilmiş: tek burger varken slot 0 (burger + olmayan cola) onu alıp tepside kilitliyor, yalnız burger isteyen slot 1 ise aç kalıyordu.
- **Asla can kaybettiremez.** Ya tepsiyi tam doldurur (doğru sipariş → teslimat) ya da en az bir boşluk bırakır, böylece batch check hiç tetiklenmez.
- **Slot sırasında açgözlü**, optimal atama değil. Kullanıcının açık isteği ("tickets in order").
- **Kapalı plan.** Liste ilk hamleden önce donar; koşu sırasında spawn olan item'lar ve gelen biletler plana giremez.

Yardımcılar: `OutstandingFor` (bilet ihtiyacı − tepsi içeriği, yalnız istenen anahtarlar düşülür), `UnwantedTrayItems` (tepsideki fazlalık/yanlış item'lar), `MaxMovesFor`.

### 5.2 Harici simülatörün modellemesi gereken kararlar

`PlanAutoCollect` yalnız **"şu anda hangi item hangi tepsiye"** sorusunu cevaplar. Bir Monte Carlo simülatörü şunları da karara bağlamak zorunda — hiçbiri projede yok:

| # | Karar | Neden gerekli | Öneri |
|---|---|---|---|
| P1 | **Hamle hızı** | Oyuncu saniyede kaç item taşıyor? Tüm zaman baskısı buna bağlı. | Parametre: `movesPerSecond` veya hamle başına sabit gecikme. Gerçek oyundan ölçülmeli. |
| P2 | **Ne zaman beklenir** | Tahtada işe yarar item yokken oyuncu ne yapar? | Beklemek tek seçenek; ama *ne kadar* bekleneceği spawn turunu tetikleyen olaya bağlı. |
| P3 | **Hangi bilet önce** | `PlanAutoCollect` slot sırasında gider. Gerçek oyuncu muhtemelen aciliyete göre gider. | En az iki politika: `slotOrder` (mevcut) ve `mostUrgentFirst`. Karşılaştırmalı çalıştır. |
| P4 | **Aciliyet önceliği** | Süresi bitmek üzere olan bileti kurtarmak için yarıda kalmış bir tepsiyi terk eder mi? | Modellenmeli — `UnwantedTrayItems` geri gönderme mekanizması bunu mümkün kılıyor. |
| P5 | **Hata oranı** | Gerçek oyuncu yanlış item bırakır (can kaybı). Mükemmel oyuncu hiç kaybetmez. | `wrongDropProbability` parametresi. Yıldız puanı ve can ekonomisi için şart. |
| P6 | **Powerup kullanımı** | Ne zaman basılır? | v1'de kapalı (bkz. §6). |
| P7 | **Kısmi tepsi riski** | Tamamlanamayacak bir tepsiyi doldurmaya başlar mı? | `PlanAutoCollect` başlamaz (bir boşluk bırakır). Gerçek oyuncu başlar ve bu bir zorluk kaynağı. |

**Önerilen v1:** `PlanAutoCollect`'i portla, üstüne P1 (hamle hızı) ve P5 (hata oranı) parametrelerini ekle. Bu, "mükemmele yakın oyuncu" tavanını verir — sistemin *yapısal* kilitlenme oranını ölçmek için doğru referans, çünkü kilitlenme oyuncu hatasından değil dağıtımdan geliyorsa orada da görünür.

---

## §6 · Powerups

Kaynak: `Assets/Data/PowerupConfig.asset` · `Systems/PowerupSystem/{PowerupEffects,PowerupManager}.cs` · `Data/DataScripts/PowerupConfig.cs`

### 6.1 Gerçek config değerleri

| id (`PowerupType`) | displayName | startingCharges | gemCost | tutorialIntroDayIndex | tutorialUseTrigger | tutorialCharges |
|---|---|---|---|---|---|---|
| `AutoCollect` = 0 | Auto-Collect | 2 | 20 | 5 | `AtDayStart` (0) | 2 |
| `TimeReset` = 1 | Time Reset | 1 | 10 | 10 | `TicketPatienceBelow` (1) | 2 |
| `NoiseClear` = 2 | Noise Clear | 2 | 5 | 8 | `AtDayStart` (0) | 2 |

`lockLabelFormat: "Day {0}"`. Enum numaraları **load-bearing** — `PowerupManager` dizilerini `(int)type` ile indeksler.

### 6.2 Gameplay etkileri

| Powerup | Tetikleyici | Runtime etkisi | Sınıf / metot |
|---|---|---|---|
| **Auto-Collect** | Oyuncu HUD butonu | Aktif biletlerin hâlâ ihtiyaç duyduğu item'ları tepsilere yerleştirir; tam dolan tepsi **teslimat tetikler**. Önce tepsilerdeki istenmeyen item'ları tahtaya geri gönderir. | `PowerupEffects.PlanAutoCollect` + `UI/AutoCollectRunner.Run` |
| **Time Reset** | Oyuncu HUD butonu | Her aktif biletin `RemainingSeconds` değerini **kendi** `TimeLimitSeconds`'ına geri çeker. Düz bir "+30 sn" değil. | `PowerupEffects.ResetActiveTicketTimers` |
| **Noise Clear** | Oyuncu HUD butonu | Aktif biletlerin *hâlâ ihtiyaç duyduğu sayının üzerindeki* her board item'ını **kalıcı olarak siler**. Tepsi içeriğini hesaba katar. Boşalan hücreler `pendingSpawns`'tan anında dolar. | `PowerupEffects.ClearUnneededItems` / `PlanNoiseClear` + `UI/NoiseClearRunner` |

Etkilenen alanlar:

- **Board içeriği:** Auto-Collect (item alır) ve Noise Clear (item siler) — ikisi de doğrudan.
- **Ticket timer:** yalnız Time Reset.
- **Ticket completion:** yalnız Auto-Collect (dolaylı — tepsi dolarsa teslim olur).
- **Noise/leak:** Noise Clear gürültüyü siler ama **leak algoritmasını değiştirmez**; `leakedTickets` dedup'ı etkilenmez.
- **Lives:** hiçbiri doğrudan can eklemez/eksiltmez. Auto-Collect can kaybını *önler* (yapısal olarak yanlış tepsi oluşturamaz).

### 6.3 Ortak kurallar

- **Boşa basış ücretsizdir.** Efekt `false` dönerse `PowerupManager.TryUse` charge düşmez (GDD 5.2).
- **Kapılar:** `GameManager.CanUsePowerup(type)` — Game Over popup'ı açıkken, gün bittiğinde ve tutorial adımı armed'ken reddeder (öğretilen powerup hariç).
- **Kilit:** `PowerupSettings.IsUnlockedOnDay(dayIndex)` → `!IsTutorialScheduled || dayIndex >= tutorialIntroDayIndex`. Yani **Auto-Collect gün 5'ten, Noise Clear gün 8'den, Time Reset gün 10'dan önce kullanılamaz ve satın alınamaz.** Negatif `tutorialIntroDayIndex` = her zaman açık.
- **Stok persist eder** (`player_profile.json`), canlar etmez.
- **Gün tamamlama grantı YOKTUR** (D-115'te kaldırıldı). Tek kaynaklar: authored `startingCharges` ve Gem satın alma.

### 6.4 Simülatör v1 bunları güvenle yok sayabilir mi?

**EVET — üç koşulla:**

1. **Gün 0–4 simülasyonunda hiçbir powerup zaten kullanılamaz** (`IsUnlockedOnDay` hepsini kilitliyor). Bu günler powerup'sız simüle edilir ve sonuç tam doğrudur.
2. **Gün 5+ için powerup'sız simülasyon bir ALT SINIR verir** — gerçek oyuncu daha iyisini yapabilir. Zorluk taraması için doğru taraf: powerup'sız çözülemeyen bir gün gerçekten zordur.
3. **Ama gelir tahmini yanlış olur.** Auto-Collect teslimat sayısını ve hızını artırır, dolayısıyla bahşiş kademesini yukarı çeker.

**Öneri:** v1'de powerup'ları kapat, ama simülatörü `powerupsEnabled: false` bayrağıyla yaz ki sonradan açılabilsin. En değerli ikinci adım Noise Clear'ı modellemek olur — tahta doygunluğu bu projenin ana zorluk ekseni (§5.5, runtime spec) ve tek panzehiri o.

> **NEEDS DECISION:** Powerup kullanım politikası (P6) tamamen modellenecekse, "ne zaman basılır" sorusunun cevabı projede yok. Basit bir eşik (örn. "tahta %80 doluyken Noise Clear") uydurmak gerekir.

---

## §7 · Tutorial

### 7.1 Hangi günlerde tutorial var

22 günün **tamamı tarandı** (`runtime.tutorial.enabled`):

| Gün | tutorial.enabled | adım sayısı |
|---|---|---|
| **day_00** | **true** | **2** |
| day_01 … day_21 | false | 0 |

**Yalnız `day_00`.** Diğer 21 gün tamamen normaldir.

### 7.2 TutorialDirector'ın uyguladığı kısıtlar

`Systems/Tutorial/TutorialDirector.cs` — armed bir adım varken:

| Kısıt | Metot | Etkisi |
|---|---|---|
| Tek hücre alınabilir | `IsPickupAllowed(x, y)` | Adımın `SourceX/SourceY` hücresi dışında hiçbir item alınamaz |
| Tek tepsi kabul eder | `IsTrayDropAllowed(slotIndex)` | Adımın `TargetTraySlotIndex`'i dışında hiçbir tepsi kabul etmez |
| Board yeniden düzenleme kapalı | `IsBoardRelocationAllowed()` | `!IsArmed` — item'lar tahta içinde taşınamaz |
| Powerup'lar kilitli | `IsPowerupUseAllowed(powerup)` | Öğretilen powerup hariç hepsi reddedilir |
| **Saat durur** | `GameManager.Update` → `if (Tutorial.IsArmed) return;` | **`TicketSlotManager.Tick` hiç çalışmaz** |

### 7.3 Karar: day_00 balancing simülasyonundan ÇIKARILMALI

Gerekçe — dördü de bağımsız:

1. **Zaman akmıyor.** Tutorial armed'ken `Tick` çalışmadığı için biletler hiç sayaç düşürmez. Bir simülatörün ölçtüğü her zaman-tabanlı metrik (timeout oranı, bahşiş kademesi, yıldız puanı) day_00'da anlamsızdır.
2. **Hamle uzayı yapay olarak 1'e iniyor.** Oyuncu politikası (§5) devre dışı — adım hangi hücreyi ve hangi tepsiyi söylüyorsa o yapılır. Simüle edilecek bir karar yok.
3. **Açılış dağıtımı zaten bastırılmış.** day_00'ın 3 start-board entry'si var, yani `openingAssignmentsWithoutDistribution = 3` (runtime spec §7). Board dağıtımı ilk çözüme kadar hiç çalışmaz.
4. **N = 5**, projedeki en kısa gün. İstatistiksel olarak zaten zayıf bir örnek.

**Öneri:** simülatör `dayIndex == 0`'ı varsayılan olarak atlasın ve bunu raporunda açıkça belirtsin. Zorlanırsa çalıştırılabilsin ama sonuç "tutorial day — timing metrics invalid" diye işaretlensin.

> **UNKNOWN:** `TutorialDirector`'ın adımlar arası geçiş mantığı (`NotifyTrayAccepted`, `NotifyTicketPatienceRatio`, `Abort`) bu döküman için detaylı incelenmedi. day_00 simüle edilecekse gerekir.

---

## §8 · Web export şeması — `expo-project-data.json`

### 8.1 Sözleşme

- **Salt okunur.** Day Editor bu dosyayı **asla yazmaz**. Unity'den export edilen referans veridir.
- Web aracı bunu başlangıçta bir kez yükler; `day_XX.json` dosyaları ayrı ve yazılabilirdir.
- `schemaVersion` her kırıcı değişiklikte artar.

### 8.2 Şema

```jsonc
{
  "schemaVersion": 1,
  "exportedAtUtc": "2026-08-30T12:00:00Z",
  "unityProject": "ExpoTheExplorer",

  "gameConfig": {
    "boardWidth": 5,
    "boardHeight": 4,
    "ticketSlotCount": 3,
    "defaultStartingLives": 3,
    "startingSoftMoney": 1000,
    "gemsPerStar": 1,
    "continueGemCost": 10
  },

  "economyConfig": {
    "warningRatio": 0.666,
    "criticalRatio": 0.333,
    "tipRateFull": 0.2,
    "tipRateWarning": 0.1,
    "tipRateCritical": 0.05
  },

  "starScoreConfig": {
    "threeStarScore": 0.55,
    "twoStarScore": 0.3,
    "wrongDeliveryPenalty": 0.2,
    "timeoutPenalty": 0.05
  },

  "ticketGenerationSeed": {
    "impatientTimeLimitSeconds": 45,
    "normalTimeLimitSeconds": 90,
    "patientTimeLimitSeconds": 150,
    "sideInclusionChance": 0.9,
    "drinkInclusionChance": 0.9,
    "modificationCountLambda": 1.69,
    "modificationAdditionChance": 0.5,
    "upcomingQueueSize": 10,
    "mainDishWeights": [
      { "foodItemId": "burger", "weight": 1.0,
        "modificationCountLambda": 2.0, "maxModificationCount": 10 }
    ]
  },

  "foods": [
    {
      "id": "burger",
      "displayName": "Burger",
      "category": "Main",
      "basePrice": 14,
      "availableModifications": ["no_lettuce","extra_cheese","no_tomato","extra_patty"],
      "spritePath": "Art/Food/Burgers Ingredients/StandarBurger.png",
      "overallScale": 0.8,
      "hasSpriteLayers": true
    }
  ],

  "modifications": [
    {
      "id": "no_lettuce",
      "displayName": "No Lettuce",
      "allowedDirection": "RemovalOnly",
      "iconPath": "Art/Food/Burgers Ingredients/Lettuce.png",
      "offeredBy": ["burger"]
    }
  ],

  "powerups": [
    {
      "type": "AutoCollect",
      "ordinal": 0,
      "displayName": "Auto-Collect",
      "description": "Fills your trays with what the current orders still need.",
      "startingCharges": 2,
      "gemCost": 20,
      "tutorialIntroDayIndex": 5,
      "tutorialUseTrigger": "AtDayStart",
      "tutorialCharges": 2,
      "affectsSimulation": true
    }
  ],

  "constants": {
    "mainDishWeightDefaultWeight": 1.0,
    "mainDishWeightDefaultModificationCountLambda": 1.0,
    "mainDishWeightDefaultMaxModificationCount": 10,
    "customerNameCount": 313
  }
}
```

### 8.3 Alan → Unity eşlemesi

| JSON yolu | Unity kaynağı | Not |
|---|---|---|
| `schemaVersion` | Exporter sabiti | Kırıcı değişiklikte artır |
| `gameConfig.boardWidth` | `GameConfig.asset → boardWidth` | `GameConfig.BoardWidth` |
| `gameConfig.boardHeight` | `GameConfig.asset → boardHeight` | `GameConfig.BoardHeight` |
| `gameConfig.ticketSlotCount` | `Core/GameState.cs → const TicketSlotCount` | **Config'te değil.** `GameState.TicketSlotCount` sabitinden oku |
| `gameConfig.defaultStartingLives` | `Core/GameState.cs → const DefaultStartingLives` | Aynı şekilde sabitten |
| `gameConfig.startingSoftMoney` | `GameConfig.asset → startingSoftMoney` | |
| `gameConfig.gemsPerStar` | `GameConfig.asset → gemsPerStar` | |
| `gameConfig.continueGemCost` | `LivesConfig.asset → continueGemCost` | Farklı asset |
| `economyConfig.*` | `EconomyConfig.asset` (5 alan) | Public getter'lardan oku |
| `starScoreConfig.*` | `StarScoreConfig.asset` (4 alan) | |
| `ticketGenerationSeed.*` | `TicketGenerationConfig.asset` | **Yalnız yeni gün tohumu.** Runtime bunu okumaz — oynanan gün kendi `runtime.ticketRuntime`'ını kullanır |
| `ticketGenerationSeed.mainDishWeights[].foodItemId` | `MainDishWeight.food.Id` | Asset'te obje referansı, export'ta id |
| `foods[]` | `FoodCatalog.asset → items` | **SIRA KORUNMALI** |
| `foods[].id` | `FoodItemConfig.Id` | |
| `foods[].category` | `FoodItemConfig.Category` | Enum **adı** yazılsın, ordinal değil |
| `foods[].basePrice` | `FoodItemConfig.BasePrice` | |
| `foods[].availableModifications` | `FoodItemConfig.AvailableModifications[].Id` | Referans → id |
| `foods[].spritePath` | `FoodItemConfig.Sprite` → `AssetDatabase.GetAssetPath` | `Assets/` öneki atılmış |
| `foods[].overallScale` | `FoodItemConfig.OverallScale` | Kozmetik |
| `foods[].hasSpriteLayers` | `FoodItemConfig.SpriteLayers.Count > 0` | Katman verisi export edilmez, varlığı bildirilir |
| `modifications[]` | `FoodCatalog`'daki yemeklerden **türetilir** | Klasör taraması **değil** — §1.2 |
| `modifications[].allowedDirection` | `ModificationConfig.AllowedDirection` | Enum adı |
| `modifications[].iconPath` | `ModificationConfig.Icon` → asset path | |
| `modifications[].offeredBy` | Ters indeks: hangi yemekler bunu listeliyor | Exporter hesaplar |
| `powerups[]` | `PowerupConfig.asset` → 3 `PowerupSettings` | `PowerupTypes.All` sırasında |
| `powerups[].type` / `.ordinal` | `PowerupType` adı ve `(int)` değeri | Ordinal load-bearing |
| `powerups[].tutorialUseTrigger` | `PowerupTutorialTrigger` | Enum adı |
| `constants.*` | `MainDishWeight` public const'ları | Fallback kuralları için gerekli |
| `constants.customerNameCount` | `Assets/Database/names.json` uzunluğu | İsimler export edilmez; simülasyon için sayı yeterli |

### 8.4 Kasten export EDİLMEYENLER

| Ne | Neden |
|---|---|
| `spriteLayers[]` detayı | Yalnız render. 7 katmanlı burger'ı web'de yeniden kurmak v1 için gereksiz iş |
| `customerPortraits[]` (28 sprite) | Bilet yüzü tamamen kozmetik; simülasyona hiç girmez |
| `names.json` içeriği (313 isim) | Yalnız kozmetik. Sayısı yeterli |
| `TicketCardVisualsConfig` | Tamamı kozmetik (renkler, `TimerSegmentSeconds`) |
| `BoardVisualsConfig`, `BoardAnimationConfig`, `DragFeelConfig`, `HapticConfig` | Tamamı görsel/his |
| `KeyConfig`, `MetaCatalog` | Gün içi simülasyonu etkilemez (anahtar = güne girme kapısı, meta = dekorasyon) |
| `TutorialTextConfig` | Yalnız metin |

> **NEEDS DECISION:** `KeyConfig` (maxKeys 5, regenMinutes 30, refillGemCost 40) uzun vadeli ekonomi/retention simülasyonu yapılacaksa gerekir. Gün içi balans için gereksiz. v1'de dışarıda bırakıldı.

---

## §9 · Export stratejisi

**Henüz implemente edilmedi — bu bir öneridir.**

### 9.1 En küçük uygulanabilir exporter

Tek dosya: `Assets/Editor/ProjectDataExporter.cs` — mevcut `Assets/Editor/` klasörüne, `ExpoTheExplorer.Editor` asmdef'i altına.

| Konu | Öneri |
|---|---|
| **Menü komutu** | `ExpoTheExplorer ▸ Export Project Data` — `Day Editor` ve `Play From Day` ile aynı menü kökü |
| **Kaynak asset'ler** | `AssetDatabase.FindAssets("t:FoodCatalog")`, `t:GameConfig`, `t:EconomyConfig`, `t:StarScoreConfig`, `t:LivesConfig`, `t:TicketGenerationConfig`, `t:PowerupConfig` — `DayEditorWindow.AutoDiscoverConfigs` ile **birebir aynı** desen |
| **Çıktı yolu** | `Assets/Resources/ProjectData/expo-project-data.json` |
| **Serialization** | **`JsonUtility` DEĞİL** — bkz. 9.3 |
| **Boyut** | ~200 satır. Katman/portre export'u olmadığı için düz bir dönüşüm |

### 9.2 Çıktı yolu gerekçesi

`Assets/Resources/ProjectData/` önerilir çünkü:

- `Resources/` altında olduğu için istenirse **oyun da okuyabilir** (bir doğrulama testi bunu diske yazılanla karşılaştırabilir).
- `Assets/` altında olduğu için **git'e girer** ve web aracıyla senkron kalır.
- `Resources/Days/` ile simetrik — aynı desen, aynı yer.

> **NEEDS DECISION:** Alternatif, proje kökünde `docs/` veya `export/` olurdu — Unity import etmez, daha temiz. Ama o zaman oyun tarafı asla doğrulayamaz. Karar senin.

### 9.3 Serialization — JsonUtility kullanılamaz

**Bu önemli bir kısıt.** `UnityEngine.JsonUtility`:

- `Dictionary` serialize edemez
- `null` referansları ayırt edemez
- Enum'ları **ordinal** olarak yazar (şema enum *adı* istiyor)
- İç içe polimorfik yapıları desteklemez

Seçenekler:

| Yaklaşım | Değerlendirme |
|---|---|
| **Elle `StringBuilder`** | Bağımlılık yok, tam kontrol. ~60 satır fazladan kod. **v1 için önerilen.** |
| `Newtonsoft.Json` (`com.unity.nuget.newtonsoft-json`) | Temiz, ama yeni bir paket bağımlılığı. Proje şu an hiç kullanmıyor |
| `JsonUtility` + düz DTO'lar | Enum'ları elle string'e çevirip, dictionary yerine dizi kullanırsan işe yarar. Şema biraz çirkinleşir |

Proje `TicketGenerationConfig.ParseNamesDatabase`'de zaten `JsonUtility`'nin sınırlarını sarmalama numarasıyla aşıyor (kök diziyi tek alanlı objeye sarma) — yani ek paket almama eğilimi belirgin. **Elle yazma öner.**

### 9.4 Sprite'lar export edilmeli mi?

**JSON'a gömme. Yol referansı yeter.** İki seçenek:

| Seçenek | Ne zaman |
|---|---|
| **Yalnız yol** (`spritePath`) | Web aracı Unity projesinin yanında çalışıyorsa (yerel dev sunucusu `Assets/`'i serve ediyor). En basit, v1 için bu |
| **PNG kopyası** ayrı bir `--with-sprites` adımıyla | Web aracı bağımsız deploy edilecekse. 21 yemek + 7 ikon = 28 PNG. `File.Copy` ile `export/sprites/` altına |

**Base64 gömme ÖNERİLMEZ** — JSON'u megabaytlara çıkarır ve her export'ta diff'i patlatır.

Katmanlı render (burger'ın 7 katmanı, hotdog'un 4'ü) web'de yeniden kurulacaksa `spriteLayers`'ın tamamı — offset, pushAmount, scale, visibility, modification referansı — export edilmeli. **v1 için önerilmez:** Day Editor'ün ihtiyacı olan tek şey bir yemeği tanımaya yetecek küçük bir ikondur, o da `sprite`.

### 9.5 Ne zaman yeniden çalıştırılmalı

Exporter'ın **otomatik tetiklenmesi önerilmez** (asset postprocessor gürültü yaratır). Elle çalıştırılacak durumlar:

- Bir `Food_*.asset` veya `Mod_*.asset` eklendi/silindi/düzenlendi
- `FoodCatalog`'un sırası değişti
- `GameConfig`, `EconomyConfig`, `StarScoreConfig`, `PowerupConfig`, `LivesConfig` düzenlendi
- `GameState.TicketSlotCount` veya `DefaultStartingLives` sabitleri değişti

> **Öneri:** JSON'a `exportedAtUtc` yaz ve web aracı bunu göster. Bayat referans veri, sessizce yanlış simülasyondan iyidir.

---

## §10 · UNKNOWN / NEEDS DECISION özeti

| # | Konu | Tip | Bölüm |
|---|---|---|---|
| X1 | **Board 5×4 mü olmalı?** Asset 5×4, C# default 6×5, CLAUDE.md "6x5 = 30 cells". Kod parametrik olduğu için oyun doğru çalışıyor, ama üç kaynak üç şey söylüyor. | NEEDS DECISION | §0, §3 |
| X2 | **`tipRateWarning`/`tipRateCritical` asset'te düşürülmüş** (0.1 / 0.05 vs 0.15 / 0.1). Kasıtlı balans mı, yarım kalmış deneme mi? | UNKNOWN | §0, §4 |
| X3 | **Yemek id adlandırma kuralı yok** (`extra_cheese` vs `extraketchup`). Yeni içerik hangi kuralı izleyecek? | UNKNOWN | §1.4 |
| X4 | **Powerup kullanım politikası** projede yok. Modellenecekse "ne zaman basılır" uydurulmalı. | NEEDS DECISION | §5.2, §6.4 |
| X5 | **Oyuncu hamle hızı** hiçbir yerde tanımlı değil. Gerçek oyundan ölçülmeli. | NEEDS DECISION | §5.2 |
| X6 | **`TicketSlotCount` ve `DefaultStartingLives` config'te değil, `const`.** Exporter bunları sabitten okumalı; literal yazarsa kod değişince sessizce bayatlar. | NEEDS DECISION | §3.3, §8.3 |
| X7 | **Export çıktı yolu:** `Assets/Resources/ProjectData/` (oyun da okuyabilir) mi, proje kökü (Unity import etmez) mi? | NEEDS DECISION | §9.2 |
| X8 | **`TutorialDirector` adım geçiş mantığı** detaylı incelenmedi. day_00 simüle edilecekse gerekir. | UNKNOWN | §7.3 |
| X9 | **`KeyConfig`** export dışı bırakıldı. Retention/ekonomi simülasyonu yapılacaksa gerekir. | NEEDS DECISION | §8.4 |
| X10 | **`spriteLayers` export'u** v1'de atlandı. Web editörü katmanlı önizleme isterse geri gelmeli. | NEEDS DECISION | §9.4 |

### Doğrulanmış sayılabilecekler

Aşağıdakiler **gerçek `.asset` YAML'larından okundu**, C# default'undan değil: `GameConfig` (4 alan), `EconomyConfig` (5 alan), `StarScoreConfig` (4 alan), `LivesConfig` (1 alan), `KeyConfig` (3 alan), `PowerupConfig` (3 × 7 alan), `TicketGenerationConfig` (8 alan + 1 ağırlık satırı), `FoodCatalog` (21 referans, sırasıyla), 21 `FoodItemConfig` (id/displayName/category/basePrice/modifikasyonlar/overallScale), 7 `ModificationConfig` (id/displayName/allowedDirection/icon).

Koddan doğrudan doğrulandı: modifikasyonların matching dışında gameplay etkisi olmadığı (§2.3), oyuncu politikasının var olmadığı (§5), `PlanAutoCollect` algoritması (§5.1), powerup etkileri ve kapıları (§6), tutorial kısıtları (§7.2), 22 günün tutorial durumu (§7.1).

---

*Expo the Explorer · External Tool Project Data Specification · 2026-08-30*
*İlgili: `day-editor-el-kitabi.md` (UI kılavuzu) · `day-runtime-spec.md` (algoritma sözleşmesi)*
