# Meta dükkânı — yol haritası (ikinci tasarım)

<!-- 2026-08-21. İlk dükkân (decisions.md D-024) yapıldı ve kullanıcının isteğiyle
     geri alındı (D-027) — hata bulunmadı, farklı bir tasarım istendi. Bu dosya o
     tasarımın planı. Her adım tek bir preflight + APPROVE döngüsüdür.

     `meta-plan.md` Adım 8 buraya işaret eder. Kurallar katmanı (MetaResolver,
     MetaPurchase, 25 test) geri alma sırasında BİLEREK korundu, çünkü fiyat/alan
     geçidi/açılma günü ekrandan bağımsız — bu plan onları yeniden yazmıyor, kullanıyor. -->

## 1. Akış — kullanıcının tarif ettiği

```
Sağ alt köşe: [🛒]  ← market düğmesi
       │ tıkla
       ▼
┌─ Dükkân sekmesi ──────────────┐
│ ⛲  Fountain            [BUY] │   ← alt alta satırlar
│ 🪑  Table 1             [BUY] │      sol: simge
│ 🌳  Plant               [BUY] │      orta: isim
└───────────────────────────────┘      sağ: BUY
       │ satırdaki BUY
       ▼
  haritada propun HAYALETİ görünür
       +
┌─ Onay ────────────┐
│    Fountain       │
│      400          │
│     [ BUY ]       │
└───────────────────┘
       │ popup'taki BUY
       ▼
  satın alınır, prop KATI hâline geçer
```

**İki aşamalı olması bu tasarımın en iyi yanı:** oyuncu parayı vermeden önce hem
*nereye* geleceğini (hayalet) hem *kaça* olduğunu (popup) görüyor. İlk dükkânda
fiyat listede yazıyordu ve önizleme ayrı bir dokunuştu; bu akış ikisini tek bir
kararın içine topluyor.

## 2. Asıl karmaşıklık: durum makinesi

Kullanıcının "kompleks" demesi yerinde — iş satırları çizmek değil, **dört durum ve
aralarındaki geçişler**:

```
KAPALI ──(market düğmesi)──▶ AÇIK
AÇIK ──(satır BUY)──▶ ÖNİZLEME(item)      [hayalet + popup görünür]
ÖNİZLEME ──(iptal)──▶ AÇIK                [hayalet silinir]
ÖNİZLEME ──(popup BUY)──▶ satın al ──▶ AÇIK  [hayalet silinir, prop katı gelir]
AÇIK ──(market düğmesi)──▶ KAPALI          [hayalet varsa silinir]
```

Hayaletin ömrü **yalnızca** `ÖNİZLEME` durumuna bağlı. Sızdığı her yol bir hata:
panel kapanırken silinmezse haritada sahipsiz bir hayalet kalır; satın alma sonrası
silinmezse katı propun üstünde yarı saydam bir kopya durur.

**Tek bileşen bu makineyi sahiplenir.** Panel, satırlar ve popup ayrı sınıflara
bölünürse durum üçe dağılır ve "hayalet kimin işi" sorusu üç kez cevaplanır.

## 3. Mimari kararlar

### S1 — Kurallar yeniden yazılmıyor, çağrılıyor

`MetaPurchase.ShopItems` listeyi, `MetaPurchase.Evaluate` her satırın ve popup'ın
durumunu veriyor. Altı verdict ve 25 testi geri alma sırasında **bilerek** korundu
(D-027) çünkü fiyat, alan geçidi ve açılma günü katalog hakkında kurallar — ekran
hakkında değil. Bu plan tek bir kural bile eklemiyor.

### S2 — Hayalet API'si geri geliyor — ama Ş3'te, Ş1'de değil

D-027 `ShowGhost`/`ClearGhost`'u sildi ve *"yeniden eklemek üç satır"* diye kaydetti.
Tam olarak o oluyor. Silinmesi doğruydu: o anda çağıranı yoktu ve çağıranı olmayan
bir public metot "bir yerde kullanılıyor" diye okunur.

**Ve aynı gerekçe geri ekleme ZAMANINI da belirliyor:** ilk taslak bunu Ş1'e koymuştu,
oysa Ş1'de de çağıranı yok. Ş3'e taşındı (D-029) — hem ekleyen hem kullanan adım o.

`Refresh()` de public'e dönüyor — satın alma `IsActive`'in cevabını değiştiriyor ve
bunu başka hiçbir şey fark etmiyor.

### S3 — İki aşama, iki `Evaluate`

Satır çizilirken bir kez, popup'ta BUY'a basılırken bir kez. İkincisi gereksiz
görünüyor ama gerekli: panel açıkken oyuncu başka bir şey satın alabilir ve bakiye
düşer, yani ilk değerlendirme bayatlar. Maliyeti bir fonksiyon çağrısı.

Ve satın alma anında **cüzdana yine de soruluyor** (`TrySpendSoftMoney`) — bakiyenin
tek yazıcısı o ve kendi cevabı otorite. İlk dükkânın bu sırası doğruydu, aynen
korunuyor:

```
Evaluate → Wallet.TrySpendSoftMoney → OwnedMetaItemIds.Add → Save → Refresh
```

Bu akış **yapısal olarak atomik**: para ve sahiplik listesi aynı `GameSession`'da
duruyor ve `Save()` ikisini birlikte yazıyor. Çökme olursa ikisi de kaybolur.
"Parası gitti ama eşya gelmedi" ifade edilemiyor.

### S4 — Katmanlama açık sayıyla, hierarchy sırasıyla değil

Bu oturumda tam olarak bu sınıf bir sorun yaşandı: arka plan HUD'ı yedi, çünkü iki
Canvas'ın da `sortingOrder`'ı 0'dı ve hangisinin üstte olduğu hierarchy sırasına
bakıyordu. Panel ve popup **açık** sayılarla sıralanır, örtük sıraya bırakılmaz.

Sıra: harita (en arka) → market düğmesi → panel → onay popup'ı (en ön).

### S5 — Sahne kabı Editor adımıyla kurulur, satırlar runtime'da

Diğer üç meta adımının deseni: yalnızca açık sahneye dokunur, **kaydetmez**, ve var
olan hiyerarşiyi yeniden kurmaz. Satırlar katalogdan runtime'da üretilir — sayıları
lokasyona ve satın almalara göre değişiyor, ve katalog tek otorite (D-015).

## 4. Adımlar

Her kutu tek bir preflight + APPROVE. Her adım **kendi başına gözle doğrulanabilir**
— bu bilinçli, çünkü bu iş görsel ve ben ekranı göremiyorum.

### [x] Ş1 — İskelet: market düğmesi + panelin açılıp kapanması  *(bitti, D-029)*

- Sağ alt köşede market düğmesi, panel aç/kapa — `MetaShopView`
- Panel **boş** — içine hiçbir şey konmuyor
- Sahne kabını kuran Editor adımı — `MetaShopSetup`
  (`ExpoTheExplorer > Meta > Build Meta Shop`)

**Hayalet API'si Ş1'den ÇIKARILDI, Ş3'e taşındı.** Sebebi D-027'nin kendi gerekçesi:
o üç üyeyi *"çağıranı olmayan bir public metot, bir yerde kullanılıyor diye okunur"*
diye silmişti. Ş1'de çağıranları yok; bugün eklemek iki adım boyunca ölü API taşımak
olurdu. Ş3 hem ekliyor hem kullanıyor.

**Doğrulama:** düğmeye basınca boş bir panel açılıyor ve kapanıyor, harita arkada
görünmeye devam ediyor, HUD üstte kalıyor. **Kullanıcı Unity'de gördü (2026-08-21):**
panel açılıyor ve boş — *"e hani hiç bir şey listelenmiyor"*. Boş olması Ş1'in
tanımı; ürünleri koyan adım Ş2.

### [x] Ş2 — Liste: simge, isim, fiyat, BUY  *(bitti, D-030)*

- `MetaPurchase.ShopItems`'tan satırlar
- Her satır: solda sprite, ortada isim, sağında fiyat, en sağda BUY (**MS1: fiyat
  satırda**, önerildiği gibi)
- Satırdaki BUY şimdilik yalnızca log basıyor
- Satır, sahnede yazarlanmış **kapalı bir şablonun** klonu (`MetaShopRowView`) —
  `TicketCardView`'ın `modificationRowTemplate` deseni. Şablonu Inspector'da
  biçimlendirince bütün satırlar takip eder, senkron tutulacak ikinci asset olmaz.
- Hangi lokasyon listeleniyor: `MetaGroundsView.ViewedLocation` (D-027'nin sildiği
  özellik geri döndü, çünkü artık çağıranı var). Dükkânın kendi katalog alanı **yok**.

**MS2 artık soru değil:** `MetaPurchase.ShopItems` parası yetmeyeni ve alanı kilitliyi
zaten listede bırakıyor, yalnızca sahip olunanı düşürüyor — Ş2 o cevabı olduğu gibi
çiziyor. **Gün-açılımlı proplar hiç girmiyor** (`Unlock != Purchase`).

**Doğrulama:** doğru ürünler listeleniyor, sahip olunanlar ve gün-açılımlılar listede
yok, kaydırma çalışıyor. **Henüz Unity'de çalıştırılmadı.**

### [x] Ş3 — Önizleme + onay popup'ı  *(bitti, D-031)*

- **Hayalet API'si eklendi** (`ShowGhost`/`ClearGhost`) — Ş1'den buraya taşınmıştı,
  çağıranı ilk kez burada oldu. **`Refresh` hâlâ private:** Ş3 satın alma yapmıyor,
  yani `IsActive`'in cevabını değiştiren bir şey yok. Ş4'te public olacak — aynı
  gerekçenin devamı.
- Satır BUY → haritada hayalet + üstte popup (isim, fiyat, BUY, CANCEL)
- Popup'taki BUY yalnızca log basıyor ve **durum değiştirmiyor**
- **Önizleme sırasında liste gizleniyor.** Panel alt sayfa ve haritanın çoğunu
  kapatıyor; hayalet haritada duruyor. Liste açıkken hayalet panelin altında kalır ve
  özellik bozuk gibi görünür. İptal listeyi geri getiriyor (yeniden kurmuyor).
- **Durum makinesi gerçek oldu.** Hayaletin tek çıkış kapısı `ClosePreview`; iptal,
  dışına dokunma, başka satır, market düğmesi, panel kapanması ve lokasyon değişimi
  hepsi oradan geçiyor.

**Doğrulama:** hayalet doğru yerde çıkıyor; iptal edince gidiyor; panel kapanınca
gidiyor; başka bir satıra basınca eski hayalet gidip yenisi geliyor.
**Henüz Unity'de çalıştırılmadı.**

#### Ş3b — popup item'in üstünde + item'e odaklanan zoom  *(bitti, D-032)*

Kullanıcının iki isteği, Ş3'ün üstüne:

- Popup ekranın ortasında değil, **propun üstünde**. Prop ekranın tepesindeyse altına
  dönüyor; kenarlardan taşmıyor.
- **Item'e zoom.** Miktarı propun boyutundan çıkıyor: propun görünümün `previewFill`
  kadarını dolduracağı zoom, `[1, previewMaxZoom]` arasına sıkıştırılıyor. Çöp kovası
  üst sınıra kadar yakınlaşıyor; ekran kadar geniş bir çit 1'in altını istediği için
  `clamp` onu 1'de tutuyor, yani **hiç zoom olmuyor**. Kullanıcının "çite yapılamaz"
  tespiti böylece kural değil, hesabın sonucu.
- Önizleme sırasında harita kaydırması **kapalı** — oyuncu karar veriyor, gezmiyor.
- Odaklanma `ShowGhost` içinde, geri yükleme `ClearGhost`'ta: Ş3'ün altı çıkış yolu
  zoom'u da geri alıyor, yedinci bir yol açılmadı.

**Ayarlar Inspector'da:** `Ghost Alpha`, `Preview Fill`, `Preview Max Zoom`,
`Preview Focus Height` (`MetaGroundsView`) ve `Confirm Gap` (`MetaShopView`).

#### Ş3c — kadraj tween ile gidiyor  *(bitti, D-033)*

Kullanıcının isteği: *"ışınlanmak yerine tween ile gitse ya zooma"*. Ş3b'de bunu Ş6'ya
ertelemiştim.

- DOTween ile, süre ve ease `MetaGroundsView`'da serialize field
  (`Preview Travel Seconds`, `Preview Travel Ease`).
- **Hedef ölçülüyor, hesaplanmıyor:** harita bir an için hedefe konup bitiş ölçeği,
  bitiş konumu ve hayaletin *yerleşik* kenarları orada okunuyor, sonra başa alınıp
  tween başlıyor. D-032'nin reddettiği pivot/anchor aritmetiğine dönmeden tween
  yapmanın tek yolu buydu.
- Bu sayede `MetaShopView`'da **tek satır değişmedi**: popup ilk kareden itibaren
  propun gideceği yerde duruyor, harita altından kayıyor.
- **Tween bir hatayı açığa çıkardı:** `ShowGhost` eskiden `ClearGhost`'u çağırıyordu,
  yani satır değiştirince önce eski kadraja dönülüyordu. Anlıkken görünmüyordu; tween'le
  "önceki hâl" tween'in ortasından yakalanır ve iptal haritayı hiç bulunmadığı bir yere
  götürürdü. Ayrıldı: satır değiştirme doğrudan yeni kadraja gidiyor.
- Kaydırma, dönüş tween'i **bitince** açılıyor — `ScrollRect` `Clamped` ve her
  `LateUpdate`'te içeriği sınırlara çekiyor, ortada açılsa ikisi aynı değeri çekiştirir.

Popup'ın kendisi hâlâ animasyonsuz — istek zoom içindi, popup girişi Ş6.

### [x] Ş4 — Satın alma  *(bitti, D-034)*

- Popup BUY → `Evaluate` → cüzdan → sahiplik → `Save` → `ClosePreview` → `Refresh` →
  liste yenileniyor
- Hayalet silinir, prop katı gelir, alınan ürün listeden düşer, panel **açık kalır** (MS4)
- **`Refresh()` burada public oldu** — Ş1 ve Ş3'te "çağıranı yok" diye ertelenmişti,
  çağıran tam olarak burada doğdu.
- **`HudWalletSource`'a dokunulmadı:** D-022 kaynağı zaten oturuma bağlamış, yani ana
  ekranın HUD'ı canlı ve altın kendiliğinden düşüyor. Kaydı bir kez okuyan bir HUD
  bayat kalırdı ve kök CLAUDE.md görünüme ikinci bir kontrol eklemeyi yasaklıyor.

**İki görünürdeki fazlalık bilinçli:** `Evaluate` satır çizilirken de burada da
çağrılıyor (panel açık kaldığı için bakiye bayatlar), ve `Evaluate` onay verse bile
cüzdana yine soruluyor (bakiyenin tek yazıcısı o). Biri karar, diğeri işlem.

**Atomiklik yapısal:** para ve sahiplik aynı `GameSession`'da, `Save()` ikisini birlikte
yazıyor — "parası gitti ama eşya gelmedi" ifade edilemiyor.

**Doğrulama:** para düşüyor, prop katı görünüyor, oyunu kapat-aç → prop hâlâ orada.
**Henüz Unity'de çalıştırılmadı.**

### [x] Ş5 — Alınamayan durumlar  *(bitti, D-035 — kullanıcının dört değişikliğiyle)*

Kullanıcı planı burada değiştirdi:

- **Alanı kilitli prop listede HİÇ görünmüyor**, açılana kadar. Bu MS2'nin yarısını
  tersine çeviriyor (ben "görünsün" diye önermiştim). Bedeli tek cümleyle kayda geçti:
  artık hiçbir şey oyuncuya Meydan'ı almanın yeni proplar açtığını söylemiyor.
  "Yakında" göstergesi sonraki bir adımın işi ve M7'nin cevabını bekliyor.
- **Parası yetmeyen satır karanlık** — `CanvasGroup` alfası, panelin koyu zemininde
  karanlıklaşma olarak okunuyor. Yalnızca alfa; satırın BUY'ı **hâlâ basılabilir**,
  önizlemeyi açıyor.
- **Alınabilecekler en yukarıda, sonra fiyat artan.** Sıralama `ShopItems`'ın içinde,
  imzası `softMoney` aldı — "alınabilir yukarıda" bakiyeye bağlı bir kural.
- **`List.Sort` kullanılmadı**, insertion sort: `List.Sort` kararsız ve eşit fiyatlı
  proplar her açılışta yer değiştirirdi. Eşitlikte katalog sırası korunuyor, testle
  sabitlendi.
- **Görünür şekilde reddeden şey popup'ın BUY'ı** (`interactable = false`). Öncesinde
  canlıydı ve parası yetmeyince hiçbir şey yapmıyordu — planın uyardığı ölü düğmenin
  tam kendisi.

Planın "Önce Meydan" yazısı **gerekmiyor**: gizlenen bir şeyin sebebini yazacak yer yok.

**Testler yeniden yazıldı, silinmedi**, ve üç yeni test eklendi (alan sahipliği propu
geri getiriyor, iki anahtarlı sıralama, eşit fiyatta yazarlanmış sıra).

**Ek (D-037):** alınamayanın **BUY'ı kırmızı** — satırda ve popup'ta. Soluklaşmanın
üstüne biniyor. Yeni referans eklenmedi (`Button.targetGraphic` boyanıyor), yani sahne
yeniden kurulmadı. Renkler `MetaShopView`'da serialize field; kırmızı, projenin Start
Over düğmesindeki kırmızıyla aynı.

**Doğrulama:** pahalı bir ürün karanlık görünüyor ve popup'ın BUY'ı kapalı; meydan
alınmadan masa listede hiç yok. **Testler ÇALIŞTIRILMADI** — Unity projeyi kilitliyor,
bu adım yalnızca derlemeyle doğrulandı.

### [ ] Ş6 — Sanat ve cila

- Market düğmesi ikonu (**sanat yok, yer tutucu gerekiyor** — bkz. MS5)
- Onay popup'ı çerçevesi (`Art/UI/Popup/` altında Day Complete ve Fail sanatı var,
  oradan gelebilir)
- Yerleşim ayarları

## 5. Açık sorular

Hiçbiri Ş1'i bloke etmiyor; her biri kendi adımında gerekiyor.

| # | Soru | Önerim | Bloke ettiği |
|---|---|---|---|
| MS1 | **Fiyat listede de yazsın mı?** Tarifinde liste = simge + isim + BUY, fiyat yalnızca popup'ta. | **Yazsın.** Yoksa oyuncu fiyat karşılaştırmak için her ürünün popup'ını tek tek açmak zorunda. Satırda soluk küçük bir sayı yeter. | Ş2 |
| MS2 | **Parası yetmeyen / alanı kilitli ürünler listede görünsün mü?** | **Görünsün.** Neyin geleceğini ve kaça olduğunu görmek dükkânın esas işi. | Ş2, Ş5 |
| MS3 | **İptal edince hayalet kalsın mı?** | **Gitsin.** Kalırsa haritada sahipsiz bir yarı saydam prop durur ve oyuncu onu aldığını sanabilir. | Ş3 |
| MS4 | **Satın alma sonrası panel açık kalsın mı?** | **Kalsın.** Üst üste birkaç şey almak akıcı olur; kapanırsa her alışta düğmeye tekrar basmak gerekir. | Ş4 |
| MS5 | **Market düğmesi ikonu?** Projede market/dükkan sanatı YOK. | Yer tutucu (renkli kare + "SHOP") ile başlayıp Ş6'da gerçek ikonu koymak. | Ş6 |
| MS6 | **Popup dışına dokunmak iptal etsin mi?** | ~~Etsin~~ — **KAPANDI, hayır (D-038).** Kullanıcı backdrop'u sahneden sildi, koddan da kalktı. Çıkış yolu CANCEL. Yan etki: önizlemede harita kararmıyor, hayalet daha net. | — |

**Önceki plandan devam eden ve hâlâ açık olanlar:** M1 (fiyatlar — tipik bir günün
geliri bilinmeden 16 fiyat yazmak rastgele olur), M7 (meydan hangi propları açıyor),
ve yuva pozisyonlarının Meta Editor'den yazarlanması. Dükkân bunlar olmadan da
çalışır ama içi boş görünür.

## 6. Kapsam dışı (bilerek)

- **Kural değişikliği yok.** Fiyat/alan/gün kuralları `MetaPurchase`'ta ve
  dokunulmuyor.
- **Gem yok.** Meta tarafı SoftMoney, D-015'ten beri.
- **Satın alma animasyonu yok.** Prop hayaletten katıya geçiyor, arada efekt yok.
- **Kategori sekmesi yok.** 16 ürün için düz liste yeterli.
- **Geri alma / satma yok.** Satın alma kalıcı.
