ProxySammler 1.8.3 reduziert die wiederkehrende Prüfung mehrfach ausgefallener Kandidaten.

- Ein Proxy wird nach dem ersten Fehlschlag wie bisher nach sechs Stunden erneut versucht.
- Scheitert er wieder, wartet die nächste Prüfung 24 Stunden; ab dem dritten Fehler 72 Stunden.
- Sobald ein Proxy funktioniert, beginnt die Fehlerzählung wieder bei null.
- Die Einstellung „Tote 6–72 Std. überspringen“ schaltet diese Wartezeiten ein oder aus. Ohne Haken werden alle gesammelten Kandidaten erneut geprüft.
- Neue Proxys werden sofort geprüft. Die vollständige Nachprüfung der gespeicherten Ergebnislisten prüft weiterhin jeden Eintrag.
- Durch die Wartezeiten können vorübergehend wieder funktionierende Proxys später erkannt werden. Sie werden nie dauerhaft ausgeschlossen.

**Download:** Windows x64, portable ZIP oder einzelne EXE, ohne separate .NET-Installation.

**Update:** Eigene `sources.txt`, den Ordner `Ergebnisse`, `proxy_history.json` und die JSON-Dateien der Quellensuche behalten. Beim Entpacken die eigene Quellenliste nicht mit der mitgelieferten Standardliste überschreiben.
