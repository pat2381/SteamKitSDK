# Modernisierungsplan

## 1. Abhängigkeiten und Plattform

- [x] SteamKit2 auf 3.4.0 aktualisieren und die Upstream-Anpassungen übernehmen.
- [x] Verwundbare Paketversionen ersetzen und Paket-Lockdateien einführen.
- [x] Auf .NET 10 LTS migrieren und das SDK zentral festlegen.

## 2. Zuverlässigkeit und API-Verträge

- [x] Veraltete Netzwerk-, Kryptografie- und Serialisierungs-APIs ersetzen.
- [x] Null-Sicherheit für Wertobjekte, `SteamClient`, CDN-Pool und Dateiziele einführen.
- [x] Die priorisierten SteamKit-Adapter (`SteamContentClient`, Downloadhandler) methodenweise nullable-sicher machen.
- [x] Nicht implementierte Legacy-Manifeste bewerten und eindeutig als nicht unterstützt dokumentieren, wenn eine sichere aktuelle Implementierung nicht möglich ist.

## 3. Qualitätssicherung

- [x] Reale Unit- und Integrations-Tests einführen.
- [x] Testabdeckung für SteamContentClient-Fehlerpfade, Downloads und Abbruchfälle erweitern.
- [x] CI um Paketvalidierung und Code-Coverage-Ausgabe ergänzen.

## 4. Paket und Dokumentation

- [x] NuGet-README in das Paket aufnehmen.
- [x] README auf .NET 10, die unterstützten Downloadwege und die aktuellen Grenzen aktualisieren.
- [x] Paketversion 0.9.0 und Release-Notes für die Breaking Changes der Modernisierung festlegen.

## 5. Abschluss

- [x] Wiederherstellung im Locked Mode, Release-Build, Tests, Vulnerability-Scan und NuGet-Paket validieren.

## Validierungsstand

Am 15. Oktober 2026 erfolgreich geprüft: Locked Restore, Release-Build ohne Warnungen,
15 Tests inklusive Coverage-Erzeugung, NuGet-Paketbau und Vulnerability-Scan ohne bekannte
Schwachstellen.
