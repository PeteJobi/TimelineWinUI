using CommunityToolkit.WinUI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.WindowsRuntime;
using VideoSplitter;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Media.Playback;
using Microsoft.UI.Dispatching;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Timeline
{
    public sealed partial class VideoControls : UserControl
    {
        private VideoControlsViewModel viewModel = new();
        private TimeSpan prevPosition;

        public static readonly DependencyProperty MediaPlayerProperty =
            DependencyProperty.Register(
                nameof(MediaPlayer),
                typeof(MediaPlayer),
                typeof(VideoControls),
                new PropertyMetadata(null, OnMediaPlayerChanged));

        public MediaPlayer? MediaPlayer
        {
            get => (MediaPlayer)GetValue(MediaPlayerProperty);
            set => SetValue(MediaPlayerProperty, value);
        }

        public static readonly DependencyProperty CurrentTimeEditableProperty = DependencyProperty.Register(
            nameof(CurrentTimeEditable),
            typeof(bool),
            typeof(VideoControls),
            new PropertyMetadata(false));

        public bool CurrentTimeEditable
        {
            get => (bool)GetValue(CurrentTimeEditableProperty);
            set => SetValue(CurrentTimeEditableProperty, value);
        }

        public static readonly DependencyProperty ShowFractionalSecondsProperty = DependencyProperty.Register(
            nameof(ShowFractionalSeconds),
            typeof(bool),
            typeof(VideoControls),
            new PropertyMetadata(false, OnShowFractionalSecondsChanged));

        public bool ShowFractionalSeconds
        {
            get => (bool)GetValue(ShowFractionalSecondsProperty);
            set => SetValue(ShowFractionalSecondsProperty, value);
        }

        public static readonly DependencyProperty HideTimesProperty = DependencyProperty.Register(
            nameof(HideTimes),
            typeof(bool),
            typeof(VideoControls),
            new PropertyMetadata(false));
        public bool HideTimes
        {
            get => (bool)GetValue(HideTimesProperty);
            set => SetValue(HideTimesProperty, value);
        }

        public static readonly DependencyProperty HideMuteProperty = DependencyProperty.Register(
            nameof(HideMute),
            typeof(bool),
            typeof(VideoControls),
            new PropertyMetadata(false));
        public bool HideMute
        {
            get => (bool)GetValue(HideMuteProperty);
            set => SetValue(HideMuteProperty, value);
        }

        public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(
            nameof(Spacing),
            typeof(int),
            typeof(VideoControls),
            new PropertyMetadata(4));
        public int Spacing
        {
            get => (int)GetValue(SpacingProperty);
            set => SetValue(SpacingProperty, value);
        }

        public static readonly DependencyProperty BeforePlayProperty = DependencyProperty.Register(
            nameof(BeforePlay),
            typeof(UIElement),
            typeof(VideoControls),
            new PropertyMetadata(null));

        public UIElement BeforePlay
        {
            get => (UIElement)GetValue(BeforePlayProperty);
            set => SetValue(BeforePlayProperty, value);
        }


        public static readonly DependencyProperty BetweenPlayAndProgressProperty = DependencyProperty.Register(
            nameof(BetweenPlayAndProgress),
            typeof(UIElement),
            typeof(VideoControls),
            new PropertyMetadata(null));

        public UIElement BetweenPlayAndProgress
        {
            get => (UIElement)GetValue(BetweenPlayAndProgressProperty);
            set => SetValue(BetweenPlayAndProgressProperty, value);
        }

        public static readonly DependencyProperty BetweenProgressAndTimeProperty = DependencyProperty.Register(
            nameof(BetweenProgressAndTime),
            typeof(UIElement),
            typeof(VideoControls),
            new PropertyMetadata(null));

        public UIElement BetweenProgressAndTime
        {
            get => (UIElement)GetValue(BetweenProgressAndTimeProperty);
            set => SetValue(BetweenProgressAndTimeProperty, value);
        }

        public static readonly DependencyProperty BetweenTimeAndMuteProperty = DependencyProperty.Register(
            nameof(BetweenTimeAndMute),
            typeof(UIElement),
            typeof(VideoControls),
            new PropertyMetadata(null));

        public UIElement BetweenTimeAndMute
        {
            get => (UIElement)GetValue(BetweenTimeAndMuteProperty);
            set => SetValue(BetweenTimeAndMuteProperty, value);
        }

        public static readonly DependencyProperty AfterMuteProperty = DependencyProperty.Register(
            nameof(AfterMute),
            typeof(UIElement),
            typeof(VideoControls),
            new PropertyMetadata(null));

        public UIElement AfterMute
        {
            get => (UIElement)GetValue(AfterMuteProperty);
            set => SetValue(AfterMuteProperty, value);
        }

        public VideoControls()
        {
            InitializeComponent();
        }

        private static void OnMediaPlayerChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var controls = (VideoControls)d;
            var mediaPlayer = e.NewValue as MediaPlayer;
            if (mediaPlayer == null) return;
            mediaPlayer.PlaybackSession.NaturalDurationChanged += PlaybackSessionOnNaturalDurationChanged;
            mediaPlayer.PlaybackSession.PositionChanged += PlaybackSessionPositionChanged;
            mediaPlayer.PlaybackSession.PlaybackStateChanged += PlaybackSessionOnPlaybackStateChanged;
            mediaPlayer.IsMutedChanged += MediaPlayerOnIsMutedChanged;

            void PlaybackSessionOnNaturalDurationChanged(MediaPlaybackSession sender, object args)
            {
                controls.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, () =>
                {
                    controls.VideoProgressSlider.Maximum = sender.NaturalDuration.TotalSeconds;
                    controls.VideoProgressSlider.Value = 0;
                    controls.ProgressValue.Maximum = sender.NaturalDuration;
                    controls.ProgressValue.Value = TimeSpan.Zero;
                    SetTimespanTexts(controls);
                });
            }

            void PlaybackSessionPositionChanged(MediaPlaybackSession sender, object args)
            {
                controls.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, () =>
                {
                    controls.prevPosition = sender.Position;
                    controls.VideoProgressSlider.Value = sender.Position.TotalSeconds;
                    controls.ProgressText.Text = TimeSpanToTextConverter.TimespanToTextFormat(sender.Position, !controls.ShowFractionalSeconds);
                    controls.ProgressValue.Value = sender.Position;
                });
            }

            void PlaybackSessionOnPlaybackStateChanged(MediaPlaybackSession sender, object args)
            {
                controls.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal,
                    () => controls.viewModel.IsPlaying = sender.PlaybackState == MediaPlaybackState.Playing);
            }

            void MediaPlayerOnIsMutedChanged(MediaPlayer sender, object args)
            {
                controls.viewModel.IsMuted = sender.IsMuted;
            }
        }

        private static void OnShowFractionalSecondsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var controls = (VideoControls)d;
            SetTimespanTexts(controls);
        }

        private static void SetTimespanTexts(VideoControls controls)
        {
            var position = controls.MediaPlayer?.PlaybackSession?.Position ?? TimeSpan.Zero;
            var duration = controls.MediaPlayer?.PlaybackSession?.NaturalDuration ?? TimeSpan.Zero;
            controls.ProgressText.Text = TimeSpanToTextConverter.TimespanToTextFormat(position, !controls.ShowFractionalSeconds);
            controls.DurationText.Text = $" / {TimeSpanToTextConverter.TimespanToTextFormat(duration, !controls.ShowFractionalSeconds)}";
        }

        private void PlayPause(object sender, RoutedEventArgs e)
        {
            if (MediaPlayer == null) return;
            if (MediaPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Playing)
            {
                MediaPlayer.Pause();
                viewModel.IsPlaying = false;
            }
            else
            {
                MediaPlayer.Play();
                viewModel.IsPlaying = true;
            }
        }

        private void MuteUnmute(object sender, RoutedEventArgs e)
        {
            if (MediaPlayer == null) return;
            MediaPlayer.IsMuted = viewModel.IsMuted = !MediaPlayer.IsMuted;
        }

        public static DependencyObject? FindChildElementByName(DependencyObject tree, string sName)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(tree); i++)
            {
                var child = VisualTreeHelper.GetChild(tree, i);
                if (child != null && ((FrameworkElement)child).Name == sName)
                    return child;
                var childInSubtree = FindChildElementByName(child, sName);
                if (childInSubtree != null)
                    return childInSubtree;
            }
            return null;
        }

        private void VideoProgressSlider_OnValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (MediaPlayer == null) return;
            if (Math.Abs(e.NewValue - prevPosition.TotalSeconds) < 0.01) return;
            MediaPlayer.PlaybackSession.Position = TimeSpan.FromSeconds(e.NewValue);
        }

        private void ProgressValue_OnValueChanged(TimeSpanTextBox sender, TimeSpan args)
        {
            if (MediaPlayer == null) return;
            if (Math.Abs(args.TotalSeconds - prevPosition.TotalSeconds) < 0.01) return;
            MediaPlayer.PlaybackSession.Position = args;
        }
    }

    class VideoControlsViewModel : INotifyPropertyChanged
    {
        private bool _isPlaying;
        public bool IsPlaying
        {
            get => _isPlaying;
            set => SetProperty(ref _isPlaying, value);
        }

        private bool _isMuted;
        public bool IsMuted
        {
            get => _isMuted;
            set => SetProperty(ref _isMuted, value);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null, params string[] alsoNotify)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            foreach (var dep in alsoNotify) OnPropertyChanged(dep);
            return true;
        }
    }
}
