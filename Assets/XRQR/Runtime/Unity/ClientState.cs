using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Xrqr.Json;

namespace Xrqr.Unity
{
    /// <summary>
    /// What the client keeps between sessions (section 4.2 and Appendix C): the main resolver
    /// list shipped with the app, the entries the user imported, the highest version seen per
    /// resolver and id (the rollback guard), and the resolver the user chose per resolver and
    /// publisher number. Imported entries and the other records live in
    /// <c>persistentDataPath/xrqr/state.json</c>.
    /// </summary>
    public sealed class ClientState
    {
        /// <summary>The main list, a Resources text asset of the form <c>{"entries": [entry, ...]}</c>.</summary>
        public const string MainListResource = "xrqr-main-list";

        readonly string m_Path;
        readonly List<ResolverEntry> m_Main = new List<ResolverEntry>();
        readonly List<ResolverEntry> m_Imported = new List<ResolverEntry>();
        readonly Dictionary<string, uint> m_Highest = new Dictionary<string, uint>(StringComparer.Ordinal);
        readonly HashSet<string> m_Choices = new HashSet<string>(StringComparer.Ordinal);

        public IReadOnlyList<ResolverEntry> Main => m_Main;
        public IReadOnlyList<ResolverEntry> Imported => m_Imported;

        /// <summary>Every trusted entry: the main list, then the imported entries.</summary>
        public IReadOnlyList<ResolverEntry> Entries
        {
            get
            {
                var all = new List<ResolverEntry>(m_Main);
                all.AddRange(m_Imported);
                return all;
            }
        }

        ClientState(string path) => m_Path = path;

        public static ClientState Load()
        {
            var state = new ClientState(Path.Combine(Application.persistentDataPath, "xrqr", "state.json"));
            var main = Resources.Load<TextAsset>(MainListResource);
            if (main != null)
                state.m_Main.AddRange(ReadEntries(StrictJson.Parse(main.bytes)["entries"], "main list"));
            else
                Debug.LogError($"XRQR: the main resolver list Resources/{MainListResource} is missing");

            if (!File.Exists(state.m_Path))
                return state;
            try
            {
                var stored = StrictJson.Parse(File.ReadAllBytes(state.m_Path));
                state.m_Imported.AddRange(ReadEntries(stored["entries"], "imported entries"));
                if (stored["highest"] is JsonObject highest)
                {
                    foreach (var member in highest.Members)
                    {
                        if (member.Value is JsonNumber n && n.Value >= 1 && n.Value <= uint.MaxValue)
                            state.m_Highest[member.Key] = (uint)n.Value;
                    }
                }
                if (stored["choices"] is JsonArray choices)
                {
                    foreach (var choice in choices.Items)
                    {
                        if (choice.AsString != null)
                            state.m_Choices.Add(choice.AsString);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"XRQR: stored client state unreadable, starting empty: {e.Message}");
            }
            return state;
        }

        static IEnumerable<ResolverEntry> ReadEntries(JsonValue list, string source)
        {
            if (!(list is JsonArray array))
                yield break;
            foreach (var item in array.Items)
            {
                ResolverEntry entry = null;
                try
                {
                    entry = ResolverEntry.Parse(Jcs.Serialize(item));
                }
                catch (XrqrException e)
                {
                    Debug.LogWarning($"XRQR: an entry of the {source} is invalid ({e.Kind}): {e.Message}");
                }
                if (entry != null)
                    yield return entry;
            }
        }

        /// <summary>
        /// Adds an imported entry. Returns <c>false</c> when the same entry is already trusted.
        /// </summary>
        public bool Import(ResolverEntry entry)
        {
            var text = Jcs.SerializeToString(entry.ToJson());
            foreach (var known in Entries)
            {
                if (Jcs.SerializeToString(known.ToJson()) == text)
                    return false;
            }
            m_Imported.Add(entry);
            Save();
            return true;
        }

        public void Remove(ResolverEntry entry)
        {
            if (m_Imported.Remove(entry))
                Save();
        }

        static string VersionKey(ResolverEntry entry, XrqrId id) => entry.Base + " " + id.Text;
        static string ChoiceKey(ResolverEntry entry, uint publisher) => entry.Base + " " + publisher;

        /// <summary>The highest version seen on <paramref name="entry"/> for <paramref name="id"/>, or 0.</summary>
        public uint HighestVersion(ResolverEntry entry, XrqrId id) =>
            m_Highest.TryGetValue(VersionKey(entry, id), out var n) ? n : 0;

        public void RecordVersion(ResolverEntry entry, XrqrId id, uint version)
        {
            if (version <= HighestVersion(entry, id))
                return;
            m_Highest[VersionKey(entry, id)] = version;
            Save();
        }

        /// <summary>Tells whether the user chose <paramref name="entry"/> before for ids of <paramref name="publisher"/>.</summary>
        public bool IsChosen(ResolverEntry entry, uint publisher) => m_Choices.Contains(ChoiceKey(entry, publisher));

        public void RecordChoice(ResolverEntry entry, uint publisher)
        {
            if (m_Choices.Add(ChoiceKey(entry, publisher)))
                Save();
        }

        void Save()
        {
            var entries = new JsonArray();
            foreach (var entry in m_Imported)
                entries.Items.Add(entry.ToJson());
            var highest = new JsonObject();
            foreach (var pair in m_Highest)
                highest.Add(pair.Key, new JsonNumber(pair.Value));
            var choices = new JsonArray();
            foreach (var choice in m_Choices)
                choices.Items.Add(new JsonString(choice));
            var document = new JsonObject().Add("entries", entries).Add("highest", highest).Add("choices", choices);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(m_Path));
                var temporary = m_Path + ".tmp";
                File.WriteAllBytes(temporary, Jcs.Serialize(document));
                if (File.Exists(m_Path))
                    File.Delete(m_Path);
                File.Move(temporary, m_Path);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"XRQR: could not save client state: {e.Message}");
            }
        }
    }
}
