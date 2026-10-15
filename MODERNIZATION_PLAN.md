# Modernisierungsplan

## 1. Abhängigkeiten und Plattform

- [x] SteamKit2 auf 3.4.0 aktualisieren und die Upstream-Anpassungen übernehmen.
- [x] Verwundbare Paketversionen ersetzen und Paket-Lockdateien einführen.
- [x] Auf .NET 10 LTS migrieren und das SDK zentral festlegen.

## 2. Zuverlässigkeit und API-Verträge

- [x] Veraltete Netzwerk-, Kryptografie- und Serialisierungs-APIs ersetzen.
- [x] Null-Sicherheit für Wertobjekte, `SteamClient`, CDN-Pool und Dateiziele einführen.
- [ ] Die SteamKit-Adapter (`SteamContentClient`, Downloadhandler) methodenweise nullable-sicher machen.
- [ ] Nicht implementierte Legacy-Manifeste bewerten und entweder implementieren oder explizit aus der öffentlichen API entfernen.

## 3. Qualitätssicherung

- [x] Reale Unit- und Integrations-Tests einführen.
- [ ] Testabdeckung für SteamContentClient-Fehlerpfade, Downloads und Abbruchfälle erweitern.
- [ ] CI um Paketvalidierung und Code-Coverage-Ausgabe ergänzen.

## 4. Paket und Dokumentation

- [x] NuGet-README in das Paket aufnehmen.
- [ ] README auf .NET 10, die unterstützten Downloadwege und die aktuellen Grenzen aktualisieren.
- [ ] Paketversion und Release-Notes für die Breaking Changes der Modernisierung festlegen.

## 5. Abschluss

- [ ] Wiederherstellung im Locked Mode, Release-Build, Tests, Vulnerability-Scan und NuGet-Paket validieren.
