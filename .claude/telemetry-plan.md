# telemetry-plan — Playtest Telemetry + Firebase

**Durum: rev 2 — kullanıcı revizyonu uygulandı, ikinci onay bekliyor. Hiçbir kod yazılmadı.**
rev 1: 2026-08-31 · rev 2: 2026-08-31 (kullanıcı geri bildirimi 1-20).
İnceleme tabanı: commit `8782f10`.

İlgili ve tekrar edilmeyen dökümanlar: `.claude/decisions.md` (D-012, D-064, D-092, D-095, D-103, D-135),
`ExpoTheExplorer/CLAUDE.md` (Locked Rules), `docs/unity-balancing-simulator-integration-spec.md`.

## rev 2 — neyin değiştiği

| # | Değişiklik |
|---|---|
| 1 | **`livesDepleted ? failed : quit` heuristic'i tamamen kaldırıldı.** Yerine kodun kendi terminal sinyali geldi — §E.3. Dokümanın hiçbir yerinde bu ifade kalmadı |
| 2 | `failed` artık yalnızca **canları tükenmiş halde sonlanan** bir attempt'e veriliyor; Continue almış bir oyuncunun sonradan menüye dönmesi `quit` |
| 3 | `livesDepleted: bool` → **`livesDepletedCount: int`**. `continuesUsed` tam olarak türetilebilir olduğu için v1'e alan eklenmedi — §F.4 |
| 4 | `LivesDepleted` üzerindeki **immediate Firestore write kaldırıldı** — §J |
| 5 | `deviceModel`, `unityVersion`, `platform` **optional diagnostics** oldu — §F.3 |
| 6 | Offline/uçak modu testi **scope dışı** — §L, §N Step 4 |
| 7 | `Reset Game + New Test Player` **day içinde çalışmıyor**, uyarı loglayıp çıkıyor — §D.3 |
| 8 | `discardCount` şemadan **tamamen çıkarıldı** — §F.3 |
| 9 | Balancing simulator'ın Firestore **read yetkisi ayrı bir pipeline** olarak §M.5'e ve §N Step 7'ye yazıldı |
| 10 | Firestore şeması **gruplandırılmış** olarak yeniden düzenlendi — §F.2 |
| 11 | §J tablosu yeniden yazıldı; §E'ye **7 senaryo doğrulaması** eklendi — §E.4 |
| 12 | Gameplay dokunuşu 1 satırdan **3 publish satırı + 2 property + 1 enum**'a çıktı. Gerekçesi §B.2'de |

---

## A. Current Architecture Findings

### A.1 Save sistemi — tek bir persistence sınırı var

| Katman | Dosya | Rol |
|---|---|---|
| Payload | `Assets/Scripts/Systems/ProgressionSystem/PlayerProfile.cs` | `[Serializable]`, sadece public alanlar (JsonUtility şartı) |
| Dosya sınırı | `Assets/Scripts/Systems/ProgressionSystem/PlayerProfileStore.cs` | `Load`/`Save`/`Delete`, `CurrentVersion = 10`, `UpgradeToCurrent`, `Normalize` |
| Besteci | `Assets/Scripts/Session/GameSession.cs:287-319` | `Save()` — dosyaya neyin gideceğine karar veren **tek yer** |
| Tetikleyici | `Assets/Scripts/Bootstrap/GameManager.cs:818` | `SaveProfile() => Session.Save();` |

- Yol: `persistentDataPath/player_profile.json`. Serileştirme `JsonUtility`. Newtonsoft yok.
- Payload: `Version`, `SoftMoney`, `Gems`, `CurrentDayIndex`, `Keys`, `LastKeyRegenUtcTicks`,
  üç powerup charge alanı, `OwnedMetaItemIds`, `LastCelebratedDayIndex`, `HapticsEnabled`. (`Lives` yok — D-064.)
- Yazma kadansı: **yalnızca gün-yaşamdöngüsü anlarında** (day completed, retry/abandon,
  completed-day retry, day advance) + satın almalar. **Quit'te yazma yok.**
- Okuma: `GameSession` constructor'ı (`:164`); iki sahne kökü de kendi `Awake`'inde bir `GameSession` kurar.

### A.2 PlayerPrefs — proje kodunda hiç kullanılmıyor

Tek eşleşme satıcı kodunda: `StompyRobot/SRDebugger/.../BugReportSheetController.cs:195,200,201`.
**Repoda `PlayerPrefs.DeleteAll` hiçbir yerde yok** — sorduğunuz geniş kapsamlı silme riski bu projede mevcut değil.

### A.3 SRDebugger ve `Delete Save File`

İki dosya, ikisi de `#if UNITY_EDITOR || DEVELOPMENT_BUILD`:
`Scripts/Debug/DebugMenuBinder.cs` (sahne köprüsü, `[SerializeField] SessionHost host`) ve
`Scripts/Debug/SROptions.Expo.cs` (`partial class SROptions` yarısı).
**`Scripts/Debug/` altında asmdef yok ve bu yük taşıyor** — bir partial'ın yarıları aynı assembly'de olmalı.

Mevcut option'lar: Wallet · Keys · Lives · Powerups · Day (`GoToDay`/`NextDay`/`RetryDay`/`DeliverTicket1-3`) ·
Meta · Save (`SaveNow`/`DeleteSaveFile`/`SaveFilePath`) · 9 salt-okur Info satırı.

`DeleteSaveFile()` (`SROptions.Expo.cs:335-355`) net olarak:

| Ne | Etkileniyor mu |
|---|---|
| `player_profile.json` | **tamamen silinir** (`File.Delete`) |
| PlayerPrefs | hayır |
| ScriptableObject / config | hayır |
| Bellekteki `GameSession`/`Wallet`/`KeyManager`/`PowerupManager` | doğrudan hayır — sahne reload'ına güvenir |
| Sahne | main screen'de reload; **day içinde reload yok**, sadece log |
| Başka kalıcı state | yok — projede ikinci bir persistence sınırı yok |

Main screen'deki "Start Over" düğmesi kaldırılmış (D-095) — oyuncuya açık reset UI'ı kalmamış.
`Assets/Editor/DayJumpWindow.cs` bir wipe değil; sadece `CurrentDayIndex` yazar.

### A.4 Day lifecycle

Açık bir state machine sınıfı yok. **Dört gün-başlangıcı yolu aynı reset dizisini çalıştırıyor:**

| Yol | Satır | Sahne | Yaydığı event |
|---|---|---|---|
| `Awake()` | 280 | ilk açılış | — |
| `RetryDay(bool givingUpOnAttempt)` | 1029 | yerinde reset | `DayRetried` (reset'ten **sonra**) |
| `RetryCompletedDay()` | 1070 | yerinde reset | **hiçbiri** |
| `AdvanceToNextDay()` | 1116 | yerinde reset | `CurrentDayIndexChanged` |

Ortak dizi: `RefreshDayTicketSequenceProvider()` → `State.Board.Clear()` → `ApplyDayStartBoardPreSeed()` →
`TrayManager.DiscardAllForNewDay()` → `TicketSlotManager.ResetSlotsForNewDay()` →
**`LivesManager.RefillForNewDay()`** → **`DayLifecycleManager.ResetForNewDay(...)`**.

- **Gameplay canlı olma anı:** `Awake()`'in son satırı `TicketSlotManager.FillEmptySlots()` (`:372`). Countdown/intro yok.
- **Success:** `TicketSlotManager.AssignTicket:151` — sekans bitti ve tüm slotlar boş → `DayCompleted`.
- **Life loss:** `LivesManager.LoseLife():45` — canlar 0 → `IsAwaitingContinue = true` + `LivesDepleted`.
- **Sahneler:** `MainScreen` (index 0) ve `SampleScene`, ikisi de Single. Persistent/boot sahnesi yok.
- **Elapsed time:** global gün saati yok; sadece bilet başına `RemainingSeconds`.
- **Attempt/retry sayacı:** projede hiç yok.
- **Application lifecycle:** `OnApplicationPause`/`Focus`/`Quit` proje kodunda hiç implement edilmemiş.

### A.5 Score, star, mistake, ticket, booster

| Kavram | Nerede | Telemetry kancası |
|---|---|---|
| Score | `DayLifecycleManager.Total` (order value + tip) | pull |
| Star | `DayLifecycleManager.StarScore` / `StarCount`, sayılar `StarScoreConfig.asset` | pull |
| Mistake | `enum DayFailureCause { WrongDelivery, Timeout }`, `RecordFailure(cause)` sayar | event yaymıyor; iki sebep ayrı ayrı gözlemlenebiliyor |
| Wrong delivery | `TrayManager.TryAddItem:75` batch check → `ScatterBackToBoard` | `TraySlotScatterBegin` |
| Ticket completed | `TicketSlotManager.DeliverTicket:87` | `TicketDelivered` |
| Ticket expired | `Tick:175` → `CancelTicket:100` | `TicketCancelled` |
| Booster | `PowerupManager.TryUse:155` | `ChargesChanged` — **azalma = kullanım** |
| Discard / çöpe atma | **Böyle bir oyuncu aksiyonu yok.** Tepsiden geri sürükleme cezasız ve event'siz | — kapsam dışı (rev 2) |

### A.6 Event bus ve manager mimarisi

```csharp
public class EventBus<T>
{
    private event Action<T> handlers;
    public void Subscribe(Action<T> handler)   => handlers += handler;
    public void Unsubscribe(Action<T> handler) => handlers -= handler;
    public void Publish(T payload)             => handlers?.Invoke(payload);
}
```

**Instance tabanlı, static değil, senkron.** Çoğu `GameState` üzerinde.
Abone olunabilir tam liste: `TicketAssigned`, `TicketDelivered`, `TicketCancelled`,
`TraySlotScatterBegin/End`, `LivesChanged`, `MaxLivesChanged`, `LivesDepleted`,
`SoftMoneyChanged`, `GemsChanged`, `CurrentDayIndexChanged`, `DayCompleted`, `DayRetried`;
ayrıca `PowerupManager.ChargesChanged`, `KeyManager.KeysChanged`, `BoardGrid.CellChanged`.

**Singleton yok, service locator yok, DontDestroyOnLoad yok, boot sahnesi yok.**
Runtime'da hiçbir şey sahnede arama yapmaz (D-013).

### A.7 Assembly definition yapısı

`Data` (yaprak) ← `Core` ← her şey. `Scripts/Bootstrap/`, `Scripts/UI/`, `Scripts/Debug/`
asmdef taşımaz → `Assembly-CSharp`. Hiçbir asmdef `GameManager`'ı referans edemez.

> **Firebase için kritik sonuç:** Firebase SDK'sı `Assets/Firebase/Plugins/` altına precompiled
> DLL'ler koyar. `Assembly-CSharp` bunları otomatik referanslar; **bir asmdef etmez** —
> `overrideReferences: true` + her DLL'i `precompiledReferences`'a elle yazmak gerekir.
> Bu yüzden Firebase'e dokunan kod `Assembly-CSharp`'ta kalmalı.

### A.8 Paketler ve Player Settings

| Konu | Durum | Firebase açısından |
|---|---|---|
| Unity | `6000.3.16f1` | Firebase'in test ettiğinden yeni → §O.1 |
| EDM4U / Google paketleri | yok | Çakışacak bir şey yok |
| Newtonsoft.Json | yok | Tüm JSON `JsonUtility` |
| `async`/`await`/`Task` | hiç kullanılmamış | Firebase, projenin **ilk async kodu** → §O.10 |
| Android identifier | **serialize edilmemiş** | Sadece `Standalone: com.DefaultCompany.2D-URP` → §G.0 |
| iOS bundle id | **serialize edilmemiş** | Aynı |
| `bundleVersion` | `1.0` | Her playtest build'inde artırılmalı → §O.4 |
| API compatibility | .NET Standard 2.1 | Erken doğrulanmalı → §O.2 |
| Android backend | IL2CPP, ARM64, minSdk 25 | Uyumlu |
| Mevcut networking/analytics | 0 satır | Sıfırdan entegrasyon |

Satıcı eklentileri: Odin 3.3.1.12, DOTween, NiceVibrations v3.3, SRDebugger + SRF.

### A.9 Day identifier

**İki farklı `int` var ve karıştırılmaları gerçek bir hata kaynağı** (D-091, D-092):

| | `DayDefinition.DayIndex` | `GameState.CurrentDayIndex` |
|---|---|---|
| Kaynak | Day JSON'un `runtime.dayIndex` alanı | Çözümlenmiş katalogdaki **pozisyon** |
| Kullanım | Sıralama + `IsUnlockedOnDay` | Save dosyası, main screen, Play |
| Stabil mi | Day dosyası yeniden numaralanmadıkça evet | Bir Day eklenir/silinir/parse edilemezse **kayar** |

**Karar (kullanıcı onayladı):** her iki değer de yazılacak. Balancing ve tarihsel karşılaştırmanın
**canonical content identity'si `dayContentIndex`** (Day JSON'un kendi numarası); `dayIndex`
oyuncunun progression/katalog pozisyonunu temsil ediyor.

---

## B. Proposed Architecture

Hedef: **Firebase'i 2 dosyaya hapsetmek, gameplay'e mümkün olan en küçük dokunuş.**

```
   GameState EventBus'ları        PowerupManager.ChargesChanged
   DayLifecycleManager (pull)                 │
              └───────────────┬───────────────┘
                              ▼
                    TelemetryBinder  (MonoBehaviour, SampleScene, Assembly-CSharp)
                    · [SerializeField] SessionHost host   ← insan sürükler (D-013)
                    · Update() → heartbeat sayacı
                    · OnDestroy / OnApplicationPause → flush
                              │
                              ▼
                    PlaytestTelemetry  (static facade, TelemetrySystem asmdef)
                    · RunTelemetryState (RAM)   · TelemetryIdentity
                              │
                              ▼
                    ITelemetrySink
                       ├── LogTelemetrySink       (Console — Firebase'siz test)
                       └── FirestoreTelemetrySink (Firebase'e dokunan TEK sınıf)
                                     │
                                     ▼
                              FirebaseBootstrap  (init + anonymous auth)
```

### B.1 Yeni dosyalar

**`Assets/Scripts/Systems/TelemetrySystem/`** — yeni asmdef, `references: [Core, Data]`.
**İçinde tek bir Firebase tipi geçmez.** EditMode testleriyle test edilebilir.

| Sınıf | Sorumluluk |
|---|---|
| `TelemetryIdentity` | `Version`, `InstallationId`, `PlayerId`, `PlayerOrdinal`, `PlayerCreatedAtUtcTicks` |
| `TelemetryIdentityStore` | `telemetry_identity.json` dosya sınırı. `PlayerProfileStore`'un ikizi, ondan tamamen bağımsız |
| `TelemetryIds` | `I_` / `P_` / `R_` üreteçleri. Saf fonksiyon |
| `RunTelemetryState` | Bir run'ın RAM sayaçları + `ToFieldMap()` |
| `RunStatus` | `enum { InProgress, Completed, Failed, Quit }` |
| `ITelemetrySink` | `WriteRun(runId, map)` · `IsReady` |
| `LogTelemetrySink` | Firebase kurulmadan tüm sistemi test etmeyi sağlar |
| `PlaytestTelemetry` | Static facade — gameplay'in konuştuğu tek yüz |

**`Assets/Scripts/Telemetry/`** — **asmdef yok** (bilinçli, `Assembly-CSharp`).

| Sınıf | Sorumluluk |
|---|---|
| `FirebaseBootstrap` | `[RuntimeInitializeOnLoadMethod]` → dependency check → anonymous sign-in → sink'i tak |
| `FirestoreTelemetrySink` | **Projede `FirebaseFirestore` yazan tek dosya** |
| `TelemetryBinder` | Sahne köprüsü. Event abonelikleri, heartbeat, flush |

Bu, `HapticsSystem` asmdef + `HapticsBinder` (Assembly-CSharp) deseninin aynısı.
**Tek bir `ITelemetrySink` var** ve üç işi birden yapıyor: Firebase sınırı, test fake'i,
Firebase'siz çalıştıran log sink'i. Başka soyutlama eklenmeyecek.

### B.2 Gameplay'e dokunuş — rev 2'de büyüdü, gerekçesi burada

rev 1 tek bir satır öneriyordu. `failed` semantiğini doğru kurmak için **iki sinyal** gerekiyor:
"yeni bir attempt başladı" ve "bu attempt nasıl bitti". İkisi farklı yerlerde doğru.

```csharp
// Core/GameState.cs  — +2 property
public EventBus<int>           DaySessionStarted { get; } = new();
public EventBus<DayAttemptEnd> DayAttemptEnded   { get; } = new();

// Core/DayAttemptEnd.cs  — yeni, 4 satır
public enum DayAttemptEnd { Lost, GivenUp }
```

```csharp
// DayLifecycleManager.ResetForNewDay — son satır
state.DaySessionStarted.Publish(state.CurrentDayIndex);

// GameManager.RetryDay — İLK satır, her şeyden önce
State.DayAttemptEnded.Publish(
    State.IsAwaitingContinue ? DayAttemptEnd.Lost : DayAttemptEnd.GivenUp);

// GameManager.ReturnToMainScreenAbandoningDay — İLK satır, her şeyden önce
State.DayAttemptEnded.Publish(
    State.IsAwaitingContinue ? DayAttemptEnd.Lost : DayAttemptEnd.GivenUp);
```

**Toplam: 2 property + 1 enum + 3 publish satırı. Hiçbir davranış değişmiyor,
mevcut abonesi olmayan yeni event'ler.**

`ResetForNewDay` neden doğru yer: **tam olarak dört runtime çağrı noktası var**
(`GameManager` 360, 1040, 1087, 1152) — dört gün-başlangıcı yolunun hepsi. Sınıf zaten
`GameState`'i tutuyor ve zaten "bir gün denemesinin defter tutucusu". Telemetry run'ı ile
`DayLifecycleManager` epoch'u böylece tanım gereği aynı şey oluyor.

`RetryDay` / `ReturnToMainScreenAbandoningDay`'in **ilk satırı** neden doğru yer: §E.3.

---

## C. Identity Lifecycle

| | **Firebase UID** | **installationId** | **playerId** | **runId** |
|---|---|---|---|---|
| Ne temsil eder | Firebase'e yazma yetkisi | Bir oyun kurulumu / test cihazı | Mantıksal playtester | Tek bir Day attempt'i |
| Format | Firebase üretir | `I_A72FC91D` | `P_72AB19` (default) veya `T001` (elle) | `R_3F8A21C0` |
| Ne zaman oluşur | İlk anonymous sign-in | İlk açılışta, kimlik dosyası yoksa | installationId ile birlikte | Her `DaySessionStarted` |
| Nerede saklanır | Firebase SDK'nın kendi yerel deposu | `telemetry_identity.json` | `telemetry_identity.json` | **sadece RAM** |
| Ne zaman değişir | Neredeyse hiç | Hiç | Sadece "New Test Player" / "Set Tester Id" | Her attempt'te |
| App restart | Aynı | Aynı | Aynı | Yeni run (eskisi `in_progress` kalır) |
| `Delete Save File` | Etkilenmez | **Etkilenmez** | **Etkilenmez** | Day içinde çalışmaz; main screen'de zaten run yok |
| New Test Player | **Etkilenmez** | **Korunur** | **Yeni üretilir** | Main screen'de yapılır, açık run yok |
| Uninstall / reinstall | Yeni UID | Yeni | Yeni | — |
| Firestore alanı | `authUid` | `installationId` | `playerId` | doküman ID'si |

### C.1 Her reset'te yeni Firebase hesabı — hayır

1. **Anonymous UID bir yetki, kimlik değil.** Analitik kimliğiniz `playerId` — sizin kendi veri alanınız.
2. **Terk edilmiş anonymous hesaplar birikir** ve geri alınamaz. Firebase'in otomatik temizliği onları siler
   ama veri modelinizi etkilemez, çünkü kimlik doküman içindeki `playerId`.
3. **`signOut()` + yeni sign-in async'tir ve yarış açar.**
4. **UID yine de dokümana yazılır (`authUid`)** — Security Rules bunun üzerinden çalışır.

### C.2 Üç ID doğru soyutlama mı — evet

`installationId` olmadan "bu iki tester aynı telefondan mı oynadı" cevapsız kalır;
`playerId` olmadan aynı cihazdaki 3 tester tek oyuncuya çöker; `runId` olmadan
attempt/retry/first-attempt analizleri imkânsız. Dördüncü bir ID önerilmiyor.

### C.3 installationId nerede saklanmalı

**Kendi dosyası:** `persistentDataPath/telemetry_identity.json`.

```json
{
  "Version": 1,
  "InstallationId": "I_A72FC91D",
  "PlayerId": "P_72AB19",
  "PlayerOrdinal": 1,
  "PlayerCreatedAtUtcTicks": 638912345678901234
}
```

| Seçenek | Değerlendirme |
|---|---|
| **Ayrı JSON dosyası** — seçilen | `PlayerProfileStore.Delete()` ona hiç dokunmaz; muafiyet yazmaya gerek yok. Versiyon alanı invariant'ı doğal sağlar. `cat`'lenebilir. Mevcut store desenini birebir taklit eder |
| `PlayerProfile`'a alan | Reddedildi. `Delete()` payload'ı bilmemek üzere tasarlanmış; bir alanı muaf tutmak o sınıfı "dosya sınırı" olmaktan çıkarır |
| PlayerPrefs anahtarı | Çalışır ama versiyonsuz (invariant ihlali), Editor'da gizli, elle inceleme zor |

---

## D. Player Reset Lifecycle

### D.1 `Delete Save File` — değiştirilmiyor

Bir geliştirme aracı; davranışını değiştirmek ona alışmış workflow'u bozar.
Yan etki (kasıtlı ve doğru): bastığınızda **playerId aynı kalır** — kendi geliştirme
resetleriniz T001'in datasını parçalamaz.

### D.2 `Reset Game + New Test Player` akışı

```
T001 Main Screen'de
     │
     ▼  [Reset Game + New Test Player]
     │
 1.  PlayerProfileStore.Delete()   →  player_profile.json SİLİNİR
                                      (para, gems, day index, keys, charges, props)
 2.  TelemetryIdentityStore:
        InstallationId  → KORUNUR   I_A72FC91D
        PlayerId        → YENİ      P_9C31F4  (veya elle T002)
        PlayerOrdinal   → +1
        Firebase UID    → KORUNUR   (yeni hesap açılmaz)
 3.  Firestore'da HİÇBİR ŞEY SİLİNMEZ.
     T001'in tüm run'ları durur. Security Rules zaten delete'i reddediyor.
 4.  SceneFlow.LoadMainScreen()
        GameSession dosyayı bulamaz → NewPlayer defaultları → CurrentDayIndex = 0
     │
     ▼
T002 Day 1'den oynuyor.   Firestore: T001 (3 run) + T002 (yeni)
```

**Aktif run kapatma adımı rev 2'de kalktı** — çünkü işlem artık yalnızca main screen'de
çalışıyor ve orada açık bir run zaten yok (§D.3).

### D.3 Day içinde çağrılırsa — reddedilir (rev 2, kullanıcı kararı)

```
Cannot reset player while a Day is active. Return to Main Screen first.
```

Save silinmez, playerId değişmez, sahne manipüle edilmez, run'a dokunulmaz.
Gerekçe: çalışan bir `GameSession`, açık bir run ve canlı gameplay state'in ortasında
identity değiştirmemek. `SROptions`'ın mevcut `Day != null` testi bunun için yeterli
(`GoToDay` ve `DeleteSaveFile` aynı testi zaten yapıyor).

### D.4 İki düğmenin karşılaştırması

| | `Delete Save File` (Save) | `Reset Game + New Test Player` (Telemetry) |
|---|---|---|
| `player_profile.json` | siler | siler (aynı `PlayerProfileStore.Delete()`) |
| `playerId` | **korur** | **yeniler** |
| `installationId` | korur | korur |
| Firestore | hiç dokunmaz | hiç dokunmaz |
| Day içinde | profili siler, reload etmez, log basar | **hiçbir şey yapmaz**, uyarı loglar |
| Amaç | geliştirme resetleri | tester devri |

---

## E. Run Lifecycle

### E.1 Ana akış

```
Play  ──►  SceneFlow.LoadDayScene()
             │
 GameManager.Awake()
   · GameSession kurulur (profil okunur)
   · DayLifecycleManager.ResetForNewDay()  ──►  DaySessionStarted
   · FillEmptySlots()  →  ilk 3 bilet, GAMEPLAY CANLI
             │
 TelemetryBinder.Start()          ← tüm Awake'ler bitmiş olmak zorunda
   · host.Session okunur          ← D-092'nin aynı yarış gerekçesi
   · Event abonelikleri
   · RUN AÇILIR   runId · startedAt · dayIndex · dayContentIndex · status = in_progress
   · sink.WriteRun(...)                                     ◄── write 1
             │
 ══════════════  gameplay — hepsi yalnızca RAM  ══════════════
   TicketDelivered       →  ticketsDelivered++
   TicketCancelled       →  ticketsExpired++ · timeoutCount++ · mistakeCount++
   TraySlotScatterBegin  →  wrongDeliveryCount++ · mistakeCount++
   LivesDepleted         →  livesDepletedCount++
   ChargesChanged (↓)    →  boosterUses[type]++            (Phase 4)
   Update()              →  elapsedSeconds += unscaledDt
        └── her 15 sn:  sink.WriteRun(runId, snapshot)     ◄── heartbeat
             │
 ══════════════  terminal — yalnızca üç sinyal  ══════════════
   DayCompleted             →  status = completed          → flush
   DayAttemptEnded(Lost)    →  status = failed             → flush
   DayAttemptEnded(GivenUp) →  status = quit               → flush
             │
   DaySessionStarted (açık run varsa)  →  status = quit, flush, sonra YENİ run aç
   OnApplicationPause / OnDestroy      →  SADECE flush, status'e DOKUNMAZ
   Uygulama öldürüldü                  →  hiçbir şey. Son heartbeat in_progress kalır.
```

### E.2 Run tam olarak nerede oluşturulmalı

**`TelemetryBinder.Start()` içinde.**

- `GameManager.Awake()`'in son satırı `FillEmptySlots()` — `Awake` bittiğinde gameplay zaten canlı.
  Unity `Start`'ları tüm `Awake`'lerden sonra çalıştırır, dolayısıyla `Start` "oynanabilir ilk kare".
- `Awake`'de yapılamaz: GameObject'ler arası `Awake` sırası garanti değil; `host.Session` null olabilir.
  `DebugMenuBinder` da bunu aynı gerekçeyle yapmıyor.
- **`Start` ilk run'ı kendisi açar; `DaySessionStarted` yalnızca sonraki run'ları açar** — çünkü
  ilk `DaySessionStarted` zaten `Awake` sırasında yayınlanmış olur. Sahne içinde retry `Start`'tan
  sonra gerçekleştiği için hiçbir run kaçmaz.

### E.3 `failed` nerede belirlenir — koddan doğrulanmış

Bu, rev 2'nin ana düzeltmesi. Sırasıyla neyi denedim ve neden reddettim:

**Denenen 1 — `LivesDepleted` = fail. REDDEDİLDİ.**
`LivesManager.LoseLife()` canlar 0'a inince `IsAwaitingContinue = true` yapıp `LivesDepleted`
yayınlıyor. Ama `TryContinueWithGems()` başarılı olursa `RefillLivesAndResume()` çalışıp
**aynı run devam ediyor** ve `GameManager.cs:717` bunu açıkça yazıyor:
*"a paid Continue never publishes DayRetried, so continuing … "*.
`LivesDepleted` terminal bir sinyal değil, run içinde tekrarlanabilen bir olay.

**Denenen 2 — terminal anda `IsAwaitingContinue` okumak. REDDEDİLDİ, ve sandığımdan kötü.**
`LivesManager.RefillLivesAndResume()` (D-103) bayrağı **canları doldurmadan ÖNCE** temizliyor:

```csharp
private void RefillLivesAndResume()
{
    state.IsAwaitingContinue = false;   // ← önce bu
    state.Lives = state.MaxLives;
}
```

Ve `RefillForNewDay()` reset dizisinde `DayLifecycleManager.ResetForNewDay()`'den **önce**
çağrılıyor. Aynı şekilde `ReturnToMainScreenAbandoningDay()` de `SceneFlow.LoadMainScreen()`'den
önce `LivesManager.RefillForNewDay()` çağırıyor.

> **Sonuç:** `DaySessionStarted` anında da, `OnDestroy` anında da `IsAwaitingContinue`
> **her zaman `false`** olur. rev 1'in heuristic'i yalnızca yanlış değil, **evrensel olarak
> yanlıştı** — her run'ı `quit` olarak damgalardı, gerçekten kaybedilenler dahil.

**Denenen 3 — `RetryDay`'in `givingUpOnAttempt` parametresi. YETERSİZ.**
Üç çağıran var ve ikisi aynı değeri geçiyor:

| Çağıran | Satır | Değer | Gerçek durum |
|---|---|---|---|
| `GameOverPopupView.OnRetryClicked` | `:162` | `true` | canlar bitmiş → **failed** |
| `SettingsPopupView.ConfirmRetry` | `:309` | `true` | canlar duruyor → **quit** |
| `SROptions` debug retry | `:237` | `false` | canlar duruyor → **quit** |

Parametre "bu bir teslim mi?" sorusunu cevaplıyor (key ücreti için), "attempt kaybedildi mi?"
sorusunu değil. İkisi farklı sorular — D-135'in kendi dersi de tam olarak bu.

**Seçilen — terminal metodun İLK satırında `IsAwaitingContinue` okumak.**

Bu bir heuristic değil, kodun kendi state'inin doğru anda okunması. D-135 `IsAwaitingContinue`
hakkında şunu söylüyor: *"the flag could only ever answer 'was the day lost?'"* — D-135'te bu bir
kusurdu çünkü orada sorulan soru "bu bir teslim mi?" idi. **Bizim sorduğumuz soru tam olarak
"was the day lost?"** — yani bayrak burada doğru cevabı veren araç.

İki metodun da girişinde bayrak henüz temizlenmemiş:

| Metot | İlk satırlar | Bayrağı temizleyen satır |
|---|---|---|
| `ReturnToMainScreenAbandoningDay()` | `DiscardPendingReward()` → `wallet.RevertToDayStart()` | `LivesManager.RefillForNewDay()` (3. çağrı) |
| `RetryDay(bool)` | `var ticketsBeforeRetry = ...` | `LivesManager.RefillForNewDay()` (6. çağrı) |

Publish **her ikisinin de ilk satırına** konursa okuma her zaman doğru.

**Bağımsız doğrulama:** `SettingsPopupView:213` ve `:274`, menünün `IsAwaitingContinue`
true iken **açılmayı reddettiğini** gösteriyor. Yani settings'ten gelen her retry/main-menu
zorunlu olarak `IsAwaitingContinue == false`, Game Over popup'tan gelen her ikisi de zorunlu
olarak `true`. Eşleme kod tarafından garanti altında.

### E.4 Status kuralları — tam ve çelişkisiz

| Sinyal | Status | Neden kesin |
|---|---|---|
| `DayCompleted` | `completed` | `TicketSlotManager` gün bitişinin tek karar noktası |
| `DayAttemptEnded(Lost)` | `failed` | Attempt, oyuncu canları tükenmişken ve Continue almamışken sonlandı |
| `DayAttemptEnded(GivenUp)` | `quit` | Oyuncu hâlâ oynanabilir bir attempt'ten kendi isteğiyle çıktı |
| `DaySessionStarted` + açık run | `quit` | Yeni bir attempt başladıysa öncekinden çıkılmış demektir. Gerçek yollarda ulaşılamaz (`DayAttemptEnded` her zaman önce gelir); yalnızca debug `NextDay`'in gün ortasında basılması gibi cheat yollarında devreye girer |
| `OnApplicationPause` / `OnDestroy` | **değişmez** | Sadece flush. Sahne, oyunun bize nasıl bittiğini söylemeden ölmüşse cevap `in_progress`'tir |
| Uygulama öldürüldü / crash | `in_progress` kalır | Client hiçbir şey yazmaz; analytics bayat `in_progress`'i abandonment olarak yorumlar |

**`livesDepletedCount` hiçbir status kararına girmez.** Sadece bir performans metriğidir.

### E.5 Senaryo doğrulaması

Her senaryo yukarıdaki gerçek kod akışına göre izlendi.

**Senaryo A — Day Start → gameplay → Day Complete**
`TicketSlotManager.AssignTicket` → `DayCompleted` → `Run A = completed`. ✔

**Senaryo B — Day Start → canlar bitti → Give Up / Retry → yeni attempt**
`LivesDepleted` (RAM: `livesDepletedCount = 1`) → oyuncu Game Over popup'ta Retry →
`RetryDay` ilk satırı: `IsAwaitingContinue == true` → `DayAttemptEnded(Lost)` →
**`Run A = failed`**. Ardından reset dizisi → `ResetForNewDay` → `DaySessionStarted` →
açık run yok → **`Run B = in_progress`**. ✔

**Senaryo C — Day Start → canlar bitti → Gem Continue → gameplay → Day Complete**
`LivesDepleted` (RAM: 1) → `TryContinueWithGems()` → `RefillLivesAndResume()` →
`IsAwaitingContinue = false`, **run kapanmaz, `DayRetried` yayınlanmaz, `ResetForNewDay`
çağrılmaz** → gameplay aynı run'da devam → `DayCompleted` → **`Run A = completed`.
Tek run.** ✔

**Senaryo D — Day Start → canlar bitti → Gem Continue → gameplay → Main Menu**
Continue `IsAwaitingContinue`'yu temizledi → oyuncu settings'ten Main Menu →
`ReturnToMainScreenAbandoningDay` ilk satırı: `IsAwaitingContinue == false` →
`DayAttemptEnded(GivenUp)` → **`Run A = quit`. `failed` DEĞİL.** ✔
*(Bu, rev 1'in bozduğu ve rev 2'nin düzelttiği senaryo.)*
Dokümanda kalan iz: `livesDepletedCount = 1` — yani "bu oyuncu bir kez duvara çarptı,
ödedi, devam etti, sonra bıraktı" hikâyesi datadan tamamen okunabiliyor.

**Senaryo E — Day Start → gameplay → Main Menu**
`IsAwaitingContinue == false` → `DayAttemptEnded(GivenUp)` → **`Run A = quit`**. ✔

**Senaryo F — Day Start → gameplay → force close**
Hiçbir terminal sinyal yok. Son yazılan heartbeat (veya `OnApplicationPause` snapshot'ı)
Firestore'da kalır → **`Run A = in_progress`**, `lastUpdatedAt` = son başarılı snapshot.
Analytics bayat `in_progress` kuralıyla abandonment sayar. ✔

**Senaryo G — Day Start → gameplay → background → foreground → gameplay → complete**
`OnApplicationPause(true)` yalnızca flush eder, status'e dokunmaz, run'ı kapatmaz.
`OnApplicationPause(false)` hiçbir şey yapmaz. `DayCompleted` → **aynı runId,
`Run A = completed`**. Background/foreground yeni run oluşturmaz. ✔

**Senaryo H (ek) — Day Complete → popup'ta Retry (yıldız için tekrar)**
`DayCompleted` çoktan `Run A = completed` yaptı. `RetryCompletedDay()` →
`ResetForNewDay` → `DaySessionStarted` → açık run yok (A kapalı) → **`Run B` açılır**. ✔

**Senaryo I (ek) — Day Complete → Next Day**
`Run A = completed`. `AdvanceToNextDay()` → `ResetForNewDay` → `DaySessionStarted` →
**`Run B` açılır**, yeni `dayContentIndex` ile. ✔

---

## F. Firestore Schema

### F.1 Yapı: tek düz koleksiyon `runs/{runId}`

**Neden düz:** Balancing sorularının hepsi oyuncular arası ("Day 8'i oynayan herkes").
İç içe yapıda bu her seferinde collection-group query gerektirir; düz yapıda basit bir
`where` yeter. Per-player güvenlik izolasyonuna ihtiyaç yok.

**`runId` doküman ID'si olarak — evet.** Doküman ID'si benzersizliği garanti eder;
`SetAsync(MergeAll)` aynı ID'ye yazdığı sürece aynı attempt için ikinci bir doküman
**fiziksel olarak oluşamaz.** `AddAsync` (otomatik ID) her snapshot'ta yeni doküman riski demek olurdu.

### F.2 Örnek doküman (v1, rev 2 gruplaması)

```json
{
  "schemaVersion": 1,
  "environment": "playtest",

  "runId":          "R_3F8A21C0",
  "playerId":       "P_72AB19",
  "installationId": "I_A72FC91D",
  "authUid":        "kQ7x…",

  "dayIndex":        8,
  "dayContentIndex": 8,
  "dayNumberShown":  9,

  "status":        "quit",
  "startedAt":     "<server timestamp>",
  "lastUpdatedAt": "<server timestamp>",
  "completedAt":   null,
  "elapsedSeconds": 184.2,

  "mistakeCount":       6,
  "wrongDeliveryCount": 4,
  "timeoutCount":       2,
  "ticketsDelivered":   7,
  "ticketsExpired":     2,
  "score":              1250,
  "stars":              0,
  "starScore":          0.0,
  "livesDepletedCount": 1,

  "buildVersion": "0.3.4",

  "platform":     "Android",
  "deviceModel":  "SM-A526B",
  "unityVersion": "6000.3.16f1"
}
```

### F.3 Alan grupları

| Grup | Alan | v1 | Kaynak |
|---|---|---|---|
| **Identity** | `runId`, `playerId`, `installationId` | **required** | `TelemetryIdentity` + `TelemetryIds` |
| | `authUid` | **required** | Security Rules bunu şart koşar |
| **Day** | `dayIndex` | **required** | `GameState.CurrentDayIndex` (katalog pozisyonu) |
| | `dayContentIndex` | **required** | `DayDefinition.DayIndex` — **canonical content identity** |
| | `dayNumberShown` | optional | `dayIndex + 1`, rapor okumayı kolaylaştırır |
| **Lifecycle** | `status` | **required** | `in_progress` / `completed` / `failed` / `quit` — §E.4 |
| | `startedAt`, `lastUpdatedAt` | **required** | **Firestore server timestamp** |
| | `completedAt` | **required** (null olabilir) | Server timestamp, yalnızca `completed`'da |
| | `elapsedSeconds` | **required** | Unity `Time.unscaledDeltaTime` birikimi |
| **Performance** | `mistakeCount`, `wrongDeliveryCount`, `timeoutCount` | **required** | Event'lerden RAM sayacı |
| | `ticketsDelivered`, `ticketsExpired` | **required** | `TicketDelivered` / `TicketCancelled` |
| | `score`, `stars`, `starScore` | **required** | `DayLifecycleManager`'dan pull |
| | `livesDepletedCount` | **required** | `LivesDepleted` event sayısı — **status kararına girmez** |
| **Metadata** | `schemaVersion`, `environment`, `buildVersion` | **required** | Sabit / build modu / `Application.version` |
| **Diagnostics** | `platform`, `deviceModel`, `unityVersion` | **optional** (rev 2) | `Application.platform`, `SystemInfo.deviceModel`. Yokluğu telemetry'yi bozmaz |
| **Phase 4** | `attemptNumber`, `boosterUses` | future | Yerel sayaç / `ChargesChanged` |
| **Phase 5** | `tickets` alt-koleksiyonu | future | Ticket-level telemetry |
| **Balancing** | `predictedDifficulty` | future | Simulator — client yazmaz |

**Şemadan çıkarılanlar (rev 2):** `discardCount` — oyunda discard mekaniği yok, sırf telemetry
için event oluşturulmayacak; tepsiden geri çıkarma semantik olarak farklı bir davranış.
`livesDepleted: bool` — `livesDepletedCount` ile değiştirildi.

`SystemInfo.deviceUniqueIdentifier` **hiçbir koşulda kullanılmayacak.**

### F.4 `continuesUsed` — alan gerektirmiyor, tam olarak türetilebilir

Continue satın alımının kodda bir event'i yok (`LivesManager.TryContinueWithGems` sadece
`bool` döndürüyor). Ama §E.4'ün status kuralları verildiğinde değer **kesin olarak** çıkarılabilir:

| Run nasıl bitti | `continuesUsed` |
|---|---|
| `failed` | `livesDepletedCount − 1` (son tükeniş devam ettirilmedi, run'ı o bitirdi) |
| `completed` veya `quit` | `livesDepletedCount` (her tükeniş bir Continue ile aşıldı) |
| `in_progress` | bilinmiyor (oyuncu Continue ekranında kapatmış olabilir) |

Bu yüzden v1'e **yeni alan da yeni event de eklenmiyor.** Analitik tarafta tek satırlık
bir hesap. İleride açık bir alan istenirse Phase 4'te `LivesManager`'a küçük bir event eklenir.

### F.5 Index gerektirecek sorgular

| Sorgu | Composite index |
|---|---|
| Day 8'in tüm playtest run'ları | `environment ↑, dayContentIndex ↑, startedAt ↓` |
| Bir build'in Day 8 sonuçları | `environment ↑, buildVersion ↑, dayContentIndex ↑` |
| Bir oyuncunun bir Day'deki denemeleri | `playerId ↑, dayContentIndex ↑, startedAt ↑` |
| Bir installation'dan gelen her şey | `installationId ↑, startedAt ↓` |
| Tamamlanmış run'lar süreye göre | `environment ↑, dayContentIndex ↑, status ↑, elapsedSeconds ↑` |

Console, index'siz bir sorgu ilk çalıştığında tek tıkla oluşturma linki verir.

### F.6 Hedef sorguların karşılığı

| Soru | Nasıl cevaplanır |
|---|---|
| Day 8'i kimler oynadı / kaç kez / kaç unique player | `where dayContentIndex==8` → sayı, distinct `playerId` |
| Completion rate | `status=="completed"` / toplam |
| Fail rate | `status=="failed"` / toplam |
| Abandonment rate | (`status=="quit"` + bayat `in_progress`) / toplam — §K |
| Ortalama/medyan completion time | `status=="completed"` → `elapsedSeconds` |
| Ortalama mistake | `mistakeCount` ortalaması |
| Star distribution | `status=="completed"` → `stars` histogramı |
| Bir player kaç kere denedi | `where playerId==X and dayContentIndex==8` sayısı |
| First-attempt completion rate | oyuncu başına `startedAt` min olan run'ın `status`'ü |
| Retry rate | 1'den fazla run'a sahip oyuncu oranı |
| Continue kullanımı | `livesDepletedCount` + `status` → §F.4 türetmesi |
| Hangi build | `where buildVersion==...` |
| Sadece playtest datası | `where environment=="playtest"` |
| Ortalama hangi noktada terk edildi | bayat `in_progress` → `elapsedSeconds` dağılımı |
| Çıkmadan önce kaç hata | aynı run'ların `mistakeCount`'u |
| Hangi ticket'lar problem çıkardı | **Phase 5** ticket-level telemetry gerektirir |

### F.7 Ticket-level telemetry — ileriye dönük

| Yapı | Artı | Eksi |
|---|---|---|
| **`runs/{runId}/tickets/{i}` alt-koleksiyon** — önerilen | Ana doküman şişmez; collection-group ile "tüm Day 8 biletleri"; bilet bittiğinde tek write | Run başına ~15 write artışı |
| Ana dokümanda nested array | Ekstra write yok | Her snapshot tüm array'i yeniden yazar; array içinde sorgu yapılamaz |
| Ayrı kök koleksiyon | Sorgu en esnek | `runId` ile join; iki koleksiyon senkron tutulur; gereksiz |

**İlk versiyonda kurulmayacak.** Alt-koleksiyon eklemek mevcut dokümanlara hiç dokunmaz.

---

## G. Firebase Setup — Console adımları (henüz UYGULANMAYACAK)

### G.0 Ön koşul — Unity tarafında şart olan tek şey

`ProjectSettings.asset`'te Android/iOS için ayrı identifier **serialize edilmemiş**.
Firebase app kaydı tam olarak eşleşen bir package name / bundle ID ister:

- `Edit → Project Settings → Player → Android → Other Settings → Package Name`
- `Edit → Project Settings → Player → iOS → Other Settings → Bundle Identifier`

Kalıcı, gerçek bir değer yazın (örn. `com.<şirket>.expotheexplorer`).
`com.DefaultCompany.*` ile devam etmeyin.

### G.1 Adımlar

1. **Firebase projesi oluştur.** **Google Analytics'i KAPALI seçin** (§M.3).
2. **Android app ekle** — package name birebir. SHA-1 gerekmez (anonymous auth kullanıyoruz).
   → `google-services.json`.
3. **iOS app ekle** — bundle ID birebir. → `GoogleService-Info.plist`.
4. **Her iki dosyayı `ExpoTheExplorer/Assets/` altına koy.**
5. **Authentication → Sign-in method → Anonymous → Enable.**
6. **Firestore Database → Create database.** Mode: **Production** (test mode 30 gün sonra kapanır
   ve o ana kadar herkese açıktır). Location: yakın region — **sonradan değiştirilemez.**
7. **Security Rules** sekmesine §M.1'deki kuralları yapıştır → Publish.
8. **Firebase Unity SDK** (güncel sürüm 13.9.0, Mart 2026) indir. **Yalnızca** `FirebaseApp`,
   `FirebaseAuth`, `FirebaseFirestore` import et. **`FirebaseAnalytics`'i import etme.**
9. **EDM4U** SDK ile gelir → `Assets → External Dependency Manager → Android Resolver → Resolve`.
10. **iOS:** build sonrası Xcode projesinde `pod install`; Mac'te CocoaPods kurulu olmalı.
11. **Doğrulama:** Step 1'in smoke testi.

**`google-services.json` ve git:** gizli anahtar değil (client config; güvenlik Rules'a dayanır).
Repo private olduğu sürece commit'lemek doğru tercih.

---

## H. SRDebugger Plan

Tek dosya: `Scripts/Debug/SROptions.Expo.cs` — mevcut `partial class SROptions`'ın sonuna
yeni bir `[Category("Telemetry")]` bloğu. Yeni dosya yok, yeni asmdef yok,
`DebugMenuBinder`'a yeni `[SerializeField]` gerekmiyor (erişim `PlaytestTelemetry` static
facade'ı üzerinden). Mevcut kurala uyar: her cheat, veriyi zaten sahiplenen sınıfa komut verir.

| Tip | DisplayName | Davranış |
|---|---|---|
| readout | Installation Id | `I_A72FC91D` |
| readout | Player Id | `P_72AB19` |
| readout | Current Run | `R_3F8A21C0` ya da `-` |
| readout | Run Status | `in_progress` / `completed` / `failed` / `quit` |
| readout | Telemetry State | `Ready` / `Initializing` / `FAILED: <sebep>` / `Disabled` |
| readout | Writes OK / Failed | `142 / 0` — sessiz hataları görünür kılar |
| property | Tester Id | Yazılabilir string (örn. `T004`) |
| button | **Set Tester Id** | playerId'yi değiştirir. **Main screen'de**; day içinde reddeder |
| button | **New Random Test Player** | `P_xxxxxx` üretir. **Main screen'de**; day içinde reddeder |
| button | **Reset Game + New Test Player** | §D.2. **Main screen'de**; day içinde reddeder (§D.3) |
| button | Force Telemetry Snapshot | Anında flush — day içinde de çalışır |

`Force Telemetry Snapshot` gerekçesi: heartbeat 15 saniyede bir; Firebase'i ilk test ederken
"yazdı mı" sorusunu beklemeden cevaplamak Step 4'ün debug döngüsünü hızlandırır.
`Writes OK / Failed` ile birlikte write hatalarını cihaz üzerinde görünür kılar. Maliyeti 5 satır.

**Yeniden kullanılan mevcut metotlar:** `PlayerProfileStore.Delete()`, `SceneFlow.LoadMainScreen()`,
`SROptions`'ın mevcut `Day`/`Sess` çözümleyicileri ve `NoSession()` guard'ı. Day-içi reddetme
için mevcut `Day != null` testi (`GoToDay` ve `DeleteSaveFile` aynı testi yapıyor).

**`Delete Save File` ile ilişkisi: yok. İkisi ayrı düğme kalacak** — §D.4.

---

## I. Telemetry Persistence Strategy

### Net cevap

> Telemetry state RAM'de tek bir `RunTelemetryState` nesnesinde tutulacak; Firestore'a
> **(1)** run açılışında, **(2)** her 15 saniyede bir heartbeat'te, **(3)** terminal
> status değişimlerinde, **(4)** `OnApplicationPause`/`OnDestroy`'da (yalnızca flush)
> yazılacak. Gameplay event'leri **sadece RAM sayaçlarını** günceller.

**Prensip (rev 2'de netleştirildi):**
*Gameplay events mutate RAM. Persistence happens at lifecycle boundaries + heartbeat.*
Bu prensipten sapan tek bir istisna yok — rev 1'in `LivesDepleted` immediate write'ı kaldırıldı (§J).

### Seçeneklerin karşılaştırması

| | A · RAM + periyodik | B · Event tabanlı | **C · Hibrit (seçilen)** |
|---|---|---|---|
| Write / run (180 sn) | ~13 | 50-80 | **~14** |
| Abandonment doğruluğu | ±15 sn | mükemmel | ±15 sn |
| Gameplay coupling | düşük | **yüksek** | düşük |
| Implementation | 1 timer | her event için ayrı yol | 1 timer + 3 terminal noktası |
| Debug edilebilirlik | kolay | zor | kolay |
| Terminal state güncelliği | **15 sn gecikir** | anında | **anında** |

**A tek başına neden yetmiyor:** `DayCompleted` ile sahne yıkımı arasında 15 saniyeden az
zaman olabilir (oyuncu popup'ta hemen "Go Back"e basar) — final skor/yıldız hiç yazılmaz.

**B neden reddedildi:** her yeni metrik yeni bir write noktası demektir. Ayrıca EventBus
**senkron**: `TicketDelivered` handler'ı içinde ağ çağrısı başlatmak, teslim → yeni bilet
atama → board dağıtımı zincirinin ortasına I/O sokar.

**15 saniye:** 180 saniyelik bir Day'de 12 heartbeat. 10 saniye %35 daha fazla write için
5 saniyelik çözünürlük; 30 saniye terk anını ±30 sn'ye bulanıklaştırır.
`TelemetryBinder` üzerinde `[SerializeField]` olarak ayarlanabilir olacak.

**Maliyet:** 3000 run'lık bir playtest ≈ 42.000 write, günlere yayılmış.
Firestore ücretsiz katmanı günde 20.000 write.

---

## J. Telemetry Update Table (rev 2)

| Event | Runtime State Action | Firestore Action | Run Status Change |
|---|---|---|---|
| **Day Start** (`TelemetryBinder.Start`) | Yeni `RunTelemetryState`, kimlik + Day + metadata | **write** — doküman oluştur, `startedAt` server ts | → `in_progress` |
| **Day Session Started** (event, açık run varsa) | Açık run'ı kapat → yeni run aç | **2 write** — eskisini finalize, yenisini oluştur | eski → `quit` · yeni → `in_progress` |
| **Mistake — wrong delivery** (`TraySlotScatterBegin`) | `wrongDeliveryCount++`, `mistakeCount++` | yok | değişmez |
| **Mistake — timeout** (`TicketCancelled`) | `timeoutCount++`, `mistakeCount++`, `ticketsExpired++` | yok | değişmez |
| **Ticket Delivered** (`TicketDelivered`) | `ticketsDelivered++` | yok | değişmez |
| **Ticket Expired** | *(timeout satırıyla aynı — tek event)* | yok | değişmez |
| **Score Changed** | abone olunmaz; `DayLifecycleManager.Total` heartbeat'te **okunur** | yok | değişmez |
| **Booster Used** (`ChargesChanged` ↓) | `boosterUses[type]++` *(Phase 4)* | yok | değişmez |
| **Lives Depleted** (`LivesDepleted`) | `livesDepletedCount++` | **yok** (rev 2'de kaldırıldı) | **değişmez** |
| **Continue Purchased** | doğrudan sinyal yok; §F.4 ile türetilir | yok | **değişmez — aynı run devam eder** |
| **Retry — canlar bitmişken** (`DayAttemptEnded(Lost)`) | run'ı finalize et | **write** — final snapshot | → **`failed`** |
| **Give Up — canlar dururken** (`DayAttemptEnded(GivenUp)`) | run'ı finalize et | **write** — final snapshot | → **`quit`** |
| **Main Menu — canlar bitmişken** (`DayAttemptEnded(Lost)`) | run'ı finalize et | **write** | → **`failed`** |
| **Main Menu — canlar dururken** (`DayAttemptEnded(GivenUp)`) | run'ı finalize et | **write** | → **`quit`** |
| **Periodic Heartbeat** (15 sn) | `elapsedSeconds`, `Total`, `StarScore` oku | **write** — sayaçlar + `lastUpdatedAt` | değişmez |
| **Day Completed** (`DayCompleted`) | `stars`, `starScore`, `score` oku, finalize | **write** — `completedAt` server ts | → **`completed`** |
| **Application Pause** (`OnApplicationPause(true)`) | `elapsedSeconds` güncelle | **write** — flush | **değişmez** |
| **Application Resume** (`OnApplicationPause(false)`) | hiçbir şey | yok | değişmez |
| **Application Quit** (`OnApplicationQuit`) | `elapsedSeconds` güncelle | best-effort write | **değişmez** |
| **Scene Destroy** (`OnDestroy`) | `elapsedSeconds` güncelle | **write** — flush | **değişmez** |
| **Force Close / crash** | — | yok | **`in_progress` kalır** |

**Dört terminal satırın (`Lives Depleted` · `Continue` · `Retry` · `Give Up`) çelişkisizliği:**
`Lives Depleted` ve `Continue` **status'e hiç dokunmaz** — ikisi de run içi olaylar.
Status'ü yalnızca `DayAttemptEnded` ve `DayCompleted` değiştirir, ve `DayAttemptEnded`'in
payload'ı `IsAwaitingContinue`'nun terminal metodun ilk satırındaki değeri olduğu için
Continue almış bir oyuncu asla `failed` alamaz (§E.3, §E.5-D).

---

## K. Abandonment Strategy

Client **hiçbir zaman `"abandoned"` yazmaz.** Terk, analiz tarafında türetilir:

```
status == "in_progress"  AND  lastUpdatedAt < (now − 10 dakika)
        →  ABANDONED (stale)
```

Run'ın son bilinen state'i zaten dokümanda: `elapsedSeconds`, `mistakeCount`,
`ticketsDelivered`, `livesDepletedCount`.

**İki tür terk ayrı ayrı ölçülüyor ve bu ayrım kasıtlı:**

| | `status == "quit"` | bayat `status == "in_progress"` |
|---|---|---|
| Ne oldu | Oyuncu **açıkça** çıktı (Main Menu, settings'ten vazgeçme, give up) | Uygulama finalize edemeden öldü |
| Client ne yazdı | Terminal snapshot, kesin | Son heartbeat / pause snapshot |
| Zaman doğruluğu | Kesin | ±15 sn |

### Edge case'ler

| Durum | Firestore | Yorum |
|---|---|---|
| Main Menu (canlar dururken) | `quit` | Kasıtlı terk — en değerli sinyal |
| Main Menu (canlar bitmişken) | `failed` | Attempt gerçekten kaybedilerek sonlandı |
| Retry (Game Over popup'tan) | eski run `failed` | `IsAwaitingContinue == true` |
| Retry (settings'ten, canlar dururken) | eski run `quit` | Settings `IsAwaitingContinue` iken açılmıyor |
| Continue → sonra Main Menu | `quit` | **`failed` değil** — §E.5-D |
| Force close / swipe | `in_progress` bayat | Gerçek terk anı ≤15 sn sonrası |
| Crash | `in_progress` | Force close'dan ayırt edilemez — kabul edilebilir |
| Telefon kilitlendi, döndü | `in_progress`, heartbeat devam eder | `lastUpdatedAt` tazelendiği için bayat sayılmaz |
| Editor'de play mode'dan çıkış | `in_progress` | `environment=="editor"` ile filtrelenir |

---

## L. Failure Strategy

**Temel kural: telemetry hatası gameplay'i asla bozmaz. Oyuncuya teknik popup gösterilmez.**

| Hata | Ne olur |
|---|---|
| Firebase initialization | `Debug.LogError`. Sink `null` → no-op mod. RAM sayaçları yine tutulur (SRDebugger'da görünür). Oyun normal çalışır |
| Authentication | Aynı. **Retry yapılmaz.** SRDebugger `FAILED: auth` gösterir |
| Firestore write | `ContinueWithOnMainThread` içinde `LogError`, `writesFailed++`. Run yaşamaya devam eder — heartbeat tüm state'i gönderdiği için **kaçan write bir sonrakinde telafi olur** |
| Init bitmeden run açıldı | `IsReady` false ise yazma atlanır. **Tek bir yazma metodu var** (`SetAsync(MergeAll)`), yani "create kaçtı" diye bir durum yok: ilk başarılı yazma dokümanı zaten oluşturur |
| Unity main thread | Firebase continuation'ları arka planda çalışır. Sadece `ContinueWithOnMainThread`. `RunTelemetryState`'e yalnızca binder (main thread) yazar |

### L.1 Offline — kapsam dışı (rev 2)

Playtest kontrollü bir ortamda, **internet bağlantısı garanti** varsayımıyla yapılacak.

**Yazılmayacaklar:** offline queue, persistent telemetry buffer, custom retry pipeline,
background sync manager, offline-first logic, network recovery sistemi.

**Test edilmeyecekler:** uçak modu senaryoları, offline recovery doğrulaması.
Bunlar zorunlu implementation/test scope'undan çıkarıldı.

Bilgi olarak: Firestore Unity SDK'sında yerel kalıcılık mobilde varsayılan açık; bir yazma
yerel diske kaydedildiği anda kabul edilir ve SDK arka planda göndermeye çalışır, dönen `Task`
ise sunucu onayında tamamlanır. Bu bize bedava geliyor — **onu bozmayacağız, üzerine bir şey
kurmayacağız, doğrulamak için ayrıca test yazmayacağız.**

---

## M. Privacy / Security

### M.1 Security Rules

```javascript
rules_version = '2';
service cloud.firestore {
  match /databases/{database}/documents {

    match /runs/{runId} {
      allow create: if request.auth != null
                    && request.resource.data.authUid == request.auth.uid
                    && request.resource.data.environment is string;

      allow update: if request.auth != null
                    && resource.data.authUid == request.auth.uid
                    && request.resource.data.authUid == resource.data.authUid;

      allow read, delete: if false;
    }

    match /{document=**} {
      allow read, write: if false;
    }
  }
}
```

- Bir tester'ın başka bir tester'ın run'ını değiştirmesi engellenir.
- **Silme tamamen kapalı** — "eski oyuncunun datası ASLA silinmemeli" gereksinimi kural
  düzeyinde garanti. Client'ta bir bug bile datayı silemez.
- Client okuma yapamaz.

**Kasıtlı olarak yapılmayanlar:** alan-alan tip doğrulaması, rate limiting, App Check,
Cloud Functions ile server-side validation.

### M.2 Toplanan / toplanmayan veri

**Toplanan:** `installationId`, `playerId`, `runId` (hepsi bizim ürettiğimiz rastgele),
Firebase anonymous UID, cihaz *modeli* (optional), platform, build sürümü, gameplay sayaçları.

**Toplanmayan:** gerçek isim, e-posta, telefon, advertising ID (IDFA/GAID), cihaz seri numarası,
`SystemInfo.deviceUniqueIdentifier`, konum, IP tabanlı tanımlayıcı.

### M.3 Firebase SDK'nın kendi otomatik topladıkları

1. **Firebase Analytics'i kurmayın.** Kurulursa otomatik olarak `app_instance_id`, cihaz modeli,
   OS sürümü, dil, ülke (IP'den), ekran çözünürlüğü, uygulama sürümü ve Android'de
   **Advertising ID** toplar; `first_open`, `session_start`, `screen_view` event'lerini
   kendiliğinden gönderir. Hiçbirine ihtiyaç yok.
2. **Firebase Auth** anonymous hesap için UID, oluşturma ve son giriş zamanı tutar. Kişisel veri toplamaz.
3. **Firestore** her isteğin IP'sini Google'ın altyapı loglarında tutar. Veri modelinizin parçası değil.
4. **Crashlytics** kurulmayacak.

### M.4 Environment ayrımı

| Yaklaşım | Ne zaman |
|---|---|
| **`environment` alanı** — şimdi | `"playtest"` (cihaz build'i) / `"editor"` (Unity play mode). Sıfır ek kurulum |
| Ayrı collection | Gereksiz — `where environment==` aynı işi yapıyor, index'leri ikiye böler |
| **Ayrı Firebase projesi** — production geldiğinde | Gerçek izolasyon: ayrı kota, fatura, rules, erişim. Şimdi kurmak gereksiz karmaşıklık |

Editor'de telemetry açık kalacak, toggle eklenmeyecek (kullanıcı kararı).

### M.5 Client read yetkisi ≠ balancing read yetkisi (rev 2)

Uzun vadeli hedeflerden biri:

```
Day Editor / Balancing Simulator  →  Firestore actual player telemetry
                                  →  Predicted vs Actual Difficulty
```

**Bu, playtest client'ının Firestore read yetkisi kazanması anlamına GELMEZ.**
İki yetki birbirinden tamamen bağımsızdır ve öyle kalmalıdır:

| | Oyun client'ı (playtest & production) | Balancing / analytics okuma |
|---|---|---|
| Kimlik | Firebase Anonymous Auth | Ayrı bir güvenli kanal |
| Yetki | `create` + `update`, **`read: false`** | Tam okuma |
| Nerede çalışır | Tester'ın telefonunda | Sizin makinenizde / güvenli bir ortamda |
| Nasıl yapılabilir | — | Editor-only trusted tool · Firebase Admin SDK · service account kullanan küçük bir script · Python analytics/export · başka güvenli read pipeline |

> **Kural:** Sırf ileride Day Editor data okuyacak diye playtest client'ına geniş Firestore
> read izni **verilmeyecek.** Admin SDK ve service account'lar Security Rules'ı zaten bypass
> eder — client'ın kuralını gevşetmeye hiçbir zaman gerek olmayacak.

Şimdilik implement edilmiyor; Step 7'ye mimari not olarak bağlandı.

---

## N. Implementation Phases

Sıralama: kimlik ve run telemetry'si Firebase'den **önce**, `LogTelemetrySink` ile çalışır
hale getiriliyor. Böylece Firebase kurulumunda bir şey ters giderse "sorun Firebase'de mi
telemetry mantığında mı" sorusu hiç sorulmaz. Firebase smoke testi yine en başta kalıyor.

### Step 1 — Firebase smoke test (atılabilir kod)

- **Goal:** Unity'den Firestore'a yazabildiğimizi kanıtlamak. Başka hiçbir şey.
- **Added:** `Assets/Scripts/Telemetry/FirebaseSmokeTest.cs` (geçici, Step 4'te silinir)
- **Modified:** `ProjectSettings.asset` (Android/iOS identifier — §G.0)
- **Behavior:** init → anonymous sign-in → `smoke_test/{guid}` yaz → logla.
- **How To Test:** Editor'de Play → Console'da UID + "write ok" → Firebase Console'da doküman.
  Sonra **Android cihazda development build** ile aynı test. *(Offline/uçak modu testi yok — §L.1.)*
- **Rollback Risk:** Düşük, ama **en yüksek belirsizlik burada.** Gameplay'e sıfır dokunuş;
  SDK import'u geri alınabilir. Asıl risk zaman: Unity 6.3 + EDM4U + Gradle uyumu (§O.1).

### Step 2 — Kimlik katmanı (Firebase yok)

- **Goal:** installationId / playerId üretimi, kalıcılığı ve reset akışı — tamamen yerel.
- **Added:** `Systems/TelemetrySystem/` → asmdef, `TelemetryIdentity.cs`,
  `TelemetryIdentityStore.cs`, `TelemetryIds.cs`; `Tests/EditMode/TelemetryIdentityTests.cs`
- **Modified:** `Scripts/Debug/SROptions.Expo.cs` (Telemetry kategorisi)
- **Behavior:** İlk açılışta `telemetry_identity.json` oluşur. `Delete Save File` ona dokunmaz.
  `Reset Game + New Test Player` yalnızca main screen'de çalışır; day içinde uyarı loglar.
- **How To Test:** EditMode: store round-trip, versiyon reddi, `NewPlayer` installationId'yi korur.
  Elle: `Delete Save File` → playerId **aynı**; `Reset Game + New Test Player` → playerId
  **değişti**, installationId aynı, oyun Day 1'de. **Day içindeyken reset düğmesi hiçbir şey
  yapmamalı.** Dosyayı `cat` ile doğrula.
- **Rollback Risk:** **Çok düşük.** Yeni dosyalar + `SROptions`'a ekleme. Gameplay'e sıfır dokunuş.

### Step 3 — Run telemetry, `LogTelemetrySink` ile

- **Goal:** Run yaşam döngüsünün tamamı ve **§E.5'teki dokuz senaryonun hepsi** Console'da doğru görünsün.
- **Added:** `RunTelemetryState.cs`, `RunStatus.cs`, `ITelemetrySink.cs`, `LogTelemetrySink.cs`,
  `PlaytestTelemetry.cs`; `Scripts/Telemetry/TelemetryBinder.cs`; `Tests/EditMode/RunTelemetryStateTests.cs`
- **Modified:** `Core/GameState.cs` (+2 property), **yeni** `Core/DayAttemptEnd.cs` (enum),
  `Systems/DayLifecycle/DayLifecycleManager.cs` (+1 satır),
  **`Bootstrap/GameManager.cs` (+2 satır: `RetryDay` ve `ReturnToMainScreenAbandoningDay` ilk satırları)**,
  `Scenes/SampleScene.unity` (`TelemetryBinder` component'i + `host` sürüklemesi)
- **How To Test — §E.5'in her senaryosu ayrı ayrı:**
  - A: Day tamamla → `completed`
  - B: canları tüket → Game Over Retry → eski run **`failed`**, yeni runId açıldı
  - C: canları tüket → **Gem Continue** → tamamla → **tek run, `completed`**
  - D: canları tüket → **Gem Continue** → Main Menu → **`quit`, `failed` DEĞİL**, `livesDepletedCount == 1`
  - E: canlar dururken Main Menu → `quit`
  - F: Editor'de play mode'u durdur → run `in_progress` kalır
  - G: (cihazda) arka plana al → geri dön → tamamla → **aynı runId**
  - H: Day tamamla → popup'tan Retry → yeni runId
  - I: Day tamamla → Next Day → yeni runId, yeni `dayContentIndex`
  - **Mevcut EditMode testlerinin tamamı çalıştırılmalı** — `DayLifecycleManagerTests`
    `ResetForNewDay`'i 4 kez çağırıyor, `LivesSystemTests` D-103 sıralamasını koruyor.
- **Rollback Risk:** **Orta** — projenin tek gameplay dokunuşu burada. Üç publish satırı da yeni,
  abonesi olmayan event'ler yayıyor; hiçbir mevcut davranış değişmiyor. Sahne değişikliği git'te görünür.

### Step 4 — `FirestoreTelemetrySink`

- **Goal:** Aynı run'lar Firestore'a düşsün.
- **Added:** `Scripts/Telemetry/FirebaseBootstrap.cs`, `Scripts/Telemetry/FirestoreTelemetrySink.cs`
- **Deleted:** `FirebaseSmokeTest.cs`; Console'dan `smoke_test` koleksiyonu
- **Modified:** `SROptions.Expo.cs` (Telemetry State + write sayaçları + Force Snapshot)
- **Behavior:** Init başarılıysa gerçek sink; değilse `LogTelemetrySink`'e düşer ve oyun devam eder.
- **How To Test:** Cihazda bir Day oyna → doküman doğru alanlarla. Senaryo D'yi cihazda tekrarla ve
  Firestore'da `status == "quit"` olduğunu gör. Day'i yarıda bırak (uygulamayı öldür) → doküman
  `in_progress` olarak son heartbeat'le kalır. *(Offline/uçak modu testi yok — §L.1.)*
- **Rollback Risk:** **Düşük** — sink değiştirmek tek satır.

### Step 5 — Gameplay metriklerinin genişletilmesi

`boosterUses`, `attemptNumber`. **Modified:** `RunTelemetryState`, `TelemetryBinder`
(`ChargesChanged` aboneliği), `TelemetryIdentity` (kimlik dosyası v2).
`continuesUsed` **gerekmiyor** — §F.4 ile türetiliyor; açık alan istenirse burada eklenir.
**Rollback Risk:** Düşük. Firestore şemasız.

### Step 6 — Ticket-level telemetry *(opsiyonel)*

`runs/{runId}/tickets/{index}` alt-koleksiyonu. Sadece "hangi bilet zorluyor" sorusu
gerçekten sorulmaya başlandığında.

### Step 7 — Balancing entegrasyonu

Firestore'dan Day bazında toplu istatistik çeken bir araç; `docs/unity-balancing-simulator-integration-spec.md`'deki
predicted difficulty ile karşılaştırma. **Ayrı bir plan konusu.**

> **Mimari not (§M.5):** bu adım **oyun client'ının Firestore read yetkisini değiştirmez.**
> Okuma ayrı ve güvenli bir kanaldan yapılır — editor-only trusted tool, Firebase Admin SDK,
> service account kullanan bir script veya Python export pipeline'ı. Client `read: false` kalır.

---

## O. Risks

| # | Risk | Değerlendirme / azaltma |
|---|---|---|
| O.1 | **Unity 6.3 + Firebase SDK 13.9 uyumu** — Unity 6.x'in Gradle/AGP değişiklikleri EDM4U ile sürtüşebilir | **En yüksek belirsizlik.** Step 1 bu yüzden bir smoke test ve gerçek cihazda doğrulanıyor. Azaltma: custom Gradle template'leri, EDM4U'yu SDK ile gelen sürümde tutmak |
| O.2 | **.NET Standard 2.1** — Firestore geçmişte bu profilde sorun çıkardı | Step 1'de doğrulanır. Gerekirse .NET Framework profiline geçmek — **tüm projeyi** etkiler, erken test şart |
| O.3 | **Android/iOS identifier serialize edilmemiş** | §G.0 ilk adım. Firebase Console'a girmeden önce yapılmalı |
| O.4 | **`bundleVersion: 1.0`** — her build aynı sürümle gelirse build karşılaştırması yapılamaz | Her playtest build'inden önce artırma alışkanlığı |
| O.5 | Save reset kimliği götürür | **Çözülmüş** — ayrı dosya bu riski yapısal olarak ortadan kaldırıyor |
| O.6 | Duplicate run | **Çözülmüş** — `runId` doküman ID'si + `SetAsync(MergeAll)` |
| O.7 | Kaçırılan run sınırı — `RetryCompletedDay` event yaymıyor | `DaySessionStarted`'ın var olma sebebi. Step 3'ün senaryo H testinde |
| **O.8** | **Yanlış `failed` ataması** — rev 1'in heuristic'i Continue almış oyuncuları yanlış etiketlerdi (ve aslında `IsAwaitingContinue` terminal anda hep `false` olduğu için **her** run'ı `quit` yapardı) | **Çözülmüş** — §E.3. Publish'ler terminal metotların **ilk** satırında; §E.5-C ve §E.5-D Step 3'ün zorunlu testleri |
| **O.9** | **Publish satırının yanlış yere konması** — `RetryDay`/`ReturnToMainScreenAbandoningDay` içinde `LivesManager.RefillForNewDay()`'den sonraya düşerse bayrak temizlenmiş olur ve sessizce her run `quit` olur | Kodda satırın üstüne bunu açıklayan bir yorum; §E.5-B testi bu hatayı anında yakalar (`failed` yerine `quit` görülür) |
| O.10 | **Unity main thread** — Firebase continuation'ları arka planda | Sadece `ContinueWithOnMainThread`. Projenin **ilk async kodu**, code review'da özellikle bakılmalı |
| O.11 | SRDebugger sadece development build'de | Playtest build'leri Development Build olacak (kullanıcı onayladı) |
| O.12 | EventBus senkron ve cascade hassas | Telemetry handler'ları **sadece RAM sayacı artırır**, hiçbir şey publish etmez, hiçbir I/O başlatmaz |
| O.13 | Editor play mode datası playtest datasını kirletir | `environment: "editor"` etiketi |
| O.14 | Build boyutu | Firestore + Auth Android'de ~3-5 MB. Playtest için önemsiz |
| O.15 | `SampleScene.unity` elle düzenleniyor | Tek component + tek sürükleme. İzole ve geri alınabilir |
| O.16 | `DayAttemptEnd` enum'u `Core`'a giriyor | 4 satırlık bir enum; `DayFailureCause`'un `DayLifecycle`'da olmasıyla aynı desen. `Core` zaten `GameState`'in evi |

---

## P. Open Questions / Decisions

**rev 1'in sekiz maddesinin tamamı kullanıcı tarafından cevaplandı ve plana işlendi:**

| | Konu | Karar |
|---|---|---|
| P.1 | Status modeli | **4 değer onaylandı.** Semantikler §E.4'te; `failed` artık `LivesDepleted`'a değil terminal sinyale bağlı |
| P.2 | Editor telemetry'si | **`environment: "editor"`**, toggle yok |
| P.3 | Playtest build tipi | **Development Build**, ayrı production UI yok |
| P.4 | Default playerId | **Random `P_72AB19`**, SRDebugger'dan elle `T001` override |
| P.5 | Reset day içinde | **Reddedilir**, uyarı loglanır (rev 1'in önerisinden daha güvenli olan seçenek) |
| P.6 | Canonical Day anahtarı | **`dayContentIndex`** |
| P.7 | Discard metriği | **Kaldırıldı** — oyunda mekanik yok, sırf telemetry için event açılmayacak |
| P.8 | Heartbeat | **15 saniye**, Inspector'dan ayarlanabilir |

### Açık kalan tek soru

**P.9 — `DayAttemptEnded` publish'ini kim yapmalı?**

Plan bunu `GameManager`'ın iki metodunun ilk satırına koyuyor ve payload'ı
`State.IsAwaitingContinue`'dan okuyor (§E.3). Alternatif, popup'ların bunu **söylemesi**
(`RetryDay`'in `givingUpOnAttempt` parametresi gibi açık bir argüman):

| | **Seçilen: `GameManager` bayrağı okur** | Alternatif: popup'lar söyler |
|---|---|---|
| Dokunulan dosya | 1 (`GameManager`) | 3 (`GameManager`, `GameOverPopupView`, `SettingsPopupView`) |
| Doğruluk | Bayrağın anlamı D-135 tarafından "was the day lost?" olarak belgelenmiş — bizim sorduğumuz soru bu | Aynı derecede doğru |
| Kırılganlık | Publish satırı `RefillForNewDay`'den sonraya kayarsa sessizce bozulur (§O.9) | Kayma riski yok |
| D-135 ile ilişki | D-135 bayrağı **farklı** bir soru için okumayı reddetti, bu soruyu değil | D-135'in desenini birebir izler |

**Önerim seçilen yol** — tek dosya, ve §E.5-B testi kırılganlığı anında yakalıyor.
Ama D-135'in desenine daha sadık olmasını tercih ederseniz üç dosyaya yayarız.

**Bu tek soru dışında plan uygulanmaya hazır.**
