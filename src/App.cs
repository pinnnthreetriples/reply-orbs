using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace ReplyOrbs {
    public static class Program {
        [STAThread] public static int Main(string[] args) {
            if (args.Contains("--self-test")) return Tests.Run(args);
            var root = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ReplyOrbs");
            var data = Array.IndexOf(args, "--data"); if (data >= 0 && data + 1 < args.Length) root = args[data + 1];
            var hash = BitConverter.ToString(SHA256.Create().ComputeHash(Encoding.UTF8.GetBytes(System.IO.Path.GetFullPath(root).ToLowerInvariant()))).Replace("-", "");
            bool created; using (var mutex = new Mutex(true, "Local\\ReplyOrbs-" + hash.Substring(0, 20), out created)) {
                if (!created) { MessageBox.Show("Reply Orbs уже запущен. Панель можно показать через значок в трее или Ctrl+Alt+R.", "Reply Orbs"); return 0; }
                try {
                    var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                    Ui.Install(app);
                    var controller = new Controller(app, new Store(root), args.Contains("--qa-fixture"));
                    app.DispatcherUnhandledException += delegate(object sender, DispatcherUnhandledExceptionEventArgs e) { controller.Status("Действие не выполнено: " + e.Exception.GetType().Name); e.Handled = true; };
                    app.Exit += delegate { controller.Dispose(); };
                    controller.Start(); app.Run(); return 0;
                } catch (Exception e) { MessageBox.Show(e is InvalidDataException ? e.Message : "Не удалось запустить Reply Orbs: " + e.GetType().Name, "Reply Orbs", MessageBoxButton.OK, MessageBoxImage.Error); return 1; }
            }
        }
    }
    public sealed class Controller : IDisposable {
        readonly Application app; readonly Store store; readonly KeyVault vault;
        readonly DeepSeek ai;
        readonly Func<string> readKey;
        readonly Func<string, CancellationToken, Task<uint>> copyGenerated;
        readonly QaBackend qa;
        readonly SelectionProbe probe = new SelectionProbe(); readonly PasteGate gate = new PasteGate();
        readonly Func<Native.Point, IntPtr, bool, bool, Task<Probe>> inspect;
        readonly Func<IntPtr> foreground;
        readonly bool fixture;
        readonly Stopwatch clock = Stopwatch.StartNew();
        State state; Window bar, settings, offer, fixtureWindow; AnswerEditor editor;
        WrapPanel row; TextBlock toast, fixtureResult; Button undo; Forms.NotifyIcon tray; System.Drawing.Icon trayIcon;
        Grid statusRow; ReplyDock dock;
        internal ReplyDock Dock { get { return dock; } }
        internal Window DockWindow { get { return bar; } }
        DispatcherTimer toastTimer, pasteTimer, offerTimer;
        DesktopEvents events; IntPtr barHandle, fixtureHandle;
        readonly Dictionary<string, Reply> drafts = new Dictionary<string, Reply>();
        readonly Dictionary<string, Capsule> capsules = new Dictionary<string, Capsule>();
        Reply deleted; int deletedIndex;
        string selected = "", generated = ""; IntPtr source;
        Native.Point selectedPoint;
        CancellationTokenSource request; int generation, observation;
        long fixtureControlUntil;
        Button generateButton, pencilButton; bool busy;
        internal GenerationVisual generationVisual;
        internal bool PastePending { get { return gate.Pending; } }
        internal bool PasteArmed { get { return gate.Armed; } }
        internal void PrepareQaPaste(IntPtr hwnd) { if (!fixture) throw new InvalidOperationException(); fixtureHandle = source = hwnd; gate.Arm(hwnd.ToInt64(), Native.GetClipboardSequenceNumber(), clock.ElapsedMilliseconds); }
        long InputRevision { get { return events == null ? 0 : events.Revision; } }
        public Controller(Application app, Store store, bool fixture) : this(app, store, fixture, null, null, null) { }
        internal Controller(Application app, Store store, bool fixture, DeepSeek client, Func<string> key, Func<string, CancellationToken, Task<uint>> copy, Func<Native.Point, IntPtr, bool, bool, Task<Probe>> inspect = null, Func<IntPtr> foreground = null) {
            this.app = app; this.store = store; this.fixture = fixture; Ui.Qa = fixture; vault = new KeyVault(store.Root); state = store.Load(); probe.FixtureAllowed = fixture;
            this.inspect = inspect ?? probe.Read; this.foreground = foreground ?? Native.GetForegroundWindow;
            if (fixture && client == null) qa = new QaBackend();
            ai = client ?? (qa == null ? new DeepSeek() : new DeepSeek(qa));
            readKey = key ?? (fixture ? (Func<string>)(delegate { return "synthetic-key"; }) : vault.Read);
            copyGenerated = copy ?? (async delegate(string answer, CancellationToken ct) {
                if (qa != null && qa.ClipboardFailure) throw new IOException("QA: буфер недоступен · повторите запрос");
                return await Native.Copy(Native.Data(new Reply { Text = answer }, store, false), ct);
            });
        }
        public void Start() {
            CreateDock();
            bar.SourceInitialized += delegate {
                barHandle = new WindowInteropHelper(bar).Handle;
                var hot1 = Native.RegisterHotKey(barHandle, 1, 0x4003, 0x52); var hot2 = Native.RegisterHotKey(barHandle, 2, 0x4006, 0x20);
                HwndSource.FromHwnd(barHandle).AddHook(delegate(IntPtr h, int msg, IntPtr wp, IntPtr lp, ref bool handled) {
                    if (msg == 0x312) { handled = true; if (wp.ToInt32() == 1) Toggle(); if (wp.ToInt32() == 2) ManualQuestion(); }
                    if (msg == 0x2E0 || msg == 0x7E) bar.Dispatcher.BeginInvoke(new Action(delegate { row.MaxWidth = SystemParameters.WorkArea.Width - 40; Ui.Clamp(bar); }));
                    return IntPtr.Zero;
                });
                if (!hot1 || !hot2) Status("Часть горячих клавиш занята. Используйте значок в трее.");
            };
            ShowDock();
            trayIcon = System.Drawing.Icon.ExtractAssociatedIcon(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ReplyOrbs.exe"));
            tray = new Forms.NotifyIcon { Icon = trayIcon ?? System.Drawing.SystemIcons.Application, Text = "Reply Orbs · Ctrl+Alt+R", Visible = true };
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("Показать / скрыть панель", null, delegate { app.Dispatcher.Invoke(new Action(Toggle)); });
            menu.Items.Add("Ответить на вопрос…", null, delegate { app.Dispatcher.Invoke(new Action(ManualQuestion)); });
            menu.Items.Add("Открыть папку проекта", null, delegate { OpenFolder(); });
            menu.Items.Add("Выход", null, delegate { app.Dispatcher.Invoke(new Action(delegate { app.Shutdown(); })); });
            tray.ContextMenuStrip = menu; tray.DoubleClick += delegate { app.Dispatcher.Invoke(new Action(Toggle)); };
            events = new DesktopEvents(app.Dispatcher); events.Click += Click; events.PointerDown += PointerDown; events.Key += UserKey; events.Focus += Focus; events.SelectionKey += SelectionKey;
            if (!events.Available) Status("Наблюдение Windows недоступно. Копирование работает; вопрос — Ctrl+Shift+Пробел.");
            if (store.Warning != null) Status(store.Warning);
            if (fixture) OpenFixture();
        }
        internal void CreateDock() {
            bar = Ui.Window("Reply Orbs", double.NaN, false); bar.ShowInTaskbar = fixture; bar.SizeToContent = SizeToContent.WidthAndHeight; Ui.NoActivate(bar, !fixture);
            var stack = new StackPanel { Margin = new Thickness(8), Background = Ui.HoverBridge };
            row = new WrapPanel { MaxWidth = SystemParameters.WorkArea.Width - 40, HorizontalAlignment = HorizontalAlignment.Left };
            stack.Children.Add(new ScrollViewer { Content = row, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, MaxHeight = Math.Min(280, SystemParameters.WorkArea.Height / 3), MaxWidth = SystemParameters.WorkArea.Width - 40 });
            statusRow = new Grid { HorizontalAlignment = HorizontalAlignment.Center, Height = 22, Visibility = Visibility.Collapsed };
            statusRow.SetBinding(FrameworkElement.WidthProperty, new Binding("ActualWidth") { Source = row });
            statusRow.ColumnDefinitions.Add(new ColumnDefinition()); statusRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            toast = Ui.Text("", 11, Ui.Cream); toast.TextWrapping = TextWrapping.NoWrap; toast.TextTrimming = TextTrimming.CharacterEllipsis; toast.VerticalAlignment = VerticalAlignment.Center; toast.TextAlignment = TextAlignment.Center;
            undo = Ui.Button("Восстановить", Undo); undo.Background = Ui.HoverBridge; undo.MinHeight = 22; undo.Padding = new Thickness(6, 2, 6, 2); undo.Visibility = Visibility.Collapsed;
            statusRow.Children.Add(toast); Grid.SetColumn(undo, 1); statusRow.Children.Add(undo); stack.Children.Add(statusRow);
            dock = new ReplyDock(bar, stack); bar.Content = dock;
            Render();
        }
        internal void ShowDock() {
            Render(); bar.Show(); bar.UpdateLayout();
            bar.Left = state.Left ?? (SystemParameters.WorkArea.Width - bar.ActualWidth) / 2;
            bar.Top = state.Top ?? SystemParameters.WorkArea.Bottom - bar.ActualHeight - 70; Ui.Clamp(bar);
            dock.PositionReady();
        }
        void Render() {
            foreach (var capsule in capsules.Values) capsule.Dispose(); row.Children.Clear(); capsules.Clear();
            var dots = Ui.Dots(true, "Меню проекта · потяните для перемещения", ProjectMenu);
            Native.Point dragOrigin = new Native.Point(); bool dragging = false, moved = false;
            dots.PreviewMouseLeftButtonDown += delegate { Native.GetCursorPos(out dragOrigin); dock.BeginMove(); dragging = true; moved = false; dots.CaptureMouse(); };
            dots.PreviewMouseMove += delegate {
                if (!dragging || Mouse.LeftButton != MouseButtonState.Pressed) return;
                Native.Point p; Native.GetCursorPos(out p); var matrix = PresentationSource.FromVisual(bar).CompositionTarget.TransformFromDevice;
                var delta = matrix.Transform(new Point(p.X - dragOrigin.X, p.Y - dragOrigin.Y));
                if (Math.Abs(delta.X) + Math.Abs(delta.Y) < 4 && !moved) return; moved = true;
                dock.MoveTo(delta);
            };
            dots.PreviewMouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e) {
                dragging = false; dots.ReleaseMouseCapture(); dock.EndMove(); e.Handled = true;
                if (moved) { var next = state.Clone(); var rest = dock.RestPosition; next.Left = rest.X; next.Top = rest.Y; Persist(next); }
                else ProjectMenu();
            };
            dots.LostMouseCapture += delegate { if (dragging) { dragging = false; dock.EndMove(); } };
            dots.Height = state.Size + 6;
            row.Children.Add(new Border { Width = 26, Height = state.Size + 6, Background = Ui.Brush("#191919"), CornerRadius = new CornerRadius(13), Margin = new Thickness(0, 0, 4, 0), Child = dots });
            for (int i = 0; i < state.Replies.Count; i++) {
                var reply = state.Replies[i]; var captured = reply;
                var capsule = new Capsule(reply, state.Size, i == state.Replies.Count - 1, delegate { Copy(captured, true); }, delegate { Edit(captured); }, Add, delegate { Copy(captured, false); });
                row.Children.Add(capsule); capsules.Add(reply.Id, capsule); capsule.SetEditing(editor != null && editor.Draft.Id == reply.Id);
            }
            if (state.Replies.Count == 0) { var plus = Ui.IconButton("plus", "Добавить первый ответ", Add, state.Size, Ui.Brush("#B0303030")); plus.Margin = new Thickness(0, 3, 0, 3); row.Children.Add(plus); }
            if (bar.IsLoaded) { bar.UpdateLayout(); Ui.Clamp(bar); }
        }
        bool Persist(State next) {
            try { store.Save(next); state = next; return true; } catch (Exception e) { Status("Не удалось сохранить данные: " + e.GetType().Name); return false; }
        }
        public void Status(string text) {
            if (toast == null) return;
            toast.Text = text; toast.ToolTip = text; if (toastTimer != null) toastTimer.Stop();
            statusRow.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
            if (fixtureResult != null) fixtureResult.Text = text;
            if (toastTimer == null) { toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(7) }; toastTimer.Tick += delegate { toastTimer.Stop(); toast.Text = ""; statusRow.Visibility = undo.Visibility == Visibility.Visible ? Visibility.Visible : Visibility.Collapsed; }; }
            toastTimer.Start();
        }
        internal void Toggle() { if (bar.IsVisible && dock.Expanded) { CancelAll(); CloseEditor(false); if (settings != null) settings.Close(); bar.Hide(); } else { bar.Show(); dock.Expand(); Ui.Clamp(bar); Native.SetForegroundWindow(new WindowInteropHelper(bar).Handle); bar.Activate(); var first = ((Border)row.Children[0]).Child as Button; if (first != null) first.Focus(); } }
        internal void OpenFolder() { try { store.EnsureDirectories(); Process.Start(new ProcessStartInfo { FileName = store.Root, UseShellExecute = true, Verb = "open" }); } catch (Exception e) { Status("Не удалось открыть папку проекта: " + (e is IOException || e is UnauthorizedAccessException ? e.Message : e.GetType().Name)); } }
        void HoldWindow(Window w) { if (dock != null) { w.Owner = bar; dock.Pin(w); } }
        void ProjectMenu() {
            if (editor != null && editor.IsSaving) return;
            if (busy) { CancelRequest(); HideOffer(); }
            gate.Cancel(); if (settings != null) { settings.Close(); return; }
            CloseEditor(true);
            var w = Ui.Window("Меню проекта", 288); settings = w; HoldWindow(w);
            var body = Ui.Body(w); ((Border)w.Content).Padding = new Thickness(8);
            MenuItem(body, "folder", "Открыть папку проекта", delegate { w.Close(); OpenFolder(); });
            MenuItem(body, "settings", "Настройки", delegate { w.Close(); Settings(); });
            MenuItem(body, "key", "DeepSeek API", delegate { w.Close(); ApiSettings(); });
            w.Deactivated += async delegate { await Task.Delay(120); if (settings == w && !w.IsActive) w.Close(); }; w.Closed += delegate { if (settings == w) settings = null; };
            Ui.Place(w, bar);
        }
        static void MenuItem(StackPanel body, string icon, string label, Action click) {
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(Ui.Icon(icon)); var t = Ui.Text(label); t.Margin = new Thickness(11, 0, 0, 0); content.Children.Add(t);
            var b = Ui.Button(label, click); b.Content = content; b.HorizontalContentAlignment = HorizontalAlignment.Left; b.MinHeight = 44; body.Children.Add(b);
        }
        void Settings() {
            gate.Cancel(); var w = Ui.Window("Настройки", 420); settings = w; HoldWindow(w);
            var body = Ui.Body(w); Ui.Header(w, "Настройки", body);
            var value = Ui.Text("Размер шариков · " + state.Size + " px", 13); body.Children.Add(value);
            var previous = state.Size;
            var slider = new Slider { Minimum = 38, Maximum = 54, TickFrequency = 2, IsSnapToTickEnabled = true, Value = state.Size, Margin = new Thickness(0, 18, 0, 10) };
            System.Windows.Automation.AutomationProperties.SetName(slider, "Размер шариков");
            body.Children.Add(slider); body.Children.Add(Ui.Text("Меньше · 38 px                                      Больше · 54 px", 11, Ui.Muted));
            slider.ValueChanged += delegate { state.Size = (int)slider.Value; value.Text = "Размер шариков · " + state.Size + " px"; Render(); };
            bool saved = false;
            Ui.Footer(body).Children.Add(Ui.Button("Сохранить", delegate { var next = state.Clone(); if (Persist(next)) { saved = true; w.Close(); } }, true));
            w.Closed += delegate { if (!saved) { state.Size = previous; Render(); } if (settings == w) settings = null; };
            Ui.Place(w, bar);
        }
        void ApiSettings() {
            gate.Cancel(); CloseEditor(true); if (settings != null) settings.Close();
            var w = Ui.Window("DeepSeek API", 420); settings = w; HoldWindow(w);
            var body = Ui.Body(w); Ui.Header(w, "DeepSeek API", body);
            var input = new PasswordBox { MaxLength = 256 }; Ui.Field(body, "API-ключ DeepSeek", input);
            body.Children.Add(Ui.Text(vault.Exists ? "Ключ сохранён для этого пользователя Windows. Введите новый, чтобы заменить." : "Ключ шифруется Windows DPAPI и доступен только вашему пользователю.", 12, Ui.Muted));
            var note = Ui.Text("DeepSeek Flash · при генерации отправляются выделенный вопрос, промпт и тексты готовых ответов. Вложения остаются на компьютере.", 12, Ui.Muted); note.Margin = new Thickness(0, 12, 0, 0); body.Children.Add(note);
            var error = Ui.Text("", 12, Ui.Brush("#E4B0A2")); body.Children.Add(error);
            var save = Ui.Button("Сохранить ключ", delegate {
                try { vault.Save(input.Password); input.Clear(); Status("Ключ защищён Windows. Проверка подключения — при генерации."); w.Close(); }
                catch (Exception e) { error.Text = e is ArgumentException ? e.Message : "Не удалось защитить ключ: " + e.GetType().Name; }
            }, true); save.IsEnabled = false; input.PasswordChanged += delegate { save.IsEnabled = input.SecurePassword.Length > 0; }; Ui.Footer(body).Children.Add(save);
            w.Closed += delegate { input.Clear(); if (settings == w) settings = null; }; Ui.Place(w, bar); input.Focus();
        }
        void CloseEditor(bool keep) {
            if (editor == null) return; var old = editor; editor = null;
            if (keep) drafts[old.Fresh ? "new" : old.Draft.Id] = old.Capture(); else drafts.Remove(old.Fresh ? "new" : old.Draft.Id);
            old.Close(); foreach (var pair in capsules) pair.Value.SetEditing(false);
        }
        void Edit(Reply reply) {
            if (editor != null && editor.IsSaving) return;
            gate.Cancel(); if (settings != null) settings.Close();
            if (editor != null && editor.Draft.Id == reply.Id) { CloseEditor(true); return; }
            CloseEditor(true); Reply draft; if (!drafts.TryGetValue(reply.Id, out draft)) draft = reply.Clone();
            ShowEditor(draft, false);
        }
        void Add() { if (editor != null && editor.IsSaving) return; gate.Cancel(); CloseEditor(true); if (settings != null) settings.Close(); Reply draft; if (!drafts.TryGetValue("new", out draft)) draft = new Reply(); ShowEditor(draft, true); }
        void ShowEditor(Reply draft, bool fresh) {
            var w = new AnswerEditor(draft.Clone(), fresh, SaveReply, DeleteReply); editor = w; HoldWindow(w);
            w.Cancelled += delegate { CloseEditor(false); };
            w.Closed += delegate { if (editor == w) { drafts.Remove(w.Fresh ? "new" : w.Draft.Id); editor = null; } if (capsules.ContainsKey(w.Draft.Id)) capsules[w.Draft.Id].SetEditing(false); };
            if (capsules.ContainsKey(draft.Id)) capsules[draft.Id].SetEditing(true);
            Ui.Place(w, bar); w.FocusName();
        }
        async void SaveReply(AnswerEditor w) {
            var draft = w.Capture(); if (string.IsNullOrWhiteSpace(draft.Name) || string.IsNullOrWhiteSpace(draft.Text) && draft.Files.Count == 0) { w.Error("Укажите название и добавьте текст или файл."); return; }
            w.Saving(true);
            try {
                var imported = await Task.Run(delegate { return store.Import(draft); });
                var next = state.Clone(); var index = next.Replies.FindIndex(r => r.Id == imported.Id);
                if (index < 0) next.Replies.Add(imported); else next.Replies[index] = imported;
                if (!Persist(next)) { w.Saving(false); return; }
                drafts.Remove(draft.Id); drafts.Remove("new"); if (editor == w) editor = null; w.Saving(false); w.Close(); Render(); Status("Ответ сохранён");
            } catch (Exception e) { w.Error(e is IOException || e is InvalidDataException ? e.Message : "Не удалось сохранить вложения: " + e.GetType().Name); w.Saving(false); }
        }
        void DeleteReply(AnswerEditor w) {
            var index = state.Replies.FindIndex(r => r.Id == w.Draft.Id); if (index < 0) { CloseEditor(false); return; }
            var next = state.Clone(); var removed = next.Replies[index]; next.Replies.RemoveAt(index);
            if (!Persist(next)) return; deleted = removed; deletedIndex = index; CloseEditor(false); Render(); undo.Visibility = Visibility.Visible; Status("Кружок удалён · файлы сохранены");
        }
        void Undo() {
            if (deleted == null) return; var next = state.Clone(); next.Replies.Insert(Math.Min(deletedIndex, next.Replies.Count), deleted.Clone());
            if (!Persist(next)) return; deleted = null; undo.Visibility = Visibility.Collapsed; Render(); Status("Ответ восстановлен");
        }
        async void Copy(Reply reply, bool files) {
            gate.Cancel(); CancelRequest(); HideOffer();
            try { await Native.Copy(Native.Data(reply, store, files)); Status(files && reply.Files.Count > 0 ? "Текст и файлы скопированы · способ вставки зависит от чата" : "Ответ скопирован"); }
            catch (Exception e) { Status(e is IOException ? e.Message : "Копирование не выполнено: " + e.GetType().Name); }
        }
        void SelectionKey() { Native.Point p; Native.GetCursorPos(out p); Observe(p, Native.GetForegroundWindow(), false, events.Revision); }
        void Click(Native.Point point, IntPtr hwnd, long inputVersion) { Observe(point, hwnd, true, inputVersion); }
        async void Observe(Native.Point point, IntPtr hwnd, bool mouseClick, long inputVersion) { await ObserveAsync(point, hwnd, mouseClick, inputVersion); }
        internal async Task ObserveAsync(Native.Point point, IntPtr hwnd, bool mouseClick, long inputVersion) {
            if (hwnd == IntPtr.Zero || Native.Own(hwnd) && hwnd != fixtureHandle) return;
            if (mouseClick) { gate.Pause(); if (pasteTimer != null) pasteTimer.Stop(); }
            var token = ++observation; var now = clock.ElapsedMilliseconds;
            await Task.Delay(70); if (token != observation || InputRevision != inputVersion || foreground() != hwnd) return;
            if (fixture && hwnd == fixtureHandle && now < fixtureControlUntil) return;
            var result = await inspect(point, hwnd, true, mouseClick); if (token != observation || InputRevision != inputVersion || foreground() != hwnd) return;
            if (result == null) { if (gate.Armed) { gate.Cancel(); Status("Поле не удалось проверить · вставьте ответ Ctrl+V"); } else if (fixture) Status("QA: UI Automation не предоставила доступное поле"); return; }
            if (!string.IsNullOrWhiteSpace(result.Selection)) {
                if (result.Selection != selected || source != hwnd) SelectQuestion(result.Selection, hwnd, point, result.Bounds);
                else if (offer != null) offer.Show(); else ShowOffer(result.Bounds);
                return;
            }
            // Keyboard selection/focus observation is never permission to paste.
            if (!mouseClick) return;
            if (busy) { CancelRequest(); HideOffer(); Status("Генерация отменена после смены контекста"); }
            if (!gate.Armed) { if (!busy) HideOffer(); if (fixture) Status(result.Eligible ? "QA: Windows подтвердила пустое поле сообщения" : "QA: выделение / пустое поле сообщения не подтверждено"); return; }
            if (!gate.Click(hwnd.ToInt64(), result.Identity, result.Eligible, result.ClickInside, now)) { gate.Cancel(); if (hwnd == source) Status("Ответ в буфере · поле не подтверждено, вставьте Ctrl+V"); return; }
            Status("Вставлю через 1,5 с · ввод или Escape отменяет");
            var elapsed = clock.ElapsedMilliseconds - now;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Max(1, PasteGate.Delay - elapsed)) }; pasteTimer = timer;
            timer.Tick += async delegate {
                timer.Stop(); var checkToken = observation;
                var current = await inspect(point, hwnd, false, false);
                if (checkToken != observation || InputRevision != inputVersion) return;
                if (current == null || !gate.Ready(hwnd.ToInt64(), current.Identity, current.Eligible, Native.GetClipboardSequenceNumber(), clock.ElapsedMilliseconds) || foreground() != hwnd) { gate.Cancel(); Status("Автовставка отменена · ответ остаётся в буфере"); return; }
                gate.Cancel(); var pasted = Native.Paste(); Status(pasted ? "Вставка выполнена · сообщение не отправлено" : "Windows не разрешила вставку · используйте Ctrl+V"); HideOffer(); selected = "";
            }; timer.Start();
        }
        void PointerDown() { observation++; if (gate.Pending) gate.Cancel(); else gate.Pause(); if (pasteTimer != null) pasteTimer.Stop(); }
        internal void UserKey(uint key) {
            observation++; if (gate.Armed) { gate.Cancel(); if (pasteTimer != null) pasteTimer.Stop(); Status("Автовставка отменена · ответ остаётся в буфере"); }
            if (key == 0x1B) CancelAll();
        }
        internal void Focus(IntPtr hwnd) {
            observation++; if (gate.Pending) gate.Cancel(); else gate.Pause(); if (pasteTimer != null) pasteTimer.Stop();
            if (hwnd != source && !Native.Own(hwnd)) { gate.Cancel(); if (busy) CancelRequest(); HideOffer(); }
        }
        void CancelRequest() { generation++; observation++; if (request != null) { request.Cancel(); request.Dispose(); request = null; } busy = false; if (generationVisual != null) generationVisual.Stop(); }
        internal void CancelAll() { CancelRequest(); gate.Cancel(); observation++; if (pasteTimer != null) pasteTimer.Stop(); HideOffer(); selected = ""; }
        internal void HideOffer() { if (busy) CancelRequest(); if (offerTimer != null) { offerTimer.Stop(); offerTimer = null; } if (generationVisual != null) { generationVisual.Dispose(); generationVisual = null; } if (offer != null) { var old = offer; offer = null; old.Close(); } generateButton = pencilButton = null; }
        internal void SelectQuestion(string question, IntPtr hwnd, Native.Point point, Rect bounds) { CancelAll(); selected = question; source = hwnd; selectedPoint = point; ShowOffer(bounds); }
        void ShowOffer(Rect bounds) {
            HideOffer();
            offer = Ui.Window("Ответить на выделенный вопрос", 104, false); offer.SizeToContent = SizeToContent.Manual; offer.Height = 70; Ui.NoActivate(offer, !fixture);
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Background = Ui.HoverBridge, Margin = new Thickness(14, 16, 14, 16) };
            generateButton = Ui.IconButton("sparkles", "Сгенерировать ответ · Escape отменяет", Generate, 38, Ui.Brush("#080808")); generateButton.BorderBrush = Ui.Brush("#404040"); generateButton.BorderThickness = new Thickness(1);
            generationVisual = new GenerationVisual(); generateButton.Content = null; Phase("ready");
            pencilButton = Ui.IconButton("pencil", "Изменить промпт", PromptSettings, 30, Ui.Brush("#303030")); pencilButton.Margin = new Thickness(6, 0, 0, 0); pencilButton.Opacity = 0; pencilButton.IsHitTestVisible = false;
            var main = new Grid { Width = 38, Height = 38, ClipToBounds = false }; main.Children.Add(generateButton); main.Children.Add(generationVisual);
            panel.Children.Add(main); panel.Children.Add(pencilButton); offer.Content = panel;
            var currentOffer = offer; var currentVisual = generationVisual; var currentPencil = pencilButton;
            offer.Closed += delegate { currentVisual.Dispose(); if (offer == currentOffer) { CancelRequest(); gate.Cancel(); if (offerTimer != null) { offerTimer.Stop(); offerTimer = null; } offer = null; generationVisual = null; generateButton = pencilButton = null; } };
            panel.MouseEnter += delegate { if (offer != currentOffer) return; currentPencil.Opacity = 1; currentPencil.IsHitTestVisible = true; if (offerTimer != null) offerTimer.Stop(); };
            panel.MouseLeave += delegate { if (offer != currentOffer) return; currentPencil.Opacity = 0; currentPencil.IsHitTestVisible = false; };
            Ui.PrepareReveal(offer); offer.Show();
            var matrix = PresentationSource.FromVisual(offer).CompositionTarget.TransformFromDevice;
            var location = matrix.Transform(new Point(bounds.IsEmpty || bounds.Width == 0 ? selectedPoint.X : bounds.Right, bounds.IsEmpty || bounds.Width == 0 ? selectedPoint.Y : bounds.Bottom));
            offer.Left = location.X - 8; offer.Top = location.Y - 10; Ui.Clamp(offer); Ui.Reveal(offer);
        }
        void Phase(string phase) {
            if (generateButton == null || offer == null) return;
            generationVisual.SetPhase(phase, Ui.Motion);
            if (pencilButton != null) pencilButton.IsEnabled = phase != "loading"; generateButton.ToolTip = phase == "loading" ? "Генерирую… Нажмите для отмены" : phase == "success" ? "Ответ в буфере. Нажмите поле чата" : phase == "error" ? "Ошибка. Нажмите, чтобы повторить" : "Сгенерировать ответ";
        }
        async void Generate() { await GenerateAsync(); }
        internal async Task GenerateAsync() {
            if (busy) { CancelRequest(); Phase("ready"); Status("Генерация отменена"); return; }
            if (string.IsNullOrWhiteSpace(selected)) return;
            if (!fixture && !vault.Exists) { Status("Добавьте ключ в DeepSeek API"); ApiSettings(); return; }
            gate.Cancel(); CancelRequest(); var token = generation; request = new CancellationTokenSource(); var cancellation = request.Token;
            busy = true; Phase("loading"); Status("Генерирую короткий ответ…"); var question = selected; var snapshot = state.Clone();
            try {
                var key = readKey(); var answer = await ai.Generate(snapshot, question, key, cancellation); key = null;
                if (token != generation || cancellation.IsCancellationRequested) return;
                var sequence = await copyGenerated(answer, cancellation);
                if (token != generation || cancellation.IsCancellationRequested) return;
                generated = answer; busy = false; Phase("success"); gate.Arm(source.ToInt64(), sequence, clock.ElapsedMilliseconds); Status("Ответ скопирован · нажмите пустое поле исходного чата");
                var completedOffer = offer; var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) }; offerTimer = timer;
                timer.Tick += delegate { timer.Stop(); if (offer == completedOffer) HideOffer(); }; if (completedOffer != null && !completedOffer.IsMouseOver) timer.Start();
            } catch (OperationCanceledException) { if (token == generation) { busy = false; Phase("error"); Status(cancellation.IsCancellationRequested ? "Генерация отменена" : "DeepSeek не ответил вовремя · повторите запрос"); } }
            catch (Exception e) { if (token == generation) { busy = false; Phase("error"); Status(e is InvalidOperationException || e is ArgumentException || e is IOException ? e.Message : "Запрос не выполнен: " + e.GetType().Name); } }
        }
        void PromptSettings() {
            gate.Cancel(); if (settings != null) settings.Close(); var w = Ui.Window("Промпт", 420); settings = w; HoldWindow(w); var body = Ui.Body(w); Ui.Header(w, "Промпт ответа", body);
            var input = new TextBox { Text = state.Prompt, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 150, MaxHeight = 300, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxLength = 6000 };
            Ui.Field(body, "Инструкции операторa", input); var error = Ui.Text("", 12, Ui.Brush("#E4B0A2")); body.Children.Add(error);
            var footer = Ui.Footer(body); footer.Children.Add(Ui.Button("Отмена", w.Close)); footer.Children.Add(Ui.Button("Сохранить", delegate { if (string.IsNullOrWhiteSpace(input.Text)) { error.Text = "Промпт не должен быть пустым."; return; } var next = state.Clone(); next.Prompt = input.Text.Trim(); if (Persist(next)) { w.Close(); Status("Промпт сохранён · нажмите на звёздочки"); } }, true));
            w.Closed += delegate { if (settings == w) settings = null; }; Ui.Place(w, offer ?? bar); input.Focus();
        }
        void ManualQuestion() {
            var original = Native.GetForegroundWindow(); if (Native.Own(original) && original != fixtureHandle) original = source;
            CancelAll(); source = original; Native.GetCursorPos(out selectedPoint); CloseEditor(true); if (settings != null) settings.Close();
            var w = Ui.Window("Вопрос клиента", 420); settings = w; HoldWindow(w); var body = Ui.Body(w); Ui.Header(w, "Вопрос клиента", body);
            body.Children.Add(Ui.Text("Если чат не раскрывает выделение Windows, скопируйте вопрос и вставьте его сюда (Ctrl+V).", 12, Ui.Muted));
            var input = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 140, MaxHeight = 280, MaxLength = 12000, Margin = new Thickness(0, 14, 0, 0) }; System.Windows.Automation.AutomationProperties.SetName(input, "Вопрос клиента"); body.Children.Add(input);
            Ui.Footer(body).Children.Add(Ui.Button("Сгенерировать", delegate { if (string.IsNullOrWhiteSpace(input.Text)) return; selected = input.Text.Trim(); w.Close(); ShowOffer(Rect.Empty); Generate(); }, true));
            w.Closed += delegate { if (settings == w) settings = null; }; Ui.Place(w, bar); input.Focus();
        }
        void OpenFixture() {
            fixtureWindow = Ui.Window("Reply Orbs · проверка Windows-интеграции", 540); fixtureWindow.ShowInTaskbar = true; fixtureWindow.Topmost = false;
            var body = Ui.Body(fixtureWindow); Ui.Header(fixtureWindow, "Тестовое поле Windows · без отправки", body);
            body.Children.Add(Ui.Text("Это техническая проверка UI Automation, а не интеграция с мессенджером.", 12, Ui.Muted));
            body.Children.Add(Ui.Button("Показать / скрыть панель", Toggle));
            var question = new TextBox { Text = "Как оплатить абонемент?", IsReadOnly = true, MinHeight = 70, Margin = new Thickness(0, 18, 0, 15) }; Ui.Field(body, "Вопрос для выделения", question);
            var compose = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 140 }; Ui.Field(body, "Сообщение", compose);
            var result = Ui.Text("", 12, Ui.Muted); fixtureResult = result; body.Children.Add(result);
            Ui.Footer(body).Children.Add(Ui.Button("Подготовить тестовый ответ", async delegate {
                fixtureControlUntil = clock.ElapsedMilliseconds + 300;
                CancelAll(); source = fixtureHandle; generated = "Тестовый ответ Windows. Сообщение не отправляется.";
                await Task.Delay(180);
                var sequence = await Native.Copy(Native.Data(new Reply { Text = generated }, store, false)); gate.Arm(source.ToInt64(), sequence, clock.ElapsedMilliseconds); result.Text = "Нажмите пустое поле «Сообщение»: вставка через 1,5 с. Ввод отменит её.";
            }, true));
            Ui.Footer(body).Children.Add(Ui.Button("Скопировать текст + два тестовых файла", async delegate {
                var folder = System.IO.Path.Combine(store.Root, "qa-input"); Directory.CreateDirectory(folder);
                var one = System.IO.Path.Combine(folder, "reply-orbs-test.txt"); var two = System.IO.Path.Combine(folder, "reply-orbs-test.bin");
                File.WriteAllText(one, "Synthetic clipboard test. No user data."); File.WriteAllBytes(two, new byte[] { 1, 2, 3, 4 });
                var r = new Reply { Text = "Синтетическая проверка текста и двух файлов. Не отправлять." };
                r.Files.Add(new Attachment { Source = one, Name = "reply-orbs-test.txt" }); r.Files.Add(new Attachment { Source = two, Name = "reply-orbs-test.bin" });
                var imported = await Task.Run(delegate { return store.Import(r); });
                await Native.Copy(Native.Data(imported, store, true)); result.Text = "В буфере UnicodeText + FileDrop (2 реальных файла).";
            }));
            body.Children.Add(Ui.Text("AI-проверка: искусственный HTTP, без ключа и сети. Завершите запрос кнопкой ниже.", 12, Ui.Muted));
            var scenario = new ComboBox { ItemsSource = new[] { "Успех", "Ошибка HTTP", "Ошибка буфера" }, SelectedIndex = 0, Margin = new Thickness(0, 10, 0, 0) }; body.Children.Add(scenario);
            var qaActions = Ui.Footer(body);
            qaActions.Children.Add(Ui.Button("Запустить AI", async delegate {
                fixtureControlUntil = clock.ElapsedMilliseconds + 300;
                await Task.Delay(180); var position = question.PointToScreen(new Point(question.ActualWidth - 50, 20));
                SelectQuestion(question.Text, fixtureHandle, new Native.Point { X = (int)position.X, Y = (int)position.Y }, Rect.Empty);
                qa.ClipboardFailure = scenario.SelectedIndex == 2; Generate();
            }));
            qaActions.Children.Add(Ui.Button("Завершить HTTP", delegate { fixtureControlUntil = clock.ElapsedMilliseconds + 300; qa.Complete(scenario.SelectedIndex == 1); }));
            qaActions.Children.Add(Ui.Button("Отменить AI", delegate { fixtureControlUntil = clock.ElapsedMilliseconds + 300; CancelAll(); Status("QA: генерация отменена"); }));
            fixtureWindow.Show(); fixtureHandle = new WindowInteropHelper(fixtureWindow).Handle;
            fixtureWindow.Closed += delegate { app.Shutdown(); };
            fixtureWindow.Left = 130; fixtureWindow.Top = 160; Ui.Clamp(fixtureWindow);
        }
        public void Dispose() {
            CancelAll(); if (events != null) events.Dispose(); if (barHandle != IntPtr.Zero) { Native.UnregisterHotKey(barHandle, 1); Native.UnregisterHotKey(barHandle, 2); }
            if (dock != null) dock.Dispose(); foreach (var capsule in capsules.Values) capsule.Dispose(); if (bar != null) bar.Close();
            if (tray != null) { tray.Visible = false; tray.Dispose(); } if (trayIcon != null) trayIcon.Dispose(); ai.Dispose(); if (toastTimer != null) toastTimer.Stop();
        }
    }
    public sealed class AnswerEditor : Window {
        public readonly Reply Draft;
        public readonly bool Fresh;
        public bool IsSaving { get { return saving; } }
        readonly TextBox name, text; readonly StackPanel fileList, menu; readonly Popup popup; readonly TextBlock error;
        readonly Button save, cancel;
        bool saving; public event Action Cancelled;
        public AnswerEditor(Reply draft, bool fresh, Action<AnswerEditor> saveAction, Action<AnswerEditor> deleteAction) {
            Draft = draft; Fresh = fresh;
            Title = fresh ? "Новый ответ" : "Редактировать ответ"; Width = 420; SizeToContent = SizeToContent.Height; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true; Background = Brushes.Transparent; Topmost = true; ShowInTaskbar = Ui.Qa; FontFamily = new FontFamily("Segoe UI"); Foreground = Ui.Ink;
            var body = Ui.Body(this); var heading = Ui.Header(this, Title, body);
            var actions = new StackPanel { Orientation = Orientation.Horizontal }; actions.Children.Add(Ui.Dots(false, "Настройки кружка", delegate { popup.IsOpen = !popup.IsOpen; }));
            actions.Children.Add(Ui.IconButton("close", "Закрыть редактор", delegate { if (Cancelled != null) Cancelled(); }));
            heading.Children.RemoveAt(1); Grid.SetColumn(actions, 1); heading.Children.Add(actions);
            menu = new StackPanel();
            popup = new Popup { PlacementTarget = actions, Placement = PlacementMode.Bottom, HorizontalOffset = -218, VerticalOffset = 8, StaysOpen = false, AllowsTransparency = true, Child = new Border { Width = 276, Background = Ui.Brush("#252525"), BorderBrush = Ui.Brush("#353535"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(17), Padding = new Thickness(8), Child = menu } };
            var icons = new WrapPanel { Visibility = Visibility.Collapsed };
            menu.Children.Add(Ui.Button("Выбрать иконку", delegate { icons.Visibility = icons.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible; }));
            for (int i = 0; i < Ui.IconKeys.Length; i++) {
                var key = Ui.IconKeys[i]; var button = Ui.IconButton(key, Ui.IconNames[i], delegate { Draft.Icon = key; popup.IsOpen = false; }, 36, Ui.Brush("#303030")); button.Margin = new Thickness(3); icons.Children.Add(button);
            }
            menu.Children.Add(icons); if (!fresh) { var del = Ui.Button("Удалить кружок", delegate { if (!saving) deleteAction(this); }); del.Foreground = Ui.Brush("#E4B0A2"); menu.Children.Add(del); }
            name = new TextBox { Text = draft.Name, MaxLength = 100 }; Ui.Field(body, "Название", name);
            text = new TextBox { Text = draft.Text, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 142, MaxHeight = 300, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxLength = 100000 }; Ui.Field(body, "Текст ответа", text);
            fileList = new StackPanel(); body.Children.Add(fileList); RenderFiles();
            error = Ui.Text("", 12, Ui.Brush("#E4B0A2")); body.Children.Add(error);
            var footer = new Grid { Margin = new Thickness(0, 18, 0, 0) }; footer.ColumnDefinitions.Add(new ColumnDefinition()); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var tools = new StackPanel { Orientation = Orientation.Horizontal }; tools.Children.Add(Ui.IconButton("clip", "Прикрепить файлы", Attach, 36));
            tools.Children.Add(Ui.IconButton("message", "Скопировать только текст", async delegate { try { await Native.Copy(new DataObject(DataFormats.UnicodeText, text.Text)); Error("Текст скопирован"); } catch { Error("Буфер занят. Повторите копирование."); } }, 36));
            footer.Children.Add(tools); var buttons = new StackPanel { Orientation = Orientation.Horizontal }; cancel = Ui.Button("Отмена", delegate { if (Cancelled != null) Cancelled(); }); buttons.Children.Add(cancel); save = Ui.Button("Сохранить", delegate { saveAction(this); }, true); buttons.Children.Add(save); Grid.SetColumn(buttons, 1); footer.Children.Add(buttons); body.Children.Add(footer);
            Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e) { if (saving) e.Cancel = true; };
            Closed += delegate { popup.IsOpen = false; };
        }
        public void FocusName() { name.Focus(); }
        public Reply Capture() { var r = Draft.Clone(); r.Name = name.Text.Trim(); r.Text = text.Text; return r; }
        public void Error(string message) { error.Text = message; }
        public void Saving(bool value) { saving = value; save.IsEnabled = cancel.IsEnabled = !value; name.IsReadOnly = text.IsReadOnly = value; save.Content = value ? "Сохраняю…" : "Сохранить"; }
        void Attach() {
            if (saving) return;
            var dialog = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Filter = "Любые файлы|*.*", Title = "Прикрепить файлы", CheckFileExists = true };
            if (dialog.ShowDialog(this) != true) return;
            foreach (var path in dialog.FileNames) {
                try { var file = new FileInfo(path); Draft.Files.Add(new Attachment { Source = path, Name = file.Name, Size = file.Length }); }
                catch { Error("Не удалось открыть один из файлов."); }
            }
            RenderFiles();
        }
        void RenderFiles() {
            fileList.Children.Clear();
            foreach (var f in Draft.Files.ToArray()) {
                var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var t = Ui.Text(f.Name + "\n" + FormatSize(f.Size), 12); t.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(t);
                var remove = Ui.IconButton("close", "Убрать вложение: " + f.Name, delegate { if (!saving) { Draft.Files.Remove(f); RenderFiles(); } }); Grid.SetColumn(remove, 1); row.Children.Add(remove);
                fileList.Children.Add(new Border { Background = Ui.Brush("#242424"), CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(0, 3, 0, 3), Child = row });
            }
        }
        static string FormatSize(long size) { return size >= 1048576 ? (size / 1048576.0).ToString("0.#") + " МБ" : size >= 1024 ? (size / 1024.0).ToString("0.#") + " КБ" : size + " Б"; }
    }
}
