# Day (Bölüm) Sistemi — Uygulama Roadmap'i

Bu doküman, sınırsız/sonsuz devam eden bilet üretimini **elle tasarlanmış, art arda gelen Day'ler** haline getirmek ve bu Day'leri düzenlemek için bir **Level Editor** (Unity Editor tooling) inşa etmek amacıyla PR-PR planı tanımlar. **Bu dosya sadece planlama amaçlıdır — henüz hiçbir koda dokunulmamıştır.**

Bu roadmap, 2026-08-07 tarihli kod tabanı incelemesine dayanıyor (bkz. "İnceleme Bulguları" bölümü). CLAUDE.md Section 3/4/5 ile GDD ile çelişen hiçbir karar burada alınmadı; açık kalan noktalar ayrıca işaretlendi.

## Terminoloji Kararı

- **"Day"** kelimesi bu sistemde kullanılacak — "chapter/level/stage" değil. Gerekçe: kod tabanında zaten `DayLifecycleManager`, `DayCompleted`, `DayRetried`, `TicketsRequiredPerDay` gibi bir "gün" kavramı var; bu roadmap onu **tekil/sonsuz bir gün**den **sıralı, elle tasarlanmış Day'ler dizisi**ne genişletiyor. Yeni bir kelime icat etmek yerine mevcut kavramı büyütüyoruz.
- **"Level" kelimesi bu sistem için KULLANILMAYACAK.** Kod tabanında `LevelManager`, `LevelProgressionConfig`, `LevelView`, `state.Level`, `LevelUp` event'i zaten oyuncu XP-seviyesi anlamında var (bkz. `docs/LevelSystem_Roadmap.md`). Bu iki sistem birbirinden tamamen bağımsız — "Level Editor" ifadesi sadece bu doküman dışında günlük konuşma/UI ismi olarak kullanılabilir (örn. Unity menüsünde "Day Editor" demek daha isabetli olur), yeni sınıf/alan isimlerinde "Level" geçmeyecek.

## İnceleme Bulguları (özet)

- `TicketSlotManager.DeliverTicket`/`CancelTicket`, boşalan her slotu koşulsuz olarak `AssignTicket` ile yeniden dolduruyor — bu döngüyü durduran hiçbir kod yok. Tek durma koşulu can bitmesi (`IsAwaitingContinue`).
- `DayLifecycleManager.RecordDelivery()`, `TicketsDeliveredToday == GameConfig.TicketsRequiredPerDay` olduğunda `DayCompleted` event'i atıyor ama **üretimi durdurmuyor** — teslimatlar hedefin üstünde saymaya devam ediyor (bunu doğrulayan bir EditMode testi var).
- Bilet içeriği tamamen prosedürel (`TicketFactory`) — elle yazılmış bilet yok. `TicketSlotManager`'ın `nextTicketProvider` delegate'i (constructor injection) zaten var ve `GameManager.CreateNextTicket()`'a bağlı — bu, alternatif bir ticket-source enjekte etmek için doğal bir uzatma noktası.
- Tüm balancing config'leri (`GameConfig`, `TicketGenerationConfig`, `BoardDistributionConfig`, `EconomyConfig`, `LivesConfig`) tekil, global, `GameManager`'a bir kere bağlanmış asset'ler — Day başına farklı config seçimi yok.
- Zorluk düşürme (retry sonrası) kasıtlı olarak boş bırakılmış (`GameManager.RetryDay()` içinde açık yorum var) — CLAUDE.md Section 4 açık soru.
- `Assets/Editor/` altında sadece `[CustomEditor]` ile tek asset'in Inspector'ını genişleten scriptler var (`BoardDistributionConfigEditor`, `TicketGenerationConfigEditor`, `EconomyConfigEditor`, `FoodItemConfigEditor`). Day dizisini düzenleyecek bağımsız bir `EditorWindow` hiç yok.

## Kapsam Dışı (bilinçli olarak hariç tutuldu)

- **Powerup System** — zaten deferred (CLAUDE.md Section 3). Bu roadmap'te powerup/Day ilişkisine dair hiçbir PR yok.
- **Endless Mode** — GDD'den kaldırıldı, geri gelmiyor. Bu roadmap onu geri getirmiyor; tam tersine, mevcut "pratikte sonsuz davranan tek gün" durumunu kapatıp Daily Goal Mode'u **Day dizisi** olarak yapılandırıyor.
- **Oyuncu XP/Level sistemi** (`docs/LevelSystem_Roadmap.md`) — ayrı sistem, bu roadmap'te değiştirilmiyor. Yalnızca `DayCompleted`/`DayRetried` event kaynağı değişebileceği için (bkz. PR-3) `LevelManager`'ın bu event'lere subscribe oluşu bozulmayacak şekilde dikkat edilecek.
- **Zorluk düşürme sayılarının kesin tuning'i** — CLAUDE.md Açık Soru olarak kalıyor. Bu roadmap sadece zorluk parametresinin **nerede** authored olacağını (Day config'inin bir parçası mı, ayrı bir scaler mı) netleştirir; sayıları kilitlemez.

## Kilitli Kararlar (bu roadmap'in dayandığı)

1. Day terminolojisi kullanılacak, "Level" kullanılmayacak (yukarıda gerekçelendirildi).
2. Bilet üretimi Day başına **elle yapılandırılabilir** olacak — en azından config-override seviyesinde (guaranteed ticket count, noise lambda, tickets-required vb. Day'e özel), tam "authored ticket dizisi" seviyesi ise Açık Soru (aşağıda).
3. Bir Day tamamlandığında (hedef teslimat sayısına ulaşıldığında) bilet üretimi **gerçekten durmalı** — şu anki "hedefin üstünde saymaya devam et" davranışı kapatılacak.
4. Day dizisi sıralı ilerler: Day N tamamlanınca Day N+1'e geçilir. Retry, aynı Day'i (zorluk düşürülmüş haliyle) tekrar başlatır — Day ilerlemesini geri almaz, sadece o Day'in denemesini sıfırlar.
5. Mevcut mimari prensipler korunacak: `BoardDistribution`/`Day` mantığı MonoBehaviour'dan bağımsız, unit-testable plain C# olacak; config'ler ScriptableObject; UI reaktif kalacak.

## Açık Sorular — İlerlemeden Önce Netleştirilmeli

Bunlar CLAUDE.md Section 4'teki gibi: değer icat etmek yerine sorulacak.

- **Q1 — Manuel bilet yapılandırma granülaritesi:** Day editöründe bilet üretimini "elle ayarlamak" ne anlama geliyor?
  - (a) Sadece **config override** — Day başına `guaranteedTicketCount`, `noiseLeakCountLambda`, `ticketsRequiredForDay` gibi sayısal parametreler elle girilir, bilet içeriği hâlâ `TicketFactory` ile random üretilir.
  - (b) Tam **authored ticket dizisi** — Day editöründe "1. bilet: Burger + patates + kola, sabırsız müşteri" gibi somut bilet listesi elle yazılır, `TicketFactory` o Day için devre dışı kalır.
  - (c) İkisi bir arada — Day'in ilk N bileti authored, kalanı config-override ile random.
  - Bu, PR-4/PR-5'in mimarisini doğrudan belirliyor; aşağıdaki plan **(a)+(c) esnekliğine** izin verecek şekilde tasarlandı ama kesin karar kullanıcıdan bekliyor.
- **Q2 — Day sayısı ve sonrası:** Kaç Day authored olacak (örn. ilk sürüm için 10 Day)? Son authored Day'den sonra ne olur — oyun biter mi, son Day sonsuz döngüye mi girer, yoksa prosedürel "sonsuz Day üretici" bir fallback'e mi düşer?
- **Q3 — Zorluk düşürme nereye oturacak:** Retry'da düşen zorluk, aynı Day config'inin "retry variant"ı mı (Day editöründe elle authored) yoksa ayrı bir runtime `DifficultyScaler` çarpanı mı?
- **Q4 — Day ilerlemesi kalıcı mı:** Oyuncu hangi Day'de olduğu bilgisi (`CurrentDayIndex`) kalıcı profile mi yazılacak? (`docs/LevelSystem_Roadmap.md` PR-1'deki persistence katmanı bunun için doğal bir ev — ayrı bir save mekanizması icat etmemek gerek.)

---

## PR-1 — Day Veri Modeli (`DayConfig` + `DayCatalog`)

**Neden önce bu:** Davranış değişikliğine girmeden önce "bir Day nedir" veri olarak tanımlanmalı.

**Kapsam:**
- Yeni `DayConfig` ScriptableObject (`Assets/Data/DataScripts/DayConfig.cs`): Day'e özel alanlar — `ticketsRequiredForDay` (mevcut global `GameConfig.TicketsRequiredPerDay`'in Day-scoped karşılığı), Day'e özel `BoardDistributionConfig`/`TicketGenerationConfig` referansı (override; boş bırakılırsa global default kullanılır).
- Yeni `DayCatalog` ScriptableObject: sıralı `DayConfig[]` listesi — Day editörünün üzerinde çalışacağı ana asset.
- Bu PR'da **davranış değişmiyor** — sadece veri modeli ekleniyor, hiçbir sistem henüz bu config'i okumuyor.

**Bağımlılık:** Yok (paralel başlanabilir).

**Kabul kriteri:** `DayConfig`/`DayCatalog` asset'leri Unity Inspector'da oluşturulabiliyor; EditMode testinde boş/override alan davranışı doğrulanmış.

---

## PR-2 — Day İlerleme Çekirdeği (`DaySequenceManager`)

**Kapsam:**
- `DayLifecycleManager` genişletilir (ya da yanına `DaySequenceManager` eklenir — isimlendirme PR sırasında netleşecek): artık tek bir global hedef değil, `DayCatalog`'daki **aktif `DayConfig`**'ten `ticketsRequiredForDay` okuyor.
- `GameState`'e `CurrentDayIndex` alanı eklenir (custom setter → event publish, mevcut `Lives`/`SoftMoney` kalıbıyla aynı).
- `GameManager.Awake()`, `DayCatalog`'dan `CurrentDayIndex`'teki `DayConfig`'i seçip ilgili sistemlere (bkz. PR-5) iletir.

**Bağımlılık:** PR-1.

**Kabul kriteri:** Farklı `DayConfig.ticketsRequiredForDay` değerleriyle `DayCompleted`'in doğru sayıda teslimatta tetiklendiği unit testle doğrulanmış.

---

## PR-3 — Day Tamamlanınca Üretimi Gerçekten Durdur

**Neden ayrı PR:** Bu, tespit edilen en kritik davranış boşluğu — şu an `DayCompleted` sadece XP commit'i tetikliyor, üretimi durdurmuyor.

**Kapsam:**
- `TicketSlotManager`'a bir "durdurulmuş" durumu eklenir (mevcut `IsAwaitingContinue` desenine benzer — örn. `IsDayComplete`): `DayCompleted` event'i geldiğinde `AssignTicket` çağrıları duraklatılır, aktif slotlardaki sayaçlar (`Tick`) durur.
- `GameManager.OnDayCompleted` genişletilir: mevcut `LevelManager.CommitProgress()` çağrısına ek olarak, üretimi durdurma + "Day tamamlandı" sinyalini UI'ya iletme (yeni bir `DayCompletePopup` gerekebilir — bu UI PR-6/7'de netleşir, burada sadece state/sinyal seviyesinde durduruluyor).
- **Dikkat:** `LevelManager`'ın `DayCompleted`/`DayRetried` subscribe'ları bozulmamalı (Kapsam Dışı bölümünde not edildi) — event imzaları/publish noktaları değişmeyecek, sadece ek davranış eklenecek.

**Bağımlılık:** PR-2.

**Kabul kriteri:** Entegrasyon testinde, hedef teslimat sayısına ulaşıldıktan sonra `TicketSlotManager` yeni bilet atamıyor; `LevelManager` hâlâ doğru XP commit ediyor.

---

## PR-4 — Day Geçişi (Day N → Day N+1, Retry Davranışı)

**Kapsam:**
- PR-3'teki "durduruldu" durumundan çıkış: bir tetikleyici (UI buton ya da otomatik gecikme — tasarım kararı, Day editörü PR'ından bağımsız küçük bir açık soru) `CurrentDayIndex`'i artırır, `DayCatalog`'dan yeni `DayConfig`'i yükler, board/tray/ticket slot'ları PR-3'teki `RetryDay()`'e benzer şekilde sıfırlar (ama can/XP'yi sıfırlamadan — Day başarıyla bitti, ceza yok).
- `GameManager.RetryDay()` davranışı gözden geçirilir: retry, **aynı** `CurrentDayIndex`'i tekrar yükler (ilerlemeyi geri almaz), sadece o Day'in denemesini sıfırlar — Q3'teki zorluk düşürme kararına göre `DayConfig`'in "retry variant"ı ya da ayrı bir çarpan uygulanır.
- Q2 netleşmeden (son Day sonrası davranış), `DayCatalog` sınırını aşan `CurrentDayIndex` için en azından güvenli bir fallback (örn. son Day'i tekrar yükle) yazılır — placeholder, Q2 cevaplanınca değişecek.

**Bağımlılık:** PR-2, PR-3. Q2 ve Q3'ün en azından ilk versiyonu bu PR'dan önce cevaplanmalı.

**Kabul kriteri:** Day 1 tamamlanınca Day 2'nin config'i (farklı `ticketsRequiredForDay` ile) devreye giriyor; retry aynı Day'i tekrar yüklüyor, ilerleme geri gitmiyor.

---

## PR-5 — Day Başına Config Enjeksiyonu (`BoardDistributionConfig`/`TicketGenerationConfig` override)

**Kapsam:**
- `GameManager`, `BoardDistributor`/`TicketFactory`'yi artık sabit `[SerializeField]` asset yerine **aktif `DayConfig`'in override'ı (varsa) + global default (yoksa)** ile inşa eder/günceller.
- Day geçişinde (PR-4) bu enjeksiyon yeniden çalışır — Day 2'nin `BoardDistributionConfig` override'ı varsa devreye girer.
- Bu PR, Q1(a) — config-override seviyesindeki manuel ayarlanabilirliği tamamlar. `TicketGenerationConfig`/`BoardDistributionConfig`'in mevcut alanları (guaranteedTicketCount, noiseLeakCountLambda, mainDishWeights vb.) zaten var — burada yeni alan icat edilmiyor, sadece Day-scoped seçim ekleniyor.

**Bağımlılık:** PR-1, PR-2.

**Kabul kriteri:** İki farklı `DayConfig`, farklı `BoardDistributionConfig` override'larıyla test edildiğinde board'a farklı noise/guaranteed davranışı yansıyor.

---

## PR-6 — Authored Ticket Dizisi (Q1 = (b) veya (c) ise)

**Not:** Bu PR'ın kapsamı tamamen **Q1**'in cevabına bağlı. Q1=(a) ise bu PR gereksiz (PR-5 yeterli) ve roadmap'ten çıkarılır.

**Kapsam (Q1=(b)/(c) varsayımıyla):**
- Yeni bir ticket-source arayüzü: `IDayTicketProvider` (ya da mevcut `nextTicketProvider` delegate imzasına uyan bir sınıf) — `TicketFactory`'nin yanına, onu **sarmalayan** ya da **yerine geçen** bir sağlayıcı.
- `ScriptedTicketProvider`: `DayConfig`'teki elle yazılmış bilet listesini (main dish + side/drink + modifications + patience type, sırayla) okuyup `TicketSlotManager`'ın beklediği `Ticket` nesnelerini üretir.
- Q1=(c) ise: ilk N authored bilet tükendiğinde `RandomTicketProvider`'a (mevcut `TicketFactory`) otomatik geçiş.
- `DayConfig`'e authored bilet listesi alanı eklenir — Inspector'da düzenlenebilir ama bu PR henüz özel bir Day editörü UI'ı içermez (o PR-7'de).

**Bağımlılık:** PR-1, PR-5, Q1'in cevabı.

**Kabul kriteri:** Authored bilet listesi verilen bir Day'de, `TicketSlotManager`'a atanan biletlerin sırasının/içeriğinin tam olarak authored listeyle eşleştiği testle doğrulanmış.

---

## PR-7 — Day Editor (`EditorWindow`)

**Kapsam:**
- Yeni `Assets/Editor/DayEditorWindow.cs`: `DayCatalog`'daki Day'lerin listesini gösteren, sürükle-bırak sıralanabilen, her Day için yeni/kopyala/sil işlemleri sunan bir `EditorWindow` (mevcut `[CustomEditor]` desenlerinden farklı — tek asset değil, bir dizi düzenleniyor).
- Seçili Day için sağ panelde: `ticketsRequiredForDay`, config override referansları (PR-5), varsa authored bilet listesi (PR-6) düzenlenebilir.
- Mevcut `TicketGenerationConfigEditor`/`EconomyConfigEditor`'daki "preview panel" deseni takip edilir: seçili Day için (örn.) tahmini süre, guaranteed/noise item sayısı gibi bir önizleme gösterilir.
- `FoodItemConfigEditor`'daki runtime-mantığı-tekrar-kullanma prensibi (preview, gerçek `BoardItem.ResolvedLayers` mantığını kullanıyordu) burada da geçerli: Day önizlemesi, gerçek `BoardDistributor`/`TicketFactory` mantığını çağırmalı, kendi kopyasını çıkarmamalı.

**Bağımlılık:** PR-1 (minimum). PR-5/PR-6 tamamlanmışsa editör onları da düzenleyebilir; değilse editör sadece PR-1'deki temel alanları düzenler ve sonraki PR'larla genişler (yani bu PR paralel/iteratif ilerleyebilir).

**Kabul kriteri:** Bir tasarımcı, koda dokunmadan `DayCatalog`'a yeni bir Day ekleyip sırasını değiştirip parametrelerini ayarlayabiliyor.

---

## PR-8 — Day İlerlemesinin Kalıcılığı (Q4'e bağlı)

**Kapsam (Q4 = "evet, kalıcı olsun" ise):**
- `docs/LevelSystem_Roadmap.md` PR-1'deki `PlayerProfile`/`SaveService` katmanına `CurrentDayIndex` alanı eklenir (ayrı bir persistence mekanizması icat edilmez — mevcut katman genişletilir).
- Oyun açılışında `CurrentDayIndex` yüklenir, `DayCatalog`'dan ilgili `DayConfig` seçilir.

**Bağımlılık:** PR-2, PR-4, ve `docs/LevelSystem_Roadmap.md` PR-1 (persistence katmanı — o roadmap'te henüz uygulanmadıysa bu PR onu bloke eder, ayrıca koordine edilmeli).

**Kabul kriteri:** Oyunu kapat-aç → oyuncu son tamamladığı Day'in bir sonrasından başlıyor.

---

## Sıra Özeti

```
PR-1 (DayConfig/DayCatalog) ─┬─→ PR-2 (DaySequenceManager) ─→ PR-3 (Üretimi durdur) ─→ PR-4 (Day geçişi/retry)
                             │                                                              │
                             ├─→ PR-5 (Config override enjeksiyonu) ─→ PR-6 (Authored bilet, Q1'e bağlı)
                             │
                             └─→ PR-7 (Day Editor UI) — PR-1'den sonra başlayıp diğerleriyle paralel/iteratif genişler

PR-4 + docs/LevelSystem_Roadmap.md PR-1 ─→ PR-8 (Day ilerlemesi kalıcılığı, Q4'e bağlı)
```

PR-1 diğer her şeyi bloke ediyor. PR-2/3/4 sıralı (Day döngüsünün çekirdeği). PR-5/6 (config/bilet içeriği) PR-2'den sonra bağımsız ilerleyebilir. PR-7 (editör UI) PR-1'den sonra erken başlayıp diğer PR'larla birlikte genişletilebilir — tasarımcının elinde çalışan bir araç olması için önceliklendirilebilir.

## Bu Roadmap'e Başlamadan Önce Netleştirilmesi Gerekenler

- **Q1 — Manuel bilet yapılandırma granülaritesi** (config-override mi, authored dizi mi, ikisi mi) — PR-5 vs PR-6 kapsamını belirliyor.
- **Q2 — Day sayısı ve son Day sonrası davranış.**
- **Q3 — Zorluk düşürme nereye oturacak** (Day config'in retry variant'ı mı, ayrı scaler mı) — PR-4'ü etkiliyor.
- **Q4 — Day ilerlemesi kalıcı mı** — PR-8'in kapsamda olup olmadığını belirliyor.
