# Economy sağlamlaştırma planı

> **2026-08-18 — XP/Level sistemi kaldırıldı (`decisions.md` D-009).** Bu plan
> XP varken yazıldı, dolayısıyla iki yerde güncellendi: **Adım 3 tamamen
> geçersiz** (tek yazıcı sorunu, `Xp`/`Level` alanlarıyla birlikte ortadan
> kalktı) ve **Adım 4** artık boş bir `PlayerProfile`'ın üzerine inşa ediyor —
> profilde saklanan hiçbir şey kalmadığı için "migrasyon" yükü de yok, ama
> versiyon alanı zorunluluğu aynen duruyor. Adım 0'daki codemap listesinde
> geçen `LevelManager.cs` / `LevelProgressionConfig.cs` / `LevelView.cs` /
> `XpBarView.cs` / `ProgressionSystemTests.cs` dosyaları artık yok.

<!-- 2026-08-17 tarihli ekonomi incelemesinin çıktısı. Her adım tek bir
     preflight + APPROVE döngüsüdür; adımlar sırayla yürütülür ve bir adım
     bitmeden sonrakine geçilmez. Bir adım bittiğinde buradaki kutusu
     işaretlenir. Bu dosya plan tutanağıdır, mimari otorite değil —
     kalıcı kararlar bittiğinde `.claude/decisions.md`'ye D-010 olarak
     yazılır (Adım 8). -->

## Amaç

İncelemede çıkan altı iş kalemini kapatmak:

| # | Sorun | Adım |
|---|---|---|
| A | Harita boşluğu: 5 sistem blueprint/index'te yok | Adım 0 |
| B | ~~SoftMoney/Gems'in tek yazıcısı yok (invariant ihlali)~~ ✅ | Adım 1 |
| C | ~~Başarısız günde SoftMoney silinmiyor → para farmı~~ ✅ | Adım 2 |
| D | ~~`RetryCompletedDay` ödenen Continue'yu iade ediyor~~ ✅ | Adım 2 |
| E | ~~Xp/Level'ın ikinci yazıcısı var~~ — konusuz kaldı (D-009) | ~~Adım 3~~ |
| F | ~~Para ve Gem kalıcı değil (her oturum sıfırlanıyor)~~ ✅ | Adım 4 |
| G | Ölü/yanlış yorumlar + sıfıra bölme riski | Adım 5 |
| H | Her yemek aynı parayı ediyor; hız/sabır sabit bedeli de kısıyor | Adım 6 |

Kapsam dışı (bilerek — bu planda yok, kaybolmasın diye kayıtta):
Gem'in hiçbir kazanç yolunun olmaması, impatient Lightning penceresi
çakışması. Bunlar ayrı birer içerik/tasarım kararı; istenirse ayrı plan
açılır.

Listeden düşen iki madde:
- *Yıldız eşiklerinin ikisi de 0 olması* — artık geçersiz: yıldız,
  authored eşiklerden değil kaybedilen candan hesaplanıyor
  (`decisions.md` D-008, commit `a55b9a8`).
- *Negatif "Tips" satırı* — **2026-08-18 tarihli fiyatlandırma kararıyla
  kapanıyor.** Yeni model: her yemeğin kendi fiyatı var, teslimat kazancı
  `Sipariş Bedeli (sabit) + Bahşiş (değişken, bedele oranlı)`; hız/sabır
  yalnızca bahşişi çarpıyor ve bahşiş 0'ın altına inmiyor, dolayısıyla
  "Tips" satırı negatif olamıyor. Karar `ExpoTheExplorer/CLAUDE.md`
  ("Food Pricing / Order Value") ve GDD v0.8 Bölüm 9'da yazılı;
  uygulaması **Adım 6** olarak bu plana eklendi.

---

## Sözleşme: "gün denemesi atomiktir"

Adım 2 ve 4'ün tamamı tek bir kuraldan türer. Kuralı burada bir kez
yazıyoruz, adımlar buna atıf yapar:

> Bir günün ekonomik sonucu, gün **başarıyla tamamlanana kadar** geçici
> sayılır. Gün başarısız olur ya da gönüllü olarak baştan oynanırsa, o
> denemede **kazanılan** her şey (SoftMoney; D-009'dan önce Xp/Level de) geri alınır;
> o denemede **harcanan** her şey (Continue bedeli) geri alınmaz.

Formül olarak, geri alma anında:

```
SoftMoney = max(0, günBaşıSoftMoney - buGünHarcanan)
Gems      = max(0, günBaşıGems      - buGünHarcanan)
```

Bu tek kural hem C'yi (kazanç silinmiyordu) hem D'yi (harcama iade
ediliyordu) kapatır. (Kural aslında XP'nin davranışından türetilmişti;
XP gitti, kural kendi başına duruyor — D-009.)

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
- `ProgressionSystem` — SoftMoney/Gem sahipliği + profil kalıcılığı — depends on: - *(D-009'dan sonra bugünkü hâli yalnızca profil kayıt sınırı; para sahipliği Adım 1'de geliyor)*
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

## Adım 1 — `Wallet`: SoftMoney/Gems için tek yazıcı ✅ 2026-08-18

Sorun B. Bugün `SoftMoney`'nin üç yazıcısı var: `GameManager`
payout (`GameManager.cs:171`), `GameManager` rollback (`:242`),
`LivesManager` continue (`LivesManager.cs:54,66`). Root `CLAUDE.md`
invariantı: *"Every piece of data has a single writer."*

**Çözüm şekli.** Proje `CLAUDE.md` Bölüm 5 `ProgressionSystem/`'i oyuncu
profili/kalıcılık yeri olarak tanımlıyor — para oraya ait.
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
- `GameState.SoftMoney` / `Gems` setter'ları `public` → **`internal`**.
- Yeni dosya `Assets/Scripts/Core/AssemblyInfo.cs`:
  `[assembly: InternalsVisibleTo("ExpoTheExplorer.Systems.ProgressionSystem")]`
- Böylece Bootstrap, LivesSystem, UI ve diğer her assembly için bakiye
  ataması **derlenmez hale gelir**; tek yol `Wallet`.

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

**Sonuç (2026-08-18).** Üretim kodunda SoftMoney/Gems'e yazan **tek yer**
`Wallet.cs` (artı `GameState`'in kendi kurucusu, Core içi). Yazıcılar
kapatıldı: `GameManager` ödeme + `RetryCompletedDay`, `LivesManager`'ın iki
Continue'su. Zorlama derleyicide: setter'lar `internal`, yeni
`Core/AssemblyInfo.cs` yalnızca `ProgressionSystem` (+ `Tests.EditMode`)
friend.

Plandan sapan/eklenen noktalar:
- **Xp/Level çekincesi düştü.** Plan "setter'ları internal yapınca
  `GameManager.Awake`'in profil ataması derlenmez" diyordu; D-009 o kodu
  zaten sildiği için adım yalnızca SoftMoney/Gems'e indi ve gerçekten
  davranış-nötr kaldı.
- **`Tests.EditMode` de friend oldu.** `GameStateTests` setter'ın
  publish-on-change sözleşmesini test etmek için doğrudan yazıyor; bunu
  cüzdana çevirmek Core'un sözleşmesi yerine cüzdanı test etmek olurdu.
  `LivesSystemTests`'in 9 kurulum satırı yeni kurucuya taşındı.
- **`GameManager.dayStartSoftMoney` ve `CaptureDayStartSnapshot` silindi**
  (plan bunu Adım 2'ye yazmıştı); snapshot artık `Wallet`'ın içinde, iki
  çağrı yeri `wallet.CaptureDayStart()` diyor.
- **`RevertToDayStart` yalnızca SoftMoney'i geri alıyor** — Gems bilinçli
  olarak dokunulmadı, `WalletTests.RevertToDayStart_LeavesGemsUntouched`
  bunun tripwire'ı. Adım 2 o testi bilerek değiştirecek.
- `EarnSoftMoney` negatif/0 miktarı yok sayıyor, böylece kazanç yolu gizli
  bir harcama yoluna dönüşemiyor (davranış değişikliği değil: tek çağıran
  teslimat ödemesi ve o hiç negatif olamıyor).
- Yeni ok **LivesSystem → ProgressionSystem** blueprint'e işlendi
  (bu kez preflight'ta önceden beyan edilerek).
- 13 test: `WalletTests.cs` (yeni). **Unity'de koşulmadı** — bu oturumda
  Unity yok.

---

## Adım 2 — Gün-atomik para kuralı ✅ 2026-08-18

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
| `OnDayRetried` (can bitti, ücretsiz retry) | **handler yok** (D-009 sildi) | `wallet.RevertToDayStart()` ← **C'nin çözümü**; handler + abonelik yeniden kurulur |
| `RetryCompletedDay` (gönüllü redo) | `State.SoftMoney = dayStartSoftMoney` | `wallet.RevertToDayStart()` ← **D'nin çözümü** |
| `OnDayCompleted` | **handler yok** (D-009 sildi) | para commit'i (Adım 4); handler + abonelik yeniden kurulur |

`GameManager.dayStartSoftMoney` alanı silinir — artık `Wallet`'ın işi.

**Açık karar D2 — orkestrasyon.** *(D-009 notu: bu karar büyük ölçüde
kendiliğinden çözüldü — senkron tutulacak ikinci nesne olan `LevelManager`
artık yok, geriye tek yazıcı olarak `Wallet` kalıyor. Aşağıdaki gerekçe,
ileride profile ikinci bir alan (`CurrentDayIndex`) girdiğinde yeniden
geçerli olacağı için duruyor.)* Yukarıdaki tabloda `GameManager` iki
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

**Sonuç (2026-08-18).** `Wallet` artık `dayStartGems` snapshot'ı ve iki
harcama defteri (`softMoneySpentThisDay`/`gemsSpentThisDay`) tutuyor;
`RevertToDayStart` her iki para için `max(0, snapshot − harcanan)`
uyguluyor. `OnDayRetried` handler'ı ve `DayRetried` aboneliği yeniden
kuruldu (Sorun C'nin fiili çözümü buydu — olay yayınlanıyordu ama
dinleyicisi yoktu). `RetryCompletedDay` zaten `RevertToDayStart` çağırdığı
için Sorun D kendiliğinden kapandı.

Notlar:
- **Defter retry'de sıfırlanmıyor**, yalnızca `CaptureDayStart`'ta (gün
  başı / gün ilerleme). Aynı günün iki denemesinde alınan iki Continue de
  harcanmış sayılıyor ve ikisi de o günün tek taban çizgisine göre
  ölçülüyor.
- `TrySpend...` negatif maliyeti reddediyor (harcama, para ekleme yoluna
  dönüşemesin); sıfır maliyet hâlâ `true` dönüyor, yani "bedava Continue"
  authorable kalıyor.
- Adım 1'de tripwire olarak koyduğum `RevertToDayStart_LeavesGemsUntouched`
  testinin **beklenen sayısı değişmedi** (Gems'in kazanç yolu olmadığı için
  "dokunulmuyor" ile "harcama iade edilmiyor" bakiyeden ayırt edilemiyor);
  yalnızca adı ve gerekçesi değişti → `..._DoesNotRefundGemsSpentThisDay`.
- 7 yeni test. **Unity'de koşulmadı** — bu oturumda Unity yok.
- D2 (`PlayerProgressService`) hâlâ "sonra": senkron tutulacak ikinci nesne
  yok, `GameManager` tek cüzdanı çağırıyor.

---

## Adım 3 — ~~Xp/Level için tek yazıcı~~ ❌ GEÇERSİZ (2026-08-18)

Sorun E, XP/Level sisteminin tamamen kaldırılmasıyla ortadan kalktı
(`decisions.md` D-009): `GameState.Xp`/`GameState.Level` diye alanlar yok,
`LevelManager` yok, `GameManager.Awake`'de profil yükleme yok. Yapılacak
bir şey kalmadı — bu adım atlanır.

---

## Adım 4 — Para ve Gem kalıcılığı + profil versiyonu ✅ 2026-08-18

Sorun F. Bugün `PlayerProfile` **boş** (D-009'dan sonra `Xp`/`Level` alanları
da gitti) ve hiç kimse `Load`/`Save` çağırmıyor; SoftMoney ve Gems her
oturumda 0'a dönüyor. Yani bu adım artık "profili genişletmek" değil,
**kalıcılığı sıfırdan açmak** — bu yüzden `GameManager`'daki bağlama
(store'u kur, `Awake`'de yükle, commit tetiğine bağla) da bu adımın işi.

Ayrıca root `CLAUDE.md` invariantı: *"Save data carries a version number;
unversioned saves are never written."* — `PlayerProfile`'da **versiyon
alanı yok**. Şemayı zaten değiştirdiğimiz an bunu eklemenin tam yeri.

```csharp
[Serializable]
public class PlayerProfile
{
    public int Version;      // yeni — yazılan her profil v1
    public int SoftMoney;    // yeni
    public int Gems;         // yeni
}
```

- **Migrasyon yükü yok, versiyon zorunluluğu var.** Diskte kalmış olabilecek
  tek şey XP döneminden bir `player_profile.json`; içindeki `Xp`/`Level`
  alanlarını `JsonUtility` sessizce yok sayar, taşınacak bir değer yoktur.
  Ama root `CLAUDE.md` invariantı gereği ilk yazılan profil v1 olmalı ve
  `Version == 0` gören `Load`, dosyayı "tanımadığım eski kayıt" sayıp
  başlangıç değerlerine düşmelidir — `JsonUtility` eksik alanı sessizce 0
  bıraktığı için "0 para" ile "alanı olmayan eski kayıt" ayrımı **yalnızca**
  `Version` ile yapılabilir.
- **Hangi Day'de olunduğu da burada mı?** `CurrentDayIndex`'in kalıcılığı
  (DaySystem_Roadmap Q4) aynı dosyaya inecek ve şu an profildeki tek aday
  alan o. İkisini tek adımda yapmak, şema versiyonunu bir kez artırmak
  demek — ayrı ayrı yapılırsa v1 ve v2 diye iki migrasyon doğar.
- Commit tetikleyicisi: **`OnDayCompleted`** (sözleşme gereği; gün
  başarısız bitince zaten hiçbir şey yazılmaz, oyundan çıkılırsa o günün
  kazancı gider). Dikkat: bu handler ve aboneliği D-009'da silindi, yani
  bu adımda `GameManager`'a yeniden eklenecek.
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

**Sonuç (2026-08-18).** `PlayerProfile` = `Version` + `SoftMoney` + `Gems`.
`PlayerProfileStore.CurrentVersion = 1`, her `Save` damgalıyor.
`GameManager.Awake` yükleyip `wallet.ApplyPersistedBalances(...)` çağırıyor
(setter'lar `internal` olduğu için yükleme de cüzdandan geçmek zorunda);
`OnDayCompleted` handler'ı ve `DayCompleted` aboneliği geri kuruldu (D-009
silmişti), diske yazan tek yer o ve `RetryCompletedDay`.

Plandan sapan iki karar:
- **Versiyon politikası plandan farklı.** Plan "`Version == 0` görürse
  varsayılana düş" diyordu; ben `Load`'u **1..CurrentVersion kabul, 0 ve
  daha yeni red** yaptım. Planın formülasyonu v2 alanı eklendiği gün
  gerçek oyuncunun v1 cüzdanını silerdi — eski dosya okunabilir, sadece
  yeni alanı yoktur ve `JsonUtility` onu 0 bırakır. `0`'ı kabul etmek de
  olmaz: o zaman "versiyonsuz eski kayıt" ile "gerçekten 0 para" ayırt
  edilemez, ki versiyon alanının varlık sebebi tam olarak bu.
- **`CurrentDayIndex` bu adıma alınmadı.** Plan "iki migrasyon çıkmasın"
  diye bunları birleştirmeyi öneriyordu; versiyon alanı bir kez var
  olduğunda o gerekçe düşüyor (v1 dosyası alanı taşımaz → 0 → "Day 0'dan
  başla", ki doğru varsayılan bu). Oyunun bıraktığın günden devam etmesi
  bir oyun tasarımı kararı (DaySystem_Roadmap Q4), kullanıcıya bırakıldı.

Diğer notlar:
- `ApplyPersistedBalances` yükleme sonrası `CaptureDayStart` çağırıyor;
  yoksa oturumun ilk retry'ı cüzdanı 0'a çevirirdi. Negatif değerler
  (elle düzenlenmiş dosya) 0'a kırpılıyor.
- Başarısız gün **diske yazmıyor** — commit edilmiş bir şey yok. Gün
  ortasında çıkmak o günün kazancını kaybettirir, sözleşme bu.
- `Save`, verilen instance'a `Version` **yazıyor** (kopya damgalamak
  store'un payload'ı tanımasını gerektirirdi). Mevcut
  `Save_WhenCalledTwice_OverwritesRatherThanAppending` testi bu yüzden
  kırılıyordu, kaydettiği instance ile karşılaştıracak şekilde düzeltildi.
- 5 yeni store testi + 3 cüzdan testi. **Unity'de koşulmadı.**
- `fingerprint.md` Persistence maddesi ve `CLAUDE.md` Progression bölümü
  "hiçbir şey kalıcı değil" diyordu, ikisi de güncellendi.

---

## Adım 5 — Ölü yorumlar ve küçük sağlamlık ⬜

Sorun G. Hepsi yorum/koruma seviyesinde, davranış değişmiyor:

- ~~`PlayerProfileStore.cs` / `GameManager.cs` / `LevelProgressionConfig.cs`
  yorumları~~ — üçü de D-009'da kapandı: `LevelProgressionConfig` silindi,
  diğer ikisinin yorumları "hiçbir çağıran yok" durumunu doğru anlatacak
  şekilde yeniden yazıldı. Adım 4 kalıcılığı açtığında **tekrar** yanlış
  hale gelecekler; o adımın parçası olarak güncellenmeleri gerekiyor.
- `Core/GameState.cs` — `CurrentDayIndex`'in "persistence lands in a later
  PR" notu Adım 4'ten sonra güncellenmeli.
- `Core/GameState.cs:149-152` — `TicketsDeliveredToday` için "nothing
  consumes this reactively yet" ifadesi `DayLifecycleManager` ışığında
  gözden geçirilir.
- ~~`LevelManager.GetXpProgressRatio` sıfıra bölme riski~~ — konusuz kaldı,
  dosya D-009'da silindi.

**Bitti kriteri:** yukarıdaki maddeler güncel; davranış testleri
değişmeden geçer.
**APPROVE gerekir mi:** Evet (`.cs`).

---

## Adım 6 — Yemek fiyatlandırması: sabit bedel + oranlı bahşiş ⬜

Sorun H. 2026-08-18'de alınan tasarım kararının koda geçmesi. Karar metni
`ExpoTheExplorer/CLAUDE.md` → "Food Pricing / Order Value" + "Tip Decay —
the timer bar's segments" ve GDD v1.1 Bölüm 9'da yazılı; burada yalnızca
uygulaması var.

**Sözleşme.** Bir teslimatın kazancı iki parçadır ve yalnızca ikincisi
değişkendir:

```
Sipariş Bedeli = Σ (bilette istenen her yemeğin BasePrice'ı)     // sabit, garanti
Kalan oran     = kalan süre / biletin süre sınırı
Kademe         = kalan <= CriticalRatio ? Critical
               : kalan <= WarningRatio  ? Warning : Full
Bahşiş         = Sipariş Bedeli × Kademenin oranı
Teslimat       = Sipariş Bedeli + Bahşiş
```

| Kalan oran | Bar | Bahşiş oranı |
|---|---|---|
| `> 0.666` | yeşil | `TipRateFull` (0.20) |
| `0.333 – 0.666` | turuncu | `TipRateWarning` (0.15) |
| `<= 0.333` | kırmızı | `TipRateCritical` (0.10) |

**Üç kademe, ve eşikler barın renk eşiklerinin ta kendisi** — oyuncunun
gördüğü renk, aldığı bahşiş kademesi. Eşikler *oran* olduğu için sabır
tipine göre kendiliğinden ölçeklenir: 45 sn'lik sabırsız bilet her 15
saniyede, 150 sn'lik sabırlı her 50 saniyede bir kademe düşer. Sipariş
bedeli hiçbir kademede kısılmaz.

Ne sabır katsayısı (v0.9'da kaldırıldı) ne de eski hız kademesi (v1.1'de
kaldırıldı) formülde var; öğe sayısı da hiçbir eşiği ölçeklemiyor.

Kullanıcı kararı (2026-08-18): **iki oran `EconomyConfig`'e taşınır**, çünkü
parayı belirliyorlar. `TicketCardsView` oranları `gameManager.EconomyConfig`
üzerinden okur, görsel config'te yalnızca üç *renk* kalır — tek sayı, yani
bar ile cüzdan bir kademenin nerede başladığı konusunda ayrışamaz.
`TimerSegmentSeconds` **salt kozmetik** (bar üzerindeki bölme tikleri) ve
görsel config'te kalır; hiçbir ödemeye bağlanmaz. *(Bu, planın önceki
sürümündeki "her N dilimde bir üstel düşüş" kuralının yerini alır — o kural
kullanıcının 3'te-bir açıklamasıyla geçersiz kaldı.)*

Sabır tipi para tarafından çıktı; D-009'dan sonra geriye **tek** etkisi
kaldı: biletin süre sınırı (`TicketRuntimeSettings`, D-005). İkinci etkisi
olan XP çarpanı, XP sistemiyle birlikte silindi — yani sabır tipi artık
süre dışında hiçbir şeyi ödüllendirmiyor (bkz. D5 açık kararı).

Veri:
- ✅ **2026-08-18** dokuz yemeğin fiyatı Inspector'dan girildi ve diske
  yazıldı: Burger 14, Hotdog 8, üç milkshake 8, Fries 4, üç gazlı 3.
- ✅ **2026-08-18** `Data/DataScripts/FoodItemConfig.cs` — `basePrice` alanı
  (`int`, `[Min(0)]`, tooltip'li) + `BasePrice` property'si eklendi. Para her
  yerde `int`; bedeli de int tutmak teslimat başına ikinci bir yuvarlama
  kaynağı doğurmaz. `FoodItemConfigEditor` `DrawDefaultInspector()` çağırdığı
  için alan Inspector'da kendiliğinden görünüyor, editör dosyasına
  dokunulmadı. Henüz **okuyucusu yok** — formül dilimi aşağıda.
- `Data/DataScripts/EconomyConfig.cs` — **silinen:** `baseTipPerItem`, üç
  hız çarpanı (`lightningMultiplier`/`fastMultiplier`/`standardMultiplier`),
  iki eşik (`lightningSecondsPerItem`/`fastSecondsPerItem`), üç decay
  listesi ve `PatienceDecayStep` sınıfı. **Gelen beş alan:** `tipRateFull`
  (0.20), `tipRateWarning` (0.15), `tipRateCritical` (0.10),
  `warningRatio` (0.666), `criticalRatio` (0.333). Beşi de serialize,
  Inspector'dan ayarlanır; varsayılanlar dosyanın kendi "placeholder, not
  balanced" duruşuyla aynı.
- `Data/DataScripts/TicketCardVisualsConfig.cs` — `timerWarningRatio` ve
  `timerCriticalRatio` **çıkar** (ekonomiye taşındı). `timerSegmentSeconds`
  ve üç renk **kalır** — birincisi salt kozmetik.
- `Scripts/UI/TicketCardsView.cs` — `TimerFillColorFor` eşikleri
  `gameManager.EconomyConfig`'ten, renkleri görsel config'ten okur.
  `gameManager` referansı zaten serialize alan olarak var, dolayısıyla
  **yeni sahne wiring'i çıkmaz**. Day Editör önizlemesi bu iki oranı hiç
  okumuyor (yalnızca sprite/renk kullanıyor), o yüzden editör tarafında
  hiçbir uyarlama gerekmiyor.
- `Scripts/Bootstrap/GameManager.cs` — `EconomyConfig` getter'ı açılır
  (UI'ın dilim uzunluğunu okuyabilmesi için).
- Asset'ler: **dokuz** yemek fiyatlandırılır — `Burger/Food_Burger`,
  `Hotdog/Food_Hotdog`, `Sides/Food_Fries` ve altı içecek
  (`Beverages/Food_Cola`, `Food_Fanta`, `Food_Sprite`,
  `Food_BluberryMilkshake`, `Food_StawberryMilkshake`,
  `Food_VanillaMilkshake`) — artı `EconomyConfig.asset`'e `tipRate`.
  **Sayıları kullanıcı verir** — placeholder uydurulmaz (root CLAUDE.md
  invariantı: içerik verisi koddan değil veriden gelir). *(Plan ilk
  yazıldığında üç yemek vardı; `be8fa0d` ile Hotdog ve beş içecek eklendi.)*

Kod:
- `EconomySystem/EconomyCalculator.cs` — `CalculateTip` yukarıdaki
  formüle döner. `DeliveryTipResult` → **`DeliveryPayoutResult`**: artık
  yalnızca bahşiş taşımıyor. Alanlar `OrderValue` (int), `TipRateApplied`,
  `Tier` (yeni `TipTier` enum'u: `Full`/`Warning`/`Critical`), `TipRate`,
  `Tip`, `Total`. Kalkanlar: `BaseTip`, `TotalTip`,
  `PatienceDecayCoefficient`, `SpeedTier`, `SpeedMultiplier`.
  (`Tier`/`TipRate`, eski struct'ın `SpeedTier`'ı UI'a "Fast x1.5"
  gösterebilmek için taşımasıyla aynı gerekçe — ve bu kez kademe barın
  rengiyle birebir aynı şey.)
- **Hız kademesi makinesi silinir:** eski `SpeedTier` enum'u,
  `ResolveSpeedTier`, `ResolveSpeedMultiplier`.
- **Ölü sabır-decay makinesi silinir:** `EconomyCalculator.
  ResolvePatienceDecayCoefficient` + `ResolveDecaySteps`,
  `EconomyConfig`'in üç eğrisi (`impatientDecaySteps`/`normalDecaySteps`/
  `patientDecaySteps`) ve `PatienceDecayStep` sınıfı,
  `EconomyConfig.asset`'teki eğri verileri. Sabır artık bahşişe
  girmediği için bunların tek tüketicisi kalmıyor — bırakılırsa
  "hiçbir şeyi sürmeyen ayar" olur (D-007'de `BoardDistributionConfig`
  ile aynı hata).
- `Bootstrap/GameManager.cs` — `OnTicketDelivered` artık
  `RoundToInt(result.Total)` kazandırır (Adım 1'den sonra bu zaten
  `wallet.EarnSoftMoney`).
- `DayLifecycle/DayLifecycleManager.cs` — `totalBaseTipToday` →
  `totalOrderValueToday`; `OrdersDeliveredValue` = günün bedel toplamı,
  `TipsValue` = günün bahşiş toplamı. **Bu satır artık negatif olamaz**
  (eski modelde `TotalTip - BaseTip`, azalma < 1 iken eksiye düşüyordu).
- `UI/DayCompletePopupView.cs` — satır etiketleri aynı kalır, yalnızca
  okuduğu alan adları değişir.

Koruma: fiyatı 0 olan yemek bir içerik hatasıdır (bedava yemek), geçerli
varsayılan değil — sessizce 0 ödemek yerine görünür kılınır.

Testler:
- `EconomySystemTests` — (1) Sipariş Bedeli fiyatların toplamıdır, öğe
  sayısının değil (farklı fiyatlı üç öğeyle); (2) kalan oran eşiğin
  üstündeyken `Bahşiş = Bedel × TipRateFull`; (3) tam `WarningRatio`'da
  Warning kademesi (sınır dahil, `<=` semantiği barın renk mantığıyla
  aynı); (4) tam `CriticalRatio`'da Critical; (5) **aynı kalan saniye,
  farklı süre sınırı → farklı kademe** (oran tabanlı olmanın kilidi:
  45 sn'lik bilette 20 sn kalması Warning, 150 sn'likte Full);
  (6) en alt kademede bile `Total >= Bedel`; (7) sabır tipi *doğrudan*
  kazanca girmiyor (aynı süre sınırı + aynı kalan → aynı kazanç);
  (8) öğe sayısı hiçbir eşiği ölçeklemiyor ← v1.1'in kilidi.
- `DayLifecycleManagerTests` — `TipsValue` negatif olamaz.
- **Silinen testler:** `CalculateTip_BaseTip_ScalesWithRequiredItemCount`
  (öğe sayısı artık kazancı belirlemiyor; fiyat toplamı testiyle
  değişir) ve beş sabır-decay testi
  (`..._IsFullBeforeFirstThreshold`, `..._HoldsFlatBetweenSteps...`,
  `..._DropsAtNextThreshold...`, `..._ThresholdsScaleWithItemCount`,
  `..._PatienceTypeWithEmptyStepsList_DefaultsToFullTip`) ile
  `SetPatienceDecaySteps`/`DecayStepsFieldFor` yardımcıları.
- **Silinen testler (2. grup, v1.1):** dört hız kademesi testi
  (`..._IsLightningTier`, `..._IsFastTier`,
  `..._IsStandardTier_WithNoMultiplier`,
  `..._SpeedThresholds_ScaleWithItemCount_NotFixedSeconds`) — eşiklerin öğe
  sayısıyla ölçeklenmesi kuralı v1.1'de kalktı, yerine (8) numaralı test
  tam tersini çiviliyor.
- `DayTicketSlotManagerIntegrationTests` ve `TicketSystemTests` —
  `DeliveryTipResult`/`SpeedTier` kurdukları yerler yeni struct'a çevrilir.

**Adım 1'e bağımlı değil** (Adım 1 "parayı kim yazıyor", bu "ne kadar"
sorusu). Sıranın burada olması, Adım 1 sonrası ödemenin zaten tek
noktadan — `Wallet` — geçiyor olması içindir.

**Bitti kriteri:** `grep -rn "baseTipPerItem\|DecayStep\|PatienceDecay\|SpeedTier"`
boş; iki oran tek bir yerde (`EconomyConfig`) authored ve hem bar rengi hem
bahşiş oradan okuyor; `TimerSegmentSeconds` hiçbir ödeme yolunda geçmiyor;
dokuz yemek asset'inin
fiyatı yazılı (✅ girildi); yukarıdaki testler yeşil; hiçbir kod yolunda
sipariş bedeli çarpılmıyor, sabır tipi ve öğe sayısı kazanca hiç girmiyor.
**APPROVE gerekir mi:** Evet (`.cs`). `.asset` yazılmaz — Unity dört yeni
alanı koddaki varsayılanlarıyla kendi serialize eder.

---

## Adım 7 — Doğrulama ⬜

- Unity EditMode test paketinin tamamı çalıştırılır.
- Bilinen kırılgan testler (`TraySystemTests.TryAddItem_WrongItem`,
  `BoardDistributionTests` ExtremeLambda) regresyon sayılmaz — ayrı not.
- Elle senaryo: (1) günü kazan → para kalıcı, (2) canı bitir + retry →
  gün içi kazanç gitti, ödenen Continue geri gelmedi, (3) günü kazan +
  Retry → aynı kural, (4) oyunu kapat/aç → bakiye duruyor, (5) pahalı ve
  ucuz siparişi aynı hızda teslim et → kazanç farkı fiyat farkını
  yansıtıyor, (6) bir siparişi kasten geciktir → bahşiş 0'a iniyor ama
  yemeğin parası tam ödeniyor, fişteki "Tips" satırı negatif değil.

---

## Adım 8 — Kayıt ⬜

- `.claude/decisions.md` → **D-010**: gün-atomik ekonomi sözleşmesi,
  `Wallet`'ın tek yazıcı olması, profil v1 + migrasyon; `affects:` alanı
  dokunulan tüm dosyalarla. (Plan ilk yazıldığında "D-004", sonra "D-009"
  deniyordu; D-009'u XP/Level kaldırma kararı aldı, sıradaki boş id D-010.)
- `.claude/decisions.md` → **D-011**: yemek başına fiyat + sabit sipariş
  bedeli / oranlı bahşiş (Adım 6); `EconomyConfig.baseTipPerItem`'ın
  kaldırılması ve "Tips satırı negatif olamaz" sonucu.
- Proje `CLAUDE.md`: "Progression" bölümüne SoftMoney'in gün-şartlı commit
  edildiği ve harcamanın iade edilmediği yazılır. (Bölüm 3 bu kuralı eskiden
  XP için anlatıyordu; D-009 o metni sildi, yani kural sıfırdan yazılacak.)
- Codemap satırları + `build_index.py` + `check_blueprint.py` tekrar.
- Postflight (`gates/postflight.md` formatı).

---

## Onay bekleyen kararlar

| Kod | Karar | Öneri | Ne zaman |
|---|---|---|---|
| ~~D1~~ | Geri almada `max(0, ...)` kırpması mı, kazanılan parayla Continue'yu engellemek mi? | ✅ **Kırpma** — kullanıcı onayladı 2026-08-18 | ~~Adım 2 başı~~ |
| D2 | `PlayerProgressService` şimdi mi, gerekirse sonra mı? | Sonra | Adım 4 başı |
| D3 | **Dokuz** yemeğin fiyatları ve `tipRate` ne olacak? | Kullanıcı Inspector'dan girer; `tipRate` hâlâ açık | Adım 6, formül dilimi |
| D4 | Modifikasyonlar sipariş bedelini değiştiriyor mu? | Hayır — fiyat yalnızca yemekten | Adım 6 başı |
| ~~D5~~ | Sabır tipinin para tarafında hiç ağırlığı olmaması mı, yoksa tipe bağlı düz bir bahşiş çarpanı mı? | ✅ **Hiç ağırlığı yok** — kullanıcı onayladı 2026-08-18 | ~~Adım 6 başı~~ |
