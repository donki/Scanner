using SocScanner.Imaging;
using SocScanner.Scanning;

namespace SocScanner.Session;

/// <summary>
/// Las hojas del modo «una a una» hasta que se guardan: se añaden al escanear y se pueden girar,
/// quitar y cambiar de orden. Al guardar bien, se vacía.
/// </summary>
public sealed class ScanDocument
{
    private readonly List<ScannedPage> _pages = [];

    public event Action? Changed;

    public IReadOnlyList<ScannedPage> Pages => _pages;

    public int Count => _pages.Count;

    public bool IsEmpty => _pages.Count == 0;

    /// <summary>¿Hay hojas sin guardar? (Para avisar antes de vaciar o de salir.)</summary>
    public bool HasUnsaved => _pages.Count > 0;

    public void Add(IEnumerable<ScannedPage> pages)
    {
        var before = _pages.Count;
        _pages.AddRange(pages);
        if (_pages.Count != before)
            Changed?.Invoke();
    }

    public void RemoveAt(int index)
    {
        if (index < 0 || index >= _pages.Count)
            return;
        _pages.RemoveAt(index);
        Changed?.Invoke();
    }

    /// <summary>Gira una hoja de 90 en 90 grados (positivo: a la derecha).</summary>
    public void Rotate(int index, int degrees)
    {
        if (index < 0 || index >= _pages.Count)
            return;
        var p = _pages[index];
        _pages[index] = p with { Image = ImageOps.Rotate(p.Image, degrees) };
        Changed?.Invoke();
    }

    /// <summary>Mueve una hoja a otra posición (para ordenar). Devuelve dónde ha quedado.</summary>
    public int Move(int index, int delta)
    {
        if (index < 0 || index >= _pages.Count)
            return index;
        var target = Math.Clamp(index + delta, 0, _pages.Count - 1);
        if (target == index)
            return index;
        var p = _pages[index];
        _pages.RemoveAt(index);
        _pages.Insert(target, p);
        Changed?.Invoke();
        return target;
    }

    public void Clear()
    {
        if (_pages.Count == 0)
            return;
        _pages.Clear();
        Changed?.Invoke();
    }
}
