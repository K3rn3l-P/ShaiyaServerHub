# Playbook — Riavvio o crash inatteso del server

## Segnale
`windows_event_log(focus:"restarts")` mostra un evento System 6008 (arresto inatteso), 41 (Kernel-Power,
riavvio senza shutdown pulito), o 1001 (BugCheck/BSOD) SENZA un evento 1074 (shutdown/restart
PIANIFICATO) vicino — quello è lo scenario "il server è ripartito da solo mentre l'admin era via".

**Filtro noto già applicato**: sul server reale gli eventi 41 con messaggio "Hypervisor launch failed"
sono un artefatto BENIGNO di virtualizzazione annidata (verificato: occorrenze ripetute, stesso
messaggio, nessun disservizio correlato) — `windows_event_log` li esclude già in automatico, quindi se
il tool segnala un 41/6008/1001 è verosimilmente un evento reale, non rumore.

## Diagnosi (corroborazione — MAI dichiarare "attacco" da un solo riavvio sporco)
1. `services_status` — tutti i servizi sono tornati su dopo il boot? (Se no, valuta `heal_game_services`
   per i servizi di gioco — quello resta il tool giusto, indipendente dalla causa del riavvio.)
2. `disk_space` — un disco pieno può causare crash o mancati riavvii puliti.
3. `sql_health` — SQL Server si è ripreso? Query bloccate residue?
4. `windows_event_log(focus:"service_crashes")` — cos'altro è morto nella stessa finestra (7031/7034)?
   Un cluster di crash insieme al riavvio racconta una storia diversa da un riavvio isolato.
5. Cause comuni NON malevole da considerare prima di allarmare: Windows Update con riavvio automatico,
   interruzione di corrente, problema driver/hardware — un riavvio pulito isolato, servizi tutti tornati
   su, nessun altro segnale = probabilmente non un incidente di sicurezza.

## Mitigazione — SOLO con i tool che esistono oggi
- Se i servizi di GIOCO non sono tornati su: `heal_game_services` (T1, già esistente) — stesso
  meccanismo di sempre, indipendente dalla causa del riavvio.
- Nessuna azione OS-level automatica in questa fase (V1 è solo osservazione+report per i segnali
  Windows-level) — segnalare chiaramente all'admin timeline + correlazioni trovate, lasciare la diagnosi
  approfondita (hardware/driver/Windows Update) a un intervento umano.

## NON fare
- Non dichiarare "il server è stato attaccato" da un singolo evento di riavvio senza corroborazione da
  altri sensori (crash servizio, blocco SQL, disco pieno, eventi di sicurezza nella stessa finestra).
- Non proporre riavvii/interventi OS aggiuntivi da soli — nessun tool esiste per quello in V1.
