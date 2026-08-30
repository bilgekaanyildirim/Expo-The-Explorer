# Day Editor El Kitabı

**Expo the Explorer · İçerik Üretim Aracı**

Oyundaki her "gün"ün — siparişler, tahta, süreler, zorluk — yazıldığı ekran. Bu döküman her alanın ne yaptığını, hangi sayının oyunda neye dokunduğunu ve bir günü baştan sona nasıl kuracağını anlatır.

---

## 00 · Nedir, nerede açılır

Bir "gün" oyunun bölümüdür: hangi siparişlerin geleceği, tahtanın nasıl açılacağı, sürelerin ne olacağı.

Unity'de üst menüden: `ExpoTheExplorer ▸ Day Editor`. Yanındaki `ExpoTheExplorer ▸ Play From Day` ise test aracıdır (bölüm 11).

Her gün diskte tek bir dosyadır:

```
ExpoTheExplorer/Assets/Resources/Days/day_00.json … day_21.json
```

Dosya adındaki numara **Day Index**'tir ve oyunun oynatma sırası budur. Editörde kaydettiğin an dosya yazılır; başka hiçbir yerde günle ilgili ayar yoktur.

> **TEMEL KURAL** — JSON dosyalarını elle açıp düzenlemeyin. Editör, dışarıda yazılmış geçersiz bir değeri kaydederken düzeltemez; oyun o günü yüklemeyi reddeder ve gün listeden düşer. Her değişiklik Day Editor üzerinden yapılmalı.

---

## 01 · Ekranın anatomisi

Pencere üç parçadır: soldaki gün listesi, üstteki şerit, sağdaki gün içeriği.

**Üst şerit** — **+ New Day** yeni bir gün açar. Yanındaki beş alan (Food Catalog, Game Config, Ticket Generation Config, Ticket Card Visuals, Board Visuals) projeden otomatik bulunur; normalde dokunmazsınız. Yalnızca editör "yanlış olanı bulmuşsa" elle sürüklenir.

**Sol: gün listesi** — Bütün günler sırayla. Adının yanındaki `*` = kaydedilmemiş değişiklik var. Satırın sağındaki `×` günü siler. Satırı sürükleyip bırakmak günlerin sırasını değiştirir.

**Sağ: seçili günün içeriği** — En üstte kayıt durumu barı, altında sırayla Food Selection, Day, Generation Settings, Ticket Sequence, Start Board, Ticket Runtime, Board Distribution ve en altta Save / Duplicate / Delete.

### Kayıt durumu barı

Sayfanın en tepesinde durur, çünkü Save butonu birkaç ekran aşağıdadır. Üç hâli vardır:

- **Saved** — bu gün dosyasıyla birebir aynı.
- **UNSAVED CHANGES** — yaptıkların henüz diske yazılmadı.
- **Never saved** — bu günün henüz dosyası yok (yeni veya kopyalanmış).

Yanındaki **Revert Changes**, son kaydetmeden bu yana yapılan her şeyi geri alır ve sorar. Pencere başlığında da `Day Editor*` görürsen kaydedilmemiş bir gün var demektir.

> **KAYBOLMAYA KARŞI** — Kaydetmeden başka bir güne geçmeye çalışırsan editör sorar: **Save** / **Keep Editing** / **Discard**. Pencereyi kapatırken de sorar. Gün doğrulama hatalıysa "Save" çalışmaz ve editör hangi hatanın kaldığını söyler.

---

## 02 · Bir gün nasıl yapılır

Alanlar ekranda bu sırayla dizilmiştir; çalışma sırası da budur.

1. **Günü aç** — `+ New Day` ile sıfırdan, ya da benzer bir günü seçip **Duplicate** ile kopyalayarak. Kopyalamak neredeyse her zaman daha hızlıdır.
2. **Yemekleri seç — Food Selection** — Bu gün hangi yemekleri servis ediyor? Buradaki seçim her şeyin havuzudur: siparişler de tahta da yalnız bunlardan kurulur.
3. **Uzunluğu yaz — Tickets Required For Day** — Gün kaç siparişte biter. Bilet sayısı bununla birebir eşleşmek zorunda.
4. **Üretim ayarlarını kur — Generation Settings** — Yan ürün/içecek oranı, modifikasyon yoğunluğu, sabır dağılımı, hangi ana yemeğin ne sıklıkta çıkacağı.
5. **Siparişleri üret — Generate** — Yukarıdaki ayarlara göre tüm bilet sırasını yazar. Sonra kart kart elle düzeltilir.
6. **Açılış tahtasını kur — Start Board** — İsteğe bağlı. Gün belirli malzemelerle açılsın istiyorsan hücrelere tıklayarak yerleştir.
7. **Süreleri ayarla — Ticket Runtime** — Üç sabır tipinin saniyeleri ve kaç bilet önden hazırlanacağı.
8. **Zorluğu dengele — Board Distribution** — Tahtaya ne kadar gürültü sızacak, kaç bilet garanti tamamlanabilir olacak. Bu, içerik bittikten *sonra* yapılan pas.
9. **Kaydet ve oyna** — **Save**, sonra `Play From Day` ile doğrudan o günden gir.

---

## 03 · Day — kimlik ve uzunluk

**`Day Index`** *(tam sayı)*
Günün numarası ve dosya adı. Kaydettiğinde dosya bu numaraya göre adlandırılır — numarayı değiştirip kaydetmek dosyayı yeniden adlandırır, eskisini siler.
*Bu kutunun altında günün doğrulama mesajları görünür: kırmızı = hata (kaydettirmez), sarı = uyarı (kaydettirir).*

**`Tickets Required For Day`** *(tam sayı)*
Gün kaç sipariş tamamlanınca biter. **Ticket Sequence'teki kart sayısı buna eşit olmak zorunda**; değilse Save kapalı kalır ve hata mesajı iki sayıyı da yazar.
*Generate bu sayı kadar bilet üretir, yani önce bunu yazmak işi kolaylaştırır.*

---

## 04 · Food Selection

Bu günün menüsü. Ekranda görsel bir ızgara olarak durur.

Katalogdaki her yemek bir kutucuktur; tıklayınca güne girer, tekrar tıklayınca çıkar. Seçili olanlar mavi çerçeveyle işaretlenir. Üstte **Select All** / **Clear All**, her kategori satırında da **All** / **None** vardır.

Bu seçim üç yerin tek kaynağıdır:

- **Generate** yalnız buradan sipariş kurar.
- Bilet editöründeki Main / Side / Drink seçenekleri yalnız burayı gösterir.
- Start Board'a yalnız buradan yemek konabilir.

Bir **Main** seçtiğinde Generation Settings altındaki **Main Dish Weights** listesine o yemek için otomatik bir satır eklenir; seçimi kaldırınca satır kaybolur. O listeyi elle ekleyip çıkaramazsın — üyeliği bu ızgara belirler.

> **SIK KARŞILAŞILAN** — Bir günde **en az bir Main** seçili olmalı. Yoksa Generate çalışmaz ve editör bunu açıkça söyler. Ayrıca seçimden çıkardığın bir yemek biletlerde kalmışsa doğrulama hata verir: "bu yemek bu günün seçiminde değil".

---

## 05 · Generation Settings

Başlığında "authoring only" yazar, çünkü burası oyunu değil **Generate tuşunu** etkiler.

Bu bölümdeki hiçbir sayı oyun içinde okunmaz. Bunlar "bu günün bilet sırası hangi ayarlarla üretildi"nin kaydıdır ve bir sonraki Generate'in girdisidir. Yani mevcut bir günün zorluğunu buradan değiştiremezsin — değiştirip tekrar Generate'e basman gerekir.

### Sipariş içeriği

**`Side Inclusion Chance`** *(0 – 1)*
Üretilen bir siparişin yan ürün içerme olasılığı. `0.9` = biletlerin yaklaşık %90'ında yan ürün olur.

**`Drink Inclusion Chance`** *(0 – 1)*
Aynısı içecek için.

**`Modification Count Lambda`** *(0 – 10, **pasif**)*
Eski bir yedek değer. Her Main'in kendi satırında kendi lambda'sı olduğu için Generate buraya artık hiç bakmaz. Dosya uyumluluğu için duruyor — **görmezden gelin.**

**`Modification Addition Chance`** *(0 – 1)*
Bir modifikasyonun "ekle" mi "çıkar" mı olacağı. **Sadece iki yönlü modifikasyonlar için** geçerlidir; yalnızca-ekleme veya yalnızca-çıkarma tanımlı modifikasyonlar yönünü kendileri taşır.

### Patience Mix

Generate'in kaç sabırlı, kaç normal, kaç sabırsız müşteri koyacağı — **ve hangi sırayla**. Sırayla yazar: önce Patient'lar, sonra Normal'lar, sonra Impatient'lar. Yani günün zorluk eğrisini buradan çizersin.

**`Patient` / `Normal` / `Impatient`** *(≥ 0)*
Üçü de `0` ise bu **"yazılmamış"** demektir, "boş gün" değil: Generate her biletin sabrını rastgele atar.
*Alanların altındaki satır ne üreteceğini önceden yazar ("Generate will lay down 7 Patient, then 0 Normal…"). Toplam, Tickets Required For Day ile tutmuyorsa orada uyarır.*

### Main Dish Weights

Food Selection'da seçtiğin her ana yemek için bir satır. Satırların *varlığı* otomatik, içindeki sayılar senin.

**`Weight`** *(bağıl ağırlık)*
Bu yemeğin çıkma sıklığı, diğerlerine oranla. İki yemek 1.0 ve 3.0 ise ikincisi üç kat sık gelir. Mutlak bir yüzde değil, oran.

**`Modification Count Lambda`** *(0 – 10)*
Bu yemeğin siparişlerinde ortalama kaç modifikasyon olacağı (Poisson oranı). `0` = bu yemek hiç modifikasyon almaz. Yükseldikçe "ekstra mayonez, ketçapsız, ekstra hardal" yığılır.

**`Max Modification Count`** *(1 – 10)*
Bu yemeğin bir siparişte alabileceği modifikasyon tavanı. Gerçek tavan, bu sayı ile yemeğin kendi sunduğu modifikasyon sayısının **küçüğüdür** — 3 modifikasyonu olan bir yemekte bunu 10 yapmak hiçbir şey değiştirmez.
*Lambda ile birlikte okunur: lambda "ortalama kaç tane", bu "en fazla kaç tane".*

Bu bölümün altında iki salt-okunur önizleme durur: yan ürün/içecek çıkma yüzdeleri ve modifikasyon sayısı dağılımı. Sayıları kaydetmeden görmek için oradadır.

---

## 06 · Ticket Sequence

Günün asıl içeriği: hangi sipariş, hangi sırayla gelecek.

Siparişler yatay bir **kart şeridi** olarak görünür — oyundaki bilet kartının aynısı. Şeritteki hareketler:

- **Sol tık + sürükle** — kartı başka bir sıraya taşır. Mavi çizgi nereye düşeceğini gösterir.
- **Sağ tık** — kartı düzenlemek için açar. Tekrar sağ tık kapatır.

Şeridin altındaki **Add Ticket** boş bir bilet ekler. Üstteki **Generate** ise **tüm sırayı siler ve baştan üretir** — sorar, ama elle yaptığın düzeltmeler gider.

### Seçili biletin editörü

**`Generate Random Ticket`** *(buton)*
Yalnız bu bileti yeniden üretir (Main / Side / Drink / modifikasyonlar / sabır tipi). Bir kart tutmadıysa tek tuşla yeniden atmanın yolu.

**`Main Item`** *(zorunlu)*
Ana yemek. Resme tıklayarak seçilir; seçili olana tekrar tıklamak boşaltır. Yalnız Food Selection'dakiler listelenir.

**`Modifications`**
Ana yemeğin hemen altında durur, çünkü **modifikasyonlar ana yemekten gelir** — yan ürün ve içecek modifikasyon taşımaz. Sunulan modifikasyonlar kutucuk olarak listelenir; tıkla ekle, iki yönlü olanlara sağ tıklayarak +/− yönünü çevir.
*Yemek değiştiğinde eski modifikasyonlar kartta kalabilir; editör bunları "bu yemeğin sunmadığı" diye işaretler ama kaydetmeni engellemez. Temizlemek senin işin.*

**`Side Item` / `Drink Item`** *(isteğe bağlı)*
Yan ürün ve içecek. Boş bırakılabilir.

**`Patience Type`** *(Patient / Normal / Impatient)*
Müşterinin sabrı. Kartın kenar rengini belirler (yeşil / krem / kırmızı) ve **süresini** Ticket Runtime'dan çeker. Ticket ömrü boyunca değişmez.

**`Name Override`** *(metin)*
Müşterinin adını sabitler. Boş bırakılırsa isim havuzundan rastgele çekilir — normal davranış budur. **Yüz için karşılığı yoktur**, portre her zaman rastgeledir.

**`Time Override`** *(saniye)*
Bu bilete özel süre. `0` = kapalı, sabır tipinin süresi geçerli. **0'dan büyük her değer** hem sabır tipini hem günün ayarını ezer.
*Tek bir bileti "kasten zor" yapmak için; günün geneli için Ticket Runtime kullanılır.*

**`Delete This Ticket`** *(buton)*
Bileti siler. Tickets Required For Day'i de düşürmeyi unutma, yoksa Save kapanır.

---

## 07 · Start Board — açılış tahtası

Gün açıldığında tahtada hazır duran malzemeler. İsteğe bağlıdır.

Solda tahtanın ızgarası, sağda seçili hücrenin editörü durur. Kullanımı:

1. Izgarada bir **hücreye tıkla** — sağda "Cell (x, y)" açılır.
2. Sağdaki yemek kutucuklarından birine **tıkla** — o hücreye yerleşir.
3. Aynı yemeğe **tekrar tıkla** ya da **Clear Cell** — hücre boşalır.

Yerleştirilen malzemeye, tıpkı bilette olduğu gibi **modifikasyon** eklenebilir — "ekstra hardallı sosisli" tahtada hazır durabilir. Yalnız Food Selection'daki yemekler konabilir.

> **KRİTİK KURAL** — Bir gün açılış tahtası yazıyorsa oyun **tam olarak o malzemelerle** açılır; üstüne hiçbir şey spawn edilmez. Bu yüzden ekrandaki **ilk üç biletten en az biri** o tahtadan tamamlanabilir olmalı.
>
> Değilse Save kapanır ve hata şunu der: *"none of the first N ticket(s) can be completed from it"*. Sebebi basit — hiçbir bilet tamamlanamıyorsa oyuncu, bir bilet zaman aşımına uğrayana kadar boş tahtaya bakar.

Açılış tahtası hiç yazılmamışsa (tek bir hücre bile dolu değilse) bu kural devreye girmez ve gün normal dağıtımla açılır.

---

## 08 · Ticket Runtime — süreler

Buradaki değerler oyunda doğrudan okunur. Her gün kendi sürelerini taşır.

**`Impatient Time Limit Seconds`** *(saniye)* — Sabırsız (kırmızı) müşterinin süresi.

**`Normal Time Limit Seconds`** *(saniye)* — Normal (krem) müşterinin süresi.

**`Patient Time Limit Seconds`** *(saniye)* — Sabırlı (yeşil) müşterinin süresi.
*Tasarım kuralı Impatient < Normal < Patient'tır. Bozarsan editör **uyarır ama kaydettirir** — kasten yapılıyor olabilir. `0` ise **hata** verir: sıfır saniyelik bilet geldiği anda yanar ve oyun günü yüklemeyi reddeder.*

**`Upcoming Queue Size`** *(≥ 1)*
Ekrandaki 3 aktif slotun önünde kaç biletin hazır bekletileceği. Bu kuyruk aynı zamanda **gürültü kaynağıdır**: tahtaya sızan "yanlış" malzemeler buradaki bekleyen biletlerden alınır.
*Board Distribution'daki `Leak Depth` bu sayıyı aşarsa fazlası boşa gider; editör bunu uyarı olarak söyler.*

> **NEDEN BURADA** — Süreler yalnızca zorluğu değil **parayı** da belirler. Bahşiş, biletin kendi süresinin ne kadarı kaldığına göre üç kademeye ayrılır (yeşil / turuncu / kırmızı bar). Yani süreyi uzatmak, oyuncuya daha çok "tam bahşiş" penceresi vermek demektir.

---

## 09 · Board Distribution — zorluk

Tahtaya ne düşeceğini belirleyen sekiz sayı. Günün asıl zorluk kolu burasıdır.

### Önce mekanizma

Her yeni sipariş bir slota girdiğinde tahta üretimi tetiklenir — sürekli çalışan bir zamanlayıcı yoktur. Sırayla iki şey olur:

1. **Gerekli havuz.** Sistem o tur için birkaç bileti "garantili" seçer ve o biletlerin eksik malzemelerini tahtaya koyar. Oyuncunun her an tamamlayabileceği en az bir bilet olmasını sağlayan şey budur.
2. **Gürültü havuzu.** Ardından, kuyrukta *bekleyen* biletlerden malzeme "sızdırılır". Bunlar şu an hiçbir işe yaramaz, tahtayı doldurur ve zorluğun asıl kaynağıdır. Oyuncu kullanmadıkça kalırlar; kendiliğinden yok olmazlar.

### Gürültü — tahta ne kadar dolsun

**`Noise Leak Count Lambda`** *(0 – 10)*
Her turda ortalama kaç gürültü malzemesi sızacağı. `0` = hiç gürültü yok, gün tertemiz. Yükseldikçe tahta hızla dolar.

**`Max Leak Count`** *(1 – 10)*
Tek turda sızabilecek en fazla malzeme. Lambda'nın tavanı — "ortalama 2 ama asla 4'ten fazla değil" demenin yolu.

**`Leak Depth`** *(1 – 10)*
Gürültünün kuyrukta **ne kadar ileriden** alınabileceği. `1` = yalnız sıradaki bilet, `10` = on bilet sonrasına kadar. Derinlik arttıkça tahtadaki malzemeler oyuncunun gördüğü siparişlerle daha az ilgili görünür — kafa karışıklığı artar.
*Kaç malzeme sızacağını **değil**, hangilerinin aday olduğunu belirler. Upcoming Queue Size'ı aşan kısmı boşa gider.*

Bu üçünün altında **Noise Leak Preview** kutusu, "0 sızıntı %x, 1 sızıntı %y…" diye gerçek olasılıkları yazar. Sayıyı çevirirken oraya bakın.

### Garanti — kaç bilet tamamlanabilir olsun

**`Guaranteed Ticket Count Mode`** *(Manual / Poisson)*
**Manual** = her tur sabit sayıda bilet garanti. **Poisson** = sayı her tur rastgele belirlenir (ama asla 0 olmaz). Manual öngörülebilir, Poisson dalgalı.

**`Guaranteed Ticket Count`** *(1 – 3, yalnız Manual)*
Manual modda her turun bütçesi. `1` = zor (hep tek bilet tamamlanabilir), `3` = rahat (üçü de).

**`Guaranteed Ticket Count Lambda`** *(0 – 10, yalnız Poisson)*
Poisson modda ortalama bütçe. **"Her zaman üç" için bunu yükseltmeyin** — 10'da bile ancak %82 üç çıkar. Kesinlik istiyorsanız Manual + 3 doğru araç.

**`Early Ticket Weight Decay`** *(0 – 1)*
Garanti hakkı dağıtılırken **erken gelmiş biletlerin ne kadar kayırılacağı**. Düşük değer (0.2) = neredeyse hep en eski bilet seçilir, sıra disiplinli. Yüksek değer (0.9) = seçim daha adil/rastgele, oyuncu sıradan bağımsız çalışmak zorunda kalır.

**`Urgent Time Threshold Seconds`** *(0 – 30)*
Bir biletin süresi bunun altına düştüğünde, kura ne derse desin **koşulsuz garanti** olur ve eksik malzemeleri tahtaya konur. Aciliyet her zaman kazanır ve bütçeyi aşabilir.
*Oyuncuyu "malzemesi olmayan bir bilet gözümün önünde yandı" hissinden koruyan emniyet valfi. Düşürmek günü sertleştirir.*

Yanda **Guaranteed Ticket Preview**, seçtiğin moda göre tur başına bütçeyi yazar. Altındaki not önemli: bu *bütçedir*; aciliyet eşiğini geçen biletler bunun üstüne eklenir.

> **ZORLUK NASIL KURULUR**
>
> **Kolay gün:** Guaranteed 2–3, Noise Lambda düşük (0–1), Leak Depth sığ (1–3), Urgent eşiği yüksek (12–15 sn).
>
> **Zor gün:** Guaranteed 1, Noise Lambda yüksek (2+), Leak Depth derin (6–10), Urgent eşiği düşük (5–8 sn), Early Decay yüksek.

---

## 10 · Kaydetme ve doğrulama

Sayfanın en altındaki dört buton:

- **Save** — günü dosyaya yazar. **Doğrulama hatası varsa tuş kapalıdır.**
- **Revert Changes** — son kaydetmeye geri döner. Sorar.
- **Duplicate** — günün kopyasını çıkarır. Yeni gün yapmanın en hızlı yolu.
- **Delete** — günü ve dosyasını siler.

### Hatalar — kaydettirmez

| Mesaj | Anlamı ve çözümü |
|---|---|
| `ticketSequence has N entries but ticketsRequiredForDay is M` | Bilet sayısı ile gün uzunluğu tutmuyor. Bilet ekle/çıkar ya da sayıyı düzelt. |
| `Ticket i: 'x' is not in this Day's food selection` | Bir bilette, Food Selection'dan çıkarılmış bir yemek duruyor. Ya yemeği geri seç ya bileti düzelt. |
| `Day Start board: none of the first N ticket(s) can be completed from it` | Açılış tahtası ilk biletlerin hiçbirine yetmiyor. Tahtaya eksik malzemeyi koy ya da ilk bileti değiştir. |
| `every patience time limit must be greater than 0` | Ticket Runtime'da bir süre 0 kalmış. |
| `Not configured yet` | Üst şeritteki config alanları boş. Normalde otomatik dolar; dolmadıysa elle sürükleyin. |

### Uyarılar — kaydettirir

| Mesaj | Anlamı |
|---|---|
| `GDD Section 8 expects Impatient < Normal < Patient` | Süre sıralaması ters. Kasten yapılmış olabilir, o yüzden engellenmiyor. |
| `Leak Depth reaches past Upcoming Queue Size` | Derinlik kuyruktan uzun; fazlası hiçbir işe yaramıyor. İkisinden birini hizala. |

---

## 11 · Test: Play From Day

`ExpoTheExplorer ▸ Play From Day` — yazdığın günü baştan başlamadan oynamak için.

Pencere bütün günleri listeler. Başındaki `>` işareti oyuncunun şu an bulunduğu günü gösterir. Her satırda "`7 required · 7 authored`" yazar — beklenen bilet sayısı ve yazılmış bilet sayısı; tutmuyorsa gün zaten kaydedilmemiş demektir.

**`Play`** — Doğrudan gün sahnesine girer. **Anahtar harcamaz, ana ekranı ve meta akışını atlar.** Bir günü dengelerken kullanılacak hızlı döngü budur.

**`Main Screen`** — Gerçek giriş yolu; ana ekrandan, anahtarıyla. Test edilen şey *güne giriş akışının kendisiyse* bu kullanılır.

Bir günü bitirmek oyuncunun kayıtlı gün numarasını ilerletir, yani pencere kendini tazeler.

---

## 12 · Tuzaklar

> **GÜN SIRASINI DEĞİŞTİRMEK** — Listede bir günü sürükleyip bırakmak **bütün günleri 0'dan başlayarak yeniden numaralar** ve dosyaları yeniden yazar. **Geri alınamaz.**
>
> Daha önemlisi: bir günü *numarasıyla* anan her şey güncellenmez — Meta Catalog'daki mekân/dekor açılış günleri, powerup'ların tanıtım günü. Taşımadan sonra hepsi başka bir güne işaret eder ve elle düzeltilmeleri gerekir.
>
> Editör, kaydedilmemiş ya da numarası değiştirilmiş bir gün varsa taşımayı reddeder — önce onu kaydet ya da geri al.

> **TUTORIAL BLOĞU** — Bazı günlerde (şu an `day_00`) oyuncuya ilk hamlesini zorla yaptıran bir tutorial bloğu var. **Bunun editör arayüzü yok**, elle yazılmıştır. Günü editörde açıp kaydetmek bloğu bozmaz — olduğu gibi taşınır — ama içeriğini editörden değiştiremezsin.

> **GENERATE HER ŞEYİ EZER** — **Generate** bilet sırasının tamamını siler. Kartları elle düzelttikten sonra ayarları değiştirip tekrar Generate'e basarsan o düzeltmeler gider. Önce üret, sonra elle düzelt — tersi değil. Tek bir kartı yenilemek için kartın kendi **Generate Random Ticket** tuşu var.

> **GENERATION SETTINGS OYUNU ETKİLEMEZ** — Yayınlanmış bir günün zorluğunu Generation Settings'ten değiştiremezsin; orası yalnız Generate'in girdisidir. Oyun içi zorluk **Board Distribution** ve **Ticket Runtime**'dadır; onlar anında etkilidir.

> **YENİ ALAN EKLENDİĞİNDE** — Eski bir gün dosyasında olmayan bir alan editörde tasarlanmış varsayılan değeriyle açılır ve ilk kaydetmede dosyaya yazılır. Yani eski bir günü açıp kaydetmek onu güncel şemaya taşır — zararsızdır, hatta istenen şeydir.

---

*Expo the Explorer · Day Editor · Kaynak: `Assets/Editor/DayEditor*.cs`, `Assets/Scripts/Systems/DaySystem`*
