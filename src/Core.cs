using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace ReplyOrbs {
    public class Attachment {
        public string Path { get; set; }
        public string Name { get; set; }
        public long Size { get; set; }
        [ScriptIgnore] public string Source { get; set; }
        public Attachment Clone() { return (Attachment)MemberwiseClone(); }
    }
    public class Reply {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Text { get; set; }
        public string Icon { get; set; }
        public List<Attachment> Files { get; set; }
        public Reply() { Id = Guid.NewGuid().ToString("N"); Name = ""; Text = ""; Icon = "message"; Files = new List<Attachment>(); }
        public Reply Clone() { var r = (Reply)MemberwiseClone(); r.Files = Files.Select(f => f.Clone()).ToList(); return r; }
    }
    public class State {
        public int Version { get; set; }
        public int Size { get; set; }
        public string Prompt { get; set; }
        public double? Left { get; set; }
        public double? Top { get; set; }
        public List<Reply> Replies { get; set; }
        public State() { Version = 1; Size = 38; Prompt = DefaultPrompt; Replies = new List<Reply>(); }
        public const string DefaultPrompt = "Отвечай клиенту максимально кратко, простым языком и по делу. Если нужны действия, оформи их короткими пунктами. Не придумывай факты. Используй готовые ответы как источник.";
        public State Clone() { var s = (State)MemberwiseClone(); s.Replies = Replies.Select(r => r.Clone()).ToList(); return s; }
        public static State Initial() {
            var s = new State();
            s.Replies.Add(new Reply { Name = "Создать абонемент", Icon = "ticket", Text = "Чтобы создать абонемент:\n\n1. Откройте раздел «Абонементы».\n2. Нажмите «Создать абонемент».\n3. Выберите клиента и нужный тариф.\n4. Сохраните изменения.\n\nЕсли возникнут сложности, напишите — помогу." });
            s.Replies.Add(new Reply { Name = "Перенести занятие", Icon = "calendar", Text = "Здравствуйте! Напишите, пожалуйста, дату и время занятия, которое хотите перенести, и удобное для вас время. Я проверю доступные варианты." });
            s.Replies.Add(new Reply { Name = "Как оплатить", Icon = "card", Text = "Здравствуйте! Оплатить абонемент можно в личном кабинете: откройте раздел «Абонементы», выберите нужный абонемент и нажмите «Оплатить»." });
            s.Replies.Add(new Reply { Name = "Адрес и как добраться", Icon = "pin", Text = "Здравствуйте! Напишите, пожалуйста, какой филиал вас интересует. Отправлю точный адрес и подскажу, как удобнее добраться." });
            return s;
        }
    }
    public sealed class Store {
        public readonly string Root;
        public string StatePath { get { return System.IO.Path.Combine(Root, "replies.json"); } }
        public string Warning { get; private set; }
        public static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 };
        public Store(string root) { Root = System.IO.Path.GetFullPath(root); EnsureDirectories(); }
        public void EnsureDirectories() { Directory.CreateDirectory(Root); Directory.CreateDirectory(System.IO.Path.Combine(Root, "Files")); Directory.CreateDirectory(System.IO.Path.Combine(Root, "Notes")); }
        public State Load() {
            if (!File.Exists(StatePath)) { var initial = State.Initial(); Save(initial); return initial; }
            try { return Decode(File.ReadAllText(StatePath, Encoding.UTF8)); }
            catch (Exception e) {
                if (!(e is IOException || e is ArgumentException || e is InvalidOperationException)) throw;
                var backup = StatePath + ".bak";
                if (!File.Exists(backup)) throw new InvalidDataException("Данные не удалось прочитать. Исходный replies.json сохранён; исправьте его или восстановите резервную копию.");
                try { var s = Decode(File.ReadAllText(backup, Encoding.UTF8)); Warning = "Загружена резервная копия. Повреждённый файл сохранён отдельно.";
                    var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
                    File.Copy(StatePath, StatePath + ".damaged-" + stamp, false);
                    File.Copy(backup, StatePath + ".recovered-" + stamp, false); return s;
                } catch { throw new InvalidDataException("Основной файл и резервная копия повреждены. Они сохранены без изменений."); }
            }
        }
        public static State Decode(string text) {
            var s = Json.Deserialize<State>(text);
            Validate(s); return s;
        }
        public static void Validate(State s) {
            if (s == null || s.Version != 1 || s.Replies == null)
                throw new InvalidDataException("Некорректный формат replies.json.");
            if (s.Replies.Any(r => r == null || string.IsNullOrEmpty(r.Id) || r.Name == null || r.Text == null || r.Files == null || r.Files.Any(f => f == null || string.IsNullOrEmpty(f.Path) || f.Name == null)) || s.Replies.Select(r => r.Id).Distinct().Count() != s.Replies.Count)
                throw new InvalidDataException("Некорректный формат replies.json.");
            s.Size = Math.Max(38, Math.Min(54, s.Size / 2 * 2));
            if (string.IsNullOrWhiteSpace(s.Prompt)) s.Prompt = State.DefaultPrompt;
            if (s.Left.HasValue && (double.IsNaN(s.Left.Value) || double.IsInfinity(s.Left.Value))) s.Left = null;
            if (s.Top.HasValue && (double.IsNaN(s.Top.Value) || double.IsInfinity(s.Top.Value))) s.Top = null;
        }
        public void Save(State s) {
            Validate(s); AtomicWrite(StatePath, Encoding.UTF8.GetBytes(Json.Serialize(s)));
        }
        public static void AtomicWrite(string path, byte[] bytes) {
            var temp = path + ".pending-" + Guid.NewGuid().ToString("N");
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
            if (File.Exists(path)) File.Replace(temp, path, path + ".bak", true); else File.Move(temp, path);
        }
        public string Resolve(Attachment f) {
            if (System.IO.Path.IsPathRooted(f.Path)) throw new InvalidDataException("Путь вложения должен быть относительным.");
            var files = System.IO.Path.Combine(Root, "Files") + System.IO.Path.DirectorySeparatorChar;
            var path = System.IO.Path.GetFullPath(System.IO.Path.Combine(Root, f.Path));
            if (!path.StartsWith(files, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Вложение за пределами папки Files.");
            var cursor = new DirectoryInfo(System.IO.Path.GetDirectoryName(path));
            while (cursor != null && cursor.FullName.StartsWith(Root, StringComparison.OrdinalIgnoreCase)) { if ((cursor.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Ссылки на внешние папки не поддерживаются."); cursor = cursor.Parent; }
            if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Ссылки на внешние файлы не поддерживаются.");
            return path;
        }
        public Reply Import(Reply draft) {
            var r = draft.Clone();
            foreach (var f in r.Files) {
                if (string.IsNullOrEmpty(f.Source)) { if (!File.Exists(Resolve(f))) throw new IOException("Вложение отсутствует: " + f.Name); continue; }
                var folder = System.IO.Path.Combine(Root, "Files", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
                var target = System.IO.Path.Combine(folder, System.IO.Path.GetFileName(f.Name));
                using (var input = new FileStream(f.Source, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { input.CopyTo(output); output.Flush(true); f.Size = output.Length; }
                f.Path = target.Substring(Root.Length + 1); f.Source = null;
            }
            return r;
        }
    }
    public sealed class KeyVault {
        readonly string path;
        static readonly byte[] Entropy = Encoding.UTF8.GetBytes("ReplyOrbs.DeepSeek.v1");
        public KeyVault(string root) { path = System.IO.Path.Combine(root, "deepseek.dpapi"); }
        public bool Exists { get { return File.Exists(path); } }
        public void Save(string key) {
            if (string.IsNullOrWhiteSpace(key) || key.Length > 256 || key.Any(char.IsControl)) throw new ArgumentException("Проверьте формат ключа.");
            var bytes = Encoding.UTF8.GetBytes(key.Trim());
            try { Store.AtomicWrite(path, ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser)); }
            finally { Array.Clear(bytes, 0, bytes.Length); }
        }
        public string Read() {
            var bytes = ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser);
            try { return Encoding.UTF8.GetString(bytes); } finally { Array.Clear(bytes, 0, bytes.Length); }
        }
    }
    // Deterministic gate shared by the native integration and regression tests.
    public sealed class PasteGate {
        public const int Delay = 1500;
        public bool Armed { get; private set; }
        public bool Pending { get; private set; }
        public long Source { get; private set; }
        long deadline, expires; string target; uint clipboard;
        public void Arm(long source, uint sequence, long now = 0) { Source = source; clipboard = sequence; expires = now + 120000; Armed = true; Pending = false; }
        public bool Click(long source, string identity, bool eligible, bool insideComposer, long now) {
            Pending = false;
            if (!Armed || source != Source || !eligible || !insideComposer || string.IsNullOrEmpty(identity) || now > expires) return false;
            target = identity; deadline = now + Delay; Pending = true; return true;
        }
        public bool Ready(long source, string identity, bool eligible, uint sequence, long now) {
            return Armed && Pending && source == Source && eligible && identity == target && sequence == clipboard && now >= deadline && now <= expires;
        }
        public void Cancel() { Armed = false; Pending = false; }
        public void Pause() { Pending = false; }
    }
}
