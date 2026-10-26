using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VPet_Simulator.Windows.Companion;

/// <summary>In-memory normalized image; no source path, EXIF metadata or temporary image file is retained.</summary>
internal sealed class ChatImage
{
    // WIC decode/encode cannot be interrupted mid-call. A shared gate prevents
    // repeated cancel/re-import actions from accumulating native codec workers.
    private static readonly SemaphoreSlim CodecGate = new(1, 1);
    public const int MaxFileBytes = 10 * 1024 * 1024;
    public const long MaxDecodedPixels = 40_000_000;
    public const int MaximumDimension = 2048;
    public const int PreviewMaximumDimension = 512;

    public string Name { get; }
    public string MediaType { get; }
    public string DataUrl { get; }
    public int ByteLength { get; }
    public int Width { get; }
    public int Height { get; }
    public BitmapSource Preview { get; }

    private ChatImage(string name, string mediaType, byte[] bytes, BitmapSource preview)
    {
        Name = string.IsNullOrWhiteSpace(name) ? "图片" : Path.GetFileName(name);
        if (Name.Length > 128) Name = Name[..128];
        MediaType = mediaType;
        ByteLength = bytes.Length;
        DataUrl = "data:" + mediaType + ";base64," + Convert.ToBase64String(bytes);
        Width = preview.PixelWidth; Height = preview.PixelHeight;
        // History and UI share a bounded thumbnail, not a full decoded image per attachment.
        // PixelOnly creates independent storage so a thumbnail cannot retain its 2048px source.
        if (Math.Max(Width, Height) > PreviewMaximumDimension)
        {
            double scale = (double)PreviewMaximumDimension / Math.Max(Width, Height);
            var thumbnail = new TransformedBitmap(preview, new ScaleTransform(scale, scale)); thumbnail.Freeze();
            Preview = PixelOnly(thumbnail);
        }
        else Preview = preview;
    }

    public static async Task<ChatImage> FromFileAsync(string path, CancellationToken cancellationToken = default)
    {
        string extension = Path.GetExtension(path);
        if (!extension.Equals(".png", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("目前支持 PNG、JPG 和 JPEG 图片。");
        byte[] bytes;
        await using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            65536, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            if (file.Length <= 0 || file.Length > MaxFileBytes) throw new ArgumentException("每张原图应大于0字节且不超过10MiB。");
            using var memory = new MemoryStream((int)file.Length);
            byte[] block = new byte[65536];
            int count;
            while ((count = await file.ReadAsync(block, cancellationToken).ConfigureAwait(false)) > 0)
            {
                if (memory.Length + count > MaxFileBytes) throw new ArgumentException("每张原图不能超过10MiB。");
                memory.Write(block, 0, count);
            }
            bytes = memory.ToArray();
        }
        return await WithCodecAsync(() => Decode(bytes, Path.GetFileName(path), cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    public static Task<ChatImage> FromBitmapAsync(BitmapSource bitmap, string name = "剪贴板截图", CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateDimensions(bitmap.PixelWidth, bitmap.PixelHeight);
        // Clipboard images belong to the calling dispatcher until they are cloned and frozen.
        BitmapSource snapshot = bitmap;
        if (!snapshot.IsFrozen)
        {
            snapshot = bitmap.CloneCurrentValue();
            snapshot.Freeze();
        }
        return WithCodecAsync(() => EncodeNormalized(snapshot, name, true, cancellationToken), cancellationToken);
    }

    private static async Task<ChatImage> WithCodecAsync(Func<ChatImage> convert, CancellationToken cancellation)
    {
        await CodecGate.WaitAsync(cancellation).ConfigureAwait(false);
        try { return await Task.Run(convert, cancellation).ConfigureAwait(false); }
        finally { CodecGate.Release(); }
    }

    private static ChatImage Decode(byte[] bytes, string name, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        try
        {
            using var input = new MemoryStream(bytes, false);
            // Read dimensions/metadata before asking the codec to materialize a potentially large image.
            var decoder = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnDemand);
            bool png = decoder is PngBitmapDecoder;
            if (!png && decoder is not JpegBitmapDecoder) throw new ArgumentException("文件内容不是有效的 PNG 或 JPEG 图片。");
            if (decoder.Frames.Count != 1 || png && ContainsAnimation(bytes, cancellation))
                throw new ArgumentException("目前只支持静态 PNG、JPG 图片，请先将动画导出为单张图片。");
            var frame = decoder.Frames[0];
            ValidateDimensions(frame.PixelWidth, frame.PixelHeight);
            ushort orientation = ReadOrientation(frame.Metadata as BitmapMetadata);
            input.Position = 0;
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
            if (frame.PixelWidth > MaximumDimension || frame.PixelHeight > MaximumDimension)
            {
                if (frame.PixelWidth >= frame.PixelHeight) bitmap.DecodePixelWidth = MaximumDimension;
                else bitmap.DecodePixelHeight = MaximumDimension;
            }
            bitmap.StreamSource = input;
            bitmap.EndInit(); bitmap.Freeze();
            BitmapSource source = ApplyOrientation(bitmap, orientation);
            return EncodeNormalized(source, name, png, cancellation);
        }
        catch (System.IO.FileFormatException exception)
        { throw new ArgumentException("图片无法解码，文件可能已损坏。", exception); }
        catch (NotSupportedException exception)
        { throw new ArgumentException("图片无法解码，文件格式不受支持或已损坏。", exception); }
    }

    private static void ValidateDimensions(int width, int height)
    {
        if (width < 1 || height < 1 || (long)width * height > MaxDecodedPixels)
            throw new ArgumentException("图片尺寸无效，或超过4000万像素上限。");
    }

    private static ushort ReadOrientation(BitmapMetadata? metadata)
    {
        if (metadata == null) return 1;
        try
        {
            object? value = metadata.GetQuery("/app1/ifd/{ushort=274}");
            return value == null ? (ushort)1 : Convert.ToUInt16(value);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or InvalidOperationException or OverflowException)
        { return 1; }
    }

    private static BitmapSource ApplyOrientation(BitmapSource source, ushort orientation)
    {
        var transform = new TransformGroup();
        switch (orientation)
        {
            case 2: transform.Children.Add(new ScaleTransform(-1, 1)); break;
            case 3: transform.Children.Add(new RotateTransform(180)); break;
            case 4: transform.Children.Add(new ScaleTransform(1, -1)); break;
            case 5: transform.Children.Add(new RotateTransform(90)); transform.Children.Add(new ScaleTransform(-1, 1)); break;
            case 6: transform.Children.Add(new RotateTransform(90)); break;
            case 7: transform.Children.Add(new RotateTransform(90)); transform.Children.Add(new ScaleTransform(1, -1)); break;
            case 8: transform.Children.Add(new RotateTransform(270)); break;
            default: return source;
        }
        transform.Freeze();
        var result = new TransformedBitmap(source, transform); result.Freeze(); return result;
    }

    private static ChatImage EncodeNormalized(BitmapSource source, string name, bool png, CancellationToken cancellation)
    {
        ValidateDimensions(source.PixelWidth, source.PixelHeight);
        cancellation.ThrowIfCancellationRequested();
        if (source.PixelWidth > MaximumDimension || source.PixelHeight > MaximumDimension)
        {
            double scale = (double)MaximumDimension / Math.Max(source.PixelWidth, source.PixelHeight);
            source = new TransformedBitmap(source, new ScaleTransform(scale, scale)); source.Freeze();
        }
        // A fresh pixel-only bitmap strips source metadata, paths and non-square source DPI.
        BitmapSource preview = PixelOnly(source);
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            BitmapEncoder encoder = png ? new PngBitmapEncoder() : new JpegBitmapEncoder { QualityLevel = 90 };
            encoder.Frames.Add(BitmapFrame.Create(preview));
            using var encoded = new MemoryStream(); encoder.Save(encoded);
            cancellation.ThrowIfCancellationRequested();
            if (encoded.Length <= MaxFileBytes)
                return new ChatImage(name, png ? "image/png" : "image/jpeg", encoded.ToArray(), preview);
            // Particularly noisy clipboard PNGs can exceed the byte limit despite the dimension limit.
            if (Math.Max(preview.PixelWidth, preview.PixelHeight) <= 256)
                throw new ArgumentException("图片处理后仍超过10MiB，请先压缩后重试。");
            var smaller = new TransformedBitmap(preview, new ScaleTransform(0.8, 0.8)); smaller.Freeze();
            preview = PixelOnly(smaller);
        }
    }

    private static BitmapSource PixelOnly(BitmapSource source)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int stride = checked(converted.PixelWidth * 4);
        byte[] pixels = new byte[checked(stride * converted.PixelHeight)];
        converted.CopyPixels(pixels, stride, 0);
        var result = BitmapSource.Create(converted.PixelWidth, converted.PixelHeight, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        result.Freeze(); return result;
    }

    private static bool ContainsAnimation(byte[] bytes, CancellationToken cancellation)
    {
        for (int offset = 8; offset <= bytes.Length - 12;)
        {
            cancellation.ThrowIfCancellationRequested();
            int length = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset, 4));
            if (length < 0 || length > bytes.Length - offset - 12) break;
            if (bytes[offset + 4] == 'a' && bytes[offset + 5] == 'c' && bytes[offset + 6] == 'T' && bytes[offset + 7] == 'L') return true;
            offset += length + 12;
        }
        return false;
    }
}
