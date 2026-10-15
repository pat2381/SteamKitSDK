# Sicherheitsprüfung

Stand: 15. Oktober 2026, Version 0.9.0.

## Durchgeführte Prüfungen

- Locked Restore, Release-Build mit Warnungen als Fehler sowie aktivierten .NET-Analyzern und kompletter Testlauf.
- NuGet-Schwachstellenscan einschließlich transitiver Abhängigkeiten.
- Suche nach typischen Geheimnis-Markern und manueller Review der Authentifizierungs-, Download- und Dateipfade.
- Paketbau als NuGet-Paket.

## Ergebnis

Zum Prüfzeitpunkt wurden keine bekannten anfälligen NuGet-Pakete gefunden. Der Analyzer-Build enthielt keine Warnungen oder Fehler. 17 Tests prüfen den Download über eine lokale HTTP-Testinstanz, Abbruchverhalten, Dateischreibvorgänge, Client-Lebenszyklus, nicht unterstützte Legacy-Manifeste sowie Sicherheitsregressionen bei Pfadauflösung und Fehlermeldungen. Eine Suche nach typischen Geheimnis-Markern im Quellbestand war ebenfalls ohne Fund.

Folgende Befunde wurden in diesem Review behoben:

1. Download-Dateinamen werden vor dem Schreiben kanonisch aufgelöst und dürfen das konfigurierte Zielverzeichnis nicht verlassen. Dies schützt vor `..`- und absoluten Pfaden aus Manifesten oder Legacy-Workshop-Metadaten.
2. `SteamInvalidBranchPasswordException` enthält das Branch-Passwort nicht mehr in der Fehlermeldung.

## Restrisiken und Betriebsregeln

- `ConsoleSteamAuthenticator` ist eine interaktive Beispielimplementierung. Mit gesetztem `persistenceDirectory` speichert sie Access-Token und Guard-Daten als Klartext. Für Produktion muss eine eigene `SteamAuthenticator`-Implementierung mit einem Secret Store verwendet werden.
- Ein automatischer Test gegen echte Steam-Konten, Lizenzen oder Workshop-Inhalte ist absichtlich nicht Teil der CI. Er würde Zugangsdaten und kostenpflichtige/externe Ressourcen erfordern. Vor dem produktiven Einsatz ist ein manueller Smoke-Test mit einem dedizierten Testkonto nötig.
- Workshop-URLs und Steam-CDN-Endpunkte sind externe Datenquellen. Das SDK prüft Zielpfade, kann aber weder die Lizenzberechtigung noch die Verfügbarkeit externer Steam-Dienste garantieren.
- Zugangsdaten, Access-Token, Guard-Daten und Branch-Passwörter dürfen nicht geloggt, in Sourcecode hinterlegt oder als Kommandozeilenargumente übergeben werden.
