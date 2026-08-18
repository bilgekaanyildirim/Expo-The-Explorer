# EXPO THE EXPLORER
## Game Design Document (v0.5)

*v0.5: Powerup 3 (Gürültü Temizleme) onaylandı; powerup kazanım/kullanım kuralları netleşti (meta-ilerleme + Gem).*
*v0.6: Etkileşim yöntemi netleşti — Drag & Drop birincil etkileşim olarak onaylandı (bkz. Bölüm 5).*
*v0.7: Powerup sistemi geliştirme kapsamı dışına alındı (ertelendi, tasarım saklanıyor). Sonsuz Mod tasarımdan kaldırıldı — tek mod Günlük Hedef Modu. Can bitip gün yeniden oynanınca o günde kazanılan Level XP'sinin kaybedildiği netleşti (bkz. Bölüm 6, 10). ~~(v1.0'da geçersiz: XP sistemi kaldırıldı.)~~*
*v0.9: Bahşişten **sabır azalma katsayısı kaldırıldı** — bahşiş yalnızca sipariş bedeli, bahşiş oranı ve hız kademesinden hesaplanıyor. Sabır tipi artık parayı değil, süre sınırını ~~ve XP çarpanını~~ etkiliyor (bkz. Bölüm 8, 9, 10).*
*v1.1: **3 kademeli Hız Bonusu (Yıldırım/Hızlı/Standart) kaldırıldı.** Yerine yine üç kademe geldi, ama eşikleri **biletin kendi süresinin üçte birleri** (kalan oran 0.666 ve 0.333) — yani zamanlama barının rengini değiştiren eşiklerin ta kendisi. Oyuncunun gördüğü renk = aldığı bahşiş kademesi. Gerekçe: eski kademelerin "saniye/öğe" eşikleri oyuncuya görünmüyordu (bkz. Bölüm 9).*
*v1.0: **XP / Level sistemi tasarımdan tamamen kaldırıldı.** Deneyim puanı, level eşikleri, level rozeti ve level'a bağlı ödüller yok; bunlara dair kod, config ve UI projeden silindi (`.claude/decisions.md` D-009). Sabır tipi artık yalnızca süre sınırını belirliyor (XP çarpanı kalmadı), retry'da kaybedilen bir kazanım yok ve oyunda şu an oturumlar arası kalıcı hiçbir veri tutulmuyor. Meta-ilerlemenin yerini **Day dizisi** aldı (bkz. Bölüm 11, `docs/DaySystem_Roadmap.md`).*
*v0.8: Her yemeğin kendi fiyatı olduğu netleşti. Bir teslimatın parası ikiye ayrıldı: **sipariş bedeli** (yemek fiyatlarının toplamı, sabit ve garanti) + **bahşiş** (tek değişken parça, sipariş bedeliyle orantılı). Hız ve sabır artık yalnızca bahşişi çarpar (bkz. Bölüm 9, 10).*

---

## 1. Genel Bakış

**Tür:** Time-management / Order-matching (Sipariş eşleştirme) — Mobil
**Platform:** Mobil (dokunmatik)
**Referans oyunlar:** Overcooked'ın "expo" istasyonu mantığı + Diner Dash tarzı zaman yönetimi + eşleştirme oyunlarının (match/sıralama) netliği

**Çekirdek Fantezi:** Oyuncu, mutfak ile müşteri arasındaki köprüdür — "expo" görevlisi. Mutfaktan gelen yemekleri doğru bilete, doğru modifikasyonlarla, doğru sırayla ulaştırmakla sorumludur. Gerilim; hem doğru seçim yapma (dikkat) hem de zamana karşı yarışma (hız) arasındaki dengeden gelir.

**Tek cümlelik pitch:** Doğru yemeği, doğru bilete, zaman bitmeden yerleştir — kafan karışırsa mutfak seni yer.

---

## 2. Çekirdek Oyun Döngüsü (Core Gameplay Loop)

```
BİLET GELİR → BOARD'A YEMEK DAĞILIR → OYUNCU DOĞRU YEMEKLERİ
TEPSİYE TOPLAR → TEPSİ DOLUP DOĞRULANIRSA → BİLET TESLİM EDİLİR
(Para + Bahşiş kazanılır) → YENİ BİLET GELİR → (döngü tekrar eder)
```

Yanlış yerleştirme olursa: **CAN AZALIR → YERLEŞTİRİLEN YEMEKLER BOARD'A GERİ DAĞILIR → OYUNCU YENİDEN TOPLAMAK ZORUNDADIR**

Bu döngü saniyeler içinde tekrar eder ve oyunun "moment-to-moment" gerilimini oluşturur. Üstüne binen sistemler (sabır tipleri, hız bonusu, ekonomi, Day dizisi) bu temel döngüyü anlamlandırır ve uzun vadeli motivasyon sağlar.

---

## 3. Bilet Sistemi (Ticket System)

*(v0.2 güncellemesi: prototip ekran görüntüsüne göre netleştirildi)*

### 3.1 Bilet Slotu Yapısı

Biletler serbest bir kuyrukta değil, **3 sabit paralel slotta** yaşar. Aynı anda en fazla 3 aktif bilet vardır; biri teslim edilip slotundan çıktığında, yeni bir bilet aynı slota gelir. Bu, oyuncunun her zaman **3 farklı önceliği aynı anda yönetmesi** anlamına gelir — GDD Bölüm 2'deki "gürültü havuzu" zorluğuyla birlikte çalışan ikinci bir zorluk katmanı.

*(Düzeltme: Önceki sürümde bilet üzerinde "1/2/3 sıra numarası rozeti" olduğu belirtilmişti — bu, prototip çiziminde kalan bir hataydı ve kaldırıldı. Slotların kendisi sabit ve 3 tane, ama üzerlerinde numaralandırma yok.)*

### 3.2 Bilet Kartı Bilgi Hiyerarşisi

Her bilet dikey bir kart olarak tasarlanır, yukarıdan aşağıya doğru şu bilgi hiyerarşisini takip eder:

1. **Üst blok:** Müşteri avatarı/adı + kalan süre (sayısal, örn. "3:45")
2. **Ana görsel:** Yemeğin en temel (modifikasyonsuz) hâlinin fotoğrafı
3. **Modifikasyon listesi:** İkon + `X` (çıkar) veya `+` (ekle) gösterimiyle (örn. marul-X, peynir-+, domates-X)
4. **Alt blok:** Yan yemek + içecek görselleri
5. **Zaman barı:** Kartın en altında, kalan süreyi gösteren yatay bir ilerleme çubuğu (bkz. Bölüm 7 — bu bar **kalan süreyi** gösterir, tepsi doluluğunu değil)

**Kenar rengi = sabır tipi:** Kartın dış çerçeve rengi, o müşterinin **sabit sabır tipini** gösterir (bkz. Bölüm 8) — örn. krem/nötr, yeşil (sabırlı), kırmızı (sabırsız). Bu renk oturum boyunca değişmez; sadece kalan süre sayısı ve zaman barı azalır.

**Not — tepsi sayacı:** Kullanıcının orijinal konseptinde `0/3` gibi bir envanter sayacı da tanımlanmıştı; prototip ekran görüntüsünde bu sayaç görünmüyor (yerine zaman barı var). Bu iki gösterge **birbirinin yerine geçmiyor** — biri süreyi, diğeri tepsi doluluğunu izler. Tepsi sayacının nihai UI'da nerede/nasıl gösterileceği netleştirilmeli (bkz. Bölüm 14, madde 11).

### ✅ Çözüldü — Tepsi Sayacı ve Kontrol Mekaniği

Tepsi sayacı **sadece toplam ürün sayısını** sayar (ana yemek + yan yemek + içecek → örn. 3 ürünlük bir bilet için `0/3`). **Modifikasyonlar sayaca dahil değildir** — ayrı bir "öğe" olarak sayılmazlar.

Mekanik şu şekilde işler:
1. Oyuncu ürünleri tepsiye yerleştirdikçe sayaç artar (`1/3`, `2/3`, ...).
2. Sayaç, biletin gerektirdiği ürün sayısına ulaştığında (tepsi "dolduğunda"), sistem **otomatik olarak** tüm tepsiyi kontrol eder: doğru ürünler mi VE doğru modifikasyonlarla mı?
3. **Hepsi doğruysa:** Bilet otomatik olarak gönderilir (bkz. Bölüm 5) → ödül kazanılır.
4. **Herhangi biri yanlışsa** (yanlış ürün ya da yanlış modifikasyonlu doğru ürün): hata sayılır → can azalır, tepsideki tüm öğeler board'a geri dağılır.

Yani kontrol **anlık değil, tepsi dolduğunda topluca** yapılır — oyuncu yanlış bir ürünü tepsiye koyduğu anda ceza almaz, sonucu tepsi tamamlanınca öğrenir. (Bu, aynı zamanda Bölüm 5'teki ilgili açık soruyu da çözer.)

---

## 4. Board / Yemek Dağıtım Sistemi

Bilet geldiğinde mutfaktan board'a yemekler dağılır. Board'daki yemek havuzu iki kaynaktan beslenir:

- **Zorunlu havuz:** Mevcut aktif bilet(ler)in gönderilmesi için gereken *minimum* yemek seti — oyuncu her zaman en az bir bileti tamamlayabilecek malzemeye sahip olmalı.
- **Gürültü havuzu (Noise pool):** Oyuncunun henüz göremediği, **ileride** gelecek biletlerden sızan yemekler. Bu, board'u kalabalıklaştırıp doğru seçim yapmayı zorlaştıran ana zorluk kaynağıdır.

**Tasarım ilkesi:** Gürültü havuzunun oranı, zorluk eğrisinin ana kontrol koludur (bkz. Bölüm 8). Erken seviyelerde düşük gürültü oranı, ileri seviyelerde yüksek gürültü oranı + benzer görünümlü yemekler (görsel olarak birbirine yakın ama farklı modifikasyonlu yemekler) kullanılabilir.

### 💡 Öneri — Board Üst Sınırı ve Gürültü Yaşam Döngüsü

Board zaten sabit boyutlu bir grid (prototipte 6×5 = 30 hücre, bkz. Bölüm 13). Bu, ayrı bir sayısal limit tanımlamaya gerek bırakmadan **doğal bir üst sınır** sağlıyor — önerilen yaklaşım:

- **Üst sınır = grid kapasitesi.** Board'a yeni bir öğe (zorunlu ya da gürültü) spawn edilmeden önce sistem boş hücre olup olmadığını kontrol eder. Boş hücre yoksa, spawn isteği bir **bekleme kuyruğuna** alınır ve ilk hücre boşaldığında (oyuncu bir öğeyi tepsiye alınca) otomatik olarak yerleştirilir.
- **Gürültü öğeleri kendiliğinden kaybolmaz/süresi dolmaz.** Bir öğe board'da göründükten sonra, oyuncu onu (doğru ya da yanlış şekilde) kullanana kadar orada kalır. Zamanla otomatik silinme gibi bir mekanik önerilmiyor — hem ekstra karmaşıklık getirir hem de "neden yemeğim kayboldu" gibi kafa karıştırıcı bir his yaratabilir.
- **Neden bu işe yarar:** Board'un dolup "tıkanması" zaten kendi başına bir zorluk sinyalidir — grid ne kadar dolarsa doğru öğeyi bulmak o kadar zorlaşır. Bu, ayrı bir "üst sınır dolunca ne olur" mekaniği tasarlamaya gerek kalmadan zorluk eğrisiyle doğal olarak örtüşüyor.
- **İsteğe bağlı ince ayar:** Zorunlu havuzun her zaman öncelikli spawn edilmesi (gürültüden önce) garanti edilebilir, böylece board tıka basa dolsa bile oyuncunun en az bir bileti tamamlaması hep mümkün kalır (bkz. Bölüm 4'teki zorunlu havuz ilkesi).

*(Bu bir öneri/başlangıç noktasıdır — sayılar ve kuyruk davranışı prototipleme sırasında ince ayar gerektirebilir.)*

---

## 5. Tepsi / Teslimat Sistemi

- Her aktif biletin altında bir **tepsi alanı** bulunur — bu, prototipteki koyu renkli kutunun ta kendisidir.
- Oyuncu board'dan doğru yemeği seçip tepsiye yerleştirir (dokunmatik: **drag & drop**).

### ✅ Çözüldü — Etkileşim Yöntemi

Birincil etkileşim yöntemi **drag & drop** olarak onaylandı. Mobilde küçük parmak hedeflerinin hataya açık olma riskine karşı şu önlemler alınacak:
- Drop zone/hitbox'lar görsel öğe sınırlarından daha **cömert** tutulacak.
- Sürükleme sırasında geçerli bırakma alanı **highlight** ile vurgulanacak.
- Geçersiz bırakmada **snap-back** (öğenin board'daki konumuna geri dönme) animasyonu gösterilecek.
- **Yerleştirilen her ürün, tepsi alanında anında görünür** — yani tepsi, oyuncunun o ana kadar topladığı ürünleri gerçek zamanlı olarak gösterir (bir sonraki düzeltmeye bkz: bu bir "önizleme" değil, oyuncunun aktif olarak doldurduğu gerçek tepsidir).
- Tepsi sayacı `x/y` şeklinde ilerler (bkz. Bölüm 3 — sadece ürün sayısı, modifikasyonlar dahil değil).
- **Tepsi doluyor ve tüm öğeler doğruysa:** Bilet otomatik olarak teslim edilir → Para + (hıza bağlı) bahşiş kazanılır.
- **Tepsi doluyor ama bir öğe yanlışsa:** Can azalır, tepsideki tüm öğeler board'a geri dağılır, oyuncu yeniden toplamaya başlar.

### ✅ Çözüldü — Kontrol Zamanlaması

Kontrol **tepsi dolduğunda** (yani gerekli ürün sayısına ulaşıldığında) topluca yapılır — yerleştirme anında değil. Oyuncu yanlış bir ürünü tepsiye koyduğunda anında bir ceza almaz; hata veya başarı, tepsi tamamlandığı anda "gönder" onayı gibi davranan otomatik kontrolle ortaya çıkar. Bu, oyuna biraz daha affedici ama yine de gerilim dolu bir "his" verir — oyuncu son ürünü yerleştirene kadar tam olarak doğru yapıp yapmadığını bilemez.

### 5.1 Düzeltme — "Önizleme Kutusu" Kavramı Yanlıştı

Önceki sürümde, bilet slotunun altındaki koyu kutu ayrı bir "tamamlanmış tepsi önizlemesi" olarak tanımlanmıştı. **Bu yanlıştı.** O kutu, ayrı bir önizleme değil — **tepsinin kendisidir.** Oyuncu board'dan ürün yerleştirdikçe, o ürünler doğrudan bu alanda birikerek görünür (üstteki maddede güncellendi). Tepsi dolup doğrulandığında bilet oradan gönderilir; ayrı bir "önizleme → asıl gönderim" gibi iki aşamalı bir akış yoktur.

### 5.2 Powerup Sistemi

**⚠️ Geliştirme kapsamı dışında (ertelendi):** Powerup sistemi şu anki geliştirme fazında **kodlanmayacak**. Tasarım aşağıda referans olarak saklanıyor; ne zaman geliştirileceğine dair bir tarih/faz henüz belirlenmedi. Bu bölümdeki açık soru (kazanım miktarı/Gem maliyeti) da bu yüzden şimdilik ele alınmıyor — sistem tekrar kapsama alındığında netleştirilecek.

Prototipte, alt kısımda görülen **3 gri daire ikonu** aslında bir **powerup sistemidir** (önceki sürümde işlevi bilinmiyordu, bkz. Bölüm 14):

1. **Powerup 1 — Oto-Toplama:** Board'daki zorunlu havuz ürünlerini (aktif biletler için gereken ürünleri) otomatik olarak ilgili tepsilere yerleştirir.
2. **Powerup 2 — Süre Sıfırlama:** Aktif biletlerin sürelerini sıfırlar/yeniler.
3. **Powerup 3 — Gürültü Temizleme (Board Netleştirme):** ✅ Onaylandı. Birkaç saniyeliğine gürültü öğelerini soluklaştırır / zorunlu havuz öğelerini vurgular, böylece oyuncu doğru ürünü çok daha kolay bulur.

Bu üçlü, oyunun üç ana zorluk kaynağını (ürün toplama, zaman baskısı, görsel karmaşa) birer birer hafifleten tutarlı bir set oluşturuyor.

### ✅ Çözüldü — Powerup Kazanım ve Kullanım Kuralları

Powerup'lar iki yoldan elde edilir:
1. **Meta-ilerleme yoluyla:** Belli bir miktarda powerup, belirli etkinlikler (events) tamamlandığında ödül olarak verilir. *(v1.0: "level atlandıkça" kazanım yolu geçersiz — level sistemi yok. Powerup sistemi zaten ertelenmiş durumda; hayata geçerse yeni bir kazanım tetiği tanımlanmalı, doğal aday Day tamamlama.)*
2. **Gem ile satın alma:** Oyuncu, Gem harcayarak ek powerup satın alabilir (bkz. Bölüm 10).

*(Bu, powerup'ların sınırsız bir kaynak olmadığını, hem ilerlemeyle kazanılan hem de Gem ile desteklenen bir "stok" sistemi olduğunu gösteriyor.)*

### Ertelendi (açık soru değil, kapsam dışı):
Meta-ilerleme yoluyla verilen powerup miktarı, hangi etkinliklerin ödüllendirdiği, Gem maliyeti ve kullanım limiti/cooldown soruları — powerup sistemi geliştirmeye alınmadığı için şimdilik yanıtlanmıyor.

---

## 6. Can Sistemi

- Oyuncunun sınırlı bir can havuzu vardır.
- Yanlış teslimat → can azalır.

### ✅ Çözüldü — Can Sıfırlanınca

Can sıfırlanınca **o gün biter** ve oyuncu o günü **yeniden oynamak zorunda kalır**. Ancak tekrar denemede **zorluk biraz düşürülür** (örn. daha az gürültü, biraz daha uzun süreler) — amaç, oyuncunun şevkinin tamamen kırılmaması. Ayrıca oyuncu **Gem harcayarak canını doldurup mevcut günde devam edebilir** ("continue" mekaniği, bkz. Bölüm 10).

### ❌ Geçersiz (v1.0) — Retry'da XP Kaybı

Bu kural, XP/Level sistemine bağlıydı ve o sistem oyundan tamamen kaldırıldığı için **artık geçerli değil** (bkz. Bölüm 10, `.claude/decisions.md` D-009). Bugünkü davranış: can sıfırlanınca gün baştan oynanır ve oyuncu yalnızca o denemedeki ilerlemeyi kaybeder — kalıcı profile işlenip geri alınan bir kazanım yoktur, çünkü kalıcı profile işlenen hiçbir şey yoktur. "Continue" mekaniğiyle farkı da sadeleşti: Gem ile devam etmek günü bitirmeden sürdürmeyi sağlar, retry ise günü baştan başlatır; para tarafındaki tek geri alma, başarılı bir günü gönüllü olarak yeniden oynamakta (bkz. Bölüm 11 — Day Complete "Retry").

### Açık soru:
Zorluk düşürme tam olarak nasıl uygulanacak — hangi parametre (gürültü oranı, süre, bilet sıklığı) ne kadar düşürülecek? Bu, dengeleme (balancing) aşamasında somutlaştırılmalı.

---

## 7. Zaman Sınırı Sistemi

*(v0.2: temel davranış prototip görüntüsüyle doğrulandı)*

- **Süre bilet bazlıdır:** Her biletin kendi geri sayımı vardır (prototipte sayısal olarak "3:45" gibi gösteriliyor).
- **Görsel gösterim:** Kartın altındaki yatay bar, kalan süre azaldıkça küçülür (mavi renk — bkz. Bölüm 3.2).
- Süre sınırı, biletin sabır tipiyle doğrudan ilişkilidir: sabırsız müşterinin toplam süresi daha kısadır (bkz. Bölüm 8).

### ✅ Çözüldü — Süre Dolunca

Süre dolduğunda: **can azalır ve bilet iptal edilir** (bilet slotundan kaldırılır, o slota yeni bir bilet gelir — bkz. Bölüm 3.1). Yani süre aşımı, yanlış teslimatla aynı ceza mekaniğini (can kaybı) tetikler; tek fark, tepsideki öğelerin board'a geri dağılması yerine biletin doğrudan iptal edilmesidir.

---

## 8. Müşteri Sabır Sistemi

Her müşterinin 3 sabır tipi vardır:

| Tip | Süre | Ekonomik Etki (v0.9) |
|---|---|---|
| **Sabırsız** | Kısa süre sınırı | Zaman baskısı yüksek — hız kademesini tutturmak zor |
| **Normal** | Orta süre sınırı | Orta zaman baskısı |
| **Sabırlı** | Uzun süre sınırı | Rahat zaman — acele gerektirmez |

**v0.9 — sabır tipi bahşişi doğrudan etkilemez.** Sabır tipinin ekonomik
ağırlığı tek bir dolaylı yoldan gelir: verdiği süre sınırı, hangi hız
kademesini yakalayabileceğini belirler. Bahşiş formülünde sabır katsayısı
yoktur (bkz. Bölüm 9). *(v1.0: ikinci yol olan XP çarpanı, XP sistemiyle
birlikte kaldırıldı — sabır tipinin süre dışında hiçbir etkisi kalmadı,
bkz. Bölüm 13 soru 22.)*

Bu sistem oyuncuyu her an **önceliklendirme** yapmaya zorlar: aynı anda birden fazla bilet varsa, sabırsız müşteriye önce mi yetişmeli yoksa yüksek öğe sayılı sabırlı bileti mi hazırlamalı?

**Görsel gösterim (v0.2 — prototiple doğrulandı):** Sabır tipi, bilet kartının **sabit kenar rengiyle** belirtilir (örn. kırmızı = sabırsız, yeşil = sabırlı, krem/nötr = normal). Bu renk oturum boyunca değişmez; sadece süre sayısı ve zaman barı azalır.

### ⛔ Geçersiz (v0.9) — Bahşiş Azalma Eğrisi

**Bu karar v0.9'da kaldırıldı:** bahşiş artık zamanla kademeli olarak
azalmıyor; sabır tipinin bahşiş üzerinde hiçbir çarpanı yok. Geç kalan
oyuncu bahşişi **hız kademesi** üzerinden kaybeder (Standart kademe =
çarpan yok), sipariş bedelini ise hiçbir koşulda kaybetmez (bkz. Bölüm
9). Aşağıdaki özgün karar, tasarımın nereden geldiğini kaybetmemek için
kayıtta bırakılmıştır — **uygulanmıyor.**

<details>
<summary>v0.2–v0.8 arası geçerli olan özgün karar</summary>

Bahşiş azalma eğrisi **kademelidir** (lineer değil). Açıklama:
- **Lineer** olsaydı: bahşiş, süre geçtikçe sabit bir oranla, düz bir çizgi gibi sürekli azalırdı (örn. her saniye %1 düşer).
- **Kademeli** (seçilen yaklaşım): bahşiş, belirli zaman eşiklerine ulaşıldığında aniden bir basamak düşer ve o seviyede sabit kalır — örn. ilk 10 saniye %100 bahşiş, sonra aniden %70'e düşer, sonra %40'a, vb.

**Eşikler dinamiktir** — sabit saniye değerleri değil, bilet karmaşıklığına (öğe sayısına) göre ölçeklenir. Bu ilke, Bölüm 9'daki hız kademesi eşikleri için de aynı şekilde geçerlidir.

</details>

**Not:** Eşiklerin öğe sayısıyla ölçeklenmesi ilkesi ölmedi — Bölüm 9'daki
**hız kademesi** eşikleri için aynen geçerli olmaya devam ediyor.

---

## 9. Bahşiş Kademeleri (biletin süresinin üçte birleri)

*(v1.1: bu bölüm eskiden "Hız Bonusu (3 Kademeli)" idi. Kademe sayısı yine üç,
ama eşikleri tamamen değişti — gerekçe aşağıda.)*

Bahşiş oranı, biletin **kalan süresinin kendi süre sınırına oranına** göre üç
kademeden birini alır. Bu iki eşik, zamanlama barının rengini değiştiren
eşiklerin ta kendisidir — **oyuncunun gördüğü renk, aldığı bahşiş kademesidir:**

| Kalan oran | Bar rengi | Bahşiş oranı |
|---|---|---|
| `> 0.666` | yeşil | `TipRateFull` |
| `0.333 – 0.666` | turuncu | `TipRateWarning` |
| `<= 0.333` | kırmızı | `TipRateCritical` |

```
Kalan oran = kalan süre / biletin süre sınırı
Bahşiş     = Sipariş Bedeli × (kademenin oranı)
```

**Eşikler oran olduğu için sabır tipine göre kendiliğinden ölçeklenir:** 45
saniyelik sabırsız bilet her 15 saniyede bir kademe düşer, 150 saniyelik
sabırlı bilet her 50 saniyede. Bölüm 10'daki "sabır tipi parayı yalnızca
verdiği süre üzerinden etkiler" ifadesinin somut mekanizması budur.

**Neden eski kademeler kaldırıldı:** Yıldırım/Hızlı/Standart eşikleri
"saniye/öğe × öğe sayısı" ile hesaplanıyordu — oyuncunun ekranda göremediği,
bilete göre değişen görünmez sınırlardı. Yeni eşikler zaten barda renk olarak
görünüyor.

**Öğe sayısı hiçbir eşiği ölçeklemez.** Büyük sipariş daha alt kademeye
düşebilir, ama yalnızca gerçekten toplanması uzun sürdüğü için.

**Not — salt görsel olan ayar:** `TicketCardVisualsConfig.TimerSegmentSeconds`
(varsayılan 5 sn) yalnızca bar üzerindeki bölme tiklerinin sıklığını belirler;
hiçbir ödemeye etki etmez. Kademe eşikleri yukarıdaki iki orandır.

### ✅ Çözüldü — Sipariş bedeli sabittir, yalnızca bahşiş değişkendir (v0.8)

Bir teslimatın kazancı iki parçadan oluşur ve **yalnızca ikincisi** hıza/sabıra göre değişir:

| Parça | Nasıl hesaplanır | Değişken mi? |
|---|---|---|
| **Sipariş Bedeli** | Bilette istenen yemeklerin **fiyatlarının toplamı** | Hayır — başarılı her teslimatta tam ödenir |
| **Bahşiş** | `Sipariş Bedeli × Bahşiş Oranı × Hız Çarpanı` | Evet — tek değişken parça |

```
Teslimat Kazancı = Sipariş Bedeli + Bahşiş
```

- **Her yemeğin kendi fiyatı vardır** ve bu fiyat yemeğin içerik verisinde yazılıdır, koda gömülmez. Burger, patates ve kola aynı parayı etmez.
- Bahşiş fiyatla **orantılıdır**: pahalı bir siparişi hızlı yetiştirmek, ucuz bir siparişi aynı hızda yetiştirmekten daha çok bahşiş getirir. "Bahşiş Oranı" tek bir global denge ayarıdır (örn. %20) — fiyat yemekte, oran ekonomi ayarlarında durur.
- Hız kademesi artık **yalnızca bahşişi** çarpar, sipariş bedelini asla azaltmaz. Bahşişin alt sınırı **sıfırdır**: geç kalan oyuncu bahşişi kaybedebilir ama yemeğin parasını her zaman alır.
- **Sabır tipi bahşiş formülünde yer almaz (v0.9).** Bahşişi belirleyen tek değişken hız kademesidir; sabır tipi parayı yalnızca dolaylı etkiler — verdiği süre, hangi kademeyi yakalayabileceğini belirler (bkz. Bölüm 8). *(v1.0: eskiden ikinci bir ödül yolu olan XP çarpanı kaldırıldı.)*
- Öğe sayısı artık kazancı doğrudan belirlemez (fiyat toplamı zaten karmaşıklıkla birlikte büyür); ancak hız ve sabır **eşikleri** öğe sayısıyla ölçeklenmeye devam eder — bu kural değişmedi (bkz. Bölüm 8 ve yukarısı).

### ✅ Çözüldü — Kademe Eşikleri

Kademe eşikleri **dinamiktir** — sabit bir yüzde/saniye değeri değil, bilet karmaşıklığına (öğe sayısına) göre ölçeklenir. Karmaşık biletlerde oyuncuya doğal olarak daha fazla süre gerektiği için bu, adil bir yaklaşımdır (bkz. Bölüm 8'deki aynı ilke).

---

## 10. İlerleme Sistemi (Progression)

**Not:** Prototip HUD'unda 3 ayrı kaynak görülüyor: **Health** (Can), **Soft Money** (Para) ve **Gem**. Gem'in rolü netleşti (aşağıda).

- **Soft Money (Para):** Her başarılı teslimatta kazanılır — **sipariş bedeli (sabit) + bahşiş (değişken)** olarak iki parça hâlinde (bkz. Bölüm 9). Sipariş bedeli, bilette istenen yemeklerin fiyat toplamıdır ve teslimat gerçekleştiği sürece tam ödenir; bahşiş bu bedele oranlıdır ve hıza/sabıra göre değişir. Doğrudan ekonomik ödül (ileride meta-oyun için harcanabilir, bkz. Bölüm 12).
- **Gem:** Ayrı bir **sert para birimi**. Kullanım alanları:
  1. **Powerup satın almak/doldurmak** (bkz. Bölüm 5.2)
  2. **"Devam et" (continue):** Can bitip gün başarısız olduğunda, Gem harcayarak canı doldurup mevcut günde devam etmek (bkz. Bölüm 6)
### ❌ Kaldırıldı (v1.0) — Deneyim Puanı (XP) / Level

Oyunda **XP ve oyuncu Level'ı yoktur.** Daha önce tasarlanan "her teslimatta XP kazan, eşik aştıkça level atla, ilerleme kalıcı profile işlensin" yapısı — XP eşik tablosu, sabır tipine bağlı XP çarpanı, level rozeti ve level'a bağlı ödüller dahil — tasarımdan ve koddan tamamen çıkarıldı (`.claude/decisions.md` D-009). Level'ın üstleneceği düşünülen roller şu an başka yerlerde:

  - **Zorluk eğrisi ve içerik kilidi:** elle tasarlanan **Day dizisi** taşıyor (her Day kendi board dağılımını, bilet dizisini ve süre sınırlarını getiriyor — bkz. Bölüm 11, `docs/DaySystem_Roadmap.md`).
  - **Kozmetik ödüller / meta-oyun erişimi:** hâlâ açık, ama artık bir level eşiğine değil, Day ilerlemesine bağlanmalı (bkz. Bölüm 12).

**Kalıcılık notu:** XP kaldırılınca oyunda diske yazılan tek veri de kalkmış oldu — şu an oyun oturumlar arası **hiçbir şey saklamıyor** (para, can ve hangi Day'de olunduğu dahil). Kayıt sınırı (`PlayerProfile`/`PlayerProfileStore`) kodda duruyor ama boş ve bağlı değil; ilk doldurulacak alan büyük ihtimalle oyuncunun hangi Day'de olduğu.

---

## 11. Bölüm / Oturum Yapısı

### ✅ Çözüldü — Tek Mod: Günlük Hedef Modu

Oyunda **tek bir mod** vardır: **Günlük Hedef Modu.** Her "gün" belirli sayıda sipariş tamamlanması gerekir; hedefe ulaşınca gün biter, sonuç ekranı (yıldız, sipariş bedeli toplamı, bahşiş toplamı) gösterilir. Can biterse gün başarısız sayılır ve yeniden oynanır — bkz. Bölüm 6.

*(v0.7: Önceki sürümde önerilen "Sonsuz Mod" tasarımdan tamamen kaldırıldı — böyle bir mod geliştirilmeyecek. Bu karar, sonsuz mod'a bağlı zorluk artışı ve mod-arası progression paylaşımı sorularını da geçersiz kılıyor.)*

---

## 12. Meta Oyun (Gelecek Faz — Vakit Kalırsa)

Uzun vadeli oynanabilirlik için düşünülen ek katman:

- **Restoran Haritası:** Oyuncunun kazandığı parayla geliştirebileceği bir restoran/harita ekranı.
- **Geliştirmeler:** Her geliştirme, oyuna **yeni yemek ve içecek çeşitleri** kazandırır (yeni bilet varyasyonları, yeni modifikasyon tipleri).
- Bu sistem, çekirdek döngüyü değiştirmez — sadece bilet çeşitliliğini ve oyuncunun uzun vadeli hedeflerini zenginleştirir.

**Not (v0.2):** Prototip ekranının alt navigasyonunda zaten **MARKT** ve **MAP** butonları yer alıyor — bu, meta-oyun vizyonunun UI iskeletinin en baştan düşünüldüğünü gösteriyor. Öneri: MARKT ekranı Soft Money/Gem harcayarak geliştirme satın almaya, MAP ekranı ise restoranın büyüme haritasını görselleştirmeye hizmet edebilir.

**Not:** Bu bölüm bilinçli olarak düşük detayda tutulmuştur; çekirdek döngü sağlamlaşmadan bu sisteme zaman ayırmak önerilmez (bkz. Yol Haritası, Faz 7).

---

## 13. UI/UX Gereksinimleri (Özet)

*(v0.2: prototip ekran görüntüsüyle doğrulanan/güncellenen tablo)*

| Ekran Öğesi | İşlev |
|---|---|
| Üst HUD | Health, Soft Money, Gem göstergeleri *(v1.0: altıgen level rozeti ve XP çubuğu sahneden kaldırıldı)* |
| 3 sabit bilet slotu | Müşteri + kalan süre + ana yemek + modifikasyon (ikon+X/+) + yan ürün/içecek + zaman barı; kenar rengi = sabır tipi |
| Tepsi alanı | Her slotun altında; oyuncu yerleştirdikçe ürünler burada birikir (bkz. Bölüm 5) |
| Board | Sabit bir grid (şu an 6 sütun × 5 satır — üretim sırasında görsel/mekaniksel olarak ayarlanabilir) — mutfaktan gelen tüm yemek öğelerinin dağıldığı ana alan |
| Powerup ikonları | 3 adet, alt kısımda (bkz. Bölüm 5.2) |
| Sonuç feedback'i | Doğru teslimat / yanlış teslimat için ayrı, net görsel-işitsel geri bildirim |
| Alt navigasyon | MARKT ve MAP butonları (meta-oyun erişimi, bkz. Bölüm 12) |
| Gün/Oturum sonu ekranı | Toplam bahşiş, tamamlanan sipariş sayısı, yıldız derecesi |

**Mobil özel notlar:**
- Dokunmatik hedefler (yemek öğeleri, tepsi alanı) parmak boyutuna uygun minimum dokunma alanına sahip olmalı.
- Board grid'i sabit hücreli olduğu için, yemek yoğunluğu arttıkça hücre boyutunun okunabilirliği korunmalı (küçük ekranlarda test edilmeli).
- Dikey ekran yönü prototipte net şekilde görülüyor — tek elle oynanabilirlik hedefiyle uyumlu.

**Not — Board boyutu:** 6×5 şu anki başlangıç noktasıdır; kesin sayı henüz kilitlenmedi, üretim sırasında görsel ve mekaniksel denemelerle ayarlanacaktır.

---

## 14. Açık Tasarım Soruları — Özet Liste

*(v0.7: Sonsuz Mod tasarımdan kaldırıldığı için ona bağlı sorular listeden silindi; powerup sistemi ertelendiği için ilgili madde "kapsam dışı" olarak işaretlendi; retry'da XP kaybı netleşti — bu son madde v1.0'da geçersiz kaldı, XP sistemi kaldırıldı.)*

1. ~~Modifikasyonlar tepsi sayacında ayrı öğe mi, yoksa ana yemeğin durumu mu?~~ **Çözüldü.** (bkz. Bölüm 3)
2. ~~Board'daki yemek sayısına üst sınır var mı?~~ **Öneri sunuldu: üst sınır = grid kapasitesi.** (bkz. Bölüm 4)
3. ~~Yanlış yerleştirme anlık mı tespit ediliyor, yoksa tepsi onayında mı?~~ **Çözüldü: tepsi onayında.** (bkz. Bölüm 5)
4. ~~Can bitince oyun tamamen mi bitiyor?~~ **Çözüldü: o gün biter, zorluk düşürülerek yeniden oynanır; Gem ile devam etme opsiyonu var.** (bkz. Bölüm 6)
5. ~~Süre bilet bazlı mı, süre dolunca ne oluyor?~~ **Çözüldü: bilet bazlı; can azalır + bilet iptal edilir.** (bkz. Bölüm 7)
6. ~~Sabır tipi görsel gösterimi + bahşiş azalma eğrisi?~~ **Çözüldü: sabit kenar rengi; eğri kademeli, eşikler dinamik.** (bkz. Bölüm 8)
7. ~~Hız kademesi eşikleri sabit mi, dinamik mi?~~ **Soru v1.1'de geçersiz kaldı:** hız kademesi sistemi tamamen kaldırıldı, yerine barın dilimlerine bağlı kademeli oran düşüşü geldi. Yeni eşikler duvar saati ve tek tip. (bkz. Bölüm 9)
8. ~~Level ilerlemesi kalıcı mı?~~ **Geçersiz (v1.0): level sistemi kaldırıldı.** Kalıcı ilerlemenin taşıyıcısı artık Day dizisi; hangi Day'de olunduğunun kaydedilmesi ayrı bir iş olarak duruyor. (bkz. Bölüm 10)
9. ~~Can bitip retry olduğunda o günün XP'sine ne oluyor?~~ **Geçersiz (v1.0): XP yok, kaybedilecek bir kazanım da yok.** (bkz. Bölüm 6, 10)
10. Tepsi doluluk sayacı (`x/y`) nihai UI'da nerede gösterilecek — özelliğin var olacağı kesin, konumu üretim sırasında belirlenecek.
11. ~~Tamamlanmış tepsi önizleme kutusundan sonra teslimat otomatik mi?~~ **Bu soru artık geçersiz: "önizleme kutusu" kavramı yanlıştı, o alan tepsinin kendisi. Otomatik kontrol/gönderim çözümü geçerliliğini koruyor.** (bkz. Bölüm 5, 5.1)
12. ~~Gem, Soft Money'den farklı ne amaçla kullanılacak?~~ **Çözüldü: powerup satın alma + "devam et" (continue) mekaniği.** (bkz. Bölüm 10)
13. Board grid boyutu (6×5) başlangıç noktası — kesin sayı üretim sırasında görsel/mekaniksel test sonrası netleşecek.
14. ~~Prototipteki 3 gri daire ikonunun işlevi nedir?~~ **Çözüldü: powerup'lar.** (bkz. Bölüm 5.2)
15. ~~Powerup 3'ün içeriği ne olacak?~~ **Çözüldü: Gürültü Temizleme onaylandı.** (bkz. Bölüm 5.2)
16. ~~Powerup kazanım/kullanım kuralları + miktar/maliyet sayıları?~~ **Kapsam dışı (ertelendi):** Powerup sistemi şu anki fazda geliştirilmeyecek; bu soru geliştirme tekrar kapsama alındığında ele alınacak. (bkz. Bölüm 5.2)
17. Can biterse uygulanacak zorluk düşürme somut olarak neye karşılık geliyor (hangi parametre ne kadar düşüyor)?
18. ~~Bir teslimatın parası tamamen hıza/sabıra mı bağlı?~~ **Çözüldü (v0.8): hayır — sipariş bedeli sabit ve garanti, yalnızca bahşiş değişken ve bahşiş fiyatla orantılı.** (bkz. Bölüm 9, 10)
19. Modifikasyonlar sipariş bedelini değiştiriyor mu (örn. "ekstra köfte" siparişi pahalılaştırıyor mu)? — **Açık.** Şu anki karar: fiyat yalnızca yemeğin kendisinden gelir, modifikasyonlar bedeli değiştirmez; ekstra malzemenin ücretlendirilmesi ayrı bir tasarım kararıdır.
20. Bahşiş Oranı'nın somut değeri ne olacak (v0.8 formülündeki tek global katsayı)? — üretim sırasında dengelenecek.
21. ~~Sabır tipi bahşişi nasıl etkiliyor?~~ **Çözüldü (v0.9): etkilemiyor.** Bahşiş = Sipariş Bedeli × Bahşiş Oranı × Hız Çarpanı. Sabır tipi yalnızca süreyi belirler. (bkz. Bölüm 8, 9)
23. Üç kademenin oranları (`TipRateFull`/`TipRateWarning`/`TipRateCritical`, öneri 0.20/0.15/0.10) ve iki eşik (0.666/0.333) somut olarak ne olacak? — üretim sırasında dengelenecek; beşi de `EconomyConfig`'te serialize alan, Inspector'dan ayarlanır.
22. Sabır tipinin para tarafında hiç ağırlığı olmaması isteniyor mu, yoksa **sabit bir bahşiş çarpanı** mı verilmeli (kademeli azalma değil, tipe bağlı düz katsayı)? — **Açık, ve v1.0 ile daha keskin:** XP çarpanı kaldırıldığı için sabır tipinin süre dışında hiçbir etkisi kalmadı.

---

## 15. Teknik Notlar (Kısa)

- Bilet ve board state'i merkezi bir "oyun durumu" (game state) nesnesinde tutulmalı; UI bu state'e reaktif şekilde bağlanmalı.
- Yemek dağıtım algoritması (zorunlu havuz + gürültü havuzu) ayrı, test edilebilir bir modül olarak yazılmalı — zorluk ayarı büyük ölçüde bu modülün parametrelerinden geçecek.
- Bahşiş/hız/sabır hesaplamaları tek bir "ekonomi" fonksiyonunda toplanmalı, farklı sayı dengelemeleri (balancing) kolayca test edilebilsin.

---

*Bu doküman, geliştirme sürecinde alınacak kararlarla birlikte güncellenmesi beklenen canlı bir belgedir. "Açık Tasarım Soruları" bölümü, karar verildikçe ilgili maddeye taşınmalı ve buradan silinmelidir.*
