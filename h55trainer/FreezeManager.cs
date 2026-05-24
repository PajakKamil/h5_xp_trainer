using System;
using System.Collections.Generic;
using System.Threading;

namespace Heroes5Trainer
{
    /// <summary>
    /// W tle co kilka milisekund nadpisuje wybrane pola aktywnego bohatera ich
    /// "zamrozonymi" wartosciami. Pozwala utrzymac statystyke na zadanej wartosci
    /// nawet wtedy, gdy gra sama probuje ja zmienic (np. level up modyfikuje atak).
    /// </summary>
    internal sealed class FreezeManager : IDisposable
    {
        private const int LoopIntervalMs = 50;

        private readonly GameMemory memory;
        private readonly ActiveHeroTracker tracker;
        private readonly Dictionary<int, int> frozenByOffset = new();
        private readonly object sync = new();
        private Thread? thread;
        private volatile bool running;

        public FreezeManager(GameMemory memory, ActiveHeroTracker tracker)
        {
            this.memory = memory;
            this.tracker = tracker;
        }

        public void Start()
        {
            if (thread != null)
                return;

            running = true;
            thread = new Thread(Loop) { IsBackground = true, Name = "FreezeManager" };
            thread.Start();
        }

        public void Stop()
        {
            running = false;
            thread?.Join();
            thread = null;
        }

        /// <summary>
        /// Przelacza zamrazanie pola. Zwraca true, jesli pole zostalo wlasnie ZAMROZONE,
        /// false jesli zostalo ODMROZONE.
        /// </summary>
        public bool Toggle(int fieldOffset, int value)
        {
            lock (sync)
            {
                if (frozenByOffset.Remove(fieldOffset))
                    return false;

                frozenByOffset[fieldOffset] = value;
                return true;
            }
        }

        public bool IsFrozen(int fieldOffset)
        {
            lock (sync)
                return frozenByOffset.ContainsKey(fieldOffset);
        }

        private void Loop()
        {
            while (running)
            {
                try
                {
                    if (tracker.TryGetHeroPtr(out nint heroPtr))
                    {
                        lock (sync)
                        {
                            foreach (KeyValuePair<int, int> kv in frozenByOffset)
                                HeroStats.WriteInt32(memory, heroPtr, kv.Key, kv.Value);
                        }
                    }
                }
                catch
                {
                    // Gra moze zniknac w trakcie zapisu - ignorujemy, zewnetrzna petla
                    // i tak za chwile zatrzyma FreezeManager.
                }

                Thread.Sleep(LoopIntervalMs);
            }
        }

        public void Dispose() => Stop();
    }
}
