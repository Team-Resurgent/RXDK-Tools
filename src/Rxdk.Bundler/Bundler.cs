// Port of the XDK bundler's CBundler (bundler.cpp): drives the .rdf parse, packs
// resources into an Xbox Packed Resource (.xpr) file, and emits the companion
// Resource.h. The .xpr layout (XPR_HEADER {magic,totalSize,headerSize}, optional
// XPR1 offsets table, resource header structs terminated by 0xffffffff, 0xDEAD
// fill to a 2048-byte sector, then the data section) matches what the runtime
// CXBPackedResource loader expects.

using System.Globalization;
using System.Text;

namespace Rxdk.Bundler;

internal enum XprVersion { Xpr0 = 0, Xpr1 = 1 }

internal sealed class ResourceRecord
{
    public string Identifier = "";
    public string Name = "";
    public uint Offset;
}

/// <summary>A resource whose header/data has been appended to the bundle.</summary>
internal interface IResource
{
    /// <summary>Appends the resource header + data; returns header/data byte counts.</summary>
    void SaveToBundle(out uint cbHeader, out uint cbData);
}

internal sealed class Bundler
{
    public const uint XPR0_MAGIC = 0x30525058; // 'XPR0'
    public const uint XPR1_MAGIC = 0x31525058; // 'XPR1'

    public XprVersion OutputVersion = XprVersion.Xpr0;

    // Growable header/data buffers. WriteHeader/WriteData/PadToAlignment append;
    // the committed length is simply the buffer count (see notes in the port).
    private readonly List<byte> _header = new();
    private readonly List<byte> _data = new();

    public uint CbHeader => (uint)_header.Count;
    public uint CbData => (uint)_data.Count;

    private readonly List<ResourceRecord> _resources = new();

    // Resolved output paths + the .rdf's directory (m_strPath).
    public string RdfPath = "";
    private string _pathPrefix = "";
    public string PathPrefix => _pathPrefix;
    public string XprPath = "";
    public string HdrPath = "";
    public string ErrPath = "";
    public string Prefix = "";

    private bool _explicitXpr, _explicitHdr, _explicitErr, _explicitPrefix;
    public bool Quiet;
    public bool SingleTexture;

    // An AlphaSource merged into an A8R8G8B8 surface takes its alpha from the
    // loaded pixel's BLUE channel (byte 0) -- matching the leaked bundler.cpp
    // (dwAlpha = (*pAlphaBits) << 24), the 5849 bundler.exe (verified: it merges
    // real, non-zero alpha for Fire/PlayField/etc.), and skinbld.exe. An earlier
    // change used byte 3 (the X channel, zero for a 24-bit BMP) to match some
    // stale shipped .xpr, but that zeroed every alpha mask and made effects like
    // Fire's flames render fully transparent. Blue is the correct, faithful merge.
    public bool AlphaFromBlueChannel = true;

    // bundler.exe pins the x87 to _PC_24 (float precision); skinbld.exe runs at the
    // default 53-bit. Off = bundler's float-precision F2I; skinbld sets it on.
    public bool FullPrecisionF2I;

    private RdfReader _reader = null!;

    // Offsets-table bookkeeping (XPR1). Exposed so resource handlers can add the
    // base offset to their recorded offsets when emitting Resource.h.
    public uint BaseResourceOffset { get; private set; }

    // --- buffer writers (bundler.cpp WriteHeader/WriteData/PadToAlignment) ----
    public void WriteHeader(ReadOnlySpan<byte> bytes) => _header.AddRange(bytes.ToArray());
    public void WriteData(ReadOnlySpan<byte> bytes) => _data.AddRange(bytes.ToArray());

    public void PadToAlignment(uint align)
    {
        uint fill = 0;
        if (_data.Count % align != 0)
            fill = align - ((uint)_data.Count % align);
        for (uint i = 0; i < fill; i++)
            _data.Add(0xAD); // memset(...,0xDEAD,...) -> byte 0xAD
    }

    private bool IsExistingIdentifier(string id) => _resources.Exists(r => r.Identifier == id);

    // --- initialization / cli (bundler.cpp Initialize) ------------------------
    public void Initialize(string[] args)
    {
        bool haveRdf = false;

        for (int n = 0; n < args.Length; n++)
        {
            string a = args[n];
            if (a.Length > 0 && (a[0] == '/' || a[0] == '-'))
            {
                string opt = a.Substring(1);
                if (string.Equals(opt, "q", StringComparison.OrdinalIgnoreCase)) { Quiet = true; continue; }
                if (n + 1 == args.Length) throw new BundlerException("Missing argument for option");

                if (string.Equals(opt, "o", StringComparison.OrdinalIgnoreCase)) { XprPath = args[n + 1]; _explicitXpr = true; }
                else if (string.Equals(opt, "h", StringComparison.OrdinalIgnoreCase)) { HdrPath = args[n + 1]; _explicitHdr = true; }
                else if (string.Equals(opt, "p", StringComparison.OrdinalIgnoreCase)) { Prefix = args[n + 1]; _explicitPrefix = true; }
                else if (string.Equals(opt, "e", StringComparison.OrdinalIgnoreCase)) { ErrPath = args[n + 1]; _explicitErr = true; }
                else throw new BundlerException("Bad option: " + a);

                n++;
            }
            else
            {
                RdfPath = BundlerPath.ToNative(a);

                // Split base name / extension (last '.').
                int dot = RdfPath.LastIndexOf('.');
                string baseName = dot >= 0 ? RdfPath.Substring(0, dot) : RdfPath;
                string ext = dot >= 0 ? RdfPath.Substring(dot + 1) : "";

                if (ext.Equals("bmp", StringComparison.OrdinalIgnoreCase) ||
                    ext.Equals("jpg", StringComparison.OrdinalIgnoreCase) ||
                    ext.Equals("tga", StringComparison.OrdinalIgnoreCase) ||
                    ext.Equals("dds", StringComparison.OrdinalIgnoreCase) ||
                    ext.Equals("png", StringComparison.OrdinalIgnoreCase))
                {
                    SingleTexture = true;
                }

                // The input's base name only supplies defaults; an explicit -o/-h/-e
                // wins, and so does out_packedresource in the .rdf.
                if (!_explicitXpr) XprPath = baseName + ".xpr";
                if (!_explicitHdr) HdrPath = baseName + ".h";
                if (!_explicitErr) ErrPath = baseName + ".err";

                // m_strPath = the input's directory (including trailing separator).
                // Source images resolve against it, so it must not follow -o.
                int slash = RdfPath.LastIndexOfAny(new[] { '\\', '/' });
                _pathPrefix = BundlerPath.ToNative(slash >= 0 ? RdfPath.Substring(0, slash + 1) : "");

                haveRdf = true;
            }
        }

        if (!haveRdf)
            throw new BundlerException("No .rdf input specified");
    }

    // --- process (bundler.cpp Process) ----------------------------------------
    public void Process()
    {
        // Set the codec's x87-precision emulation for this run. Reset every time so
        // a skinbld build and a plain bundler build never leak precision into each
        // other when both run in one process.
        CXD3DXCodec.FullPrecision = FullPrecisionF2I;

        if (SingleTexture)
        {
            var t = new Texture2D(this);
            int slash = RdfPath.LastIndexOfAny(new[] { '\\', '/' });
            t.Source = slash >= 0 ? RdfPath.Substring(slash + 1) : RdfPath;

            uint offset = CbHeader;
            t.SaveToBundle(out uint cbH, out _);
            _resources.Add(new ResourceRecord { Identifier = "", Name = "", Offset = offset });

            HandleEof();
            return;
        }

        byte[] rdf = File.ReadAllBytes(RdfPath);
        _reader = new RdfReader(rdf);

        while (true)
        {
            var tok = _reader.GetNextToken();
            if (Dispatch(tok.Id))
                break; // S_FALSE from EOF handler
        }
    }

    // Returns true when processing should stop (EOF handled).
    private bool Dispatch(uint id)
    {
        switch (id)
        {
            case Tok.EOF: HandleEof(); return true;
            case Tok.OUT_VERSION: HandleOutVersion(); return false;
            case Tok.OUT_PACKEDRESOURCE: HandleOutPackedResource(); return false;
            case Tok.OUT_HEADER: HandleOutHeader(); return false;
            case Tok.OUT_PREFIX: HandleOutPrefix(); return false;
            case Tok.OUT_ERROR: HandleOutError(); return false;
            case Tok.RESOURCE_TEXTURE: HandleTexture(); return false;
            case Tok.RESOURCE_CUBEMAP: HandleCubemap(); return false;
            case Tok.RESOURCE_VOLUMETEXTURE: HandleVolumeTexture(); return false;
            case Tok.RESOURCE_VERTEXBUFFER: HandleVertexBuffer(); return false;
            case Tok.RESOURCE_USERDATA: HandleUserData(); return false;
            case Tok.RESOURCE_INDEXBUFFER: HandleIndexBuffer(); return false;
            default: throw new BundlerException($"Unexpected top-level token 0x{id:X8}");
        }
    }

    // --- out_* handlers -------------------------------------------------------
    private void HandleOutVersion()
    {
        string v = _reader.GetNextTokenString(TokType.Any);
        if (v.Equals("XPR0", StringComparison.OrdinalIgnoreCase)) OutputVersion = XprVersion.Xpr0;
        else if (v.Equals("XPR1", StringComparison.OrdinalIgnoreCase)) OutputVersion = XprVersion.Xpr1;
        else OutputVersion = XprVersion.Xpr0;
    }

    private void HandleOutPackedResource()
    {
        string f = _reader.GetNextTokenString(TokType.Filename);
        if (_resources.Count > 0) return;
        if (!_explicitXpr) XprPath = BundlerPath.ResolveAgainst(_pathPrefix, f);
    }

    private void HandleOutHeader()
    {
        string f = _reader.GetNextTokenString(TokType.Filename);
        if (_resources.Count > 0) return;
        if (!_explicitHdr) HdrPath = BundlerPath.ResolveAgainst(_pathPrefix, f);
    }

    private void HandleOutPrefix()
    {
        string p = _reader.GetNextTokenString(TokType.Any);
        if (_resources.Count > 0) return;
        if (!_explicitPrefix) Prefix = p;
    }

    private void HandleOutError()
    {
        string f = _reader.GetNextTokenString(TokType.Filename);
        if (_resources.Count > 0) return;
        if (!_explicitErr) ErrPath = BundlerPath.ResolveAgainst(_pathPrefix, f);
    }

    // --- Texture (bundler.cpp HandleTextureToken) -----------------------------
    private void HandleTexture()
    {
        var tex = new Texture2D(this);
        var rec = new ResourceRecord();

        rec.Identifier = _reader.GetNextTokenString(TokType.Identifier);
        rec.Name = rec.Identifier;

        if (IsExistingIdentifier(rec.Identifier))
            throw new BundlerException($"Second usage of identifier <{rec.Identifier}>");

        var open = _reader.GetNextToken();
        if (open.Id != Tok.OPENBRACE)
            throw new BundlerException("Texture name should be followed by an open brace");

        bool done = false;
        while (!done)
        {
            var tok = _reader.GetNextToken();
            string val = "";
            if ((tok.Id & Tok.PROPERTY) != 0)
                val = _reader.GetNextTokenString(tok.PropType);

            switch (tok.Id)
            {
                case Tok.PROPERTY_NAME: rec.Name = val; break;
                case Tok.PROPERTY_TEXTURE_SOURCE: tex.Source = val; break;
                case Tok.PROPERTY_TEXTURE_ALPHASOURCE: tex.AlphaSource = val; break;
                case Tok.PROPERTY_TEXTURE_FILTER: tex.Filter = FilterFromString(val); break;
                case Tok.PROPERTY_TEXTURE_FORMAT: tex.FormatName = val; break;
                case Tok.PROPERTY_TEXTURE_WIDTH: tex.Width = ParseInt(val); break;
                case Tok.PROPERTY_TEXTURE_HEIGHT: tex.Height = ParseInt(val); break;
                case Tok.PROPERTY_TEXTURE_LEVELS: tex.Levels = ParseInt(val); break;
                case Tok.CLOSEBRACE: done = true; break;
                default: throw new BundlerException($"<{tok.Keyword}> is not a texture property.");
            }
        }

        uint offset = CbHeader;
        tex.SaveToBundle(out _, out uint cbData);
        rec.Offset = offset;
        _resources.Add(rec);

        if (!Quiet)
            Console.WriteLine($"Texture: Wrote {rec.Identifier} out in format {tex.FormatName} ({cbData} bytes)");
    }

    // HandleCubemapToken (bundler.cpp).
    private void HandleCubemap()
    {
        var rec = BeginResource();
        var cube = new Cubemap(this);
        bool done = false;

        while (!done)
        {
            var tok = _reader.GetNextToken();
            string val = "";
            if ((tok.Id & Tok.PROPERTY) != 0)
                val = _reader.GetNextTokenString(tok.PropType);

            switch (tok.Id)
            {
                case Tok.PROPERTY_NAME: rec.Name = val; break;
                case Tok.PROPERTY_CUBEMAP_SOURCE_XP: cube.SourceXP = val; break;
                case Tok.PROPERTY_CUBEMAP_SOURCE_XN: cube.SourceXN = val; break;
                case Tok.PROPERTY_CUBEMAP_SOURCE_YP: cube.SourceYP = val; break;
                case Tok.PROPERTY_CUBEMAP_SOURCE_YN: cube.SourceYN = val; break;
                case Tok.PROPERTY_CUBEMAP_SOURCE_ZP: cube.SourceZP = val; break;
                case Tok.PROPERTY_CUBEMAP_SOURCE_ZN: cube.SourceZN = val; break;
                case Tok.PROPERTY_CUBEMAP_ALPHASOURCE_XP: cube.AlphaXP = val; break;
                case Tok.PROPERTY_CUBEMAP_ALPHASOURCE_XN: cube.AlphaXN = val; break;
                case Tok.PROPERTY_CUBEMAP_ALPHASOURCE_YP: cube.AlphaYP = val; break;
                case Tok.PROPERTY_CUBEMAP_ALPHASOURCE_YN: cube.AlphaYN = val; break;
                case Tok.PROPERTY_CUBEMAP_ALPHASOURCE_ZP: cube.AlphaZP = val; break;
                case Tok.PROPERTY_CUBEMAP_ALPHASOURCE_ZN: cube.AlphaZN = val; break;
                case Tok.PROPERTY_TEXTURE_FILTER: cube.Filter = FilterFromString(val); break;
                case Tok.PROPERTY_TEXTURE_FORMAT: cube.FormatName = val; break;
                case Tok.PROPERTY_CUBEMAP_SIZE: cube.Size = ParseInt(val); break;
                case Tok.PROPERTY_TEXTURE_LEVELS: cube.Levels = ParseInt(val); break;
                case Tok.CLOSEBRACE: done = true; break;
                default: throw new BundlerException($"<{tok.Keyword}> is not a cubemap property.");
            }
        }

        EndResource(rec, cube);
    }

    // HandleVolumeTextureToken (bundler.cpp). Source/AlphaSource repeat, one per
    // depth slice, so they accumulate in file order rather than overwriting.
    private void HandleVolumeTexture()
    {
        var rec = BeginResource();
        var vol = new VolumeTexture(this);
        bool done = false;

        while (!done)
        {
            var tok = _reader.GetNextToken();
            string val = "";
            if ((tok.Id & Tok.PROPERTY) != 0)
                val = _reader.GetNextTokenString(tok.PropType);

            switch (tok.Id)
            {
                case Tok.PROPERTY_NAME: rec.Name = val; break;
                case Tok.PROPERTY_TEXTURE_SOURCE: vol.Sources.Add(val); break;
                case Tok.PROPERTY_TEXTURE_ALPHASOURCE: vol.AlphaSources.Add(val); break;
                case Tok.PROPERTY_TEXTURE_FILTER: vol.Filter = FilterFromString(val); break;
                case Tok.PROPERTY_TEXTURE_FORMAT: vol.FormatName = val; break;
                case Tok.PROPERTY_TEXTURE_WIDTH: vol.Width = ParseInt(val); break;
                case Tok.PROPERTY_TEXTURE_HEIGHT: vol.Height = ParseInt(val); break;
                case Tok.PROPERTY_VOLUMETEXTURE_DEPTH: vol.Depth = ParseInt(val); break;
                case Tok.PROPERTY_TEXTURE_LEVELS: vol.Levels = ParseInt(val); break;
                case Tok.CLOSEBRACE: done = true; break;
                default: throw new BundlerException($"<{tok.Keyword}> is not a volume texture property.");
            }
        }

        // Report like a texture rather than going through the silent EndResource:
        // a volume is the one resource whose size is easy to get wrong (slices x
        // mips), so the byte count is worth seeing in the build log.
        uint offset = CbHeader;
        vol.SaveToBundle(out _, out uint cbData);
        rec.Offset = offset;
        _resources.Add(rec);

        if (!Quiet)
            Console.WriteLine($"VolumeTexture: Wrote {rec.Identifier} out in format {vol.FormatName} " +
                              $"({vol.Width}x{vol.Height}x{vol.Depth}, {vol.Levels} level(s), {cbData} bytes)");
    }

    private ResourceRecord BeginResource()
    {
        var rec = new ResourceRecord();
        rec.Identifier = _reader.GetNextTokenString(TokType.Identifier);
        rec.Name = rec.Identifier;
        if (IsExistingIdentifier(rec.Identifier))
            throw new BundlerException($"Second usage of identifier <{rec.Identifier}>");
        if (_reader.GetNextToken().Id != Tok.OPENBRACE)
            throw new BundlerException("Resource name should be followed by an open brace");
        return rec;
    }

    private void EndResource(ResourceRecord rec, IResource res)
    {
        uint offset = CbHeader;
        res.SaveToBundle(out _, out _);
        rec.Offset = offset;
        _resources.Add(rec);
    }

    private static bool IsHexLiteral(string s) =>
        s.Length > 2 && s[0] == '0' && (s[1] == 'x' || s[1] == 'X');

    // HandleVertexBufferToken (bundler.cpp).
    private void HandleVertexBuffer()
    {
        var rec = BeginResource();
        var vb = new VertexBuffer(this);
        bool gotData = false;
        bool done = false;

        while (!done)
        {
            var tok = _reader.GetNextToken();
            switch (tok.Id)
            {
                case Tok.PROPERTY_NAME:
                    rec.Name = _reader.GetNextTokenString(TokType.Any);
                    break;
                case Tok.PROPERTY_VERTEXBUFFER_VERTEXFILE:
                    if (gotData) throw new BundlerException("Too many VertexData or VertexFile statements");
                    gotData = true;
                    vb.LoadVertexDataFromFile(BundlerPath.ResolveAgainst(_pathPrefix,
                        _reader.GetNextTokenString(TokType.Filename)));
                    break;
                case Tok.PROPERTY_VERTEXBUFFER_VERTEXDATA:
                    if (gotData) throw new BundlerException("Too many VertexData or VertexFile statements");
                    gotData = true;
                    if (_reader.GetNextToken().Id != Tok.OPENBRACE)
                        throw new BundlerException("VertexData property must begin with an open brace.");
                    while (true)
                    {
                        string s = _reader.GetNextTokenString(TokType.Any);
                        if (s.Length == 0 || s[0] == '}') break;
                        if (IsHexLiteral(s))
                            vb.AddVertexData(Convert.ToUInt32(s.Substring(2), 16));
                        else if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                            vb.AddVertexData(d);
                    }
                    break;
                case Tok.PROPERTY_VERTEXBUFFER_VERTEXFORMAT:
                    if (_reader.GetNextToken().Id != Tok.OPENBRACE)
                        throw new BundlerException("VertexFormat property must begin with an open brace.");
                    while (true)
                    {
                        string s = _reader.GetNextTokenString(TokType.Any);
                        if (s.Length == 0 || s[0] == '}') break;
                        if (!Vsdt.ByName.TryGetValue(s, out uint fmt))
                            throw new BundlerException($"Unrecognized attribute format: {s}");
                        vb.AddVertexFormat(fmt);
                    }
                    break;
                case Tok.CLOSEBRACE:
                    done = true;
                    break;
                default:
                    throw new BundlerException($"<{tok.Keyword}> is not a vertexbuffer property.");
            }
        }

        EndResource(rec, vb);
    }

    // HandleIndexBufferToken (bundler.cpp).
    private void HandleIndexBuffer()
    {
        var rec = BeginResource();
        var ib = new IndexBuffer(this);
        bool gotData = false;
        bool done = false;

        while (!done)
        {
            var tok = _reader.GetNextToken();
            switch (tok.Id)
            {
                case Tok.PROPERTY_NAME:
                    rec.Name = _reader.GetNextTokenString(TokType.Any);
                    break;
                case Tok.PROPERTY_INDEXBUFFER_INDEXFILE:
                    if (gotData) throw new BundlerException("Too many IndexData or IndexFile statements");
                    gotData = true;
                    ib.LoadIndicesFromFile(_reader.GetNextTokenString(TokType.Filename));
                    break;
                case Tok.PROPERTY_INDEXBUFFER_INDEXDATA:
                    if (gotData) throw new BundlerException("Too many IndexData or IndexFile statements");
                    gotData = true;
                    if (_reader.GetNextToken().Id != Tok.OPENBRACE)
                        throw new BundlerException("Index property must begin with an open brace.");
                    while (true)
                    {
                        string s = _reader.GetNextTokenString(TokType.Any);
                        if (s.Length == 0 || s[0] == '}') break;
                        if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out long v))
                            ib.AddIndex((ushort)v);
                    }
                    break;
                case Tok.CLOSEBRACE:
                    done = true;
                    break;
                default:
                    throw new BundlerException($"<{tok.Keyword}> is not a indexbuffer property.");
            }
        }

        EndResource(rec, ib);
    }

    // HandleUserDataToken (bundler.cpp).
    private void HandleUserData()
    {
        var rec = BeginResource();
        var ud = new UserData(this);
        bool done = false;

        while (!done)
        {
            var tok = _reader.GetNextToken();
            string val = "";
            if ((tok.Id & Tok.PROPERTY) != 0)
                val = _reader.GetNextTokenString(tok.PropType);

            switch (tok.Id)
            {
                case Tok.PROPERTY_NAME: rec.Name = val; break;
                case Tok.PROPERTY_USERDATA_DATAFILE: ud.Source = val; break;
                case Tok.CLOSEBRACE: done = true; break;
                default: throw new BundlerException($"<{tok.Keyword}> is not a userdata property.");
            }
        }

        EndResource(rec, ud);
    }

    private static uint ParseInt(string s) => uint.TryParse(s, out var v) ? v : 0; // atoi semantics for non-negative

    // FilterFromString (bundler.cpp) — D3DX filter flags. Kept as raw values so
    // the blitter can interpret them identically to the XDK.
    public static uint FilterFromString(string filter)
    {
        uint f = D3DX.FILTER_TRIANGLE, address = 0, dither = 0;
        foreach (var raw in filter.Split('|'))
        {
            var t = raw.Trim();
            if (t.Equals("NONE", StringComparison.OrdinalIgnoreCase)) f = D3DX.FILTER_NONE;
            if (t.Equals("POINT", StringComparison.OrdinalIgnoreCase)) f = D3DX.FILTER_POINT;
            if (t.Equals("LINEAR", StringComparison.OrdinalIgnoreCase)) f = D3DX.FILTER_LINEAR;
            if (t.Equals("TRIANGLE", StringComparison.OrdinalIgnoreCase)) f = D3DX.FILTER_TRIANGLE;
            if (t.Equals("BOX", StringComparison.OrdinalIgnoreCase)) f = D3DX.FILTER_BOX;
            if (t.Equals("WRAP", StringComparison.OrdinalIgnoreCase)) address = 0;
            if (t.Equals("CLAMP", StringComparison.OrdinalIgnoreCase)) address = D3DX.FILTER_MIRROR;
            if (t.Equals("DITHER", StringComparison.OrdinalIgnoreCase)) dither = D3DX.FILTER_DITHER;
        }
        return f | address | dither;
    }

    // --- EOF: finalize + write files (HandleEOFToken/FlushDataFile/WriteHeaderFile)
    private void HandleEof()
    {
        // Terminate the header list with 0xffffffff.
        WriteHeader(BitConverter.GetBytes(0xffffffffu));

        FlushDataFile();
        WriteHeaderFile();
    }

    private void FlushDataFile()
    {
        // Pad the data to a 2k DVD sector.
        PadToAlignment(2048);

        uint magic = OutputVersion == XprVersion.Xpr1 ? XPR1_MAGIC : XPR0_MAGIC;

        uint offsetsTableSize = 0, offsetsTablePadBytes = 0;
        BaseResourceOffset = 0;

        if (OutputVersion >= XprVersion.Xpr1)
        {
            offsetsTableSize = 4u + 8u * (uint)_resources.Count + 4u;
            foreach (var r in _resources)
                offsetsTableSize += (uint)(Encoding.ASCII.GetByteCount(r.Name) + 1);
            offsetsTablePadBytes = (16 - (offsetsTableSize % 16)) % 16;
            offsetsTableSize += offsetsTablePadBytes;
            BaseResourceOffset = offsetsTableSize;
        }

        const uint XprHeaderSize = 12; // sizeof(XPR_HEADER)
        uint headerSize = XprHeaderSize + offsetsTableSize + CbHeader;

        uint cbFill = 0;
        if (headerSize % 2048 != 0)
        {
            cbFill = 2048 - (headerSize % 2048);
            headerSize += cbFill;
        }
        uint totalSize = headerSize + CbData;

        // The .rdf names its output path (out_packedresource, typically under Media\), and that
        // directory does not necessarily exist in a fresh checkout -- create it rather than
        // failing the build.
        var xprDir = Path.GetDirectoryName(Path.GetFullPath(XprPath));
        if (!string.IsNullOrEmpty(xprDir))
            Directory.CreateDirectory(xprDir);

        using var fs = new FileStream(XprPath, FileMode.Create, FileAccess.Write);
        var w = new BinaryWriter(fs);

        // XPR_HEADER = { dwMagic, dwTotalSize, dwHeaderSize }.
        w.Write(magic);
        w.Write(totalSize);
        w.Write(headerSize);

        if (OutputVersion >= XprVersion.Xpr1)
        {
            w.Write((uint)_resources.Count);

            uint stringBase = (uint)_resources.Count * 8u + 8u; // n*(ptr+dword) + dword + ptr
            foreach (var r in _resources)
            {
                w.Write(stringBase);
                stringBase += (uint)(Encoding.ASCII.GetByteCount(r.Name) + 1);
                w.Write(r.Offset + BaseResourceOffset);
            }
            w.Write(0u); // NULL terminator

            foreach (var r in _resources)
            {
                w.Write(Encoding.ASCII.GetBytes(r.Name));
                w.Write((byte)0);
            }

            for (uint i = 0; i < offsetsTablePadBytes; i++)
                w.Write((byte)0);
        }

        // Resource header structs (terminated by 0xffffffff already appended).
        w.Write(_header.ToArray());

        for (uint i = 0; i < cbFill; i++)
            w.Write((byte)0xAD); // 0xDEAD fill

        w.Write(_data.ToArray());
    }

    private void WriteHeaderFile()
    {
        var sb = new StringBuilder();
        sb.Append($"// Automatically generated by the bundler tool from {RdfPath}\n\n");

        string prefix;
        if (!string.IsNullOrEmpty(Prefix))
        {
            prefix = Prefix;
        }
        else
        {
            int slash = RdfPath.LastIndexOfAny(new[] { '\\', '/' });
            prefix = slash >= 0 ? RdfPath.Substring(slash + 1) : RdfPath;
            int dot = prefix.IndexOf('.');
            if (dot >= 0) prefix = prefix.Substring(0, dot);
        }

        sb.Append($"#define {prefix}_NUM_RESOURCES {_resources.Count}UL\n\n");

        if (SingleTexture)
        {
            sb.Append($"#define {prefix}_OFFSET {_resources[0].Offset + BaseResourceOffset}UL\n");
        }
        else
        {
            foreach (var r in _resources)
                sb.Append($"#define {prefix}_{r.Identifier}_OFFSET {r.Offset + BaseResourceOffset}UL\n");
        }

        File.WriteAllText(HdrPath, sb.ToString());
    }
}
