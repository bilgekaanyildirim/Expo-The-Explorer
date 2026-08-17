# Day-başına config planı

<!-- 2026-08-17. BoardDistributionConfig + TicketGenerationConfig ayarlarının
     global SO'lardan Day Editör'e ve Day JSON'a taşınması. economy-plan.md ile
     aynı format: her adım tek bir preflight + APPROVE döngüsü, adımlar sırayla,
     biten adımın kutusu işaretlenir. Bu dosya plan tutanağıdır, mimari otorite
     değil — kalıcı kararlar bittiğinde .claude/decisions.md'ye yazılır.
     D1/D2/D3 kullanıcı tarafından karara bağlandı (bkz. Kararlar). -->

## Amaç

Bugün iki balans config'i de tüm oyun için tek bir global ScriptableObject
asset'inden okunuyor. İstenen: bu ayarların **Day Editör içinde** düzenlenmesi
ve **bölümün JSON'una** yazılması, yani her günün kendi balansını taşıması.

---

## Kararlar (kilitli)

**K1 — Override değil, tam taşıma.** `has...Override` toggle'ları ve alan
adlarındaki `Override` eki tamamen kaldırılır. Her gün, ayarlarının
**eksiksiz** değerini taşır; "ezme" diye bir kavram yok. Global SO'lar yalnız
iki iş için kalır: (1) **yeni gün** açılırken başlangıç değeri kaynağı,
(2) `namesDatabase` gibi balans olmayan asset referanslarının sahibi.
Runtime artık SO'yu okumaz — günü okur (tek yazıcı invariant'ı).

**K2 — Generate ayarları `editorMeta`'da kalır, kayıt olarak.** Kullanıcı
gerekçesi: bir bölüme sonradan dönüldüğünde **hangi ayarlarla üretilmiş
olduğu görülebilsin** ve o ayarlarla düzeltme/yeniden üretim yapılabilsin.
Bu ayarlar oyunun çalışması için gerekli değil (üretilen sonuç zaten
`ticketSequence`'a pişiyor), o yüzden `editorMeta` doğru yer —
`DayCatalogParser`'ın orayı okumama sözleşmesi **korunur**.

**K3 — Her gün eksiksiz değer taşır.** Toggle olmadığı için "boş = varsayılan"
diye bir durum yok. Mevcut `day_00.json` / `day_01.json` bugünkü SO
değerleriyle doldurulur (davranış değişmez). Bloğu eksik bir gün dosyası
`DayCatalogParser`'ın bugünkü sert davranışıyla tutarlı şekilde hata verip
düşer — sessizce SO'ya düşmek, bugün ölü veriyi fark ettirmeyen davranışın
aynısı olurdu.

---

## Bugünkü durum (kod incelemesi)

İşin yarısı yapılmış, ama **yanlış bölmeye** yapılmış ve yarısı ölü veri.

| Ne | Durum |
|---|---|
| `DayEditorMetaJson`'da alanlar (3 ticket-gen + 8 board-dist) | **Var** — `DayJson.cs:42-54` |
| Day Editör'de UI'ları (Odin `ToggleGroup`) | **Var** — `DayEditorModel.cs:702-715` |
| JSON'a yazılıp geri okunması | **Var** — `day_00/01.json`'da mevcut |
| Her iki SO'da `CloneWithOverrides` + testleri | **Var** — asset'i bozmadan klonluyor |
| Ticket-gen alanlarının tüketilmesi | **Kısmen** — sadece `DayContentGenerator.cs:103` (Generate anı) |
| Board-dist alanlarının tüketilmesi | **YOK — ölü veri.** Hiçbir kod okumuyor |
| Runtime'ın günün config'ini görmesi | **YOK** |

Kök neden — **`editorMeta` sözleşmesi**: `DayJson.cs:10`'daki yorum ve
`DayCatalogParser.ResolveDay` (`:52`, yalnız `dayJson.runtime` okur)
`editorMeta`'yı runtime'ın **hiç okumadığını** garanti ediyor. Board
distribution ise tamamen runtime'da çalışan bir sistem (D-001'de canlı
bağlandı). Yani board-dist alanları, runtime'ın tanım gereği bakmadığı
bölmeye konmuş; yazılıyor, kaydediliyor, hiçbir şey yapmıyor.

Runtime bağlantıları (`GameManager`):

- `:94` `ticketFactory = new TicketFactory(ticketGenerationConfig)` — **oturum
  ömürlü**, güne göre hiç yenilenmiyor.
- `:317` `boardDistributor = new BoardDistributor(State, boardDistributionConfig)`
  — gün değişiminde **zaten yeniden kuruluyor**
  (`RefreshDayTicketSequenceProvider`), ama global asset ile. Bağlanacak yer hazır.
- `:157` `ticketGenerationConfig.UpcomingQueueSize` — lookahead derinliği.
- `:315` `new DayTicketSequenceProvider(..., ticketGenerationConfig, ticketFactory)`.

---

## Kritik ayrım: iki config simetrik değil

Ticket sequence **önceden üretilip** `runtime.ticketSequence`'a pişiyor.
Dolayısıyla:

| Alan grubu | Ne zaman etkili | JSON'da yeri |
|---|---|---|
| `sideInclusionChance`, `drinkInclusionChance`, `modificationCountLambda`, `modificationAdditionChance`, `mainDishWeights` | **Sadece Generate anında.** Runtime görmez | `editorMeta` — **kayıt olarak kalır** (K2) |
| Zaman limitleri (impatient/normal/patient), `upcomingQueueSize` | **Runtime** (`TimeLimitSecondsFor`, lookahead) | `runtime` — **taşınır** |
| Board distribution'ın 8 alanının tamamı | **Runtime** (her `OnOrderPlaced`) | `runtime` — **taşınır** |
| `namesDatabase` (TextAsset) | Runtime, ama asset referansı | JSON'a girmez, SO'da kalır |

"İki config'i de aynı şekilde taşı" yanlış olurdu: ticket generation'ın
olasılık knob'ları authoring kaydı, board distribution ise baştan sona
runtime verisi.

---

## Hedef şema

`Override` eki ve toggle'lar yok; her alan her günde dolu.

```jsonc
{
  "runtime": {
    "dayIndex": 0,
    "ticketsRequiredForDay": 10,
    "boardDistribution": {              // YENİ — runtime okur
      "noiseLeakCountLambda": 0.5,
      "guaranteedTicketCountMode": "Manual",
      "guaranteedTicketCount": 1,
      "guaranteedTicketCountLambda": 1.0,
      "earlyTicketWeightDecay": 0.5,
      "urgentTimeThresholdSeconds": 10.0,
      "leakDepth": 10,
      "maxLeakCount": 10
    },
    "ticketRuntime": {                  // YENİ — runtime okur
      "impatientTimeLimitSeconds": 45,
      "normalTimeLimitSeconds": 90,
      "patientTimeLimitSeconds": 150,
      "upcomingQueueSize": 10
    },
    "ticketSequence": [ /* ... */ ],
    "boardTimeline": [ /* ... */ ]
  },
  "editorMeta": {                       // runtime ASLA okumaz — authoring kaydı
    "allowedFoodItemIds": ["burger", "fries", "cola"],
    "ticketGeneration": {               // "bu gün hangi ayarla üretildi" kaydı
      "sideInclusionChance": 0.5,
      "drinkInclusionChance": 0.5,
      "modificationCountLambda": 1.0,
      "modificationAdditionChance": 0.5,
      "mainDishWeights": [
        { "foodItemId": "burger", "weight": 1.0, "modificationCountLambda": 1.0 }
      ]
    }
  }
}
```

---

## Adımlar

### Adım 1 — `runtime.boardDistribution` bloğu (veri yolu) ✅ 2026-08-17
- [x] `DayJson.cs`: `BoardDistributionJson` sınıfı + `DayRuntimeJson.boardDistribution`.
      Enum JSON'da **string** (`"Manual"`/`"Poisson"`), `patienceType` deseniyle aynı.
- [x] `DayDefinition`: `ResolvedBoardDistribution` (düz veri, SO değil). Ctor'a
      **opsiyonel son parametre** olarak eklendi — zorunlu olsaydı manifest dışı
      5 test suite'i derlenmez olurdu; runtime yolu (`DayCatalogParser`) her zaman
      dolduruyor.
- [x] `DayCatalogParser.ResolveDay`: bloğu okur, `Enum.TryParse` ile çözer,
      eksik/bozuk blokta günü düşürür.
- [x] `editorMeta`'daki 8 board-dist alanı + `hasBoardDistributionOverride` kaldırıldı.
- [x] Test: happy path + Poisson + eksik blok + bozuk enum + sıfırlanmış sayaçlar.
- [x] **Plandan sapma (onaylı):** `day_00.json`/`day_01.json` migrasyonu Adım 6'dan
      buraya alındı — K3 devreye girince migrate edilmemiş dosyalar düşer ve oyun
      `"No Day loaded"` ile patlardı. Adım 6'da yalnız `DayValidator` kuralları kaldı.
- [x] **Kullanıcı kararı:** migrasyon, asset'in bugünkü hâliyle değil **tasarım
      değerleriyle** yapıldı (`earlyTicketWeightDecay: 0.5`,
      `urgentTimeThresholdSeconds: 10`, `guaranteedTicketCountLambda: 1`).
      Gerekçe: `BoardDistributionConfig.asset` bu dört alanı hiç taşımıyor (alanlardan
      önce oluşturulmuş) ve `0` okunduğu için D-001'in acil-ticket garantisi ile
      loto rastgeleliği fiilen kapalıydı. **Adım 2 bağlandığında oynanış değişecek.**
- [x] **Kapsam genişlemesi:** parser'a JSON besleyen 3 test dosyası daha
      (`DayValidatorTests`, `DayContentGeneratorTests`,
      `DayTicketSlotManagerIntegrationTests`) fixture'larına blok eklemek üzere
      manifeste alındı, ikinci onay istendi.
- [x] **Yan düzeltme:** `DayEditorModel`'de yıldız eşiği alanları yoktu ve
      `ToDayJson()` her Save'de `DayRuntimeJson`'ı sıfırdan kurduğu için üç eşik
      sessizce sıfırlanıyordu. Alanlar + round-trip eklendi, round-trip testi
      artık her iki bloğu da bekçiliyor.
- [ ] **Açık:** EditMode testleri **çalıştırılamadı** — Unity Editor proje açık
      tutuyordu, batch mod kilide takılıyor. `dotnet build` ile tüm assembly'ler
      **0 hata** derlendi, ama bu testlerin geçtiği anlamına gelmez. Unity kapalıyken
      koşturulup bu kutu işaretlenecek.

### Adım 2 — Runtime bağlantısı (board distribution)
- [ ] `GameManager.RefreshDayTicketSequenceProvider`: `boardDistributionConfig`
      yerine günün bloğundan çözülen config ile `BoardDistributor` kurar
      (`CloneWithOverrides` mekanizması hazır, adı `Override`'dan arındırılır).
- [ ] **Klon ömrü:** `Instantiate` edilen SO gün değişiminde / retry'da
      `Destroy` edilmeli — `DayContentGenerator` bunu `finally` içinde
      `DestroyImmediate` ile yapıyor, runtime'da aynı disiplin gerekir.
- [ ] `[SerializeField] boardDistributionConfig` artık yalnız "yeni gün
      şablonu"; runtime okumaz (K1).
- [ ] Test: farklı `guaranteedTicketCount` taşıyan iki gün farklı davranıyor.

### Adım 3 — `runtime.ticketRuntime` bloğu + TicketFactory'nin güne bağlanması
- [ ] Blok + parser + `DayDefinition` alanı (Adım 1 deseni).
- [ ] `GameManager:94`'teki oturum ömürlü `ticketFactory`,
      `RefreshDayTicketSequenceProvider` içine taşınır — `DayTicketSequenceProvider`'a
      verilen config ile `TicketFactory`'nin config'i **aynı klon** olmalı, yoksa
      zaman limitleri baz asset'ten, geri kalanı klondan gelir.
- [ ] `:157` lookahead, günün `upcomingQueueSize`'ından okunur.
- [ ] Dikkat: `TicketFactory` her kurulumda yeni `Random` alıyor; gün başına
      yeniden kurulum RNG akışını sıfırlar — kabul edilebilirliği doğrulanır.

### Adım 4 — `editorMeta.ticketGeneration` (Generate ayarları, tam kapsam)
- [ ] Alanlar `Override` ekinden ve toggle'dan arındırılır, hepsi her günde dolu (K1).
- [ ] Bugün eksik olan `modificationAdditionChance` ve `mainDishWeights` eklenir —
      "hangi ayarla üretildi" kaydının tam olması için (K2).
- [ ] `mainDishWeights` JSON'da **food id** ile tutulur (`allowedFoodItemIds` ile
      aynı kural), `FoodCatalog` üzerinden çözülür.
- [ ] `DayContentGenerator`: artık koşulsuz olarak günün ayarlarını kullanır
      (`hasTicketGenerationOverride` kontrolü kalkar).

### Adım 5 — Day Editör UI
- [ ] Üç bölüm de **her zaman görünür** düz section olur (`ToggleGroup` kalkar):
      "Ticket Generation" (Generate kaydı), "Ticket Runtime", "Board Distribution".
- [ ] Yeni gün açılırken alanlar SO'lardan **seed** edilir — sıfır tuzağının
      çözümü (bkz. Riskler). Ayrıca "SO varsayılanlarından doldur" butonu.
- [ ] Toolbar'a `BoardDistributionConfig` alanı eklenir (bugün yok —
      `DayEditorWindow:97-111`'de 5 config var), `DayEditorModel.Configure`
      imzasına girer.
- [ ] `BoardDistributionConfigEditor`'ün leak-dağılım preview'ı ve
      `TicketGenerationConfigEditor`'ün main-dish yüzde preview'ı Day Editör
      içinden de çizilir; yoksa tuning körleşir.
- [ ] `DayEditorDayStartPreview` / ticket kartı preview'ı günün ayarlarını
      kullanır, global asset'i değil.

### Adım 6 — Validator + migrasyon
- [ ] `DayValidator`: `leakDepth > upcomingQueueSize` uyarısı; `mainDishWeights`
      içinde günün servis etmediği yemek varsa uyarı; `guaranteedTicketCount` ile
      `GameState.TicketSlotCount` tutarlılığı.
- [ ] `day_00.json` / `day_01.json` yeni bloklarla, bugünkü SO değerleriyle
      doldurulur → davranış değişmez (K3).

### Adım 7 — Test, harita, karar kaydı
- [ ] **`DayCatalogParserTests.cs:114-115`** — yalnız `editorMeta`'da farklı iki
      günün aynı parse edildiğini iddia ediyor. Bu test K2'nin sözleşmesini
      kodluyor, **korunur**; sadece board-dist alanları oradan çıktığı için
      güncellenir.
- [ ] `BoardDistributionConfigCloneWithOverridesTests`,
      `DayEditorModelConverterTests`, `DayContentGeneratorTests` güncellenir.
- [ ] `decisions.md`'ye yeni karar (D-004) + CLAUDE.md'nin "Board / Food
      Distribution" bölümüne per-day balans notu.
- [ ] `codemap-*` / `index.md` yenilenir.

---

## Riskler ve tuzaklar

1. **Sıfır tuzağı.** Alanlar mutlak değer tutuyor ve C# varsayılanı `0`.
   Toggle kalktığı için değerler artık **her zaman** uygulanacak; eksik/sıfır
   bir blok `earlyTicketWeightDecay = 0` (loto tamamen deterministik olur) ve
   `urgentTimeThresholdSeconds = 0` (acil-ticket garantisi kapanır) demek.
   `guaranteedTicketCount`/`leakDepth`/`maxLeakCount` savunmacı clamp'lerle
   kurtulur ama **sessizce**. Bu yüzden Adım 5'teki SO'dan seed etme ve Adım
   6'daki migrasyon opsiyonel değil, zorunlu.
   *Bugün canlı olan hâli:* "Ticket Generation Override" kutusu işaretlenirse
   Generate, sıfır olasılıklarla yan yemeksiz/içeceksiz/modifikasyonsuz ticket
   üretir. Board kutusu bugün zararsız, çünkü kimse okumuyor.
2. **Klon sızıntısı.** `CloneWithOverrides` → `Instantiate`. Her gün geçişi /
   retry bir SO instance'ı yaratır; `Destroy` edilmezse oturum boyunca birikir.
3. **İki yazıcı.** Alanlar hem SO'da hem günde okunur kalırsa CLAUDE.md
   invariant'ı ihlal olur. Adım 1 ve 2 eski alanları **silmeyi** içeriyor,
   paralel bırakmayı değil.
4. **Şema sürümü.** `DayJson`'da versiyon alanı yok. Save değil content olduğu
   için invariant'ı doğrudan ihlal etmiyor; K3 kararı (eksik blok = hata) bu
   boşluğu kapatır.
5. **Preview körlüğü.** Balans SO Inspector'ından çıkarsa oradaki iki preview
   da çıkar; Adım 5 telafi etmezse tuning gözle yapılamaz hâle gelir.

## Kapsam dışı (bilerek)

`GameConfig`, `EconomyConfig`, `LivesConfig`, `LevelProgressionConfig` ve
görsel config'ler bu planda yok. Board grid boyutu (CLAUDE.md Açık Sorular)
per-day yapılabilir bir aday ama bu planın parçası değil.
