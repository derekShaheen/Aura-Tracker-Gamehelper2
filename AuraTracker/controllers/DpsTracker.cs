using System;
using System.Collections.Generic;
using GameHelper.RemoteObjects.Components;

namespace AuraTracker.controllers;

internal sealed class DpsTracker
{
    private sealed class DpsState
    {
        public int LastPool;
        public long LastTicks;
        public float Ema;
    }

    private readonly Dictionary<uint, DpsState> states = new();

    public void Reset()
    {
        states.Clear();
    }

    public float Update(uint entityId, Life life, float smoothingSeconds)
    {
        int hpCur = Math.Max(life.Health.Current, 0);
        int esCur = Math.Max(life.EnergyShield.Current, 0);
        int pool = hpCur + esCur;

        long nowTicks = DateTime.UtcNow.Ticks;

        if (!states.TryGetValue(entityId, out var state))
        {
            state = new DpsState
            {
                LastPool = pool,
                LastTicks = nowTicks,
                Ema = 0f
            };
            states[entityId] = state;
            return 0f;
        }

        float dt = MathF.Max(0f, (nowTicks - state.LastTicks) / 10_000_000f);
        state.LastTicks = nowTicks;

        if (dt > 0f)
        {
            int delta = state.LastPool - pool;
            state.LastPool = pool;

            float sample = delta > 0 ? delta / dt : 0f;
            float tau = MathF.Max(0.1f, smoothingSeconds);
            float alpha = 1f - MathF.Exp(-dt / tau);
            float target = sample > 0 ? sample : 0f;
            state.Ema = state.Ema + alpha * (target - state.Ema);
        }

        states[entityId] = state;
        return state.Ema;
    }
}
