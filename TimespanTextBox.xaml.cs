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
using Windows.Foundation;
using Windows.Foundation.Collections;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Timeline
{
    public sealed partial class TimeSpanTextBox : UserControl
    {
        public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
            nameof(Minimum),
            typeof(TimeSpan),
            typeof(TimeSpanTextBox),
            new PropertyMetadata(TimeSpan.Zero, OnMinimumChanged));
        public TimeSpan Minimum
        {
            get => (TimeSpan)GetValue(MinimumProperty);
            set => SetValue(MinimumProperty, value);
        }

        public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
            nameof(Maximum),
            typeof(TimeSpan),
            typeof(TimeSpanTextBox),
            new PropertyMetadata(TimeSpan.MaxValue, OnMaximumChanged));
        public TimeSpan Maximum
        {
            get => (TimeSpan)GetValue(MaximumProperty);
            set => SetValue(MaximumProperty, value);
        }

        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
            nameof(Value),
            typeof(TimeSpan),
            typeof(TimeSpanTextBox),
            new PropertyMetadata(TimeSpan.Zero, OnValueChanged));
        public TimeSpan Value
        {
            get => (TimeSpan)GetValue(ValueProperty);
            set => SetValueWithTextBoxCheck(ValueProperty, value);
        }
        public event TypedEventHandler<TimeSpanTextBox, TimeSpan> ValueChanged;
        private bool valueIsZero;

        public static readonly DependencyProperty IgnoreMaximumIfZeroProperty = DependencyProperty.Register(
            nameof(IgnoreMaximumIfZero),
            typeof(bool),
            typeof(VideoControls),
            new PropertyMetadata(false));
        public bool IgnoreMaximumIfZero
        {
            get => (bool)GetValue(IgnoreMaximumIfZeroProperty);
            set => SetValue(IgnoreMaximumIfZeroProperty, value);
        }

        public static readonly DependencyProperty DontShowFractionalSecondsProperty = DependencyProperty.Register(
            nameof(DontShowFractionalSeconds),
            typeof(bool),
            typeof(VideoControls),
            new PropertyMetadata(false, OnDontShowFractionalSecondsChanged));
        public bool DontShowFractionalSeconds
        {
            get => (bool)GetValue(DontShowFractionalSecondsProperty);
            set => SetValue(DontShowFractionalSecondsProperty, value);
        }

        public TimeSpanTextBox()
        {
            InitializeComponent();
        }

        private void Text_OnLoaded(object sender, RoutedEventArgs e)
        {
            var textBox = (TextBox)sender;
            var delButton = FindChildElementByName(textBox, "DeleteButton") as Control;
            if (delButton == null) return;
            var parentGrid = delButton.Parent;
            if (parentGrid != null)
            {
                ((Grid)parentGrid).Children.Remove(delButton);
            }
            SetMask(textBox, this);
        }

        private static void OnMinimumChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var textBox = (TimeSpanTextBox)d;
            if (textBox.Value < textBox.Minimum) textBox.Value = textBox.Minimum;
            //e has e.NewValue and e.OldValue
        }

        private static void OnMaximumChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var textBox = (TimeSpanTextBox)d;
            if (textBox.Value > textBox.Maximum && (textBox.Maximum > TimeSpan.Zero || !textBox.IgnoreMaximumIfZero)) textBox.Value = textBox.Maximum;
        }

        private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var textBox = (TimeSpanTextBox)d;
            //Debug.WriteLine($"VC {textBox.Name} {textBox.Value}");
            if (textBox.Value < textBox.Minimum) textBox.Value = textBox.Minimum;
            else if (textBox.Value > textBox.Maximum && (textBox.Maximum > TimeSpan.Zero || !textBox.IgnoreMaximumIfZero)) textBox.Value = textBox.Maximum;
            textBox.ValueChanged?.Invoke(textBox, textBox.Value);
        }

        private static void OnDontShowFractionalSecondsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var timespanTextBox = (TimeSpanTextBox)d;
            var textBox = (TextBox)VisualTreeHelper.GetChild(d, 0);
            SetMask(textBox, timespanTextBox);
        }

        private static void SetMask(TextBox textBox, TimeSpanTextBox timeSpanTextBox)
        {
            TextBoxExtensions.SetMask(textBox, timeSpanTextBox.DontShowFractionalSeconds ? "99:59:99" : "99:59:59.999");
            textBox.Text = TimeSpanToTextConverter.TimespanToTextFormat(timeSpanTextBox.Value, timeSpanTextBox.DontShowFractionalSeconds);
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

        private void SetValueWithTextBoxCheck(DependencyProperty dp, object value)
        {
            //This is done because if the value is currently TimeSpan.Zero, and the user types in an invalid TimeSpan,
            //the value remains TimeSpan.Zero, but the TextBox.Text still contains the invalid TimeSpan text
            if (!valueIsZero && Value == TimeSpan.Zero && (TimeSpan)value == TimeSpan.Zero)
            {
                var textBox = (TextBox)VisualTreeHelper.GetChild(this, 0);
                textBox.Text = TimeSpanToTextConverter.TimespanToTextFormat(TimeSpan.Zero, DontShowFractionalSeconds);
                valueIsZero = true;
            }else valueIsZero = false;
            SetValue(dp, value);
        }
    }

    public class TimeSpanToTextConverter : IValueConverter
    {
        public bool DontShowFractionalSeconds { get; set; }

        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is TimeSpan timeSpan)
            {
                return TimespanToTextFormat(timeSpan, DontShowFractionalSeconds);
            }

            return string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            if (value is string str && TimeSpan.TryParse(str, out var timeSpan))
            {
                return timeSpan;
            }

            return TimeSpan.Zero;
        }

        public static string TimespanToTextFormat(TimeSpan timeSpan, bool dontShowFractionalSeconds = false)
        {
            var format = dontShowFractionalSeconds ? @"hh\:mm\:ss" : @"hh\:mm\:ss\.fff";
            return timeSpan.ToString(format);
        }
    }
}
