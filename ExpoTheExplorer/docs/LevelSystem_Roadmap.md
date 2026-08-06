# Level (XP/Level) Sistemi — Uygulama Roadmap'i

Bu doküman, GDD v0.7 ve `CLAUDE.md` Section 3 (Progression, Lives System) baz alınarak, Level/XP sisteminin PR-PR nasıl inşa edileceğini tanımlar. **Bu dosya sadece planlama amaçlıdır — henüz hiçbir koda dokunulmamıştır.**

## Kapsam Dışı (bilinçli olarak hariç tutuldu)

- **Powerup System** — GDD Section 5.2'de "ertelendi" olarak işaretlendi. Bu roadmap'te powerup ödül entegrasyonuna dair hiçbir PR yok; level-up ödülü olarak powerup verme akışı, powerup sistemi tekrar kapsama alındığında ayrı bir roadmap konusu olacak.
- **Endless Mode** — tasarımdan kaldırıldı, hiç var olmayacak. Tek mod: Günlük Hedef Modu.

## Kilitli Kurallar (bu roadmap'in dayandığı kararlar)

1. XP her başarılı teslimatta kazanılır (GDD Section 2, 5, 10).
2. Level, XP eşikleri aşıldıkça artar; ilerleme kalıcıdır (meta-progression, oturum bazlı sıfırlanmaz) — **ama sadece gün başarıyla tamamlanınca kalıcı profile işlenir** (GDD Section 6, 10).
3. Can sıfırlanıp gün retry olduğunda, o günde kazanılan XP kaybedilir — kalıcı profile hiç işlenmemiş sayılır (GDD Section 6).
4. Gem ile "devam et" (continue) mekaniği retry değildir — gün canlı kalır, XP kazanımı geçerliliğini korur (GDD Section 6, 10).

## Açık Kalan Bağımlı Soru (bloke etmiyor, ama not edilmeli)

- Can bitince uygulanacak **zorluk düşürme** parametreleri (gürültü oranı/süre/bilet sıklığı) hâlâ kilitlenmedi (GDD Section 6, Açık Soru). Bu, retry akışının XP-kaybı kısmını **etkilemiyor** — o kısım netleşti ve uygulanabilir. Zorluk düşürme sayıları, retry akışına dokunan PR'dan (PR-4) **ayrı** bir tuning kararı olarak, ayrıca sorulmalı.

---

## PR-1 — Kalıcılık (Persistence) Temeli

**Neden önce bu:** Proje çapında hiç save/load katmanı yok (audit bulgusu). "Kalıcı XP/Level" gereksinimi, altında bir persistence mekanizması olmadan teknik olarak karşılanamaz. Bu PR, Level sistemine özel değil ama onu bloke ediyor.

**Kapsam:**
- Minimal bir `PlayerProfile` kalıcılık katmanı (örn. JSON dosyası ya da `PlayerPrefs` üzerinden serialize/deserialize).
- İlk aşamada sadece Level sistemini kilitlemek için gereken alanlar: kalıcı `Xp`, `Level`. (SoftMoney/Gems'in de aynı katmana taşınması ayrı bir karar — bu PR'da zorunlu değil, ama mimari buna izin vermeli.)
- Oyun açılışında profil yüklenir, `GameState` bu değerlerle initialize edilir; gün başarıyla tamamlandığında profil diske yazılır.

**Dokunulacak alanlar:** `Assets/Scripts/Core/` (yeni bir `PlayerProfile`/`SaveService` sınıfı), `GameManager` bootstrap akışı (yükleme çağrısı).

**Kabul kriteri:** Oyunu kapat-aç → daha önce kazanılan Level/XP korunuyor (henüz UI'da görünmese de, EditMode testiyle doğrulanabilir).

---

## PR-2 — Core State: `Xp`/`Level` Event-Publish Alanları + Config

**Kapsam:**
- `GameState.cs`'deki `Xp`/`Level` auto-property'leri, `Lives`/`SoftMoney`/`Gems` ile aynı kalıpta (custom setter → `EventBus` publish) yeniden yazılır.
- Yeni event'ler: `XpChanged` (her XP kazanımında), `LevelUp` (sadece level arttığında, ayrı sinyal).
- Yeni `LevelProgressionConfig` ScriptableObject: level başına gereken XP eğrisi (curve/formül), teslimat başına verilecek XP miktarı. Sayılar **placeholder** olacak — kesin dengeleme (balancing) sayıları ayrı bir tuning kararı, magic number gömülmeyecek.
- `GameConfig`'e `StartingXp`/`StartingLevel` alanları eklenir (PR-1'deki persistence yoksa bu değerler yeni oyuncu için başlangıç noktası olur).

**Bağımlılık:** PR-1 (profil yükleme sırası, `GameState` constructor'ında hangi değerin öncelikli olacağını — kayıtlı profil mi, config default mu — netleştirir).

**Kabul kriteri:** `XpChanged`/`LevelUp` event'leri unit testle tetikleniyor; config'te hardcoded sayı yok.

---

## PR-3 — `ProgressionSystem` Modülü (`LevelManager`)

**Kapsam:**
- `Assets/Scripts/Systems/ProgressionSystem/` içine, `LivesManager` şablonunda plain C# bir `LevelManager` sınıfı: `AddXp(int amount)` çağrıldığında `LevelProgressionConfig`'teki eşik eğrisine göre level atlamayı hesaplar, `GameState.Xp`/`Level`'ı günceller.
- Kendi `.asmdef`'i (kardeş sistemlerle aynı yapı).
- `Assets/Tests/EditMode/ProgressionSystemTests.cs` — MonoBehaviour'suz, izole unit testler (XP eşik aşımı, ardışık level atlama, sınır durumları).

**Bağımlılık:** PR-2 (config + event altyapısı).

**Kabul kriteri:** `LevelManager` MonoBehaviour bağımlılığı olmadan test edilebiliyor; XP eşik hesaplaması testlerle doğrulanmış.

---

## PR-4 — Gün İçi XP Staging + Retry'da Kayıp / Gün Sonunda Commit

**Neden ayrı bir PR:** Bu, roadmap'in en riskli parçası — kilitli kural şu: XP, ancak gün **başarıyla tamamlanınca** kalıcı profile işleniyor; can bitip retry olduğunda o günün XP'si siliniyor. Bu, "her teslimatta direkt kalıcı profile yaz" yaklaşımıyla çelişir — bir **staging (bekleyen/geçici) XP** kavramı gerekiyor.

**Kapsam:**
- `LevelManager`'a (veya ayrı bir "gün oturumu" kavramına) gün-içi kazanılan XP'yi ayrı tutan bir mekanizma: teslimat anında XP "pending" olarak eklenir (UI anlık günceller — GDD'ye göre oyuncu XP kazanımını görmeli), ama kalıcı profile (PR-1'deki save katmanına) **yazılmaz**.
- Gün başarıyla bittiğinde (Daily Goal Mode hedefi tamamlanınca): pending XP kalıcı profile commit edilir + diske yazılır.
- Can bitip retry olduğunda (GDD Section 6): pending XP sıfırlanır, `GameState.Xp`/`Level` son commit edilmiş kalıcı değere geri döner.
- Gem ile "continue" akışı (mevcut `GameOverPopupView`/`LivesManager.TryContinueWithSoftMoney/TryContinueWithGems`) bu commit/discard mantığını **tetiklememeli** — gün hâlâ canlı, pending XP korunur.

**Bağımlılık:** PR-1, PR-3. Ayrıca mevcut `GameManager`/gün sonu akışının nerede "gün başarılı bitti" ve "can bitti → retry" sinyallerini verdiğini netleştirmek gerekiyor (bu akışlar zaten var mı, yoksa Daily Goal Mode'un "hedef tamamlandı" sinyali de eksik mi — bu PR başlamadan kod tabanında ayrıca doğrulanmalı).

**Kabul kriteri:** Unit test senaryosu: gün içinde X XP kazanılır → retry tetiklenir → kalıcı profildeki XP değişmemiştir. Ayrı senaryo: gün başarıyla biter → pending XP kalıcı profile yazılmıştır.

---

## PR-5 — `GameManager` Wiring: Teslimatta XP Ödülü

**Kapsam:**
- `GameManager.OnTicketDelivered`'a `LevelManager.AddXp(...)` çağrısı eklenir (şu an sadece SoftMoney güncelleniyor).
- XP miktarının teslimata göre nasıl hesaplanacağı (sabit mi, öğe sayısına göre mi — GDD'de netleşmemiş bir detay) PR-2'deki config üzerinden okunur; sayı belirsizse placeholder ile ilerlenir ve kullanıcıya bayrak kaldırılır.

**Bağımlılık:** PR-2, PR-3, PR-4 (pending/commit mantığı olmadan bu PR anlamsız kalır).

**Kabul kriteri:** Teslimat sonrası `XpChanged` event'i doğru miktarla tetikleniyor; entegrasyon testinde teslimat → XP artışı doğrulanıyor.

---

## PR-6 — UI: Level Rozeti, XP Bar, Level-Up Geri Bildirimi

**Kapsam:**
- `GemsView` şablonunda (`Start()`'ta subscribe, `OnDestroy()`'da unsubscribe, `ValidateReferences()`): `LevelView` (HUD'daki altıgen level rozeti, GDD Section 13) + `XpBarView` (level içi ilerleme çubuğu).
- Level atladığında kısa bir geri bildirim (ses/görsel — GDD'de detay netleşmemiş, minimal bir versiyon yeterli; kesin animasyon/ses tasarımı ayrı bir sonraki iterasyon).
- Gün sonu ekranına (GDD Section 11, 13 — "kazanılan XP" gösterimi) bu oturumda kazanılan XP'nin eklenmesi (mevcut ekranda henüz yoksa).

**Bağımlılık:** PR-2 (event'ler), PR-4 (pending XP'nin UI'da anlık görünmesi gerekiyor — gün bitmeden bile oyuncu XP kazanımını görmeli).

**Kabul kriteri:** Sahne içinde XP kazanıldığında bar/rozet güncelleniyor; retry tetiklendiğinde UI, kalıcı (commit edilmiş) değere geri dönüyor.

---

## Sıra Özeti

```
PR-1 (Persistence) ──┐
                      ├─→ PR-4 (Staging + Retry/Commit) ──→ PR-5 (Delivery wiring) ──→ PR-6 (UI)
PR-2 (Core+Config) ──┤
                      │
PR-3 (LevelManager) ──┘
```

PR-1, PR-2, PR-3 birbirinden bağımsız paralel ilerleyebilir. PR-4, üçünü de bekler. PR-5 ve PR-6, PR-4'ten sonra sıralı gider.

## Bu Roadmap'e Başlamadan Önce Netleştirilmesi Gerekenler

- **Teslimat başına XP formülü:** sabit miktar mı, öğe sayısına/karmaşıklığa göre mi ölçekleniyor? (GDD'de belirtilmemiş — PR-2/PR-5 öncesi sorulmalı.)
- **Level eşik eğrisi:** lineer mi, üstel mi, elle girilen bir tablo mu? (Tasarım/dengeleme kararı — PR-2 öncesi en azından bir başlangıç formülü üzerinde anlaşılmalı, kesin sayılar sonradan tune edilebilir.)
- **Level'ın oyun içi etkisi:** GDD Section 10'da "netleştirilmeli" olarak işaretli (zorluk kilidi mi, kozmetik mi, meta-oyun erişimi mi). Bu roadmap, Level'ın *kazanılıp saklanmasını* çözüyor; Level'ın *ne açtığı* ayrı bir tasarım kararı ve bu roadmap'in kapsamı dışında.
