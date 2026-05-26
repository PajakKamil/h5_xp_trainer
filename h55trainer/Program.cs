using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace Heroes5Trainer
{
    internal static class Program
    {
        // Kody klawiszy Windows Virtual-Key.
        private const int VkControl   = 0x11;
        private const int VkAlt       = 0x12;   // VK_MENU
        private const int Vk0         = 0x30;
        private const int Vk1         = 0x31;
        private const int Vk2         = 0x32;
        private const int Vk3         = 0x33;
        private const int Vk4         = 0x34;
        private const int Vk5         = 0x35;
        private const int Vk6         = 0x36;
        private const int Vk7         = 0x37;
        private const int Vk8         = 0x38;
        private const int Vk9         = 0x39;
        private const int VkOemMinus  = 0xBD;   // '-'
        private const int VkOemPlus   = 0xBB;   // '='

        private const int KeyPressedMask = 0x8000;

        // Czasy uspienia oraz wartosci docelowe.
        private const int WaitForGameDelayMs = 1000;
        private const int PerKeyDebounceMs = 350;
        private const int IdleDelayMs = 10;
        private const int SnapshotPollMs = 200;
        private const int DefaultStatValue = 99;
        private const int XpQuickBonus = 1_000_000;
        private const int HeroDumpLength = 0x180;

        // Granice walidacji wartosci pochodzacych z stdin. Sztywne, bo gra i tak
        // klamruje wyzsze wartosci na UI a ujemne staty wywracaja oblicznia.
        private const int MinStatValue = 0;
        private const int MaxStatValue = 999;
        private const int MinXpDelta = -2_000_000_000;
        private const int MaxXpDelta =  2_000_000_000;

        // Akcje sa dispatched zarowno przez hotkeye, jak i przez stdin (sidecar Tauri).
        // Jeden lock serializuje obie sciezki, by GameMemory nie dostawal rownoleglych pisow.
        private static readonly object ActionLock = new object();

        // Bieżąca sesja podpięta pod gre. Zerujemy gdy gra znika.
        private static volatile TrainerSession? CurrentSession;

        private static void Main()
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.Title = "Heroes 5 Trainer - Console Edition";

            JsonOut.Emit("Banner", "Heroes 5 Trainer (Console App)");

            new Thread(StdinDispatcherLoop) { IsBackground = true, Name = "StdinDispatcher" }.Start();
            new Thread(SnapshotWatcherLoop) { IsBackground = true, Name = "SnapshotWatcher" }.Start();

            while (true)
            {
                using GameMemory memory = new GameMemory();

                JsonOut.Emit("WaitingForGame",
                    $"Oczekiwanie na uruchomienie gry ({string.Join(" / ", GameTarget.ModuleNames)})...");

                if (!AttachToGame(memory))
                    return;

                GameTarget target = GameTarget.FromProcessName(memory.GameProcess!.ProcessName)!;

                XpPatch xpPatch = new XpPatch(memory, target);
                ActiveHeroTracker tracker = new ActiveHeroTracker(memory, target);
                using FreezeManager freeze = new FreezeManager(memory, tracker);
                freeze.Start();

                TrainerSession session = new TrainerSession(memory, xpPatch, tracker, freeze);
                CurrentSession = session;

                JsonOut.Emit("GameAttached", $"Podpieto pod gre: {target.ModuleName}",
                    data: $"{{\"module\":\"{JsonOut.Escape(target.ModuleName)}\"}}");
                EmitHotkeysList();
                EmitCommandsList();

                RunHotkeyLoop(memory, session);

                CurrentSession = null;

                // Gra znikla - watek freeze stop, hookow nie usuwamy (proces nie zyje).
                freeze.Stop();

                JsonOut.Emit("GameDetached",
                    "Gra zostala zamknieta. Powrot do oczekiwania na gre...");
            }
        }

        private static bool AttachToGame(GameMemory memory)
        {
            while (!memory.TryFindProcess(GameTarget.ProcessNames))
                Thread.Sleep(WaitForGameDelayMs);

            if (memory.OpenForEditing())
                return true;

            JsonOut.Emit("Error",
                "Nie udalo sie uzyskac dostepu do pamieci gry. Uruchom program jako Administrator.");
            return false;
        }

        private static void RunHotkeyLoop(GameMemory memory, TrainerSession session)
        {
            Dictionary<int, long> lastPressedAtMs = new Dictionary<int, long>();

            while (memory.IsGameRunning)
            {
                CheckHotkey(Vk1, alt: false, lastPressedAtMs, "RefillMovement",   session);
                CheckHotkey(Vk2, alt: false, lastPressedAtMs, "XpPatchToggle",    session);
                CheckHotkey(Vk3, alt: false, lastPressedAtMs, "TrackerToggle",    session);
                CheckHotkey(Vk4, alt: false, lastPressedAtMs, "ShowSnapshot",     session);
                CheckHotkey(Vk5, alt: false, lastPressedAtMs, "SetAttack",        session);
                CheckHotkey(Vk6, alt: false, lastPressedAtMs, "SetDefense",       session);
                CheckHotkey(Vk7, alt: false, lastPressedAtMs, "SetSpellPower",    session);
                CheckHotkey(Vk8, alt: false, lastPressedAtMs, "SetKnowledge",     session);
                CheckHotkey(Vk9, alt: false, lastPressedAtMs, "RefillMana",       session);
                CheckHotkey(Vk0, alt: false, lastPressedAtMs, "ToggleFreezeAttack", session);
                CheckHotkey(VkOemMinus, alt: false, lastPressedAtMs, "AddXp",     session);
                CheckHotkey(VkOemPlus,  alt: false, lastPressedAtMs, "DumpHero",  session);
                CheckHotkey(Vk1, alt: true,  lastPressedAtMs, "SetMorale",        session);
                CheckHotkey(Vk2, alt: true,  lastPressedAtMs, "SetLuck",          session);

                Thread.Sleep(IdleDelayMs);
            }
        }

        // Detect Ctrl+<key> lub Ctrl+Alt+<key>. Stan Alt musi pasowac dokladnie,
        // by Ctrl+Alt+1 nie odpalal jednoczesnie akcji Ctrl+1.
        private static void CheckHotkey(int virtualKey, bool alt, Dictionary<int, long> lastPressedAtMs,
                                        string command, TrainerSession session)
        {
            if ((NativeMethods.GetAsyncKeyState(VkControl) & KeyPressedMask) == 0) return;
            if ((NativeMethods.GetAsyncKeyState(virtualKey) & KeyPressedMask) == 0) return;
            bool altDown = (NativeMethods.GetAsyncKeyState(VkAlt) & KeyPressedMask) != 0;
            if (alt != altDown) return;

            // Klucz debounce'u dla wariantu z Altem oddzielny, by Ctrl+1 i Ctrl+Alt+1 nie
            // dzielily licznika.
            int debounceKey = virtualKey | (alt ? 0x10000 : 0);

            long nowMs = Environment.TickCount64;
            if (lastPressedAtMs.TryGetValue(debounceKey, out long lastMs) && nowMs - lastMs < PerKeyDebounceMs)
                return;
            lastPressedAtMs[debounceKey] = nowMs;

            // Hotkeye nigdy nie niosa wartosci - akcja uzyje domyslnej (DefaultStatValue / XpQuickBonus).
            Dispatch(command, value: null, session);
        }

        private static void StdinDispatcherLoop()
        {
            string? line;
            while ((line = Console.ReadLine()) != null)
            {
                string trimmed = line.Trim();
                if (trimmed.Length == 0) continue;

                TrainerSession? session = CurrentSession;
                if (session == null)
                {
                    JsonOut.Emit("Error", $"Komenda '{trimmed}' odrzucona - brak podpietej gry.");
                    continue;
                }

                if (trimmed.StartsWith('{'))
                {
                    if (!TryParseJsonCommand(trimmed, out string? cmd, out int? value))
                        continue; // blad juz wyemitowany
                    Dispatch(cmd!, value, session);
                }
                else
                {
                    // Plain text form: sama nazwa komendy, bez wartosci.
                    Dispatch(trimmed, value: null, session);
                }
            }
        }

        // Format JSON ze stdin: {"command":"Name","value":123}. 'value' opcjonalne.
        private static bool TryParseJsonCommand(string line, out string? command, out int? value)
        {
            command = null;
            value = null;
            try
            {
                using JsonDocument doc = JsonDocument.Parse(line);
                JsonElement root = doc.RootElement;

                if (root.ValueKind != JsonValueKind.Object)
                {
                    JsonOut.Emit("Error", "Oczekiwano obiektu JSON na stdin.");
                    return false;
                }

                if (!root.TryGetProperty("command", out JsonElement cmdEl)
                    || cmdEl.ValueKind != JsonValueKind.String)
                {
                    JsonOut.Emit("Error", "Brak lub niepoprawne pole 'command' (oczekiwano stringa).");
                    return false;
                }
                command = cmdEl.GetString();
                if (string.IsNullOrWhiteSpace(command))
                {
                    JsonOut.Emit("Error", "Pole 'command' nie moze byc puste.");
                    return false;
                }

                if (root.TryGetProperty("value", out JsonElement valEl)
                    && valEl.ValueKind != JsonValueKind.Null)
                {
                    if (valEl.ValueKind != JsonValueKind.Number || !valEl.TryGetInt32(out int iv))
                    {
                        JsonOut.Emit("Error",
                            $"Pole 'value' dla komendy '{command}' musi byc liczba calkowita (Int32).");
                        return false;
                    }
                    value = iv;
                }
                return true;
            }
            catch (JsonException ex)
            {
                JsonOut.Emit("Error", $"Niepoprawny JSON: {ex.Message}");
                return false;
            }
        }

        private static void Dispatch(string command, int? value, TrainerSession session)
        {
            lock (ActionLock)
            {
                try
                {
                    CommandSpec? spec = TrainerSession.FindSpec(command);
                    if (spec == null)
                    {
                        JsonOut.Emit("Error", $"Nieznana komenda: {command}");
                        return;
                    }

                    int? effectiveValue;
                    if (spec.Value == null)
                    {
                        if (value.HasValue)
                            JsonOut.Emit("Warning",
                                $"Komenda '{command}' nie przyjmuje wartosci - pole 'value' zignorowane.");
                        effectiveValue = null;
                    }
                    else
                    {
                        int v = value ?? spec.Value.DefaultValue;
                        if (v < spec.Value.Min || v > spec.Value.Max)
                        {
                            JsonOut.Emit("Error",
                                $"Wartosc {v} dla '{command}' poza zakresem [{spec.Value.Min}..{spec.Value.Max}].");
                            return;
                        }
                        effectiveValue = v;
                    }

                    spec.Handler(session, effectiveValue);
                }
                catch (Exception ex)
                {
                    JsonOut.Emit("Error", ex.Message);
                }
            }
        }

        private static void SnapshotWatcherLoop()
        {
            HeroSnapshot? lastSnapshot = null;
            nint lastPtr = 0;

            while (true)
            {
                try
                {
                    TrainerSession? session = CurrentSession;
                    if (session != null
                        && session.Tracker.IsInstalled
                        && session.Tracker.TryGetHeroPtr(out nint heroPtr))
                    {
                        HeroSnapshot snap;
                        lock (ActionLock)
                            snap = HeroStats.Snapshot(session.Memory, heroPtr);

                        if (heroPtr != lastPtr || !snap.Equals(lastSnapshot))
                        {
                            EmitHeroSnapshot(snap, heroPtr);
                            lastSnapshot = snap;
                            lastPtr = heroPtr;
                        }
                    }
                    else
                    {
                        lastSnapshot = null;
                        lastPtr = 0;
                    }
                }
                catch
                {
                    // Gra moze zniknac w trakcie odczytu - kolejna iteracja zauwazy CurrentSession=null.
                }

                Thread.Sleep(SnapshotPollMs);
            }
        }

        // === Akcje (wywolywane przez Dispatch). value: po walidacji w Dispatch -
        // dla komend bez ValueSpec zawsze null, dla pozostalych zawsze ma sensowna wartosc. ===

        internal static void DoRefillMovement(TrainerSession s, int? _)
        {
            if (!TryRequireHero(s.Tracker, out nint heroPtr)) return;
            int max = HeroStats.ReadInt32(s.Memory, heroPtr, HeroStats.MovementMaxOffset);
            HeroStats.WriteInt32(s.Memory, heroPtr, HeroStats.MovementCurrentOffset, max);
            JsonOut.Emit("RefillMovement", $"Ruch napelniony ({max}/{max}).",
                data: $"{{\"value\":{max},\"max\":{max}}}");
        }

        internal static void DoXpPatchToggle(TrainerSession s, int? _)
        {
            if (!s.XpPatch.IsInstalled)
            {
                s.XpPatch.Install();
                JsonOut.Emit("XpPatchToggle",
                    $"Trainer XP AKTYWNY - nastepne zdobycie XP = +{XpPatch.XpBonus:N0}.",
                    enabled: true,
                    data: $"{{\"bonus\":{XpPatch.XpBonus}}}");
            }
            else
            {
                s.XpPatch.Uninstall();
                JsonOut.Emit("XpPatchToggle", "Trainer XP WYLACZONY.", enabled: false);
            }
        }

        internal static void DoTrackerToggle(TrainerSession s, int? _)
        {
            if (!s.Tracker.IsInstalled)
            {
                s.Tracker.Install();
                JsonOut.Emit("TrackerToggle",
                    "Tracker aktywnego bohatera AKTYWNY - wybierz bohatera w grze.",
                    enabled: true);
            }
            else
            {
                s.Tracker.Uninstall();
                JsonOut.Emit("TrackerToggle", "Tracker aktywnego bohatera WYLACZONY.",
                    enabled: false);
            }
        }

        internal static void DoShowSnapshot(TrainerSession s, int? _)
        {
            if (!TryRequireHero(s.Tracker, out nint heroPtr)) return;
            HeroSnapshot snap = HeroStats.Snapshot(s.Memory, heroPtr);
            EmitHeroSnapshot(snap, heroPtr);
        }

        internal static void DoSetAttack(TrainerSession s, int? v)     => SetStat(s, "atak",        HeroStats.AttackOffset,     v!.Value);
        internal static void DoSetDefense(TrainerSession s, int? v)    => SetStat(s, "obrona",      HeroStats.DefenseOffset,    v!.Value);
        internal static void DoSetSpellPower(TrainerSession s, int? v) => SetStat(s, "spell power", HeroStats.SpellPowerOffset, v!.Value);
        internal static void DoSetKnowledge(TrainerSession s, int? v)  => SetStat(s, "wiedza",      HeroStats.KnowledgeOffset,  v!.Value);

        private static void SetStat(TrainerSession s, string name, int fieldOffset, int value)
        {
            if (!TryRequireHero(s.Tracker, out nint heroPtr)) return;
            HeroStats.WriteInt32(s.Memory, heroPtr, fieldOffset, value);
            JsonOut.Emit("SetStat", $"{name} = {value}",
                data: $"{{\"stat\":\"{JsonOut.Escape(name)}\",\"value\":{value}}}");
        }

        internal static void DoRefillMana(TrainerSession s, int? _)
        {
            if (!TryRequireHero(s.Tracker, out nint heroPtr)) return;
            int max = HeroStats.ReadInt32(s.Memory, heroPtr, HeroStats.MaxManaOffset);
            HeroStats.WriteInt32(s.Memory, heroPtr, HeroStats.ManaOffset, max);
            JsonOut.Emit("RefillMana", $"Mana napelniona ({max}/{max}).",
                data: $"{{\"value\":{max},\"max\":{max}}}");
        }

        internal static void DoToggleFreezeAttack(TrainerSession s, int? v)
        {
            int freezeValue = v!.Value;
            bool nowFrozen = s.Freeze.Toggle(HeroStats.AttackOffset, freezeValue);
            JsonOut.Emit("ToggleFreezeAttack",
                nowFrozen ? $"Atak ZAMROZONY na {freezeValue}." : "Atak ODMROZONY.",
                enabled: nowFrozen,
                data: $"{{\"value\":{freezeValue}}}");
        }

        internal static void DoAddXp(TrainerSession s, int? v)
        {
            if (!TryRequireHero(s.Tracker, out nint heroPtr)) return;
            int delta = v!.Value;
            int current = HeroStats.ReadInt32(s.Memory, heroPtr, HeroStats.ExperienceOffset);
            int updated = current + delta;
            HeroStats.WriteInt32(s.Memory, heroPtr, HeroStats.ExperienceOffset, updated);
            JsonOut.Emit("AddXp", $"XP {current:N0} -> {updated:N0} ({(delta >= 0 ? "+" : "")}{delta:N0}).",
                data: $"{{\"before\":{current},\"after\":{updated},\"bonus\":{delta}}}");
        }

        internal static void DoDumpHero(TrainerSession s, int? _)
        {
            if (!TryRequireHero(s.Tracker, out nint heroPtr)) return;
            string dump = HeroStats.DumpHex(s.Memory, heroPtr, HeroDumpLength);
            JsonOut.Emit("DumpHero",
                $"Dump struktury bohatera @ 0x{heroPtr:X} ({HeroDumpLength} bajtow).",
                data: $"{{\"heroPtr\":\"0x{heroPtr:X}\",\"length\":{HeroDumpLength}," +
                      $"\"dump\":\"{JsonOut.Escape(dump)}\"}}");
        }

        internal static void DoSetMorale(TrainerSession s, int? v)
        {
            if (!TryRequireHero(s.Tracker, out nint heroPtr)) return;
            int value = v!.Value;
            HeroStats.WriteInt32(s.Memory, heroPtr, HeroStats.MoraleBonusOffsetA, value);
            JsonOut.Emit("SetMorale", $"Morale = {value} (bonus do bazy).",
                data: $"{{\"value\":{value}}}");
        }

        internal static void DoSetLuck(TrainerSession s, int? v)
        {
            if (!TryRequireHero(s.Tracker, out nint heroPtr)) return;
            int value = v!.Value;
            HeroStats.WriteInt32(s.Memory, heroPtr, HeroStats.LuckBonusOffsetA, value);
            JsonOut.Emit("SetLuck", $"Luck = {value} (bonus do bazy).",
                data: $"{{\"value\":{value}}}");
        }

        private static bool TryRequireHero(ActiveHeroTracker tracker, out nint heroPtr)
        {
            if (!tracker.IsInstalled)
            {
                JsonOut.Emit("Warning", "Najpierw wlacz tracker bohatera (TrackerToggle).");
                heroPtr = 0;
                return false;
            }
            if (!tracker.TryGetHeroPtr(out heroPtr))
            {
                JsonOut.Emit("Warning", "Brak aktywnego bohatera - wybierz bohatera w grze.");
                return false;
            }
            return true;
        }

        private static void EmitHeroSnapshot(HeroSnapshot s, nint heroPtr)
        {
            string data =
                $"{{\"heroPtr\":\"0x{heroPtr:X}\"," +
                $"\"attack\":{s.Attack},\"defense\":{s.Defense}," +
                $"\"spellPower\":{s.SpellPower},\"knowledge\":{s.Knowledge}," +
                $"\"experience\":{s.Experience}," +
                $"\"currentLevel\":{s.CurrentLevel},\"toLevel\":{s.ToLevel}," +
                $"\"mana\":{s.Mana},\"maxMana\":{s.MaxMana}," +
                $"\"movement\":{s.Movement},\"movementMax\":{s.MovementMax}}}";

            JsonOut.Emit("HeroSnapshot",
                $"ATK={s.Attack} DEF={s.Defense} SP={s.SpellPower} KN={s.Knowledge} " +
                $"LVL={s.CurrentLevel}->{s.ToLevel} XP={s.Experience:N0} " +
                $"MANA={s.Mana}/{s.MaxMana} RUCH={s.Movement}/{s.MovementMax}",
                data: data);
        }

        private static void EmitHotkeysList()
        {
            // Format zwarty - jeden event z lista hotkeyow w data, plus czytelny message.
            var sb = new StringBuilder();
            sb.Append("HOTKEYE: ");
            sb.Append("[Ctrl+1] RefillMovement; ");
            sb.Append($"[Ctrl+2] XpPatchToggle (+{XpPatch.XpBonus:N0}); ");
            sb.Append("[Ctrl+3] TrackerToggle; ");
            sb.Append("[Ctrl+4] ShowSnapshot; ");
            sb.Append($"[Ctrl+5] SetAttack={DefaultStatValue}; ");
            sb.Append($"[Ctrl+6] SetDefense={DefaultStatValue}; ");
            sb.Append($"[Ctrl+7] SetSpellPower={DefaultStatValue}; ");
            sb.Append($"[Ctrl+8] SetKnowledge={DefaultStatValue}; ");
            sb.Append("[Ctrl+9] RefillMana; ");
            sb.Append($"[Ctrl+0] ToggleFreezeAttack={DefaultStatValue}; ");
            sb.Append($"[Ctrl+-] AddXp (+{XpQuickBonus:N0}); ");
            sb.Append($"[Ctrl+=] DumpHero ({HeroDumpLength}B); ");
            sb.Append($"[Ctrl+Alt+1] SetMorale={DefaultStatValue}; ");
            sb.Append($"[Ctrl+Alt+2] SetLuck={DefaultStatValue}");

            string data =
                "{\"hotkeys\":[" +
                "{\"keys\":\"Ctrl+1\",\"command\":\"RefillMovement\"}," +
                "{\"keys\":\"Ctrl+2\",\"command\":\"XpPatchToggle\"}," +
                "{\"keys\":\"Ctrl+3\",\"command\":\"TrackerToggle\"}," +
                "{\"keys\":\"Ctrl+4\",\"command\":\"ShowSnapshot\"}," +
                "{\"keys\":\"Ctrl+5\",\"command\":\"SetAttack\"}," +
                "{\"keys\":\"Ctrl+6\",\"command\":\"SetDefense\"}," +
                "{\"keys\":\"Ctrl+7\",\"command\":\"SetSpellPower\"}," +
                "{\"keys\":\"Ctrl+8\",\"command\":\"SetKnowledge\"}," +
                "{\"keys\":\"Ctrl+9\",\"command\":\"RefillMana\"}," +
                "{\"keys\":\"Ctrl+0\",\"command\":\"ToggleFreezeAttack\"}," +
                "{\"keys\":\"Ctrl+-\",\"command\":\"AddXp\"}," +
                "{\"keys\":\"Ctrl+=\",\"command\":\"DumpHero\"}," +
                "{\"keys\":\"Ctrl+Alt+1\",\"command\":\"SetMorale\"}," +
                "{\"keys\":\"Ctrl+Alt+2\",\"command\":\"SetLuck\"}" +
                "]}";

            JsonOut.Emit("Hotkeys", sb.ToString(), data: data);
        }

        internal static void EmitCommandsList()
        {
            StringBuilder text = new StringBuilder();
            StringBuilder json = new StringBuilder();
            json.Append("{\"commands\":[");

            bool firstJson = true;
            foreach (KeyValuePair<string, CommandSpec> kv in TrainerSession.AllSpecs)
            {
                string name = kv.Key;
                CommandSpec spec = kv.Value;

                if (text.Length > 0) text.Append("; ");
                text.Append(name);
                if (spec.Value != null)
                    text.Append($"(value:int [{spec.Value.Min}..{spec.Value.Max}], default={spec.Value.DefaultValue})");
                else
                    text.Append("(no value)");

                if (!firstJson) json.Append(',');
                firstJson = false;
                json.Append("{\"command\":\"").Append(JsonOut.Escape(name)).Append('"');
                json.Append(",\"description\":\"").Append(JsonOut.Escape(spec.Description)).Append('"');
                if (spec.Value != null)
                {
                    json.Append(",\"value\":{")
                        .Append("\"type\":\"int32\",")
                        .Append("\"min\":").Append(spec.Value.Min).Append(',')
                        .Append("\"max\":").Append(spec.Value.Max).Append(',')
                        .Append("\"default\":").Append(spec.Value.DefaultValue).Append(',')
                        .Append("\"name\":\"").Append(JsonOut.Escape(spec.Value.Name)).Append('"')
                        .Append('}');
                }
                else
                {
                    json.Append(",\"value\":null");
                }
                json.Append('}');
            }
            json.Append("]}");

            JsonOut.Emit("Commands", "COMMANDS: " + text, data: json.ToString());
        }
    }

    internal sealed record ValueSpec(string Name, int Min, int Max, int DefaultValue);

    internal sealed record CommandSpec(
        Action<TrainerSession, int?> Handler,
        string Description,
        ValueSpec? Value);

    /// <summary>
    /// Stan bieżącej sesji trenera (per podpięcie do procesu gry). Rejestr komend jest
    /// statyczny - te same specy dla hotkeyow i stdin (sidecar Tauri).
    /// </summary>
    internal sealed class TrainerSession
    {
        public GameMemory Memory { get; }
        public XpPatch XpPatch { get; }
        public ActiveHeroTracker Tracker { get; }
        public FreezeManager Freeze { get; }

        // Zakresy zaczerpniete z Program.* via odbicie nie sa AOT-friendly,
        // dlatego stale powtorzone tu jako literaly - musza pasowac do Program.cs.
        private const int MinStat = 0;
        private const int MaxStat = 999;
        private const int DefaultStat = 99;
        private const int XpDefault = 1_000_000;
        private const int XpMin = -2_000_000_000;
        private const int XpMax =  2_000_000_000;

        private static readonly Dictionary<string, CommandSpec> Specs =
            new Dictionary<string, CommandSpec>(StringComparer.OrdinalIgnoreCase)
            {
                ["RefillMovement"]     = new(Program.DoRefillMovement,    "Napelnia punkty ruchu aktywnego bohatera do maksimum.", null),
                ["XpPatchToggle"]      = new(Program.DoXpPatchToggle,     "Toggle: nastepne zdobycie XP otrzyma bonus (+2 000 000 000).", null),
                ["TrackerToggle"]      = new(Program.DoTrackerToggle,     "Toggle trackera aktywnego bohatera (warunek dla pozostalych akcji).", null),
                ["ShowSnapshot"]       = new(Program.DoShowSnapshot,      "Emituje HeroSnapshot z aktualnymi statystykami bohatera.", null),
                ["SetAttack"]          = new(Program.DoSetAttack,         "Ustawia atak bohatera.",        new ValueSpec("attack",      MinStat, MaxStat, DefaultStat)),
                ["SetDefense"]         = new(Program.DoSetDefense,        "Ustawia obrone bohatera.",      new ValueSpec("defense",     MinStat, MaxStat, DefaultStat)),
                ["SetSpellPower"]      = new(Program.DoSetSpellPower,     "Ustawia spell power bohatera.", new ValueSpec("spellPower",  MinStat, MaxStat, DefaultStat)),
                ["SetKnowledge"]       = new(Program.DoSetKnowledge,      "Ustawia wiedze bohatera.",      new ValueSpec("knowledge",   MinStat, MaxStat, DefaultStat)),
                ["RefillMana"]         = new(Program.DoRefillMana,        "Napelnia mane do maksimum.", null),
                ["ToggleFreezeAttack"] = new(Program.DoToggleFreezeAttack,"Toggle zamrazania ataku na podanej wartosci (lub default).", new ValueSpec("freezeAttack", MinStat, MaxStat, DefaultStat)),
                ["AddXp"]              = new(Program.DoAddXp,             "Dodaje delta XP do biezacego XP bohatera (moze byc ujemna).", new ValueSpec("xpDelta", XpMin, XpMax, XpDefault)),
                ["DumpHero"]           = new(Program.DoDumpHero,          "Emituje hex dump struktury bohatera (384 bajty).", null),
                ["SetMorale"]          = new(Program.DoSetMorale,         "Ustawia bonus morale bohatera (slot A).", new ValueSpec("morale", MinStat, MaxStat, DefaultStat)),
                ["SetLuck"]            = new(Program.DoSetLuck,           "Ustawia bonus luck bohatera (slot A).",   new ValueSpec("luck",   MinStat, MaxStat, DefaultStat)),
            };

        public static IReadOnlyDictionary<string, CommandSpec> AllSpecs => Specs;

        public static CommandSpec? FindSpec(string command) =>
            Specs.TryGetValue(command, out CommandSpec? spec) ? spec : null;

        public TrainerSession(GameMemory memory, XpPatch xpPatch,
                              ActiveHeroTracker tracker, FreezeManager freeze)
        {
            Memory = memory;
            XpPatch = xpPatch;
            Tracker = tracker;
            Freeze = freeze;
        }
    }

    /// <summary>
    /// Pisanie ustrukturyzowanych eventow JSON Lines na stdout - sidecar Tauri parsuje
    /// linia po linii. Pelne JSON-y skladane recznie (zero zaleznosci AOT na serializerze).
    /// </summary>
    internal static class JsonOut
    {
        public static void Emit(string type, string message, bool? enabled = null, string? data = null)
        {
            var sb = new StringBuilder(128);
            sb.Append("{\"type\":\"").Append(Escape(type)).Append('"');
            if (enabled.HasValue)
                sb.Append(",\"enabled\":").Append(enabled.Value ? "true" : "false");
            sb.Append(",\"message\":\"").Append(Escape(message)).Append('"');
            if (data != null)
                sb.Append(",\"data\":").Append(data);
            sb.Append('}');

            // Console.Out jest synchronizowany na poziomie pojedynczego WriteLine - bez extra locka.
            Console.Out.WriteLine(sb.ToString());
            Console.Out.Flush();
        }

        public static string Escape(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"':  sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n");  break;
                    case '\r': sb.Append("\\r");  break;
                    case '\t': sb.Append("\\t");  break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("X4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
