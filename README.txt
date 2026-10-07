PROXYSAMMLER 1.7
================

Portable Windows-Anwendung zum Sammeln und Prüfen freier HTTP-, SOCKS4- und
SOCKS5-Proxys.

START
-----
1. ZIP-Datei vollständig entpacken.
2. ProxySammler.exe starten.
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
"Neue Proxys suchen" prüft zunächst nur bisher unbekannte Kandidaten. Neue
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

TOTE 6 STUNDEN ÜBERSPRINGEN
---------------------------
Mit gesetztem Haken werden Proxys, die kürzlich bereits ausgefallen sind, für
sechs Stunden nicht erneut geprüft. Ohne Haken dauert die Prüfung länger und
verursacht mehr Verbindungen, kann aber Proxys früher wiederfinden, die nur
vorübergehend gestört waren. Die vollständige Nachprüfung der drei gespeicherten
Listen überspringt keine Einträge.

PRÜF-BOTS UND TIMEOUT
---------------------
Die Zahl der Prüfbots bestimmt, wie viele Kandidaten gleichzeitig geprüft
werden. 50 ist ein guter Ausgangswert. Bei Router- oder Verbindungsproblemen
den Wert reduzieren. Der Timeout bestimmt die maximale Dauer einer einzelnen
vollständigen Prüfung.

ZWISCHENABLAGE
--------------
Mit "IP:Port kopieren" werden die funktionierenden Proxys des zuletzt
angezeigten Prüflaufs zeilenweise in die Windows-Zwischenablage kopiert.

QUELLEN
-------
Die Datei sources.txt kann über "Quellen bearbeiten" geöffnet werden. Format:

  protokoll|https://adresse/zur/liste.txt

Erlaubte Protokolle: http, socks4, socks5

SICHERHEIT
----------
Öffentliche kostenlose Proxys sind nicht vertrauenswürdig. Darüber sollten
keine Passwörter, Cookies oder vertraulichen Inhalte übertragen werden.
Windows SmartScreen kann bei der nicht digital signierten Anwendung warnen.
