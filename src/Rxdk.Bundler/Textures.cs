// Port of the XDK bundler's CBaseTexture + CTexture2D (basetexture.cpp/texture.cpp):
// load source art, convert the pixels to the requested Xbox format, lay the data
// out linear/swizzled, and emit the packed D3DTexture header via XGSetTextureHeader.

namespace Rxdk.Bundler;

internal abstract class BaseTexture
{
    protected readonly Bundler B;

    public string FormatName = "";
    public int FormatIndex = -1;       // index into XboxFormats.TextureFormats
    public uint Filter = D3DX.FILTER_TRIANGLE;
    public uint Levels = 1;
    public uint ColorKey = 0;

    protected BaseTexture(Bundler b) => B = b;

    protected FormatSpec Spec => XboxFormats.TextureFormats[FormatIndex];

    // --- ConvertTextureFormat (basetexture.cpp) -------------------------------
    // Source is A8R8G8B8 (0xAARRGGBB dwords). Writes packed pixels of dstFormat
    // into dst and returns the number of bytes written.
    protected static uint ConvertTextureFormat(byte[] src, uint width, uint height, uint depth, byte[] dst, uint dstFormat)
    {
        int s = 0, o = 0;

        void W8(uint v) => dst[o++] = (byte)v;
        void W16(uint v) { dst[o++] = (byte)(v & 0xff); dst[o++] = (byte)((v >> 8) & 0xff); }
        void W32(uint v) { dst[o++] = (byte)(v & 0xff); dst[o++] = (byte)((v >> 8) & 0xff); dst[o++] = (byte)((v >> 16) & 0xff); dst[o++] = (byte)((v >> 24) & 0xff); }

        for (uint z = 0; z < depth; z++)
        for (uint y = 0; y < height; y++)
        for (uint x = 0; x < width; x++)
        {
            uint px = (uint)(src[s] | (src[s + 1] << 8) | (src[s + 2] << 16) | (src[s + 3] << 24));
            s += 4;

            float a = ((px & 0xff000000) >> 24) / 255.0f;
            float r = ((px & 0x00ff0000) >> 16) / 255.0f;
            float g = ((px & 0x0000ff00) >> 8) / 255.0f;
            float b = ((px & 0x000000ff) >> 0) / 255.0f;

            float v = g, u = b;
            float l = (r * 0.299f) + (g * 0.587f) + (b * 0.114f);

            switch (dstFormat)
            {
                case XboxFormats.X_D3DFMT_A8R8G8B8:
                case XboxFormats.X_D3DFMT_LIN_A8R8G8B8:
                    W32(((uint)(a * 0xff) << 24) | ((uint)(r * 0xff) << 16) | ((uint)(g * 0xff) << 8) | (uint)(b * 0xff)); break;
                case XboxFormats.X_D3DFMT_X8R8G8B8:
                case XboxFormats.X_D3DFMT_LIN_X8R8G8B8:
                    W32(((uint)(r * 0xff) << 16) | ((uint)(g * 0xff) << 8) | (uint)(b * 0xff)); break;
                case XboxFormats.X_D3DFMT_A8B8G8R8:
                case XboxFormats.X_D3DFMT_LIN_A8B8G8R8:
                    W32(((uint)(a * 0xff) << 24) | ((uint)(b * 0xff) << 16) | ((uint)(g * 0xff) << 8) | (uint)(r * 0xff)); break;
                case XboxFormats.X_D3DFMT_B8G8R8A8:
                case XboxFormats.X_D3DFMT_LIN_B8G8R8A8:
                    W32(((uint)(b * 0xff) << 24) | ((uint)(g * 0xff) << 16) | ((uint)(r * 0xff) << 8) | (uint)(a * 0xff)); break;
                case XboxFormats.X_D3DFMT_R8G8B8A8:
                case XboxFormats.X_D3DFMT_LIN_R8G8B8A8:
                    W32(((uint)(r * 0xff) << 24) | ((uint)(g * 0xff) << 16) | ((uint)(b * 0xff) << 8) | (uint)(a * 0xff)); break;
                case XboxFormats.X_D3DFMT_A1R5G5B5:
                case XboxFormats.X_D3DFMT_LIN_A1R5G5B5:
                    W16(((uint)(a * 0x01) << 15) | ((uint)(r * 0x1f) << 10) | ((uint)(g * 0x1f) << 5) | (uint)(b * 0x1f)); break;
                case XboxFormats.X_D3DFMT_X1R5G5B5:
                case XboxFormats.X_D3DFMT_LIN_X1R5G5B5:
                    W16(((uint)(r * 0x1f) << 10) | ((uint)(g * 0x1f) << 5) | (uint)(b * 0x1f)); break;
                case XboxFormats.X_D3DFMT_R5G5B5A1:
                case XboxFormats.X_D3DFMT_LIN_R5G5B5A1:
                    W16(((uint)(r * 0x1f) << 11) | ((uint)(g * 0x1f) << 6) | ((uint)(b * 0x1f) << 1) | (uint)(a * 0x01)); break;
                case XboxFormats.X_D3DFMT_R5G6B5:
                case XboxFormats.X_D3DFMT_LIN_R5G6B5:
                    W16(((uint)(r * 0x1f) << 11) | ((uint)(g * 0x3f) << 5) | (uint)(b * 0x1f)); break;
                case XboxFormats.X_D3DFMT_R6G5B5:
                case XboxFormats.X_D3DFMT_LIN_R6G5B5:
                    W16(((uint)(r * 0x3f) << 10) | ((uint)(g * 0x1f) << 5) | (uint)(b * 0x1f)); break;
                case XboxFormats.X_D3DFMT_A4R4G4B4:
                case XboxFormats.X_D3DFMT_LIN_A4R4G4B4:
                    W16(((uint)(a * 0x0f) << 12) | ((uint)(r * 0x0f) << 8) | ((uint)(g * 0x0f) << 4) | (uint)(b * 0x0f)); break;
                case XboxFormats.X_D3DFMT_R4G4B4A4:
                case XboxFormats.X_D3DFMT_LIN_R4G4B4A4:
                    W16(((uint)(r * 0x0f) << 12) | ((uint)(g * 0x0f) << 8) | ((uint)(b * 0x0f) << 4) | (uint)(a * 0x0f)); break;
                case XboxFormats.X_D3DFMT_R8B8:
                case XboxFormats.X_D3DFMT_LIN_R8B8:
                    W16(((uint)(r * 0xff) << 8) | (uint)(b * 0xff)); break;
                case XboxFormats.X_D3DFMT_G8B8:
                case XboxFormats.X_D3DFMT_LIN_G8B8:
                    W16(((uint)(g * 0xff) << 8) | (uint)(b * 0xff)); break;
                case XboxFormats.X_D3DFMT_A8L8:
                case XboxFormats.X_D3DFMT_LIN_A8L8:
                    W16(((uint)(a * 0xff) << 8) | (uint)(l * 0xff)); break;
                case XboxFormats.X_D3DFMT_L16:
                case XboxFormats.X_D3DFMT_LIN_L16:
                    W16((uint)(l * 0xffff)); break;
                case XboxFormats.X_D3DFMT_L8:
                case XboxFormats.X_D3DFMT_LIN_L8:
                    W8((uint)(l * 0xff)); break;
                case XboxFormats.X_D3DFMT_A8:
                case XboxFormats.X_D3DFMT_LIN_A8:
                    W8((uint)(a * 0xff)); break;
                case XboxFormats.X_D3DFMT_AL8:
                case XboxFormats.X_D3DFMT_LIN_AL8:
                    W8((uint)(l * 0xff)); break;
                case XboxFormats.X_D3DFMT_V16U16:
                case XboxFormats.X_D3DFMT_LIN_V16U16:
                    W32(((uint)(v * 0xffff) << 16) | (uint)(u * 0xffff)); break;
                default:
                    throw new BundlerException($"ConvertTextureFormat: unsupported destination format 0x{dstFormat:X}");
            }
        }
        return (uint)o;
    }

    // --- data writers (basetexture.cpp) --------------------------------------
    protected uint WriteLinearTextureData(byte[] bits, uint width, uint height, uint depth)
    {
        uint bpt = XboxFormats.BytesPerPixelFromFormat(Spec.XboxFormat);
        uint pitch = (width * bpt + XboxFormats.D3DTEXTURE_PITCH_ALIGNMENT - 1) & ~(XboxFormats.D3DTEXTURE_PITCH_ALIGNMENT - 1);
        uint textureSize = pitch * height * depth;

        if (pitch == width * bpt)
        {
            B.WriteData(bits.AsSpan(0, (int)textureSize));
        }
        else
        {
            var zeros = new byte[64];
            int src = 0;
            uint rowBytes = width * bpt;
            uint pad = pitch - rowBytes;
            for (uint z = 0; z < depth; z++)
            for (uint y = 0; y < height; y++)
            {
                B.WriteData(bits.AsSpan(src, (int)rowBytes));
                src += (int)rowBytes;
                B.WriteData(zeros.AsSpan(0, (int)pad));
            }
        }
        return textureSize;
    }

    protected uint WriteSwizzledTextureData(byte[] bits, uint width, uint height, uint depth)
    {
        uint bpt = XboxFormats.BytesPerPixelFromFormat(Spec.XboxFormat);
        byte[] swizzled = depth == 1
            ? Swizzle.SwizzleRect2D(bits, width, height, bpt)
            : Swizzle.SwizzleBox3D(bits, width, height, depth, bpt);
        B.WriteData(swizzled);
        return width * height * depth * bpt;
    }

    // WriteCompressedTextureData (basetexture.cpp): compress the A8R8G8B8 mip to DXT.
    protected uint WriteCompressedTextureData(byte[] bits, uint width, uint height, uint depth)
    {
        uint srcPitch = width * 4; // dwWidth * sizeof(DWORD)
        // Premultiply is keyed off the format NAME digit (DXT2/DXT4), not the Xbox id
        // (DXT2 and DXT3 share id 0x0E but only DXT2 premultiplies).
        bool preMultiply = Spec.Name.Length > 10 && (Spec.Name[10] == '2' || Spec.Name[10] == '4');

        uint compressedSize;
        switch (Spec.XboxFormat)
        {
            case XboxFormats.X_D3DFMT_DXT1: compressedSize = width * height / 2; break; // 8 bytes/block
            case XboxFormats.X_D3DFMT_DXT2: compressedSize = width * height; break;     // 16 bytes/block (also DXT3)
            case XboxFormats.X_D3DFMT_DXT4: compressedSize = width * height; break;     // 16 bytes/block (also DXT5)
            default: throw new BundlerException($"WriteCompressedTextureData: bad format 0x{Spec.XboxFormat:X}");
        }

        var compressed = new byte[depth * compressedSize];
        int srcOff = 0, dstOff = 0;
        for (uint i = 0; i < depth; i++)
        {
            S3Tc.CompressRect(compressed, dstOff, Spec.XboxFormat, width, height,
                              bits, srcOff, XboxFormats.X_D3DFMT_LIN_A8R8G8B8, srcPitch,
                              0.5f, preMultiply ? S3Tc.XGCOMPRESS_PREMULTIPLY : 0);
            srcOff += (int)(srcPitch * height);
            dstOff += (int)compressedSize;
        }

        if (depth == 1)
        {
            B.WriteData(compressed.AsSpan(0, (int)compressedSize));
        }
        else
        {
            throw new BundlerException("Compressed volume textures are not yet ported (block-linear reorder).");
        }
        return depth * compressedSize;
    }

    // --- SaveImage (basetexture.cpp) -----------------------------------------
    protected uint SaveImage(uint levels, CImage image)
    {
        B.PadToAlignment(XboxFormats.D3DTEXTURE_ALIGNMENT);

        uint width = image.Width, height = image.Height;
        var surface = new byte[width * height * 4];
        uint written = 0;

        for (uint level = 0; level < levels; level++)
        {
            var mip = new CImage(width, height, image.Format);
            CImage.Blt(mip, image, Filter, 0); // level 0: copy; higher levels resample (not yet ported)

            if (Spec.Type == FmtType.Compressed)
            {
                written += WriteCompressedTextureData(mip.Data, width, height, 1);
            }
            else
            {
                ConvertTextureFormat(mip.Data, width, height, 1, surface, Spec.XboxFormat);
                if (Spec.Type == FmtType.Swizzled)
                    written += WriteSwizzledTextureData(surface, width, height, 1);
                else
                    written += WriteLinearTextureData(surface, width, height, 1);
            }

            if (width >= 2) width >>= 1;
            if (height >= 2) height >>= 1;
            if (Spec.Type == FmtType.Compressed)
            {
                width = Math.Max(width, 4);
                height = Math.Max(height, 4);
            }
        }
        return written;
    }

    // --- LoadImage (basetexture.cpp) — combine color + optional alpha ---------
    protected CImage LoadImage(string source, string alphaSource, string basePath)
    {
        string colorPath = BundlerPath.ResolveAgainst(basePath, source);
        var color = CImage.LoadFromFile(colorPath);
        if (color.Format == D3DFmt.P8)
            color.Depalettize();

        CImage? alpha = null;
        if (!string.IsNullOrEmpty(alphaSource))
        {
            string alphaPath = BundlerPath.ResolveAgainst(basePath, alphaSource);
            alpha = CImage.LoadFromFile(alphaPath);
            if (alpha.Format == D3DFmt.P8)
                throw new BundlerException("Palettized alpha source images are not supported.");
        }

        // A mismatched pair is reconciled by resampling both surfaces up to the
        // larger of the two before the channels are merged.
        uint width = color.Width, height = color.Height;
        if (alpha != null)
        {
            width = Math.Max(width, alpha.Width);
            height = Math.Max(height, alpha.Height);
        }

        var resizedColor = new CImage(width, height, D3DFmt.A8R8G8B8);
        CImage.Blt(resizedColor, color, Filter, ColorKey);

        if (alpha != null)
        {
            var resizedAlpha = new CImage(width, height, alpha.Format);
            CImage.Blt(resizedAlpha, alpha, Filter, 0);

            // Merge the alpha channel. The two shipped tools disagree on which
            // byte of the loaded 0xAARRGGBB alpha pixel becomes the destination
            // alpha: bundler.exe uses byte 3 (the X/alpha channel), which is zero
            // for a 24-bit BMP that loaded as X8R8G8B8, so every 24-bit AlphaSource
            // yields alpha 0 -- confirmed byte-exact against the shipped sample
            // .xpr files (Input\Lightgun matches to the byte). skinbld.exe instead
            // takes the blue channel (byte 0); it opts in via AlphaFromBlueChannel.
            int alphaByte = B.AlphaFromBlueChannel ? 0 : 3;
            for (long i = 0; i < (long)width * height; i++)
                resizedColor.Data[i * 4 + 3] = resizedAlpha.Data[i * 4 + alphaByte];
        }

        return resizedColor;
    }

    protected CImage ResizeImage(uint width, uint height, CImage image)
    {
        var resized = new CImage(width, height, D3DFmt.A8R8G8B8);
        CImage.Blt(resized, image, Filter, 0);
        return resized;
    }
}

internal sealed class Texture2D : BaseTexture, IResource
{
    public string Source = "";
    public string AlphaSource = "";
    public uint Width;
    public uint Height;

    private CImage _image = null!;

    public Texture2D(Bundler b) : base(b) { }

    public void SaveToBundle(out uint cbHeader, out uint cbData)
    {
        LoadTexture();
        B.PadToAlignment(XboxFormats.D3DTEXTURE_ALIGNMENT);
        cbHeader = SaveHeaderInfo(B.CbData);
        cbData = SaveImage(Levels, _image);
    }

    private void LoadTexture()
    {
        _image = LoadImage(Source, AlphaSource, B.PathPrefix);

        if (!string.IsNullOrEmpty(FormatName))
        {
            FormatIndex = XboxFormats.FormatFromString(FormatName);
            if (FormatIndex < 0)
                throw new BundlerException($"Invalid texture format: {FormatName}");
        }

        if (FormatIndex < 0)
        {
            FormatName = "D3DFMT_A8R8G8B8";
            FormatIndex = XboxFormats.FormatFromString(FormatName);
        }

        // Final width/height.
        if (Width == 0 || Height == 0)
        {
            if (Spec.Type == FmtType.Linear)
            {
                Width = _image.Width;
                Height = _image.Height;
            }
            else
            {
                for (Width = 1; Width < _image.Width; Width <<= 1) { }
                for (Height = 1; Height < _image.Height; Height <<= 1) { }
            }
        }

        // Final level count.
        if (Spec.Type == FmtType.Linear)
        {
            Levels = 1;
        }
        else
        {
            uint maxLevels = 1;
            while ((1u << (int)(maxLevels - 1)) < Width && (1u << (int)(maxLevels - 1)) < Height)
                maxLevels++;
            if (Levels < 1 || Levels > maxLevels)
                Levels = maxLevels;
        }

        _image = ResizeImage(Width, Height, _image);
    }

    // SaveHeaderInfo: XGSetTextureHeader -> D3DTexture {Common,Data,Lock,Format,Size}.
    private uint SaveHeaderInfo(uint dwStart)
    {
        XboxFormats.EncodeFormat(Width, Height, 1, Levels, Spec.XboxFormat, 0, false, false,
                                 out uint format, out uint size);

        uint common = 1u | XboxFormats.D3DCOMMON_TYPE_TEXTURE | XboxFormats.D3DCOMMON_VIDEOMEMORY;

        Span<byte> hdr = stackalloc byte[20];
        BitConverter.TryWriteBytes(hdr.Slice(0, 4), common);
        BitConverter.TryWriteBytes(hdr.Slice(4, 4), dwStart); // Data
        BitConverter.TryWriteBytes(hdr.Slice(8, 4), 0u);      // Lock
        BitConverter.TryWriteBytes(hdr.Slice(12, 4), format);
        BitConverter.TryWriteBytes(hdr.Slice(16, 4), size);
        B.WriteHeader(hdr);
        return 20;
    }
}
