using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace ReplyOrbs {
    // Only the small handle is a hit target at rest. The shelf is removed from layout.
    internal sealed class ReplyDock : Grid, IDisposable {
        readonly Window window;
        readonly FrameworkElement shelf;
        readonly Border idle;
        readonly ScaleTransform scale = new ScaleTransform(.88, .88);
        readonly TranslateTransform offset = new TranslateTransform(0, 8);
        readonly DispatcherTimer delay, finish;
        readonly Dictionary<Window, EventHandler> pins = new Dictionary<Window, EventHandler>();
        readonly Func<bool> motion;
        bool disposed, hovered, changing, positioned, moving;
        double center, bottom, moveCenter, moveBottom, moveLeft;
        internal bool Expanded { get; private set; }
        internal bool IsIdle { get { return shelf.Visibility == Visibility.Collapsed; } }
        internal bool PendingCollapse { get { return delay.IsEnabled || finish.IsEnabled; } }
        internal FrameworkElement Handle { get { return idle; } }
        internal int PinCount { get { return pins.Count; } }
        internal ReplyDock(Window window, FrameworkElement shelf, Func<bool> motion = null) {
            this.window = window; this.shelf = shelf; this.motion = motion ?? (delegate { return Ui.Motion; });
            Background = Brushes.Transparent;
            idle = new Border { Width = 72, Height = 25, Background = Ui.HoverBridge, Child = new Border { Width = 56, Height = 9, Background = Ui.Brush("#1A1A1A"), BorderBrush = Ui.Brush("#777771"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4.5) } };
            System.Windows.Automation.AutomationProperties.SetName(idle, "Развернуть готовые ответы");
            var transform = new TransformGroup(); transform.Children.Add(scale); transform.Children.Add(offset);
            shelf.RenderTransform = transform; shelf.RenderTransformOrigin = new Point(.5, 1); shelf.Opacity = 0; shelf.Visibility = Visibility.Collapsed;
            Children.Add(shelf); Children.Add(idle);
            delay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
            finish = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(140) };
            delay.Tick += DelayTick; finish.Tick += FinishTick;
            MouseEnter += Enter; MouseLeave += Leave;
            GotKeyboardFocus += KeyboardEnter; LostKeyboardFocus += KeyboardLeave;
            window.SizeChanged += WindowSizeChanged;
            window.Deactivated += Deactivated;
            SystemParameters.StaticPropertyChanged += MotionChanged;
        }
        internal void PositionReady() { positioned = true; center = window.Left + idle.Width / 2; bottom = window.Top + idle.Height; }
        internal void Moved() { center = window.Left + window.ActualWidth / 2; bottom = window.Top + window.ActualHeight; }
        internal void BeginMove() { moving = true; Expand(); moveCenter = center; moveBottom = bottom; moveLeft = window.Left; }
        internal void MoveTo(Point delta) { center = moveCenter + delta.X; bottom = moveBottom + delta.Y; window.Left = moveLeft + delta.X; window.Top = bottom - window.ActualHeight; }
        internal void EndMove() { moving = false; Ui.Clamp(window); Reconsider(); }
        internal Point RestPosition { get { return new Point(center - 36, bottom - 25); } }
        void WindowSizeChanged(object sender, SizeChangedEventArgs e) {
            if (!positioned || changing || disposed) return;
            // Keep the left edge stable while arrow/plus widths change; preserve the baseline.
            window.Top = bottom - window.ActualHeight;
            if (IsIdle) window.Left = center - window.ActualWidth / 2;
            Ui.Clamp(window);
        }
        void Enter(object sender, MouseEventArgs e) { hovered = true; Expand(); }
        void Leave(object sender, MouseEventArgs e) { hovered = false; Reconsider(); }
        void KeyboardEnter(object sender, KeyboardFocusChangedEventArgs e) { Expand(); }
        void KeyboardLeave(object sender, KeyboardFocusChangedEventArgs e) { Reconsider(); }
        void Deactivated(object sender, EventArgs e) { Reconsider(); }
        bool Held { get { return moving || hovered || IsMouseOver || window.IsActive && IsKeyboardFocusWithin || pins.Count > 0; } }
        internal void Expand() {
            if (disposed) return;
            delay.Stop(); finish.Stop();
            if (Expanded) return;
            if (IsIdle && positioned) Moved();
            Expanded = true;
            changing = true; idle.Visibility = Visibility.Collapsed; shelf.Visibility = Visibility.Visible;
            window.UpdateLayout();
            if (positioned) { window.Left = center - window.ActualWidth / 2; window.Top = bottom - window.ActualHeight; Ui.Clamp(window); }
            changing = false;
            Animate(true);
        }
        internal void Reconsider() {
            if (disposed) return;
            if (Held) { Expand(); return; }
            if (!IsIdle && !delay.IsEnabled && !finish.IsEnabled) delay.Start();
        }
        void DelayTick(object sender, EventArgs e) {
            delay.Stop(); if (disposed || Held) return;
            Expanded = false; Animate(false);
            if (motion()) finish.Start(); else FinishIdle();
        }
        void FinishTick(object sender, EventArgs e) { finish.Stop(); if (Held) Expand(); else FinishIdle(); }
        void FinishIdle() {
            StopAnimations(); Expanded = false; changing = true;
            shelf.Visibility = Visibility.Collapsed; idle.Visibility = Visibility.Visible;
            window.UpdateLayout();
            if (positioned) { window.Left = center - window.ActualWidth / 2; window.Top = bottom - window.ActualHeight; Ui.Clamp(window); }
            changing = false;
        }
        void Animate(bool open) {
            var opacity = shelf.Opacity; var fromScale = scale.ScaleX; var fromY = offset.Y;
            StopAnimations(); shelf.Opacity = open ? 1 : 0; scale.ScaleX = scale.ScaleY = open ? 1 : .88; offset.Y = open ? 0 : 8;
            if (!motion()) return;
            Ui.Start(shelf, OpacityProperty, new DoubleAnimation(opacity, shelf.Opacity, TimeSpan.FromMilliseconds(140)) { FillBehavior = FillBehavior.Stop });
            var spring = new DoubleAnimation(fromScale, scale.ScaleX, TimeSpan.FromMilliseconds(330)) { FillBehavior = FillBehavior.Stop, EasingFunction = new BackEase { Amplitude = .18, EasingMode = EasingMode.EaseOut } };
            Ui.Start(scale, ScaleTransform.ScaleXProperty, spring); Ui.Start(scale, ScaleTransform.ScaleYProperty, spring);
            Ui.Start(offset, TranslateTransform.YProperty, new DoubleAnimation(fromY, offset.Y, TimeSpan.FromMilliseconds(330)) { FillBehavior = FillBehavior.Stop, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
        void StopAnimations() { shelf.BeginAnimation(OpacityProperty, null); scale.BeginAnimation(ScaleTransform.ScaleXProperty, null); scale.BeginAnimation(ScaleTransform.ScaleYProperty, null); offset.BeginAnimation(TranslateTransform.YProperty, null); }
        void MotionChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e) { if (e.PropertyName == "ClientAreaAnimation") { StopAnimations(); shelf.Opacity = Expanded ? 1 : 0; scale.ScaleX = scale.ScaleY = Expanded ? 1 : .88; offset.Y = Expanded ? 0 : 8; if (!Expanded && !IsIdle) FinishIdle(); } }
        internal void Pin(Window child) {
            if (disposed || pins.ContainsKey(child)) return;
            EventHandler closed = null;
            closed = delegate { child.Closed -= closed; pins.Remove(child); if (!disposed) Dispatcher.BeginInvoke(new Action(Reconsider), DispatcherPriority.Background); };
            pins.Add(child, closed); child.Closed += closed; Expand();
        }
        public void Dispose() {
            if (disposed) return; disposed = true; delay.Stop(); finish.Stop(); StopAnimations();
            delay.Tick -= DelayTick; finish.Tick -= FinishTick; MouseEnter -= Enter; MouseLeave -= Leave;
            GotKeyboardFocus -= KeyboardEnter; LostKeyboardFocus -= KeyboardLeave;
            window.SizeChanged -= WindowSizeChanged; SystemParameters.StaticPropertyChanged -= MotionChanged;
            window.Deactivated -= Deactivated;
            foreach (var pair in pins) pair.Key.Closed -= pair.Value; pins.Clear();
        }
    }
}
