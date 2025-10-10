using DraggerResizer;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Windows.UI;
using Orientation = DraggerResizer.Orientation;

namespace Timeline
{
    public class BaseTimeline
    {
        private readonly TimelineViewModel model;
        private readonly DraggerResizer.DraggerResizer dragger;
        private readonly Canvas canvas;
        protected readonly Canvas progressCanvas;
        private readonly FrameworkElement seeker;
        protected readonly Canvas progressCanvasParent;
        private const double MinimumScale = 5;
        private const double LinesOffset = 0.5;
        private const double Units = 5;
        private const double IncrementScaleBy = 0.5;
        private double scale = MinimumScale;
        private readonly int[] scaleIncrementCounts = [10, 15, 20];
        private readonly int[] labelIntervals = [4, 2, 1];
        private readonly TimeSpan[] spans =
        [
            TimeSpan.FromHours(1),
            TimeSpan.FromMinutes(30),
            TimeSpan.FromMinutes(15),

            TimeSpan.FromMinutes(10),
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(2.5),

            TimeSpan.FromMinutes(2),
            TimeSpan.FromMinutes(1),
            TimeSpan.FromSeconds(30),

            TimeSpan.FromSeconds(20),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(5),

            TimeSpan.FromSeconds(4),
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(1),
        ];
        //e.g scale is incremented by 0.5 (incrementScaleBy) 10 (scaleIncrementCounts) times while the label intervals is 4 (labelIntervals) * 5 units. Then the label intervals
        //change to 2 and the scale now has to increment 20 times before the label interval changes to 1, after which scale has to be incremented by 30 and then it repeats.
        private int currentSpanIndex;
        private int currentLabelPosIndex;
        private double prevTimelineScale;
        private TimeSpan prevProgress;
        private TimeSpan prevDuration;
        //private readonly Color Transparent = Color.FromArgb(255, 255, 0, 255);
        private readonly Color Transparent = Color.FromArgb(0, 255, 255, 255);
        private readonly ScrollingScrollOptions scrollAnimationDisabled = new(ScrollingAnimationMode.Disabled);

        public BaseTimeline(TimelineViewModel model, Canvas canvas)
        {
            this.canvas = canvas;
            this.model = model;
            dragger = new DraggerResizer.DraggerResizer();
            model.PropertyChanged += ModelOnPropertyChanged;

            if(canvas.Children.Count < 1) throw new ArgumentException("Canvas must have at least one child which is the seeker element");
            seeker = (FrameworkElement)canvas.Children[0];
            //var canvasParent = canvas.Parent as FrameworkElement;
            //if(canvasParent == null) throw new ArgumentException("Canvas must have a parent");
            //canvas.Width = canvasParent.ActualWidth;
            canvas.Children.Remove(seeker);
            canvas.Children.Add(new Canvas()); //Ruler canvas
            progressCanvasParent = new Canvas();
            progressCanvas = new Canvas
            {
                Background = new SolidColorBrush(Transparent),
                Height = canvas.ActualHeight
            };
            progressCanvasParent.Children.Add(seeker);
            progressCanvasParent.Children.Add(progressCanvas);
            progressCanvas.Tapped += ProgressCanvasOnTapped;
            canvas.Children.Add(progressCanvasParent);
            seeker.UpdateLayout(); //This sets the ActualWidth of seeker
            dragger.InitDraggerResizer(seeker, [Orientation.Horizontal],
                new HandlingParameters { DontChangeZIndex = true, Boundary = Boundary.BoundedAtCenter },
                new HandlingCallbacks { AfterDragging = r => SeekerDragged(r.Left, r.Width) });
            dragger.SetElementZIndex(seeker, 100);

            var scrollParent = canvas.Parent as ScrollPresenter;
            if (scrollParent != null)
            {
                scrollParent.PointerWheelChanged += ScrollView_OnPointerWheelChanged;
                scrollParent.ViewChanged += ScrollView_OnViewChanged;
            }
        }

        public void ResetScale() => model.TimelineScaleOf100 = GetInitialScale();

        private void ModelOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(TimelineViewModel.TimelineScaleOf100):
                    if (Math.Abs(prevTimelineScale - model.TimelineScaleOf100) > 0.005 && model.Duration > TimeSpan.Zero)
                    {
                        prevTimelineScale = model.TimelineScaleOf100;
                        SetScale(model.TimelineScaleOf100);
                    }
                    break;
                case nameof(TimelineViewModel.Progress):
                    if (model.Progress != prevProgress)
                    {
                        prevProgress = model.Progress;
                        PositionSeeker(prevProgress);
                    }
                    break;
                case nameof(TimelineViewModel.Duration):
                    if (prevDuration != model.Duration && model.Duration > TimeSpan.Zero)
                    {
                        prevDuration = model.Duration;
                        model.TimelineScaleOf100 = GetInitialScale();
                    }
                    break;
            }
        }

        private void ProgressCanvasOnTapped(object sender, TappedRoutedEventArgs e)
        {
            var distance = e.GetPosition(progressCanvas).X;
            PositionSeeker(distance);
            prevProgress = distance / progressCanvas.Width * model.Duration;
            model.Progress = prevProgress;
            //model.Progress = prevProgress = distance / progressCanvas.Width * model.Duration;
        }

        private void ScrollView_OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            var scrollPresenter = (ScrollPresenter)sender;
            var wheelDelta = e.GetCurrentPoint(scrollPresenter).Properties.MouseWheelDelta;
            var scrollAmount = wheelDelta * -1;
            scrollPresenter.ScrollBy(scrollAmount, 0, scrollAnimationDisabled);
            e.Handled = true;
        }

        private void ScrollView_OnViewChanged(ScrollPresenter sender, object args)
        {
            var offset = sender.HorizontalOffset;
            var snappedOffset = Math.Round(offset);
            if (Math.Abs(offset - snappedOffset) > 0.01)
            {
                sender.ScrollTo(snappedOffset, 0, scrollAnimationDisabled);
            }
        }

        private void SeekerDragged(double left, double width)
        {
            var distance = left + width / 2;
            model.Progress = prevProgress = distance / progressCanvas.Width * model.Duration;
        }

        private void PositionSeeker(double distance) => dragger.PositionElementLeft(seeker, distance - seeker.ActualWidth / 2);
        private void PositionSeeker(TimeSpan position) => dragger.PositionElementLeft(seeker, position / model.Duration * progressCanvas.Width - seeker.ActualWidth / 2);

        private double GetInitialScale()
        {
            var availableWidth = ((FrameworkElement)canvas.Parent).ActualWidth;
            var c = 0;
            var unitRanges = scaleIncrementCounts.Select(si =>
            {
                var lastScaleInc = scaleIncrementCounts.Take(c).Sum();
                var first = availableWidth / ((MinimumScale + (IncrementScaleBy * lastScaleInc)) *
                                 labelIntervals[c] * Units);
                var last = availableWidth / ((MinimumScale + (IncrementScaleBy * (si + lastScaleInc))) * labelIntervals[c] * Units);
                c++;
                return (first, last);
            }).ToArray();

            var segments = spans.Length / scaleIncrementCounts.Length;
            var percPerSegment = 1 / (double)segments * 100;
            var percCovered = 0d;
            for (var i = 0; i < spans.Length; i++)
            {
                var span = spans[i];
                var unitRangesIndex = i % unitRanges.Length;
                var unitRange = unitRanges[unitRangesIndex];
                var spanStart = span * unitRange.first;
                var spanEnd = span * unitRange.last;
                if (spanStart >= model.Duration && spanEnd <= model.Duration)
                {
                    var incCount = IncCount(unitRangesIndex, spans[i]);
                    var percRatioRemainder = (double)incCount / scaleIncrementCounts.Sum() * percPerSegment;
                    return percCovered + percRatioRemainder;
                }
                if (spanStart <= model.Duration && spanEnd <= model.Duration)
                {
                    return percCovered - 1d / scaleIncrementCounts.Sum() * percPerSegment;
                }

                if (unitRangesIndex == unitRanges.Length - 1) percCovered += percPerSegment;
            }
            return percCovered - 1d / scaleIncrementCounts.Sum() * percPerSegment;

            int IncCount(int labelPosIndex, TimeSpan span)
            {
                var spanDifference = TimeSpan.MaxValue;
                var start = scaleIncrementCounts.Take(labelPosIndex).Sum();
                var end = start + scaleIncrementCounts[labelPosIndex];
                var labelInt = labelIntervals[labelPosIndex];
                var result = -1;
                for (var i = start; i <= end; i++)
                {
                    var unit = availableWidth / ((MinimumScale + (IncrementScaleBy * i)) * labelInt * Units);
                    var total = unit * span;
                    if((total - model.Duration).Duration() < spanDifference)
                    {
                        spanDifference = (total - model.Duration).Duration();
                        result = i;
                    }
                }
                return result;
            }
        }

        private void SetScale(double percent)
        {
            var segments = spans.Length / scaleIncrementCounts.Length;
            var percPerSegment = 1 / (double)segments * 100;
            var chosenSegment = (int)(percent / percPerSegment);
            var remainder = percent % percPerSegment;
            var chosenIncrementIndex = -1;
            var howManyIncrements = -1;
            var s = 0;
            var sum = scaleIncrementCounts.Sum();
            var lastPercRatio = 0d;
            for (var i = 0; i < scaleIncrementCounts.Length; i++)
            {
                var scaleIncrement = scaleIncrementCounts[i];
                s += scaleIncrement;
                var incRatio = (double)s / sum * percPerSegment;
                if (remainder > incRatio)
                {
                    lastPercRatio = incRatio;
                    continue;
                }

                chosenIncrementIndex = i;
                var equ = incRatio - lastPercRatio;
                howManyIncrements = (int)((remainder - lastPercRatio) / equ * scaleIncrement);
                break;
            }

            scale = MinimumScale + (scaleIncrementCounts.Take(chosenIncrementIndex).Sum() + howManyIncrements) * IncrementScaleBy;
            currentLabelPosIndex = chosenIncrementIndex;
            currentSpanIndex = (chosenSegment * scaleIncrementCounts.Length) + chosenIncrementIndex;
            currentSpanIndex = Math.Min(currentSpanIndex, spans.Length - 1);
            PopulateTimeline();
        }

        private void PopulateTimeline()
        {
            var rulerCanvas = (Canvas)canvas.Children[0];
            rulerCanvas.Children.Clear();
            var currentLabelInt = labelIntervals[currentLabelPosIndex];
            var currentSpan = spans[currentSpanIndex];
            var numOfLabels = Math.Ceiling(model.Duration / currentSpan);
            var numOfLines = numOfLabels * currentLabelInt * Units;
            canvas.Width = numOfLines * scale + LinesOffset + 40;
            var singleSpanWidth = currentLabelInt * Units * scale;
            progressCanvasParent.Width = progressCanvas.Width = model.Duration / currentSpan * singleSpanWidth + LinesOffset;
            progressCanvas.UpdateLayout();
            model.TimelineWidth = progressCanvas.Width;
            PositionSeeker(model.Progress);

            for (var i = 1; i <= numOfLines; i++)
            {
                var line = new Line
                {
                    X1 = Math.Round(i * scale) + LinesOffset,
                    Y1 = 0,
                    Y2 = i % (Units * currentLabelInt) == 0 ? 12 : i % Units == 0 ? 7 : 4
                };
                line.X2 = line.X1;
                line.Stroke = (SolidColorBrush)Application.Current.Resources["TextFillColorPrimaryBrush"];
                rulerCanvas.Children.Add(line);
            }

            for (var i = 1; i <= numOfLabels; i++)
            {
                var pos = i * scale * Units * currentLabelInt;
                var textBlock = new TextBlock
                {
                    Text = (i * currentSpan).ToString(),
                    FontSize = 10,
                    Width = 60,
                    HorizontalTextAlignment = TextAlignment.Center
                };
                rulerCanvas.Children.Add(textBlock);
                Canvas.SetTop(textBlock, 10);
                Canvas.SetLeft(textBlock, pos - textBlock.Width / 2);
            }
        }
    }

    public class TimelineViewModel : INotifyPropertyChanged
    {
        private double _timelinescaleof100;
        public double TimelineScaleOf100
        {
            get => _timelinescaleof100;
            set => SetProperty(ref _timelinescaleof100, value);
        }

        private TimeSpan _progress;
        public TimeSpan Progress
        {
            get => _progress;
            set => SetProperty(ref _progress, value);
        }

        private TimeSpan _duration;
        public TimeSpan Duration
        {
            get => _duration;
            set => SetProperty(ref _duration, value);
        }

        private double _durationwidth;
        public double TimelineWidth
        {
            get => _durationwidth;
            set => SetProperty(ref _durationwidth, value);
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
