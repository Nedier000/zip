using System;
using System.IO;
using Android.Content;
using Android.Media;
using Android.Views;
using Android.Widget;

namespace HoKTool.Android;

/// <summary>
/// Android MediaPlayer preview for formats Android can decode natively.
/// FSB and other game-specific containers remain exportable but are not
/// falsely presented as playable.
/// </summary>
public sealed class StudioAudioPreviewView : LinearLayout
{
    private readonly string filePath;
    private readonly TextView status;
    private readonly TextView position;
    private readonly SeekBar seek;
    private readonly SeekBar volume;
    private readonly CheckBox loop;
    private MediaPlayer? player;
    private bool prepared;
    private bool released;

    public StudioAudioPreviewView(Context context, string path) : base(context)
    {
        filePath = path;
        Orientation = Orientation.Vertical;
        SetPadding(12, 10, 12, 10);

        AddView(new TextView(context)
        {
            Text = Path.GetFileName(path),
            TextSize = 13
        });

        status = new TextView(context) { Text = "Đang chuẩn bị audio…", TextSize = 12 };
        AddView(status);

        position = new TextView(context) { Text = "00:00 / 00:00", TextSize = 11 };
        AddView(position);

        seek = new SeekBar(context) { Max = 1000, Progress = 0 };
        AddView(seek, new LayoutParams(-1, -2));
        seek.StopTrackingTouch += (_, _) =>
        {
            if (prepared && player != null && !released)
            {
                player.SeekTo(seek.Progress);
            }
        };

        var controls = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        var play = new Button(context) { Text = "PLAY" };
        var pause = new Button(context) { Text = "PAUSE" };
        var stop = new Button(context) { Text = "STOP" };
        controls.AddView(play, new LayoutParams(0, -2, 1));
        controls.AddView(pause, new LayoutParams(0, -2, 1));
        controls.AddView(stop, new LayoutParams(0, -2, 1));
        AddView(controls);

        loop = new CheckBox(context) { Text = "Loop" };
        AddView(loop);

        AddView(new TextView(context) { Text = "Volume", TextSize = 11 });
        volume = new SeekBar(context) { Max = 100, Progress = 80 };
        AddView(volume, new LayoutParams(-1, -2));
        volume.ProgressChanged += (_, e) =>
        {
            if (e.FromUser && player != null)
            {
                float value = e.Progress / 100f;
                try { player.SetVolume(value, value); } catch { }
            }
        };

        play.Click += (_, _) =>
        {
            try
            {
                if (prepared && player != null && !released) player.Start();
            }
            catch (Exception ex) { status.Text = "Không phát được: " + ex.Message; }
        };
        pause.Click += (_, _) =>
        {
            try { if (prepared && player?.IsPlaying == true) player.Pause(); }
            catch (Exception ex) { status.Text = "Không tạm dừng được: " + ex.Message; }
        };
        stop.Click += (_, _) =>
        {
            try
            {
                if (prepared && player != null && !released)
                {
                    player.Pause();
                    player.SeekTo(0);
                    seek.Progress = 0;
                    UpdatePosition();
                }
            }
            catch (Exception ex) { status.Text = "Không dừng được: " + ex.Message; }
        };

        string extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is not (".ogg" or ".mp3" or ".wav" or ".m4a" or ".aac" or ".3gp"))
        {
            status.Text = $"Android MediaPlayer không hỗ trợ preview định dạng {extension}. Dữ liệu gốc vẫn có thể xuất.";
            play.Enabled = pause.Enabled = stop.Enabled = false;
            seek.Enabled = false;
            return;
        }

        try
        {
            player = new MediaPlayer();
            player.SetDataSource(path);
            player.IsLooping = false;
            player.SetVolume(0.8f, 0.8f);
            player.Prepared += (_, _) =>
            {
                if (released || player == null) return;
                prepared = true;
                player.IsLooping = loop.Checked;
                seek.Max = Math.Max(1, player.Duration);
                seek.Progress = 0;
                status.Text = "Audio sẵn sàng";
                UpdatePosition();
            };
            player.Completion += (_, _) =>
            {
                if (released) return;
                if (loop.Checked)
                {
                    try { player?.SeekTo(0); player?.Start(); } catch { }
                }
                else
                {
                    seek.Progress = 0;
                    UpdatePosition();
                    status.Text = "Đã phát xong";
                }
            };
            loop.CheckedChange += (_, e) =>
            {
                try { if (prepared && player != null) player.IsLooping = e.IsChecked; } catch { }
            };
            player.PrepareAsync();
        }
        catch (Exception ex)
        {
            status.Text = "Không mở được audio: " + ex.Message;
            ReleasePlayer();
        }
    }

    private void UpdatePosition()
    {
        if (!prepared || player == null || released) return;
        try
        {
            int current = Math.Max(0, player.CurrentPosition);
            int duration = Math.Max(0, player.Duration);
            position.Text = $"{FormatTime(current)} / {FormatTime(duration)}";
        }
        catch { }
    }

    private static string FormatTime(int milliseconds)
    {
        int seconds = Math.Max(0, milliseconds / 1000);
        return $"{seconds / 60:00}:{seconds % 60:00}";
    }

    protected override void OnDetachedFromWindow()
    {
        ReleasePlayer();
        base.OnDetachedFromWindow();
    }

    private void ReleasePlayer()
    {
        if (released) return;
        released = true;
        try { player?.Stop(); } catch { }
        try { player?.Release(); } catch { }
        try { player?.Dispose(); } catch { }
        player = null;
    }
}
