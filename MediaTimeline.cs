using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Playback;

namespace Timeline
{
    public class MediaTimeline: BaseTimeline
    {
        private MediaViewModel model;
        private readonly DispatcherQueue dispatcher;
        private readonly StackPanel? scenePreviewPanel;
        private bool inPositionThrottle;
        private bool prevIsPlaying;
        private readonly MediaPlayer mediaPlayer;
        private readonly Process? ffmpegProcess;
        private string? currentPreviewsFolder;
        public const double SpaceForLines = 30;
        private const int frameTime24Fps = 1000 / 24;
        private const double ScenePreviewPanelHeight = 70;
        private TimeSpan prevProgress;
        private double previewImageWidth;
        private readonly string? videoPath;
        private CancellationTokenSource? previewsTokenSource;

        public MediaTimeline(MediaViewModel model, Canvas canvas, MediaPlayer mediaPlayer, string? ffmpegPath = null, string? videoPath = null) : base(model, canvas)
        {
            this.model = model;
            this.mediaPlayer = mediaPlayer;
            dispatcher = canvas.DispatcherQueue;
            PlaybackSessionOnNaturalDurationChanged(mediaPlayer.PlaybackSession, null);
            mediaPlayer.PlaybackSession.NaturalDurationChanged += PlaybackSessionOnNaturalDurationChanged;
            mediaPlayer.PlaybackSession.PlaybackStateChanged += PlaybackSessionOnPlaybackStateChanged;
            mediaPlayer.PlaybackSession.PositionChanged += PlaybackSessionOnPositionChanged;
            model.PropertyChanged += ModelOnPropertyChanged;

            if (!string.IsNullOrWhiteSpace(ffmpegPath) && !string.IsNullOrWhiteSpace(videoPath))
            {
                mediaPlayer.PlaybackSession.NaturalVideoSizeChanged += PlaybackSessionOnNaturalVideoSizeChanged;
                ffmpegProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = ffmpegPath,
                        CreateNoWindow = true,
                        RedirectStandardError = true,
                        RedirectStandardOutput = true,
                    },
                    EnableRaisingEvents = true
                };
                this.videoPath = videoPath;
                previewsTokenSource = new CancellationTokenSource();
                scenePreviewPanel = new StackPanel
                {
                    Height = ScenePreviewPanelHeight,
                    Width = model.TimelineWidth,
                    Orientation = Orientation.Horizontal,
                };
                progressCanvasParent.Children.Insert(0, scenePreviewPanel);
                Canvas.SetTop(scenePreviewPanel, SpaceForLines);
            }
        }

        public async Task Dispose()
        {
            if (previewsTokenSource != null) await previewsTokenSource.CancelAsync();
        }

        private void PlaybackSessionOnNaturalDurationChanged(MediaPlaybackSession sender, object args)
        {
            if (sender.NaturalDuration == TimeSpan.Zero) return;
            dispatcher.TryEnqueue(DispatcherQueuePriority.Normal, () => model.Duration = sender.NaturalDuration);
        }

        private async void PlaybackSessionOnPlaybackStateChanged(MediaPlaybackSession sender, object args)
        {
            dispatcher.TryEnqueue(DispatcherQueuePriority.Normal,
                () => model.IsPlaying = prevIsPlaying = sender.PlaybackState == MediaPlaybackState.Playing);
            if (sender.PlaybackState == MediaPlaybackState.Playing) await AnimateSeeker(sender);
            else if(!CloseToEnd(sender.Position)) dispatcher.TryEnqueue(DispatcherQueuePriority.Normal, () => model.Progress = sender.Position);
        }

        private async void PlaybackSessionOnPositionChanged(MediaPlaybackSession sender, object args)
        {
            if (inPositionThrottle || sender.Position == prevProgress) return;
            inPositionThrottle = true;
            dispatcher.TryEnqueue(DispatcherQueuePriority.Normal, () =>
            {
                model.Progress = prevProgress = sender.Position;
                //Debug.WriteLine($"{sender.Position} / {model.Progress}");
            });
            await Task.Delay(frameTime24Fps * 12);
            inPositionThrottle = false;
        }

        private void PlaybackSessionOnNaturalVideoSizeChanged(MediaPlaybackSession sender, object args)
        {
            if (model.Duration == TimeSpan.Zero) return;
            previewImageWidth = sender.NaturalVideoWidth / (double)sender.NaturalVideoHeight * ScenePreviewPanelHeight;
            dispatcher.TryEnqueue(DispatcherQueuePriority.Normal, () =>
            {
                if (scenePreviewPanel?.Width > 0) _ = SetUpPreviews();
            });
        }

        private void ModelOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(MediaViewModel.Progress):
                    mediaPlayer.PlaybackSession.Position = model.Progress;
                    break;
                case nameof(MediaViewModel.IsPlaying):
                    if (model.IsPlaying != prevIsPlaying)
                    {
                        prevIsPlaying = model.IsPlaying;
                        if (model.IsPlaying)
                        {
                            mediaPlayer.Play();
                        }
                        else
                        {
                            mediaPlayer.Pause();
                        }
                    }
                    break;
                case nameof(MediaViewModel.TimelineWidth):
                    if (scenePreviewPanel != null)
                    {
                        scenePreviewPanel.Width = model.TimelineWidth;
                        if(previewImageWidth != 0) _ = SetUpPreviews();
                    }
                    break;
            }
        }

        private async Task AnimateSeeker(MediaPlaybackSession session)
        {
            while (session.PlaybackState == MediaPlaybackState.Playing && !CloseToEnd(session.Position))
            {
                dispatcher.TryEnqueue(DispatcherQueuePriority.Normal, () =>
                {
                    model.Progress = prevProgress = session.Position;
                    //Debug.WriteLine($"{session.Position}/{model.Progress}/{session.NaturalDuration}");
                });
                await Task.Delay(frameTime24Fps);
            }
        }

        //The reason for this condition is that if the MediaPlayer.Position is set after the media finishes playing, playing again won't start from the beginning
        private bool CloseToEnd(TimeSpan position) => model.Duration - position <= TimeSpan.FromMilliseconds(frameTime24Fps);

        private async Task SetUpPreviews()
        {
            if (previewsTokenSource == null || scenePreviewPanel == null) return;
            await previewsTokenSource.CancelAsync();
            previewsTokenSource = new CancellationTokenSource();
            currentPreviewsFolder = Path.Join(Path.GetTempPath(), Path.GetRandomFileName()) + "/";
            Directory.CreateDirectory(currentPreviewsFolder);
            scenePreviewPanel.Children.Clear();
            var numOfPreviews = scenePreviewPanel.Width / previewImageWidth;
            var previewInterval = 1 / numOfPreviews * model.Duration;
            var currentTimePoint = TimeSpan.Zero;
            var token = previewsTokenSource.Token;
            var previewsFolder = currentPreviewsFolder;
            for (var i = 0; i < numOfPreviews; i++)
            {
                await SetPreviewImage(currentTimePoint, i, previewsFolder, token);
                if (token.IsCancellationRequested) break;
                currentTimePoint += previewInterval;
            }
            await DeletePreviewFolder(previewsFolder, token);
        }

        private async Task SetPreviewImage(TimeSpan previewTimePoint, int index, string outputFolder, CancellationToken token)
        {
            if(scenePreviewPanel == null) return;
            await StartProcess($"-ss {previewTimePoint} -i \"{videoPath}\" -frames:v 1 -vf scale=w=-1:h={ScenePreviewPanelHeight} \"{outputFolder}{index}.png\"", token);
            if (token.IsCancellationRequested) return;
            var image = new Image();
            image.Name = index.ToString();
            image.Source = new BitmapImage(new Uri($"{outputFolder}{index}.png"));
            image.Stretch = Stretch.Uniform;
            scenePreviewPanel.Children.Add(image);
        }

        private static async Task DeletePreviewFolder(string previewFolder, CancellationToken token)
        {
            try
            {
                await Task.Delay(500, token);
            }
            catch(TaskCanceledException){}
            finally
            {
                Directory.Delete(previewFolder, true);
            }
        }

        private async Task StartProcess(string arguments, CancellationToken token)
        {
            var finished = false;
            token.Register(() =>
            {
                if (finished) return;
                ffmpegProcess.CancelErrorRead();
                ffmpegProcess.CancelOutputRead();
                finished = true;
            });
            ffmpegProcess.StartInfo.Arguments = arguments;
            ffmpegProcess.Start();
            ffmpegProcess.BeginErrorReadLine();
            ffmpegProcess.BeginOutputReadLine();
            try
            {
                await ffmpegProcess.WaitForExitAsync(token);
                if (finished) return;
                ffmpegProcess.CancelErrorRead();
                ffmpegProcess.CancelOutputRead();
                finished = true;
            }
            catch (Exception e) { }
        }
    }

    public class MediaViewModel : TimelineViewModel
    {
        private bool _isplaying;
        public bool IsPlaying
        {
            get => _isplaying;
            set => SetProperty(ref _isplaying, value);
        }
    }
}
