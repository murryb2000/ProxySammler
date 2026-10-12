PROXYSAMMLER 1.8.6
================

Portable Windows-Anwendung zum Sammeln und Prüfen freier HTTP-, SOCKS4- und
SOCKS5-Proxys.

START
-----
1. ZIP-Datei vollständig entpacken.
2. ProxySammler-1.8.6-Windows-x64.exe starten.
3. Gewünschte Proxytypen und das Prüfprofil auswählen.
4. "Sammeln und prüfen" oder "Neue Proxys suchen" anklicken.

DREI EINFACHE ERGEBNISLISTEN
----------------------------
Im Ordner "Ergebnisse" verwaltet das Programm nur noch diese drei dauerhaften
Listen im Format IP:PORT:

  HTTP.txt
  SOCKS4.txt
  SOCKS5.txt

Beim ersten Start werden vorhandene Trefferlisten aus Version 1.5 automatisch
übernommen. Neue Dateien werden nicht pro Prüflauf angelegt.

NEUE PROXYS SUCHEN
------------------
"Neue Proxys suchen" prüft zunächst unbekannte Kandidaten und früher
ausgefallene Proxys. Mit aktiviertem Negativcache werden kürzlich ausgefallene
Adressen nach sechs, 24 oder 72 Stunden erneut geprüft; ohne Haken sofort. Neue
funktionierende Treffer werden ohne Duplikate an die bestehenden drei Listen
angehängt. Danach prüft das Programm die vollständigen betroffenen Listen ein
zweites Mal und entfernt alle nicht mehr funktionierenden Proxys. Am Ende
enthalten HTTP.txt, SOCKS4.txt und SOCKS5.txt nur die aktuell bestätigten
Einträge.

STÜNDLICHE AUTOMATISCHE SUCHE
-----------------------------
Der Haken "Stündlich automatisch neue Proxys suchen" startet eine Stunde nach
dem Einschalten erstmals "Neue Proxys suchen" und wiederholt dies stündlich,
solange das Programm geöffnet und der Haken gesetzt ist. Jeder Lauf verwendet
die dann aktuellen Einstellungen und die aktuelle sources.txt. Läuft zu einem
geplanten Zeitpunkt bereits eine Prüfung, wird der automatische Lauf danach
nachgeholt. Automatische Läufe zeigen keine Abschluss-Popups; Fortschritt und
Ergebnis erscheinen im Programm. Der Haken ist bei jedem Programmstart aus.

LISTEN ERNEUT PRÜFEN
--------------------
"Listen erneut prüfen" testet die gespeicherten Einträge der ausgewählten
Proxytypen. Tote Einträge werden entfernt; nicht ausgewählte Listen bleiben
unverändert. Wenn bei einer Nachprüfung gar kein Proxy bestätigt wird, werden
die alten Listen zum Schutz vor einem vorübergehend ausgefallenen Testziel
nicht überschrieben.

PRÜFPROFILE
-----------
"Standard (HTTPS)" prüft HTTP-, SOCKS4- und SOCKS5-Proxys mit einem echten
TLS-Aufbau. "MacAttack-kompatibel" prüft nur HTTP-Proxys mit demselben
Verfahren wie MacAttack, setzt den Timeout auf 30 Sekunden und deaktiviert den
Turbo-Modus.

TURBO-MODUS
-----------
Der Turbo-Modus führt vor der vollständigen Prüfung einen Porttest mit einer
Sekunde Zeitlimit aus. Das beschleunigt große Listen deutlich. Sehr langsame
Proxys können dabei jedoch aussortiert werden, obwohl sie ohne Turbo innerhalb
des normalen Timeouts noch funktioniert hätten. Das Ergebnis ist deshalb
nicht immer vollständig identisch.

TOTE 6 BIS 72 STUNDEN ÜBERSPRINGEN
---------------------------
Mit gesetztem Haken werden ausgefallene Proxys nach dem ersten Fehler für
sechs Stunden, nach dem zweiten für 24 und danach für 72 Stunden pausiert. Ohne Haken dauert die Prüfung länger und
verursacht mehr Verbindungen, kann aber Proxys früher wiederfinden, die nur
vorübergehend gestört waren. Die vollständige Nachprüfung der drei gespeicherten
Listen überspringt keine Einträge.

PRÜF-BOTS UND TIMEOUT
---------------------
Die Zahl der Prüfbots ist die Obergrenze für gleichzeitige Prüfungen. Bei
großen Kandidatenmengen beginnt das Programm mit höchstens 50 Bots und passt
die Zahl zwischen Prüfblöcken anhand von Durchsatz und Zeitüberschreitungen
vorsichtig bis zur gewählten Obergrenze an. Kleine Mengen nutzen sofort den
eingestellten Wert. 50 ist ein guter Ausgangswert. Bei Router- oder Verbindungsproblemen
den Wert reduzieren. Der Timeout bestimmt die maximale Dauer einer einzelnen
vollständigen Prüfung.

ZWISCHENABLAGE
--------------
Mit "IP:Port kopieren" werden die funktionierenden Proxys des zuletzt
angezeigten Prüflaufs zeilenweise in die Windows-Zwischenablage kopiert.

QUELLEN
-------
Die Datei sources.txt kann über "Quellen bearbeiten" geöffnet werden.
Im Fenster "Neue Quellen finden" zeigt "Quellenertrag" pro URL die letzten
funktionierenden und neu bestätigten Proxys sowie den Gesamtertrag. Nach
drei Abrufen ohne neue bestätigte Treffer wird eine Quelle bei der stündlichen
automatischen Suche sechs Stunden pausiert. Manuell gestartete Suchen laden
weiterhin alle Quellen; eine erfolgreiche Quelle wird wieder normal berücksichtigt.
Die Statistik steht in source_performance.json. Format:

  protokoll|https://adresse/zur/liste.txt

Erlaubte Protokolle: http, socks4, socks5

NEUE QUELLEN FINDEN
-------------------
Der Button "Neue Quellen finden" öffnet ein eigenes Verwaltungsfenster. Die
GitHub-Suche durchsucht kürzlich aktualisierte öffentliche Repositories nach
Proxylisten und Links in README-Dateien. Ein API-Schlüssel ist nicht nötig.
Optional kann GITHUB_TOKEN als Umgebungsvariable für höhere GitHub-Limits
gesetzt werden. Bei einer Ratenbegrenzung zeigt das Programm den nächsten
möglichen Zeitpunkt an.

Gefundene Quellen werden höchstens 8 MB groß heruntergeladen, auf Proxyformate
untersucht und zunächst mit höchstens 12 Proxyadressen anhand der normalen
Prüfroutine getestet. Ohne Treffer folgen bis zu zwei weitere Stichproben:
insgesamt höchstens 36 verschiedene Adressen. Bei erneuter Prüfung verschiebt
sich die Auswahl. Eine Stichprobe kann dennoch funktionierende Adressen übersehen.
Quellen ohne Treffer oder mit Abruffehlern werden bei späteren Suchläufen erneut
geprüft. Quellen- und normale Proxy-Prüfungen laufen nicht gleichzeitig.
TXT-, CSV- und JSON-Quellen werden auch nach der Übernahme gelesen; die
Protokollauswahl filtert die tatsächlichen Proxytypen auch bei gemischten Listen. Kandidaten und Einstellungen stehen getrennt in source_candidates.json
und source_discovery_settings.json. Beschädigte JSON-Dateien werden beim Start
ignoriert. "Ausgewählte Quelle prüfen" wiederholt den Test. Nur geprüfte Quellen
können manuell übernommen werden. "Alle geeigneten Quellen übernehmen" nutzt
die Bewertung und verlangt mindestens einen funktionierenden Proxy. Die
automatische Übernahme ist beim ersten Start ausgeschaltet.

Die automatische Quellensuche läuft nur bei geöffnetem Programm, täglich oder
wöchentlich. Sie ist unabhängig von der stündlichen Proxy-Suche. Eine neue
Quelle wird ohne doppelte URL an sources.txt angehängt; die bisherige Datei
bleibt zusätzlich als sources.txt.bak erhalten. Eigene Quellen werden nie
automatisch gelöscht. Nach manueller Übernahme kann "Neue Proxys suchen"
sofort mit den aktuellen Einstellungen gestartet werden.

Bewertung (0–100 Punkte): Erreichbarkeit 20, eindeutige Adressen bis 20,
funktionierende Proxys bis 20, Erfolgsquote bis 20, Aktualität (30 Tage) 10.
Abzüge: doppelter Inhalt 25, bekannte Quelle 20, wiederholte Fehler bis 30.
Automatische Übernahme ab 45 Punkten und mindestens einem funktionierenden
Treffer. Eine niedrige Erfolgsquote allein schließt eine Quelle nicht aus.

Für automatisch entdeckte URLs werden nur HTTP/HTTPS und öffentliche IPv4-Ziele
zugelassen. DNS-Auflösung und Weiterleitungen werden erneut geprüft. Lokale,
private und reservierte Ziele sind auch aus Proxy-Stichproben ausgeschlossen.
IPv6-Ziele werden derzeit nicht
abgerufen. Manuell eingetragene sources.txt-Quellen bleiben davon unberührt.

SICHERHEIT
----------
Öffentliche kostenlose Proxys sind nicht vertrauenswürdig. Darüber sollten
keine Passwörter, Cookies oder vertraulichen Inhalte übertragen werden.
Windows SmartScreen kann bei der nicht digital signierten Anwendung warnen.

FENSTER UND BEDIENELEMENTE
--------------------------
Beim ersten Start öffnet sich das Fenster mit etwa 1335 × 733 Pixeln zentriert.
Größe, Position und maximierter Zustand werden beim Schließen in
window_settings.json gespeichert und beim nächsten Start wiederhergestellt.
Auf kleineren Bildschirmen wird das Fenster passend begrenzt. Bei schmalen
Fenstern verschieben sich Optionsgruppen und Buttons in zusätzliche Zeilen.
Die Buttons der zweiten Zeile behalten nach dem Start ihre Breite und Position,
bis der Platz für einen Umbruch nicht mehr ausreicht.

ÄNDERUNGEN IN 1.8.6
------------------
- Quellenertrag pro URL anzeigen und bei automatischer Suche Quellen ohne
  neue bestätigte Treffer zeitweise pausieren.
- Bei großen Prüfungen die Zahl gleichzeitig aktiver Bots vorsichtig anpassen.
- Die drei Ergebnislisten über temporäre Dateien sicher ersetzen.

ÄNDERUNGEN IN 1.8.5
------------------
- Abschlussmeldungen nennen die Dauer der Suche oder Listenprüfung.
- In der Statuszeile steht die Laufzeit direkt hinter „Fertig“.

ÄNDERUNGEN IN 1.8.4
------------------
- Startgröße wie im Referenzbild; spätere Fenstergröße und Position bleiben erhalten.
- Buttons und Optionen behalten ihre Reihenfolge und brechen erst bei Platzmangel um.

ÄNDERUNGEN IN 1.8.3
------------------
- Wiederholt ausgefallene Proxys werden gestaffelt nach 6, 24 und 72 Stunden erneut geprüft.
- Ohne Haken werden alle Kandidaten geprüft. Erfolgreiche Tests setzen die Fehlerfolge zurück.

ÄNDERUNGEN IN 1.8.2
------------------
- Bei Sammlung, Proxy-Prüfung und Quellensuche wird pro Abschnitt eine geschätzte Restzeit angezeigt.
- Beim Laden der Quellen zeigt das Programm die abgeschlossenen Quellen an.
- Die Schätzung wird nach einigen Ergebnissen eingeblendet und für neue Phasen zurückgesetzt.

ÄNDERUNGEN IN 1.8.1
------------------
- CSV-/JSON-Quellen funktionieren auch in der normalen Proxy-Suche.
- Gemischte Listen respektieren die ausgewählten Proxytypen.
- Quellen- und Proxy-Prüfungen laufen nacheinander; fällige stündliche Läufe warten.
- Frühere Proxyfehler werden nach Ablauf des Negativcache erneut geprüft.
- Ohne Treffer wird die Quellenstichprobe auf bis zu 36 Adressen erweitert.
- Quellen ohne Treffer und fehlerhafte Kandidaten werden erneut geprüft.
- Alte Trefferzahlen bleiben nach einem Abruffehler nicht stehen.

UPDATE
------
Die neue EXE kann im bisherigen Programmordner verwendet werden. Beim Entpacken
nicht die eigene sources.txt durch die mitgelieferte Standardliste ersetzen.
Ergebnisse, proxy_history.json, source_performance.json, window_settings.json und die JSON-Dateien der Quellensuche behalten.
