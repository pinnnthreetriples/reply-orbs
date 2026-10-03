using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Path = System.Windows.Shapes.Path;

namespace ReplyOrbs {
    public static class Ui {
        public static bool Qa;
        public static readonly Brush Ink = Brush("#F0F0EE"), Muted = Brush("#A1A19E"), Cream = Brush("#FFFFE9"), Green = Brush("#78DC9D");
        // Layered Windows hit-testing ignores fully transparent pixels. This invisible bridge
        // keeps hover alive while the pointer crosses the intentional 6/8 px gaps.
        public static readonly Brush HoverBridge = new SolidColorBrush(Color.FromArgb(1, 25, 25, 25));
        public static bool Motion { get { return SystemParameters.ClientAreaAnimation; } }
        public static Brush Brush(string value) { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(value)); }
        public static void Install(Application app) {
            app.Resources = (ResourceDictionary)XamlReader.Parse(@"<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
              <Style TargetType='TextBox'><Setter Property='Foreground' Value='#F0F0EE'/><Setter Property='Background' Value='#232323'/><Setter Property='CaretBrush' Value='#FFFFE9'/><Setter Property='FontSize' Value='14'/><Setter Property='Padding' Value='12,10'/><Setter Property='BorderBrush' Value='#303030'/><Setter Property='BorderThickness' Value='1'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='TextBox'><Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='11'><ScrollViewer x:Name='PART_ContentHost' Margin='{TemplateBinding Padding}'/></Border></ControlTemplate></Setter.Value></Setter><Style.Triggers><Trigger Property='IsKeyboardFocused' Value='True'><Setter Property='BorderBrush' Value='#79796B'/></Trigger></Style.Triggers></Style>
              <Style TargetType='PasswordBox'><Setter Property='Foreground' Value='#F0F0EE'/><Setter Property='Background' Value='#232323'/><Setter Property='CaretBrush' Value='#FFFFE9'/><Setter Property='FontSize' Value='14'/><Setter Property='Padding' Value='12,10'/><Setter Property='BorderBrush' Value='#303030'/><Setter Property='BorderThickness' Value='1'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='PasswordBox'><Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='11'><ScrollViewer x:Name='PART_ContentHost' Margin='{TemplateBinding Padding}'/></Border></ControlTemplate></Setter.Value></Setter></Style>
              <Style TargetType='Button'><Setter Property='Foreground' Value='#F0F0EE'/><Setter Property='Background' Value='Transparent'/><Setter Property='BorderBrush' Value='Transparent'/><Setter Property='Padding' Value='10,8'/><Setter Property='Cursor' Value='Hand'/><Setter Property='FontSize' Value='12'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'><Border x:Name='shell' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' CornerRadius='9'><ContentPresenter Margin='{TemplateBinding Padding}' HorizontalAlignment='Center' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='shell' Property='Opacity' Value='0.82'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='shell' Property='BorderBrush' Value='#8C8C78'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter TargetName='shell' Property='Opacity' Value='0.45'/></Trigger><Trigger Property='IsPressed' Value='True'><Setter TargetName='shell' Property='Opacity' Value='0.6'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
              <Style x:Key='Round' TargetType='Button' BasedOn='{StaticResource {x:Type Button}}'><Setter Property='Padding' Value='0'/><Setter Property='BorderThickness' Value='0'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'><Border x:Name='shell' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='99'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='shell' Property='BorderBrush' Value='#8C8C78'/><Setter TargetName='shell' Property='BorderThickness' Value='1'/></Trigger><Trigger Property='IsPressed' Value='True'><Setter TargetName='shell' Property='Opacity' Value='0.7'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
              <Style TargetType='ToolTip'><Setter Property='Foreground' Value='#F0F0EE'/><Setter Property='Background' Value='#090909'/><Setter Property='BorderBrush' Value='#353535'/><Setter Property='Padding' Value='9,7'/></Style>
            </ResourceDictionary>");
        }
        static readonly Dictionary<string, string> Paths = new Dictionary<string, string> {
            {"message", "M3,4 L21,4 21,18 9,18 4,22 4,18 3,18 Z M7,8 L17,8 M7,12 L14,12"},
            {"ticket", "M3,5 L21,5 21,9 C16,9 16,15 21,15 L21,19 3,19 3,15 C8,15 8,9 3,9 Z M14,5 L14,8 M14,11 L14,13 M14,16 L14,19"},
            {"calendar", "M3,5 L21,5 21,20 3,20 Z M7,2 L7,8 M17,2 L17,8 M3,10 L21,10 M13,13 L13,17 17,17"},
            {"card", "M3,5 L21,5 21,19 3,19 Z M3,10 L21,10 M6,15 L10,15"},
            {"pin", "M12,22 C9,18 5,14 5,9 C5,0 19,0 19,9 C19,14 15,18 12,22 Z M15,9 A3,3 0 1 1 9,9 A3,3 0 1 1 15,9"},
            {"star", "M12,2 L15,8 22,9 17,14 18,21 12,18 6,21 7,14 2,9 9,8 Z"},
            {"heart", "M12,21 C-5,10 4,-2 12,7 C20,-2 29,10 12,21 Z"},
            {"smile", "M22,12 A10,10 0 1 1 2,12 A10,10 0 1 1 22,12 M7,9 L7.1,9 M17,9 L17.1,9 M7,14 Q12,20 17,14"},
            {"sparkles", "M11,3 L13,9 19,11 13,13 11,19 9,13 3,11 9,9 Z M20,2 L20,6 M18,4 L22,4 M20,17 L20,21 M18,19 L22,19"},
            {"zap", "M13,2 L4,14 11,14 10,22 20,10 13,10 Z"},
            {"check", "M4,12 L9,17 20,6"},
            {"help", "M22,12 A10,10 0 1 1 2,12 A10,10 0 1 1 22,12 M9,8 C9,4 17,5 15,10 L12,13 12,14 M12,18 L12.1,18"},
            {"info", "M22,12 A10,10 0 1 1 2,12 A10,10 0 1 1 22,12 M12,10 L12,17 M12,6 L12.1,6"},
            {"bell", "M5,17 L19,17 17,14 17,8 C17,1 7,1 7,8 L7,14 Z M9,20 Q12,24 15,20"},
            {"phone", "M7,2 L3,3 C-2,15 13,28 21,21 L22,17 16,14 13,17 C9,15 7,13 6,9 L9,7 Z"},
            {"mail", "M3,4 L21,4 21,20 3,20 Z M3,5 L12,12 21,5"},
            {"user", "M16,7 A4,4 0 1 1 8,7 A4,4 0 1 1 16,7 M4,21 L4,18 C4,11 20,11 20,18 L20,21"},
            {"users", "M14,7 A4,4 0 1 1 6,7 A4,4 0 1 1 14,7 M2,21 L2,18 C2,11 18,11 18,18 L18,21 M18,3 C23,3 23,11 18,11 M20,15 Q24,15 23,21"},
            {"file", "M5,2 L14,2 20,8 20,22 5,22 Z M14,2 L14,8 20,8 M8,13 L17,13 M8,17 L15,17"},
            {"image", "M3,3 L21,3 21,21 3,21 Z M3,17 L9,11 13,15 17,11 21,15 M9,7 A1,1 0 1 1 7,7 A1,1 0 1 1 9,7"},
            {"clip", "M8,15 L17,6 C21,2 25,6 21,10 L10,21 C4,26 -2,20 4,14 L14,4 C17,1 21,4 18,7 L7,18 C5,20 3,18 5,16 L15,6"},
            {"gift", "M3,8 L21,8 21,12 3,12 Z M5,12 L5,22 19,22 19,12 M12,8 L12,22 M12,8 C-2,9 5,-5 12,8 C26,-5 27,9 12,8"},
            {"home", "M2,11 L12,2 22,11 M5,9 L5,22 19,22 19,9 M9,22 L9,14 15,14 15,22"},
            {"briefcase", "M3,7 L21,7 21,21 3,21 Z M8,7 L8,3 16,3 16,7 M3,12 Q12,18 21,12 M11,13 L13,13"},
            {"plus", "M12,4 L12,20 M4,12 L20,12"}, {"up", "M6,15 L12,9 18,15"}, {"down", "M6,9 L12,15 18,9"},
            {"close", "M6,6 L18,18 M6,18 L18,6"}, {"pencil", "M4,20 L5,14 16,3 21,8 10,19 Z M14,5 L19,10"},
            {"folder", "M2,6 L10,6 12,9 22,9 20,21 2,21 Z M2,16 L2,3 10,3 13,6 20,6 20,9"},
            {"key", "M13,8 A5,5 0 1 1 3,8 A5,5 0 1 1 13,8 M12,12 L22,22 M16,16 L19,13 M19,19 L22,16"},
            {"settings", "M3,6 L21,6 M3,12 L21,12 M3,18 L21,18 M7,3 L7,9 M16,9 L16,15 M10,15 L10,21"},
            {"trash", "M3,6 L21,6 M8,6 L8,3 16,3 16,6 M5,6 L6,22 18,22 19,6 M10,10 L10,18 M14,10 L14,18"}
        };
        public static readonly string[] IconKeys = { "message", "ticket", "calendar", "card", "pin", "star", "heart", "smile", "sparkles", "zap", "check", "help", "info", "bell", "phone", "mail", "user", "users", "file", "image", "clip", "gift", "home", "briefcase" };
        public static readonly string[] IconNames = { "Ответ", "Абонемент", "Расписание", "Оплата", "Адрес", "Звезда", "Сердце", "Улыбка", "Искры", "Молния", "Галочка", "Вопрос", "Информация", "Уведомление", "Телефон", "Почта", "Клиент", "Группа", "Документ", "Картинка", "Вложение", "Подарок", "Дом", "Работа" };
        public static FrameworkElement Icon(string name, Brush color = null, double size = 19) {
            string data; if (!Paths.TryGetValue(name ?? "", out data)) data = Paths["message"];
            var path = new Path { Data = Geometry.Parse(data), Stroke = color ?? Ink, StrokeThickness = 1.65, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round };
            return new Viewbox { Width = size, Height = size, Child = new Canvas { Width = 24, Height = 24, Children = { path } }, IsHitTestVisible = false };
        }
        public static TextBlock Text(string text, double size = 13, Brush color = null) { return new TextBlock { Text = text, FontSize = size, Foreground = color ?? Ink, TextWrapping = TextWrapping.Wrap }; }
        public static Button Button(string label, Action click, bool primary = false) {
            var b = new Button { Content = label, MinHeight = 37, Background = primary ? Cream : Brushes.Transparent, Foreground = primary ? Brush("#171717") : Muted };
            AutomationProperties.SetName(b, label); b.Click += delegate { click(); }; return b;
        }
        public static Button IconButton(string icon, string label, Action click, double size = 30, Brush background = null) {
            var b = new Button { Content = Icon(icon), Width = size, Height = size, Style = (Style)Application.Current.Resources["Round"], Background = background ?? Brushes.Transparent, ToolTip = label };
            AutomationProperties.SetName(b, label); b.Click += delegate { click(); }; return b;
        }
        public static Button Dots(bool vertical, string label, Action click) {
            var text = new StackPanel { Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            for (int i = 0; i < 3; i++) text.Children.Add(new System.Windows.Shapes.Ellipse { Width = 2.4, Height = 2.4, Fill = Muted, Margin = vertical ? new Thickness(0, i == 0 ? 0 : 3, 0, 0) : new Thickness(i == 0 ? 0 : 3, 0, 0, 0) });
            text.RenderTransformOrigin = new Point(.5, .5); var scale = new ScaleTransform(); text.RenderTransform = scale;
            var b = new Button { Content = text, Width = vertical ? 26 : 30, Height = 34, Padding = new Thickness(0), ToolTip = label, Background = Brushes.Transparent, Style = (Style)Application.Current.Resources["Round"] };
            AutomationProperties.SetName(b, label);
            b.MouseEnter += delegate { foreach (System.Windows.Shapes.Ellipse dot in text.Children) dot.Fill = Cream; Animate(scale, ScaleTransform.ScaleXProperty, 1.12, 160); Animate(scale, ScaleTransform.ScaleYProperty, 1.12, 160); };
            b.MouseLeave += delegate { foreach (System.Windows.Shapes.Ellipse dot in text.Children) dot.Fill = Muted; Animate(scale, ScaleTransform.ScaleXProperty, 1, 160); Animate(scale, ScaleTransform.ScaleYProperty, 1, 160); };
            b.Click += delegate { click(); }; return b;
        }
        public static void Animate(Animatable target, DependencyProperty property, double value, int ms = 320, bool spring = true) {
            var from = (double)target.GetValue(property);
            target.SetValue(property, value);
            if (!Motion) { target.BeginAnimation(property, null); return; }
            var animation = new DoubleAnimation(from, value, TimeSpan.FromMilliseconds(ms)) { FillBehavior = FillBehavior.Stop, EasingFunction = spring ? (IEasingFunction)new BackEase { Amplitude = .25, EasingMode = EasingMode.EaseOut } : new CubicEase { EasingMode = EasingMode.EaseOut } };
            Start(target, property, animation);
        }
        // Initialize the replacement clock at time zero before the next layout/render.
        // Otherwise WPF briefly exposes the target base value between two clocks.
        internal static void Start(Animatable target, DependencyProperty property, DoubleAnimation animation) {
            var clock = (AnimationClock)animation.CreateClock(true); clock.Completed += delegate { target.ApplyAnimationClock(property, null); }; target.ApplyAnimationClock(property, clock);
            clock.Controller.SeekAlignedToLastTick(TimeSpan.Zero, TimeSeekOrigin.BeginTime);
        }
        internal static void Start(UIElement target, DependencyProperty property, DoubleAnimation animation) {
            var clock = (AnimationClock)animation.CreateClock(true); clock.Completed += delegate { target.ApplyAnimationClock(property, null); }; target.ApplyAnimationClock(property, clock);
            clock.Controller.SeekAlignedToLastTick(TimeSpan.Zero, TimeSeekOrigin.BeginTime);
        }
        public static void Width(FrameworkElement element, double width, bool? motion = null) {
            var from = double.IsNaN(element.Width) ? element.ActualWidth : element.Width; element.BeginAnimation(FrameworkElement.WidthProperty, null); element.Width = width;
            if (motion ?? Motion) Start(element, FrameworkElement.WidthProperty, new DoubleAnimation(from, width, TimeSpan.FromMilliseconds(350)) { FillBehavior = FillBehavior.Stop, EasingFunction = width > from ? (IEasingFunction)new BackEase { Amplitude = .18, EasingMode = EasingMode.EaseOut } : new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
        public static void PrepareReveal(Window window) {
            window.Opacity = Motion ? 0 : 1;
            var root = window.Content as FrameworkElement;
            if (root == null) return;
            root.RenderTransformOrigin = new Point(.5, 1); root.RenderTransform = new ScaleTransform(Motion ? .96 : 1, Motion ? .96 : 1);
        }
        public static void Reveal(Window window) {
            var from = window.Opacity; window.Opacity = 1;
            if (!Motion) { window.BeginAnimation(System.Windows.Window.OpacityProperty, null); return; }
            Start(window, System.Windows.Window.OpacityProperty, new DoubleAnimation(from, 1, TimeSpan.FromMilliseconds(140)) { FillBehavior = FillBehavior.Stop });
            var root = window.Content as FrameworkElement; var transform = root == null ? null : root.RenderTransform as ScaleTransform;
            if (transform != null) { Animate(transform, ScaleTransform.ScaleXProperty, 1, 320); Animate(transform, ScaleTransform.ScaleYProperty, 1, 320); }
        }
        public static Window Window(string title, double width, bool activate = true) {
            return new Window { Title = title, Width = width, SizeToContent = SizeToContent.Height, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, AllowsTransparency = true, Background = Brushes.Transparent, Topmost = true, ShowInTaskbar = Qa, ShowActivated = activate, FontFamily = new FontFamily("Segoe UI"), Foreground = Ink, FontSize = 13 };
        }
        public static StackPanel Body(Window window) {
            var stack = new StackPanel();
            var scroll = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, MaxHeight = Math.Max(180, SystemParameters.WorkArea.Height - 90) };
            window.Content = new Border { Background = Brush("#191919"), BorderBrush = Brush("#333333"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(24), Padding = new Thickness(22), Child = scroll };
            window.PreviewKeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) { window.Close(); e.Handled = true; } };
            return stack;
        }
        public static Grid Header(Window w, string title, StackPanel body) {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 20) };
            grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var t = Text(title, 15); t.VerticalAlignment = VerticalAlignment.Center; t.MouseLeftButtonDown += delegate { w.DragMove(); }; grid.Children.Add(t);
            var close = IconButton("close", "Закрыть", w.Close); Grid.SetColumn(close, 1); grid.Children.Add(close); body.Children.Add(grid); return grid;
        }
        public static void Field(StackPanel body, string title, FrameworkElement control) {
            var label = Text(title, 12, Muted); label.Margin = new Thickness(0, 0, 0, 8); body.Children.Add(label);
            control.Margin = new Thickness(0, 0, 0, 17); body.Children.Add(control); AutomationProperties.SetName(control, title);
        }
        public static StackPanel Footer(StackPanel body) { var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) }; body.Children.Add(footer); return footer; }
        public static void Place(Window w, Window anchor) {
            PrepareReveal(w); w.Show(); w.UpdateLayout();
            Native.Rectangle target, parent;
            var handle = new WindowInteropHelper(w).Handle;
            Native.GetWindowRect(handle, out target); Native.GetWindowRect(new WindowInteropHelper(anchor).Handle, out parent);
            Native.SetWindowPos(handle, IntPtr.Zero, (parent.Left + parent.Right - target.Right + target.Left) / 2, parent.Top - (target.Bottom - target.Top) - 14, 0, 0, 0x15);
            Clamp(w); if (w.ShowActivated) w.Activate(); Reveal(w);
        }
        public static void Clamp(Window w) {
            var source = PresentationSource.FromVisual(w); var sx = source == null ? 1 : source.CompositionTarget.TransformToDevice.M11; var sy = source == null ? 1 : source.CompositionTarget.TransformToDevice.M22;
            var handle = new WindowInteropHelper(w).Handle; Native.Rectangle rect;
            if (handle == IntPtr.Zero || !Native.GetWindowRect(handle, out rect)) return;
            var screen = System.Windows.Forms.Screen.FromHandle(handle).WorkingArea;
            w.MaxWidth = Math.Max(150, (screen.Width - 16) / sx); w.MaxHeight = Math.Max(100, (screen.Height - 16) / sy);
            var left = Math.Max(screen.Left + 8, Math.Min(rect.Left, screen.Right - (rect.Right - rect.Left) - 8));
            var top = Math.Max(screen.Top + 8, Math.Min(rect.Top, screen.Bottom - (rect.Bottom - rect.Top) - 8));
            Native.SetWindowPos(handle, IntPtr.Zero, left, top, 0, 0, 0x15);
        }
        public static void NoActivate(Window window, bool toolWindow = true) {
            window.SourceInitialized += delegate {
                var handle = new WindowInteropHelper(window).Handle;
                Native.SetWindowLong(handle, -20, Native.GetWindowLong(handle, -20) | 0x08000000 | (toolWindow ? 0x80 : 0));
                HwndSource.FromHwnd(handle).AddHook(delegate(IntPtr h, int msg, IntPtr wp, IntPtr lp, ref bool handled) { if (msg == 0x21) { handled = true; return new IntPtr(3); } return IntPtr.Zero; });
            };
        }
    }
    internal sealed class GenerationVisual : Grid, IDisposable {
        readonly List<Action> stops = new List<Action>();
        readonly List<Animatable> transforms = new List<Animatable>();
        readonly List<UIElement> elements = new List<UIElement>();
        internal string Phase { get; private set; }
        internal int BarCount { get; private set; }
        internal int RingCount { get; private set; }
        internal bool HasAnimations { get { return transforms.Any(t => t.HasAnimatedProperties) || elements.Any(e => e.HasAnimatedProperties); } }
        public GenerationVisual() {
            Width = Height = 38; IsHitTestVisible = false; ClipToBounds = false;
            Unloaded += delegate { Stop(); };
            SystemParameters.StaticPropertyChanged += MotionChanged;
        }
        void MotionChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e) {
            if (e.PropertyName == "ClientAreaAnimation") SetPhase(Phase, Ui.Motion);
        }
        void Animate(Animatable target, DependencyProperty property, DoubleAnimation animation) {
            transforms.Add(target); stops.Add(delegate { target.BeginAnimation(property, null); });
            target.BeginAnimation(property, animation);
        }
        void Animate(UIElement target, DependencyProperty property, DoubleAnimation animation) {
            elements.Add(target); stops.Add(delegate { target.BeginAnimation(property, null); });
            target.BeginAnimation(property, animation);
        }
        internal void SetPhase(string phase, bool motion) {
            Stop(); transforms.Clear(); elements.Clear(); Children.Clear(); BarCount = RingCount = 0; Phase = phase;
            if (phase == "loading") {
                if (motion) for (int i = 0; i < 2; i++) {
                    var ring = new System.Windows.Shapes.Ellipse { Width = 38, Height = 38, Stroke = Ui.Cream, StrokeThickness = 1, Opacity = .45, RenderTransformOrigin = new Point(.5, .5) };
                    var scale = new ScaleTransform(.94, .94); ring.RenderTransform = scale; Children.Add(ring); RingCount++;
                    var grow = new DoubleAnimation(.94, 1.65, TimeSpan.FromMilliseconds(1300)) { BeginTime = TimeSpan.FromMilliseconds(i * 650), RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
                    Animate(scale, ScaleTransform.ScaleXProperty, grow); Animate(scale, ScaleTransform.ScaleYProperty, grow);
                    Animate(ring, OpacityProperty, new DoubleAnimation(.45, 0, TimeSpan.FromMilliseconds(1300)) { BeginTime = grow.BeginTime, RepeatBehavior = RepeatBehavior.Forever });
                }
                var bars = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                var delay = new[] { 0, 300, 150, 450, 600 };
                for (int i = 0; i < 5; i++) {
                    var bar = new Border { Width = 2, Height = 19, CornerRadius = new CornerRadius(2), Background = Ui.Cream, Margin = new Thickness(i == 0 ? 0 : 3, 0, 0, 0), RenderTransformOrigin = new Point(.5, .5) };
                    var scale = new ScaleTransform(1, motion ? .25 : new[] { .35, .65, 1, .65, .35 }[i]); bar.RenderTransform = scale; bars.Children.Add(bar); BarCount++;
                    if (motion) {
                        var wave = new DoubleAnimation(.25, 1, TimeSpan.FromMilliseconds(700)) { BeginTime = TimeSpan.FromMilliseconds(delay[i]), AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
                        Animate(scale, ScaleTransform.ScaleYProperty, wave);
                        Animate(bar, OpacityProperty, new DoubleAnimation(.65, 1, wave.Duration) { BeginTime = wave.BeginTime, AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
                    }
                }
                Children.Add(bars); return;
            }
            var icon = Ui.Icon(phase == "success" ? "check" : phase == "error" ? "help" : "sparkles", phase == "success" ? Ui.Brush("#14291A") : Ui.Ink, phase == "success" ? 23 : 19);
            if (phase == "success") {
                Children.Add(new System.Windows.Shapes.Ellipse { Fill = Ui.Green, Width = 38, Height = 38 });
                ((System.Windows.Shapes.Path)((Canvas)((Viewbox)icon).Child).Children[0]).StrokeThickness = 2.4;
            }
            icon.HorizontalAlignment = HorizontalAlignment.Center; icon.VerticalAlignment = VerticalAlignment.Center; Children.Add(icon);
            if (phase == "success" && motion) {
                var scale = new ScaleTransform(1, 1); icon.RenderTransform = scale; icon.RenderTransformOrigin = new Point(.5, .5);
                var pop = new DoubleAnimation(.2, 1, TimeSpan.FromMilliseconds(440)) { FillBehavior = FillBehavior.Stop, EasingFunction = new BackEase { Amplitude = .5, EasingMode = EasingMode.EaseOut } };
                Animate(scale, ScaleTransform.ScaleXProperty, pop); Animate(scale, ScaleTransform.ScaleYProperty, pop);
            }
        }
        internal void Stop() { foreach (var stop in stops) stop(); stops.Clear(); }
        public void Dispose() { Stop(); SystemParameters.StaticPropertyChanged -= MotionChanged; }
    }
    public sealed class Capsule : StackPanel, IDisposable {
        readonly Border core;
        readonly Button arrow, plus;
        readonly StackPanel arrowWrap, plusWrap;
        readonly double size;
        readonly Func<bool> motion;
        readonly System.Windows.Threading.DispatcherTimer leaveTimer;
        bool hovered, arrowOpen, plusOpen, disposed;
        internal FrameworkElement Circle { get { return core; } }
        internal FrameworkElement ArrowArea { get { return arrowWrap; } }
        internal FrameworkElement PlusArea { get { return plusWrap; } }
        public bool Editing;
        public Capsule(Reply reply, double size, bool last, Action copy, Action edit, Action add, Action copyText, Func<bool> motion = null) {
            this.size = size; this.motion = motion ?? (delegate { return Ui.Motion; }); Orientation = Orientation.Horizontal; Margin = new Thickness(0, 0, 4, 0); Background = Ui.HoverBridge;
            core = new Border { Width = size + 6, Height = size + 6, Background = Ui.Brush("#353535"), CornerRadius = new CornerRadius(99), Padding = new Thickness(2.5), BorderThickness = new Thickness(.5), BorderBrush = Ui.Brush("#09FFFFFF"), Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 11, ShadowDepth = 3, Opacity = .2 } };
            var orb = Ui.IconButton(reply.Icon, reply.Name + (reply.Files.Count > 0 ? " · вложений: " + reply.Files.Count + "\nЛКМ: текст и файлы · ПКМ: только текст" : ""), copy, size, Ui.Brush("#080808"));
            orb.MouseRightButtonUp += delegate(object sender, MouseButtonEventArgs e) { copyText(); e.Handled = true; };
            if (reply.Files.Count > 0) {
                var icon = new Grid(); icon.Children.Add(Ui.Icon(reply.Icon));
                var badge = new Border { Background = Ui.Cream, CornerRadius = new CornerRadius(99), MinWidth = 15, Height = 15, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, -8, -7) };
                var count = Ui.Text(reply.Files.Count.ToString(), 9, Ui.Brush("#181818")); count.TextAlignment = TextAlignment.Center; badge.Child = count; icon.Children.Add(badge); orb.Content = icon;
            }
            core.Child = orb; Children.Add(core);
            arrow = Ui.IconButton("up", "Редактировать: " + reply.Name, edit, 29); arrow.Content = Ui.Icon("up", Ui.Muted, 15); arrow.Height = size + 6; arrow.Opacity = 0; arrow.IsHitTestVisible = false;
            arrow.MouseEnter += delegate { arrow.Content = Ui.Icon(Editing ? "down" : "up", Ui.Cream, 15); };
            arrow.MouseLeave += delegate { arrow.Content = Ui.Icon(Editing ? "down" : "up", Ui.Muted, 15); };
            arrowWrap = new StackPanel { Width = 0, ClipToBounds = true, Background = Ui.HoverBridge }; arrowWrap.Children.Add(arrow); Children.Add(arrowWrap);
            if (last) {
                plusWrap = new StackPanel { Width = 0, Orientation = Orientation.Horizontal, ClipToBounds = true, Background = Ui.HoverBridge };
                plus = Ui.IconButton("plus", "Добавить ответ", add, size, Ui.Brush("#B0303030")); plus.Margin = new Thickness(8, 3, 0, 3); plus.BorderBrush = Ui.Brush("#1CFFFFFF"); plus.BorderThickness = new Thickness(1); plus.Opacity = 0; plus.IsHitTestVisible = false; plusWrap.Children.Add(plus); Children.Add(plusWrap);
            }
            leaveTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) }; leaveTimer.Tick += LeaveTick;
            MouseEnter += Enter; MouseLeave += Leave; GotKeyboardFocus += FocusEntered; LostKeyboardFocus += FocusLeft; Unloaded += Unload;
        }
        void Enter(object sender, MouseEventArgs e) { if (disposed) return; hovered = true; leaveTimer.Stop(); Update(); }
        void Leave(object sender, MouseEventArgs e) { if (disposed) return; hovered = false; leaveTimer.Stop(); leaveTimer.Start(); }
        void FocusEntered(object sender, KeyboardFocusChangedEventArgs e) { if (!disposed) Update(true); }
        void FocusLeft(object sender, KeyboardFocusChangedEventArgs e) { if (!disposed) Update(); }
        void LeaveTick(object sender, EventArgs e) { leaveTimer.Stop(); if (!disposed) Update(); }
        void Update(bool keyboard = false) {
            bool active = hovered || IsMouseOver || IsKeyboardFocusWithin || keyboard;
            bool nextArrow = Editing || active;
            if (arrowOpen != nextArrow) { arrowOpen = nextArrow; Ui.Width(arrowWrap, nextArrow ? 29 : 0, motion()); Fade(arrow, nextArrow); }
            bool nextPlus = plusWrap != null && active && !Editing;
            if (plusOpen != nextPlus) { plusOpen = nextPlus; Ui.Width(plusWrap, nextPlus ? size + 8 : 0, motion()); Fade(plus, nextPlus); }
        }
        void Fade(Button button, bool show) { var from = button.Opacity; button.BeginAnimation(OpacityProperty, null); button.Opacity = show ? 1 : 0; button.IsHitTestVisible = show; if (motion()) Ui.Start(button, OpacityProperty, new DoubleAnimation(from, button.Opacity, TimeSpan.FromMilliseconds(140)) { FillBehavior = FillBehavior.Stop }); }
        public void SetEditing(bool editing) { if (disposed) return; if (Editing != editing) { Editing = editing; arrow.Content = Ui.Icon(editing ? "down" : "up", Ui.Muted, 15); } Update(); }
        void Unload(object sender, RoutedEventArgs e) { leaveTimer.Stop(); StopAnimations(); }
        void StopAnimations() { arrowWrap.BeginAnimation(WidthProperty, null); arrow.BeginAnimation(OpacityProperty, null); if (plusWrap != null) { plusWrap.BeginAnimation(WidthProperty, null); plus.BeginAnimation(OpacityProperty, null); } }
        public void Dispose() {
            if (disposed) return; disposed = true; leaveTimer.Stop(); leaveTimer.Tick -= LeaveTick;
            MouseEnter -= Enter; MouseLeave -= Leave; GotKeyboardFocus -= FocusEntered; LostKeyboardFocus -= FocusLeft; Unloaded -= Unload;
            StopAnimations();
        }
    }
}
