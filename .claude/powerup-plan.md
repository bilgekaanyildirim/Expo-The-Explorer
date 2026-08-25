# powerup-plan — GDD 5.2'nin üç power-up'ını hayata geçirme yol haritası

Bu dosya `key-plan.md` / `meta-plan.md` / `economy-plan.md` ile aynı işi görür:
bir sistemin adımlarını, her adımın NEREDE bittiğini ve hangi kararın nerede
alındığını tek yerde tutar. Her adım kendi preflight'ını ve kendi APPROVE'unu
alır; bir adım bitmeden sonraki başlamaz.

**Kaynak tasarım:** `ExpoTheExplorer/docs/Expo_the_Explorer_GDD_1.md` Bölüm 5.2.
O bölüm 2026-08-25'e kadar "geliştirme kapsamı dışında (ertelendi)" işaretliydi;
Adım 0 onu kapsama aldı ve açık bıraktığı soruları kapattı.

## İki ekran, iki iş

Kullanıcının kararı (2026-08-25): **Gem ile satın alma ana ekranda yapılır, gün
sahnesinde değil.** Bu, sistemi ikiye ayırıyor ve ayrım baştan sona korunuyor:

| | Gün sahnesi (`SampleScene`) | Ana ekran (`MainScreen`) |
|---|---|---|
| Ne yapar | power-up'ı KULLANIR | power-up SATIN ALIR |
| Ne görür | üç daire + kalan stok | kendi paneli, üç satır, Gem fiyatı |
| Stok biterse | buton soluk, basınca sessizce hiçbir şey olmaz | — |

Bu ayrım bedava geliyor çünkü stok `GameSession`'da duruyor ve o iki ekranda da
kuruluyor (`GameManager` ve `MainScreenRoot`, ikisi de `SessionHost`). Efektler
ise yalnızca gün sahnesinde kaydediliyor — ana ekranda hiçbir efekt kayıtlı
olmadığı için oraya yanlışlıkla bir kullanım butonu konsa bile hak harcanamaz.

**Boş butona basmak ceza değil.** Stok bitmişken basmak sessizce hiçbir şey
yapar; ayrı bir popup yok (`NoKeysPopupView`'ın aksine — anahtarlarda popup var
çünkü orada oyuncu OYUNA giremiyor, burada ise sadece bir kolaylıktan mahrum
kalıyor).

## Neden bu sıra

Üç power-up aynı omurgayı paylaşıyor: bir stok, bir Gem satın alması, bir HUD
butonu. O omurga önce gelmezse üçü de kendi stok kopyasını yazar ve tek-yazar
invariantı üç yerden kırılır.

Omurga → gün HUD'u → dükkân sırası, **ekonomi döngüsünü efektlerden önce
kapatıyor**: Adım 3 bittiğinde oyuncu hak kazanabiliyor, satın alabiliyor ve
sayacın düştüğünü görebiliyor. Ancak ondan sonra efektler EN BASİTTEN EN
KARMAŞIĞA tek tek takılıyor — süre sıfırlama tek satırlık bir state yazması,
oto-toplama ise teslimat cascade'inin ortasında board'u değiştiriyor. Böylece
omurganın doğruluğu en ucuz efektle kanıtlanmış oluyor, en pahalısıyla değil.

## Adımlar

### Adım 0 — Plan + GDD (kod yok) ✅
Bu dosya, artı GDD 5.2'nin ve proje `CLAUDE.md`'sinin yeniden yazımı: erteleme
notu kalkar, kazanım kuralı / stok / Gem satın alması netleşir, "Ertelendi"
başlığı altındaki dört sorudan üçü kapanır (cooldown bilinçli olarak kapsam
dışı kalır). Korumalı dosyaya dokunmaz.

### Adım 1 — Stok omurgası ✅
`PowerupType` (enum, Data), `PowerupConfig` (yeni SO — tip başına başlangıç
stoğu / Gem fiyatı / gün-tamamlama kazanımı), `PowerupManager` (stoğun TEK yazarı: kullanım,
Gem ile satın alma, gün tamamlanınca kazanım), kalıcılık (`PlayerProfile` v8 +
store), `GameSession` içinde kurulum, İKİ ekranın kökünde de config alanı.

- Efekt YOK. `TryUse` yalnızca hakkı düşürür ve `RegisterEffect` ile kaydedilmiş
  bir delege çağırır; Adım 4–6 o delegeleri doldurur. Hiç efekt kayıtlı değilse
  `false` döner ve hak harcanmaz.
- Şekil olarak `KeySystem`in birebir kopyası: sayı kendi private alanında durur
  (GameState bir GÜN'ün state'i, stok ise meta kaynak), Gem'in tek yazarı
  `Wallet` olarak kalır, profildeki `-1` "yok" işaretçisini manager çözer.
- **Biter:** `PowerupSystemTests` (27 test) yazıldı ve derleniyor; oyunda görünür bir
  değişiklik yok. Testler HENÜZ KOŞTURULMADI -- Unity proje kilidini tuttuğu için
  batchmode koşusu yapılamıyor, Test Runner penceresinden çalıştırılmalı.
- Bir tasarım kararı burada somutlaştı ve plana yazılmayı hak ediyor: `PowerupConfig`
  bağlanmamışsa oyun ÇÖKMEZ, power-up'sız çalışır (projenin "fails open" ilkesi). Bunun
  bedeli, `GameSession`'ın yükleyip yönetemediği hakları diske geri yazması: yoksa
  unutulmuş bir Inspector sürüklemesi oyuncunun Gem'le aldığı stoğu sıfırlardı.

### Adım 2 — Gün sahnesi HUD barı ✅
`PowerupBarView`: kalan hakkı gösteren üç buton. Satın alma YOK.

**Kurulum script'i YAZILMADI ve bu bir eksiklik değil.** Plan başta
`HudCanvasPrefabSetup` desende bir üretici öngörüyordu; kullanıcı paneli
(`PowerUpPanel` > AutoCollect/LeakCleaner/TimerReset butonları, her birinde bir
`Count` TMP etiketi) `SampleScene`'de zaten elle kurmuştu. Onu kodla yeniden
inşa eden bir script aynı objeler üzerinde ikinci bir otorite olur ve ilk
çalıştığında kullanıcının düzenini ezerdi. View hiçbir şey instantiate etmiyor,
yalnızca sürüklenen referansları okuyor. Adım küçüldü: tek dosya, kurulum
script'i yok, test yok (Assembly-CSharp'taki bir MonoBehaviour bu projede
EditMode'dan görülemiyor — `GameManager`ın da test taşımamasının sebebi bu).

- Soluklaşma `charges > 0`'a bağlı, `PowerupManager.CanUse`'a DEĞİL: CanUse
  "kayıtlı efekt var mı"yı da soruyor ve efektler Adım 4-6'da geldiği için üç
  butonu da baştan ölü gösterirdi. Gün sahnesinde ikisi zaten eşitleniyor.
- **Biter:** üç buton ekranda, sayaçlar doğru, stok bitince soluklaşıyor.
  Basınca hâlâ hiçbir şey OLMUYOR — bu adımın kapsamı bu.

### Adım 3 — Ana ekran power-up dükkânı ✅
`PowerupShopView` + kendi butonu ve paneli — `MetaShopView`'ın seam'ini takip
eder (`sessionHost`, satın alma sonrası `session.Save()`), ama onun İÇİNE
girmez: o dosya prop + SoftMoney'nin sahibi ve MetaSystem'e ait.

**Adım 3b: kurulum script'i sonradan YAZILDI.** Adım 3'te "yazmıyorum" demiştim,
Adım 2'nin gerekçesini (paneli kullanıcı zaten kurmuştu) buraya da taşıyarak.
Kullanıcı düzeltti ve haklıydı: gün sahnesindeki paneli kurmuştu, bunu
kurmamıştı, ve elle 13 obje yerleştirmek istemiyordu. `PowerupShopSetup`
(`ExpoTheExplorer > Meta > Build Powerup Shop`) `MetaShopSetup`ın duruşunu birebir
alıyor -- yalnızca açık sahne, kaydetmez, var olanı yeniden İNŞA ETMEZ -- ama
onun aksine SATIRLARI DA kuruyor: o step kurmuyor çünkü satır sayısını yalnızca
katalog bilir, burada cevap üç ve enum'da yazılı.

**Ders:** "kurulum script'i yazma" bir ilke değil, duruma bağlı. Doğru soru
"kullanıcı bu hiyerarşiyi zaten kurdu mu?" — kurduysa script ikinci otorite
olur, kurmadıysa script işi ona yıkmamanın yolu.

Ayrıca iki yerde `MetaShopView`den bilinçli olarak ayrıldı:

- **Satır şablonu yok, üç authored satır var.** O şablon "kaç satır olacağını
  yalnızca katalog bilir" problemi için; burada cevap üç ve enum'da yazılı.
- **Onay popup'ı yok.** Bir prop pahalı ve kalıcı bir yerleşim kararı; bir hak
  birkaç Gem ve etkisi anında görünür.
- **Parası yetmeyen SATIN AL tıklanamaz**, `MetaShopRowView`in tersine. O satır
  tıklanabilir kalıyor çünkü popup'ı reddi AÇIKLAYABİLİYOR; burada popup yok,
  o yüzden sessizce hiçbir şey yapmayan bir dokunuş üç seçeneğin en kötüsü olurdu.

Dükkân kendi açma butonunu taşıyor (`MetaShopView`in `marketButton`ı gibi), bu
yüzden `MainScreenView` power-up'ların varlığından habersiz kalıyor ve
**blueprint'e yeni ok eklenmedi.**

- **Biter:** ekonomi döngüsü kapalı — gün tamamlayınca hak kazanılıyor, Gem'le
  hak satın alınabiliyor, gün sahnesindeki sayaç bunu gösteriyor.

### Adım 4 — Power-up 2: Süre Sıfırlama ✅
`PowerupEffects.ResetActiveTicketTimers` + `GameManager.RegisterPowerupEffects`.
8 test.

Bu adım tek bir efektten fazlasını kurdu; üç efektin de üzerine oturacağı iki
şey burada tanımlandı:

- **Kayıt yeri `GameManager`.** Power-up'ı onu gerçekleştirebilecek şeye bağlayan
  TEK yer, ve gün sahnesinin kompozisyon kökü olduğu için doğru yer. Ana ekran
  aynı manager'ı kuruyor (sayaçları satabilmek için) ama hiçbir efekt
  kaydetmiyor — iki ekranlı ayrımı güvenli yapan şey bu.
- **Paylaşılan kapı:** `!IsAwaitingContinue && !IsDayComplete`. Can bitince gün
  donuyor ve oyuncu Continue popup'ına bakıyor; orada bir hak harcamak
  göremediği bir dünyaya para vermek olurdu. `BoardItemDragHandler` sürüklemeyi
  aynı bayrakla kapatıyor, emsal o. Adım 5 ve 6 bu kapının arkasına birer satır
  ekleyecek.

Ayrıca `Ticket.RemainingSeconds`a ÜÇÜNCÜ bir yazar eklendi ve sınır bilinçli:
yapıcı açar, `TicketSlotManager.Tick` harcar (ikisi de "zaman ileri gider"), bu
ise zamanın geri gittiği tek yer. Alternatif -- alanı tek yazarda tutmak için
efekti `TicketSlotManager` üzerinden geçirmek -- reddedildi, çünkü o sınıfın
power-up diye bir şeyi bilmesi için hiçbir sebebi yok. `decisions.md`'ye Adım
7'de kararın parçası olarak yazılacak.

- Hiçbir biletin süresi eksik değilse efekt `false` döner ve **hak harcanmaz** —
  GDD 5.2'nin ortak kuralı, ve testlerin bu dosyayı hak ettiren kısmı: bu, kural
  manager tarafından test EDİLEMEZ, çünkü manager yalnızca bir bool görüyor.
- **Biter:** ikinci buton gerçekten çalışıyor.

### Adım 5 — Power-up 3: Gürültü Temizleme ✅
`TicketRequirements` (Core'a çıkarılan çoklu-küme yardımcısı — `TraySlot.Matches`
de ona geçer) + `PowerupEffects.ClearUnneededItems`: aktif biletlerin hiçbirine
gerekmeyen ürünleri board'dan kaldırır.

**Bu adım 2026-08-25'te yeniden yazıldı.** Önceki plan bunu görsel bir efekt
sanıyordu: birkaç saniyelik bir pencere, `BoardClarityTimer` diye bir zamanlayıcı,
`BoardView`'da soluklaştırma. Kullanıcı düzeltti — soluklaştırma yok, doğrudan
KALDIRMA var. Sonuç olarak adım küçüldü, büyümedi: zamanlayıcı sınıfı da, UI
değişikliği de gereksiz. Kaldırma `BoardGrid.RemoveItem`'a düşüyor, o da zaten
`CellChanged` yayınlıyor ve `BoardView` görseli kendiliğinden gizliyor — yani bu
efekt tamamen veri katmanında yaşıyor ve sahneye hiç dokunmuyor.

- **Kural kimlik üzerine, sayı üzerine değil.** Bir ürünün (yemek + modifikasyon)
  kimliği aktif biletlerin hâlâ ihtiyaç duyduğu kimliklerden biri değilse kalkar;
  fazlalıklar kalır. Bir bilet tek burger istiyorsa üç burgerin üçü de durur —
  oyuncu üçüncüsü için "gerek yok" demez, "fazla var" der.
- Board'da kaldıracak hiçbir şey yoksa efekt `false` döner ve hak harcanmaz.
- Boşalan hücreler `BoardGrid`'in bekleyen doğum kuyruğundan anında dolar, yani
  temizleme sıkışmış zorunlu havuz ürünlerinin de board'a inmesini sağlıyor.
- **Denge uyarısı:** bu artık muhtemelen üçünün EN GÜÇLÜSÜ, ama Adım 1'de en ucuz
  fiyatı (15 Gem) ve en yüksek başlangıç stoğunu (2) taşıyor. Sıralama ters;
  sayılar dengeleme turunda düzeltilmeli.
- **Garanti bilet sistemiyle etkileşim, kullanıcının sorusu üzerine `BoardDistributor`
  okunarak cevaplandı ve TESTE bağlandı.** İki bağımsız sebeple çökmüyor: (1) aktif
  biletlerin gerektirdiği hiçbir ürün silinmiyor ve garanti zaten "en az bir bilet
  tamamlanabilir olsun" demek; (2) `SpawnMissingRequiredItems` hiçbir şey
  önbelleklemiyor, her turda board'u yeniden sayıp eksiği doğuruyor. İkisi de birer
  testle pinlendi. Bulunan incelik: upcoming bir garanti bileti için önden doğmuş
  ürünler silinebiliyor, ama o bilet slota girdiğinde yeniden doğuyor.
- **Denge yan etkisi:** `leakedTickets` bir upcoming biletin yalnızca BİR kez sızmasına
  izin veriyor. Temizlik o sızmış ürünü kaldırırsa bilet işaretli kaldığı için bir daha
  sızmıyor — yani bu power-up o biletlerin gürültü katkısını KALICI olarak siliyor.
  Fiyat/stok ayarlanırken bilinmesi gereken bir güç kaynağı.
- Yanında `TicketRequirements` Core'a çıkarıldı (üç tüketici) ve `TraySlot.Matches` ona
  geçirildi. Not: aynı kural `BoardDistributor`da iki kez daha yazılı ve dokunulmadı —
  manifest dışıydı; ayrı bir küçük iş.
- **Biter:** üçüncü buton gerçekten çalışıyor.

### Adım 6 — Power-up 1: Oto-Toplama ✅
`PowerupEffects.PlanAutoCollect` (saf, test edilebilir plan) + `AutoCollectRunner`
(planı mevcut sürükle-bırak kabul yolundan geçirir: `WorldTrayView.TryAcceptDrop`).

- İkinci bir tepsi çizim yolu AÇILMIYOR. Tepsi görselleri bugün tamamen
  sürüklenen objenin reparent edilmesiyle oluşuyor; veri katmanına yazan bir
  oto-toplama tepsiyi boş gösterirdi.
- Her adımda plan YENİDEN hesaplanır, tek seferlik liste üzerinde yürünmez:
  bir tepsinin dolması teslimatı, teslimat yeni bilet atamasını, o da board'un
  yeniden doğmasını senkron tetikliyor — donmuş bir liste o cascade'in
  ortasında geçersizleşir.
- **Tek geçiş, elindeki biletler.** Basıldığı andaki aktif bilet ÖRNEKLERİ yakalanıyor
  ve bir slotun bileti değiştiği anda o slot bu basış için bitiyor. Yoksa tek basış
  şu zinciri kurardı: topla → teslim et → yeni bilet gelsin → ürünleri doğsun → onları
  da topla → ... ve günü bitirirdi. GDD "aktif biletler için gereken ürünler" diyor.
  Kontrol referans eşitliği, slot boşluğu DEĞİL — yeni bilet aynı slota geliyor.
- **Karar/uygulama ayrımı zorunluluktan.** Koşucu Assembly-CSharp'ta (önceden tanımlı
  assembly, hiçbir asmdef referans alamıyor), yani EditMode'dan test EDİLEMEZ. O yüzden
  kural içeren her şey `PowerupEffects`e çekildi ve koşucuda yalnızca "uygula" kaldı.
- **Yanlış sipariş üretemez:** yalnızca biletin hâlâ eksik olduğu ürünler öneriliyor,
  bu bir teste bağlandı. Tepsideki YANLIŞ bir ürün hiçbir gereksinimi düşürmüyor —
  o tepsi zaten cana mal olacak, ve hatayı power-up'a tamamlatmak yanlış olurdu.
- **Biter:** üç power-up da çalışıyor.

### Adım 8 — Oto-Toplama'nın temposu ⟲ GERİ ALINDI (2026-08-26)
Kullanıcı oyunda denedi ("oto collect animasyonu çook hızlı"), taşımalar bir
coroutine'e ve `PowerupConfig`teki bir aralığa yayıldı, sonra kullanıcı geri
almamı istedi. **Kod eski hâlinde: bütün taşımalar tek karede oluyor.**

Kayda geçiyor çünkü teşhis hâlâ doğru ve biri yine fark edecek: animasyon hızlı
değil, HİÇ YOK. Yeniden yapılırsa asıl problem tempo değil muhasebedir —
`PowerupManager` hakkı düşürmek için `Run()`ın `bool`unu ŞİMDİ ister, iş ise bir
saniye daha sürer. Çözüm cevabı öne almaktı: basış anında hem "toplanacak ürün
var mı" hem "board handler'ı verebiliyor mu" sorulur, coroutine'in yapamayacağı
bir iş için hak harcanmaz. Uçuş sırasında ikinci basış reddedilir, kuyruğa
alınmaz. Ve Adım 6'nın referans-eşitliği kontrolü o noktada ihtiyati olmaktan
çıkıp taşıyıcı hâle gelir, çünkü bilet toplamanın ortasında zaman aşımına
uğrayabilir.

### Adım 7 — Haritalar + kapanış ✅
**D-081** yazıldı: kararın tamamı tek kayıtta. Adım 1'de bilinçli olarak
ertelenmişti (yarım bir kararı yazıp beş kez değiştirmek yerine üç efekt de
oturduktan sonra bir kez yazmak için) ve bu doğru çıktı — Gürültü Temizleme'nin
anlamı yolda değişti, satın alma ekran değiştirdi, ve tek-yazar sınırı iki kez
esnedi. Bunları beş ayrı düzeltmeyle değil, bir kez doğru yazmak mümkün oldu.
Adım 1'in blueprint notu artık D-081'e bağlandı.

`blueprint.md`'nin `PowerupSystem` satırı Adım 1'de girdi: codemap satırlarının
`sys:` alanının geçerli olabilmesi için sistemin blueprint'te var olması
gerekiyordu, sonraya bırakılamazdı.

## Kapsam dışı (bilinçli)

- **Cooldown / kullanım limiti.** GDD ertelemişti, burada da erteleniyor:
  `abstraction-level.md` kimsenin çevirmeyeceği bir knob'ı ölü ağırlık sayar.
  Stoğun kendisi zaten sınır.
- **Etkinlik (event) ödülleri.** GDD'nin kazanım yolu #1'i "belirli etkinlikler"
  diyordu; bu projede henüz etkinlik sistemi yok. Gün tamamlama, GDD'nin kendi
  gösterdiği doğal aday olarak onun yerine geçiyor.
- **Gün sahnesinde satın alma.** Kullanıcının kararıyla ana ekrana taşındı.
  `PowerupManager.TryBuyWithGems` kuralı Adım 1'de yazılıyor ama onu çağıran tek
  ekran ana ekran; gün sahnesinden çağıran bir buton EKLENMEZ.
