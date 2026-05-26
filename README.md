# Heroes 5 Trainer

Trainer do gry **Heroes of Might and Magic V** (oryginał oraz mod **Might & Magic: Heroes 5.5**). Pozwala modyfikować statystyki aktywnego bohatera, dosypywać zasoby i podkręcać zdobywanie XP.

Obsługiwane wersje gry (rozpoznawane po nazwie procesu):
- `MMH55_64.exe` (mod 5.5) - pełne wsparcie
- `H5_Game.exe` (oryginał) - tylko patch XP; reszta funkcji wymaga uzupełnienia offsetów dla tej wersji

---

## Uruchomienie

1. **Uruchom grę.**
2. **Uruchom trainer jako Administrator** (wymagane do modyfikacji pamięci procesu).
3. Trainer automatycznie czeka na proces gry i podpina się, gdy go znajdzie. Po zamknięciu gry wraca do trybu oczekiwania.

Trainer emituje eventy JSON Lines na stdout (jeden JSON na linię) - format używany przez ewentualnego sidecara UI. Komendy można też wprowadzać przez stdin (zwykły tekst lub JSON: `{"command":"AddGold","value":50000}`).

---

## Hotkeye

Wszystkie hotkeye używają modyfikatora **Ctrl** (część dodatkowo **Alt**). Wartości "default" stosują się przy uruchomieniu z hotkeya - przez stdin można podać własne.

### Bohater - statystyki i stan

| Hotkey | Komenda | Działanie |
|---|---|---|
| Ctrl+1 | RefillMovement | Napełnia punkty ruchu do bieżącego maksa |
| Ctrl+2 | XpPatchToggle | Toggle patcha XP (bonus +2 000 000 000 przy następnym zdobyciu XP - **działa globalnie, także na wrogów!**) |
| Ctrl+3 | TrackerToggle | Włącza/wyłącza tracker aktywnego bohatera (wymagany dla większości akcji niżej) |
| Ctrl+4 | ShowSnapshot | Wypisuje aktualne statystyki bohatera |
| Ctrl+5 | SetAttack | Atak = 99 |
| Ctrl+6 | SetDefense | Obrona = 99 |
| Ctrl+7 | SetSpellPower | Spell Power = 99 |
| Ctrl+8 | SetKnowledge | Wiedza = 99 |
| Ctrl+9 | RefillMana | Napełnia manę do maksa |
| Ctrl+0 | ToggleFreezeAttack | Toggle zamrażania ataku na 99 (gra próbuje co klatkę zmieniać - freeze nadpisuje) |
| Ctrl+- | AddXp | Dodaje +1 000 000 XP aktywnemu bohaterowi |
| Ctrl+= | DumpHero | Hex dump struktury bohatera (384 B) do diagnostyki offsetów |
| Ctrl+Alt+1 | SetMorale | Morale = 99 (bonus do bazy) |
| Ctrl+Alt+2 | SetLuck | Luck = 99 (bonus do bazy) |

### Zasoby aktywnego gracza

| Hotkey | Komenda | Default delta |
|---|---|---|
| Ctrl+Alt+3 | AddWood | +99 |
| Ctrl+Alt+4 | AddOre | +99 |
| Ctrl+Alt+5 | AddMercury | +99 |
| Ctrl+Alt+6 | AddSulfur | +99 |
| Ctrl+Alt+7 | AddCrystal | +99 |
| Ctrl+Alt+8 | AddGems | +99 |
| Ctrl+Alt+9 | AddGold | +999 999 |

### Ruch

| Hotkey | Komenda | Działanie |
|---|---|---|
| Ctrl+Alt+0 | SetMovement | Ustawia punkty ruchu na 999 999 (działa do końca tury - gra przeliczy ruch z bazowego maksa po zakończeniu tury) |

---

## Instrukcja obsługi - typowy flow

1. Uruchom grę i wejdź do rozgrywki (mapa, nie menu).
2. Uruchom trainer (jako Admin) - powinien zgłosić `GameAttached`.
3. **Ctrl+3** żeby włączyć tracker, potem **wybierz bohatera w grze** (kliknij na niego). Tracker zapamiętuje wskaźnik na bohatera dopiero po faktycznym zaznaczeniu.
4. Dowolne akcje statystyk/ruchu - efekt natychmiastowy. Zasoby działają bez trackera - czytają strukturę gracza inną ścieżką.

---

## Ważne informacje

- **Patch XP działa globalnie** - jeśli wróg zdobędzie XP gdy patch jest aktywny, on też dostanie bonus. Toggle po użyciu.
- **Uprawnienia:** Program wymaga uprawnień administratora do modyfikacji pamięci procesu gry.
- **Save / load:** Wszystkie zapisy działają tylko na bieżącej sesji - po wczytaniu save'a zmodyfikowane wartości NIE są przywracane przez trainer (trzeba zaaplikować ponownie). Adresy wskaźników są stabilne między save'ami i restartami gry.
- **Zasoby - nieobsługiwana wersja gry:** Jeśli komenda zasobowa zwraca `Warning: Nie udalo sie wyliczyc adresu zasobow`, znaczy że dla danej wersji procesu nie ma skonfigurowanej ścieżki wskaźników (na razie tylko `MMH55_64.exe`).