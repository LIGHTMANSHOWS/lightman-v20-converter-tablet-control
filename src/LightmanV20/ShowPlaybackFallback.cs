namespace LightmanZapravka3D;

/// <summary>
/// Decide cuándo una sesión lanzada por V20 ya dejó de reproducirse. La clase no
/// ejecuta la conmutación; sólo entrega un pulso único para que MainForm vuelva a
/// Resolume en el hilo de interfaz.
/// </summary>
internal sealed class ShowPlaybackFallback
{
    private static readonly TimeSpan StartGrace = TimeSpan.FromSeconds(6);
    private readonly object _gate = new();
    private bool _armed;
    private bool _activeObserved;
    private bool _pausedObserved;
    private DateTime _armedAtUtc;
    private int _idleConfirmations;
    private int _disconnectConfirmations;
    private long _generation;

    public bool Armed
    {
        get { lock (_gate) return _armed; }
    }

    public void Arm(DateTime utcNow)
    {
        lock (_gate)
        {
            unchecked { _generation++; }
            _armed = true;
            _activeObserved = false;
            _pausedObserved = false;
            _armedAtUtc = utcNow;
            _idleConfirmations = 0;
            _disconnectConfirmations = 0;
        }
    }

    public void Disarm()
    {
        lock (_gate)
        {
            unchecked { _generation++; }
            ResetSession();
        }
    }

    public bool Observe(DateTime utcNow, XScheduleStatus status, bool xlightsSelected) =>
        Observe(utcNow, status, xlightsSelected, xlightsSignalActive: false, out _);

    public bool Observe(DateTime utcNow, XScheduleStatus status, bool xlightsSelected, out long generation)
        => Observe(utcNow, status, xlightsSelected, xlightsSignalActive: false, out generation);

    public bool Observe(DateTime utcNow, XScheduleStatus status, bool xlightsSelected,
        bool xlightsSignalActive, out long generation)
    {
        lock (_gate)
        {
            generation = -1;
            if (!_armed) return false;
            if (!xlightsSelected)
            {
                unchecked { _generation++; }
                ResetSession();
                return false;
            }

            bool graceElapsed = utcNow - _armedAtUtc >= StartGrace;
            if (!status.Connected)
            {
                _idleConfirmations = 0;
                // Pausa es una decisión explícita del operador, no el final del
                // show. Ni la pérdida de HTTP ni el silencio Art-Net la cancelan.
                if (_pausedObserved)
                {
                    _disconnectConfirmations = 0;
                    return false;
                }
                // Una caída del API no demuestra que el show haya terminado. Si
                // xLights todavía emite Art-Net, conservamos la fuente y esperamos
                // a recuperar el estado HTTP.
                if (xlightsSignalActive)
                {
                    _disconnectConfirmations = 0;
                    return false;
                }
                if (!_activeObserved && !graceElapsed)
                {
                    _disconnectConfirmations = 0;
                    return false;
                }

                // Varias fallas consecutivas distinguen una caída real de un
                // timeout aislado del servidor web de xSchedule.
                if (++_disconnectConfirmations < 3) return false;
                generation = _generation;
                ResetSession();
                return true;
            }

            _disconnectConfirmations = 0;
            string playback = status.Status.Trim();
            if (playback.Equals("Playing", StringComparison.OrdinalIgnoreCase))
            {
                _activeObserved = true;
                _pausedObserved = false;
                _idleConfirmations = 0;
                return false;
            }
            if (playback.Equals("Paused", StringComparison.OrdinalIgnoreCase))
            {
                _activeObserved = true;
                _pausedObserved = true;
                _idleConfirmations = 0;
                return false;
            }

            if (!playback.Equals("Idle", StringComparison.OrdinalIgnoreCase))
            {
                _idleConfirmations = 0;
                return false;
            }

            // El breve Idle que puede existir entre Play y Playing no es un final.
            // Si nunca arrancó, la gracia evita dejar V20 indefinidamente en una
            // fuente xLights sin reproducción.
            if (!_activeObserved && !graceElapsed)
            {
                _idleConfirmations = 0;
                return false;
            }

            if (++_idleConfirmations < 2) return false;
            generation = _generation;
            ResetSession();
            return true;
        }
    }

    public bool IsCurrent(long generation)
    {
        lock (_gate) return _generation == generation;
    }

    private void ResetSession()
    {
        _armed = false;
        _activeObserved = false;
        _pausedObserved = false;
        _armedAtUtc = default;
        _idleConfirmations = 0;
        _disconnectConfirmations = 0;
    }
}
