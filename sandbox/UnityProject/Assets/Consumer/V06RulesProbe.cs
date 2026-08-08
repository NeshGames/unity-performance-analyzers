using System;
using System.Collections.Generic;
using UnityEngine;

// Deliberate violations of the v0.6 allocation rules, plus the two boundaries that were
// redrawn for them. Each line notes the diagnostic the analyzer must report when this
// assembly compiles — and the lines marked "must NOT report" are the point of the file:
// the dedup between these rules is behaviour, and a real Unity compilation is where it
// gets checked against the real UnityEngine assemblies rather than the test stubs.

public struct ProbeGridCoord
{
    public int X;
    public int Y;
}

public readonly struct ProbeEquatableCoord : IEquatable<ProbeEquatableCoord>
{
    public readonly int X;

    public ProbeEquatableCoord(int x) => X = x;

    public bool Equals(ProbeEquatableCoord other) => X == other.X;

    public override bool Equals(object obj) => obj is ProbeEquatableCoord other && Equals(other);

    public override int GetHashCode() => X;
}

public enum ProbeMode
{
    Idle,
    Running,
}

public class V06RulesProbe : MonoBehaviour
{
    // UPA0028: struct key without IEquatable<T> or a GetHashCode override.
    readonly Dictionary<ProbeGridCoord, int> _tiles = new Dictionary<ProbeGridCoord, int>();

    // Must NOT report: this one implements both members.
    readonly Dictionary<ProbeEquatableCoord, int> _ok = new Dictionary<ProbeEquatableCoord, int>();

    // Must NOT report UPA0028: enums are measured as non-boxing.
    readonly Dictionary<ProbeMode, int> _byMode = new Dictionary<ProbeMode, int>();

    readonly List<int> _target = new List<int>();
    readonly List<int> _source = new List<int>();

    float _a, _b, _c;
    string _label = "alpha,beta";
    Animator _animator;
    Texture2D _texture;

    void Update()
    {
        var largest = Mathf.Max(_a, _b, _c);                        // UPA0027 (params float[])
        _ = largest;

        Debug.LogFormat("{0}", _tiles.Count);                       // UPA0027 boxing variant + UPA0005
                                                                    // must NOT also report UPA0006

        var parts = _label.Split(',');                              // UPA0030
        var head = _label.Substring(0, 3);                          // UPA0030
        var names = Enum.GetNames(typeof(ProbeMode));               // UPA0030
        _ = parts;
        _ = head;
        _ = names;

        var clips = _animator.GetCurrentAnimatorClipInfo(0);        // UPA0018 (not UPA0030)
        var pixels = _texture.GetPixels();                          // UPA0018 (not UPA0030)
        _ = clips;
        _ = pixels;

        // Must NOT report: these are the replacements the messages name.
        var raw = _texture.GetRawTextureData<byte>();
        _ = raw.Length;
    }

    void Awake()
    {
        // UPA0029: not hot-path scoped, so it reports here too.
        foreach (var value in _source)
        {
            _target.Add(value);
        }

        // Must NOT report UPA0029: a HashSet has no AddRange taking IEnumerable.
        var seen = new HashSet<int>();
        foreach (var value in _source)
        {
            seen.Add(value);
        }

        _ = _ok.Count;
        _ = _byMode.Count;
    }
}
