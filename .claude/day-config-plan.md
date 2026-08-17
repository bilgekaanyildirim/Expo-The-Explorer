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

### Adım 2 — Runtime bağlantısı (board distribution) ✅ 2026-08-17
**Plandan sapma (onaylı):** klon yerine düz veri. Planın "SO'yu `Instantiate` et,
ez, `Destroy` et" yolu yerine `Data`'ya `BoardDistributionSettings` konuldu ve
`BoardDistributor` SO yerine onu alıyor. Böylece klon ömrü riski (aşağıdaki
Riskler #2) **tamamen ortadan kalktı**, SO runtime yolundan çıktı ve CLAUDE.md
Bölüm 5'in "Food Distribution Module Unity bağımlılığı taşımasın" maddesine
yaklaşıldı. `BoardDistributionConfig.CloneWithOverrides` ölü kod olduğu için
silindi (override katmanının artığıydı).

- [x] `BoardDistributionSettings` (Data): 8 değer + clamp'lerin **tek** uygulandığı yer.
- [x] `BoardDistributor`: ctor artık `BoardDistributionSettings` alıyor, SO'ya hiç
      dokunmuyor.
- [x] `DayDefinition`: `ResolvedBoardDistribution` kaldırıldı, yerine
      `BoardDistributionSettings` — aynı 8 sayıyı iki tipte tutmak `data-source.md`'nin
      yasakladığı çift kayıt olurdu.
- [x] `GameManager`: `CurrentDay.BoardDistribution` ile kuruyor;
      `[SerializeField] boardDistributionConfig` alanı **kaldırıldı**.
- [x] `BoardDistributionConfig`: `ToSettings()` eklendi, `CloneWithOverrides` silindi;
      artık yalnız Day Editör seed'i + Inspector preview'ı.
- [x] Test: `GuaranteedTicketCountAuthoredInDayJson_ChangesHowManyTicketsGetCovered`
      — gerçek JSON metninden gerçek parser'la geçip davranış farkını ölçüyor.
      Mevcut "serialized zero" regression testleri artık yeni clamp yerini bekçiliyor.
- [x] `BoardDistributionConfigCloneWithOverridesTests.cs` silindi (silinen kodu test ediyordu).
- [x] `decisions.md` D-004 yazıldı; `fingerprint.md`'nin "Data authorities" satırı
      board-distribution için dolduruldu (Adım 1 postflight'ının açık `N` maddesi kapandı).
- [ ] **Açık:** EditMode testleri yine **çalıştırılamadı** (Unity proje kilidi).
      `dotnet build` → **0 hata**. Playtest bekleniyor: bu adımla acil-ticket garantisi
      ve loto rastgeleliği fiilen açıldı, oynanış değişti.

### Adım 3 — `runtime.ticketRuntime` bloğu ✅ 2026-08-17
**Plandan sapma (onaylı):** `TicketFactory` **güne bağlanmadı, gerekmiyordu.** Play
time'da tek çağrılan metodu `PickRandomCustomerName()`; `Create` ve
`PickRandomPatienceType` yalnız authoring yolundan çağrılıyor. İsim havuzu güne
göre değişen bir balans olmadığı için oturum ömürlü kalması doğru — planın
uyardığı "aynı klon olmalı" tuzağı ve RNG-sıfırlama endişesi böylece konusuz kaldı.

- [x] `TicketRuntimeSettings` (Data): 3 süre limiti + `upcomingQueueSize`.
- [x] `TicketRuntimeSettingsExtensions` (Core): sabır→süre switch'inin **tek** yeri;
      `TicketGenerationConfigExtensions` silindi. Switch Core'da kaldı çünkü Data
      `Core.PatienceType`'ı referanslayamıyor (mevcut kısıt, `EconomyConfig` notu).
- [x] `DayJson.ticketRuntime` + parser (eksik blok / 0 süre / 0 kuyruk → gün düşer).
- [x] `TicketEntryFactory` + `DayTicketSequenceProvider` artık settings alıyor;
      `GameManager` lookahead'i `CurrentDay.TicketRuntime`'dan okuyor.
- [x] `TicketGenerationConfig` **silinmedi** (D-004'ün aksine): isim veritabanı ve
      üretim knob'larının sahibi olarak duruyor, `ToRuntimeSettings()` seed'i eklendi.
- [x] `day_00`/`day_01` asset'in birebir değerleriyle migrate edildi (45/90/150/10) —
      dört alan da asset'te fiziksel olarak vardı, Adım 1'deki belirsizlik yok.
      **Bu adım davranışı değiştirmiyor.**
- [x] `decisions.md` D-005.
- [ ] **Açık:** EditMode testleri çalıştırılamadı (Unity proje kilidi). `dotnet build`
      → 0 hata, ve yeni dosyaların csproj'lere gerçekten kayıtlı olduğu doğrulandı.

### Adım 4 — `editorMeta.ticketGeneration` (Generate ayarları, tam kapsam) ✅ 2026-08-17
- [x] Toggle ve `Override` ekleri kalktı; `TicketGenerationJson` bloğu geldi.
- [x] `modificationAdditionChance` + `mainDishWeights` eklendi — kayıt artık tam (K2).
- [x] `mainDishWeights` food id ile tutuluyor, `DayContentGenerator.ResolveMainDishWeights`
      çözüyor; kayıp id sessizce düşüyor (`ResolveFoodPool` ile aynı duruş).
      `MainDishWeight`'a public ctor eklendi.
- [x] `CloneWithOverrides` → `CloneForDayGeneration` (parametreler zorunlu).
      **Klon burada korundu**, D-004'ün aksine: `namesDatabase` TextAsset'i JSON'a
      giremiyor, ve bu yol yalnız authoring'de çalışıp klonu `finally`'de siliyor.
- [x] **Bilinçli asimetri:** eksik blok burada günü düşürmüyor (runtime bloklarının
      aksine) — `DayCatalogParser` `editorMeta`'yı zaten okumuyor, ve eski bir gün
      açılabilir kalmalı. Yedek: SO'nun gerçek değerleri, sıfır değil.
- [x] `day_00`/`day_01` asset'ten birebir migrate edildi (0.9 / 0.9 / 1.69 / 0.5 /
      burger[ağırlık 1, λ 2]). **Davranış değişmiyor.**
- [x] `decisions.md` D-006.
- [x] **Kapsam genişlemesi:** `TicketGenerationConfigCloneWithOverridesTests` silinen
      metodu test ediyordu, derlemede yakalandı, `...CloneForDayGenerationTests` olarak
      yeniden yazıldı. *Ders:* sembol yeniden adlandırırken locate taraması alan adları
      üzerinden değil, **sembolün kendi adı** üzerinden yapılmalı — bu oturumda aynı
      tipte ikinci kaçaktı (ilki Adım 3'te `BoardDistributionTests`).
- [ ] **Açık:** EditMode koşusu bekleniyor.

### Adım 5 — Day Editör UI (+ `BoardDistributionConfig`'in tasfiyesi)

**Karar (2026-08-17, kullanıcı):** SO silinecek, ama **bu adımda**, tek hamlede.
Adım 2 sonrası asset'in tek gerçek bağımlısı kendi Inspector önizlemesi kaldı
(`BoardDistributionConfigEditor`) — hiçbir şeyi çalıştırmayan bir asset'in
önizlemesi. Şimdi silinmedi çünkü kullanıcı tam da o önizlemenin gösterdiği iki
değeri (`noiseLeakCountLambda`, `maxLeakCount`) ayarlıyor; önizleme Day Editör'e
taşınmadan silmek onu körlerdi. Sıra: **önce önizlemeyi taşı → yeni-gün
varsayılanlarının kaynağına karar ver → sonra asset'i + custom editor'ü sil.**

Bu adımda kapatılacak iki artık:
- `BoardDistributionConfig.ToSettings()` — Adım 2'de eklendi, **çağıranı yok**.
  Ya seed yoluna bağlanır ya asset'le birlikte silinir.
- `SampleScene.unity` hâlâ asset'in GUID'ini tutuyor (Adım 2'de kaldırılan
  `GameManager.boardDistributionConfig` alanından kalma). Silmeden önce sahne bir
  kez kaydedilirse kopuk referans uyarısı çıkmaz.
- Yeni-gün varsayılanları şu an `DayEditorBoardDistribution` içinde koda gömülü
  (0.5 / 10 / 1 ...). CLAUDE.md'nin "sayılar koda gömülmez" invariant'ıyla
  gerilimde; asset silinecekse bu gerilimin nasıl çözüldüğü burada yazılmalı.


- [x] Üç bölüm de her zaman görünür düz section (`ToggleGroup` Adım 1-4'te zaten kalktı).
- [x] Leak dağılımı ve ana yemek yüzdesi önizlemeleri `DayEditorSettingsPreviews`'e
      çıkarıldı; Day Editör **günün kendi değerleriyle** çiziyor,
      `TicketGenerationConfigEditor` de aynı yardımcıyı çağırıyor (matematik tek yerde).
- [x] Yeni gün, en yüksek indeksli günün üç ayar bloğunu kopyalıyor (D-007).
      İçerik (ticket dizisi, Day Start, yıldız eşikleri, yemek seçimi) **kopyalanmıyor**.
      `Configure` kopyalamadan önce çağrılıyor — yoksa ana yemek id'leri katalogsuz
      çözülüp boş kalırdı.
- [x] `BoardDistributionConfig` silindi: sınıf + `.asset` + custom editor.
      `GuaranteedTicketCountMode` `BoardDistributionSettings.cs`'e taşındı (aynı
      namespace, kullanan 8 dosyada değişiklik yok). `ToSettings()` çağıranı olmadan
      sınıfla birlikte gitti.
- [x] Toolbar'a `BoardDistributionConfig` alanı **eklenmedi** — plan bunu istiyordu ama
      asset silindiği için konusuz kaldı.
- [x] Ticket kartı / Day Start önizlemeleri: bunlar per-Day ayar okumuyor
      (`TicketCardVisualsConfig`, `GameConfig` board boyutu, `BoardVisualsConfig` —
      hiçbiri bu planda taşınan ayar değil), madde gerekçesiyle kapatıldı.
- [ ] **Açık — kullanıcı adımı:** `SampleScene.unity` satır 3003'te
      `boardDistributionConfig:` kalıntısı duruyor (D-004'te kaldırılan alandan).
      Artık kopuk bir GUID; sahne bir kez açılıp kaydedilince düşer. Elle YAML
      düzenlenmedi (tek sahne, K1).
- [ ] **Açık:** EditMode koşusu + editörde gözle doğrulama bekleniyor.

### Adım 6 — Validator kuralları ✅ 2026-08-17
Migrasyon maddesi Adım 1/3/4'te yapıldığı için burada yalnız kurallar kaldı.

- [x] `DayValidationResult`'a **uyarı kanalı** eklendi. Gerekçe: bugünkü tek kanalda her
      hata Save'i kapatıyordu; eklenecek kontrollerin çoğu "çalışır ama muhtemelen
      istediğin bu değil" cinsinden. `IsValid` yalnız hatalara bakıyor.
- [x] **Hata** (Save kapanır) — üçü de `DayCatalogParser`'ın reddettiği durumların aynısı,
      yani editör artık runtime'ın açmayı reddedeceği bir dosya yazamıyor:
      süre limiti ≤ 0, `upcomingQueueSize` < 1, board sayaçlarından biri < 1.
- [x] **Uyarı** (Save açık): `leakDepth` > `upcomingQueueSize`; süre limitleri
      Impatient < Normal < Patient sırasında değil (GDD Bölüm 8); yıldız eşikleri artan
      değil ya da üçü de 0; `guaranteedTicketCount` slot sayısının üstünde (runtime kırpar).
- [x] Ana yemek ağırlığı günün servis etmediği yemeğe yazılmışsa uyarı — validator'da
      değil **önizlemede**, çünkü ağırlıklar `editorMeta`'da ve `DayDefinition` onları
      taşımıyor. Önizleme zaten satır satır uyarı basıyordu.
- [x] Day Editör uyarıları ayrı bir sarı kutuda gösteriyor (hata kutusuyla karışmasın).
- [x] Test: her kural için bir test + ayarsız `DayDefinition`'ın patlamadığı (authoring
      çağrıcıları blokları hiç doldurmuyor) + temiz günde hiç uyarı çıkmadığı.
- [ ] **Açık:** EditMode koşusu bekleniyor.

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
