# Számlarendező

Windowsos WPF alkalmazás PDF számlák előnézetéhez, iktatásához és kereséséhez.

## Indítás

A fejlesztéshez .NET 6 SDK, az alkalmazás futtatásához pedig a Microsoft Edge
WebView2 Runtime szükséges (Windows 10 1809 vagy újabb).

```powershell
dotnet run
```

Önálló, .NET-et is tartalmazó 64 bites kiadás készítése:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true
```

Az elkészült futtatható fájl a `bin\Release\net6.0-windows10.0.17763.0\win-x64\publish`
mappában található. A WebView2 Runtime-ot az önálló kiadás sem tartalmazza.

Az iktatás során válassza ki a bemeneti mappát. A sikeresen iktatott PDF-ek a
Beállításokban megadott célmappán belül az `év\hónap` almappába kerülnek. Alapértelmezés
szerint a célmappa a `Dokumentumok\Számlák`. Az „Eredeti fájl megőrzése” kapcsolóval
beállítható, hogy az iktatás másolja vagy áthelyezze a bemeneti PDF-et. A keresés
alapértelmezés szerint a célmappában és annak almappáiban, fájlnév alapján keres.
A beállításokat az alkalmazás felhasználói profilja tárolja.
Az iktatóablak nem modális, így a mögötte lévő PDF-előnézet görgethető marad.

## Gyors szkennelés

A „Gyors szkennelés” a Windows WIA-kompatibilis szkenner-választóját nyitja meg.
Válasszon eszközt és olvassa be a dokumentumot; a beolvasott oldalból az alkalmazás
PDF-et készít a Beállításokban megadott scan mappában, majd automatikusan iktatja a
célmappa aktuális év/hónap almappájába. A scan mappa alapértelmezése
`Dokumentumok\szamla_scan`. Ha az eredeti fájlok megőrzése be van kapcsolva, a scan
mappában marad a beolvasott PDF is; egyébként az iktatás áthelyezi onnan.
A használni kívánt szkennert a Beállítások „Szkenner” listájában lehet kiválasztani,
és a „Frissítés” gombbal újra lehet keresni a csatlakoztatott eszközöket.

Ehhez WIA-kompatibilis scanner és a gyártó Windows-illesztőprogramja szükséges.
A gyors szkennelés jelenleg egy oldalt olvas be; a többoldalas adagoló kezelése és
az oldalak egy PDF-be fűzése még nincs megvalósítva.

## Helyi AI-számlaelemzés

A Beállításokban kapcsolható be az AI elemzés. Ehhez telepített és futó Ollama
szükséges; letöltési és telepítési útmutató a Beállítások AI csoportjában található.
Támogatott modellek: `granite3.2-vision`,
`qwen2.5vl:3b` és `qwen2.5vl:7b`. A még nem telepített modell kiválasztásakor
az alkalmazás felajánlja annak letöltését.

Az elemzés a PDF oldalait képpé alakítja, majd az Ollama helyi API-ján keresztül
keresi meg a számla dátumát, vevőjét, eladóját, teljes fizetendő összegét és
számlaszámát. Többoldalas PDF esetén minden oldalt külön képpé alakít és külön
elemez, majd egyesíti a felismert mezőket. A PDF-ből kinyert szöveget is felhasználja.
Az AI elemzési utasítás és a képfelbontás (DPI) a Beállításokban állítható.
Ha az AI felismeri a számla dátumát, az iktatási év és hónap automatikusan erre
áll be; hiányzó dátumnál az előző dokumentum választása marad, az első dokumentumnál
pedig az aktuális dátum az alapérték. Az adatok az iktatás előtt szerkeszthetők; a hiányzó mezőket sárga
figyelmeztetés jelzi. A fájlnév a Beállításokban összeállított mezősorrendből készül,
az elemeket aláhúzás választja el. Az elemzés az Ollama helyi szolgáltatásához
csatlakozik (`127.0.0.1`), a számlaképek nem kerülnek külső szolgáltatáshoz.
Sikertelen elemzésnél az ikonokkal újrapróbálható az elemzés vagy új utasítás adható
meg. Az új utasításos újrapróbálásnál a DPI is külön módosítható; az egyszeri
utasítás- és DPI-választás a mentett beállításokat nem változtatja meg.
Az aktuális számla elemzése közben a várólistán lévő további számlák elemzése is
elindul, eredményeik csak memóriában maradnak az alkalmazás futása alatt.

Az „Előzőhöz fűzés” az aktuális PDF oldalait az előző iktatott PDF végére illeszti.
Ha az eredeti fájl megőrzése ki van kapcsolva, sikeres összeillesztés után az eredeti
bemeneti fájl törlődik, mert annak tartalma már része az összevont PDF-nek.
