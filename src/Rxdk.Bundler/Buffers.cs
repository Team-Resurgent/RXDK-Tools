// Port of the XDK bundler's non-texture resource types: CVertexBuffer (vb.cpp),
// CIndexBuffer (indexbuffer.cpp), and CUserData (userdata.cpp). These are pure
// data — no pixel processing — so the ports are direct.

using System.Text;

namespace Rxdk.Bundler;

// Vertex attribute formats (vb.h XD3DVSDT_*).
internal static class Vsdt
{
    public const uint FLOAT1 = 0x00, FLOAT2 = 0x01, FLOAT3 = 0x02, FLOAT4 = 0x03;
    public const uint D3DCOLOR = 0x04, UNUSED = 0x05, SHORT2 = 0x06, SHORT4 = 0x07;
    public const uint NORMSHORT1 = 0x08, NORMSHORT2 = 0x09, NORMSHORT3 = 0x0A, NORMSHORT4 = 0x0B;
    public const uint NORMPACKED3 = 0x0C, SHORT1 = 0x0D, SHORT3 = 0x0E;
    public const uint PBYTE1 = 0x0F, PBYTE2 = 0x10, PBYTE3 = 0x11, PBYTE4 = 0x12, FLOAT2H = 0x13;

    // (inputs, bytesout) per format, indexed by the XD3DVSDT value.
    public static readonly (uint inputs, uint bytesout)[] Info =
    {
        (1, 4), (2, 8), (3, 12), (4, 16), // FLOAT1..4
        (4, 4),                            // D3DCOLOR
        (0, 0),                            // UNUSED
        (2, 4), (4, 8),                    // SHORT2, SHORT4
        (1, 2), (2, 4), (3, 6), (4, 8),    // NORMSHORT1..4
        (3, 4),                            // NORMPACKED3
        (1, 2), (3, 6),                    // SHORT1, SHORT3
        (1, 1), (2, 2), (3, 3), (4, 4),    // PBYTE1..4
        (3, 12),                           // FLOAT2H
    };

    public static readonly Dictionary<string, uint> ByName = new(StringComparer.Ordinal)
    {
        ["D3DVSDT_FLOAT1"] = FLOAT1, ["FLOAT1"] = FLOAT1,
        ["D3DVSDT_FLOAT2"] = FLOAT2, ["FLOAT2"] = FLOAT2,
        ["D3DVSDT_FLOAT3"] = FLOAT3, ["FLOAT3"] = FLOAT3,
        ["D3DVSDT_FLOAT4"] = FLOAT4, ["FLOAT4"] = FLOAT4,
        ["D3DVSDT_D3DCOLOR"] = D3DCOLOR, ["D3DCOLOR"] = D3DCOLOR,
        ["D3DVSDT_SHORT2"] = SHORT2, ["SHORT2"] = SHORT2,
        ["D3DVSDT_SHORT4"] = SHORT4, ["SHORT4"] = SHORT4,
        ["D3DVSDT_NORMSHORT1"] = NORMSHORT1, ["NORMSHORT1"] = NORMSHORT1,
        ["D3DVSDT_NORMSHORT2"] = NORMSHORT2, ["NORMSHORT2"] = NORMSHORT2,
        ["D3DVSDT_NORMSHORT3"] = NORMSHORT3, ["NORMSHORT3"] = NORMSHORT3,
        ["D3DVSDT_NORMSHORT4"] = NORMSHORT4, ["NORMSHORT4"] = NORMSHORT4,
        ["D3DVSDT_NORMPACKED3"] = NORMPACKED3, ["NORMPACKED3"] = NORMPACKED3,
        ["D3DVSDT_SHORT1"] = SHORT1, ["SHORT1"] = SHORT1,
        ["D3DVSDT_SHORT3"] = SHORT3, ["SHORT3"] = SHORT3,
        ["D3DVSDT_PBYTE1"] = PBYTE1, ["PBYTE1"] = PBYTE1,
        ["D3DVSDT_PBYTE2"] = PBYTE2, ["PBYTE2"] = PBYTE2,
        ["D3DVSDT_PBYTE3"] = PBYTE3, ["PBYTE3"] = PBYTE3,
        ["D3DVSDT_PBYTE4"] = PBYTE4, ["PBYTE4"] = PBYTE4,
        ["D3DVSDT_FLOAT2H"] = FLOAT2H, ["FLOAT2H"] = FLOAT2H,
    };
}

internal sealed class VertexBuffer : IResource
{
    private readonly Bundler _b;
    private readonly List<double> _data = new();
    private readonly List<uint> _format = new();
    private bool _raw;
    private byte[] _rawData = Array.Empty<byte>();

    public VertexBuffer(Bundler b) => _b = b;

    public void AddVertexData(double val) => _data.Add(val);
    public void AddVertexFormat(uint fmt) => _format.Add(fmt);

    public void LoadVertexDataFromFile(string path)
    {
        _rawData = File.ReadAllBytes(path);
        _raw = true;
    }

    public void SaveToBundle(out uint cbHeader, out uint cbData)
    {
        if (_format.Count == 0) throw new BundlerException("No attribute formats specified");
        if ((_raw ? _rawData.Length : _data.Count) == 0) throw new BundlerException("No attribute data specified");

        _b.PadToAlignment(4); // D3DVERTEXBUFFER_ALIGNMENT

        // D3DVertexBuffer { Common, Data, Lock }. vb.cpp ORs D3DCOMMON_VIDEOMEMORY.
        uint common = XboxFormats.D3DCOMMON_TYPE_VERTEXBUFFER | 0x00800000u | 1u;
        Span<byte> hdr = stackalloc byte[12];
        BitConverter.TryWriteBytes(hdr.Slice(0, 4), common);
        BitConverter.TryWriteBytes(hdr.Slice(4, 4), _b.CbData);
        BitConverter.TryWriteBytes(hdr.Slice(8, 4), 0u);
        _b.WriteHeader(hdr);
        cbHeader = 12;

        cbData = SaveVertexBufferData();
    }

    private uint SaveVertexBufferData()
    {
        uint bytesPerVertex = 0, inputsPerVertex = 0;
        foreach (var f in _format)
        {
            bytesPerVertex += Vsdt.Info[f].bytesout;
            inputsPerVertex += Vsdt.Info[f].inputs;
        }

        if (_raw)
        {
            _b.WriteData(_rawData);
            return (uint)_rawData.Length;
        }

        uint vertices = inputsPerVertex == 0 ? 0 : (uint)_data.Count / inputsPerVertex;
        uint written = 0;
        int c = 0;
        var buf = new List<byte>();

        void F(double d) { buf.AddRange(BitConverter.GetBytes((float)d)); }
        void S(double d) { buf.AddRange(BitConverter.GetBytes((short)d)); }
        void By(double d) { buf.Add((byte)d); }

        for (uint vtx = 0; vtx < vertices; vtx++)
        {
            foreach (var fmt in _format)
            {
                buf.Clear();
                switch (fmt)
                {
                    case Vsdt.FLOAT1: F(_data[c++]); break;
                    case Vsdt.FLOAT2: F(_data[c++]); F(_data[c++]); break;
                    case Vsdt.FLOAT2H:
                    case Vsdt.FLOAT3: F(_data[c++]); F(_data[c++]); F(_data[c++]); break;
                    case Vsdt.FLOAT4: F(_data[c++]); F(_data[c++]); F(_data[c++]); F(_data[c++]); break;
                    case Vsdt.D3DCOLOR: By(_data[c++] * 255.0); By(_data[c++] * 255.0); By(_data[c++] * 255.0); By(_data[c++] * 255.0); break;
                    case Vsdt.SHORT1: S(_data[c++]); break;
                    case Vsdt.SHORT2: S(_data[c++]); S(_data[c++]); break;
                    case Vsdt.SHORT3: S(_data[c++]); S(_data[c++]); S(_data[c++]); break;
                    case Vsdt.SHORT4: S(_data[c++]); S(_data[c++]); S(_data[c++]); S(_data[c++]); break;
                    case Vsdt.NORMSHORT1: S(_data[c++] * 32767.0); break;
                    case Vsdt.NORMSHORT2: S(_data[c++] * 32767.0); S(_data[c++] * 32767.0); break;
                    case Vsdt.NORMSHORT3: S(_data[c++] * 32767.0); S(_data[c++] * 32767.0); S(_data[c++] * 32767.0); break;
                    case Vsdt.NORMSHORT4: S(_data[c++] * 32767.0); S(_data[c++] * 32767.0); S(_data[c++] * 32767.0); S(_data[c++] * 32767.0); break;
                    case Vsdt.NORMPACKED3:
                    {
                        uint p0 = ((uint)(_data[c++] * 1023.0) & 0x7ff) << 0;
                        uint p1 = ((uint)(_data[c++] * 1023.0) & 0x7ff) << 11;
                        uint p2 = ((uint)(_data[c++] * 511.0) & 0x3ff) << 22;
                        buf.AddRange(BitConverter.GetBytes(p0 | p1 | p2));
                        break;
                    }
                    case Vsdt.PBYTE1: By(_data[c++] * 255.0); break;
                    case Vsdt.PBYTE2: By(_data[c++] * 255.0); By(_data[c++] * 255.0); break;
                    case Vsdt.PBYTE3: By(_data[c++] * 255.0); By(_data[c++] * 255.0); By(_data[c++] * 255.0); break;
                    case Vsdt.PBYTE4: By(_data[c++] * 255.0); By(_data[c++] * 255.0); By(_data[c++] * 255.0); By(_data[c++] * 255.0); break;
                }
                _b.WriteData(buf.ToArray());
                written += (uint)buf.Count;
            }
        }
        return written;
    }
}

internal sealed class IndexBuffer : IResource
{
    private readonly Bundler _b;
    private readonly List<ushort> _indices = new();

    public IndexBuffer(Bundler b) => _b = b;

    public void AddIndex(ushort v) => _indices.Add(v);

    public void LoadIndicesFromFile(string path)
    {
        byte[] d = File.ReadAllBytes(path);
        _indices.Clear();
        for (int i = 0; i + 1 < d.Length; i += 2)
            _indices.Add(BitConverter.ToUInt16(d, i));
    }

    public void SaveToBundle(out uint cbHeader, out uint cbData)
    {
        if (_indices.Count == 0) throw new BundlerException("No indices specified");

        _b.PadToAlignment(4); // D3DINDEXBUFFER_ALIGNMENT

        // D3DIndexBuffer { Common, Data, Lock }. No D3DCOMMON_VIDEOMEMORY (per indexbuffer.cpp).
        uint common = XboxFormats.D3DCOMMON_TYPE_INDEXBUFFER | 1u;
        Span<byte> hdr = stackalloc byte[12];
        BitConverter.TryWriteBytes(hdr.Slice(0, 4), common);
        BitConverter.TryWriteBytes(hdr.Slice(4, 4), _b.CbData);
        BitConverter.TryWriteBytes(hdr.Slice(8, 4), 0u);
        _b.WriteHeader(hdr);
        cbHeader = 12;

        var data = new byte[_indices.Count * 2];
        for (int i = 0; i < _indices.Count; i++)
            BitConverter.TryWriteBytes(data.AsSpan(i * 2, 2), _indices[i]);
        _b.WriteData(data);
        cbData = (uint)data.Length;
    }
}

internal sealed class UserData : IResource
{
    private readonly Bundler _b;
    public string Source = "";

    public UserData(Bundler b) => _b = b;

    public void SaveToBundle(out uint cbHeader, out uint cbData)
    {
        if (string.IsNullOrEmpty(Source)) throw new BundlerException("No source specified");

        string path = BundlerPath.ResolveAgainst(_b.PathPrefix, Source);
        byte[] file = File.ReadAllBytes(path);

        _b.PadToAlignment(4); // USERDATA_ALIGNMENT (no data actually written)

        // The userdata blob lives entirely in the header: [0x80000000, size, bytes...].
        var blob = new byte[8 + file.Length];
        BitConverter.TryWriteBytes(blob.AsSpan(0, 4), 0x80000000u);
        BitConverter.TryWriteBytes(blob.AsSpan(4, 4), (uint)file.Length);
        Array.Copy(file, 0, blob, 8, file.Length);
        _b.WriteHeader(blob);

        cbHeader = (uint)blob.Length;
        cbData = 0;
    }
}
