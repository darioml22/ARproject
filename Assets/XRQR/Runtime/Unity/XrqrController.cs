using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.Interaction.Toolkit.Samples.ARStarterAssets;
using UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets;

namespace Xrqr.Unity
{
    /// <summary>
    /// The XRQR client in the AR scene: scans a code, asks the trusted resolvers, lets the user
    /// choose when several answer, shows what will be placed and from where, fetches and verifies
    /// the model, loads it with glTFast, and arms placement. A scanned or typed resolver entry is
    /// offered for import.
    /// </summary>
    public sealed class XrqrController : MonoBehaviour
    {
        // The field names are those of the former QRReader, so the scene keeps its references.
        [SerializeField, Tooltip("The AR camera whose frames are scanned.")]
        ARCameraManager cameraManager;

        [SerializeField, Tooltip("Optional text that mirrors the status line.")]
        TMP_Text outputText;

        ClientState m_State;
        QrFrameScanner m_Scanner;
        XrqrPanel m_Panel;
        XrqrPlacement m_Placement;
        Transform m_Holder;
        CancellationTokenSource m_Flow;
        Texture2D m_ShownThumbnail;
        bool m_Scanning;
        readonly Dictionary<string, LoadedModel> m_Models = new Dictionary<string, LoadedModel>(StringComparer.Ordinal);

        void Start()
        {
            m_State = ClientState.Load();
            m_Scanner = new QrFrameScanner(cameraManager);
            m_Panel = new XrqrPanel(StartScanning, ShowResolvers);
            m_Panel.Submitted += OnTyped;

            var holder = new GameObject("XRQR Models");
            holder.SetActive(false);
            holder.transform.SetParent(transform, false);
            m_Holder = holder.transform;

            var trigger = FindAnyObjectByType<ARInteractorSpawnTrigger>();
            var spawner = FindAnyObjectByType<ObjectSpawner>();
            if (trigger != null && spawner != null)
            {
                m_Placement = new XrqrPlacement(trigger, spawner);
                m_Placement.Refused += reason => SetStatus(reason, true);
                m_Placement.Placed += _ => SetStatus("Placed. Tap another surface for another copy, or Scan for a new code.");
            }
            else
            {
                Debug.LogWarning("XRQR: no ARInteractorSpawnTrigger and ObjectSpawner in the scene; models load but cannot be placed.", this);
            }
            StartScanning();
        }

        void Update()
        {
            if (!m_Scanning)
                return;
            var text = m_Scanner.Tick();
            if (text != null)
                OnScanned(text);
        }

        void OnDestroy()
        {
            m_Flow?.Cancel();
            m_Scanner?.Dispose();
            foreach (var model in m_Models.Values)
                model.Dispose();
            m_Models.Clear();
            if (m_ShownThumbnail != null)
                Destroy(m_ShownThumbnail);
            m_Panel?.Destroy();
        }

        // ---- Input -------------------------------------------------------------------------

        /// <summary>A scanned text is read as it is: a code is never trimmed (section 2).</summary>
        void OnScanned(string text)
        {
            if (XrqrCode.TryParse(text, out var id))
            {
                Resolve(id);
                return;
            }
            if (text.TrimStart().StartsWith("{", StringComparison.Ordinal))
            {
                OfferEntry(text);
                return;
            }
            SetStatus("This QR code is not an XRQR code.", true);
        }

        /// <summary>Typed text may carry stray spaces from the keyboard, so it is trimmed.</summary>
        void OnTyped(string text)
        {
            text = (text ?? string.Empty).Trim();
            if (text.StartsWith("{", StringComparison.Ordinal))
            {
                OfferEntry(text);
                return;
            }
            try
            {
                var id = XrqrCode.Parse(text);
                m_Panel.ClearInput();
                Resolve(id);
            }
            catch (XrqrException e)
            {
                SetStatus($"Not an XRQR code ({e.Kind}): {e.Message}.", true);
            }
        }

        // ---- Scanning ----------------------------------------------------------------------

        void StartScanning()
        {
            CancelFlow();
            // The scanner keeps its last reading, so a code still in view is not looked up again
            // until its repeat delay has passed.
            m_Scanning = true;
            m_Panel.HideCard();
            m_Panel.SetActions();
            SetStatus(m_Placement != null && m_Placement.IsArmed
                ? "Scanning. Tap a surface to place the loaded model."
                : "Point the camera at an XRQR code.");
        }

        CancellationToken NewFlow()
        {
            CancelFlow();
            m_Flow = new CancellationTokenSource();
            m_Scanning = false;
            return m_Flow.Token;
        }

        /// <summary>Cancels the running flow. The source is not disposed, since its pending steps still read the token.</summary>
        void CancelFlow()
        {
            m_Flow?.Cancel();
            m_Flow = null;
        }

        // ---- Lookup ------------------------------------------------------------------------

        void Resolve(XrqrId id)
        {
            var cancellation = NewFlow();
            Run(() => ResolveAsync(id, cancellation), cancellation);
        }

        /// <summary>Runs one step of a flow from a UI or scan event, reporting its failure on the panel.</summary>
        async void Run(Func<Task> step, CancellationToken cancellation)
        {
            try
            {
                await step();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                Fail(e, cancellation);
            }
        }

        async Task ResolveAsync(XrqrId id, CancellationToken cancellation)
        {
            m_Placement?.Disarm();
            m_Panel.HideCard();
            m_Panel.SetActions(("Cancel", StartScanning));
            var entries = m_State.Entries;
            SetStatus($"Asking {entries.Count} trusted resolver(s) for {XrqrCode.Format(id)}.");
            var (candidates, failures) = await XrqrLookup.ResolveAsync(id, entries, cancellation);
            if (cancellation.IsCancellationRequested)
                return;
            foreach (var failure in failures)
                Debug.Log($"XRQR: {failure.Entry.Name} ({failure.Entry.Base}) gave no verified answer: {failure.Reason}");

            if (candidates.Count == 0)
            {
                var reasons = string.Join("\n", failures.Select(f => $"{f.Entry.Name}: {f.Reason}"));
                SetStatus($"No trusted resolver serves {XrqrCode.Format(id)}.\n{reasons}", true);
                m_Panel.SetActions(("Scan again", StartScanning));
                return;
            }
            if (candidates.Count == 1)
            {
                await Confirm(candidates[0], cancellation);
                return;
            }
            var remembered = candidates.Where(c => m_State.IsChosen(c.Entry, c.Meta.PublisherNumber)).ToList();
            if (remembered.Count == 1)
                await Confirm(remembered[0], cancellation);
            else
                Choose(candidates, cancellation);
        }

        /// <summary>Several resolvers gave verified answers: the user chooses, and the choice is remembered per resolver and publisher.</summary>
        void Choose(List<Candidate> candidates, CancellationToken cancellation)
        {
            SetStatus($"{candidates.Count} trusted resolvers answer for this code. Choose the one to use:");
            var actions = new List<(string, Action)>();
            foreach (var candidate in candidates)
            {
                var chosen = candidate;
                actions.Add(($"{chosen.Meta.Title}\n{chosen.Meta.PublisherName}, via {chosen.Entry.Name}", () =>
                {
                    m_State.RecordChoice(chosen.Entry, chosen.Meta.PublisherNumber);
                    Run(() => Confirm(chosen, cancellation), cancellation);
                }));
            }
            actions.Add(("Cancel", StartScanning));
            m_Panel.SetActions(actions.ToArray());
        }

        /// <summary>Shows what will be placed and from where, then loads it, unless the rollback guard objects.</summary>
        async Task Confirm(Candidate candidate, CancellationToken cancellation)
        {
            ShowCard(candidate);
            var highest = m_State.HighestVersion(candidate.Entry, candidate.Meta.Id);
            if (candidate.Meta.Version < highest)
            {
                SetStatus($"Version {candidate.Meta.Version} is older than version {highest}, seen before from {candidate.Entry.Name}. " +
                          "It may follow a takedown, or be a replayed answer.", true);
                m_Panel.SetActions(
                    ("Load anyway", () => Run(() => Load(candidate, cancellation), cancellation)),
                    ("Cancel", StartScanning));
                return;
            }
            await Load(candidate, cancellation);
        }

        async Task Load(Candidate candidate, CancellationToken cancellation)
        {
            var meta = candidate.Meta;
            var key = $"{candidate.Entry.Base} {meta.Id.Text} {meta.Version}";
            m_Panel.SetActions(("Cancel", StartScanning));
            if (!m_Models.TryGetValue(key, out var model))
            {
                SetStatus($"Downloading and verifying the model ({Megabytes(meta.Bytes)}).");
                var glb = await XrqrLookup.FetchModelAsync(candidate, cancellation);
                cancellation.ThrowIfCancellationRequested();
                SetStatus("Loading the model.");
                model = await ModelLoader.LoadAsync(glb, $"XRQR {meta.Id.Text} v{meta.Version}", m_Holder, cancellation);
                if (cancellation.IsCancellationRequested)
                {
                    model.Dispose();
                    return;
                }
                m_Models[key] = model;
            }
            m_State.RecordVersion(candidate.Entry, meta.Id, meta.Version);
            if (m_Placement == null)
            {
                SetStatus("Model loaded and verified; this scene has no placement.", true);
                m_Panel.SetActions(("Scan another code", StartScanning));
                return;
            }
            m_Placement.Arm(model, meta.Surface);
            SetStatus(meta.Surface == Surface.Floor ? "Verified. Tap a floor to place it."
                : meta.Surface == Surface.Wall ? "Verified. Tap a wall to place it."
                : "Verified. Tap a surface to place it.");
            m_Panel.SetActions(("Scan another code", StartScanning));
        }

        void Fail(Exception e, CancellationToken cancellation)
        {
            if (cancellation.IsCancellationRequested)
                return;
            if (e is ModelFetchException || e is ModelLoadException || e is XrqrException)
            {
                SetStatus(e.Message, true);
            }
            else
            {
                Debug.LogException(e, this);
                SetStatus("Unexpected error: " + e.Message, true);
            }
            m_Panel.SetActions(("Scan again", StartScanning));
        }

        // ---- Resolver entries ----------------------------------------------------------------

        void OfferEntry(string json)
        {
            NewFlow();
            ResolverEntry entry;
            try
            {
                entry = ResolverEntry.Parse(json);
            }
            catch (XrqrException e)
            {
                SetStatus($"Not a resolver entry ({e.Kind}): {e.Message}.", true);
                m_Panel.SetActions(("Scan again", StartScanning));
                return;
            }
            ShowThumbnail(null);
            var kids = string.Join(", ", entry.Keys.Select(k => k.KidText));
            m_Panel.ShowCard(null, entry.Name, $"{entry.Base}\nKeys: {kids}");
            SetStatus("Trust this resolver? Codes are looked up on it, and its keys verify what it serves.");
            m_Panel.SetActions(
                ("Trust", () =>
                {
                    var added = m_State.Import(entry);
                    m_Panel.ClearInput();
                    StartScanning();
                    SetStatus(added ? $"{entry.Name} is now trusted." : $"{entry.Name} is already trusted.");
                }),
                ("Cancel", StartScanning));
        }

        void ShowResolvers()
        {
            NewFlow();
            m_Panel.HideCard();
            var list = new StringBuilder("Trusted resolvers:");
            foreach (var entry in m_State.Main)
                list.Append($"\n{entry.Name}, {entry.Base} (shipped)");
            foreach (var entry in m_State.Imported)
                list.Append($"\n{entry.Name}, {entry.Base} (imported)");
            list.Append("\nTo add one, scan its entry QR code or paste its JSON under Type.");
            SetStatus(list.ToString());
            var actions = new List<(string, Action)>();
            foreach (var entry in m_State.Imported)
            {
                var removed = entry;
                actions.Add(($"Remove {removed.Name}", () =>
                {
                    m_State.Remove(removed);
                    ShowResolvers();
                }));
            }
            actions.Add(("Back", StartScanning));
            m_Panel.SetActions(actions.ToArray());
        }

        // ---- Display -----------------------------------------------------------------------

        void ShowCard(Candidate candidate)
        {
            var meta = candidate.Meta;
            ShowThumbnail(Thumbnail.Decode(meta.Thumbnail));
            var details = new StringBuilder();
            details.Append($"{meta.PublisherName} (publisher {meta.PublisherNumber})\n");
            details.Append($"via {candidate.Entry.Name}, {candidate.Entry.Base}\n");
            details.Append(string.Format(CultureInfo.InvariantCulture, "Version {0}, {1}, {2:0.00} x {3:0.00} x {4:0.00} m, {5}",
                meta.Version, Megabytes(meta.Bytes), meta.DimensionX, meta.DimensionY, meta.DimensionZ, meta.Surface.ToString().ToLowerInvariant()));
            if (meta.Link != null)
                details.Append('\n').Append(meta.Link);
            m_Panel.ShowCard(m_ShownThumbnail, meta.Title, details.ToString());
        }

        void ShowThumbnail(Texture2D texture)
        {
            if (m_ShownThumbnail != null)
                Destroy(m_ShownThumbnail);
            m_ShownThumbnail = texture;
        }

        void SetStatus(string text, bool warning = false)
        {
            m_Panel.SetStatus(text, warning);
            if (outputText != null)
                outputText.text = text;
            if (warning)
                Debug.LogWarning("XRQR: " + text);
        }

        static string Megabytes(ulong bytes) => string.Format(CultureInfo.InvariantCulture, "{0:0.0} MB", bytes / 1048576.0);
    }
}
