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

Az alkalmazás a szöveges PDF-ekből kiolvassa és megmutatja a tartalmat; a fájlnév
ajánlásához az első felismerhető dátumot használja. A képként beszkennelt PDF-ekhez
egyelőre OCR szükséges. A `IPdfInvoiceReader` interfész bővítési pont OCR- vagy
helyi LLM-alapú elemzés bekötéséhez.

Az „Előzőhöz fűzés” az aktuális PDF oldalait az előző iktatott PDF végére illeszti.
Ha az eredeti fájl megőrzése ki van kapcsolva, sikeres összeillesztés után az eredeti
bemeneti fájl törlődik, mert annak tartalma már része az összevont PDF-nek.
