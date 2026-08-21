using System.Collections.Concurrent;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace VKApiServer;

public static class FaceEngine
{
    private const int Size = 112;
    private const int Dims = 128;

    public const float MatchThreshold = 0.40f;

    private static InferenceSession? _session;
    private static readonly object _lock = new();
    private static string _inputName = "data";

    public static bool Available => ModelPath() != null;

    private static string? ModelPath()
    {
        var p = Environment.GetEnvironmentVariable("FACE_MODEL_PATH");
        if (!string.IsNullOrWhiteSpace(p) && File.Exists(p)) return p;
        p = Path.Combine(AppContext.BaseDirectory, "models", "sface.onnx");
        return File.Exists(p) ? p : null;
    }

    private static InferenceSession Session()
    {
        if (_session != null) return _session;
        lock (_lock)
        {
            if (_session != null) return _session;
            var path = ModelPath() ?? throw new InvalidOperationException("Face model not installed on this server.");
            var opts = new Microsoft.ML.OnnxRuntime.SessionOptions { IntraOpNumThreads = 2, InterOpNumThreads = 1 };
            var s = new InferenceSession(path, opts);
            _inputName = s.InputMetadata.Keys.First();
            _session = s;
            return _session;
        }
    }

    public static float[] Embed(byte[] imageBytes)
    {
        using var img = Image.Load<Rgb24>(imageBytes);

        int side = Math.Min(img.Width, img.Height);
        int left = (img.Width - side) / 2;
        int top = (img.Height - side) / 2;
        img.Mutate(x => x
            .Crop(new Rectangle(left, top, side, side))
            .Resize(Size, Size));

        var tensor = new DenseTensor<float>(new[] { 1, 3, Size, Size });
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                var px = img[x, y];
                tensor[0, 0, y, x] = px.B;
                tensor[0, 1, y, x] = px.G;
                tensor[0, 2, y, x] = px.R;
            }
        }

        using var results = Session().Run(new[] { NamedOnnxValue.CreateFromTensor(_inputName, tensor) });
        var raw = results.First().AsEnumerable<float>().ToArray();
        if (raw.Length < Dims)
            throw new InvalidOperationException("Unexpected model output.");

        double norm = 0;
        for (int i = 0; i < raw.Length; i++) norm += raw[i] * raw[i];
        norm = Math.Sqrt(norm);
        if (norm < 1e-9) throw new InvalidOperationException("Could not read a face from that image.");

        var v = new float[raw.Length];
        for (int i = 0; i < raw.Length; i++) v[i] = (float)(raw[i] / norm);
        return v;
    }

    public static float Similarity(float[] a, float[] b)
    {
        if (a.Length != b.Length) return -1f;
        double dot = 0;
        for (int i = 0; i < a.Length; i++) dot += a[i] * b[i];
        return (float)dot;
    }

    public static byte[] Pack(float[] v)
    {
        var bytes = new byte[v.Length * 4];
        Buffer.BlockCopy(v, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    public static float[] Unpack(byte[] b)
    {
        var v = new float[b.Length / 4];
        Buffer.BlockCopy(b, 0, v, 0, b.Length);
        return v;
    }

    public static string Thumb(byte[] imageBytes, int px = 96)
    {
        using var img = Image.Load<Rgb24>(imageBytes);
        int side = Math.Min(img.Width, img.Height);
        img.Mutate(x => x
            .Crop(new Rectangle((img.Width - side) / 2, (img.Height - side) / 2, side, side))
            .Resize(px, px));
        using var ms = new MemoryStream();
        img.SaveAsJpeg(ms);
        return "data:image/jpeg;base64," + Convert.ToBase64String(ms.ToArray());
    }

    public static byte[] DecodeDataUri(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) throw new InvalidOperationException("No image supplied.");
        int comma = s.IndexOf(',');
        if (s.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && comma > 0)
            s = s.Substring(comma + 1);
        return Convert.FromBase64String(s.Trim());
    }
}
