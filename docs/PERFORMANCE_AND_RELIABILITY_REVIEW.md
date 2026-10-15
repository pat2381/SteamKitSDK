# Performance- und Zuverlässigkeitsreview

Stand: 15. Oktober 2026, Version 0.9.0.

Dieses Review kombiniert statische Analyse, Tests und eine Prüfung der Download-, CDN- und Authentifizierungspfade. Ein reproduzierbarer Live-Benchmark gegen Steam ist nicht enthalten, weil er ein berechtigtes Steam-Testkonto, reale Depots und variable externe CDN-Bedingungen benötigt.

## Umgesetzte Verbesserungen

| Bereich | Befund | Umsetzung | Wirkung |
| --- | --- | --- | --- |
| Zustandsmodell | `MultiDepotDownloadHandler` blieb nach erfolgreichem Download nicht im Zustand `Downloaded`. | Erfolgszustand gesetzt, Fehler führen zu `Failed` und werden weitergereicht. | Aufrufer können Fortschritt und Fehler korrekt behandeln. |
| Fehlerbehandlung | Verify- und Downloadfehler wurden an mehreren Stellen verschluckt. | Fehler werden nach der Zustandsaktualisierung wieder geworfen. | Keine scheinbar erfolgreichen, aber unvollständigen Downloads. |
| Chunk-Retries | Netzwerk- und Dateischreibfehler konnten unendlich wiederholt werden. | Maximal fünf Chunk-Versuche; Schreibfehler werden mit Kontext weitergegeben. | Keine Endlosschleifen, besser diagnosierbare Ausfälle. |
| Logging | Retry-Informationen waren nicht sichtbar; eine Logmeldung formatierte Zeichenketten vorzeitig. | Strukturierte Warnung je fehlgeschlagenem Chunk-Versuch und strukturierte Chunk-Anzahl. | Abfragen nach Depot und Versuch sind in strukturierten Logsystemen möglich. |
| Speicher | Für jeden Chunk wurde zusätzlich eine vollständige Delegate-Liste erzeugt; die Parallelroutine kopierte sie nochmals in ein Array. | Die Parallelisierung verarbeitet das `IEnumerable` gestreamt und hält nur maximal `maxConcurrentDownloadsPerTask` aktive Aufgaben. | Weniger temporäre Allokationen bei großen Spielen. |
| CDN-Abbruch | Wartende Entnahmen aus dem CDN-Pool reagierten nicht auf dessen interne Erschöpfung. | Aufrufer- und Pool-Abbruchtoken werden verknüpft. | Wartende Downloads können korrekt abbrechen statt unbestimmt zu blockieren. |
| Workshop-Kataloge | Die Rohabfrage sammelte ohne Grenze alle Seiten im Speicher. | Neue Überladung mit `maximumResults`, Cursor-Schutz und Abbruchprüfung. | Aufrufer können den Speicherverbrauch begrenzen. |
| Depot-Auswahl | Eine leere gefilterte Depotliste wurde nicht erkannt. | Prüfung auf `Count == 0`. | Sofortige, eindeutige Fehlermeldung statt eines leeren Handlers. |

## Logging und Fehlerbehandlung

`DefaultDownloadHandler.Logger` und `SteamCdnServerPool.Logger` akzeptieren `Microsoft.Extensions.Logging.ILogger`. Für aussagekräftige Logs sollte die Anwendung mindestens Informationen zu App-ID, Depot-ID, Manifest-ID, Dateianzahl, heruntergeladenen Bytes, Retry-Anzahl und Dauer erfassen. Zugangsdaten, Steam-Guard-Codes, Refresh-Token und Branch-Passwörter gehören nie in Logs.

Die bestehenden Events (`FileVerified`, `FileDownloaded`, `VerificationCompleted`, `DownloadComplete`) sind für UI-Fortschritt geeignet. Sie sollten nicht für blockierende oder langsame Arbeit verwendet werden, weil sie die Abschlussreihenfolge beeinflussen können.

## Empfohlene nächste Ausbaustufe

Die folgenden Punkte sind bewusst nicht in Version 0.9.0 erzwungen, weil sie größere öffentliche API- oder Architekturänderungen benötigen:

1. **Asynchroner, begrenzter CDN-Pool** – `BlockingCollection` und die zusätzlichen `Task.Run`-Aufrufe für Chunk-Starts durch einen `Channel<Server>`-basierten Pool mit eindeutigen Servern ersetzen. Das reduziert Thread-Pool-Blockierung und verhindert doppelte Server-Einträge beim Nachfüllen.
2. **Streaming-Pipeline für Manifeste und Chunks** – Aktuell werden alle `ChunkJob`-Objekte nach der Verifikation gehalten. Eine producer/consumer-Pipeline würde die Peak-Speichernutzung bei sehr großen Depots weiter reduzieren, ist aber ein größeres Redesign wegen Dateiverifikation, Fortschrittsanzeige und Reihenfolgegarantien.
3. **Konfigurierbare Resilience-Optionen** – Die fünf Chunk-Versuche sollten in einer Optionsklasse mit exponentiellem Backoff, Jitter und einer Gesamtzeitbegrenzung konfigurierbar werden. Derzeit ist die Grenze absichtlich fest, um Endlosschleifen zu verhindern.
4. **Einheitliche Observability** – `ILoggerFactory`, `ActivitySource` und `Meter` zentral über Optionen in `SteamClient` einspeisen. Sinnvolle Metriken: aktive Downloads, Retry-Zähler, Download-Bytes, Fehler nach Kategorie, CDN-Latenz und Dauer der Verifikation.
5. **Cache für App- und Branch-Metadaten** – `GetDepotsAsync` kann Branch- und Produktinformationen mehrfach abrufen. Ein zeitlich begrenzter, thread-sicherer Cache würde API-Aufrufe reduzieren; Invalidierung und Berechtigungswechsel müssen dabei klar definiert sein.
6. **Sichere Token-Speicherung als Erweiterungspunkt** – Der Demo-Authenticator bleibt absichtlich einfach. Eine plattformübergreifende, optionale Secret-Store-Abstraktion sollte in einer Major-Version als separates Paket angeboten werden.

## Messplan für einen realen Lasttest

1. Ein dediziertes Steam-Testkonto mit einem kleinen, einem mittleren und einem großen berechtigten Depot verwenden.
2. Für `maxConcurrentDownloadsPerTask` die Werte 2, 4, 8, 16 und 32 messen; höhere Parallelität ist nicht automatisch schneller und kann CDN-Rate-Limits auslösen.
3. Pro Durchlauf Durchsatz, Peak Working Set, GC-Pausen, offene Dateihandles, CDN-Fehlerquote, Retry-Anzahl und Zeit für Verifikation sowie Download erfassen.
4. Jede Konfiguration mindestens dreimal in derselben Netzwerkumgebung wiederholen und Median sowie 95. Perzentil vergleichen.
5. Den Zielordner vor einem Wiederholungsdurchlauf beibehalten, um die Verifikations- und Resume-Performance separat zu bewerten.

## Validierung dieses Änderungsstands

Release-Build mit aktivierten .NET-Analyzern: keine Warnungen und keine Fehler. Die Test-Suite umfasst 20 Tests, darunter Zustands- und Fehlerweitergabe im Mehrdepot-Handler. Die vollständige CI-validierte Paket-, Coverage- und Schwachstellenprüfung bleibt unverändert aktiv.
