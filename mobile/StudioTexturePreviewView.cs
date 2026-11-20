using System;
using Android.Content;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using AssetStudio;

namespace HoKTool.Android;

/// <summary>
/// Android Texture2D preview with the same B/G/R/A channel toggles as
/// Studio-HoK's desktop texture preview.
/// </summary>
public sealed class StudioTexturePreviewView : LinearLayout
{
    private readonly Bitmap source;
    private readonly ImageView image;
    private Bitmap? rendered;
    private readonly bool[] channels = { true, true, true, true };

    public StudioTexturePreviewView(Context context, Bitmap sourceBitmap, Texture2D? texture = null) : base(context)
    {
        source = sourceBitmap ?? throw new ArgumentNullException(nameof(sourceBitmap));
        Orientation = Orientation.Vertical;
        SetPadding(8, 8, 8, 8);

        if (texture != null)
        {
            string info =
                $"Width: {texture.m_Width} · Height: {texture.m_Height}\n" +
                $"Format: {texture.m_TextureFormat}\n" +
                $"Filter: {texture.m_TextureSettings.m_FilterMode} · " +
                $"Aniso: {texture.m_TextureSettings.m_Aniso} · " +
                $"Mip bias: {texture.m_TextureSettings.m_MipBias}\n" +
                $"Wrap: {texture.m_TextureSettings.m_WrapMode}";
            var details = new TextView(context) { Text = info, TextSize = 11 };
            AddView(details);
        }

        var channelRow = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        string[] labels = { "B", "G", "R", "A" };
        for (int i = 0; i < labels.Length; i++)
        {
            int channelIndex = i;
            var check = new CheckBox(context)
            {
                Text = labels[i],
                Checked = true,
                TextSize = 12
            };
            check.CheckedChange += (_, e) =>
            {
                channels[channelIndex] = e.IsChecked;
                RenderChannels();
            };
            channelRow.AddView(check, new LayoutParams(0, -2, 1));
        }
        AddView(channelRow);

        image = new ImageView(context);
        image.SetScaleType(ImageView.ScaleType.FitCenter);
        image.SetAdjustViewBounds(true);
        AddView(image, new LayoutParams(-1, (int)(320 * context.Resources!.DisplayMetrics!.Density)));

        RenderChannels();
    }

    private void RenderChannels()
    {
        int width = source.Width;
        int height = source.Height;
        if (width <= 0 || height <= 0 || (long)width * height > 16_777_216) return;

        int[] pixels = new int[checked(width * height)];
        source.GetPixels(pixels, 0, width, 0, 0, width, height);

        int active = 0;
        for (int i = 0; i < channels.Length; i++) if (channels[i]) active++;

        for (int i = 0; i < pixels.Length; i++)
        {
            int pixel = pixels[i];
            int a = (pixel >>> 24) & 0xFF;
            int r = (pixel >>> 16) & 0xFF;
            int g = (pixel >>> 8) & 0xFF;
            int b = pixel & 0xFF;

            if (!channels[0]) b = active == 1 && channels[3] ? 255 : 0;
            if (!channels[1]) g = active == 1 && channels[3] ? 255 : 0;
            if (!channels[2]) r = active == 1 && channels[3] ? 255 : 0;
            if (!channels[3]) a = 255;

            pixels[i] = (a << 24) | (r << 16) | (g << 8) | b;
        }

        var next = Bitmap.CreateBitmap(width, height, Bitmap.Config.Argb8888!);
        next.SetPixels(pixels, 0, width, 0, 0, width, height);
        var previous = rendered;
        rendered = next;
        image.SetImageBitmap(next);
        if (previous != null && !ReferenceEquals(previous, source))
        {
            try { previous.Recycle(); } catch { }
        }
    }

    protected override void OnDetachedFromWindow()
    {
        try { rendered?.Recycle(); } catch { }
        try { source.Recycle(); } catch { }
        rendered = null;
        base.OnDetachedFromWindow();
    }
}
