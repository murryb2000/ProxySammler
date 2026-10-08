ProxySammler 1.8.1 behebt Fehler bei der neuen Quellensuche und beim Wiederfinden ausgefallener Proxys.

- Übernommene CSV- und JSON-Quellen liefern jetzt auch in der normalen Proxy-Suche Kandidaten.
- Bei gemischten Quellen wird nach dem tatsächlichen Proxytyp gefiltert.
- Quellensuche und normale Proxy-Prüfung laufen nacheinander. Fällige stündliche Suchen warten auf das Ende der Quellensuche.
- Früher ausgefallene Proxys werden nach sechs Stunden erneut geprüft. Ohne „Tote 6 Std. überspringen“ werden sie sofort erneut berücksichtigt.
- Ohne Treffer wird die Quellenprüfung von zwölf auf bis zu 36 verschiedene Stichproben erweitert. Die Auswahl verschiebt sich bei Wiederholung.
- Quellen ohne Treffer und fehlerhafte Kandidaten werden bei späteren Suchläufen erneut geprüft.
- Abruffehler zeigen keine veralteten Trefferzahlen mehr an.

Die normale HTTP-/SOCKS4-/SOCKS5-Prüfung bleibt unverändert. Stichproben garantieren nicht, dass jede funktionierende Adresse gefunden wird.

**Download:** Windows x64, portable ZIP oder einzelne EXE, ohne separate .NET-Installation.

**Update:** Eigene `sources.txt`, den Ordner `Ergebnisse`, `proxy_history.json` und die JSON-Dateien der Quellensuche behalten. Beim Entpacken die eigene Quellenliste nicht mit der mitgelieferten Standardliste überschreiben.
