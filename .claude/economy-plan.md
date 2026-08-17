# Economy sağlamlaştırma planı

<!-- 2026-08-17 tarihli ekonomi incelemesinin çıktısı. Her adım tek bir
     preflight + APPROVE döngüsüdür; adımlar sırayla yürütülür ve bir adım
     bitmeden sonrakine geçilmez. Bir adım bittiğinde buradaki kutusu
     işaretlenir. Bu dosya plan tutanağıdır, mimari otorite değil —
     kalıcı kararlar bittiğinde `.claude/decisions.md`'ye D-004 olarak
     yazılır (Adım 7). -->

## Amaç

İncelemede çıkan altı iş kalemini kapatmak:

| # | Sorun | Adım |
|---|---|---|
| A | Harita boşluğu: 5 sistem blueprint/index'te yok | Adım 0 |
| B | SoftMoney/Gems'in tek yazıcısı yok (invariant ihlali) | Adım 1 |
| C | Başarısız günde SoftMoney silinmiyor → para farmı | Adım 2 |
| D | `RetryCompletedDay` ödenen Continue'yu iade ediyor | Adım 2 |
| E | Xp/Level'ın ikinci yazıcısı var (`GameManager.Awake`) | Adım 3 |
| F | Para ve Gem kalıcı değil (her oturum sıfırlanıyor) | Adım 4 |
| G | Ölü/yanlış yorumlar + sıfıra bölme riski | Adım 5 |

Kapsam dışı (bilerek — bu planda yok, kaybolmasın diye kayıtta):
yıldız eşiklerinin ikisi de 0 olması, Gem'in hiçbir kazanç yolunun
olmaması, negatif "Tips" satırı, impatient Lightning penceresi çakışması.
Bunlar ayrı birer içerik/tasarım kararı; istenirse ayrı plan açılır.

---

## Sözleşme: "gün denemesi atomiktir"

Adım 2 ve 4'ün tamamı tek bir kuraldan türer. Kuralı burada bir kez
yazıyoruz, adımlar buna atıf yapar:

> Bir günün ekonomik sonucu, gün **başarıyla tamamlanana kadar** geçici
> sayılır. Gün başarısız olur ya da gönüllü olarak baştan oynanırsa, o
> denemede **kazanılan** her şey (SoftMoney, Xp, Level) geri alınır;
> o denemede **harcanan** her şey (Continue bedeli) geri alınmaz.

Formül olarak, geri alma anında:

```
SoftMoney = max(0, günBaşıSoftMoney - buGünHarcanan)
Gems      = max(0, günBaşıGems      - buGünHarcanan)
Xp/Level  = günBaşıProfil            (zaten böyle çalışıyor)
```

Bu tek kural hem C'yi (kazanç silinmiyordu) hem D'yi (harcama iade
ediliyordu) kapatır ve XP'nin bugünkü davranışıyla simetrik olur.

**Açık karar D1 — `max(0, ...)` kırpması.** Oyuncu gün içinde kazandığı
parayla Continue alırsa (gün başı 100, gün içi kazanç 200, Continue 250),
geri alma sonucu eksiye düşer. Kırpma bunu 0'a çeker, yani oyuncu gün
başındaki 100'ü de kaybeder. Alternatif: Continue'yu yalnızca *gün başı
bakiyeden* ödenebilir yapmak (kazanılan para gün bitene kadar
harcanamaz). Öneri: **kırpma** — daha az kural, oyuncuya sürprizi
"riskli continue" olarak okunabilir. Adım 2'ye başlarken onaylanacak.

---

## Adım 0 — Harita onarımı (kod yok) ✅ 2026-08-17

**Neden önce:** `procedures/locate.md` adım 1–2, ekonomiyle ilgili hiçbir
soruyu şu an cevaplayamıyor. Sonraki her adımın preflight'ı bu haritadan
besleniyor.

Diskte var olduğu, `GameManager`'ın bağımlı olduğu, ama `blueprint.md` ve
`index.md`'de satırı olmayan sistemler:

- `EconomySystem` — bahşiş/hız/sabır formülü — depends on: -
- `ProgressionSystem` — Xp/Level/SoftMoney/Gem sahipliği + profil kalıcılığı — depends on: -
- `LivesSystem` — can kaybı + ücretli Continue — depends on: - *(Adım 1'de ProgressionSystem eklenecek)*
- `DayLifecycle` — gün sonu fiş muhasebesi (BaseTip/Tips/başarısız) — depends on: EconomySystem
- `TraySystem` — tepsi içeriği + parti doğrulama — depends on: -

Yapılacaklar:
1. `.claude/blueprint.md` → "Systems and dependencies" bölümüne 5 satır.
2. Codemap'lerde bu sistemlere ait satırların `sys: ?` alanlarını doldur,
   `MISSING-role` rollerini yaz (`codemap-core.md`, `codemap-ui.md`,
   `codemap-editor.md`). En az şu dosyalar: `EconomyCalculator.cs`,
   `EconomyConfig.cs`, `LevelManager.cs`, `PlayerProfile.cs`,
   `PlayerProfileStore.cs`, `LivesManager.cs`, `LivesConfig.cs`,
   `DayLifecycleManager.cs`, `TrayManager.cs`, `TraySlot.cs`,
   `GameState.cs`, `GameConfig.cs`, `LevelProgressionConfig.cs`,
   ilgili asmdef'ler ve `EconomySystemTests.cs`/`LivesSystemTests.cs`/
   `ProgressionSystemTests.cs`/`PlayerProfileStoreTests.cs`/
   `DayLifecycleManagerTests.cs`, UI tarafında `SoftMoneyView.cs`,
   `GemsView.cs`, `LevelView.cs`, `XpBarView.cs`,
   `DayCompletePopupView.cs`, `GameOverPopupView.cs`.
3. `python3 .claude/hooks/build_index.py` → index'te 6 değil 11 sistem.
4. `python3 .claude/hooks/check_blueprint.py` → 0 ERROR.

**Bitti kriteri:** `index.md`'de EconomySystem/ProgressionSystem/
LivesSystem/DayLifecycle/TraySystem satırları var; ekonomiyle ilgili
hiçbir codemap satırında `sys: ?` kalmadı.
**APPROVE gerekir mi:** Hayır (sadece `.md`).

**Sonuç (2026-08-17):** `index.md` 6 → **11 sistem, 0 unmapped**; beş
yeni sistemin beşi de `OK`. `check_blueprint.py` → **0 error**, 2 warning
(ikisi de kapsam dışı kalıntı: eklenti/diğer sistemlerin `sys: ?`
satırları ve `03c037f`'ten kalan 8 `STALE`). `MISSING-role` sayacı:
core 68 → 44, ui 14 → 8, editor 5 → 4. 24 codemap satırı elle dolduruldu
(rol/sys/dep/used/crit). `dep?:` taslaklarında yalnızca yorum metninde
geçen sahte bağımlılıklar (`PlayerProfileStore → GameState`,
`LivesManager → TrayManager/TicketSlotManager`, `TraySystemTests →
GameManager/BoardDistributor` vb.) `dep:`'e terfi ederken ayıklandı.
`GameState.cs`/`GameConfig.cs` bilinçli olarak `sys: ?` bırakıldı —
`FoodCatalog` satırındaki mevcut karar notuyla aynı gerekçe (dört
sistemin ortak tükettiği paylaşımlı Core/Data'ya tek sahip atanmaz);
rol/dep/used/crit alanları yine de dolduruldu.

**Yan not:** bu adım sırasında arka plan oturumu için worktree izolasyon
koruması devreye girdi ve paylaşılan checkout'a yazmayı engelledi.
`.claude/settings.json`'a `worktree.bgIsolation: "none"` eklendi — bu,
dosyanın kendi `_worktree_deny` notuyla ve `permissions.deny`'daki
mevcut `Agent(isolation:worktree)` reddiyle aynı gerekçe: worktree
altında `CLAUDE_PROJECT_DIR` ve preflight manifest yolları başka bir
kopyayı gösterdiği için preflight/codemap guard'ları doğrulama yapamaz.

---

## Adım 1 — `Wallet`: SoftMoney/Gems için tek yazıcı ⬜

Sorun B. Bugün `SoftMoney`'nin üç yazıcısı var: `GameManager`
payout (`GameManager.cs:171`), `GameManager` rollback (`:242`),
`LivesManager` continue (`LivesManager.cs:54,66`). Root `CLAUDE.md`
invariantı: *"Every piece of data has a single writer."*

**Çözüm şekli.** Proje `CLAUDE.md` Bölüm 5 zaten
`ProgressionSystem/ // XP/Level/Gem/SoftMoney` diyor — para oraya ait.
Yeni sınıf: `Assets/Scripts/Systems/ProgressionSystem/Wallet.cs`.

```csharp
public class Wallet            // SoftMoney + Gems'in TEK yazıcısı
{
    public void EarnSoftMoney(int amount);
    public bool TrySpendSoftMoney(int cost);   // yetmezse false, hiçbir şey harcamaz
    public bool TrySpendGems(int cost);
    public void CaptureDayStart();             // bakiye snapshot'ı + harcama sayaçları sıfırlanır
    public void RevertToDayStart();            // "sözleşme" formülü (Adım 2'de dolar)
}
```

Yazma yetkisinin derleyici tarafından zorlanması:
- `GameState.SoftMoney` / `Gems` / `Xp` / `Level` setter'ları
  `public` → **`internal`**.
- Yeni dosya `Assets/Scripts/Core/AssemblyInfo.cs`:
  `[assembly: InternalsVisibleTo("ExpoTheExplorer.Systems.ProgressionSystem")]`
- Böylece Bootstrap, LivesSystem, UI ve diğer her assembly için bakiye
  ataması **derlenmez hale gelir**; tek yol `Wallet`/`LevelManager`.

Dokunulan yerler:
- `Core/GameState.cs` — setter görünürlüğü + yorum güncellemesi.
- `Core/AssemblyInfo.cs` — yeni.
- `ProgressionSystem/Wallet.cs` — yeni.
- `LivesSystem/LivesManager.cs` — `GameState` yerine `Wallet` üzerinden
  harcar; `TryContinueWithSoftMoney/Gems` gövdesi `wallet.TrySpend...`'e
  iner. Davranış birebir aynı kalır.
- `LivesSystem/…asmdef` — `ExpoTheExplorer.Systems.ProgressionSystem`
  referansı eklenir. **Yeni ok: LivesSystem → ProgressionSystem** (tek
  yönlü, döngü yok; blueprint'te Adım 0 satırı bu adımda güncellenir).
- `Bootstrap/GameManager.cs` — `Wallet` kurulur, `OnTicketDelivered`
  içindeki `State.SoftMoney +=` → `wallet.EarnSoftMoney(...)`.
- `Tests/EditMode/LivesSystemTests.cs` — doğrudan `state.SoftMoney = 500`
  yerine cüzdan üzerinden kurulum.

**Bu adımda davranış değişmez** — sadece yazma yolu teke iner. Böylece
Adım 2'nin hata yüzeyi küçülür.

**Bitti kriteri:** `grep -rn "State.SoftMoney *=" Scripts` yalnızca
`Wallet.cs` içinde eşleşir; EditMode testleri geçer.
**APPROVE gerekir mi:** Evet (`.cs` + `.asmdef`).

---

## Adım 2 — Gün-atomik para kuralı ⬜

Sorunlar C ve D. Yukarıdaki **sözleşme** burada koda dönüşür.

`Wallet` içine:
- `dayStartSoftMoney`, `dayStartGems` (snapshot)
- `softMoneySpentThisDay`, `gemsSpentThisDay` (harcama defteri;
  `TrySpend...` başarılı olduğunda artar)
- `CaptureDayStart()` → snapshot al, defterleri sıfırla
- `RevertToDayStart()` → `max(0, snapshot - harcanan)` *(karar D1)*

`GameManager` tarafında bağlanacak dört nokta (üçü zaten var):

| Olay | Bugün | Sonra |
|---|---|---|
| `Awake` / `AdvanceToNextDay` | `CaptureDayStartSnapshot()` | + `wallet.CaptureDayStart()` |
| `OnDayRetried` (can bitti, ücretsiz retry) | sadece `LevelManager.DiscardToLastCommitted()` | + `wallet.RevertToDayStart()` ← **C'nin çözümü** |
| `RetryCompletedDay` (gönüllü redo) | `State.SoftMoney = dayStartSoftMoney` | `wallet.RevertToDayStart()` ← **D'nin çözümü** |
| `OnDayCompleted` | `LevelManager.CommitProgress()` | + para commit'i (Adım 4) |

`GameManager.dayStartSoftMoney` alanı silinir — artık `Wallet`'ın işi.

**Açık karar D2 — orkestrasyon.** Yukarıdaki tabloda `GameManager` iki
nesneyi (Wallet + LevelManager) elle senkron tutuyor; biri unutulursa
atomiklik sessizce bozulur. Alternatif: `ProgressionSystem` içine
`PlayerProgressService` koyup dört olayı tek nesneye indirmek
(`CaptureDayStart / CommitDay / DiscardAttempt / RevertCompletedDay`),
Wallet ve LevelManager onun altına girer. Öneri: **önce tablo hâliyle
yap + davranışı testle çivile**; `PlayerProgressService` gerçekten üçüncü
bir çağrı noktası çıkarsa Adım 4'te tekrar değerlendirilir (şimdi
yapmak `abstraction-level.md` açısından erken).

Yeni testler (`Tests/EditMode/WalletTests.cs`):
- Gün içi kazanç + retry → bakiye gün başına döner.
- Gün içi Continue harcaması + retry → harcama **iade edilmez**.
- Kazanılan parayla alınan Continue + retry → D1 kararına göre 0'a kırpılır.
- `RetryCompletedDay` sonrası bakiye ile `RetryDay` sonrası bakiye aynı kuralı verir.

**Bitti kriteri:** yukarıdaki dört test yeşil; `GameManager`'da doğrudan
para ataması kalmadı.
**APPROVE gerekir mi:** Evet.

---

## Adım 3 — Xp/Level için tek yazıcı ⬜

Sorun E. `GameManager.Awake:90-91` profili yükledikten sonra
`State.Xp = profile.Xp; State.Level = profile.Level;` diyerek
`LevelManager`'ı atlıyor.

- `LevelManager`'a `ApplyProfile(PlayerProfile profile)` eklenir (ya da
  mevcut kurucu profili doğrudan state'e uygular).
- `GameManager.Awake` bu iki satırı bırakır, `LevelManager`'ı profil
  yüklendikten **sonra** kurar ve uygulamayı ona devreder.
- Adım 1'deki `internal` setter zaten bunu derleme hatasıyla zorlayacak;
  bu adım o hatayı doğru şekilde kapatmaktır.

**Bitti kriteri:** `Xp`/`Level`'a yazan tek dosya `LevelManager.cs`.
**APPROVE gerekir mi:** Evet.

---

## Adım 4 — Para ve Gem kalıcılığı + profil versiyonu ⬜

Sorun F. Bugün `PlayerProfile` sadece `Xp`/`Level` taşıyor; SoftMoney her
oturumda 250'ye, Gems 100'e sıfırlanıyor.

Ayrıca root `CLAUDE.md` invariantı: *"Save data carries a version number;
unversioned saves are never written."* — `PlayerProfile`'da **versiyon
alanı yok**. Şemayı zaten değiştirdiğimiz an bunu eklemenin tam yeri.

```csharp
[Serializable]
public class PlayerProfile
{
    public int Version;      // yeni — yazılan her profil v1
    public int Xp;
    public int Level;
    public int SoftMoney;    // yeni
    public int Gems;         // yeni
}
```

- **Migrasyon:** diskteki eski dosyada `Version == 0` ve para alanları
  yok/0. `PlayerProfileStore.Load` v0 gördüğünde Xp/Level'ı korur,
  SoftMoney/Gems'i `GameConfig` başlangıç değerleriyle doldurur ve v1
  olarak işaretler. `JsonUtility` eksik alanı sessizce 0 bıraktığı için
  "0 para" ile "alanı olmayan eski kayıt" ayrımı **yalnızca** `Version`
  ile yapılabilir — bu yüzden versiyon alanı opsiyonel değil.
- Commit tetikleyicisi: **`OnDayCompleted`** (sözleşme gereği; gün
  başarısız bitince zaten hiçbir şey yazılmaz, oyundan çıkılırsa o günün
  kazancı gider — XP'nin bugünkü davranışıyla aynı).
- `RevertToDayStart` (gönüllü redo) diske de yazar; bugün
  `LevelManager.RevertToDayStart` böyle yapıyor, para da aynı yolu izler.
- `GameState` kurucusu artık başlangıç parasını `GameConfig`'ten değil,
  yüklenen profilden almalı → tohumlama sırası `GameManager.Awake`'de
  netleştirilir (profil yükle → state'e uygula → `CaptureDayStart`).

Testler: `PlayerProfileStoreTests`'e v0→v1 migrasyon vakası; kaydet/yükle
turunda para alanlarının korunması; bozuk JSON'da mevcut davranışın
(varsayılana düşme) bozulmaması.

**Bitti kriteri:** oyunu kapat/aç → SoftMoney ve Gems korunuyor; eski
profil dosyası hatasız v1'e taşınıyor.
**APPROVE gerekir mi:** Evet.

---

## Adım 5 — Ölü yorumlar ve küçük sağlamlık ⬜

Sorun G. Hepsi yorum/koruma seviyesinde, davranış değişmiyor:

- `ProgressionSystem/PlayerProfileStore.cs:10-12` — "No caller invokes
  Save yet" artık yanlış (`CommitProgress`/`RevertToDayStart` yazıyor).
- `Bootstrap/GameManager.cs:85-87` — "Nothing calls Save() yet ... wired
  up in a later PR" aynı şekilde yanlış.
- `Data/DataScripts/LevelProgressionConfig.cs:8-12` — "This config has no
  consumer yet ... deferred to a later PR" yanlış; `LevelManager`
  tüketiyor.
- `Core/GameState.cs:156` — "persistence lands in a later PR" notu Adım
  4'ten sonra güncellenmeli.
- `Core/GameState.cs:149-152` — `TicketsDeliveredToday` için "nothing
  consumes this reactively yet" ifadesi `DayLifecycleManager` ışığında
  gözden geçirilir.
- `LevelManager.GetXpProgressRatio` — `XpToNextLevel[Level]` 0
  yazılırsa sıfıra bölme (`fillAmount = NaN/∞`). Sıfır/negatif eşikte
  1f dönülür.

**Bitti kriteri:** yukarıdaki altı nokta güncel; davranış testleri
değişmeden geçer.
**APPROVE gerekir mi:** Evet (`.cs`).

---

## Adım 6 — Doğrulama ⬜

- Unity EditMode test paketinin tamamı çalıştırılır.
- Bilinen kırılgan testler (`TraySystemTests.TryAddItem_WrongItem`,
  `BoardDistributionTests` ExtremeLambda) regresyon sayılmaz — ayrı not.
- Elle senaryo: (1) günü kazan → para kalıcı, (2) canı bitir + retry →
  gün içi kazanç gitti, ödenen Continue geri gelmedi, (3) günü kazan +
  Retry → aynı kural, (4) oyunu kapat/aç → bakiye duruyor.

---

## Adım 7 — Kayıt ⬜

- `.claude/decisions.md` → **D-004**: gün-atomik ekonomi sözleşmesi,
  `Wallet`'ın tek yazıcı olması, profil v1 + migrasyon; `affects:` alanı
  dokunulan tüm dosyalarla.
- Proje `CLAUDE.md`: "Progression" bölümüne SoftMoney'in de XP gibi
  gün-şartlı commit edildiği ve harcamanın iade edilmediği yazılır
  (Bölüm 3'te bugün yalnızca XP için yazıyor).
- Codemap satırları + `build_index.py` + `check_blueprint.py` tekrar.
- Postflight (`gates/postflight.md` formatı).

---

## Onay bekleyen kararlar

| Kod | Karar | Öneri | Ne zaman |
|---|---|---|---|
| D1 | Geri almada `max(0, ...)` kırpması mı, kazanılan parayla Continue'yu engellemek mi? | Kırpma | Adım 2 başı |
| D2 | `PlayerProgressService` şimdi mi, gerekirse sonra mı? | Sonra | Adım 4 başı |
